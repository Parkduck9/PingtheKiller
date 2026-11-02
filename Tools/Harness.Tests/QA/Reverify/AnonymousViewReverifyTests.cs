using PingTheKiller.Harness;
using PingTheKiller.Harness.Tests;
using PingTheKiller.Harness.Tests.QA;
using PingTheKiller.Harness.Tests.QA.U3c;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.Reverify;

public sealed class AnonymousViewReverifyTests
{
    private static async Task<CheckResult> RunH3Async(int port)
    {
        var (report, _) = await Qa.RunAsync(port, new[] { "H3" }, timeoutSeconds: 3);
        return Qa.Item(report, "H3");
    }

    [Fact]
    public async Task StoppedSurvivor_EmittedAnonymouslyAtTruePosition_FailsH3()
    {
        // Attack: stopped survivor emitted anonymously at exact true position with Signal flag
        await using var server = new LobbyServer
        {
            AnonymousKillerView = true,
            LeakSilent = true
        };
        var h3 = await RunH3Async(server.Port);

        Assert.False(h3.Passed, $"FALSE PASS: anonymous exact-position leak passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("Silent survivor", h3.Evidence);
    }

    [Fact]
    public async Task StoppedSurvivor_EmittedAnonymouslyWithSignalZeroFlag_FailsH3()
    {
        // Attack: stopped survivor emitted anonymously at true position with Signal=0 flag
        // Uses QaViewServer with Exact leak (sets Flags=0 when stopped)
        var (h3, _) = await U3cRun.H3Async(new ViewKnobs
        {
            Anonymous = true,
            Leak = StoppedLeak.Exact
        }, "anon-leak-exact-signal0");

        Assert.False(h3.Passed, $"FALSE PASS: anonymous exact leak with Signal=0 passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("Silent survivor", h3.Evidence);
    }

    [Fact]
    public async Task StoppedSurvivor_EmittedAnonymouslyOffsetInsideEpsilon_FailsH3()
    {
        // Attack: stopped survivor emitted anonymously at offset (0.02, -0.03) m (distance ~0.036 m <= 0.05 m)
        await using var server = new LobbyServer
        {
            AnonymousKillerView = true,
            LeakSilentOffset = true
        };
        var h3 = await RunH3Async(server.Port);

        Assert.False(h3.Passed, $"FALSE PASS: anonymous offset leak inside epsilon passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("Silent survivor", h3.Evidence);
    }

    [Fact]
    public async Task StoppedSurvivor_EmittedAnonymouslyOffsetOutsideEpsilon_FailsH3()
    {
        // Attack: stopped survivor emitted anonymously at offset 0.06 m (> 0.05 m epsilon)
        // Attribution cannot explain it, so it fails with "unexplained killer-view entry"
        var (h3, _) = await U3cRun.H3Async(new ViewKnobs
        {
            Anonymous = true,
            Leak = StoppedLeak.Offset,
            OffsetX = 0.05f,
            OffsetZ = 0.04f // sqrt(0.0025 + 0.0016) = 0.064 m > 0.05 m
        }, "anon-leak-offset-outside-eps");

        Assert.False(h3.Passed, $"FALSE PASS: anonymous offset leak outside epsilon passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("unexplained killer-view entry", h3.Evidence);
    }

    [Fact]
    public async Task Control_AnonymousKillerViewCorrectServer_PassesH3()
    {
        // Control: correct server with anonymous killer view and no leak
        await using var server = new LobbyServer
        {
            AnonymousKillerView = true,
            LeakSilent = false
        };
        var h3 = await RunH3Async(server.Port);

        Assert.True(h3.Passed, $"FALSE FAIL: correct anonymous server failed H3: {h3.Evidence}");
        Assert.Equal(20, h3.Score);
        Assert.Contains("0 unexplained, 0 to stopped survivor", h3.Evidence);
    }
}
