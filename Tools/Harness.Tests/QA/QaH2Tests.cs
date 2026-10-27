using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

public sealed class QaH2Tests
{
    [Fact]
    public async Task CorrectServerPassesH2()
    {
        await using var server = new QaServer();
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H2" }, durationSeconds: 2);
        Qa.AssertPassed(report, "H2");
    }

    [Fact]
    public async Task TickGapOfExactlyTwoIsAllowed()
    {
        // TickJump=1 => the survivor stream skips one tick once (delta 2). Spec: only gaps > 2 fail.
        await using var server = new QaServer(new QaFaults { SurvivorTickJumpAt = 15, TickJump = 1 });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H2" }, durationSeconds: 2);
        Qa.AssertPassed(report, "H2");
    }

    public static TheoryData<string> BrokenH2 => new()
    {
        "sustained-150ms-interval", "stall-200ms-every-5th-world", "killer-stream-single-300ms-stall",
        "survivor-tick-gap-3", "survivor-tick-gap-4", "duplicate-tick", "tick-goes-backwards"
    };

    private static QaFaults Fault(string name) => name switch
    {
        "sustained-150ms-interval" => new QaFaults { WorldIntervalMs = 150 },
        "stall-200ms-every-5th-world" => new QaFaults { StallEveryN = 5, StallMs = 200 },
        "killer-stream-single-300ms-stall" => new QaFaults { KillerStallAtIteration = 15, StallMs = 300 },
        "survivor-tick-gap-3" => new QaFaults { SurvivorTickJumpAt = 15, TickJump = 2 },
        "survivor-tick-gap-4" => new QaFaults { SurvivorTickJumpAt = 15, TickJump = 3 },
        "duplicate-tick" => new QaFaults { DuplicateTickAt = 15 },
        "tick-goes-backwards" => new QaFaults { TickBackwardAt = 15 },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(BrokenH2))]
    public async Task BrokenSyncFailsH2(string fault)
    {
        await using var server = new QaServer(Fault(fault));
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H2" }, timeoutSeconds: 1, durationSeconds: 3);
        Qa.AssertFailed(report, "H2", fault.Contains("tick") ? "Tick sequence" : "p99", fault);
        Assert.Equal(0, report.Total);
    }
}
