using PingTheKiller.Harness;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

// U3d: the harness's own segment-against-wall test. FixedMap wall (18,14)-(20,32) is 2 m thick.
public sealed class LineOfSightGeometryTests
{
    [Fact]
    public void ASegmentThroughAWallIsBlockedByItsFullDepth()
    {
        // Across wall (12,24)-(18,37) from the west road into the 3 m alley east of it: 3 m to the nearest face.
        Assert.Equal(Sight.Blocked, LineOfSight.Classify(10, 30, 20, 30));
        Assert.Equal(-3.0, LineOfSight.Margin(10, 30, 20, 30), 9);
        Assert.True(LineOfSight.Blocked(10, 30, 20, 30));
        // An endpoint inside the wall is blocked too.
        Assert.Equal(Sight.Blocked, LineOfSight.Classify(15, 30, 10, 30));
    }

    [Fact]
    public void TouchingAnEdgeIsBlockedByContractButInsideTheAmbiguousBand()
    {
        // Along the face z = 24 of wall (12,24)-(18,37), and ending exactly on it from the alley below.
        foreach (var (ax, az, bx, bz) in new[] { (10.0, 24.0, 20.0, 24.0), (15.0, 22.0, 15.0, 24.0) })
        {
            Assert.True(LineOfSight.Blocked(ax, az, bx, bz));
            Assert.Equal(0.0, LineOfSight.Margin(ax, az, bx, bz), 9);
            Assert.Equal(Sight.Ambiguous, LineOfSight.Classify(ax, az, bx, bz));
        }
    }

    [Fact]
    public void TouchingACornerIsBlockedByContractButInsideTheAmbiguousBand()
    {
        // Through the corner (12,24) only: before it x < 12, after it z < 24.
        Assert.True(LineOfSight.Blocked(11, 25, 13, 23));
        Assert.Equal(0.0, LineOfSight.Margin(11, 25, 13, 23), 9);
        Assert.Equal(Sight.Ambiguous, LineOfSight.Classify(11, 25, 13, 23));
    }

    [Fact]
    public void AFullyClearSegmentReportsItsClearance()
    {
        // Killer spawn (5,5) to survivor spawn (5,26): 5 m from the map's boundary walls, 13 m from the nearest inner wall.
        Assert.False(LineOfSight.Blocked(5, 5, 5, 26));
        Assert.Equal(5.0, LineOfSight.Margin(5, 5, 5, 26), 9);
        Assert.Equal(Sight.Clear, LineOfSight.Classify(5, 5, 5, 26));
        Assert.Equal(Sight.Clear, LineOfSight.Classify(5, 5, 5, 5)); // zero length, in the open
    }

    [Theory]
    [InlineData(23.97f, Sight.Ambiguous)] // misses the face z = 24 by 0.03 m
    [InlineData(23.94f, Sight.Clear)]     // misses by 0.06 m
    [InlineData(24.03f, Sight.Ambiguous)] // penetrates by 0.03 m
    [InlineData(24.06f, Sight.Blocked)]   // penetrates by 0.06 m
    public void TheBandIsFiveCentimetresEitherSideOfAFace(float z, Sight expected)
    {
        Assert.Equal(expected, LineOfSight.Classify(10, z, 20, z));
        Assert.Equal(z >= 24, LineOfSight.Blocked(10, z, 20, z));
    }

    [Theory]
    [InlineData(0.03, Sight.Ambiguous)]
    [InlineData(0.07, Sight.Clear)]
    public void ANearMissPastACornerIsJudgedByItsDistance(double gap, Sight expected)
    {
        // A diagonal passing the corner (12,24) on its outside at distance `gap`.
        double offset = gap * Math.Sqrt(2);
        Assert.Equal(expected, LineOfSight.Classify(10 - offset, 26, 14 - offset, 22));
        Assert.Equal(gap, LineOfSight.Margin(10 - offset, 26, 14 - offset, 22), 6);
    }

