using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// INTEG-R (11/17): how R2's packet routing meets R3's explicit rescue and get-up on one E key.
// - E priority: with a Lagging ally in reach the held E rescues; it never hacks, never advertises the route and never takes a
//   route key.
// - Getting up: no hack, no route, no route key.
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
}
