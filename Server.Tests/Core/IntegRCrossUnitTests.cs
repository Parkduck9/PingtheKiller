using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// INTEG-R (11/17): how R2's packet routing meets R3's explicit rescue and get-up on one E key.
// - E priority: with a Lagging ally in reach the held E rescues; it never hacks, never advertises the route and never takes a
//   route key.
// - Getting up: no hack, no route, no route key; R1's swing cannot hit a getting-up Survivor (the 2 s protection).
// - R1 Firewall on a terminal: the route is dropped (not advertised, keys ignored) and a new one comes after the lock.
public sealed class IntegRCrossUnitTests
{
    private const float Dt = 1f / GameConstants.TickRate;

    private static void Hold(Peer peer, bool on) => peer.Send(new C2S_Interact { Active = (byte)(on ? 1 : 0) });
    private static RouteDirection Right((byte Id, byte Dirs, byte Index, int Terminal) r) => HackRoute.DirectionAt(r.Dirs, r.Index);

    // Survivor 0 hacks terminal 0 (E held, a route dealt); survivor 1 stands 0.8 m away.
    private static RulesMatch HackingNextToAnAlly()
    {
        var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        var t = srv.Map.TerminalPositions[0];
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        srv.PlaceForTests(m[0].Id, t.X - .5f, t.Z);
        srv.PlaceForTests(m[1].Id, t.X - .5f, t.Z + .8f);
        srv.SetTerminalProgressForTests(0, 50);
        Hold(m[0], true);
        m.Tick(3);
        Assert.NotEqual(0, srv.RouteForTests(m[0].Id).Id);
        Assert.NotEqual(0, m[0].Last<S2C_SelfState>().RouteId);
        return m;
    }

