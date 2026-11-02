using PingTheKiller.Harness;
using PingTheKiller.Harness.Tests;
using PingTheKiller.Harness.Tests.QA;
using PingTheKiller.Harness.Tests.QA.U3d;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.Reverify;

public sealed class BlockedKillerEntryH3Tests
{
    [Fact]
    public async Task BlockedKillerEntry_FailsH3()
    {
        // Attack: killer entry sent to survivor while line of sight is blocked by a wall
        await using var server = new LobbyServer
        {
            Layout = SightLayout.Blocked,
            KillerEntry = KillerLos.Always
        };
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" }, timeoutSeconds: 3);
        var h3 = Qa.Item(report, "H3");

        Assert.False(h3.Passed, $"FALSE PASS: killer entry through wall passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("is blocked", h3.Evidence);
    }

    [Fact]
    public async Task Control_ContractLineOfSight_PassesH3()
    {
        // Control: killer entry sent only when line of sight is clear (blocked ticks have no entry)
        await using var server = new LobbyServer
        {
            Layout = SightLayout.Blocked,
            KillerEntry = KillerLos.Contract
        };
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" }, timeoutSeconds: 3);
        var h3 = Qa.Item(report, "H3");

        Assert.True(h3.Passed, $"FALSE FAIL: contract line-of-sight server failed H3: {h3.Evidence}");
        Assert.Equal(20, h3.Score);
        Assert.Contains("blocked-and-absent", h3.Evidence);
    }
}

public sealed class BlockedKillerEntryH6Tests
{
    [Fact]
    public async Task BlockedKillerEntry_FailsH6()
    {
        // Attack: killer entry sent to survivor B while standing behind a wall in sight step
        await using var server = new ScenarioServer(H6Fault.LosWhileBlocked);
        var (h6, _) = await H6.RunAsync(server.Port);

        Assert.False(h6.Passed, $"FALSE PASS: blocked killer entry passed H6: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Contains("is blocked", h6.Evidence);
    }
}

public sealed class StaleKillerPositionH6Tests
{
    [Fact]
    public async Task StaleKillerPosition_FailsH6()
    {
        // Attack: killer entry in survivor world has previous-tick position while killer moves
        await using var server = new ScenarioServer(H6Fault.LosStalePosition);
        var (h6, _) = await H6.RunAsync(server.Port);

        Assert.False(h6.Passed, $"FALSE PASS: stale killer position passed H6: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Contains("(stale)", h6.Evidence);
    }
}

public sealed class ContractLineOfSightH6Tests
{
    [Fact]
    public async Task Control_ContractLineOfSight_PassesH6()
    {
        // Control: contract-correct line of sight server passes H6
        await using var server = new ScenarioServer { LineOfSight = true };
        var (h6, _) = await H6.RunAsync(server.Port);

        Assert.True(h6.Passed, $"FALSE FAIL: contract line-of-sight server failed H6: {h6.Evidence}");
        Assert.Equal(10, h6.Score);
        Assert.Contains("legal line-of-sight killer entries", h6.Evidence);
    }
}
