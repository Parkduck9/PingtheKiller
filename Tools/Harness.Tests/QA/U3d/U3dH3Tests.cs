using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

internal static class U3dH3Run
{
    public static async Task<(CheckResult Item, SurvivorTap Tap, LobbyServer Server)> RunAsync(string label, LobbyServer server,
        Func<LosTapAccessor, Func<SurvCtx, bool>?>? mutation = null, Func<int, TimeSpan>? delay = null, Func<SurvivorTap, U3dProxy.Rewrite>? custom = null)
    {
        await using (server)
        {
            var accessor = new LosTapAccessor();
            var tap = new SurvivorTap(mutation?.Invoke(accessor));
            accessor.Tap = tap;
            await using var proxy = new U3dProxy(server.Port, custom?.Invoke(tap) ?? tap.Rewrite, delay);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var report = await HarnessRunner.RunAsync(new Options { Port = proxy.Port, Timeout = TimeSpan.FromSeconds(2),
                Only = new HashSet<string> { "H3" } }, deadline.Token);
            var item = Assert.Single(report.Items);
            U3dEvidence.Write($"H3/{label}", $"{(item.Passed ? "PASS" : "FAIL")} applied={tap.Applied}/{tap.Seen} {item.Evidence}");
            return (item, tap, server);
        }
    }

    // H3 reads the survivor stream from its moving phase on, so a leak must repeat: every 7th tick from tick 30.
    public static Func<SurvCtx, bool> Sparse(Func<SurvCtx, bool> mutation) => c => c.Tick >= 30 && c.Tick % 7 == 0 && mutation(c);
}

// ---- H3 false passes -----------------------------------------------------------------------------------------------------------
public abstract class U3dH3Leak
{
    private readonly SightLayout layout;
    private readonly KillerLos entries;
    private readonly Func<Func<SurvCtx, bool>> mutation;
    private readonly string[] expected;

    private protected U3dH3Leak(SightLayout layout, KillerLos entries, Func<Func<SurvCtx, bool>> mutation, params string[] expected)
    {
        (this.layout, this.entries, this.mutation, this.expected) = (layout, entries, mutation, expected);
    }

    [Fact]
    public async Task H3FailsWithMatchingEvidence()
    {
        var (item, tap, _) = await U3dH3Run.RunAsync(GetType().Name, new LobbyServer { Layout = layout, KillerEntry = entries }, _ => U3dH3Run.Sparse(mutation()));
        Assert.True(tap.Applied >= 1, $"premise broken: mutation never applied ({tap.Seen} survivor worlds). H3: {item.Evidence}");
        Assert.False(item.Passed, $"FALSE PASS on gate H3 in {GetType().Name} (mutation applied {tap.Applied}x): {item.Evidence}");
        Assert.Equal(0, item.Score);
        foreach (string text in expected) Assert.Contains(text, item.Evidence);
    }
}

public sealed class U3dH3BlockedEntrySparseTests() : U3dH3Leak(SightLayout.Blocked, KillerLos.Contract,
    () => c => c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3 && U3dMut.Insert(c), "Killer 1 leaked to survivor 2", "is blocked");
public sealed class U3dH3OffsetSparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Edit(c, e => e with { X = e.X + 0.06f }), "Killer 1 leaked to survivor 2", "m from the killer's own position");
public sealed class U3dH3FlagsSparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Edit(c, e => e with { Flags = 1 }), "the contract requires all three 0");
public sealed class U3dH3StatusTimerSparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Edit(c, e => e with { StatusTimer = 0.1f }), "the contract requires all three 0");
public sealed class U3dH3CaptureCountSparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Edit(c, e => e with { CaptureCount = 2 }), "the contract requires all three 0");
public sealed class U3dH3SpectatorWithEntrySparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => { if (!U3dMut.LegalPresent(c)) return false; c.World.Players[c.SelfIndex] = c.Self with { Status = (byte)PlayerStatus.Eliminated }; return true; },
    "Killer 1 leaked to survivor 2", "spectator");
public sealed class U3dH3RoleOtherIdSparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Edit(c, e => e with { Id = 77 }), "entry Id=77 Role=1");
public sealed class U3dH3RightIdWrongRoleSparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Edit(c, e => e with { Role = (byte)Role.Survivor }), "only the real Id");
public sealed class U3dH3DuplicateEntrySparseTests() : U3dH3Leak(SightLayout.Clear, KillerLos.Contract,
    () => c => U3dMut.LegalPresent(c) && U3dMut.Insert(c, c.World.Players[c.EntryIndex]), "2 killer entries");

