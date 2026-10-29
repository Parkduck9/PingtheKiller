using System.Diagnostics;
using PingTheKiller.Harness;
using PingTheKiller.Harness.Tests.QA;
using Xunit;

namespace PingTheKiller.Harness.Tests;

internal static class H6
{
    public static async Task<(CheckResult Item, TimeSpan Elapsed)> RunAsync(int port, double timeoutSeconds = 3)
    {
        using var hard = new CancellationTokenSource(ScenarioCheck.Budget + TimeSpan.FromSeconds(20));
        var clock = Stopwatch.StartNew();
        var report = await HarnessRunner.RunAsync(new Options { Port = port, Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            Only = new HashSet<string> { "H6" } }, hard.Token);
        return (Assert.Single(report.Items), clock.Elapsed);
    }
}

// H6 must pass under both U2b versions (leave-and-re-enter without and with the 2 s execution grace) and must
// not depend on the grace. Separate classes so both run in parallel.
// U3c: also with the U2c anonymous killer view (Id 0, shuffled, quantised progress) and with unknown-Id frames interleaved.
public abstract class ScenarioPassTest(bool grace, bool anonymous = false, bool unknown = false)
{
    [Fact]
    public async Task H6PassesAgainstTheRulesFakeWithPositiveEvidenceForEveryStep()
    {
        await using var server = new ScenarioServer { ExecutionGrace = grace, AnonymousKillerView = anonymous, UnknownFrames = unknown };
        var (h6, _) = await H6.RunAsync(server.Port);
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        foreach (string expected in new[] { "co-hacked by A+B", "ExitOpen=1 seen by killer, A and B", "A Lagging, CaptureCount=1",
            "killer held contact 3 s", "no execution", "A Normal with CaptureCount=1", "before the GameResult",
            "winner=Survivor, reason=SurvivorEscaped", "0 excess" })
            Assert.Contains(expected, h6.Evidence);
        Assert.Equal(1, server.MatchStarts);
        Assert.False(server.UnexpectedFlow);
        Assert.Equal(0, server.Executions);
        Assert.Equal(3, server.GameResultsSent);
        Assert.Equal(anonymous, server.AnonymousWorldsSent > 0);
        if (unknown)
        {
            Assert.Contains("skipped unknown packets: id=0x11 ×", h6.Evidence);
            Assert.Contains("id=0x7E ×", h6.Evidence);
            Assert.DoesNotContain(" 0 unknown-Id frames raw-scanned", h6.Evidence);
        }
        else Assert.DoesNotContain("skipped unknown packets", h6.Evidence);
    }
}

public sealed class ScenarioPassWithoutGraceTests() : ScenarioPassTest(false);
public sealed class ScenarioPassWithGraceTests() : ScenarioPassTest(true);
public sealed class ScenarioPassAnonymousKillerViewTests() : ScenarioPassTest(true, anonymous: true);
public sealed class ScenarioPassUnknownFramesTests() : ScenarioPassTest(false, unknown: true);

// One class per broken fake so xUnit runs these ~50 s scenarios in parallel collections.
public abstract class H6BrokenFakeTest
{
    private readonly H6Fault fault;
    private readonly string[] expected;

    private protected H6BrokenFakeTest(H6Fault fault, params string[] expected)
    {
        this.fault = fault;
        this.expected = expected;
    }

