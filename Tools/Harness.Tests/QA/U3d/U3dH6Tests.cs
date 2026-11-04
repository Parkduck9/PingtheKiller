using System.Buffers.Binary;
using System.Text.RegularExpressions;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

// Mutations applied by the proxy to survivor-bound WorldStates of the contract-correct line-of-sight ScenarioServer.
internal static class U3dMut
{
    public static Func<SurvCtx, bool> Once(Func<SurvCtx, bool> mutation)
    {
        int done = 0;
        return c =>
        {
            if (Volatile.Read(ref done) != 0 || !mutation(c)) return false;
            return Interlocked.Exchange(ref done, 1) == 0;
        };
    }

    // Contract-correct on tick delivery: the recipient's own entry becomes Eliminated in every blocked-and-absent world, so the
    // world classifies Ineligible (a spectator world) instead of BlockedAbsent.
    public static Func<SurvCtx, bool> BlockedWorldsBecomeSpectatorWorlds => c =>
    {
        if (c.EntryIndex >= 0 || !c.Eligible || c.Ref != RefClass.Blocked) return false;
        c.World.Players[c.SelfIndex] = c.Self with { Status = (byte)PlayerStatus.Eliminated };
        return true;
    };

    // The same, for survivor B's stream (A's own status must stay readable for the capture step) and for A once B has
    // escaped: under rules v2 B holds the switch farthest from the killer, so on map v2.1 A's final walk to that exit leaves
    // the open road behind walls (its capture and rescue are over by then).
    public static Func<SurvCtx, bool> BlockedWorldsOfBAndOfAAfterBEscapedBecomeSpectatorWorlds => c =>
        (c.Conn == 2 || c.World.Players.Exists(p => p.Role == (byte)Role.Survivor && p.Id != c.RecipientId && p.Status == (byte)PlayerStatus.Escaped)) &&
        BlockedWorldsBecomeSpectatorWorlds(c);

    public static bool Insert(SurvCtx c, PlayerState? entry = null) { c.World.Players.Insert(0, entry ?? c.LegalEntry); return true; }

    public static bool Edit(SurvCtx c, Func<PlayerState, PlayerState> change)
    {
        if (c.EntryIndex < 0) return false;
        c.World.Players[c.EntryIndex] = change(c.World.Players[c.EntryIndex]);
        return true;
    }

    // The fake's own legal entry exists and the recipient is clear of walls (so the only wrong thing is the mutation).
    public static bool LegalPresent(SurvCtx c) => c.EntryIndex >= 0 && c.Ref == RefClass.Clear && c.Margin > 0.3;

    public static Func<SurvCtx, bool> BlockedEntryOnce() => Once(c => c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3 && Insert(c));
    public static Func<SurvCtx, bool> BlockedEntryEveryTickOfB() => c => c.Conn == 2 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3 && Insert(c);

    // B is blocked and A (a different recipient, same tick) was shown the killer: B gets the entry too.
    public static Func<SurvCtx, bool> CrossRecipientOnce(LosTapAccessor tap) => Once(c =>
        c.Conn == 2 && c.EntryIndex < 0 && c.Margin < -0.3 && tap.Get().EntryAt(1, c.Tick) == true && Insert(c));

    public static Func<SurvCtx, bool> StaleOnce() => Once(c =>
        LegalPresent(c) && c.Prev is { } prev && Math.Abs(prev.X - c.Truth.X) + Math.Abs(prev.Z - c.Truth.Z) >= 0.1 &&
        Edit(c, e => e with { X = prev.X, Z = prev.Z }));

    public static Func<SurvCtx, bool> OffsetOnce(float dx, float dz) => Once(c => LegalPresent(c) && Edit(c, e => e with { X = e.X + dx, Z = e.Z + dz }));
    public static Func<SurvCtx, bool> OffsetAlways(float dx, float dz) => c => LegalPresent(c) && Edit(c, e => e with { X = e.X + dx, Z = e.Z + dz });

