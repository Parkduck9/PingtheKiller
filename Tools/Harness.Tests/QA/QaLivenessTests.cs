using System.Diagnostics;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

// Every network wait must have a deadline: a silent or stalled server must make each check fail in bounded time.
public sealed class QaLivenessTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(4);

    [Theory]
    [InlineData("H1")]
    [InlineData("H2")]
    [InlineData("H3")]
    [InlineData("H4")]
    [InlineData("H5")]
    public async Task AcceptThenSilentServerFailsEachCheckInBoundedTime(string id)
    {
        await using var server = new SilentServer();
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { id }, timeoutSeconds: 0.3, durationSeconds: 5, hardLimitSeconds: 20);
        Qa.AssertFailed(report, id, label: nameof(QaLivenessTests) + ":" + id);
        Assert.True(elapsed < Bound, $"{id} took {elapsed} against a silent server.");
    }

    [Fact]
    public async Task FullRunAgainstSilentServerFailsEverythingInBoundedTime()
    {
        await using var server = new SilentServer();
        var clock = Stopwatch.StartNew();
        using var hard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromMilliseconds(300),
            Duration = TimeSpan.FromSeconds(5) }, hard.Token);
        Assert.Equal(0, report.Total);
        Assert.False(report.GatesPassed);
        Assert.All(report.Items, i => Assert.False(i.Passed));
        Assert.Equal(6, report.Items.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"full run took {clock.Elapsed}");
    }

    [Theory]
    [InlineData("H1")]
    [InlineData("H2")]
    [InlineData("H3")]
    [InlineData("H4")]
    [InlineData("H5")]
    public async Task ConnectionRefusedFailsEachCheckQuickly(string id)
    {
        var (report, elapsed) = await Qa.RunAsync(SilentServer.ClosedPort(), new[] { id }, timeoutSeconds: 0.5, hardLimitSeconds: 20);
        Qa.AssertFailed(report, id, label: nameof(QaLivenessTests) + ":" + id);
        Assert.True(elapsed < Bound, $"{id} took {elapsed} with connection refused.");
    }

    [Theory]
    [InlineData("H2")]
    [InlineData("H3")]
    [InlineData("H4")]
    [InlineData("H5")]
    public async Task ServerThatGoesSilentAfterMatchStartFailsInBoundedTime(string id)
    {
        await using var server = new QaServer(new QaFaults { StopWorldAfterStart = true });
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { id }, timeoutSeconds: 0.5, durationSeconds: 5, hardLimitSeconds: 20);
        Qa.AssertFailed(report, id, label: nameof(QaLivenessTests) + ":" + id);
        Assert.True(elapsed < TimeSpan.FromSeconds(8), $"{id} took {elapsed} against a server that stopped sending WorldState.");
    }

    [Theory]
    [InlineData("H2")]
    [InlineData("H3")]
    [InlineData("H4")]
    [InlineData("H5")]
    public async Task ServerThatOnlySendsIrrelevantTrafficCannotKeepTheCheckAlive(string id)
    {
        // A flood of unrelated packets (Pong spam) must not extend an expected-packet wait indefinitely.
        await using var server = new QaServer(new QaFaults { SpamPongsInsteadOfWorld = true });
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { id }, timeoutSeconds: 0.5, durationSeconds: 3, hardLimitSeconds: 25);
        Qa.AssertFailed(report, id, label: nameof(QaLivenessTests) + ":" + id);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"{id} took {elapsed} against a chatty server.");
    }

    [Fact]
    public async Task CancellationTokenStopsARunningCheckPromptly()
    {
        await using var server = new SilentServer();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var clock = Stopwatch.StartNew();
        // Long per-wait timeout (30 s) but external cancellation after 0.4 s (Ctrl+C path).
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(30),
            Only = new HashSet<string> { "H1", "H3" } }, cts.Token);
        Assert.All(report.Items, i => Assert.False(i.Passed));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(6), $"cancel took {clock.Elapsed}");
    }
}
