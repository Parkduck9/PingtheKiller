using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

// QA round 2 (attempt 2: unjoinable-tick fix). Edge tolerance, ordering, skew.

// ---- SightJoin coverage rule, unit level (round 3; replaces the a2 edge-tolerance tests) ----------------------------------------
// U3d a3 contract clarification: a correct server sends every tick to every human, so an unjoined survivor world fails at ANY
// tick number, with or without a killer entry. The a2 tolerance tests (6 ticks, worst-case unbounded frames, entry-less unjoined
// world only counted) were re-classified by QA round 3 because the tolerance they pinned was removed on purpose (F6, F7).
public sealed class U3dCoverageRuleUnitTests
{
    private const int KillerId = 1, B = 3;
    private static readonly PlayerState Killer = new() { Id = KillerId, Role = (byte)Role.Killer, X = 5.25f, Z = 5.5f };
    private static readonly PlayerState Entry = Killer with { Flags = 0 };
    private static readonly PlayerState BehindWallB = new() { Id = B, X = 22.5f, Z = 20.25f };

    private static S2C_WorldState W(uint tick, params PlayerState[] players) => new() { Tick = tick, Players = players.ToList() };

    private static SightJoin JoinWithKillerTicks(uint from, uint to, params uint[] missing)
    {
        var join = new SightJoin(KillerId);
        for (uint t = from; t <= to; t++) if (!missing.Contains(t)) join.AddKiller(W(t, Killer));
        return join;
    }

    [Theory]
    [InlineData(50u)]  // a hole in the middle of the killer stream
    [InlineData(2u)]   // the second tick
    [InlineData(98u)]  // one before the last
    public void ATickMissingInsideTheRangeFails(uint missing)
    {
        var join = JoinWithKillerTicks(1, 99, missing);
        Assert.Null(join.AddSurvivor(B, W(missing, BehindWallB, Entry)));
        Assert.Contains("never delivered to the killer", Assert.Throws<CheckFailure>(() => join.Finish()).Message);
    }

    // Any tick number outside or inside the killer range, any distance from it: an unjoined survivor world fails. 94/206 were
    // "tolerated" by a2, 0 was the F6 exploit.
    [Theory]
    [InlineData(0u, true)] [InlineData(0u, false)]
    [InlineData(94u, true)] [InlineData(94u, false)]
    [InlineData(99u, true)] [InlineData(99u, false)]
    [InlineData(206u, true)] [InlineData(206u, false)]
    [InlineData(207u, true)] [InlineData(207u, false)]
    [InlineData(1_000_000u, true)] [InlineData(1_000_000u, false)]
    [InlineData(uint.MaxValue, true)] [InlineData(uint.MaxValue, false)]
    public void AnUnjoinedWorldFailsAtAnyTickWithOrWithoutAnEntry(uint tick, bool withEntry)
    {
        var join = JoinWithKillerTicks(100, 200);
        var world = withEntry ? W(tick, BehindWallB, Entry) : W(tick, BehindWallB);
        Assert.Null(join.AddSurvivor(B, world));
        var failure = Assert.Throws<CheckFailure>(() => join.Finish());
        Assert.Contains("never delivered to the killer", failure.Message);
        Assert.Contains($"tick {tick}", failure.Message);
    }