    public static Func<SurvCtx, bool> FieldOnce(Func<PlayerState, PlayerState> change) => Once(c => LegalPresent(c) && Edit(c, change));

    public static Func<SurvCtx, bool> SpectatorWithEntryOnce(PlayerStatus status) => Once(c =>
    {
        if (!LegalPresent(c)) return false;
        c.World.Players[c.SelfIndex] = c.Self with { Status = (byte)status };
        return true;
    });

    public static Func<SurvCtx, bool> ExtraOnce(Func<SurvCtx, PlayerState> make) => Once(c => LegalPresent(c) && Insert(c, make(c)));
    public static Func<SurvCtx, bool> DuplicateEntryOnce() => Once(c => LegalPresent(c) && Insert(c, c.World.Players[c.EntryIndex]));

    public static Func<SurvCtx, bool> SkillEffectOnce(int caster) => Once(c =>
    {
        if (!LegalPresent(c)) return false;
        c.After.Add(PacketCodec.Encode(new S2C_SkillEffect { CasterId = caster, SkillId = 1, X = c.Truth.X, Z = c.Truth.Z, Duration = 3 }));
        return true;
    });

    public static Func<SurvCtx, bool> UnknownFrameOnce(int xOffset, int zOffset) => Once(c =>
    {
        if (!LegalPresent(c)) return false;
        var (x, z) = (c.Truth.X, c.Truth.Z);
        c.After.Add(SurvivorTap.Unknown(0x7E, 24, 0x5A, p =>
        {
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(xOffset), x);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(zOffset), z);
        }));
        return true;
    });
}

// Lets a mutation reach the tap that is constructed after it.
internal sealed class LosTapAccessor
{
    public SurvivorTap? Tap;
    public LosTap Get() => Tap!.Tap;
}

internal static class U3dH6Run
{
    public static async Task<(CheckResult Item, SurvivorTap Tap, ScenarioServer Server, TimeSpan Elapsed)> RunAsync(string label,
        Func<LosTapAccessor, Func<SurvCtx, bool>?>? mutation = null, bool expectLos = false, Func<int, TimeSpan>? delay = null,
        bool losServer = true, bool anonymous = false, bool unknownFrames = false, bool openRoad = false)
    {
        await using var server = new ScenarioServer { LineOfSight = losServer, ExecutionGrace = true, AnonymousKillerView = anonymous,
            UnknownFrames = unknownFrames, OpenRoad = openRoad };
        var accessor = new LosTapAccessor();
        var tap = new SurvivorTap(mutation?.Invoke(accessor));
        accessor.Tap = tap;
        await using var proxy = new U3dProxy(server.Port, tap.Rewrite, delay);
        var (item, elapsed) = await H6.RunAsync(proxy.Port, expectLos: expectLos);
        U3dEvidence.Write($"H6/{label}", $"{(item.Passed ? "PASS" : "FAIL")} applied={tap.Applied}/{tap.Seen} {item.Evidence}");
        return (item, tap, server, elapsed);
    }
}

// ---- false passes: each must FAIL H6 with evidence naming the cause --------------------------------------------------------
public abstract class U3dH6Leak
{
    private readonly Func<LosTapAccessor, Func<SurvCtx, bool>> mutation;
    private readonly string[] expected;

    private protected U3dH6Leak(Func<LosTapAccessor, Func<SurvCtx, bool>> mutation, params string[] expected)
    {
        this.mutation = mutation;
        this.expected = expected;
    }

    [Fact]
    public async Task H6FailsWithMatchingEvidence()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync(GetType().Name, a => mutation(a));
        Assert.True(tap.Applied >= 1, $"premise broken: the mutation never applied ({tap.Seen} survivor worlds seen). H6: {item.Evidence}");
        Assert.False(item.Passed, $"FALSE PASS in {GetType().Name} (mutation applied {tap.Applied}x: {string.Join(" | ", tap.Log.Take(3))}): {item.Evidence}");
        Assert.Equal(0, item.Score);
        foreach (string text in expected) Assert.Contains(text, item.Evidence);
    }
}

