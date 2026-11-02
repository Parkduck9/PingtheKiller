using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

public sealed class QaH3Tests
{
    [Fact]
    public async Task CorrectServerPassesH3()
    {
        await using var server = new QaServer();
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" });
        Qa.AssertPassed(report, "H3");
        Assert.False(server.UnexpectedFlow);
        Assert.Equal(1, server.MatchStarts);
    }

    public static TheoryData<string> BrokenH3 => new()
    {
        "flag-only-entry-for-silent-survivor",
        "entry-lingers-1.5s-after-stop",
        "xz-bytes-in-terminal-entry",
        "x-only-in-timeleft-field",
        "z-only-in-killer-yaw-field",
        "xz-bytes-in-skilleffect-packet",
        "killer-entry-in-survivor-world"
    };

    private static QaFaults Fault(string name) => name switch
    {
        "flag-only-entry-for-silent-survivor" => new QaFaults { LeakFlagOnly = true },
        "entry-lingers-1.5s-after-stop" => new QaFaults { LeakEntryForSeconds = 1.5 },
        "xz-bytes-in-terminal-entry" => new QaFaults { LeakInTerminal = true },
        "x-only-in-timeleft-field" => new QaFaults { LeakXInTimeLeft = true },
        "z-only-in-killer-yaw-field" => new QaFaults { LeakZInKillerYaw = true },
        "xz-bytes-in-skilleffect-packet" => new QaFaults { LeakInSkillEffect = true },
        // U3d: a killer entry in a survivor world is a leak unless it is contract-legal (clear sight, Flags/StatusTimer/CaptureCount 0).
        // QaServer's positions are off-map (no walls), so sight is always clear; the entry is made illegal by copying the killer's
        // Signal flag into it. Previously any entry failed; the old, legal entry now passes (LegalKillerEntryInSurvivorWorldPassesH3).
        "killer-entry-in-survivor-world" => new QaFaults { LeakKillerToSurvivors = true, KillerEntryFlags = (byte)PingTheKiller.Shared.Game.PlayerFlags.Signal },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(BrokenH3))]
    public async Task LeakingServerFailsH3(string fault)
    {
        await using var server = new QaServer(Fault(fault));
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" }, hardLimitSeconds: 30);
        Qa.AssertFailed(report, "H3", label: fault);
        Assert.Equal(0, report.Total);
        Assert.False(report.GatesPassed);
        Assert.Contains("leak", Qa.Item(report, "H3").Evidence, StringComparison.OrdinalIgnoreCase);
    }

    // U3d: what the previous theory case sent (the killer's entry with real X/Z, all other fields 0, clear sight) is legal now.
    [Fact]
    public async Task LegalKillerEntryInSurvivorWorldPassesH3()
    {
        await using var server = new QaServer(new QaFaults { LeakKillerToSurvivors = true });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" });
        Qa.AssertPassed(report, "H3");
        Assert.Contains("legal line-of-sight killer entries", Qa.Item(report, "H3").Evidence);
    }

    [Fact]
    public async Task ServerThatNeverShowsAMovingSurvivorToTheKillerFailsH3()
    {
        // Over-masking: the killer can never see anyone, so the "moving Signal baseline" must fail.
        await using var server = new QaServer(new QaFaults { NeverShowSurvivorToKiller = true });
        var (report, elapsed) = await Qa.RunAsync(server.Port, new[] { "H3" }, timeoutSeconds: 1);
        Qa.AssertFailed(report, "H3", label: "never-shows-survivor");
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"took {elapsed}");
    }

    // KNOWN LIMITATION (documented, not a required check): H3's byte search is exact-float only, so a
    // server that leaks the position in a transformed (rounded) form outside PlayerState is not detected.
    [Fact]
    public async Task Gap_RoundedCoordinateLeakOutsidePlayerStateIsNotDetected()
    {
        await using var server = new QaServer(new QaFaults { LeakQuantizedInTerminal = true });
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" });
        Assert.True(Qa.Item(report, "H3").Passed,
            "Gap documented in docs/qa/U3-qa-a1.md has been closed; update the report and make this assert Failed.");
    }
}