    [Fact]
    public async Task BrokenFakeFailsH6WithMatchingEvidence()
    {
        await using var server = new ScenarioServer(fault);
        var (h6, elapsed) = await H6.RunAsync(server.Port);
        Assert.False(h6.Passed, $"FALSE PASS against {fault}: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        foreach (string text in expected) Assert.Contains(text, h6.Evidence);
        Assert.True(elapsed < ScenarioCheck.Budget, $"{fault} took {elapsed}");
    }
}

public sealed class H6NoGameResultTests() : H6BrokenFakeTest(H6Fault.NoGameResult, "[escape by", "No S2C_GameResult");
public sealed class H6DuplicateGameResultTests() : H6BrokenFakeTest(H6Fault.DuplicateGameResult, "[GameResult]", "killer received 2 S2C_GameResult");
public sealed class H6EscapedEmptyTests() : H6BrokenFakeTest(H6Fault.EscapedEmpty, "[GameResult]", "EscapedIds=[]");
public sealed class H6EscapedBothTests() : H6BrokenFakeTest(H6Fault.EscapedBoth, "[GameResult]", "EscapedIds=[2,3]");
public sealed class H6EscapedOtherTests() : H6BrokenFakeTest(H6Fault.EscapedOther, "[GameResult]", "EscapedIds=[", "expected exactly [");
public sealed class H6WrongWinnerTests() : H6BrokenFakeTest(H6Fault.WinnerKiller, "[GameResult]", "winner=1, reason=0");
public sealed class H6WrongReasonTests() : H6BrokenFakeTest(H6Fault.ReasonTimeUp, "[GameResult]", "winner=0, reason=2");
public sealed class H6SkillEffectLeakTests() : H6BrokenFakeTest(H6Fault.LeakSkillEffect, "killer coordinate", "bytes leaked", "S2C_SkillEffect");
public sealed class H6KillerEntryLeakTests() : H6BrokenFakeTest(H6Fault.LeakKillerEntry, "Killer 1 leaked to survivor");
public sealed class H6ExitNeverOpensTests() : H6BrokenFakeTest(H6Fault.ExitNeverOpens, "[exit open]", "ExitOpen == 1");
public sealed class H6NoCaptureTests() : H6BrokenFakeTest(H6Fault.NoCapture, "[capture: killer to A]", "A captured", "not observed");
public sealed class H6NoRescueTests() : H6BrokenFakeTest(H6Fault.NoRescue, "[rescue]", "A rescued", "not observed");
// Old literal rule: the capturing contact executes on the next tick.
public sealed class H6ExecuteOnContinuedContactTests() : H6BrokenFakeTest(H6Fault.ExecuteOnContinuedContact,
    "[capture:", "player 2 has status 3");
// F1: a valid SurvivorEscaped GameResult ~1 s into the escape walk, long before the exit.
public sealed class H6PrematureEndTests() : H6BrokenFakeTest(H6Fault.PrematureEnd, "[escape by", "GameResult before escaper reached the exit");
// Re-arms while still in contact: executes two ticks after the capture, while the killer holds contact.
public sealed class H6RearmsInContactTests() : H6BrokenFakeTest(H6Fault.RearmsInContact, "[capture:", "player 2 has status 3");
// U3c: killer X/Z inside an unknown-Id frame to the survivors (cannot be decoded, so it is scanned raw).
public sealed class H6LeakInUnknownPacketTests() : H6BrokenFakeTest(H6Fault.LeakInUnknownPacket, "killer coordinate bytes in unknown packet 0x7E (update Shared)");

public sealed class ScenarioOptionTests
{
    private static Options Only(params string[] ids) => new() { Only = new HashSet<string>(ids) };

    [Fact]
    public void H6WithAnyMatchCheckNeedsItsOwnPort()
    {
        foreach (string match in new[] { "H2", "H3", "H4", "H5" })
            Assert.Equal(ScenarioCheck.NeedsOwnServer, Only(match, "H6").H6Conflict());
        Assert.Equal(ScenarioCheck.NeedsOwnServer, new Options().H6Conflict()); // default = all six
        Assert.Equal(ScenarioCheck.NeedsOwnServer, (Only("H3", "H6") with { H6Port = 7777 }).H6Conflict());
        Assert.Equal(ScenarioCheck.NeedsOwnServer, (Only("H3", "H6") with { H6Port = 7777, H6Host = "127.0.0.1" }).H6Conflict());
        Assert.Null((Only("H3", "H6") with { H6Port = 7778 }).H6Conflict());
        Assert.Null((Only("H3", "H6") with { H6Port = 7777, H6Host = "10.0.0.9" }).H6Conflict()); // same port, other host
        Assert.Null(Only("H6").H6Conflict());
        Assert.Null(Only("H1", "H6").H6Conflict());
        Assert.Null(Only("H3", "H4").H6Conflict());
    }

