using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3b;

// ---- Round 2: U2b leave-and-re-enter execution rule (+ 2 s grace switch) -----------------------------------

public sealed class U3bControlGraceOnTests
{
    [Fact]
    public async Task CorrectFakeWithTwoSecondGracePassesH6()
    {
        await using var server = new QaRulesServer(new Knobs { ExecutionGraceSeconds = 2 });
        var (h6, _) = await U3bRun.H6Async(server, "control-grace-2s");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        Assert.Equal(0, server.Executions);
        Assert.Contains("held contact 3 s", h6.Evidence);
    }
}

// OLD literal rule: contact executes on the next tick. H6 must fail inside the 2 s hold.
public sealed class U3bOldLiteralRuleTests() : U3bBrokenFake(() => new Knobs { ExecuteOnContinuedContact = true }, TimeSpan.FromSeconds(90),
    "[capture:", "player 2 has status 3");

public sealed class U3bOldLiteralRuleWithGraceTests() : U3bBrokenFake(() => new Knobs { ExecuteOnContinuedContact = true, ExecutionGraceSeconds = 2 },
    TimeSpan.FromSeconds(90), "[capture:");

// Re-arms while still touching: executes 2 ticks after the capture (grace off) ...
public sealed class U3bRearmsInContactTests() : U3bBrokenFake(() => new Knobs { RearmsInContact = true }, TimeSpan.FromSeconds(90),
    "[capture:", "player 2 has status 3");

// ... or right after the 2 s grace ends (grace on): H6 must not depend on the grace to catch it.
public sealed class U3bRearmsInContactWithGraceTests() : U3bBrokenFake(() => new Knobs { RearmsInContact = true, ExecutionGraceSeconds = 2 },
    TimeSpan.FromSeconds(120), "player 2 has status 3");

// Probes of the 2 s hold: a server executing after 1 s / 1.5 s of continuous contact must fail ...
public sealed class U3bExecutesAfterOneSecondContactTests() : U3bBrokenFake(() => new Knobs { ExecuteAfterContactSeconds = 1.0f },
    TimeSpan.FromSeconds(90), "[capture:", "player 2 has status 3");

public sealed class U3bExecutesAfterOnePointNineSecondContactTests() : U3bBrokenFake(() => new Knobs { ExecuteAfterContactSeconds = 1.9f },
    TimeSpan.FromSeconds(90), "[capture:", "player 2 has status 3");

// ---- F2 re-verification and the per-frame ExitOpen rule ----------------------------------------------------

// Terminals display last tick's progress while the exit logic is current: the exit is open one frame before the
// second terminal shows 100. The per-frame rule must catch this inconsistency.
public sealed class U3bExitOpenOneFrameBeforeProgress100Tests() : U3bBrokenFake(() => new Knobs { ProgressDisplayLagsOneTick = true },
    TimeSpan.FromSeconds(60), "ExitOpen=1", "exit opened without terminals");

// ---- Escape-evidence rule: boundary probes -----------------------------------------------------------------

// A server escaping at 2.6 m (> 2.0 + 0.5 and one tick of travel) must fail.
public sealed class U3bEscapesAt2Point6mTests() : U3bBrokenFake(() => new Knobs { ExitRadiusOverride = 2.6f }, TimeSpan.FromSeconds(150),
    "GameResult before escaper reached the exit");

public sealed class U3bKnownLimitEscapeToleranceTests
{
    // KNOWN-LIMIT (info): the evidence rule tolerates a server escaping players slightly outside the 2 m radius.
    [Fact]
    public async Task KnownLimit_ServerEscapingAt2Point2m_StillPassesH6()
    {
        await using var server = new QaRulesServer(new Knobs { ExitRadiusOverride = 2.2f });
        var (h6, _) = await U3bRun.H6Async(server, "KNOWN-LIMIT escape-radius-2.2");
        Assert.True(h6.Passed, "characterisation changed: " + h6.Evidence);
    }

    // Formerly KNOWN-LIMIT F9b (2 s hold let a 2.5 s contact execution pass). Closed by the 3 s hold (F9 fix):
    // the orchestrator flipped this characterisation into a must-fail assertion.
    [Fact]
    public async Task ExecutionAfter2Point5sOfContinuousContact_FailsH6()
    {
        await using var server = new QaRulesServer(new Knobs { ExecuteAfterContactSeconds = 2.5f });
        var (h6, _) = await U3bRun.H6Async(server, "execute-after-2.5s-contact");
        Assert.False(h6.Passed, "FALSE PASS: " + h6.Evidence);
        Assert.Equal(0, h6.Score);
        Assert.Contains("A executed during the capturing contact", h6.Evidence);
    }
}

// Rule-independence under input latency: with the armed rule an input delay changes nothing.
public sealed class U3bInputDelay3TicksTests() : U3bInputDelayBase(3);
public sealed class U3bGraceOnInputDelay2TicksTests
{
    [Fact]
    public async Task GraceOnWithInputDelayStillPasses()
    {
        await using var server = new QaRulesServer(new Knobs { ExecutionGraceSeconds = 2, InputDelayTicks = 2 });
        var (h6, _) = await U3bRun.H6Async(server, "grace-2s-input-delay-2");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(0, server.Executions);
    }
}
