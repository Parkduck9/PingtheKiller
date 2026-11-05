using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.QA;
using Xunit;

namespace PingTheKiller.Tests.Core;

// GDD v2 rules R1-R9 (U6a): co-hack rates, kick and regression, sync checks, switches, collapse, hatch, capture stages,
// winner and end reasons.
public sealed class RulesV2Tests
{
    private const float Dt = 1f / GameConstants.TickRate;
    private const double HackPerTick = Dt * 100.0 / GameConstants.TerminalHackTimeSeconds;
    // Parking spot for a bystander survivor: open street east of the (42,16)-(53,30) block on map v2.1, >= 8 m from
    // every terminal and > 20 m from every hatch point, exit and switch. (Map v2's (48.5, 25) is inside that block now.)
    private const float ParkX = 54f, ParkZ = 20f;

    private static Player P(RulesMatch m, Peer peer) => (Player)Spy.Player(m.F.Server, peer.Id);
    private static double Now(RulesMatch m) => Spy.Server<double>(m.F.Server, "_now");
    private static void Hold(Peer peer, bool on) => peer.Send(new C2S_Interact { Active = (byte)(on ? 1 : 0) });
    private static bool Has(TerminalState t, TerminalFlags flag) => (t.Flags & (byte)flag) != 0;
    private static S2C_WorldState World(Peer peer) => peer.Last<S2C_WorldState>();

    private static List<T> Fresh<T>(Peer peer) where T : IPacket
    {
        peer.Drain();
        var list = peer.Packets.OfType<T>().ToList();
        peer.Packets.RemoveAll(p => p is T);
        return list;
    }

    // A standable point around (cx, cz) that satisfies the condition, searched outward on rings.
    private static (float X, float Z) FindPoint(FixedMap map, float cx, float cz, Func<float, float, bool> ok)
    {
        for (float r = 0.25f; r <= 25; r += 0.25f)
            for (int a = 0; a < 96; a++)
            {
                float x = cx + r * MathF.Cos(a * MathF.PI / 48), z = cz + r * MathF.Sin(a * MathF.PI / 48);
                if (x > 0.5f && z > 0.5f && x < 79.5f && z < 79.5f && map.CanStand(x, z) && ok(x, z)) return (x, z);
            }
        throw new InvalidOperationException($"no point around ({cx},{cz})");
    }

