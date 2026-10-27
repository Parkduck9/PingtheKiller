using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

public sealed class QaH4Tests
{
    [Fact]
    public async Task CorrectServerPassesH4()
    {
        await using var server = new QaServer();
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H4" });
        Qa.AssertPassed(report, "H4");
        Assert.False(server.IsDead);
        Assert.Equal(0, server.ReconnectRequests);
    }

    public static TheoryData<string> BrokenH4 => new()
    {
        "hang-after-length-minus1", "crash-after-length-minus1",
        "hang-after-length-maxint", "crash-after-length-maxint", "keeps-maxint-socket-open",
        "keeps-length0-socket-open", "keeps-length1-socket-open", "keeps-length-minus1-socket-open", "keeps-length16385-socket-open",
        "hang-after-unknown-id",
        "no-clamp(100,100)", "per-axis-clamp(1.41x diagonal)", "clamp-1.5x", "per-input-movement(500/s => 25x)",
        "hang-on-nan", "crash-on-nan", "nan-poisons-position", "nan-treated-as-move",
        "hang-on-flood", "crash-on-flood"
    };

    private static QaFaults Fault(string name) => name switch
    {
        "hang-after-length-minus1" => new QaFaults { DeadOnNegativeLength = DeadMode.Hang },
        "crash-after-length-minus1" => new QaFaults { DeadOnNegativeLength = DeadMode.Crash },
        "hang-after-length-maxint" => new QaFaults { DeadOnMaxLength = DeadMode.Hang },
        "crash-after-length-maxint" => new QaFaults { DeadOnMaxLength = DeadMode.Crash },
        "keeps-maxint-socket-open" => new QaFaults { TolerateLength = int.MaxValue },
        "keeps-length0-socket-open" => new QaFaults { TolerateLength = 0 },
        "keeps-length1-socket-open" => new QaFaults { TolerateLength = 1 },
        "keeps-length-minus1-socket-open" => new QaFaults { TolerateLength = -1 },
        "keeps-length16385-socket-open" => new QaFaults { TolerateLength = 16385 },
        "hang-after-unknown-id" => new QaFaults { DeadOnUnknownId = DeadMode.Hang },
        "no-clamp(100,100)" => new QaFaults { Clamp = ClampMode.None },
        "per-axis-clamp(1.41x diagonal)" => new QaFaults { Clamp = ClampMode.PerAxis },
        "clamp-1.5x" => new QaFaults { Clamp = ClampMode.Scale150 },
        "per-input-movement(500/s => 25x)" => new QaFaults { MovePerInput = true },
        "hang-on-nan" => new QaFaults { DeadOnNaN = DeadMode.Hang },
        "crash-on-nan" => new QaFaults { DeadOnNaN = DeadMode.Crash },
        "nan-poisons-position" => new QaFaults { NaNPoisonsPosition = true },
        "nan-treated-as-move" => new QaFaults { NaNAsMove = true },
        "hang-on-flood" => new QaFaults { DeadOnFlood = DeadMode.Hang },
        "crash-on-flood" => new QaFaults { DeadOnFlood = DeadMode.Crash },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(BrokenH4))]
    public async Task BrokenServerFailsH4(string fault)
    {
        await using var server = new QaServer(Fault(fault));
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { "H4" }, timeoutSeconds: 1, hardLimitSeconds: 40);
        string? expected = fault switch
        {
            "no-clamp(100,100)" or "per-axis-clamp(1.41x diagonal)" or "clamp-1.5x" or "per-input-movement(500/s => 25x)" => "exceeds",
            "nan-poisons-position" => "Non-finite",
            "nan-treated-as-move" => "NaN input moved",
            _ => null
        };
        Qa.AssertFailed(report, "H4", expected, fault);
        Assert.Equal(0, report.Total);
        Assert.False(report.GatesPassed);
        Assert.True(elapsed < TimeSpan.FromSeconds(25), $"H4 took {elapsed} to fail ({fault}).");
    }

    // OBSERVATION (minor, false-fail direction): PLAN.md H4 only demands "no crash" for an unknown packet ID, but the
    // harness additionally requires the abusive socket to be closed. A server that silently ignores unknown IDs
    // and keeps serving everyone is therefore failed. Documented in docs/qa/U3-qa-a1.md.
    [Fact]
    public async Task Observation_HarnessRequiresDisconnectOnUnknownIdEvenThoughPlanOnlyRequiresNoCrash()
    {
        await using var server = new QaServer(new QaFaults { TolerateUnknownId = true });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H4" }, timeoutSeconds: 1);
        Qa.AssertFailed(report, "H4", label: "tolerates-unknown-id");
        Assert.False(server.IsDead);
    }

    // By design (U3-a2 item 1) a server may defensively disconnect an abusive mover. Documenting what that
    // implies: a server that kicks ANY client sending a non-zero direction still passes H4 on its own,
    // so H4 alone cannot prove that normal movement works (H3's Signal baseline covers that).
    [Fact]
    public async Task Observation_ServerThatKicksEveryMovingClientStillPassesH4Alone()
    {
        await using var server = new QaServer(new QaFaults { KickOnAnyMovement = true });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H4" });
        Assert.True(Qa.Item(report, "H4").Passed, Qa.Item(report, "H4").Evidence);
    }

    [Fact]
    public async Task ServerThatKicksEveryMovingClientFailsH3()
    {
        await using var server = new QaServer(new QaFaults { KickOnAnyMovement = true });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" }, timeoutSeconds: 1);
        Qa.AssertFailed(report, "H3", label: "kick-on-any-movement");
    }
}