public sealed class U3dH6BlockedEntryOnceTests() : U3dH6Leak(_ => U3dMut.BlockedEntryOnce(), "Killer 1 leaked to survivor", "is blocked");
public sealed class U3dH6BlockedEntryEveryTickOfBTests() : U3dH6Leak(_ => U3dMut.BlockedEntryEveryTickOfB(), "Killer 1 leaked to survivor 3", "is blocked");
public sealed class U3dH6CrossRecipientOnceTests() : U3dH6Leak(a => U3dMut.CrossRecipientOnce(a), "Killer 1 leaked to survivor 3", "is blocked");
public sealed class U3dH6SpectatorEscapedWithEntryTests() : U3dH6Leak(_ => U3dMut.SpectatorWithEntryOnce(PlayerStatus.Escaped), "Killer 1 leaked to survivor", "spectator");
public sealed class U3dH6SpectatorEliminatedWithEntryTests() : U3dH6Leak(_ => U3dMut.SpectatorWithEntryOnce(PlayerStatus.Eliminated), "Killer 1 leaked to survivor", "spectator");
public sealed class U3dH6StaleOncePositionTests() : U3dH6Leak(_ => U3dMut.StaleOnce(), "Killer 1 leaked to survivor", "(stale)");
public sealed class U3dH6OffByOneSixthMetreOnceTests() : U3dH6Leak(_ => U3dMut.OffsetOnce(0.06f, 0), "Killer 1 leaked to survivor", "m from the killer's own position");
public sealed class U3dH6OffByZOnceTests() : U3dH6Leak(_ => U3dMut.OffsetOnce(0, -0.07f), "Killer 1 leaked to survivor", "m from the killer's own position");
public sealed class U3dH6OffByDiagonalOnceTests() : U3dH6Leak(_ => U3dMut.OffsetOnce(0.04f, 0.04f), "Killer 1 leaked to survivor", "m from the killer's own position"); // 0.0566 m
public sealed class U3dH6FlagsOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { Flags = (byte)PlayerFlags.Signal }), "Killer 1 leaked to survivor", "the contract requires all three 0");
public sealed class U3dH6StatusTimerOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { StatusTimer = 0.25f }), "Killer 1 leaked to survivor", "the contract requires all three 0");
public sealed class U3dH6TinyStatusTimerOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { StatusTimer = 1e-30f }), "Killer 1 leaked to survivor", "the contract requires all three 0");
public sealed class U3dH6CaptureCountOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { CaptureCount = 1 }), "Killer 1 leaked to survivor", "the contract requires all three 0");
public sealed class U3dH6WrongStatusOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { Status = (byte)PlayerStatus.Lagging }), "Killer 1 leaked to survivor", "Status=");
public sealed class U3dH6WrongYawOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { Yaw = e.Yaw + 0.5f }), "Killer 1 leaked to survivor", "Yaw=");
public sealed class U3dH6WrongRoleRightIdOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { Role = (byte)Role.Survivor }), "Killer 1 leaked to survivor", "only the real Id");
public sealed class U3dH6RoleKillerUnderOtherIdOnceTests() : U3dH6Leak(_ => U3dMut.FieldOnce(e => e with { Id = 99 }), "Killer 1 leaked to survivor", "entry Id=99 Role=1");
public sealed class U3dH6ExtraKillerRoleFreshIdBesideLegalEntryTests() : U3dH6Leak(
    _ => U3dMut.ExtraOnce(c => new PlayerState { Id = 99, Role = (byte)Role.Killer, X = c.Truth.X, Z = c.Truth.Z }), "Killer 1 leaked to survivor", "2 killer entries");
public sealed class U3dH6SurvivorShapedFreshIdBesideLegalEntryTests() : U3dH6Leak(
    _ => U3dMut.ExtraOnce(c => new PlayerState { Id = 99, Role = (byte)Role.Survivor, X = c.Truth.X, Z = 7.77f }), "killer coordinate", "bytes leaked");
