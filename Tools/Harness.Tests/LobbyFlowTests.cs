using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class LobbyFlowTests
{
    [Fact]
    public async Task MatchChecksShareOneHostConfiguredMatchAndOnlyH5RebindsReservedSlot()
    {
        await using var server = new LobbyServer();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        // H2's real-time p99 is a deployment performance check, not a deterministic
        // assertion about the test machine's scheduler. Its math is tested separately.
        var options = new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3", "H4", "H5", "H6" } };
        var report = await HarnessRunner.RunAsync(options, deadline.Token);
        Assert.All(report.Items.Where(i => i.Id != "H6"), item => Assert.True(item.Passed, $"{item.Id}: {item.Evidence}"));
        Assert.Equal(45, report.Total);
        Assert.False(report.GatesPassed); // H1 was not run.
        Assert.Equal("skipped (needs U2b)", report.Items.Single(i => i.Id == "H6").Evidence);
        Assert.Equal(1, server.MatchStarts);
        Assert.Equal(1, server.Rebinds); // H4 retains the existing survivor socket.
        Assert.Equal(1, server.ReconnectRequests);
        Assert.Contains("500 inputs", report.Items.Single(i => i.Id == "H4").Evidence);
        Assert.Contains("achieved rate=", report.Items.Single(i => i.Id == "H4").Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenReconnectFailsH5ButNotH4(bool disconnectNaN)
    {
        await using var server = new LobbyServer { BrokenReconnect = true, DisconnectNaN = disconnectNaN };
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H4", "H5" } });
        var h4 = report.Items.Single(i => i.Id == "H4");
        Assert.True(h4.Passed, h4.Evidence);
        var h5 = report.Items.Single(i => i.Id == "H5");
        Assert.False(h5.Passed);
        Assert.Contains("Reconnect changed PlayerId", h5.Evidence);
        Assert.Equal(disconnectNaN ? 2 : 1, server.ReconnectRequests);
        if (disconnectNaN)
        {
            Assert.Contains("server disconnected abusive mover (accepted)", h4.Evidence);
            Assert.Contains("PlayerId=2->3", h4.Evidence);
        }
    }

    [Theory]
    [InlineData(false, false, "exceeds")]
    [InlineData(true, false, "100,100: no movement evidence")]
    [InlineData(false, true, "500/s: no movement evidence")]
    public async Task H4RejectsSpeedingAndStationaryMovers(bool neverMove, bool blockFlood, string expected)
    {
        await using var server = new LobbyServer { NeverMove = neverMove, BlockFloodMovement = blockFlood,
            MovementMultiplier = neverMove || blockFlood ? 1 : 10 };
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H4" } });
        var h4 = Assert.Single(report.Items);
        Assert.False(h4.Passed);
        Assert.Contains(expected, h4.Evidence);
        if (neverMove || blockFlood)
        {
            Assert.True(server.OppositeInputs > 0);
            Assert.Contains("direction=opposite", h4.Evidence);
        }
    }

    [Fact]
    public async Task H4RetriesOppositeDirectionWhenForwardIsBlocked()
    {
        await using var server = new LobbyServer { BlockForward = true };
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H4" } });
        var h4 = Assert.Single(report.Items);
        Assert.True(h4.Passed, h4.Evidence);
        Assert.Contains("100,100, direction=opposite", h4.Evidence);
        Assert.Contains("500/s, direction=opposite", h4.Evidence);
        Assert.Equal(0, server.ReconnectRequests);
    }

    [Fact]
    public async Task H4AcceptsDefensiveFloodDisconnectWithControlStillServing()
    {
        await using var server = new LobbyServer { DisconnectFlood = true };
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H4" } });
        var h4 = Assert.Single(report.Items);
        Assert.True(h4.Passed, h4.Evidence);
        Assert.Contains("500/s, direction=forward", h4.Evidence);
        Assert.Contains("server disconnected abusive mover (accepted), control pong OK", h4.Evidence);
        Assert.Equal(0, server.ReconnectRequests);
    }

    [Theory]
    [InlineData(true, false, "Silent survivor")]
    [InlineData(false, true, "Killer")]
    public async Task H3FailsAgainstMaskingRegressions(bool leakSilent, bool leakKiller, string evidence)
    {
        await using var server = new LobbyServer { LeakSilent = leakSilent, LeakKiller = leakKiller };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3" } }, deadline.Token);
        Assert.Equal(0, report.Total);
        Assert.False(report.Items.Single().Passed);
        Assert.Contains(evidence, report.Items.Single().Evidence);
    }
}