    [Fact]
    public void ACustomWallListIsUsedWhenGiven()
    {
        var walls = new[] { new Wall(0, 0, 1, 1) };
        Assert.Equal(Sight.Blocked, LineOfSight.Classify(-1, 0.5, 2, 0.5, walls));
        Assert.Equal(Sight.Clear, LineOfSight.Classify(10, 20, 30, 20, walls)); // the map wall is not in this list
    }
}

public sealed class KillerSightJudgeTests
{
    private const int KillerId = 1, Me = 2, Other = 3;
    private static readonly PlayerState Killer = new() { Id = KillerId, Role = (byte)Role.Killer, X = 5.25f, Z = 5.5f, Yaw = 0.7f, Flags = 1 };
    private static readonly PlayerState Entry = new() { Id = KillerId, Role = (byte)Role.Killer, X = Killer.X, Z = Killer.Z, Yaw = Killer.Yaw };
    private static readonly PlayerState InOpen = new() { Id = Me, X = 5.125f, Z = 26.375f };
    private static readonly PlayerState BehindWall = new() { Id = Me, X = 22.5f, Z = 20.25f };
    private static readonly PlayerState Mate = new() { Id = Other, X = 7.5f, Z = 25.5f };

    private static S2C_WorldState W(params PlayerState[] players) => new() { Tick = 40, Players = players.ToList() };

    private static SightVerdict Judge(S2C_WorldState world, PlayerState? killer = null, PlayerState? previous = null) =>
        KillerSight.Judge(world, Me, KillerId, killer, previous);

    [Fact]
    public void TheContractEntryWithClearSightIsLegalAndNamesItsIndex()
    {
        var verdict = Judge(W(Mate, Entry, InOpen), Killer);
        Assert.Null(verdict.Leak);
        Assert.Equal(SightOutcome.Visible, verdict.Outcome);
        Assert.Equal(1, verdict.Entry);
        // A Lagging recipient may see the killer too; the killer's own Flags (Signal) are not copied into the entry.
        Assert.Equal(SightOutcome.Visible, Judge(W(InOpen with { Status = (byte)PlayerStatus.Lagging }, Entry), Killer).Outcome);
        // Within 0.05 m of the killer's own position.
        Assert.Null(Judge(W(InOpen, Entry with { X = Killer.X + 0.04f }), Killer).Leak);
    }

    [Fact]
    public void AbsentEntriesAreCountedByTheirSight()
    {
        Assert.Equal(SightOutcome.ClearAbsent, Judge(W(InOpen, Mate), Killer).Outcome);
        Assert.Equal(SightOutcome.BlockedAbsent, Judge(W(BehindWall, Mate), Killer).Outcome);
        Assert.Equal(SightOutcome.Unknown, Judge(W(BehindWall, Mate)).Outcome);
        Assert.Equal(SightOutcome.Ineligible, Judge(W(InOpen with { Status = (byte)PlayerStatus.Escaped }), Killer).Outcome);
        Assert.Equal(SightOutcome.Unknown, Judge(W(Mate), Killer).Outcome); // no own entry
    }

    [Fact]
    public void TheEntryWhileBlockedIsALeak()
    {
        string? leak = Judge(W(BehindWall, Entry), Killer).Leak;
        Assert.Contains("Killer 1 leaked to survivor 2 at tick=40", leak);
        Assert.Contains("is blocked", leak);
    }

    [Theory]
    [InlineData(PlayerStatus.Escaped)]
    [InlineData(PlayerStatus.Eliminated)]
    public void SpectatorsNeverSeeTheKillerEvenWithoutAKillerTick(PlayerStatus status)
    {
        var world = W(InOpen with { Status = (byte)status }, Entry);
        Assert.Contains("spectator", Judge(world, Killer).Leak);
        Assert.Contains("spectator", Judge(world).Leak);
    }

    [Fact]
    public void NonZeroFlagsStatusTimerOrCaptureCountAreLeaks()
    {
        foreach (var bad in new[] { Entry with { Flags = 1 }, Entry with { StatusTimer = 1.5f }, Entry with { StatusTimer = -0f },
                     Entry with { CaptureCount = 1 } })
        {
            Assert.Contains("the contract requires all three 0", Judge(W(InOpen, bad), Killer).Leak);
            Assert.Contains("the contract requires all three 0", Judge(W(InOpen, bad)).Leak); // no truth needed
        }
    }