public sealed class U3dH6SurvivorShapedZeroIdBesideLegalEntryTests() : U3dH6Leak(
    _ => U3dMut.ExtraOnce(c => new PlayerState { Id = 0, Role = (byte)Role.Survivor, X = 3.33f, Z = c.Truth.Z }), "killer coordinate", "bytes leaked");
public sealed class U3dH6DuplicateEntryTests() : U3dH6Leak(_ => U3dMut.DuplicateEntryOnce(), "Killer 1 leaked to survivor", "2 killer entries");
public sealed class U3dH6SkillEffectBesideLegalEntryTests() : U3dH6Leak(_ => U3dMut.SkillEffectOnce(99), "killer coordinate", "S2C_SkillEffect");
public sealed class U3dH6SkillEffectKillerCasterBesideLegalEntryTests() : U3dH6Leak(_ => U3dMut.SkillEffectOnce(1), "killer coordinate", "S2C_SkillEffect");
public sealed class U3dH6UnknownFrameOddOffsetBesideLegalEntryTests() : U3dH6Leak(_ => U3dMut.UnknownFrameOnce(3, 13), "killer coordinate bytes in unknown packet 0x7E");
public sealed class U3dH6UnknownFrameAlignedBesideLegalEntryTests() : U3dH6Leak(_ => U3dMut.UnknownFrameOnce(0, 8), "killer coordinate bytes in unknown packet 0x7E");

// ---- false fails: contract-correct servers must PASS --------------------------------------------------------------------------
internal static class U3dCounts
{
    public static int Of(string evidence, string pattern)
    {
        var m = Regex.Match(evidence, pattern);
        Assert.True(m.Success, $"{pattern} not in {evidence}");
        return int.Parse(m.Groups[1].Value);
    }

    public static (int Visible, int Blocked, int Ambiguous, int Unknown, int ClearAbsent, int Spectator) Parse(string evidence) => (
        Of(evidence, @"(\d+) legal line-of-sight killer entries"), Of(evidence, @"(\d+) blocked-and-absent"),
        Of(evidence, @"(\d+) ambiguous within"), Of(evidence, @"(\d+) undecided"), Of(evidence, @"(\d+) clear-and-absent"),
        Of(evidence, @"(\d+) spectator worlds"));
}

public sealed class U3dH6ContractServerPlainCountsMatchTheReferenceTests
{
    // Correct server, --expect-los OFF. The QA reference tallies every survivor-bound world the proxy forwards; the harness'
    // evidence counts must equal it (the evidence is "present and correct").
    [Fact]
    public async Task PassesAndTheEvidenceCountsEqualTheIndependentTally()
    {
        var (item, tap, server, _) = await U3dH6Run.RunAsync("contract-plain", expectLos: false);
        Assert.True(item.Passed, item.Evidence);
        Assert.Equal(10, item.Score);
        var c = U3dCounts.Parse(item.Evidence);
        int T(string key) => tap.Tally.GetValueOrDefault(key);
        string tally = $"tally visible={T("visible")} blocked-absent={T("blocked-absent")} ambiguous={T("ambiguous")} clear-absent={T("clear-absent")} spectator={T("spectator")}; evidence={c}; server entries sent={server.KillerEntriesSent}";
        U3dEvidence.Write("H6/contract-plain-counts", tally);
        Assert.True(c.Visible > 0 && c.Blocked > 0, tally);
        Assert.Equal(0, c.Unknown);
        // Frames after the harness stops reading are not in the evidence; everything before it is, so allow the 1 s drain slack.
        Assert.InRange(c.Visible, T("visible") - 2, T("visible") + 2);
        Assert.InRange(c.Blocked, T("blocked-absent") - 2, T("blocked-absent") + 2);
        Assert.InRange(c.Ambiguous, T("ambiguous") - 2, T("ambiguous") + 2);
        Assert.InRange(c.ClearAbsent, T("clear-absent") - 2, T("clear-absent") + 2);
        Assert.Equal(0, server.Executions);
    }
}

