using System.Diagnostics;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

// QA round 3 (attempt 3: tick coverage, no tolerance). Holes in the join: duplicate, out-of-order, first and last frames,
// a killer stream that stops or has a hole mid-window, big skew.

internal static class U3dR3
{
    public static async Task<(CheckResult Item, SurvivorTap Tap, TimeSpan Elapsed, int Premise)> H6Async(string label,
        Func<SurvivorTap, (U3dProxy.Rewrite Rewrite, Func<int> Premise)> custom, Func<SurvCtx, bool>? mutate = null,
        Func<int, TimeSpan>? delay = null, bool expectLos = false)
    {
        await using var server = new ScenarioServer { LineOfSight = true, ExecutionGrace = true };
        var tap = new SurvivorTap(mutate);
        var (rewrite, premise) = custom(tap);
        await using var proxy = new U3dProxy(server.Port, rewrite, delay);
        var (item, elapsed) = await H6.RunAsync(proxy.Port, expectLos: expectLos);
        U3dEvidence.Write($"H6/{label}", $"{(item.Passed ? "PASS" : "FAIL")} applied={tap.Applied}/{tap.Seen} premise={premise()} {elapsed.TotalSeconds:F0}s {item.Evidence}");
        return (item, tap, elapsed, premise());
    }

    // A blocked survivor-bound world with the killer entry inserted at the real position: a leak under the contract.
    public static bool BlockedLeak(SurvCtx c) => c.Conn == 2 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3 && U3dMut.Insert(c);
}

// ---- duplicate ticks: a leak cannot hide as the second frame of a tick ---------------------------------------------------------------
public sealed class U3dH6DuplicateTickLeakTests
{
    // For every blocked frame of B the server sends the clean frame and then a second frame with the same Tick that carries the
    // through-the-wall entry (real position). Each stream alone looks fine except for the duplicate tick.
    [Theory]
    [InlineData(false)]  // clean first, leaking duplicate second
    [InlineData(true)]   // leaking first, clean duplicate second
    public async Task LeakingTwinOfACleanFrameFails(bool leakFirst)
    {
        var (item, tap, _, _) = await U3dR3.H6Async($"dup-tick-leak-{(leakFirst ? "first" : "second")}", tap => (tap.Rewrite, () => tap.Applied),
            c =>
            {
                if (c.Conn != 2 || c.EntryIndex >= 0 || !c.Eligible || c.Margin >= -0.3) return false;
                byte[] clean = PacketCodec.Encode(c.World);
                U3dMut.Insert(c);
                if (leakFirst) c.After.Add(clean);   // the sent frame is the leaky one, the clean twin follows
                else
                {
                    c.After.Add(PacketCodec.Encode(c.World));   // the leaky twin follows the clean sent frame
                    c.World.Players.RemoveAt(0);
                }
                return true;
            });
        Assert.True(tap.Applied >= 1, $"premise: {tap.Applied}");
        Assert.False(item.Passed, $"FALSE PASS (duplicate tick leak): {item.Evidence}");
        Assert.Equal(0, item.Score);
    }

    // No leak at all: a correct-looking duplicate frame alone is a tick-sequence violation (H2 already requires strictly rising ticks).
    [Fact]
    public async Task ACleanDuplicateFrameAloneFails()
    {
        var (item, tap, _, _) = await U3dR3.H6Async("dup-tick-clean", tap => (tap.Rewrite, () => tap.Applied),
            c =>
            {
                if (c.Conn != 1 || c.Tick % 40 != 0) return false;
                c.After.Add(PacketCodec.Encode(c.World));
                return true;
            });
        Assert.True(tap.Applied >= 2, $"premise: {tap.Applied}");
        Assert.False(item.Passed, $"FALSE PASS (duplicate tick): {item.Evidence}");
        Assert.Contains("tick sequence violated in", item.Evidence);
    }
}

// ---- out-of-order ------------------------------------------------------------------------------------------------------------------
public sealed class U3dH6OutOfOrderTests
{
    // Every 40th survivor-1 frame is held back and sent after the next one.
    private static (U3dProxy.Rewrite, Func<int>) Swap(SurvivorTap tap, int connection)
    {
        List<byte[]>? held = null;
        int swaps = 0;
        U3dProxy.Rewrite rewrite = (conn, packet, raw) =>
        {
            var frames = tap.Rewrite(conn, packet, raw).ToList();
            if (conn != connection || packet is not S2C_WorldState world) return frames;
            if (held != null) { var both = frames.Concat(held).ToList(); held = null; Interlocked.Increment(ref swaps); return both; }
            if (world.Tick % 40 == 0) { held = frames; return Array.Empty<byte[]>(); }
            return frames;
        };
        return (rewrite, () => Volatile.Read(ref swaps));
    }

