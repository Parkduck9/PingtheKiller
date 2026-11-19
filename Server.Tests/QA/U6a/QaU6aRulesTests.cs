using System;
using System.Collections.Generic;
using System.Linq;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests;
using PingTheKiller.Tests.Core;
using Xunit;

namespace PingTheKiller.Tests.QA.U6a;

public sealed class QaU6aRulesTests
{
    // Parking spot for a bystander survivor: open street east of the (42,16)-(53,30) block on map v2.1, >= 8 m from
    // every terminal and > 20 m from every hatch point, exit and switch. (Map v2's (48.5, 25) is inside that block now.)
    private const float ParkX = 54f, ParkZ = 20f;
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

    [Fact]
    [Trait("Category", "QaU6a")]
    public void SilentSurvivorIsOmittedFromKillerWorldStateAndRegressingFlagNeverSentToKiller()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var s0 = m[0];
        var s1 = m[1];
        var t0 = srv.Map.TerminalPositions[0];

        // Place s0 near terminal 0, s1 far away, killer at terminal 0. s0 stands 1.2 m from the terminal (inside its 1.5 m
        // reach) so it is 1.7 m from the killer: at 1.0 m the killer's 1.2 m capture radius would capture it on the first
        // tick, and a Lagging survivor is always Signal (U6a attempt 2: the setup, not the product, was wrong).
        srv.PlaceForTests(s0.Id, t0.X - 1.2f, t0.Z);
        srv.PlaceForTests(s1.Id, 48.5f, 60f);
        srv.PlaceForTests(m.Killer.Id, t0.X + 0.5f, t0.Z);
        srv.SetTerminalProgressForTests(0, 50);
        m.Tick();

        // While survivors stand still (Silent), killer's WorldState must not contain s0 or s1
        Assert.Null(m.KillerSees(s0));
        Assert.Null(m.KillerSees(s1));

        // Killer kicks terminal 0 (2.0s hold)
        Hold(m.Killer, true);
        int n = 0;
        while (srv.TerminalProgressForTests(0) >= 50 && n < 100)
        {
            m.Tick();
            n++;
        }
        Assert.InRange(n, 39, 41);
        Hold(m.Killer, false);
        m.Tick();

