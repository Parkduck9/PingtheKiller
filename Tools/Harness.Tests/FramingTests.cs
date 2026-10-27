using System.Diagnostics;
using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class FramingTests
{
    private static Options Settings(PingServer server) => new()
    {
        Port = server.Port, Timeout = TimeSpan.FromSeconds(2), Only = new HashSet<string> { "H1" }
    };

    [Fact]
    public async Task TenThousandPingFramesPassOverRealTcpWithFragmentedReplies()
    {
        await using var server = new PingServer();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        string evidence = await FramingCheck.RunAsync(Settings(server), deadline.Token);
        Assert.Contains("10000/10000", evidence);
        Assert.Equal(10001, server.Pings);
    }

    [Theory]
    [InlineData(123)]
    [InlineData(9999)]
    public async Task DroppingOnePongMakesH1FailAndScoresZero(int index)
    {
        await using var server = new PingServer(dropIndex: index);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var report = await HarnessRunner.RunAsync(Settings(server), deadline.Token);
        Assert.Equal(0, report.Total);
        Assert.False(report.GatesPassed);
        Assert.False(report.Items.Single().Passed);
        Assert.Contains($"Pong #{index}", report.Items.Single().Evidence);
    }

    [Fact]
    public async Task ExtraPongAfterTheTenThousandthFailsTheBarrier()
    {
        await using var server = new PingServer(duplicateLast: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var report = await HarnessRunner.RunAsync(Settings(server), deadline.Token);
        Assert.False(report.Items.Single().Passed);
        Assert.Contains("Extra or reordered", report.Items.Single().Evidence);
    }

    [Fact]
    public async Task SilentServerFailsWithinBoundedTime()
    {
        await using var server = new PingServer(silent: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var clock = Stopwatch.StartNew();
        var report = await HarnessRunner.RunAsync(Settings(server) with { Timeout = TimeSpan.FromMilliseconds(250) }, deadline.Token);
        Assert.False(report.Items.Single().Passed);
        Assert.Equal(0, report.Total);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Silent server took {clock.Elapsed}.");
    }

    [Fact]
    public async Task MalformedReplyFailsH1()
    {
        await using var server = new PingServer(malformed: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var report = await HarnessRunner.RunAsync(Settings(server), deadline.Token);
        Assert.False(report.Items.Single().Passed);
        Assert.Contains("invalid frame", report.Items.Single().Evidence);
    }
}
