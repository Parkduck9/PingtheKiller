using System.Text.RegularExpressions;
using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

internal static class SightEvidence
{
    // "<n> legal line-of-sight killer entries, <m> blocked-and-absent, ..." (SightCounts.ToString).
    public static (int Visible, int Blocked) Counts(string evidence)
    {
        var match = Regex.Match(evidence, @"(\d+) legal line-of-sight killer entries, (\d+) blocked-and-absent");
        Assert.True(match.Success, evidence);
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }
}

// U3d H6: the contract-correct line-of-sight fake passes with --expect-los, in the plain and in the anonymous/unknown-frame
// views. One class each so xUnit runs the ~60 s scenarios in parallel.
public abstract class ScenarioLineOfSightPassTest(bool anonymous, bool unknown)
{
    [Fact]
    public async Task H6PassesWithLegalLineOfSightEntriesAndExpectLos()
    {
        await using var server = new ScenarioServer { LineOfSight = true, ExecutionGrace = true, AnonymousKillerView = anonymous, UnknownFrames = unknown };
        var (h6, _) = await H6.RunAsync(server.Port, expectLos: true);
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        Assert.Contains("sight: B stands behind a wall", h6.Evidence);
        Assert.Contains("--expect-los: ", h6.Evidence);
        var (visible, blocked) = SightEvidence.Counts(h6.Evidence);
        Assert.True(visible > 0 && blocked > 0, h6.Evidence);
        Assert.True(server.KillerEntriesSent > 0 && server.BlockedWorldsSent > 0);
        // Entries in the 0.05 m band are counted as ambiguous, not visible, so only the upper bound is exact.
        Assert.True(visible <= server.KillerEntriesSent, $"{visible} > {server.KillerEntriesSent}");
        Assert.Equal(0, server.Executions);
    }
}

public sealed class ScenarioLineOfSightPassTests() : ScenarioLineOfSightPassTest(false, false);
public sealed class ScenarioLineOfSightAnonymousUnknownFramesPassTests() : ScenarioLineOfSightPassTest(true, true);

public sealed class ScenarioExpectLosWithoutServerSupportTests
{
    // Today's server (no line-of-sight entries) with --expect-los: every step passes, then the sight requirement fails.
    [Fact]
    public async Task H6FailsTheExpectLosRequirementWhenNoEntryIsEverShown()
    {
        await using var server = new ScenarioServer { ExecutionGrace = true };
        var (h6, _) = await H6.RunAsync(server.Port, expectLos: true);
        Assert.False(h6.Passed, h6.Evidence);
        Assert.Contains("[line of sight (--expect-los)]", h6.Evidence);
        Assert.Contains("0 legal line-of-sight killer entries", h6.Evidence);
        Assert.Equal(0, SightEvidence.Counts(h6.Evidence).Visible);
        Assert.Equal(1, server.MatchStarts);
    }
}

// U3d broken line-of-sight fakes: each must fail H6 even without --expect-los.
public sealed class H6LosWhileBlockedTests() : H6BrokenFakeTest(H6Fault.LosWhileBlocked, "Killer 1 leaked to survivor", "is blocked");
public sealed class H6LosToSpectatorTests() : H6BrokenFakeTest(H6Fault.LosToSpectator, "Killer 1 leaked to survivor", "spectator");
public sealed class H6LosCrossRecipientTests() : H6BrokenFakeTest(H6Fault.LosCrossRecipient, "Killer 1 leaked to survivor 3", "is blocked");
public sealed class H6LosStalePositionTests() : H6BrokenFakeTest(H6Fault.LosStalePosition, "Killer 1 leaked to survivor", "(stale)");
public sealed class H6LosNonZeroFieldsTests() : H6BrokenFakeTest(H6Fault.LosNonZeroFields, "Killer 1 leaked to survivor", "the contract requires all three 0");
// U3d a3: the entry on ticks the killer's own stream never carried (QA F1; no tolerance since a3).
public sealed class H6LosUnjoinableTickTests() : H6BrokenFakeTest(H6Fault.LosUnjoinableTick, "[killer-coordinate leak scan]",
    "never delivered to the killer", "the killer's own stream never carried it");
public sealed class H6LosSkillEffectTests() : H6BrokenFakeTest(H6Fault.LosSkillEffect, "killer coordinate", "bytes leaked", "S2C_SkillEffect");

// U3d H3: the same legality rule on the sandbox match (LobbyServer with on-map positions).
public sealed class H3LineOfSightTests
{
    private static async Task<CheckResult> H3Async(LobbyServer server)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3" } }, deadline.Token);
        return Assert.Single(report.Items);
    }

    [Fact]
    public async Task ContractEntryWithClearSightPassesAndIsCounted()
    {
        await using var server = new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract };
        var h3 = await H3Async(server);
        Assert.True(h3.Passed, h3.Evidence);
        Assert.True(SightEvidence.Counts(h3.Evidence).Visible > 0, h3.Evidence);
        Assert.Contains("0 to stopped survivor 2", h3.Evidence);
        Assert.True(server.KillerEntriesSent > 0);
    }

    [Fact]
    public async Task ContractServerBehindAWallSendsNothingAndPasses()
    {
        await using var server = new LobbyServer { Layout = SightLayout.Blocked, KillerEntry = KillerLos.Contract };
        var h3 = await H3Async(server);
        Assert.True(h3.Passed, h3.Evidence);
        var (visible, blocked) = SightEvidence.Counts(h3.Evidence);
        Assert.Equal(0, visible);
        Assert.True(blocked > 0, h3.Evidence);
        Assert.Equal(0, server.KillerEntriesSent);
    }

    [Fact]
    public async Task NonZeroFieldsWithClearSightFail()
    {
        await using var server = new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.NonZeroFields };
        var h3 = await H3Async(server);
        Assert.False(h3.Passed);
        Assert.Contains("Killer 1 leaked to survivor 2", h3.Evidence);
        Assert.Contains("the contract requires all three 0", h3.Evidence);
    }

    [Fact]
    public async Task TodaysServerShowsZeroVisibleEntries()
    {
        await using var server = new LobbyServer { Layout = SightLayout.Clear };
        var h3 = await H3Async(server);
        Assert.True(h3.Passed, h3.Evidence);
        Assert.Equal(0, SightEvidence.Counts(h3.Evidence).Visible);
        Assert.Contains("clear-and-absent", h3.Evidence);
    }

    // U3d a2 (QA F1): the entry behind a wall on ticks the killer never receives must fail, not be counted as undecided.
    [Fact]
    public async Task EntriesOnTicksTheKillerNeverReceivesFail()
    {
        await using var server = new LobbyServer { Layout = SightLayout.Blocked, KillerEntry = KillerLos.UnjoinableTicks };
        var h3 = await H3Async(server);
        Assert.False(h3.Passed);
        Assert.Contains("Killer 1 leaked to survivor 2", h3.Evidence);
        Assert.Contains("never delivered to the killer: survivor 2's WorldState", h3.Evidence);
        Assert.True(server.KillerEntriesSent > 0);
    }

    // A stopped survivor that leaks still fails H3 although the killer entry is legal in the same worlds.
    [Fact]
    public async Task ALegalKillerEntryDoesNotHideASilentSurvivorLeak()
    {
        await using var server = new LobbyServer { Layout = SightLayout.Clear, KillerEntry = KillerLos.Contract, LeakSilent = true };
        var h3 = await H3Async(server);
        Assert.False(h3.Passed);
        Assert.Contains("Silent survivor", h3.Evidence);
    }
}
