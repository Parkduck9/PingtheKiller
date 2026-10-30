using System.Diagnostics;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3b;

internal static class U3bRun
{
    // Runs H6 alone through HarnessRunner (the public entry point) against a QA fake with a 3 s per-wait deadline.
    public static async Task<(CheckResult Item, TimeSpan Elapsed)> H6Async(QaRulesServer server, string label, double timeoutSeconds = 3)
    {
        var (item, elapsed) = await H6.RunAsync(server.Port, timeoutSeconds);
        string? dump = Environment.GetEnvironmentVariable("QA_EVIDENCE_FILE");
        if (dump != null)
            lock (typeof(U3bRun)) File.AppendAllText(dump, $"{label}\t{(item.Passed ? "PASS" : "FAIL")}\t{elapsed.TotalSeconds:F1}s\t{item.Evidence.ReplaceLineEndings(" ")}\n");
        return (item, elapsed);
    }
}

// A broken fake must score 0 on H6, with evidence that names the broken step. One class per fake so xUnit runs
// the ~50 s scenarios in parallel.
public abstract class U3bBrokenFake
{
    private readonly Func<Knobs> make;
    private readonly string[] expected;
    private readonly TimeSpan limit;

    private protected U3bBrokenFake(Func<Knobs> make, params string[] expected) : this(make, TimeSpan.FromSeconds(150), expected) { }

    private protected U3bBrokenFake(Func<Knobs> make, TimeSpan limit, params string[] expected)
    {
        this.make = make;
        this.expected = expected;
        this.limit = limit;
    }