    [Fact]
    public void AnEPressNearALaggingAllyRescuesInsteadOfHackingAndTheRouteGoesQuiet()
    {
        using var m = HackingNextToAnAlly();
        var srv = m.F.Server;
        var hacker = m[0];
        srv.SetStatusForTests(m[1].Id, PlayerStatus.Lagging);
        m.Tick(2);
        Assert.NotEqual(0, m.Truth(hacker).Flags & (byte)PlayerFlags.Rescuing);
        Assert.Equal(0, m.Truth(hacker).Flags & (byte)PlayerFlags.Hacking);
        var self = hacker.Last<S2C_SelfState>();
        Assert.Equal((byte)ActionKind.Rescue, self.ActionKind);
        Assert.Equal(0, self.RouteId); // the route panel hides and WASD is movement again on the client

        // A route key sent now is dropped: no progress, no index change, no Overloaded flash.
        var route = srv.RouteForTests(hacker.Id);
        double before = srv.TerminalProgressForTests(0);
        srv.HackInputForTests(hacker.Id, route.Id, (byte)Right(route));
        srv.HackInputForTests(hacker.Id, route.Id, (byte)(((int)Right(route) + 1) % 4));
        Assert.Equal(route, srv.RouteForTests(hacker.Id));
        Assert.Equal(before, srv.TerminalProgressForTests(0), 6);

        // The held E finishes the rescue (3 s) and the terminal gained nothing meanwhile; from the tick after, the still-held E
        // hacks again (the rescuer never released it), which adds at most one passive tick here.
        int ticks = 0;
        while (m.Truth(m[1]).Status != (byte)PlayerStatus.Normal && ticks < 200) { m.Tick(); ticks++; }
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[1]).Status);
        Assert.InRange(ticks + 2, (int)(GameConstants.RescueTimeSeconds / Dt) - 1, (int)(GameConstants.RescueTimeSeconds / Dt) + 1);
        Assert.InRange(srv.TerminalProgressForTests(0), before, before + 1.5 * Dt * 100.0 / GameConstants.TerminalPassiveSeconds);
    }

    [Fact]
    public void AGettingUpSurvivorAtATerminalNeitherHacksNorRoutes()
    {
        using var m = HackingNextToAnAlly();
        var srv = m.F.Server;
        var getter = m[1];
        Hold(m[0], false);
        srv.SetStatusForTests(getter.Id, PlayerStatus.Lagging);
        m.Tick();
        srv.SetStatusForTests(getter.Id, PlayerStatus.Normal); // a rescue: the get-up starts now
        var t = srv.Map.TerminalPositions[0];
        srv.PlaceForTests(getter.Id, t.X + .5f, t.Z);
        Hold(getter, true);
        double before = srv.TerminalProgressForTests(0);
        int getUpTicks = (int)System.Math.Floor(GameConstants.GetUpSeconds / Dt) - 1;
        for (int i = 0; i < getUpTicks; i++)
        {
            m.Tick();
            Assert.Equal(0, m.Truth(getter).Flags & (byte)PlayerFlags.Hacking);
            Assert.Equal((byte)0, srv.RouteForTests(getter.Id).Id);
        }
        Assert.Equal(0, getter.Last<S2C_SelfState>().RouteId);
        Assert.Equal(before, srv.TerminalProgressForTests(0), 6);
        srv.HackInputForTests(getter.Id, 1, (byte)RouteDirection.Up);
        Assert.Equal(before, srv.TerminalProgressForTests(0), 6);
    }
    [Fact]
    public void AFirewallLockedTerminalDropsTheRouteAndIgnoresItsKeys()
    {
        using var m = RulesMatch.Start(1, SkillIds.FirewallBlock);
        var srv = m.F.Server;
        var t = srv.Map.TerminalPositions[1];
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        srv.SetTerminalProgressForTests(1, 40);
        srv.PlaceForTests(m[0].Id, t.X + .8f, t.Z);
        Hold(m[0], true);
        m.Tick(3);
        var route = srv.RouteForTests(m[0].Id);
        Assert.NotEqual(0, route.Id);
        Assert.Equal(1, route.Terminal);
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick(2);
        Assert.NotEqual(0, m[0].Last<S2C_WorldState>().Terminals[1].Flags & (byte)TerminalFlags.Locked);
        Assert.Equal((byte)0, srv.RouteForTests(m[0].Id).Id);
        Assert.Equal(0, m[0].Last<S2C_SelfState>().RouteId);
        double locked = srv.TerminalProgressForTests(1);
        srv.HackInputForTests(m[0].Id, route.Id, (byte)Right(route));
        m.Tick(20);
        Assert.Equal(locked, srv.TerminalProgressForTests(1), 6);
        Assert.Equal((byte)0, srv.RouteForTests(m[0].Id).Id);
        m.Tick((int)(SkillIds.FirewallTerminalSeconds / Dt) + 5); // the lock ends; the held E hacks again with a fresh route
        Assert.NotEqual(0, srv.RouteForTests(m[0].Id).Id);
        Assert.NotEqual(route.Id, srv.RouteForTests(m[0].Id).Id);
        Assert.True(srv.TerminalProgressForTests(1) > locked);
    }

    [Fact]
    public void TheSwingCannotHitASurvivorWhoIsGettingUp()
    {
        using var m = RulesMatch.Start(2);
        var srv = m.F.Server;
        srv.ContactSwingForTests = false;
        srv.PlaceForTests(m.Killer.Id, 5, 24);
        srv.PlaceForTests(m[0].Id, 5, 25.5f);
        srv.PlaceForTests(m[1].Id, 5, 36);
        m.Tick(25);
        srv.SetStatusForTests(m[0].Id, PlayerStatus.Lagging);
        m.Tick();
        srv.SetStatusForTests(m[0].Id, PlayerStatus.Normal); // rescued: getting up and protected
        var log = new List<string>();
        srv.Log += log.Add;
        m.Killer.Send(new C2S_MoveInput { DirX = 0, DirZ = 0, Yaw = 0, Seq = m.Killer.NextSeq++ });
        m.Killer.Send(new C2S_Attack { Seq = 9001 });
        m.Tick(10); // the wind-up ends and the hit check runs inside the get-up
        Assert.NotEqual(0, m.Truth(m[0]).Flags & (byte)PlayerFlags.GettingUp);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[0]).Status);
        Assert.Equal(0, m.Truth(m[0]).CaptureCount); // the test status change counts no capture; the swing added none
        Assert.Contains(log, l => l.StartsWith($"swing player={m.Killer.Id} result=miss", StringComparison.Ordinal));
    }
}