    [Fact]
    public void UnboundedFramesOnTickZeroAllFail()
    {
        var join = JoinWithKillerTicks(1, 500);
        for (int i = 0; i < 1000; i++) Assert.Null(join.AddSurvivor(B, W(0, BehindWallB, Entry)));
        Assert.Contains("never delivered to the killer", Assert.Throws<CheckFailure>(() => join.Finish()).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithNoKillerWorldAtAllEverySurvivorWorldFails(bool withEntry)
    {
        var join = new SightJoin(KillerId);
        Assert.Null(join.AddSurvivor(B, withEntry ? W(5, BehindWallB, Entry) : W(5, BehindWallB)));
        Assert.Contains("never delivered to the killer", Assert.Throws<CheckFailure>(() => join.Finish()).Message);
    }

    [Fact]
    public void AnEntryLessUnjoinedWorldAmongJoinedOnesFailsToo()
    {
        var join = JoinWithKillerTicks(1, 99, 50);
        for (uint t = 1; t <= 99; t++) if (t != 50) Assert.NotNull(join.AddSurvivor(B, W(t, BehindWallB)));
        Assert.Null(join.AddSurvivor(B, W(50, BehindWallB)));
        Assert.Contains("never delivered to the killer", Assert.Throws<CheckFailure>(() => join.Finish()).Message);
    }

    [Fact]
    public void ALateKillerTickJoinsAndFinishPasses()
    {
        var join = new SightJoin(KillerId);
        for (uint t = 1; t <= 20; t++) Assert.Null(join.AddSurvivor(B, W(t, BehindWallB)));
        Assert.True(join.AwaitingKiller);
        for (uint t = 1; t <= 20; t++) join.AddKiller(W(t, Killer));
        Assert.False(join.AwaitingKiller);
        join.Finish();
        Assert.Equal(20, join.Counts[SightOutcome.BlockedAbsent]);
    }

    [Fact]
    public void ASeededKillerTickFromBeforeTheWindowJoins()
    {
        var join = new SightJoin(KillerId);
        join.SeedKiller(W(7, Killer));
        Assert.NotNull(join.AddSurvivor(B, W(7, BehindWallB)));
        join.Finish();
    }

    // ---- per-stream tick integrity -----------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(new uint[] { 1, 2, 3, 4 }, true)]
    [InlineData(new uint[] { 1, 3, 5, 7 }, true)]          // gaps of exactly 2 are legal
    [InlineData(new uint[] { 1, 4 }, false)]               // gap 3
    [InlineData(new uint[] { 1, 2, 2 }, false)]            // duplicate
    [InlineData(new uint[] { 5, 4 }, false)]               // out of order
    [InlineData(new uint[] { 812, 0 }, false)]             // renumbered to Tick 0 (F6)
    [InlineData(new uint[] { 812, 813, 1_000_000 }, false)]
    [InlineData(new uint[] { 3, 2, 3, 4 }, false)]         // a late frame, even if the stream recovers
    public void TickSequenceRequiresRisingTicksWithGapsOfAtMostTwo(uint[] ticks, bool legal)
    {
        var sequence = new TickSequence("survivor 3");
        foreach (uint t in ticks) sequence.Add(t);
        if (legal) TickSequence.Require(sequence);
        else Assert.Contains("tick sequence violated in survivor 3 stream", Assert.Throws<CheckFailure>(() => TickSequence.Require(sequence)).Message);
    }

    [Fact]
    public void TheFirstViolationIsTheOneReported()
    {
        var sequence = new TickSequence("killer");
        foreach (uint t in new uint[] { 10, 11, 20, 21, 5 }) sequence.Add(t);
        Assert.Contains("11 -> 20", Assert.Throws<CheckFailure>(() => TickSequence.Require(sequence)).Message);
    }
}

// ---- edge-tick leak, integration ------------------------------------------------------------------------------------------------
// The killer stream's first tick is 1 (nothing before MatchStart), so a server that numbers every leaking survivor frame Tick 0 sits
// 1 tick below the range: within the 6-tick tolerance.
public sealed class U3dH6EdgeTickZeroLeakTests
{
    [Fact]
    public async Task BlockedLeakNumberedTickZeroOnEveryBlockedFrameOfB()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync("edge-tick-zero-blocked-leak",
            _ => c => { if (c.Conn == 2 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3) { U3dMut.Insert(c); c.SentTick = 0; return true; } return false; });
        Assert.True(tap.Applied > 50, $"premise: {tap.Applied}");
        U3dEvidence.Write("H6/edge-tick-zero-result", $"{(item.Passed ? "PASS" : "FAIL")}");
        Assert.False(item.Passed, $"FALSE PASS (finding F6): {tap.Applied} through-the-wall entries numbered Tick 0: {item.Evidence}");
    }
}

public sealed class U3dH3EdgeTickZeroLeakTests
{
    [Fact]
    public async Task BlockedLeakNumberedTickZeroOnSparseFrames()
    {
        var (item, tap, _) = await U3dH3Run.RunAsync("edge-tick-zero-blocked-leak", new LobbyServer { Layout = SightLayout.Blocked, KillerEntry = KillerLos.Contract },
            _ => c => { if (c.Tick >= 20 && c.Tick % 8 == 0 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3) { U3dMut.Insert(c); c.SentTick = 0; return true; } return false; });
        Assert.True(tap.Applied >= 5, $"premise: {tap.Applied}");
        U3dEvidence.Write("H3/edge-tick-zero-result", $"{(item.Passed ? "PASS" : "FAIL")}");
        Assert.False(item.Passed, $"FALSE PASS on gate H3 (finding F6): {tap.Applied} through-the-wall entries numbered Tick 0: {item.Evidence}");
    }
}

// ---- 250 to 350 ms skew -----------------------------------------------------------------------------------------------------------
// A correct server must pass at 200 ms on either stream (at 250 ms the pre-existing Silent-survivor check already flakes: 1 in 5 runs). From 300 ms on, H3 fails in checks that predate U3d and have nothing to do
// with line of sight ("Survivor remained Signal after zero input" when the survivor stream lags; the Silent-survivor byte check on
// frames the killer receives late when the killer stream lags). The control (no killer entries at all) fails identically.
public sealed class U3dH3SkewBeyond200Tests
{
    [Theory]
    [InlineData(false, 200, 0)]
    [InlineData(false, 0, 200)]
    [InlineData(false, 200, 200)]
    [InlineData(true, 200, 0)]
    [InlineData(true, 0, 200)]
    public async Task ContractServerPassesWhenOneStreamLags(bool blocked, int killerDelayMs, int survivorDelayMs)
    {
        var layout = blocked ? SightLayout.Blocked : SightLayout.Clear;
        var (item, _, _) = await U3dH3Run.RunAsync($"skew2-{layout}-k{killerDelayMs}-s{survivorDelayMs}",
            new LobbyServer { Layout = layout, KillerEntry = KillerLos.Contract }, delay: conn => TimeSpan.FromMilliseconds(conn == 0 ? killerDelayMs : survivorDelayMs));
        Assert.True(item.Passed, $"FALSE FAIL (contract-correct server, killer +{killerDelayMs} ms, survivor +{survivorDelayMs} ms): {item.Evidence}");
    }