    [Fact]
    public async Task H6FailsWithMatchingEvidence()
    {
        await using var server = new QaRulesServer(make());
        var (h6, elapsed) = await U3bRun.H6Async(server, GetType().Name);
        AfterRun(server); // premise of the fake first, so a false pass is reported together with its proof
        Assert.False(h6.Passed, $"FALSE PASS in {GetType().Name}: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Equal(10, h6.Max);
        foreach (string text in expected) Assert.Contains(text, h6.Evidence);
        Assert.True(elapsed < limit, $"{GetType().Name} took {elapsed} (limit {limit})");

    }

    private protected virtual void AfterRun(QaRulesServer server) { }
}

// ---- controls -----------------------------------------------------------------------------------------------

public sealed class U3bControlTests
{
    [Fact]
    public async Task CorrectFakePassesH6AndScoresTen()
    {
        await using var server = new QaRulesServer();
        var (h6, elapsed) = await U3bRun.H6Async(server, "control");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        Assert.Equal(0, server.Executions);
        Assert.Equal(1, server.MatchStarts);
        Assert.Equal(3, server.ResultsSent);
        Assert.True(elapsed < TimeSpan.FromSeconds(150));
        Assert.DoesNotContain("skipped", h6.Evidence, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class U3bSlowServerTests
{
    // Brief 4: movement 10 % slower than spec must still pass (leg timeouts have slack).
    [Fact]
    public async Task ServerTenPercentSlowerStillPassesH6()
    {
        await using var server = new QaRulesServer(new Knobs { SpeedFactor = 0.9f });
        var (h6, _) = await U3bRun.H6Async(server, "speed-0.9");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(10, h6.Score);
        Assert.Equal(0, server.Executions);
    }
}

// ---- navigation ---------------------------------------------------------------------------------------------

public sealed class U3bNeverMovesTests() : U3bBrokenFake(() => new Knobs { NeverMove = true }, TimeSpan.FromSeconds(30),
    "stuck on leg", "target (", "after ");

// A server at 20 % speed is far outside the 1.5x + 3 s leg slack: the mover must be declared stuck (and quickly).
public sealed class U3bFifthSpeedStuckTests() : U3bBrokenFake(() => new Knobs { SpeedFactor = 0.2f }, TimeSpan.FromSeconds(60), "stuck on leg");

// ---- liveness / bounded time --------------------------------------------------------------------------------

public sealed class U3bSilentRightAfterMatchStartTests() : U3bBrokenFake(() => new Knobs { SilentAfterTick = 0 }, TimeSpan.FromSeconds(20),
    "[setup]", "3.0 s");

public sealed class U3bSilentAfterTwoSecondsTests() : U3bBrokenFake(() => new Knobs { SilentAfterTick = 40 }, TimeSpan.FromSeconds(30),
    "3.0 s");

public sealed class U3bSilentDuringCaptureTests() : U3bBrokenFake(() => new Knobs { SilentAfterTick = 700 }, TimeSpan.FromSeconds(60),
    "3.0 s");

public sealed class U3bSurvivorSocketClosedDuringHackTests() : U3bBrokenFake(
    () => new Knobs { ClosePlayerWhen = s => s.MaxProgress >= 30 ? 3 : null }, TimeSpan.FromSeconds(40), "B:");

public sealed class U3bSurvivorSocketClosedAfterCaptureTests() : U3bBrokenFake(
    () => new Knobs { ClosePlayerWhen = s => s.ALagging ? 3 : null }, TimeSpan.FromSeconds(60), "B:");

public sealed class U3bLaggingSurvivorSocketClosedTests() : U3bBrokenFake(
    () => new Knobs { ClosePlayerWhen = s => s.ALagging ? 2 : null }, TimeSpan.FromSeconds(60), "A:");

public sealed class U3bKillerSocketClosedAfterCaptureTests() : U3bBrokenFake(
    () => new Knobs { ClosePlayerWhen = s => s.ALagging ? 1 : null }, TimeSpan.FromSeconds(60), "killer:");

// ---- scenario steps faked or skipped ------------------------------------------------------------------------

public sealed class U3bExitOpenAtTickZeroTests() : U3bBrokenFake(() => new Knobs { ForceExitOpenAtTick = 0 }, TimeSpan.FromSeconds(30), "ExitOpen");

// The exit opens 5 s into the match although no terminal is complete. The brief requires H6 not to pass this.
public sealed class U3bExitOpensWithoutTerminalsTests() : U3bBrokenFake(() => new Knobs { ForceExitOpenAtTick = 100 }, "exit");

public sealed class U3bProgressNeverReaches100Tests() : U3bBrokenFake(() => new Knobs { ProgressCappedAt99 = true }, TimeSpan.FromSeconds(60),
    "Progress == 100");

public sealed class U3bCaptureLeavesStatusNormalTests() : U3bBrokenFake(() => new Knobs { CaptureKeepsNormal = true }, TimeSpan.FromSeconds(90),
    "[capture: killer to A]", "A captured");

public sealed class U3bLaggingWithCaptureCountZeroTests() : U3bBrokenFake(() => new Knobs { CaptureCountOnCapture = 0 }, TimeSpan.FromSeconds(90),
    "CaptureCount=0", "expected Lagging/1/>0");

public sealed class U3bLaggingWithCaptureCountTwoTests() : U3bBrokenFake(() => new Knobs { CaptureCountOnCapture = 2 }, TimeSpan.FromSeconds(90),
    "CaptureCount=2", "expected Lagging/1/>0");

public sealed class U3bLaggingWithoutTimerTests() : U3bBrokenFake(() => new Knobs { LaggingTimerZero = true }, TimeSpan.FromSeconds(90),
    "StatusTimer=0", "expected Lagging/1/>0");

public sealed class U3bKillerExecutesOnCaptureTests() : U3bBrokenFake(() => new Knobs { CaptureExecutes = true }, TimeSpan.FromSeconds(90),
    "player 2 has status 3");

public sealed class U3bRescueDropsCaptureCountTests() : U3bBrokenFake(() => new Knobs { RescueDropsCaptureCount = true }, TimeSpan.FromSeconds(120),
    "[rescue]", "A rescued");

public sealed class U3bRescueFlipsTheRescuerTests() : U3bBrokenFake(() => new Knobs { RescueMakesPartnerLagging = true }, TimeSpan.FromSeconds(120),
    "[rescue]", "early GameResult");

// The server ends the match with a perfectly valid SurvivorEscaped result ~1 s after the escaper starts walking,
// long before anyone is in ExitRadius. H6 must not report pass (brief 3).
public sealed class U3bResultBeforeAnyoneReachesExitTests() : U3bBrokenFake(() => new Knobs { PrematureEnd = true })
{
    private protected override void AfterRun(QaRulesServer server)
    {
        Assert.NotNull(server.DistanceToExitAtPrematureEnd);
        Assert.True(server.DistanceToExitAtPrematureEnd > 20,
            $"fake premise: escaper was {server.DistanceToExitAtPrematureEnd:F1} m from the exit");
    }
}

public sealed class U3bResultTheMomentRescueCompletesTests() : U3bBrokenFake(() => new Knobs { EndWithoutAnyWalk = true });

// ---- GameResult ---------------------------------------------------------------------------------------------

internal static class Results
{
    public static IReadOnlyList<(S2C_GameResult, int)> One(S2C_GameResult r) => new[] { (r, 0) };
    public static S2C_GameResult With(S2C_GameResult b, Action<S2C_GameResult> edit)
    {
        var copy = new S2C_GameResult { WinnerRole = b.WinnerRole, Reason = b.Reason, EscapedIds = b.EscapedIds.ToList() };
        edit(copy);
        return copy;
    }
}

public sealed class U3bNeverSendsGameResultTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, _) => Array.Empty<(S2C_GameResult, int)>() }, "No S2C_GameResult");

public sealed class U3bResultToKillerOnlyTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => id == 1 ? Results.One(b) : Array.Empty<(S2C_GameResult, int)>() }, "No S2C_GameResult");