    [Fact]
    public async Task TwoSwappedSurvivorFramesFail()
    {
        var (item, _, _, swaps) = await U3dR3.H6Async("out-of-order-clean", tap => Swap(tap, 1));
        Assert.True(swaps >= 2, $"premise: {swaps}");
        Assert.False(item.Passed, $"FALSE PASS (out-of-order frames): {item.Evidence}");
        Assert.Contains("tick sequence violated in", item.Evidence);
    }

    [Fact]
    public async Task SwappedFramesDoNotLetALeakThrough()
    {
        // B's frames are swapped every 40th tick AND every blocked frame of B carries the entry.
        var (item, tap, _, swaps) = await U3dR3.H6Async("out-of-order-leak", tap => Swap(tap, 2), c => c.Tick >= 40 && U3dR3.BlockedLeak(c));
        Assert.True(swaps >= 1 && tap.Applied >= 1, $"premise: swaps={swaps} applied={tap.Applied}");
        Assert.False(item.Passed, $"FALSE PASS: {item.Evidence}");
        Assert.Equal(0, item.Score);
    }
}

// ---- first and last frames ------------------------------------------------------------------------------------------------------
public sealed class U3dH6FirstAndLastFrameTests
{
    // The first WorldState after MatchStart is at the start of every stream; an entry with the wrong position on tick 1..3 is judged
    // like any other (H6 covers every stream from the first WorldState).
    [Fact]
    public async Task AnOffsetEntryOnTheVeryFirstFramesIsCaught()
    {
        var (item, tap, _, _) = await U3dR3.H6Async("first-frames-offset-entry", tap => (tap.Rewrite, () => tap.Applied),
            c =>
            {
                if (c.Conn == 0 || c.Tick > 3 || c.EntryIndex < 0) return false;
                c.World.Players[c.EntryIndex] = c.World.Players[c.EntryIndex] with { X = c.Truth.X + 0.5f };
                return true;
            });
        Assert.True(tap.Applied >= 1, $"premise: {tap.Applied}");
        Assert.False(item.Passed, $"FALSE PASS: {item.Evidence}");
        Assert.Contains("Killer 1 leaked", item.Evidence);
    }

    // After the GameResult of conn 1 the server sends one more WorldState (Tick+1, or a rewound/duplicate tick) carrying a killer
    // entry at the last known killer position. The killer never gets it (or the tick is not new), so it must fail and must not hang.
    [Theory]
    [InlineData("next-tick")]
    [InlineData("same-tick")]
    [InlineData("rewound-to-1")]
    public async Task AWorldStateWithAKillerEntryAfterTheGameResultFails(string mode)
    {
        int sent = 0;
        var (item, _, elapsed, premise) = await U3dR3.H6Async($"after-gameresult-{mode}", tap =>
        {
            byte[]? lastWorld = null;
            U3dProxy.Rewrite rewrite = (conn, packet, raw) =>
            {
                var frames = tap.Rewrite(conn, packet, raw).ToList();
                if (conn != 1) return frames;
                if (packet is S2C_WorldState) lastWorld = raw;
                if (packet is S2C_GameResult && lastWorld != null && Interlocked.Increment(ref sent) == 1)
                {
                    var extra = (S2C_WorldState)PacketCodec.Decode((ushort)PacketId.S2C_WorldState, lastWorld.AsSpan(6));
                    uint last = extra.Tick;
                    var truth = tap.Tap.KillerAt(last) ?? tap.Tap.KillerAt(last - 1) ?? default;
                    extra.Tick = mode switch { "next-tick" => last + 1, "same-tick" => last, _ => 1 };
                    extra.Players.RemoveAll(p => p.Id == 1 || p.Role == (byte)Role.Killer);
                    extra.Players.Insert(0, new PlayerState { Id = 1, Role = (byte)Role.Killer, X = truth.X, Z = truth.Z, Yaw = truth.Yaw, Status = truth.Status });
                    frames.Add(PacketCodec.Encode(extra));
                }
                return frames;
            };
            return (rewrite, () => Volatile.Read(ref sent));
        });
        Assert.True(premise >= 1, "premise: a GameResult passed conn 1");
        Assert.False(item.Passed, $"FALSE PASS (killer entry after the GameResult, {mode}): {item.Evidence}");
        Assert.True(elapsed < TimeSpan.FromSeconds(250), $"took {elapsed}");
    }
}

