using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3c;

internal static class U3cH6
{
    public static async Task<CheckResult> RunAsync(int port, string label)
    {
        var (item, _) = await H6.RunAsync(port, 3);
        U3cRun.Dump(label, item);
        return item;
    }

    // Rewrites killer-bound (connection 0) WorldStates; everything else is forwarded untouched.
    public static QaProxy.Rewrite OnKillerWorld(Action<S2C_WorldState> change) => (conn, packet, raw) =>
    {
        if (conn != 0 || packet is not S2C_WorldState world) return new[] { raw };
        change(world);
        return new[] { PacketCodec.Encode(world) };
    };

    public static void ReplaceSurvivorIds(S2C_WorldState world, Func<int, int> next)
    {
        for (int i = 0; i < world.Players.Count; i++)
        {
            var p = world.Players[i];
            if (p.Role == (byte)Role.Killer) continue;
            p.Id = next(i);
            world.Players[i] = p;
        }
    }
}

// ---- H6 must never look a survivor up by Id in the killer stream ------------------------------------------------------
// A proxy in front of ScenarioServer rewrites every killer-bound survivor entry to an Id that is WRONG on purpose
// (random nonzero, including the killer's own Id 1 and the real survivor Ids 2 and 3). Any lookup by Id in the killer
// stream would then fail (or pass for the wrong reason).

public sealed class U3cH6RandomNonzeroKillerIdsAnonymousServerTests
{
    [Fact]
    public async Task H6PassesWhenEveryKillerViewSurvivorEntryCarriesARandomNonzeroId()
    {
        await using var server = new ScenarioServer { AnonymousKillerView = true, ExecutionGrace = true };
        var random = new Random(5);
        var ids = new[] { 1, 2, 3, 4, 777, 100000, 0 };
        await using var proxy = new QaProxy(server.Port, U3cH6.OnKillerWorld(w => U3cH6.ReplaceSurvivorIds(w, _ => ids[random.Next(ids.Length)])));
        var h6 = await U3cH6.RunAsync(proxy.Port, "anon/H6-random-killer-view-ids");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        Assert.True(server.AnonymousWorldsSent > 0);
    }
}

public sealed class U3cH6SwappedKillerIdsIdModeTests
{
    [Fact]
    public async Task H6PassesWhenTheKillerViewSwapsTheSurvivorIdsAndShowsRealProgress()
    {
        // Id mode (real progress percentages) with the two survivors' Ids swapped in the killer view.
        await using var server = new ScenarioServer { ExecutionGrace = false };
        await using var proxy = new QaProxy(server.Port, U3cH6.OnKillerWorld(w => U3cH6.ReplaceSurvivorIds(w, i => 5 - w.Players[i].Id)));
        var h6 = await U3cH6.RunAsync(proxy.Port, "id/H6-swapped-killer-view-ids");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
    }
}

// ---- Exit-open consistency inside the killer stream (quantised progress, N9) ---------------------------------------------