public sealed class U3dH6ContractServerExpectLosNoSkewTests
{
    [Fact]
    public async Task PassesWithExpectLosAndPassesThroughTheProxyUnchanged()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("contract-expect", expectLos: true);
        Assert.True(item.Passed, item.Evidence);
        Assert.Contains("--expect-los: ", item.Evidence);
        var c = U3dCounts.Parse(item.Evidence);
        Assert.True(c.Visible > 0 && c.Blocked > 0, item.Evidence);
    }
}

// KNOWN LIMIT, not line of sight: with the KILLER's own stream 100 or 200 ms late the scenario's capture-hold step ("killer lost
// contact") fails although the server is contract-correct, because the killer steers from an old position and overshoots A.
// The control (a server without any line-of-sight entries, i.e. the pre-U3d scenario flow) fails the same way, so this predates
// U3d. The assertion pins that, if H6 fails here, the cause is the scenario flow and not the line-of-sight judgement.
public sealed class U3dH6KillerStreamSkewTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task AnyFailureIsTheScenarioFlowNotTheLineOfSightJudgement(int milliseconds)
    {
        Func<int, TimeSpan> delay = conn => conn == 0 ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero;
        var (los, _, _, _) = await U3dH6Run.RunAsync($"skew-killer-{milliseconds}ms", expectLos: true, delay: delay);
        var (plain, _, _, _) = await U3dH6Run.RunAsync($"skew-killer-{milliseconds}ms-control-no-los-server", expectLos: false, losServer: false, delay: delay);
        foreach (var item in new[] { los, plain })
            if (!item.Passed)
            {
                Assert.DoesNotContain("leaked to survivor", item.Evidence);
                Assert.DoesNotContain("--expect-los needs", item.Evidence);
                Assert.DoesNotContain("is blocked", item.Evidence);
                Assert.Contains("[capture", item.Evidence);
            }
        U3dEvidence.Write($"H6/skew-killer-{milliseconds}ms-summary", $"los-server {(los.Passed ? "PASS" : "FAIL")}, no-los control {(plain.Passed ? "PASS" : "FAIL")}");
    }
}

public sealed class U3dH6SurvivorStreamsSkewed200msTests
{
    [Fact]
    public async Task ContractServerPassesWithTheSurvivorStreamsLate()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("skew-survivors-200ms", expectLos: true, delay: conn => conn == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(200));
        Assert.True(item.Passed, item.Evidence);
        Assert.Equal(0, U3dCounts.Parse(item.Evidence).Unknown);
    }
}

public sealed class U3dH6OnlyTheKillerIsEarlyAndASurvivorLateTests
{
    // Skew in opposite directions between the two survivors and the killer: A +200 ms, B 0, killer 0.
    [Fact]
    public async Task ContractServerPassesWithOneSurvivorLate()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("skew-A-200ms", expectLos: true, delay: conn => conn == 1 ? TimeSpan.FromMilliseconds(200) : TimeSpan.Zero);
        Assert.True(item.Passed, item.Evidence);
    }
}

public sealed class U3dH6EntryOffsetInsideEpsilonStillPassesTests
{
    // The contract allows X/Z within 0.05 m of the killer's own position: a 0.04 m offset on every entry is legal.
    [Fact]
    public async Task EntryShiftedBy004mOnEveryTickPasses()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync("offset-0.04-always", _ => U3dMut.OffsetAlways(0.04f, 0));
        Assert.True(tap.Applied > 100, $"premise: {tap.Applied}");
        Assert.True(item.Passed, item.Evidence);
    }
}