// ---- a killer stream that stops or has a hole ---------------------------------------------------------------------------------------
public sealed class U3dH6KillerStreamGapTests
{
    private static (U3dProxy.Rewrite, Func<int>) KillerDrops(SurvivorTap tap, Func<uint, bool> drop)
    {
        int dropped = 0;
        U3dProxy.Rewrite rewrite = (conn, packet, raw) =>
        {
            if (conn == 0 && packet is S2C_WorldState w && drop(w.Tick)) { Interlocked.Increment(ref dropped); tap.Tap.Observe(conn, packet); return Array.Empty<byte[]>(); }
            return tap.Rewrite(conn, packet, raw);
        };
        return (rewrite, () => Volatile.Read(ref dropped));
    }

    // The killer stream simply stops at tick 150 (the server keeps sending to the survivors): the coverage wait is bounded by
    // --timeout and the check fails; it must neither hang nor pass.
    [Fact]
    public async Task KillerStreamStoppingMidWindowFailsWithoutHanging()
    {
        var (item, _, elapsed, dropped) = await U3dR3.H6Async("killer-stops-at-150", tap => KillerDrops(tap, t => t > 150));
        Assert.True(dropped >= 10, $"premise: {dropped}");
        Assert.False(item.Passed, $"FALSE PASS (killer stream stopped at tick 150): {item.Evidence}");
        Assert.True(elapsed < TimeSpan.FromSeconds(250), $"took {elapsed}");
    }

    // The killer stream stops 15 ticks before the end (the match is about 1125 ticks long): the survivors' last ticks were never
    // delivered to the killer. The coverage wait is bounded and the check fails.
    [Fact]
    public async Task KillerStreamStoppingJustBeforeTheEndFails()
    {
        var (item, _, elapsed, dropped) = await U3dR3.H6Async("killer-stops-at-end", tap => KillerDrops(tap, t => t > 1100));
        Assert.True(dropped >= 5, $"premise: {dropped}");
        Assert.False(item.Passed, $"FALSE PASS (killer stream stopped 25 ticks before the end): {item.Evidence}");
        Assert.True(elapsed < TimeSpan.FromSeconds(250), $"took {elapsed}");
    }

    // A hole of 5 killer ticks in the middle, then the stream resumes: the survivors received those ticks.
    [Fact]
    public async Task AHoleOfFiveKillerTicksFails()
    {
        var (item, _, _, dropped) = await U3dR3.H6Async("killer-hole-5", tap => KillerDrops(tap, t => t is >= 100 and < 105));
        Assert.True(dropped >= 5, $"premise: {dropped}");
        Assert.False(item.Passed, $"FALSE PASS (killer stream lost ticks 100..104): {item.Evidence}");
    }

    // A hole of exactly 1 killer tick (a gap of 2 is legal for the killer stream on its own) still leaves a survivor tick the killer
    // never received.
    [Fact]
    public async Task AHoleOfOneKillerTickFailsBecauseTheSurvivorsGotIt()
    {
        var (item, _, _, dropped) = await U3dR3.H6Async("killer-hole-1", tap => KillerDrops(tap, t => t == 100));
        Assert.True(dropped >= 1, $"premise: {dropped}");
        Assert.False(item.Passed, $"FALSE PASS: {item.Evidence}");
        Assert.Contains("never delivered to the killer", item.Evidence);
    }

    // The killer stream carries a duplicate of tick 100 (the second copy at another position).
    [Fact]
    public async Task ADuplicateKillerTickFails()
    {
        int dup = 0;
        var (item, _, _, _) = await U3dR3.H6Async("killer-dup-tick", tap =>
        {
            U3dProxy.Rewrite rewrite = (conn, packet, raw) =>
            {
                var frames = tap.Rewrite(conn, packet, raw).ToList();
                if (conn == 0 && packet is S2C_WorldState w && w.Tick == 100) { Interlocked.Increment(ref dup); frames.Add(raw); }
                return frames;
            };
            return (rewrite, () => Volatile.Read(ref dup));
        });
        Assert.True(dup >= 1);
        Assert.False(item.Passed, $"FALSE PASS: {item.Evidence}");
        Assert.Contains("tick sequence violated in", item.Evidence);
    }
}

public sealed class U3dH3KillerStreamGapTests
{
    private static Func<SurvivorTap, U3dProxy.Rewrite> KillerDrops(Func<uint, bool> drop) => tap => (conn, packet, raw) =>
    {
        if (conn == 0 && packet is S2C_WorldState w && drop(w.Tick)) { tap.Tap.Observe(conn, packet); return Array.Empty<byte[]>(); }
        return tap.Rewrite(conn, packet, raw);
    };