    [Fact]
    public void AStalePositionIsALeakAndNamedAsSuch()
    {
        var previous = Killer with { X = Killer.X - 0.23f };
        string? leak = Judge(W(InOpen, Entry with { X = previous.X }), Killer, previous).Leak;
        Assert.Contains("0.230 m from the killer's own position", leak);
        Assert.Contains("(stale)", leak);
        Assert.Contains("m from the killer's own position", Judge(W(InOpen, Entry with { Z = Killer.Z + 0.06f }), Killer).Leak);
    }

    [Fact]
    public void YawStatusIdRoleAndDuplicatesMustMatchTheContract()
    {
        Assert.Contains("Yaw=", Judge(W(InOpen, Entry with { Yaw = 1.2f }), Killer).Leak);
        Assert.Null(Judge(W(InOpen, Entry with { Yaw = Killer.Yaw + 2 * MathF.PI }), Killer).Leak); // same direction
        Assert.Contains("Status=", Judge(W(InOpen, Entry with { Status = 1 }), Killer).Leak);
        Assert.Contains("entry Id=99 Role=1", Judge(W(InOpen, Entry with { Id = 99 }), Killer).Leak);
        Assert.Contains("entry Id=1 Role=0", Judge(W(InOpen, Entry with { Role = 0 }), Killer).Leak);
        Assert.Contains("2 killer entries", Judge(W(InOpen, Entry, Entry), Killer).Leak);
        Assert.Contains("own entry is missing", Judge(W(Mate, Entry), Killer).Leak);
    }

    [Fact]
    public void TheAmbiguousBandIsNotJudgedButFieldsStillAre()
    {
        // Killer and recipient on a line 0.03 m outside the face z = 24 of wall (12,24)-(18,37).
        var killer = Killer with { X = 10, Z = 23.97f };
        var me = InOpen with { X = 20, Z = 23.97f };
        var entry = Entry with { X = killer.X, Z = killer.Z };
        var verdict = Judge(W(me, entry), killer);
        Assert.Equal((SightOutcome.Ambiguous, 1, (string?)null), (verdict.Outcome, verdict.Entry, verdict.Leak));
        Assert.Equal(SightOutcome.Ambiguous, Judge(W(me), killer).Outcome);
        Assert.NotNull(Judge(W(me, entry with { Flags = 1 }), killer).Leak);
        Assert.NotNull(Judge(W(me, entry with { X = 10.2f }), killer).Leak);
        // 0.06 m inside the wall is blocked.
        Assert.Contains("is blocked", Judge(W(me with { Z = 24.06f }, entry with { Z = 24.06f }), killer with { Z = 24.06f }).Leak);
    }

    [Fact]
    public void WithoutTheKillerTickACorrectlyShapedEntryIsUndecided()
    {
        var verdict = Judge(W(InOpen, Entry));
        Assert.Equal((SightOutcome.Unknown, 1, (string?)null), (verdict.Outcome, verdict.Entry, verdict.Leak));
    }
}

public sealed class SightJoinTests
{
    private const int KillerId = 1;
    private static readonly PlayerState Killer = new() { Id = KillerId, Role = (byte)Role.Killer, X = 5.25f, Z = 5.5f };
    private static readonly PlayerState Entry = Killer with { Flags = 0 };
    private static readonly PlayerState InOpen = new() { Id = 2, X = 5.125f, Z = 26.375f };
    private static readonly PlayerState BehindWall = new() { Id = 3, X = 22.5f, Z = 20.25f };

    private static S2C_WorldState W(uint tick, params PlayerState[] players) => new() { Tick = tick, Players = players.ToList() };