    [Fact]
    public void H6EndpointDefaultsToPortAndHost()
    {
        var plain = Options.Parse(new[] { "--host", "10.1.2.3", "--port", "7001", "--only", "H6" });
        Assert.Equal(("10.1.2.3", 7001), (plain.ForH6().Host, plain.ForH6().Port));
        var split = Options.Parse(new[] { "--host", "10.1.2.3", "--port", "7001", "--h6-port", "7002" });
        Assert.Equal(("10.1.2.3", 7002), (split.ForH6().Host, split.ForH6().Port));
        Assert.Equal(("10.1.2.3", 7001), (split.Host, split.Port));
        var other = Options.Parse(new[] { "--h6-host", "10.9.9.9", "--h6-port", "7002" });
        Assert.Equal(("10.9.9.9", 7002), (other.ForH6().Host, other.ForH6().Port));
        Assert.Null(other.H6Conflict());
    }

    [Theory]
    [InlineData("--h6-port", "0")]
    [InlineData("--h6-port", "65536")]
    [InlineData("--h6-port", "x")]
    [InlineData("--h6-host", " ")]
    public void InvalidH6OptionsFail(string key, string value) => Assert.Throws<ArgumentException>(() => Options.Parse(new[] { key, value }));

    [Fact]
    public async Task SharedServerFailsH6ImmediatelyWithoutNetworkIo()
    {
        // Any connection attempt to this closed port would produce a refusal message, not the exact rule text.
        int port = SilentServer.ClosedPort();
        foreach (int? h6Port in new int?[] { null, port })
        {
            var report = await HarnessRunner.RunAsync(new Options { Port = port, H6Port = h6Port, Timeout = TimeSpan.FromSeconds(0.5),
                Only = new HashSet<string> { "H5", "H6" } });
            var h6 = report.Items.Single(i => i.Id == "H6");
            Assert.False(h6.Passed);
            Assert.Equal(0, h6.Score);
            Assert.Equal(ScenarioCheck.NeedsOwnServer, h6.Evidence);
        }
    }

    [Fact]
    public async Task H6UsesTheH6PortServer()
    {
        // H5 is pointed at a closed port; H6 at a silent server. H6 must connect there and time out on Join.
        await using var silent = new SilentServer();
        var clock = Stopwatch.StartNew();
        var report = await HarnessRunner.RunAsync(new Options { Port = SilentServer.ClosedPort(), H6Port = silent.Port,
            Timeout = TimeSpan.FromSeconds(0.3), Only = new HashSet<string> { "H5", "H6" } });
        var h6 = report.Items.Single(i => i.Id == "H6");
        Assert.False(h6.Passed);
        Assert.Contains("[setup] server did not answer within the 0.3 s per-wait deadline", h6.Evidence);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task OnlyH6AgainstASilentServerFailsInBoundedTime()
    {
        await using var silent = new SilentServer();
        var (h6, elapsed) = await H6.RunAsync(silent.Port, timeoutSeconds: 0.3);
        Assert.False(h6.Passed);
        Assert.Equal(0, h6.Score);
        Assert.Contains("[setup]", h6.Evidence);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"took {elapsed}");
    }

    [Fact]
    public async Task OnlyH6AgainstAClosedPortFailsQuickly()
    {
        var (h6, elapsed) = await H6.RunAsync(SilentServer.ClosedPort(), timeoutSeconds: 0.5);
        Assert.False(h6.Passed);
        Assert.Contains("[setup]", h6.Evidence);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"took {elapsed}");
    }
}