        // Survivors see Regressing = 1 on terminal 0; Killer NEVER sees Regressing
        Assert.True(Has(World(s0).Terminals[0], TerminalFlags.Regressing));
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Regressing));

        // Killer sees progress as 0 or 100 only (progress is ~45% here, so killer sees 0)
        Assert.Equal(0, World(m.Killer).Terminals[0].Progress);
        Assert.InRange(World(s0).Terminals[0].Progress, 44, 45);

        // s0 starts hacking -> Regressing is cleared
        Hold(s0, true);
        m.Tick();
        Assert.False(Has(World(s0).Terminals[0], TerminalFlags.Regressing));
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Regressing));

        // While s0 is hacking, s0 becomes Signal and is visible to the Killer
        Assert.NotNull(m.KillerSees(s0));
        // But s1 is still Silent and NOT visible
        Assert.Null(m.KillerSees(s1));

        // s0 stops hacking -> returns to Silent after signal hold (0.15s = 3 ticks). Killer skills are random per match, so
        // wait past a possible 1 s Packet Afterimage too (it keeps the last seen entry frozen for 1 s by design).
        Hold(s0, false);
        m.Tick(30);
        Assert.Null(m.KillerSees(s0));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void HatchVisibilityIsMaskedByDistanceAndWalls()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var a = m[0];
        var b = m[1];
        m.Tick();

        // Eliminate b so hatch opens
        srv.SetStatusForTests(b.Id, PlayerStatus.Eliminated);
        m.Tick();
        var hatch = srv.HatchForTests();
        Assert.True(hatch.Open);

        // Place killer far away from hatch (> 20m)
        srv.PlaceForTests(m.Killer.Id, 5, 5);
        m.Tick();
        Assert.Equal(0, World(m.Killer).HatchVisible);
        Assert.Equal(0f, World(m.Killer).HatchX);
        Assert.Equal(0f, World(m.Killer).HatchZ);

        // Place survivor within 14m of hatch with line of sight
        // Find a standable point near hatch without wall blockage
        float hx = hatch.X;
        float hz = hatch.Z;
        srv.PlaceForTests(a.Id, hx, hz + 1.0f);
        m.Tick();
        Assert.Equal(1, World(a).HatchVisible);
        Assert.Equal(hx, World(a).HatchX);
        Assert.Equal(hz, World(a).HatchZ);

        // Stand on hatch and hold interact -> escape immediately
        Hold(a, true);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Escaped, m.Truth(a).Status);
        var result = m.Killer.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllResolved, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void HatchNeverSpawnsWhenMatchStartsConcurrentlyWithOnlyOneSurvivor()
    {
        using var m = RulesMatch.Start(1);
        var srv = m.F.Server;
        srv.PlaceForTests(m[0].Id, 10, 26);
        srv.SetTerminalProgressForTests(0, 100);
        m.Tick(100);

        // Hatch must not spawn because match started with only 1 survivor (R6: starting >= 2)
        Assert.False(srv.HatchForTests().Open);
        Assert.Equal(0, World(m[0]).HatchVisible);
        Assert.Equal(0, World(m.Killer).HatchVisible);
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void KillerClosesHatchStartsCollapseAndPowersBothExits()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var a = m[0];
        var b = m[1];
        srv.PlaceForTests(a.Id, ParkX, ParkZ);
        srv.SetStatusForTests(b.Id, PlayerStatus.Eliminated);
        m.Tick();

        var hatch = srv.HatchForTests();
        Assert.True(hatch.Open);
        Assert.False(World(m.Killer).Powered());
        Assert.Equal(-1f, World(m.Killer).CollapseLeft);

        // Killer moves within 1.4m of hatch
        srv.PlaceForTests(m.Killer.Id, hatch.X, hatch.Z + 1.0f);
        m.Tick();
        Assert.Equal(1, World(m.Killer).HatchVisible);

        // Killer holds Interact for 1.0 s
        Hold(m.Killer, true);
        int n = 0;
        while (srv.HatchForTests().Open && n < 50)
        {
            m.Tick();
            n++;
        }
        Assert.InRange(n, 19, 21); // 1.0 s at 20 Hz
        Assert.False(srv.HatchForTests().Open);
        Assert.Equal(0, World(m.Killer).HatchVisible);

        // Exits are now powered and collapse is running
        Assert.True(World(a).Powered());
        Assert.True(World(m.Killer).Powered());
        Assert.InRange(World(a).CollapseLeft, GameConstants.CollapseSeconds - 0.2f, GameConstants.CollapseSeconds);
    }

    // 11/17 (R2): the sync check was replaced by packet routing; these keep the intent of the two U6a sync-check cases
    // (stopping the hack makes presses harmless; the killer and unknown ids are ignored without a reply).
    [Fact]
    [Trait("Category", "QaU6a")]
    public void RouteKeysAfterReleasingInteractAreIgnoredWithoutPenaltyAndTheRouteWaits()
    {
        using var m = RulesMatch.Start(1);
        var srv = m.F.Server;
        var s = m[0];
        var t = srv.Map.TerminalPositions[0];

        srv.PlaceForTests(s.Id, t.X - 0.5f, t.Z);
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60f);
        srv.SetTerminalProgressForTests(0, 50);
        Hold(s, true);
        m.Tick(2);
        var route = s.Last<S2C_SelfState>();
        Assert.NotEqual(0, route.RouteId);

        // Release interact (stop hacking)
        Hold(s, false);
        m.Tick();
        double progressAtRelease = srv.TerminalProgressForTests(0);

        // Right and wrong keys for the held route: no progress, no penalty, not overloaded, the route is kept
        s.Send(new C2S_HackInput { RouteId = route.RouteId, Direction = (byte)HackRoute.DirectionAt(route.RouteDirs, 0) });
        s.Send(new C2S_HackInput { RouteId = route.RouteId, Direction = (byte)(((int)HackRoute.DirectionAt(route.RouteDirs, 0) + 1) % 4) });
        m.Tick(2);
        Assert.Equal(progressAtRelease, srv.TerminalProgressForTests(0), 4);
        Assert.False(Has(World(s).Terminals[0], TerminalFlags.Overloaded));
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));
        Assert.Equal((route.RouteId, route.RouteDirs, (byte)0), (srv.RouteForTests(s.Id).Id, srv.RouteForTests(s.Id).Dirs, srv.RouteForTests(s.Id).Index));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void HackInputFromKillerOrUnknownRouteIdIsIgnoredWithoutReply()
    {
        using var m = RulesMatch.Start(1);
        var srv = m.F.Server;
        var s = m[0];
        var t = srv.Map.TerminalPositions[0];

        srv.PlaceForTests(s.Id, t.X - 0.5f, t.Z);
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60f);
        srv.SetTerminalProgressForTests(0, 50);
        Hold(s, true);
        m.Tick(2);
        var route = s.Last<S2C_SelfState>();
        Assert.NotEqual(0, route.RouteId);
        var right = (byte)HackRoute.DirectionAt(route.RouteDirs, 0);

        // Killer tries to answer the survivor's route
        Fresh<S2C_SelfState>(m.Killer);
        m.Killer.Send(new C2S_HackInput { RouteId = route.RouteId, Direction = right });
        m.Tick();
        Assert.Equal(0, srv.RouteForTests(s.Id).Index);
        Assert.All(Fresh<S2C_SelfState>(m.Killer), k => Assert.Equal(0, k.RouteId));

        // Survivor sends an unknown route id and the "no route" id 0
        s.Send(new C2S_HackInput { RouteId = (byte)(route.RouteId + 1), Direction = right });
        s.Send(new C2S_HackInput { RouteId = 0, Direction = right });
        m.Tick();
        Assert.Equal((route.RouteId, (byte)0), (srv.RouteForTests(s.Id).Id, srv.RouteForTests(s.Id).Index));
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void LaggingPersonalCollapseClockRunsTwiceAsFastAndEliminatesAtZero()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var a = m[0];
        var b = m[1];

        // Power exits
        int required = World(m.Killer).TerminalsRequired;
        for (int i = 0; i < required; i++) srv.SetTerminalProgressForTests(i, 100);
        m.Tick();
        Assert.True(World(a).Powered());

        // Open exit 0 with a
        var sw = srv.Map.ExitSwitches[0];
        srv.PlaceForTests(a.Id, sw.X, sw.Z);
        srv.PlaceForTests(b.Id, ParkX, ParkZ);
        Hold(a, true);
        for (int i = 0; i < 400 && !World(a).Open(0); i++) m.Tick();
        Assert.True(World(a).Open(0));
        Hold(a, false);

        // a escapes
        srv.PlaceForTests(a.Id, srv.Map.Exits[0].X, srv.Map.Exits[0].Z);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Escaped, m.Truth(a).Status);

        // Set b to Lagging
        srv.SetStatusForTests(b.Id, PlayerStatus.Lagging);
        m.Tick();

        // Advance 10 seconds (200 ticks): global decrements ~10s, personal decrements ~20s
        m.Tick(200);
        var selfB = b.Last<S2C_SelfState>();
        var worldB = World(b);
        Assert.InRange(worldB.CollapseLeft, 109.5f, 110.5f); // 1x
        Assert.InRange(selfB.CollapseLeft, 99.0f, 100.5f);   // 2x

        // Fast forward until b's personal clock reaches 0 and b is eliminated
        int n = 0;
        while (!m.Killer.All<S2C_GameResult>().Any() && n < 2500)
        {
            m.Tick();
            n++;
        }
        // ~100s remaining at 2x rate = ~50s = ~1000 ticks
        Assert.InRange(n, 980, 1030);

        var result = m.Killer.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.Collapse, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole); // 1 of 2 escaped: 1 * 2 >= 2
        Assert.Contains(a.Id, result.EscapedIds);
        Assert.Contains(b.Id, result.EliminatedIds);
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void CaptureStagesProgressSixtyToThirtyToEliminated()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var s = m[0];
        srv.PlaceForTests(s.Id, 5, 26);
        srv.PlaceForTests(m[1].Id, ParkX, ParkZ);
        srv.PlaceForTests(m.Killer.Id, 5, 25); // contact

        // 1st capture -> Lagging, timer 60s, count 1
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
        Assert.Equal(1, m.Truth(s).CaptureCount);
        Assert.InRange(m.Truth(s).StatusTimer, 59.8f, 60.0f);

        // GDD v2 §4 (U6a attempt 2, user decision 2026-11-03): the killer stepping away and back no longer advances the
        // stage; only the Lagging timer does. 60 s -> 2nd stage (count 2, 30 s) -> 30 s -> Eliminated.
        m.Step(m.Killer, 0, -2);
        m.Tick(42);
        m.Step(m.Killer, 0, 2);
        Assert.Equal(1, m.Truth(s).CaptureCount);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
        int n = 0;
        while (m.Truth(s).CaptureCount == 1 && n < 1300) { m.Tick(); n++; }
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
        Assert.InRange(m.Truth(s).StatusTimer, 29.8f, 30.0f);
        Assert.InRange(n, 1150, 1160); // the remaining ~57.8 s of the first 60 s

        // Killer steps away and back again: still no stage change; the 30 s timer eliminates.
        m.Step(m.Killer, 0, -2);
        m.Tick(42);
        m.Step(m.Killer, 0, 2);
        Assert.Equal(2, m.Truth(s).CaptureCount);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(s).Status);
        n = 0;
        while (m.Truth(s).Status == (byte)PlayerStatus.Lagging && n < 700) { m.Tick(); n++; }
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(s).Status);
        Assert.InRange(n, 550, 560); // the remaining ~27.8 s
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void SwitchHoldingDoesNotStackMultipleSurvivors()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var a = m[0];
        var b = m[1];
        var sw = srv.Map.ExitSwitches[0];

        // Power exits
        int required = World(m.Killer).TerminalsRequired;
        for (int i = 0; i < required; i++) srv.SetTerminalProgressForTests(i, 100);
        m.Tick();

        // Both a and b hold the switch
        srv.PlaceForTests(a.Id, sw.X, sw.Z);
        srv.PlaceForTests(b.Id, sw.X, sw.Z);
        Hold(a, true);
        Hold(b, true);

        // Advance 100 ticks (5.0 s of the ExitSwitchSeconds switch time; B1: 17 s)
        m.Tick(100);

        // Progress should be 5/ExitSwitchSeconds (29 % at 17 s), NOT twice that (2x stacking)
        int expected = (int)Math.Floor(5 * 100 / GameConstants.ExitSwitchSeconds);
        byte progress = World(a).SwitchProgress(0);
        Assert.InRange(progress, expected - 1, expected + 1);
    }

    [Theory]
    [Trait("Category", "QaU6a")]
    [InlineData(0, 1, Role.Killer)]
    [InlineData(1, 1, Role.Survivor)]
    [InlineData(0, 2, Role.Killer)]
    [InlineData(1, 2, Role.Survivor)]
    [InlineData(2, 2, Role.Survivor)]
    [InlineData(1, 3, Role.Killer)]
    [InlineData(2, 3, Role.Survivor)]
    [InlineData(3, 7, Role.Killer)]
    [InlineData(4, 7, Role.Survivor)]
    public void WinnerEvaluationBoundaryTests(int escaped, int starting, Role expected)
    {
        Assert.Equal(expected, GameServer.WinnerFor(escaped, starting));
    }
}