    [Fact]
    public void SurvivorWorldsWaitForTheirKillerTickInEitherOrder()
    {
        var join = new SightJoin(KillerId);
        Assert.Null(join.AddSurvivor(2, W(7, InOpen, Entry)));   // survivor first: pending
        join.AddKiller(W(7, Killer, InOpen with { Id = 0 }));    // resolves tick 7
        join.AddKiller(W(8, Killer));
        Assert.Equal(SightOutcome.Visible, join.AddSurvivor(2, W(8, InOpen, Entry))!.Value.Outcome); // killer first: at once
        Assert.Equal(SightOutcome.BlockedAbsent, join.AddSurvivor(3, W(8, BehindWall))!.Value.Outcome);
        join.Finish(); // everything joined
        Assert.Equal((2, 1, 0, 3), (join.Counts.Visible, join.Counts.BlockedAbsent, join.Counts[SightOutcome.Unknown], join.Counts.Total));
        Assert.Contains("2 legal line-of-sight killer entries, 1 blocked-and-absent", join.Counts.ToString());
        Assert.DoesNotContain("skipped", join.Counts.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABlockedEntryFailsWhenItsKillerTickArrives()
    {
        var join = new SightJoin(KillerId);
        Assert.Null(join.AddSurvivor(3, W(7, BehindWall, Entry)));
        var failure = Assert.Throws<CheckFailure>(() => join.AddKiller(W(7, Killer)));
        Assert.Contains("Killer 1 leaked to survivor 3 at tick=7", failure.Message);
    }

    [Fact]
    public void LeaksThatNeedNoKillerPositionFailAtOnce()
    {
        var join = new SightJoin(KillerId);
        Assert.Throws<CheckFailure>(() => join.AddSurvivor(2, W(7, InOpen, Entry with { CaptureCount = 1 })));
        Assert.Throws<CheckFailure>(() => join.AddSurvivor(2, W(7, InOpen with { Status = (byte)PlayerStatus.Eliminated }, Entry)));
    }

    [Fact]
    public void TheFinalJudgementSeesThePreviousTickForStaleEvidence()
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(7, Killer));
        join.AddKiller(W(8, Killer with { X = Killer.X + 0.23f }));
        var stale = join.Judge(2, W(8, InOpen, Entry));
        Assert.Contains("(stale)", stale.Leak);
        Assert.Equal(0, join.Counts.Total); // Judge does not count
    }