    [Theory]
    [InlineData(60)]
    [InlineData(75)]
    public async Task KillerStreamStoppingMidCaptureFailsH3(uint stopAfter)
    {
        var sw = Stopwatch.StartNew();
        var (item, _, _) = await U3dH3Run.RunAsync($"killer-stops-at-{stopAfter}", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract },
            custom: KillerDrops(t => t > stopAfter));
        Assert.False(item.Passed, $"FALSE PASS (killer stream stopped at tick {stopAfter}): {item.Evidence}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task AHoleOfOneKillerTickFailsH3()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("killer-hole-1", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract },
            custom: KillerDrops(t => t >= 70 && t % 5 == 0));
        Assert.False(item.Passed, $"FALSE PASS (killer stream lost ticks the survivor received): {item.Evidence}");
    }

    // A survivor leak whose Tick is repeated (the same Tick twice in the survivor stream) is caught in H3 as well.
    [Fact]
    public async Task DuplicateSurvivorTickCarryingALeakFailsH3()
    {
        var (item, tap, _) = await U3dH3Run.RunAsync("dup-tick-leak", new LobbyServer { Layout = SightLayout.Blocked, KillerEntry = KillerLos.Contract },
            _ => c =>
            {
                if (c.Tick < 20 || c.Tick % 5 != 0 || c.EntryIndex >= 0 || !c.Eligible || c.Margin >= -0.3) return false;
                U3dMut.Insert(c);
                byte[] leaky = PacketCodec.Encode(c.World);
                c.World.Players.RemoveAt(0);
                c.After.Add(leaky);
                return true;
            });
        Assert.True(tap.Applied >= 5, $"premise: {tap.Applied}");
        Assert.False(item.Passed, $"FALSE PASS (duplicate tick leak in H3): {item.Evidence}");
    }
}

// ---- skew: a correct server must pass with a stream 1 s late (retention, coverage wait) ----------------------------------------------------
public sealed class U3dH3Skew1000Tests
{
    [Theory]
    [InlineData(1000, 0, false)]
    [InlineData(0, 1000, false)]
    [InlineData(1000, 0, true)]
    [InlineData(0, 1000, true)]
    public async Task ContractServerPassesWithOneStreamAFullSecondLate(int killerDelayMs, int survivorDelayMs, bool blocked)
    {
        var layout = blocked ? SightLayout.Blocked : SightLayout.Clear;
        var (item, _, _) = await U3dH3Run.RunAsync($"skew1000-{layout}-k{killerDelayMs}-s{survivorDelayMs}",
            new LobbyServer { Layout = layout, KillerEntry = KillerLos.Contract }, delay: conn => TimeSpan.FromMilliseconds(conn == 0 ? killerDelayMs : survivorDelayMs));
        Assert.True(item.Passed, $"FALSE FAIL (contract-correct server, killer +{killerDelayMs} ms, survivor +{survivorDelayMs} ms): {item.Evidence}");
    }
}

public sealed class U3dH6Skew500Tests
{
    [Theory]
    [InlineData(0, 500)]  // killer +0.5 s
    [InlineData(1, 500)]  // A +0.5 s
    public async Task ContractServerPassesWithOneStreamHalfASecondLate(int lateConnection, int ms)
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync($"skew-conn{lateConnection}-{ms}ms", expectLos: true,
            delay: conn => conn == lateConnection ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero);
        Assert.True(item.Passed, $"FALSE FAIL (stream {lateConnection} +{ms} ms): {item.Evidence}");
    }

    // Proportional waypoint steering tolerates B's 500 ms stream delay, with no line-of-sight entries required.
    [Fact]
    public async Task AControlServerWithoutLineOfSightEntriesToleratesHalfASecondOnBsStream()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("control-no-los-B-500ms", losServer: false,
            delay: conn => conn == 2 ? TimeSpan.FromMilliseconds(500) : TimeSpan.Zero);
        Assert.True(item.Passed,item.Evidence);
        Assert.Contains("A Normal with CaptureCount=1",item.Evidence);
    }
    // LIMIT: a 1 s stream lag remains outside the movement/rescue pacing budget.
    [Fact]
    public async Task Limit_AControlServerWithoutLineOfSightEntriesFailsToAtOneSecondOnBsStream()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("control-no-los-B-1000ms", losServer: false,
            delay: conn => conn == 2 ? TimeSpan.FromMilliseconds(1000) : TimeSpan.Zero);
        Assert.False(item.Passed, "Behaviour changed: H6 now tolerates a 1 s lag on B, so the 1 s LOS tests can be added. " + item.Evidence);
        // GDD v2 (U6a): the 15 s switch hold is a new step where a lagging walker overshoots the 1.5 m switch radius.
        Assert.True(item.Evidence.Contains("stuck on leg") || (item.Evidence.Contains("[rescue]") && item.Evidence.Contains("not observed")) ||
            (item.Evidence.Contains("[switch") && item.Evidence.Contains("not observed")), item.Evidence);
    }
}