// A legal entry in every world must not hide a Silent survivor leak (also with the anonymous killer view).
public sealed class U3dH3LegalEntryBesideSilentLeakTests
{
    [Fact]
    public async Task IdModeFails()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("silent-leak-id-mode", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract, LeakSilent = true });
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("Silent survivor", item.Evidence);
    }

    [Fact]
    public async Task AnonymousViewFails()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("silent-leak-anonymous", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract, LeakSilent = true, AnonymousKillerView = true });
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("Silent survivor", item.Evidence);
    }

    [Fact]
    public async Task AnonymousViewWithoutLeakPassesWithLegalEntries()
    {
        var (item, tap, server) = await U3dH3Run.RunAsync("anonymous-contract", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract, AnonymousKillerView = true });
        Assert.True(item.Passed, item.Evidence);
        Assert.True(server.KillerEntriesSent > 0 && U3dCounts.Parse(item.Evidence).Visible > 0, item.Evidence);
    }

    [Fact]
    public async Task ARoundedSilentLeakBesideALegalEntryStillFails()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("silent-rounded-anonymous", new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract, LeakSilentRounded = true, AnonymousKillerView = true });
        Assert.False(item.Passed, item.Evidence);
    }
}

// ---- H3 false fails ------------------------------------------------------------------------------------------------------------
public sealed class U3dH3SkewTests
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
        var (item, _, server) = await U3dH3Run.RunAsync($"skew-{layout}-k{killerDelayMs}-s{survivorDelayMs}",
            new LobbyServer { Layout = layout, KillerEntry = KillerLos.Contract }, delay: conn => TimeSpan.FromMilliseconds(conn == 0 ? killerDelayMs : survivorDelayMs));
        Assert.True(item.Passed, item.Evidence);
        var c = U3dCounts.Parse(item.Evidence);
        if (layout == SightLayout.Clear) Assert.True(c.Visible > 0 && server.KillerEntriesSent > 0, item.Evidence);
        else Assert.True(c.Blocked > 0 && c.Visible == 0, item.Evidence);
        // Whatever fraction did not join (stream ends) is counted, never failed; a 200 ms skew is at most ~5 ticks at each end.
        Assert.True(c.Unknown <= 12, $"undecided={c.Unknown}: {item.Evidence}");
    }
}

public sealed class U3dH3KillerAtAWallCornerTests
{
    // A contract-correct server (generated by the proxy from the QA reference) with the killer standing exactly on / near a wall
    // corner. The segment from any survivor to a killer on the corner touches the wall, so the contract hides the Killer.
    internal static Func<SurvivorTap, U3dProxy.Rewrite> BrainFor(float killerX, float killerZ, bool lenient, bool always = false) => Brain(killerX, killerZ, lenient, always);

    private static Func<SurvivorTap, U3dProxy.Rewrite> Brain(float killerX, float killerZ, bool lenient, bool always = false) => tap => (conn, packet, raw) =>
    {
        if (packet is not S2C_WorldState world) { tap.Tap.Observe(conn, packet); return new[] { raw }; }
        if (conn == 0)
        {
            for (int i = 0; i < world.Players.Count; i++)
                if (world.Players[i].Id == 1 && world.Players[i].Role == (byte)Role.Killer) world.Players[i] = world.Players[i] with { X = killerX, Z = killerZ };
            tap.Tap.Observe(conn, world);
            return new[] { PacketCodec.Encode(world) };
        }
        tap.Tap.Observe(conn, world);
        world.Players.RemoveAll(p => p.Id == 1 || p.Role == (byte)Role.Killer);
        int recipient = tap.Tap.RecipientId(conn);
        var self = world.Players.Find(p => p.Id == recipient);
        double margin = RefLos.Margin(self.X, self.Z, killerX, killerZ, exactGrid: false);
        bool eligible = self.Status is 0 or 1;
        bool show = eligible && (always || (lenient ? margin >= -1e-9 : margin > 1e-9));
        if (show)
        {
            world.Players.Insert(0, new PlayerState { Id = 1, Role = (byte)Role.Killer, X = killerX, Z = killerZ });
        }
        return new[] { PacketCodec.Encode(world) };
    };