public sealed class U3bResultSkipsOneSurvivorTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => id == 3 ? Array.Empty<(S2C_GameResult, int)>() : Results.One(b) });

public sealed class U3bResultSkipsTheKillerTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => id == 1 ? Array.Empty<(S2C_GameResult, int)>() : Results.One(b) });

public sealed class U3bResultSentTwiceToSurvivorTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => id == 2 ? new[] { (b, 0), (b, 0) } : Results.One(b) }, "[GameResult]", "received 2 S2C_GameResult");

public sealed class U3bResultDuplicateWithinDrainTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => id == 3 ? new[] { (b, 0), (b, 10) } : Results.One(b) }, "[GameResult]", "received 2 S2C_GameResult");

public sealed class U3bEscapedIdsContainKillerTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.EscapedIds.Add(1))) }, "[GameResult]", "EscapedIds=[");

public sealed class U3bEscapedIdsEmptyTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.EscapedIds.Clear())) }, "[GameResult]", "EscapedIds=[]");

public sealed class U3bEscapedIdsBothSurvivorsTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.EscapedIds = new List<int> { 2, 3 })) }, "[GameResult]", "EscapedIds=[2,3]");

public sealed class U3bEscapedIdsWrongSurvivorTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.EscapedIds = new List<int> { b.EscapedIds[0] == 2 ? 3 : 2 })) },
    "[GameResult]", "expected exactly [");

public sealed class U3bWinnerKillerTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.WinnerRole = (byte)Role.Killer)) }, "[GameResult]", "winner=1");

public sealed class U3bReasonAllSurvivorsDownTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.Reason = (byte)GameEndReason.AllSurvivorsDown)) }, "[GameResult]", "reason=1");

public sealed class U3bReasonTimeUpTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => r.Reason = (byte)GameEndReason.TimeUp)) }, "[GameResult]", "reason=2");

public sealed class U3bKillerWinsAllSurvivorsDownTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (_, b) => Results.One(Results.With(b, r => { r.WinnerRole = (byte)Role.Killer; r.Reason = (byte)GameEndReason.AllSurvivorsDown; r.EscapedIds.Clear(); })) },
    "[GameResult]");

// Copies differ between clients: the killer's and A's copies are right, B's lists both survivors.
public sealed class U3bCopiesDifferBetweenClientsTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => Results.One(id == 3 ? Results.With(b, r => r.EscapedIds = new List<int> { 2, 3 }) : b) }, "[GameResult]");

public sealed class U3bKillerCopyDiffersTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => Results.One(id == 1 ? Results.With(b, r => r.Reason = (byte)GameEndReason.TimeUp) : b) }, "[GameResult]");

// ---- killer-coordinate leaks to survivors -------------------------------------------------------------------

// All of these leak exactly once, on the first survivor-bound frame after A became Lagging (during the retreat).
internal static class Leaks
{
    public static Action<LeakContext> Once(Action<LeakContext> leak)
    {
        int done = 0;
        return ctx =>
        {
            if (!ctx.ALagging || ctx.RecipientId != 2 || Interlocked.Exchange(ref done, 1) != 0) return;
            leak(ctx);
        };
    }

    public static Action<LeakContext> Always(Action<LeakContext> leak) => ctx => leak(ctx);

    public static long PackXZ(LeakContext c) =>
        ((long)BitConverter.SingleToInt32Bits(c.KillerZ) << 32) | (uint)BitConverter.SingleToInt32Bits(c.KillerX);
}

// U3d: the single entry used to be a leak by itself. Its fields (real Id/Role/X/Z, A Lagging in contact so sight is clear, all other
// fields 0) are contract-legal now (see U3dH6LegalKillerEntryInContactOnceTests), so the leak is the naive copy of the killer's state:
// the Signal flag is carried into the entry, which the contract forbids (Flags must be 0).
public sealed class U3bLeakKillerEntryByIdOnceTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c => c.World.Players.Add(new PlayerState { Id = c.KillerId, Role = (byte)Role.Killer, X = c.KillerX, Z = c.KillerZ, Flags = (byte)PlayerFlags.Signal }))
}, "Killer 1 leaked to survivor", "the contract requires all three 0");

