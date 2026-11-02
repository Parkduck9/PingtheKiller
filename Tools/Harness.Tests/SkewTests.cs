using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

// U3d a3: a correct server whose bytes reach one client 300 ms late (a lagging stream) must pass H3 and H6. Coverage is by
// tick, not by wall clock: the killer stream is read until it covers every survivor tick, contact is compared at equal ticks.
// Connection 0 is the killer. One class per case so xUnit runs the scenarios in parallel.
public abstract class H3SkewTest(bool killerLate, bool clear)
{
    [Fact]
    public async Task ContractServerPassesH3WithOneStream300MsLate()
    {
        await using var server = new LobbyServer { Layout = clear ? SightLayout.Clear : SightLayout.Blocked, KillerEntry = KillerLos.Contract };
        await using var proxy = new SkewProxy(server.Port, connection =>
            (connection == 0) == killerLate ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var report = await HarnessRunner.RunAsync(new Options { Port = proxy.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3" } }, deadline.Token);
        var h3 = Assert.Single(report.Items);
        Assert.True(h3.Passed, h3.Evidence);
        Assert.Contains("every survivor tick joined a killer tick", h3.Evidence);
        var (visible, blocked) = SightEvidence.Counts(h3.Evidence);
        Assert.True(clear ? visible > 0 : visible == 0 && blocked > 0, h3.Evidence);
    }
}

public sealed class H3KillerLate300ClearTests() : H3SkewTest(true, true);
public sealed class H3SurvivorLate300ClearTests() : H3SkewTest(false, true);
public sealed class H3KillerLate300BlockedTests() : H3SkewTest(true, false);
public sealed class H3SurvivorLate300BlockedTests() : H3SkewTest(false, false);

public abstract class H6SkewTest(bool killerLate)
{
    [Fact]
    public async Task LineOfSightServerPassesH6WithStreams300MsLate()
    {
        await using var server = new ScenarioServer { LineOfSight = true, ExecutionGrace = true };
        await using var proxy = new SkewProxy(server.Port, connection =>
            (connection == 0) == killerLate ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero);
        var (h6, _) = await H6.RunAsync(proxy.Port, expectLos: true);
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        Assert.Contains("every survivor tick joined a killer tick", h6.Evidence);
        Assert.Contains("in every stream", h6.Evidence);
        var (visible, blocked) = SightEvidence.Counts(h6.Evidence);
        Assert.True(visible > 0 && blocked > 0, h6.Evidence);
    }
}

public sealed class H6KillerLate300Tests() : H6SkewTest(true);
public sealed class H6SurvivorsLate300Tests() : H6SkewTest(false);
