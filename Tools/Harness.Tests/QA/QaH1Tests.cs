using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

public sealed class QaH1Tests
{
    [Fact]
    public async Task CorrectServerPassesH1()
    {
        await using var server = new QaServer();
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H1" }, timeoutSeconds: 3);
        Qa.AssertPassed(report, "H1");
    }

    // Every case: a server broken in exactly one way. H1 (gate, 25 pts) must fail.
    public static TheoryData<string> BrokenH1 => new()
    {
        "drop-first", "drop-middle", "drop-barrier(10000th ping)",
        "reorder-early", "reorder-late(9998/9999)", "reorder-with-barrier(9999/10000)",
        "alter-timestamp-first", "alter-timestamp-middle", "alter-timestamp-last", "alter-timestamp-barrier",
        "duplicate-first", "duplicate-middle",
        "naive-framing-no-reassembly", "one-frame-per-read", "close-after-1", "close-after-5000", "close-after-9999", "close-after-10000"
    };

    private static QaFaults Fault(string name) => name switch
    {
        "drop-first" => new QaFaults { DropPongAt = 0 },
        "drop-middle" => new QaFaults { DropPongAt = 5000 },
        "drop-barrier(10000th ping)" => new QaFaults { DropPongAt = 10000 },
        "reorder-early" => new QaFaults { ReorderPongAt = 100 },
        "reorder-late(9998/9999)" => new QaFaults { ReorderPongAt = 9998 },
        "reorder-with-barrier(9999/10000)" => new QaFaults { ReorderPongAt = 9999 },
        "alter-timestamp-first" => new QaFaults { AlterTimestampAt = 0 },
        "alter-timestamp-middle" => new QaFaults { AlterTimestampAt = 4321 },
        "alter-timestamp-last" => new QaFaults { AlterTimestampAt = 9999 },
        "alter-timestamp-barrier" => new QaFaults { AlterTimestampAt = 10000 },
        "duplicate-first" => new QaFaults { DuplicatePongAt = 0 },
        "duplicate-middle" => new QaFaults { DuplicatePongAt = 5000 },
        "naive-framing-no-reassembly" => new QaFaults { NaiveFraming = true },
        "one-frame-per-read" => new QaFaults { OneFramePerRead = true },
        "close-after-1" => new QaFaults { ClosePeerAfterPongs = 1 },
        "close-after-5000" => new QaFaults { ClosePeerAfterPongs = 5000 },
        "close-after-9999" => new QaFaults { ClosePeerAfterPongs = 9999 },
        "close-after-10000" => new QaFaults { ClosePeerAfterPongs = 10000 },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // KNOWN LIMITATION (documented in docs/qa/U3-qa-a1.md, minor): the final barrier Pong is the last frame
    // the harness reads, so a duplicate of that single Pong is never observed.
    [Fact]
    public async Task Gap_DuplicateOfTheBarrierPongItselfIsNotDetected()
    {
        await using var server = new QaServer(new QaFaults { DuplicatePongAt = 10000 });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H1" }, timeoutSeconds: 3);
        Assert.True(Qa.Item(report, "H1").Passed,
            "Gap closed: update docs/qa/U3-qa-a1.md and turn this into a BrokenServerFailsH1 case.");
    }

    [Theory]
    [MemberData(nameof(BrokenH1))]
    public async Task BrokenServerFailsH1(string fault)
    {
        await using var server = new QaServer(Fault(fault));
        // 1 s per-wait deadline: loopback pongs arrive in microseconds, so a dropped pong is detected fast.
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { "H1" }, timeoutSeconds: 1, hardLimitSeconds: 30);
        Qa.AssertFailed(report, "H1", label: fault);
        Assert.Equal(0, report.Total);
        Assert.True(elapsed < TimeSpan.FromSeconds(15), $"H1 took {elapsed} to fail ({fault}).");
    }
}