    [Theory]
    [InlineData(12f, 12f, false)]     // exactly on the corner, strict contract
    [InlineData(24f, 12f, false)]
    [InlineData(12f, 36f, false)]
    [InlineData(24f, 36f, false)]
    [InlineData(12f, 12f, true)]      // touching treated as clear by a lenient server: inside the band, not judged
    [InlineData(11.96875f, 11.96875f, false)] // 0.03 m outside the corner on both axes: clear for the server, ambiguous for the harness
    [InlineData(12.03125f, 12.03125f, false)] // 0.03 m inside the wall
    public async Task H3PassesAndCountsTheBandAsAmbiguous(float x, float z, bool lenient)
    {
        var (item, _, _) = await U3dH3Run.RunAsync($"corner-{x}-{z}-{(lenient ? "lenient" : "strict")}",
            new LobbyServer { Layout = SightLayout.Clear }, custom: tap => Brain(x, z, lenient)(tap));
        Assert.True(item.Passed, item.Evidence);
        var c = U3dCounts.Parse(item.Evidence);
        Assert.True(c.Unknown <= 5, item.Evidence);
        Assert.True(c.Ambiguous > 0 || c.Visible > 0 || c.Blocked > 0, item.Evidence);
    }

    [Fact]
    public async Task KillerExactlyOnTheCornerIsAllAmbiguousAndNeverVisible()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("corner-18-14-strict-counts",
            new LobbyServer { Layout = SightLayout.Clear }, custom: tap => Brain(12f, 12f, false)(tap));
        Assert.True(item.Passed, item.Evidence);
        var c = U3dCounts.Parse(item.Evidence);
        Assert.Equal(0, c.Visible);
        Assert.Equal(0, c.Blocked);
        Assert.True(c.Ambiguous > 50, item.Evidence);
    }
}

// ---- the ambiguous band as an attack surface (by design, spec 1) ----------------------------------------------------------------
// A leaking server (entry always shown, wall or not) with the killer 0.03 m INSIDE a wall corner: every survivor's segment ends
// inside the wall by at most 0.03 m, i.e. inside the 0.05 m band. The harness may not decide there (spec 1), so H3 passes. The
// contrast case puts the killer well inside the wall (1 m deep): the same always-show server fails at once.
public sealed class U3dH3AmbiguousBandIsNotJudgedTests
{
    [Fact]
    public async Task ByDesign_AlwaysShowingServerWithTheKillerInsideTheBandPassesH3()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("band-always-show-0.03-inside", new LobbyServer { Layout = SightLayout.Clear },
            custom: tap => U3dH3KillerAtAWallCornerTests.BrainFor(12.03125f, 12.03125f, lenient: false, always: true)(tap));
        Assert.True(item.Passed, item.Evidence);
        var c = U3dCounts.Parse(item.Evidence);
        Assert.True(c.Ambiguous > 50 && c.Visible == 0, item.Evidence);
    }

    [Fact]
    public async Task ContrastKillerOneMetreInsideTheWallFailsTheSameServer()
    {
        var (item, _, _) = await U3dH3Run.RunAsync("band-always-show-deep-inside", new LobbyServer { Layout = SightLayout.Clear },
            custom: tap => U3dH3KillerAtAWallCornerTests.BrainFor(19f, 20f, lenient: false, always: true)(tap));
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("is blocked", item.Evidence);
    }
}

// ---- the unknown-tick probe on the gate H3 (a2: flipped to must-fail after the fix for F1) -------------------------------------
// The survivor worlds of every 8th tick are renumbered, so the killer's own stream never carries those ticks. A through-the-wall
// killer entry is added to exactly those worlds. Round 1: H3 passed. Since attempt 2 it must fail.
public sealed class U3dH3UnjoinableTickLeakTests
{
    [Fact]
    public async Task BlockedLeakOnRenumberedTicksFailsH3()
    {
        var (item, tap, _) = await U3dH3Run.RunAsync("unknown-tick-blocked-leak", new LobbyServer { Layout = SightLayout.Blocked, KillerEntry = KillerLos.Contract },
            _ => c => { if (c.Tick >= 20 && c.Tick % 8 == 0 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3) { U3dMut.Insert(c); c.SentTick = c.Tick + 1_000_000; return true; } return false; });
        Assert.True(tap.Applied >= 5, $"premise: the through-the-wall killer entry was sent {tap.Applied}x");
        Assert.False(item.Passed, $"FALSE PASS on gate H3: {item.Evidence}");
        Assert.Equal(0, item.Score);
        Assert.Contains("never carried", item.Evidence);
    }

    // Control for the above: the same leak without renumbering is caught on the first such tick.
    [Fact]
    public async Task TheSameLeakOnJoinableTicksFailsH3()
    {
        var (item, tap, _) = await U3dH3Run.RunAsync("control-joinable-blocked-leak", new LobbyServer { Layout = SightLayout.Blocked, KillerEntry = KillerLos.Contract },
            _ => c => { if (c.Tick >= 20 && c.Tick % 8 == 0 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3) { U3dMut.Insert(c); return true; } return false; });
        Assert.True(tap.Applied >= 1);
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("is blocked", item.Evidence);
    }
}