public sealed class U3cH6KillerStreamExitOpenWithoutTerminalsTests
{
    [Fact]
    public async Task H6FailsWhenTheKillerStreamShowsTheExitOpenWhileItsOwnQuantisedTerminalsShowFewerThanRequiredAt100()
    {
        await using var server = new ScenarioServer { AnonymousKillerView = true };
        await using var proxy = new QaProxy(server.Port, U3cH6.OnKillerWorld(w => { if (w.Tick >= 100) SetExitFlags(w, ExitFlags.Powered | ExitFlags.Open); }));
        var h6 = await U3cH6.RunAsync(proxy.Port, "anon/H6-killer-exit-open-without-terminals");
        Assert.False(h6.Passed, $"FALSE PASS: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Contains("in killer's WorldState", h6.Evidence);
        Assert.Contains("exit powered without terminals", h6.Evidence);
    }

    // v0.4: the exit flags of every exit in a killer-bound frame.
    internal static void SetExitFlags(PingTheKiller.Shared.Packets.S2C_WorldState w, ExitFlags flags)
    {
        for (int i = 0; i < w.Exits.Count; i++)
        {
            var exit = w.Exits[i];
            exit.Flags = (byte)flags;
            w.Exits[i] = exit;
        }
    }
}

public sealed class U3cH6KillerStreamNeverOpensTheExitTests
{
    [Fact]
    public async Task H6FailsWhenTheKillerStreamNeverShowsTheExitOpen()
    {
        await using var server = new ScenarioServer { AnonymousKillerView = true };
        await using var proxy = new QaProxy(server.Port, U3cH6.OnKillerWorld(w => U3cH6KillerStreamExitOpenWithoutTerminalsTests.SetExitFlags(w, 0)));
        var h6 = await U3cH6.RunAsync(proxy.Port, "anon/H6-killer-never-sees-exit-open");
        Assert.False(h6.Passed, $"FALSE PASS: {h6.Evidence}");
        Assert.Contains("both exits Powered after the required terminals", h6.Evidence);
    }
}

// Quantisation done "wrong but legal" for the exit rule: progress rounded to the nearest of 0/100 (50..99 -> 100). The exit
// is still consistent (>= required at 100 whenever open), so H6 passes.
public sealed class U3cH6RoundedQuantisationStillPassesTests
{
    [Fact]
    public async Task H6PassesWithKillerProgressRoundedToTheNearestOfZeroAndOneHundred()
    {
        await using var server = new ScenarioServer { ExecutionGrace = true };
        await using var proxy = new QaProxy(server.Port, U3cH6.OnKillerWorld(w =>
            w.Terminals = w.Terminals.Select(t => t with { Progress = (byte)(t.Progress >= 50 ? 100 : 0) }).ToList()));
        // Rounding up can show a terminal done before it is, which makes ExitOpen lag the killer's terminals, never lead them.
        var h6 = await U3cH6.RunAsync(proxy.Port, "id/H6-rounded-quantisation");
        Assert.True(h6.Passed, h6.Evidence);
    }
}

// ---- Unknown frames inside survivor-bound streams -------------------------------------------------------------------

public abstract class U3cH6UnknownFrameLeak(ushort id, int xOffset, int zOffset, bool expectPass, bool toKillerInstead = false)
{
    [Fact]
    public async Task H6OutcomeMatchesTheKillerCoordinatesInsideTheUnknownFrame()
    {
        await using var server = new ScenarioServer { AnonymousKillerView = true, ExecutionGrace = true };
        float kx = float.NaN, kz = float.NaN;
        await using var proxy = new QaProxy(server.Port, (conn, packet, raw) =>
        {
            if (packet is not S2C_WorldState world) return new[] { raw };
            if (conn == 0)
                foreach (var p in world.Players)
                    if (p.Role == (byte)Role.Killer) { kx = p.X; kz = p.Z; }
            bool target = toKillerInstead ? conn == 0 : conn != 0;
            if (!target || world.Tick % 20 != 0 || float.IsNaN(kx)) return new[] { raw };
            var payload = new byte[24];
            if (xOffset >= 0) BitConverter.TryWriteBytes(payload.AsSpan(xOffset), kx);
            if (zOffset >= 0) BitConverter.TryWriteBytes(payload.AsSpan(zOffset), kz);
            return new[] { raw, LobbyServer.UnknownFrame(id, payload.Length, 0x5A, payload) };
        });
        var h6 = await U3cH6.RunAsync(proxy.Port, $"anon/H6-unknown-0x{id:X2}-x@{xOffset}-z@{zOffset}{(toKillerInstead ? "-to-killer" : "")}");
        if (expectPass) Assert.True(h6.Passed, h6.Evidence);
        else
        {
            Assert.False(h6.Passed, $"FALSE PASS (killer X/Z inside unknown packet 0x{id:X2}): {h6.Evidence}");
            Assert.Equal(0, h6.Score);
            Assert.Contains($"killer coordinate bytes in unknown packet 0x{id:X2} (update Shared)", h6.Evidence);
        }
    }
}

public sealed class U3cH6UnknownFutureIdKillerXOddOffsetTests() : U3cH6UnknownFrameLeak(0x21, 3, -1, false);
public sealed class U3cH6UnknownIdKillerZOddOffsetTests() : U3cH6UnknownFrameLeak(0x7E, -1, 17, false);
public sealed class U3cH6UnknownKillerCoordinatesSentToTheKillerOnlyTests() : U3cH6UnknownFrameLeak(0x21, 0, 4, true, toKillerInstead: true);

public sealed class U3cH6AnonymousViewWithUnknownFramesTests
{
    [Fact]
    public async Task H6PassesWithTheAnonymousKillerViewAndUnknownFramesInEveryStream()
    {
        await using var server = new ScenarioServer { AnonymousKillerView = true, UnknownFrames = true, ExecutionGrace = true };
        var h6 = await U3cH6.RunAsync(server.Port, "anon/H6-unknown-frames");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Contains("skipped unknown packets: id=0x21 ×", h6.Evidence);
        Assert.Contains("id=0x7E ×", h6.Evidence);
    }
}