    private static float Distance(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    // ---- R1 / R2: hacking rates

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.8)]
    [InlineData(3, 2.4)]
    [InlineData(4, 2.4)]
    [InlineData(7, 2.4)]
    public void CoHackRatesAreOneOnePointEightAndTwoPointFourAtMost(int hackers, double rate)
    {
        Assert.Equal(rate, GameConstants.CoHackRate(hackers), 6);
        Assert.Equal(45f, GameConstants.TerminalHackTimeSeconds);
        Assert.Equal(900f, GameConstants.MatchLengthSeconds);
    }

    // ---- R2: kick and regression

    [Fact]
    public void KickTakesFivePointsAfterTwoSecondsThenRegressesUntilASurvivorHacksAgain()
    {
        using var m = RulesMatch.Start(1);
        var srv = m.F.Server; var s = m[0]; var t = srv.Map.TerminalPositions[0];
        srv.PlaceForTests(s.Id, 48.5f, 60);
        srv.PlaceForTests(m.Killer.Id, t.X + .5f, t.Z);
        srv.SetTerminalProgressForTests(0, 50);
        m.Tick();
        Hold(m.Killer, true);
        int n = 0;
        while (srv.TerminalProgressForTests(0) >= 50 && n < 100)
        {
            m.Tick(); n++;
            if (n == 10) Assert.Equal((byte)ActionKind.Kick, m.Killer.Last<S2C_SelfState>().ActionKind);
        }
        Assert.InRange(n, 39, 41); // 2.0 s
        Assert.InRange(srv.TerminalProgressForTests(0), 44.9, 45.0);
        Assert.True(Has(World(s).Terminals[0], TerminalFlags.Regressing));
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Regressing)); // survivors only
        Assert.Equal(0, World(m.Killer).Terminals[0].Progress);

        // A regressing terminal cannot be kicked again: holding on only shows the 0.5 points/s regression.
        double before = srv.TerminalProgressForTests(0);
        m.Tick(60);
        Assert.InRange(before - srv.TerminalProgressForTests(0), 1.45, 1.55);
        Hold(m.Killer, false);
        m.Tick(40);
        Assert.InRange(before - srv.TerminalProgressForTests(0), 2.45, 2.55);

        // A survivor starting to hack clears the regression and progress rises again.
        srv.PlaceForTests(s.Id, t.X - .5f, t.Z);
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        Hold(s, true);
        m.Tick();
        Assert.False(Has(World(s).Terminals[0], TerminalFlags.Regressing));
        double resumed = srv.TerminalProgressForTests(0);
        Hold(s, false);
        m.Tick(40);
        Assert.Equal(resumed, srv.TerminalProgressForTests(0), 6); // no hacker, no regression
    }

    [Fact]
    public void MovingCancelsTheKickAndOnlyTerminalsBetweenZeroAndHundredCanBeKicked()
    {
        using var m = RulesMatch.Start(1);
        var srv = m.F.Server; var t = srv.Map.TerminalPositions[0];
        srv.PlaceForTests(m[0].Id, 48.5f, 60);
        srv.PlaceForTests(m.Killer.Id, t.X + .5f, t.Z);
        srv.SetTerminalProgressForTests(0, 50);
        m.Tick();
        Hold(m.Killer, true);
        m.Tick(35);
        m.Step(m.Killer, 0, 1); // one tick of movement resets the hold; Interact stays held
        int n = 0;
        while (srv.TerminalProgressForTests(0) >= 50 && n < 100) { m.Tick(); n++; }
        Assert.InRange(n, 39, 42); // a full 2 s again after the move (the stop input lands on the first tick)

        // Releasing Interact cancels too.
        srv.SetTerminalProgressForTests(3, 30);
        var t3 = srv.Map.TerminalPositions[3];
        srv.PlaceForTests(m.Killer.Id, t3.X + .5f, t3.Z);
        m.Tick(30);
        Hold(m.Killer, false);
        m.Tick();
        Hold(m.Killer, true);
        m.Tick(30);
        Assert.Equal(30, srv.TerminalProgressForTests(3), 6);

        // 0 and 100 are not kickable.
        srv.SetTerminalProgressForTests(3, 0);
        srv.SetTerminalProgressForTests(2, 100);
        m.Tick(60);
        Assert.Equal(0, srv.TerminalProgressForTests(3), 6);
        Assert.False(Has(World(m[0]).Terminals[3], TerminalFlags.Regressing));
        var t2 = srv.Map.TerminalPositions[2];
        srv.PlaceForTests(m.Killer.Id, t2.X + .5f, t2.Z);
        m.Tick(60);
        Assert.Equal(100, World(m.Killer).Terminals[2].Progress);
        Assert.False(Has(World(m[0]).Terminals[2], TerminalFlags.Regressing));
    }

    // ---- R3: sync checks

    // One survivor hacks terminal 0 from 50 % with sync checks on; the next check fires on the next tick.
    private static (RulesMatch M, Peer S, S2C_SyncCheck Check) StartCheck()
    {
        var m = RulesMatch.Start(1);
        var srv = m.F.Server; var s = m[0]; var t = srv.Map.TerminalPositions[0];
        srv.SyncChecksEnabled = true;
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        srv.PlaceForTests(s.Id, t.X - .5f, t.Z);
        srv.SetTerminalProgressForTests(0, 50);
        Hold(s, true);
        m.Tick();
        Assert.Empty(Fresh<S2C_SyncCheck>(s));
        srv.SetSyncTimerForTests(s.Id, 0.01);
        m.Tick();
        var check = Assert.Single(Fresh<S2C_SyncCheck>(s));
        Assert.Equal(World(s).Terminals[0].Id, check.TerminalId);
        Assert.Equal((GameConstants.SyncCheckDurationSeconds, GameConstants.SyncZoneSize, GameConstants.SyncGreatSize),
            (check.Duration, check.ZoneSize, check.GreatSize));
        Assert.InRange(check.ZoneStart, GameConstants.SyncZoneStartMin, GameConstants.SyncZoneStartMax);
        return (m, s, check);
    }

    // Waits until the press arrives at least `late` seconds after the target moment, then sets the RTT so that the
    // judge's estimate (arrival - rtt) is exactly the target, and presses. Returns the progress change of the press tick.
    // The fixture clock stands still, so the press is timed at the start of the tick that handles it.
    private static (S2C_SyncResult Result, double Change) PressAt(RulesMatch m, Peer s, S2C_SyncCheck check, double fraction, double late = 0.06)
    {
        var srv = m.F.Server;
        double target = P(m, s).SyncSentAt + fraction * GameConstants.SyncCheckDurationSeconds;
        while (Now(m) < target + late) m.Tick();
        srv.SetRttForTests(s.Id, Now(m) - target);
        double before = srv.TerminalProgressForTests(0);
        s.Send(new C2S_SyncPress { CheckId = check.CheckId });
        m.Tick();
        var result = Assert.Single(Fresh<S2C_SyncResult>(s));
        Assert.Equal(check.CheckId, result.CheckId);
        return (result, srv.TerminalProgressForTests(0) - before);
    }

    [Fact]
    public void GreatPressAddsAPointAndTheRttOffsetMovesALateArrivalBackIntoTheGreatPart()
    {
        var (m, s, check) = StartCheck();
        using var _ = m;
        // Arrival is after the whole zone and its tolerance; only the RTT estimate puts the press in the great part.
        double late = (check.ZoneSize + GameConstants.SyncToleranceSeconds / GameConstants.SyncCheckDurationSeconds) *
            GameConstants.SyncCheckDurationSeconds + 0.02;
        var (result, change) = PressAt(m, s, check, check.ZoneStart + check.GreatSize / 2, late);
        Assert.Equal((byte)SyncResult.Great, result.Result);
        Assert.InRange(change, HackPerTick + 1 - 0.001, HackPerTick + 1 + 0.001);
        Assert.False(Has(World(s).Terminals[0], TerminalFlags.Overloaded));
        Assert.InRange(P(m, s).SyncTimer, GameConstants.SyncCheckIntervalMinSeconds - Dt, GameConstants.SyncCheckIntervalMaxSeconds);
    }

    [Theory]
    [InlineData(0.5, (byte)SyncResult.Good)]          // the middle of the zone after the great part
    [InlineData(-0.99, (byte)SyncResult.Good)]        // just inside the leading tolerance
    [InlineData(1.99, (byte)SyncResult.Good)]         // just inside the trailing tolerance
    [InlineData(-1.01, (byte)SyncResult.Fail)]        // just outside the leading tolerance
    [InlineData(2.01, (byte)SyncResult.Fail)]         // just outside the trailing tolerance
    public void PressesAreJudgedWithTheToleranceOnBothEdges(double where, byte expected)
    {
        var (m, s, check) = StartCheck();
        using var _ = m;
        double tol = GameConstants.SyncToleranceSeconds / GameConstants.SyncCheckDurationSeconds;
        // where: 0.5 = middle of the non-great zone; -1 = leading tolerance edge; 2 = trailing tolerance edge (scaled).
        double fraction = where == 0.5 ? check.ZoneStart + (check.GreatSize + check.ZoneSize) / 2
            : where < 0 ? check.ZoneStart - tol * -where
            : check.ZoneStart + check.ZoneSize + tol * (where - 1);
        var (result, change) = PressAt(m, s, check, fraction);
        Assert.Equal(expected, result.Result);
        var survivorFlags = World(s).Terminals[0];
        if (expected == (byte)SyncResult.Good)
        {
            Assert.InRange(change, HackPerTick - 0.001, HackPerTick + 0.001);
            Assert.False(Has(survivorFlags, TerminalFlags.Overloaded));
        }
        else
        {
            Assert.InRange(change, HackPerTick - 5 - 0.001, HackPerTick - 5 + 0.001);
            Assert.True(Has(survivorFlags, TerminalFlags.Overloaded));
            Assert.True(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded)); // everyone sees Overloaded
            m.Tick(41);
            Assert.False(Has(World(s).Terminals[0], TerminalFlags.Overloaded));
        }
    }

    [Fact]
    public void AnUnansweredCheckFailsAfterDurationPlusToleranceAndTheRtt()
    {
        var (m, s, check) = StartCheck();
        using var _ = m;
        m.F.Server.SetRttForTests(s.Id, 0.2);
        int n = 0;
        List<S2C_SyncResult> results;
        while ((results = Fresh<S2C_SyncResult>(s)).Count == 0 && n < 60) { m.Tick(); n++; }
        // The check left at the start of its tick; 1.1 + 0.15 + 0.2 = 1.45 s = 29 ticks later a tick starts past the
        // limit, after every press made in time has arrived.
        Assert.InRange(n, 28, 30);
        Assert.Equal((byte)SyncResult.Fail, Assert.Single(results).Result);
        Assert.True(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));
        // Hacked since StartCheck: its two ticks plus the n waiting ticks.
        Assert.InRange(m.F.Server.TerminalProgressForTests(0), 50 + (n + 2) * HackPerTick - 5 - 0.01, 50 + (n + 2) * HackPerTick - 5 + 0.01);
    }

    [Fact]
    public void StoppingTheHackCancelsTheCheckSilentlyAndStaleUnknownRepeatedOrKillerPressesAreIgnored()
    {
        var (m, s, check) = StartCheck();
        using var _ = m;
        var srv = m.F.Server;
        Hold(s, false);
        m.Tick();
        double kept = srv.TerminalProgressForTests(0);
        m.Tick(40);
        s.Send(new C2S_SyncPress { CheckId = check.CheckId }); // the cancelled check
        s.Send(new C2S_SyncPress { CheckId = 12345 });         // an unknown check
        m.Tick(2);
        Assert.Empty(Fresh<S2C_SyncResult>(s));
        Assert.Equal(kept, srv.TerminalProgressForTests(0), 6);
        Assert.False(Has(World(s).Terminals[0], TerminalFlags.Overloaded));
        Assert.False(P(m, s).SyncPending);

        // The interval timer only runs while hacking.
        srv.SetSyncTimerForTests(s.Id, 0.5);
        m.Tick(40);
        Assert.Empty(Fresh<S2C_SyncCheck>(s));
        Hold(s, true);
        m.Tick(11);
        var next = Assert.Single(Fresh<S2C_SyncCheck>(s));
        Assert.True(next.CheckId > check.CheckId);

        // The killer cannot answer it, a wrong id is ignored, and a second press after the result is ignored.
        m.Killer.Send(new C2S_SyncPress { CheckId = next.CheckId });
        s.Send(new C2S_SyncPress { CheckId = next.CheckId + 1 });
        m.Tick();
        Assert.Empty(Fresh<S2C_SyncResult>(s));
        Assert.Empty(Fresh<S2C_SyncResult>(m.Killer));
        Assert.True(P(m, s).SyncPending);
        PressAt(m, s, next, next.ZoneStart + next.GreatSize / 2);
        double after = srv.TerminalProgressForTests(0);
        s.Send(new C2S_SyncPress { CheckId = next.CheckId });
        m.Tick();
        Assert.Empty(Fresh<S2C_SyncResult>(s));
        Assert.InRange(srv.TerminalProgressForTests(0) - after, HackPerTick - 0.001, HackPerTick + 0.001);
    }

    [Theory]
    [InlineData(0.5, 0.5, SyncResult.Great)]
    [InlineData(0.535, 0.5, SyncResult.Great)]
    [InlineData(0.536, 0.5, SyncResult.Good)]
    [InlineData(0.62, 0.5, SyncResult.Good)]
    [InlineData(0.4999, 0.5, SyncResult.Good)]         // the leading tolerance band is Good, not Great
    [InlineData(0.3637, 0.5, SyncResult.Good)]         // 0.5 - 0.15/1.1 = 0.36364
    [InlineData(0.3635, 0.5, SyncResult.Fail)]
    [InlineData(0.7563, 0.5, SyncResult.Good)]         // 0.62 + 0.13636
    [InlineData(0.7565, 0.5, SyncResult.Fail)]
    [InlineData(0.0, 0.5, SyncResult.Fail)]
    [InlineData(1.2, 0.8, SyncResult.Fail)]
    public void ClassifyUsesTheGreatPartTheZoneAndTheToleranceInFractions(double fraction, float zoneStart, SyncResult expected) =>
        Assert.Equal(expected, GameServer.Classify(fraction, zoneStart));

    // ---- R4: switches and escape

    private static RulesMatch PoweredMatch()
    {
        var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        int required = World(m.Killer).TerminalsRequired;
        for (int i = 0; i < required; i++) srv.SetTerminalProgressForTests(i, 100);
        m.Tick();
        Assert.True(World(m.Killer).Powered());
        Assert.True(World(m[0]).Powered());
        return m;
    }

    [Fact]
    public void SwitchNeedsPowerFillsOverFifteenSecondsKeepsProgressDoesNotStackAndStopsWhileLocked()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server; var a = m[0]; var b = m[1];
        var sw = srv.Map.ExitSwitches[0];
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        srv.PlaceForTests(a.Id, sw.X, sw.Z);
        Hold(a, true);
        m.Tick(40);
        Assert.Equal(0, World(a).SwitchProgress(0)); // unpowered
        Hold(a, false);
        int required = World(m.Killer).TerminalsRequired;
        for (int i = 0; i < required; i++) srv.SetTerminalProgressForTests(i, 100);
        m.Tick();
        Assert.True(World(a).Powered());

        Hold(a, true);
        m.Tick(100); // 5 s of 15 s
        Assert.InRange(World(a).SwitchProgress(0), (byte)32, (byte)34);
        Assert.InRange(World(m.Killer).SwitchProgress(0), (byte)32, (byte)34);
        Assert.Equal((byte)ActionKind.Switch, a.Last<S2C_SelfState>().ActionKind);
        Assert.NotNull(m.KillerSees(a)); // holding a switch is a Signal
        Hold(a, false);
        m.Tick(40);
        Assert.InRange(World(a).SwitchProgress(0), (byte)32, (byte)34); // releasing keeps the progress

        var near = FindPoint(srv.Map, sw.X, sw.Z, (x, z) => Distance(x, z, sw.X, sw.Z) is > 0.5f and < 1.2f);
        srv.PlaceForTests(b.Id, near.X, near.Z);
        Hold(a, true); Hold(b, true);
        m.Tick(100);
        Assert.InRange(World(a).SwitchProgress(0), (byte)65, (byte)67); // two holders = one

        Spy.Server<double[]>(srv, "_exitLockedUntil")[0] = Now(m) + 5;
        m.Tick(40);
        Assert.True(World(m.Killer).Locked(0));
        Assert.InRange(World(a).SwitchProgress(0), (byte)65, (byte)67);
        m.Tick(70);
        Assert.False(World(a).Locked(0));
        Assert.InRange(World(a).SwitchProgress(0), (byte)68, (byte)72);
    }

    [Fact]
    public void OnlyANormalSurvivorAtAnOpenExitEscapes()
    {
        using var m = PoweredMatch();
        var srv = m.F.Server; var a = m[0]; var b = m[1];
        var sw = srv.Map.ExitSwitches[0]; var exit = srv.Map.Exits[0];
        srv.PlaceForTests(a.Id, sw.X, sw.Z);
        srv.PlaceForTests(b.Id, exit.X, exit.Z);
        m.Tick(20);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(b).Status); // powered but closed
        srv.SetStatusForTests(b.Id, PlayerStatus.Lagging);
        Hold(a, true);
        int n = 0;
        while (!World(a).Open(0) && n < 400) { m.Tick(); n++; }
        Assert.InRange(n, 299, 301);
        Assert.InRange(World(a).CollapseLeft, GameConstants.CollapseSeconds - 0.11f, GameConstants.CollapseSeconds);
        m.Tick(5);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(b).Status); // a Lagging survivor cannot escape
        srv.SetStatusForTests(b.Id, PlayerStatus.Normal);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Escaped, m.Truth(b).Status);
        Assert.False(World(a).Open(1));
    }

    // ---- R5: collapse

    [Fact]
    public void PersonalCollapseClockRunsTwiceAsFastWhileLaggingAndItsEndEliminatesWithReasonCollapse()
    {
        using var m = PoweredMatch();
        var srv = m.F.Server; var a = m[0]; var b = m[1];
        var sw = srv.Map.ExitSwitches[0]; var exit = srv.Map.Exits[0];
        srv.PlaceForTests(a.Id, sw.X, sw.Z);
        srv.PlaceForTests(b.Id, ParkX, ParkZ);
        Hold(a, true);
        for (int i = 0; i < 400 && !World(a).Open(0); i++) m.Tick();
        Assert.True(World(a).Open(0));
        Hold(a, false);
        srv.PlaceForTests(a.Id, exit.X, exit.Z);
        srv.SetStatusForTests(b.Id, PlayerStatus.Lagging);
        m.Tick(200);
        Assert.Equal((byte)PlayerStatus.Escaped, m.Truth(a).Status);
        Assert.InRange(World(b).CollapseLeft, 109.9f, 110.1f);
        Assert.InRange(P(m, b).CollapseLeft, 99.85, 100.15); // 2x while Lagging
        Assert.InRange(b.Last<S2C_SelfState>().CollapseLeft, 99.5f, 100.5f);

        srv.SetStatusForTests(b.Id, PlayerStatus.Normal);
        int n = 0;
        while (!m.Killer.All<S2C_GameResult>().Any() && n < 3000) { m.Tick(); n++; }
        Assert.InRange(n, 1997, 2003); // the remaining 100 s at 1x
        var result = m.Killer.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.Collapse, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole); // 1 of 2 escaped
        Assert.Equal(new[] { a.Id }, result.EscapedIds);
        Assert.Equal(new[] { b.Id }, result.EliminatedIds);
    }

    // ---- R6: hatch

    [Fact]
    public void HatchNeedsTwoStartingSurvivors()
    {
        using var m = RulesMatch.Start(1);
        m.Tick(40);
        Assert.False(m.F.Server.HatchForTests().Open);
        Assert.Equal(0, World(m[0]).HatchVisible);
    }

    [Fact]
    public void HatchOpensForTheLastSurvivorIsSeenOnlyWithinFifteenMetresInLineAndLetsANormalSurvivorEscape()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server; var a = m[0]; var b = m[1];
        m.Tick();
        Assert.False(srv.HatchForTests().Open);
        srv.SetStatusForTests(b.Id, PlayerStatus.Eliminated);
        m.Tick();
        var hatch = srv.HatchForTests();
        Assert.True(hatch.Open);
        Assert.Contains(srv.Map.HatchPoints, p => p.X == hatch.X && p.Z == hatch.Z);
        var map = srv.Map;

        var seen = FindPoint(map, hatch.X, hatch.Z, (x, z) => Distance(x, z, hatch.X, hatch.Z) is > 10f and < 14.5f && !map.SegmentBlocked(x, z, hatch.X, hatch.Z));
        srv.PlaceForTests(a.Id, seen.X, seen.Z);
        m.Tick();
        Assert.Equal(((byte)1, hatch.X, hatch.Z), (World(a).HatchVisible, World(a).HatchX, World(a).HatchZ));
        Assert.Equal(0, World(m.Killer).HatchVisible); // per recipient: the killer stays at (5,5), over 20 m away

        var far = FindPoint(map, hatch.X, hatch.Z, (x, z) => Distance(x, z, hatch.X, hatch.Z) is > 15.5f and < 20f && !map.SegmentBlocked(x, z, hatch.X, hatch.Z));
        srv.PlaceForTests(a.Id, far.X, far.Z);
        m.Tick();
        Assert.Equal(0, World(a).HatchVisible);

        var blocked = FindPoint(map, hatch.X, hatch.Z, (x, z) => Distance(x, z, hatch.X, hatch.Z) < 14.5f && map.SegmentBlocked(x, z, hatch.X, hatch.Z));
        srv.PlaceForTests(a.Id, blocked.X, blocked.Z);
        m.Tick();
        Assert.Equal(0, World(a).HatchVisible);

        var next = FindPoint(map, hatch.X, hatch.Z, (x, z) => Distance(x, z, hatch.X, hatch.Z) < 1.4f);
        srv.PlaceForTests(a.Id, next.X, next.Z);
        m.Tick(5);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(a).Status); // standing on it is not enough
        Hold(a, true);
        m.Tick();
        var result = m.Killer.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllResolved, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);
        Assert.Equal(new[] { a.Id }, result.EscapedIds);
        Assert.Equal(new[] { b.Id }, result.EliminatedIds);
    }

    [Fact]
    public void KillerClosesTheHatchInOneSecondWhichPowersTheExitsAndStartsTheCollapse()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server; var a = m[0]; var b = m[1];
        srv.PlaceForTests(a.Id, ParkX, ParkZ);
        srv.SetStatusForTests(b.Id, PlayerStatus.Eliminated);
        m.Tick();
        var hatch = srv.HatchForTests();
        var next = FindPoint(srv.Map, hatch.X, hatch.Z, (x, z) => Distance(x, z, hatch.X, hatch.Z) < 1.4f && Distance(x, z, ParkX, ParkZ) > 5);
        srv.PlaceForTests(m.Killer.Id, next.X, next.Z);
        m.Tick();
        Assert.Equal(1, World(m.Killer).HatchVisible);
        Assert.False(World(m.Killer).Powered());
        Assert.Equal(-1f, World(m.Killer).CollapseLeft);
        Hold(m.Killer, true);
        int n = 0;
        while (srv.HatchForTests().Open && n < 60)
        {
            m.Tick(); n++;
            if (n == 5) Assert.Equal((byte)ActionKind.HatchClose, m.Killer.Last<S2C_SelfState>().ActionKind);
        }
        Assert.InRange(n, 19, 21);
        Assert.Equal(0, World(m.Killer).HatchVisible);
        Assert.True(World(a).Powered());
        Assert.InRange(World(a).CollapseLeft, GameConstants.CollapseSeconds - 0.11f, GameConstants.CollapseSeconds);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(a).Status);
    }

    // ---- R7 / R8 / R9: capture stages, winner and end reasons

    [Fact]
    public void AThirdCaptureEliminatesAndAllDownIsAKillerWin()
    {
        using var m = RulesMatch.Start(1);
        var srv = m.F.Server; var s = m[0];
        srv.PlaceForTests(s.Id, 5, 36);
        srv.PlaceForTests(m.Killer.Id, 6, 36);
        P(m, s).CaptureCount = 2;
        m.Tick();
        var result = m.Killer.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllSurvivorsDown, result.Reason);
        Assert.Equal((byte)Role.Killer, result.WinnerRole);
        Assert.Empty(result.EscapedIds);
        Assert.Equal(new[] { s.Id }, result.EliminatedIds);
    }

    [Fact]
    public void ASecondCaptureLagsForThirtySecondsAndItsTimeoutEliminates()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server; var s = m[0];
        srv.PlaceForTests(s.Id, 5, 36);
        srv.PlaceForTests(m[1].Id, ParkX, ParkZ);
        srv.PlaceForTests(m.Killer.Id, 6, 36);
        P(m, s).CaptureCount = 1;
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
        Assert.Equal(2, m.Truth(s).CaptureCount);
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        int n = 0;
        while (m.Truth(s).Status == (byte)PlayerStatus.Lagging && n < 1000) { m.Tick(); n++; }
        Assert.InRange(n, 599, 601);
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(s).Status);
    }

    // GDD v2 §4 (user decision 2026-11-03): the killer cannot touch a Lagging survivor into the next stage. Touching it over
    // and over (contact, leave, re-enter, every pattern) for its whole timer changes neither its stage nor its status; only
    // the timer does: 60 s -> count 2 with 30 s -> Eliminated.
    [Fact]
    public void RepeatedTouchesForTheWholeLaggingTimerNeverChangeTheStageOrStatus()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server; var s = m[0];
        srv.PlaceForTests(s.Id, 5, 36);
        srv.PlaceForTests(m[1].Id, ParkX, ParkZ);
        srv.PlaceForTests(m.Killer.Id, 6, 36);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
        Assert.Equal(1, m.Truth(s).CaptureCount);
        int ticks = 1;
        for (int stage = 1; stage <= 2; stage++)
        {
            int end = stage == 1 ? 1200 : 1800; // 60 s, then 30 s more
            for (int i = 0; m.Truth(s).Status == (byte)PlayerStatus.Lagging && m.Truth(s).CaptureCount == stage && i < 1300; i++)
            {
                // Alternate: stand in contact, step out of reach, step back in.
                float x = (i % 6) switch { 0 or 1 or 2 => 6f, 3 => 7.5f, _ => 5.9f };
                srv.PlaceForTests(m.Killer.Id, x, 36);
                m.Tick();
                ticks++;
                var t = m.Truth(s);
                if (ticks < end - 2) Assert.True(t.Status == (byte)PlayerStatus.Lagging && t.CaptureCount == stage, $"changed at tick {ticks}");
            }
            Assert.InRange(ticks, end - 2, end + 2);
            if (stage == 1)
            {
                Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
                Assert.Equal(2, m.Truth(s).CaptureCount);
                Assert.InRange(m.Truth(s).StatusTimer, 29.9f, 30f);
            }
        }
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(s).Status);
        Assert.Equal(0f, m.Killer.Last<S2C_SelfState>().ExecutionGraceLeft);
    }

    [Fact]
    public void TimeUpEliminatesTheRestAndTheWinnerStillFollowsTheEscapedMajority()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server; var a = m[0]; var b = m[1];
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        Spy.SetServer(srv, "_powered", true);
        Spy.Server<bool[]>(srv, "_exitOpen")[0] = true; // open without the collapse, so only the clock ends the match
        srv.PlaceForTests(a.Id, srv.Map.Exits[0].X, srv.Map.Exits[0].Z);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Escaped, m.Truth(a).Status);
        Spy.SetServer(srv, "_matchTime", 899.9);
        m.Tick(3);
        var result = m.Killer.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.TimeUp, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);
        Assert.Equal(new[] { a.Id }, result.EscapedIds);
        Assert.Equal(new[] { b.Id }, result.EliminatedIds);
    }

    [Theory]
    [InlineData(3, 7, Role.Killer)]
    [InlineData(4, 7, Role.Survivor)]
    [InlineData(1, 2, Role.Survivor)]
    [InlineData(0, 2, Role.Killer)]
    [InlineData(1, 3, Role.Killer)]
    [InlineData(2, 3, Role.Survivor)]
    [InlineData(0, 1, Role.Killer)]
    [InlineData(1, 1, Role.Survivor)]
    public void SurvivorsWinWhenAtLeastHalfEscaped(int escaped, int starting, Role winner) =>
        Assert.Equal(winner, GameServer.WinnerFor(escaped, starting));

    [Fact]
    public void EveryGameEndReasonHasItsV04Value()
    {
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues<GameEndReason>().Select(r => (int)r));
        Assert.Equal(new[] { "AllResolved", "AllSurvivorsDown", "TimeUp", "Collapse" }, Enum.GetNames<GameEndReason>());
    }

    // ---- map data for R4 / R6

    [Fact]
    public void SwitchesAreStandableFourMetresTowardTheCentreAndHatchPointsKeepSixMetresFromExitsAndSwitches()
    {
        var map = new FixedMap();
        Assert.Equal(map.Exits.Count, map.ExitSwitches.Count);
        for (int i = 0; i < map.Exits.Count; i++)
        {
            var e = map.Exits[i]; var s = map.ExitSwitches[i];
            Assert.True(map.CanStand(s.X, s.Z), $"switch {i} at ({s.X},{s.Z})");
            Assert.InRange(Distance(e.X, e.Z, s.X, s.Z), 3.99f, 4.01f);
            Assert.True(Distance(s.X, s.Z, 40, 40) < Distance(e.X, e.Z, 40, 40));
            Assert.True(Distance(e.X, e.Z, s.X, s.Z) > GameConstants.ExitRadius + GameConstants.ExitSwitchRadius);
        }
        foreach (var h in map.HatchPoints)
        {
            Assert.True(map.CanStand(h.X, h.Z), $"hatch ({h.X},{h.Z})");
            foreach (var p in map.Exits.Concat(map.ExitSwitches))
                Assert.True(Distance(h.X, h.Z, p.X, p.Z) >= 6, $"hatch ({h.X},{h.Z}) is within 6 m of ({p.X},{p.Z})");
        }
    }
}