// ---- --expect-los semantics --------------------------------------------------------------------------------------------------
public sealed class U3dH6ExpectLosNeedsBlockedAndAbsentTests
{
    // Round 3 rebuild (a2 moved the Tick of every blocked-and-absent frame off the killer's ticks, which is a contract violation
    // since a3: "never delivered to the killer"). Now the tick delivery is untouched: in every blocked-and-absent survivor world
    // the recipient's own entry is turned into an Eliminated one, so the world is a spectator world (no entry, not eligible) and
    // classifies Ineligible, never BlockedAbsent. Visible entries and every tick stay contract-correct, so only the --expect-los
    // requirement can fail: visible > 0 but blocked-and-absent == 0.
    // U6e rebuild: on map v2.1 the killer's capture walk to A passes behind walls, so the mutation also hit A's own stream and H6
    // read A as Eliminated before the capture. The H6 gate itself is private to the harness, so the full run stays: the fake puts
    // the killer, A and every terminal in the wall-free rectangle at the exit's corner (OpenRoad, derived from FixedMap), where A
    // never loses sight of the killer, and only B's blocked worlds (B hides behind a wall in the sight step) are mutated.
    // Post-merge (rules v2 + map v2.1): A's last walk to the far exit is blocked too, so A's worlds after B escaped are mutated as well.
    [Fact]
    public async Task ExpectLosFailsWithoutAnyBlockedAndAbsentTick()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync("expect-no-blocked",
            _ => U3dMut.BlockedWorldsOfBAndOfAAfterBEscapedBecomeSpectatorWorlds,
            expectLos: true, openRoad: true);
        Assert.True(tap.Applied > 50, $"premise: {tap.Applied}");
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("[line of sight (--expect-los)]", item.Evidence);
        var c2 = U3dCounts.Parse(item.Evidence);
        Assert.True(c2.Visible > 0, item.Evidence);
        Assert.Equal(0, c2.Blocked);
    }

    [Fact]
    public async Task WithoutTheFlagTheSameServerPasses()
    {
        var (item, _, _, _) = await U3dH6Run.RunAsync("no-flag-no-blocked",
            _ => U3dMut.BlockedWorldsOfBAndOfAAfterBEscapedBecomeSpectatorWorlds,
            expectLos: false, openRoad: true);
        Assert.True(item.Passed, item.Evidence);
    }
}

// ---- the "unknown tick" probe (a2: flipped to must-fail after the fix for F1) ---------------------------------------------------
// A survivor-bound world whose Tick the killer's own stream never carried cannot be judged. This server is contract-correct
// everywhere except that, for the survivor behind the wall, it renumbers the tick and shows the killer through the wall. Round 1
// pinned the PASS as a gap; since attempt 2 an entry on an unjoinable tick must fail.
public sealed class U3dH6UnjoinableTickLeakTests
{
    [Fact]
    public async Task BlockedLeakWithRenumberedTicksFailsH6()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync("unknown-tick-blocked-leak",
            _ => c => { if (c.Conn == 2 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3) { U3dMut.Insert(c); c.SentTick = c.Tick + 1_000_000; return true; } return false; });
        Assert.True(tap.Applied > 50, $"premise: the through-the-wall entry was sent {tap.Applied}x");
        Assert.False(item.Passed, $"FALSE PASS: {item.Evidence}");
        Assert.Equal(0, item.Score);
        Assert.Contains("never carried", item.Evidence);
    }

    // The same leak with the real killer position: confirms the leak-scan allowance for the entry's own X/Z offsets is what lets
    // the real killer coordinates through (they would be "bytes leaked" without the entry being marked legitimate).
    [Fact]
    public async Task SurvivorShapedLeakOnAnUnjoinableTickIsStillCaughtByTheByteScan()
    {
        var (item, tap, _, _) = await U3dH6Run.RunAsync("unknown-tick-no-entry-byte-leak",
            _ => c => { if (c.Conn == 2 && c.EntryIndex < 0 && c.Eligible && c.Margin < -0.3)
                { c.World.Players.Insert(0, new PlayerState { Id = 98, Role = 0, X = c.Truth.X, Z = c.Truth.Z }); c.SentTick = c.Tick + 1_000_000; return true; } return false; });
        Assert.True(tap.Applied >= 1);
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("bytes leaked", item.Evidence);
    }
}