public sealed class U3bLeakKillerEntryEveryFrameTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Always(c => c.World.Players.Add(new PlayerState { Id = c.KillerId, Role = (byte)Role.Killer, X = c.KillerX, Z = c.KillerZ }))
}, TimeSpan.FromSeconds(30), "Killer 1 leaked to survivor");

// Role Killer under a fresh Id (not the killer's Id).
public sealed class U3bLeakKillerRoleUnderFreshIdTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c => c.World.Players.Add(new PlayerState { Id = 99, Role = (byte)Role.Killer, X = c.KillerX, Z = c.KillerZ }))
}, "leaked to survivor");

// Survivor-shaped entry under a fresh Id carrying the killer's position: only the byte scan can see it.
public sealed class U3bLeakSurvivorShapedFreshIdOnceTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c => c.World.Players.Add(new PlayerState { Id = 99, Role = (byte)Role.Survivor, X = c.KillerX, Z = c.KillerZ }))
}, "killer coordinate", "bytes leaked");

public sealed class U3bLeakSkillEffectWithKillerCasterOnceTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c => c.Send(new S2C_SkillEffect { CasterId = c.KillerId, SkillId = 0, X = c.KillerX, Z = c.KillerZ, Duration = 1.5f }))
}, "killer coordinate", "S2C_SkillEffect");

public sealed class U3bLeakSkillEffectOnlyXOnceTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c => c.Send(new S2C_SkillEffect { CasterId = c.KillerId, SkillId = 0, X = c.KillerX, Z = 0, Duration = 1.5f }))
}, "killer coordinate", "S2C_SkillEffect");

public sealed class U3bLeakPongTimestampOnceTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c => c.Send(new S2C_Pong { Timestamp = Leaks.PackXZ(c) }))
}, "killer coordinate", "S2C_Pong");

public sealed class U3bLeakLobbyStateSlotOnceTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c =>
    {
        var lobby = new S2C_LobbyState();
        lobby.Slots[7] = new Slot { PlayerId = BitConverter.SingleToInt32Bits(c.KillerZ), Nickname = "x" };
        c.Send(lobby);
    })
}, "killer coordinate", "S2C_LobbyState");

// Killer X sits in the ThreatLevel-adjacent TimeLeft slot of a survivor world: TimeLeft is a legitimate float field,
// so this is the documented blind spot (see KnownLimits below); here the bogus copy is an additional one.
public sealed class U3bLeakExtraCopyBesideLegitimateFieldTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c =>
    {
        // B's entry (a legitimate holder) takes the killer X AND a bogus entry's Id field carries a second copy.
        var list = c.World.Players;
        int b = list.FindIndex(p => p.Id == 3);
        var entry = list[b];
        entry.X = c.KillerX;
        list[b] = entry;
        list.Add(new PlayerState { Id = BitConverter.SingleToInt32Bits(c.KillerX), Role = (byte)Role.Survivor, X = 1, Z = 2 });
    })
}, "killer coordinate", "2 occurrence(s)");

// Same, but the extra copy is the fresh-Id entry's X while B's own X legitimately equals it as well.
public sealed class U3bLeakCoincidenceFieldPlusSkillEffectCopyTests() : U3bBrokenFake(() => new Knobs
{
    SurvivorFrame = Leaks.Once(c =>
    {
        // Frame 1: a world in which B legitimately stands at killer X (coincidence), plus an unknown-Id entry at killer X.
        var list = c.World.Players;
        int b = list.FindIndex(p => p.Id == 3);
        var entry = list[b];
        entry.Z = c.KillerZ;
        list[b] = entry;
        list.Add(new PlayerState { Id = 98, Role = (byte)Role.Survivor, Z = c.KillerZ });
    })
}, "killer coordinate");

// ---- known limits of the by-spec scan (documented, not asserted as failures; see U3bKnownLimitTests) ---------

// The survivor that stays at its place (not the escaper) never receives a GameResult.
public sealed class U3bResultSkipsTheStandingSurvivorTests() : U3bBrokenFake(
    () => new Knobs { ResultPlan = (id, b) => id != 1 && id != b.EscapedIds[0] ? Array.Empty<(S2C_GameResult, int)>() : Results.One(b) },
    "[GameResult]", "no S2C_GameResult");
