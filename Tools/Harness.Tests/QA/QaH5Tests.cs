using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

public sealed class QaH5Tests
{
    [Fact]
    public async Task CorrectServerPassesH5()
    {
        await using var server = new QaServer();
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H5" });
        Qa.AssertPassed(report, "H5");
        Assert.Equal(1, server.ReconnectRequests);
    }

    public static TheoryData<string> BrokenH5 => new()
    {
        "different-player-id", "position-reset", "role-changed", "status-changed", "capture-count-reset",
        "rejoin-result-nonzero", "rejoin-zero-token", "rejoin-answer-delayed-beyond-wait-deadline"
    };

    private static QaFaults Fault(string name) => name switch
    {
        "different-player-id" => new QaFaults { BrokenReconnect = true },
        "position-reset" => new QaFaults { ResetPositionOnRejoin = true },
        "role-changed" => new QaFaults { ChangeRoleOnRejoin = true },
        "status-changed" => new QaFaults { ChangeStatusOnRejoin = true },
        "capture-count-reset" => new QaFaults { ResetCapturesOnRejoin = true },
        "rejoin-result-nonzero" => new QaFaults { RejoinResult = 2 },
        "rejoin-zero-token" => new QaFaults { RejoinZeroToken = true },
        "rejoin-answer-delayed-beyond-wait-deadline" => new QaFaults { RejoinDelayMs = 3500 },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(BrokenH5))]
    public async Task BrokenReconnectFailsH5(string fault)
    {
        await using var server = new QaServer(Fault(fault));
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { "H5" }, timeoutSeconds: 2, hardLimitSeconds: 30);
        string? expected = fault switch
        {
            "different-player-id" => "PlayerId",
            "position-reset" or "role-changed" or "status-changed" or "capture-count-reset" => "Reconnect changed",
            "rejoin-result-nonzero" => "Join rejected",
            "rejoin-zero-token" => "zero reconnect token",
            _ => null
        };
        Qa.AssertFailed(report, "H5", expected, fault);
        Assert.True(elapsed < TimeSpan.FromSeconds(15), $"H5 took {elapsed} ({fault}).");
    }

    [Fact]
    public async Task RejoinSlowerThanFiveSecondsFailsEvenWhenPerWaitTimeoutIsLonger()
    {
        // Per-wait timeout 9 s, but the spec window is 5 s: a 5.5 s rejoin must fail H5.
        await using var server = new QaServer(new QaFaults { RejoinDelayMs = 5500 });
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { "H5" }, timeoutSeconds: 9, hardLimitSeconds: 30);
        Qa.AssertFailed(report, "H5", label: "rejoin-5.5s");
        Assert.True(elapsed < TimeSpan.FromSeconds(8), $"H5 waited {elapsed}; should stop at the 5 s window.");
    }

    [Fact]
    public async Task BrokenReconnectDoesNotFailGateH4()
    {
        await using var server = new QaServer(new QaFaults { BrokenReconnect = true });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H4", "H5" });
        Qa.AssertPassed(report, "H4");
        Qa.AssertFailed(report, "H5", label: "broken-reconnect-with-h4");
    }
}