    // Round 3: the a2 version of this test accepted a failure from the old timing checks or the edge tolerance (F8). Since a3 H3
    // waits for coverage by tick (brief item 5), a contract-correct server with one stream 300 or 350 ms late must PASS outright.
    [Theory]
    [InlineData(300, 0)]
    [InlineData(350, 0)]
    [InlineData(0, 300)]
    [InlineData(0, 350)]
    public async Task ContractServerPassesFromThreeHundredMs(int killerDelayMs, int survivorDelayMs)
    {
        Func<int, TimeSpan> delay = conn => TimeSpan.FromMilliseconds(conn == 0 ? killerDelayMs : survivorDelayMs);
        var (los, _, _) = await U3dH3Run.RunAsync($"skew3-los-k{killerDelayMs}-s{survivorDelayMs}", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract }, delay: delay);
        Assert.True(los.Passed, $"FALSE FAIL (contract-correct server, killer +{killerDelayMs} ms, survivor +{survivorDelayMs} ms): {los.Evidence}");
    }
}

public sealed class U3dH6SurvivorSkewBeyond200Tests
{
    [Theory]
    [InlineData(250)]
    [InlineData(300)]
    public async Task BothSurvivorStreamsLate(int ms)
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync($"skew2-survivors-{ms}ms", expectLos: true, delay: conn => conn == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms));
        Assert.True(item.Passed, $"FALSE FAIL (survivors +{ms} ms): {item.Evidence}");
    }

    [Fact]
    public async Task OneSurvivorLate300()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("skew2-B-300ms", expectLos: true, delay: conn => conn == 2 ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero);
        Assert.True(item.Passed, $"FALSE FAIL (B +300 ms): {item.Evidence}");
    }
}

// Same edge-tick idea in H6 with coordinates that are not bit-equal to any killer position: the byte scan has nothing to match.
public sealed class U3dH6EdgeTickZeroPerturbedLeakTests
{
    [Fact]
    public async Task BlockedLeakNumberedTickZeroWithCoordinatesOff1cm()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync("edge-tick-zero-perturbed-leak",
            _ => c => { if (c.Conn == 2 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3)
                { U3dMut.Insert(c, c.LegalEntry with { X = c.Truth.X + 0.013f, Z = c.Truth.Z - 0.017f }); c.SentTick = 0; return true; } return false; });
        Assert.True(tap.Applied > 50, $"premise: {tap.Applied}");
        Assert.False(item.Passed, $"FALSE PASS (finding F6): {tap.Applied} through-the-wall entries numbered Tick 0, X/Z off by about 2 cm: {item.Evidence}");
    }
}

// ---- ordering of the final check against the leak scan ------------------------------------------------------------------------------
// A contract-correct server whose KILLER stream starts 3 ticks after the survivors' (the killer's first 3 WorldStates are dropped):
// A sees the killer from tick 1, so survivor frames at ticks 1..3 carry a legal entry on ticks the killer stream never carried, 3 ticks
// below its range, inside the 6-tick edge tolerance. Finish() counts them as undecided, but they get no scan allowance, and the
// stationary killer's coordinates are in the killer-bit set, so the byte scan rejects the legal entry's own X/Z.
// Re-classified by QA round 3 (was ContractServerWithTheKillerStreamStarting3TicksLateMustPass, a2 finding F7). Under the
// clarified contract a correct server sends every tick to every human; a killer stream that never carries ticks 1..3 while the
// survivors receive them is a server defect, so H6 must FAIL and name the tick.
public sealed class U3dH6KillerStreamStartsLateTests
{
    [Fact]
    public async Task ServerWithholdingTheFirst3TicksFromTheKillerFailsH6()
    {
        await using var server = new ScenarioServer { LineOfSight = true, ExecutionGrace = true };
        var tap = new SurvivorTap();
        int withheld = 0;
        await using var proxy = new U3dProxy(server.Port, (conn, packet, raw) =>
        {
            if (conn == 0 && packet is S2C_WorldState w && w.Tick <= 3) { Interlocked.Increment(ref withheld); tap.Tap.Observe(conn, packet); return Array.Empty<byte[]>(); }
            return tap.Rewrite(conn, packet, raw);
        });
        var (item, _) = await H6.RunAsync(proxy.Port, expectLos: true);
        U3dEvidence.Write("H6/killer-stream-starts-3-ticks-late", $"{(item.Passed ? "PASS" : "FAIL")} {item.Evidence}");
        Assert.True(withheld >= 1, "premise: at least one killer tick was withheld");
        Assert.False(item.Passed, $"FALSE PASS (killer never received ticks the survivors did): {item.Evidence}");
        Assert.Contains("never delivered to the killer", item.Evidence);
    }
}