    // U3d a3: no tolerance. Every survivor world must join a killer tick; one that never does fails, with or without an entry,
    // wherever it sits (QA F6: a tick-0 label is not an escape).
    [Theory]
    [InlineData(11u)]  // inside the killer's range
    [InlineData(0u)]   // QA F6: a tick-0 label
    [InlineData(19u)]  // past the last killer tick
    public void AnEntryOnATickTheKillerNeverReceivedFails(uint tick)
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(10, Killer));
        join.AddKiller(W(12, Killer));
        Assert.Null(join.AddSurvivor(3, W(tick, BehindWall, Entry)));
        var failure = Assert.Throws<CheckFailure>(() => join.Finish());
        Assert.Contains($"Killer 1 leaked to survivor 3 at tick={tick}: survivor tick {tick} never delivered to the killer", failure.Message);
        Assert.Contains("the killer's own stream never carried it; latest killer tick 12", failure.Message);
    }

    [Fact]
    public void AWorldWithoutAnEntryOnAnUnjoinedTickFailsToo()
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(10, Killer));
        Assert.Null(join.AddSurvivor(2, W(11, InOpen)));
        Assert.Null(join.AddSurvivor(3, W(11, BehindWall)));
        var failure = Assert.Throws<CheckFailure>(() => join.Finish());
        Assert.StartsWith("survivor tick 11 never delivered to the killer: survivor 2's WorldState tick=11", failure.Message);
        Assert.Contains("2 such survivor world(s); a correct server sends every tick to every human", failure.Message);
        Assert.DoesNotContain("leaked", failure.Message);
    }

    [Fact]
    public void AnEntryBearingWorldIsReportedBeforeAnEarlierWorldWithoutOne()
    {
        var join = new SightJoin(KillerId);
        Assert.Null(join.AddSurvivor(2, W(5, InOpen)));
        Assert.Null(join.AddSurvivor(3, W(8, BehindWall, Entry)));
        var failure = Assert.Throws<CheckFailure>(() => join.Finish());
        Assert.StartsWith("Killer 1 leaked to survivor 3 at tick=8: survivor tick 8 never delivered", failure.Message);
        Assert.Contains("latest killer tick none", failure.Message);
    }

    [Fact]
    public void AwaitingKillerHoldsOnlyWhileAPendingTickIsNewerThanEveryKillerTick()
    {
        var join = new SightJoin(KillerId);
        Assert.False(join.AwaitingKiller);
        Assert.Null(join.AddSurvivor(2, W(7, InOpen)));
        Assert.True(join.AwaitingKiller);               // no killer tick yet
        join.AddKiller(W(6, Killer));
        Assert.True(join.AwaitingKiller);               // 7 may still come
        join.AddKiller(W(8, Killer));
        Assert.False(join.AwaitingKiller);              // the killer stream passed 7 without it: 7 can never join
        Assert.Throws<CheckFailure>(() => join.Finish());
        join.AddKiller(W(7, Killer));                   // a late (reordered) tick 7 still resolves it
        join.Finish();
    }

    [Fact]
    public void RetainedKillerWorldsSeedTheJoin()
    {
        var join = new SightJoin(KillerId);
        foreach (uint tick in new uint[] { 3, 4, 5 }) join.SeedKiller(W(tick, Killer));
        Assert.Equal(SightOutcome.Visible, join.AddSurvivor(2, W(4, InOpen, Entry))!.Value.Outcome);
        join.Finish();
    }

    [Fact]
    public void OnlyVisibleOrAmbiguousVerdictsAllowTheEntrysOffsets()
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(7, Killer));
        var undecided = join.Judge(2, W(8, InOpen, Entry)); // pure re-judgement only; a window with it pending fails in Finish
        Assert.Equal((SightOutcome.Unknown, 1, -1), (undecided.Outcome, undecided.Entry, undecided.AllowedEntry));
        var visible = join.Judge(2, W(7, InOpen, Entry));
        Assert.Equal((SightOutcome.Visible, 1, 1), (visible.Outcome, visible.Entry, visible.AllowedEntry));
        Assert.Equal(-1, join.Judge(3, W(7, BehindWall, Entry)).AllowedEntry); // a leak
        Assert.Equal(1, new SightVerdict(SightOutcome.Ambiguous, 1, null).AllowedEntry);
    }

    [Fact]
    public void TheKillersOwnEntryNeedsItsRealIdAndRole()
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(7, Killer with { Role = (byte)Role.Survivor }));
        Assert.Null(join.AddSurvivor(2, W(7, InOpen))); // no truth recorded for tick 7
        Assert.False(join.AwaitingKiller);               // tick 7 arrived, so waiting cannot help
        Assert.Contains("survivor tick 7 cannot be joined: the killer's WorldState tick=7 has no unique own entry",
            Assert.Throws<CheckFailure>(() => join.Finish()).Message);
        Assert.Null(KillerSight.Own(W(1, Killer, Killer), KillerId));
    }
}

public sealed class LegalKillerEntryLeakScanTests
{
    private const int KillerId = 1, A = 2, B = 3;
    private const float KillerX = 13.37187f, KillerZ = 27.81243f;
    private static readonly HashSet<int> Bits = new() { KillerLeakScan.Bits(KillerX), KillerLeakScan.Bits(KillerZ) };
    private static readonly PlayerState Entry = new() { Id = KillerId, Role = (byte)Role.Killer, X = KillerX, Z = KillerZ, Yaw = 0.25f };

    private static ReceivedFrame Frame(IPacket packet) => new(packet, PacketCodec.Encode(packet), 0);
    private static S2C_WorldState W(params PlayerState[] players) => new() { Tick = 9, TimeLeft = 500, Players = players.ToList() };

    [Fact]
    public void OnlyTheLegalEntrysOwnXZOffsetsAreAllowed()
    {
        var world = Frame(W(new PlayerState { Id = A, X = 1, Z = 2 }, Entry));
        Assert.Null(KillerLeakScan.Excess(world, Bits, new[] { A, B }, legalKillerEntry: 1));
        Assert.Contains("bytes leaked", KillerLeakScan.Excess(world, Bits, new[] { A, B }));            // not judged legal
        Assert.Contains("bytes leaked", KillerLeakScan.Excess(world, Bits, new[] { A, B }, legalKillerEntry: 0)); // wrong index
    }

    [Fact]
    public void AnyOtherCopyInTheSameFrameIsStillALeak()
    {
        var extra = Frame(W(Entry, new PlayerState { Id = 99, X = KillerX, Z = 3 }));
        Assert.Contains("2 occurrence(s)", KillerLeakScan.Excess(extra, Bits, new[] { A, B }, legalKillerEntry: 0));
        var inYaw = Frame(W(Entry with { Yaw = KillerZ }));
        Assert.Contains("bytes leaked", KillerLeakScan.Excess(inYaw, Bits, new[] { A, B }, legalKillerEntry: 0));
        var inTimer = Frame(W(Entry, new PlayerState { Id = 98, StatusTimer = KillerX }));
        Assert.NotNull(KillerLeakScan.Excess(inTimer, Bits, new[] { A, B }, legalKillerEntry: 0));
        // A SkillEffect is its own frame: the legal world does not excuse it.
        Assert.Contains("S2C_SkillEffect", KillerLeakScan.Excess(Frame(new S2C_SkillEffect { CasterId = KillerId, X = KillerX, Z = KillerZ }),
            Bits, new[] { A, B }, legalKillerEntry: 0));
    }
}

public sealed class ExpectLosOptionTests
{
    [Fact]
    public void ExpectLosIsAValuelessFlagAnywhereOnTheLine()
    {
        Assert.False(new Options().ExpectLos);
        Assert.False(Options.Parse(new[] { "--port", "7" }).ExpectLos);
        var parsed = Options.Parse(new[] { "--port", "7", "--expect-los", "--only", "H6" });
        Assert.True(parsed.ExpectLos);
        Assert.Equal(7, parsed.Port);
        Assert.True(parsed.Only.SetEquals(new[] { "H6" }));
        Assert.True(Options.Parse(new[] { "--expect-los" }).ExpectLos);
        Assert.True(Options.Parse(new[] { "--h6-port", "8", "--expect-los" }).ForH6().ExpectLos);
    }
}

// U3d a3: per-stream WorldState tick integrity (strictly increasing, gaps of at most 2).
public sealed class TickSequenceTests
{
    [Fact]
    public void StepsOfOneOrTwoPass()
    {
        var ticks = new TickSequence("killer");
        foreach (uint tick in new uint[] { 5, 6, 8, 9, 11 }) ticks.Add(tick);
        TickSequence.Require(ticks);
        Assert.Equal("killer 5..11 (5)", ticks.ToString());
        Assert.Equal(11u, ticks.Last);
    }

    [Theory]
    [InlineData(812u, 0u)]  // QA F6: a tick-0 relabel
    [InlineData(10u, 10u)]  // duplicate
    [InlineData(10u, 9u)]   // backwards
    [InlineData(10u, 13u)]  // gap of 3
    public void AViolationFailsAndNamesTheStream(uint previous, uint next)
    {
        var ticks = new TickSequence("survivor 2");
        ticks.Add(previous);
        ticks.Add(next);
        ticks.Add(next + 1);
        var failure = Assert.Throws<CheckFailure>(() => TickSequence.Require(new TickSequence("killer"), ticks));
        Assert.Equal($"tick sequence violated in survivor 2 stream: {previous} -> {next} (WorldState ticks must increase by 1 or 2).", failure.Message);
    }

    [Fact]
    public void TheFirstViolationIsKept()
    {
        var ticks = new TickSequence("killer");
        foreach (uint tick in new uint[] { 1, 5, 0 }) ticks.Add(tick);
        Assert.Contains("1 -> 5", Assert.Throws<CheckFailure>(() => TickSequence.Require(ticks)).Message);
    }
}
