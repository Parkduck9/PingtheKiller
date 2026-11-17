using PingTheKiller.ServerCore;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

// GDD v2 §2 packet routing (11/17, R2): server-authoritative routes, inputs, bursts, errors, the passive part, co-hacking,
// masking, the bots' solver and the solo completion time it produces.
public sealed class PacketRoutingTests
{
    private const float Dt = 1f / GameConstants.TickRate;
    private const double PassivePerTick = Dt * 100.0 / GameConstants.TerminalPassiveSeconds;
    private readonly ITestOutputHelper _output;
    public PacketRoutingTests(ITestOutputHelper output) { _output = output; }

    private static void Hold(Peer peer, bool on) => peer.Send(new C2S_Interact { Active = (byte)(on ? 1 : 0) });
    private static bool Has(TerminalState t, TerminalFlags flag) => (t.Flags & (byte)flag) != 0;
    private static S2C_WorldState World(Peer peer) => peer.Last<S2C_WorldState>();
    private static RouteDirection Right(S2C_SelfState s) => HackRoute.DirectionAt(s.RouteDirs, s.RouteIndex);
    private static RouteDirection Wrong(S2C_SelfState s) => (RouteDirection)(((int)Right(s) + 1) % 4);
    private static void Key(Peer peer, S2C_SelfState s, RouteDirection d) =>
        peer.Send(new C2S_HackInput { RouteId = s.RouteId, Direction = (byte)d });

    private static List<T> Fresh<T>(Peer peer) where T : IPacket
    {
        peer.Drain();
        var list = peer.Packets.OfType<T>().ToList();
        peer.Packets.RemoveAll(p => p is T);
        return list;
    }

    // One survivor (and, when asked, a second one) hacking terminal 0 from `progress`, the killer parked far away.
    private static RulesMatch Hacking(int survivors = 1, double progress = 50, int hackers = 1)
    {
        var m = RulesMatch.Start(survivors);
        var srv = m.F.Server;
        var t = srv.Map.TerminalPositions[0];
        srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
        for (int i = 0; i < hackers; i++) srv.PlaceForTests(m[i].Id, t.X - .5f, t.Z + (i == 0 ? 0 : .5f));
        srv.SetTerminalProgressForTests(0, progress);
        for (int i = 0; i < hackers; i++) Hold(m[i], true);
        m.Tick(2);
        return m;
    }

    [Fact]
    public void RoutesAreDealtOnlyWhileHackingAndAreDeterministicPerPrivateSeed()
    {
        List<byte> Routes()
        {
            using var m = Hacking();
            var srv = m.F.Server;
            var s = m[0];
            var dirs = new List<byte>();
            for (int i = 0; i < 6; i++)
            {
                var route = s.Last<S2C_SelfState>();
                Assert.NotEqual(0, route.RouteId);
                Assert.Equal((byte)GameConstants.RouteLength, route.RouteLength);
                Assert.Equal((route.RouteId, route.RouteDirs, route.RouteIndex), (srv.RouteForTests(s.Id).Id, srv.RouteForTests(s.Id).Dirs, srv.RouteForTests(s.Id).Index));
                dirs.Add(route.RouteDirs);
                Key(s, route, Wrong(route)); // an error deals the next route at once
                m.Tick();
                Assert.NotEqual(route.RouteId, s.Last<S2C_SelfState>().RouteId);
            }
            return dirs;
        }
        var a = Routes();
        _output.WriteLine(string.Join(",", a.Select(d => d.ToString("X2"))));
        Assert.Equal(a, Routes());
        Assert.True(a.Distinct().Count() > 1, "routes vary within a match");

        // No route before the first hacking tick, nor for a survivor that never hacks.
        using var idle = RulesMatch.Start(2);
        idle.Tick(5);
        Assert.All(idle[0].All<S2C_SelfState>(), s => Assert.Equal(0, s.RouteId));
    }

    [Fact]
    public void RightKeysStepTheRouteTheFullRouteBurstsEightPointsAndTheNextRouteWaitsForTheGap()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        var route = s.Last<S2C_SelfState>();
        double before = srv.TerminalProgressForTests(0);
        for (int i = 0; i < GameConstants.RouteLength - 1; i++)
        {
            Key(s, route, Right(route));
            m.Tick();
            var next = s.Last<S2C_SelfState>();
            Assert.Equal((route.RouteId, (byte)(i + 1)), (next.RouteId, next.RouteIndex));
            route = next;
        }
        Assert.Equal(before + 3 * PassivePerTick, srv.TerminalProgressForTests(0), 4);
        Key(s, route, Right(route));
        m.Tick();
        Assert.Equal(before + 4 * PassivePerTick + GameConstants.RouteBurstPoints, srv.TerminalProgressForTests(0), 4);
        Assert.Equal(0, s.Last<S2C_SelfState>().RouteId);
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));

        // The gap runs only while hacking: released for 2 s it does not shorten. This route was answered within 0.3 s
        // of being dealt, so the gap is the rest of RouteMinCycleSeconds (attempt 2), at least RouteGapSeconds.
        Hold(s, false);
        m.Tick(40);
        Assert.Equal(0, s.Last<S2C_SelfState>().RouteId);
        Hold(s, true);
        int gapTicks = (int)Math.Round(GameConstants.RouteGapSeconds / Dt);
        int cycleTicks = (int)Math.Round(GameConstants.RouteMinCycleSeconds / Dt);
        m.Tick(gapTicks - 2);
        Assert.Equal(0, s.Last<S2C_SelfState>().RouteId);
        m.Tick(cycleTicks - gapTicks + 2);
        Assert.NotEqual(0, s.Last<S2C_SelfState>().RouteId);
    }

    // Attempt 2: a route answered slower than RouteMinCycleSeconds - RouteGapSeconds is followed by exactly the gap.
    [Fact]
    public void ASlowRouteIsFollowedByTheShortGapAndAFastOneWaitsForTheMinimumCycle()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        m.Tick((int)Math.Round(GameConstants.RouteMinCycleSeconds / Dt) + 2); // the route is older than the minimum cycle
        var route = s.Last<S2C_SelfState>();
        for (int i = 0; i < GameConstants.RouteLength; i++)
        {
            route = s.Last<S2C_SelfState>();
            Key(s, route, Right(route));
            m.Tick();
        }
        Assert.Equal((byte)0, srv.RouteForTests(s.Id).Id);
        int gapTicks = (int)Math.Round(GameConstants.RouteGapSeconds / Dt), waited = 0;
        while (srv.RouteForTests(s.Id).Id == 0 && waited < 100) { m.Tick(); waited++; }
        _output.WriteLine($"next route after {waited} ticks (gap {gapTicks})");
        Assert.InRange(waited, gapTicks - 1, gapTicks + 1);
        Assert.True(GameConstants.RouteGapSeconds < 1f && GameConstants.RouteMinCycleSeconds > GameConstants.RouteGapSeconds);
    }

    // Attempt 2 (F1): the route is only on the wire while its owner hacks that terminal; it is kept (not re-rolled) and
    // comes back when the owner hacks there again.
    [Fact]
    public void TheRouteLeavesTheWireWhenHackingStopsAndComesBackUnchangedAtTheSameTerminal()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        var route = s.Last<S2C_SelfState>();
        Key(s, route, Right(route));
        m.Tick();
        var held = srv.RouteForTests(s.Id);
        Hold(s, false);
        m.Tick(2);
        Assert.Equal(0, s.Last<S2C_SelfState>().RouteId);
        Assert.Equal(held, srv.RouteForTests(s.Id));
        // A key while not hacking is dropped (no progress change, no error).
        double before = srv.TerminalProgressForTests(0);
        Key(s, route, Wrong(route));
        m.Tick();
        Assert.Equal(before, srv.TerminalProgressForTests(0), 6);
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));
        Hold(s, true);
        m.Tick(2);
        var back = s.Last<S2C_SelfState>();
        Assert.Equal((held.Id, held.Dirs, held.Index), (back.RouteId, back.RouteDirs, back.RouteIndex));
    }

    [Fact]
    public void AWrongKeyCostsThreePointsOverloadsTheTerminalForOnePointFiveSecondsAndDealsANewRoute()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        var route = s.Last<S2C_SelfState>();
        Key(s, route, Right(route));
        m.Tick();
        route = s.Last<S2C_SelfState>();
        double before = srv.TerminalProgressForTests(0);
        Key(s, route, Wrong(route));
        m.Tick();
        var fresh = s.Last<S2C_SelfState>();
        Assert.NotEqual(route.RouteId, fresh.RouteId);
        Assert.Equal(0, fresh.RouteIndex);
        Assert.Equal(before - GameConstants.RouteErrorPenaltyPoints + PassivePerTick, srv.TerminalProgressForTests(0), 4);
        // The killer sees only the Overloaded flash (and done / not done), survivors see it too.
        Assert.True(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));
        Assert.True(Has(World(s).Terminals[0], TerminalFlags.Overloaded));
        Assert.Equal(0, World(m.Killer).Terminals[0].Progress);
        int overloadTicks = (int)Math.Round(GameConstants.OverloadedSeconds / Dt);
        m.Tick(overloadTicks - 2);
        Assert.True(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));
        m.Tick(2);
        Assert.False(Has(World(m.Killer).Terminals[0], TerminalFlags.Overloaded));

        // A key for the old route (in flight when the error happened) is stale and dropped.
        double kept = srv.TerminalProgressForTests(0);
        Key(s, route, Wrong(route));
        m.Tick();
        Assert.Equal(kept + PassivePerTick, srv.TerminalProgressForTests(0), 4);
        Assert.Equal(fresh.RouteId, s.Last<S2C_SelfState>().RouteId);
        // Progress never goes below zero.
        srv.SetTerminalProgressForTests(0, 1);
        var now = s.Last<S2C_SelfState>();
        Key(s, now, Wrong(now));
        m.Tick();
        Assert.InRange(srv.TerminalProgressForTests(0), 0, PassivePerTick + 1e-6);
    }

    [Fact]
    public void RouteInputsAreRateLimitedToTwelveInAnyOneSecondWindow()
    {
        using var m = Hacking(progress: 90);
        var srv = m.F.Server;
        var s = m[0];
        var log = new List<string>();
        srv.Log += log.Add;
        int accepted = 0;
        for (int i = 0; i < 20; i++) // 20 ticks = 1 s, one wrong key per tick for the current route
        {
            var route = s.Last<S2C_SelfState>();
            Key(s, route, Wrong(route));
            m.Tick();
            if (s.Last<S2C_SelfState>().RouteId != route.RouteId) accepted++;
        }
        Assert.Equal(GameConstants.RouteInputsPerSecond, accepted);
        Assert.Equal(90 + 20 * PassivePerTick + 2 * PassivePerTick - 12 * GameConstants.RouteErrorPenaltyPoints,
            srv.TerminalProgressForTests(0), 3);
        // After the window slides past the first keys, inputs count again.
        var later = s.Last<S2C_SelfState>();
        Key(s, later, Wrong(later));
        m.Tick();
        Assert.NotEqual(later.RouteId, s.Last<S2C_SelfState>().RouteId);
        // Logs: one line per error at most twice a second per player, the skipped ones counted into the next line.
        var routeLines = log.Where(l => l.StartsWith("route player=" + s.Id + " ")).ToList();
        _output.WriteLine(string.Join("\n", routeLines));
        Assert.InRange(routeLines.Count, 2, 4);
        Assert.Contains(routeLines, l => l.Contains(" error ") && l.Contains("unlogged="));
    }

    [Fact]
    public void PassivePartIsNinetySecondsSoloAndCoHackersGetTheirOwnRoutesWithBurstsThatAddUp()
    {
        using (var solo = Hacking(progress: 0))
        {
            double start = solo.F.Server.TerminalProgressForTests(0);
            solo.Tick(200);
            Assert.Equal(start + 200 * PassivePerTick, solo.F.Server.TerminalProgressForTests(0), 3);
        }
        using var m = Hacking(survivors: 2, progress: 10, hackers: 2);
        var srv = m.F.Server;
        var a = m[0].Last<S2C_SelfState>();
        var b = m[1].Last<S2C_SelfState>();
        Assert.NotEqual(0, a.RouteId);
        Assert.NotEqual(0, b.RouteId);
        Assert.Equal(srv.RouteForTests(m[0].Id).Terminal, srv.RouteForTests(m[1].Id).Terminal);
        double before = srv.TerminalProgressForTests(0);
        for (int i = 0; i < GameConstants.RouteLength; i++)
        {
            a = m[0].Last<S2C_SelfState>();
            b = m[1].Last<S2C_SelfState>();
            Key(m[0], a, Right(a));
            Key(m[1], b, Right(b));
            m.Tick();
        }
        double passive = 4 * PassivePerTick * GameConstants.TwoHackerRate;
        // Attempt 2 (F4): each co-hacker's burst falls off with RouteBurstShare(2) = (1.8 / 2)^2.
        Assert.Equal(0.81f, GameConstants.RouteBurstShare(2), 4);
        Assert.Equal(1f, GameConstants.RouteBurstShare(1));
        Assert.Equal(before + passive + 2 * GameConstants.RouteBurstPoints * GameConstants.RouteBurstShare(2), srv.TerminalProgressForTests(0), 3);
        // A survivor's key never answers the other survivor's route.
        m.Tick(70);
        a = m[0].Last<S2C_SelfState>();
        b = m[1].Last<S2C_SelfState>();
        Assert.NotEqual(0, a.RouteId);
        Assert.NotEqual(0, b.RouteId);
        m[1].Send(new C2S_HackInput { RouteId = a.RouteId, Direction = (byte)Right(a) });
        m.Tick();
        Assert.Equal((a.RouteId, (byte)0), (srv.RouteForTests(m[0].Id).Id, srv.RouteForTests(m[0].Id).Index));
    }

    [Fact]
    public void OnlyTheHackerEverReceivesARouteAndTheKillerLearnsNothingButTheOverloadFlash()
    {
        using var m = Hacking(survivors: 2);
        var srv = m.F.Server;
        var s = m[0];
        var t = srv.Map.TerminalPositions[0];
        // The ally stands next to the hacker without hacking; the killer watches from 3 m.
        srv.PlaceForTests(m[1].Id, t.X - .5f, t.Z - 1.3f);
        srv.PlaceForTests(m.Killer.Id, t.X - 3.5f, t.Z);
        m.Tick();
        for (int i = 0; i < 40; i++)
        {
            var route = s.Last<S2C_SelfState>();
            if (route.RouteId != 0) Key(s, route, i % 9 == 8 ? Wrong(route) : Right(route));
            m.Tick();
        }
        Assert.Contains(s.All<S2C_SelfState>(), x => x.RouteId != 0);
        Assert.All(m[1].All<S2C_SelfState>(), x => Assert.Equal((0, 0, 0, 0), (x.RouteId, x.RouteLength, x.RouteDirs, x.RouteIndex)));
        Assert.All(m.Killer.All<S2C_SelfState>(), x => Assert.Equal((0, 0, 0, 0), (x.RouteId, x.RouteLength, x.RouteDirs, x.RouteIndex)));
        // The killer view keeps terminals at done / not done; survivors see exact progress (allies see hacking as before).
        Assert.All(m.Killer.All<S2C_WorldState>(), w => Assert.Equal(0, w.Terminals[0].Progress));
        Assert.True(World(m[1]).Terminals[0].Progress > 50);
        Assert.Contains(World(m[1]).Players, p => p.Id == s.Id && (p.Flags & (byte)PlayerFlags.Hacking) != 0);
    }

    [Fact]
    public void FinishingTheTerminalDropsTheRouteAndAnotherTerminalDealsANewOne()
    {
        using var m = Hacking(progress: 99);
        var srv = m.F.Server;
        var s = m[0];
        m.Tick(40);
        Assert.Equal(100, World(s).Terminals[0].Progress);
        Assert.Equal(0, s.Last<S2C_SelfState>().RouteId);
        Assert.Equal(-1, srv.RouteForTests(s.Id).Terminal);
        var t1 = srv.Map.TerminalPositions[1];
        srv.PlaceForTests(s.Id, t1.X - .5f, t1.Z);
        m.Tick(2);
        Assert.Equal(1, srv.RouteForTests(s.Id).Terminal);
        Assert.NotEqual(0, s.Last<S2C_SelfState>().RouteId);
    }

    // ---- bots

    private static SurvivorBotView View(byte id, byte dirs, byte index, double time) => new SurvivorBotView
    {
        Time = time, Hacking = true, RouteId = id, RouteLength = HackRoute.MaxLength, RouteDirs = dirs, RouteIndex = index
    };

    private static byte Next(byte id) => (byte)(id == byte.MaxValue ? 1 : id + 1);

    [Fact]
    public void TheBotSolverAnswersEachStepAfterPointOneFiveToPointThreeFiveSecondsWithAboutFivePercentErrors()
    {
        var solver = new RouteSolver();
        var random = new Random(5);
        int keys = 0, errors = 0;
        var waits = new List<double>();
        byte id = 1, index = 0, dirs = 0b11_10_01_00;
        double t = 0, shownAt = 0;
        while (keys < 4000)
        {
            if (solver.Update(View(id, dirs, index, t), true, random, out byte routeId, out byte direction))
            {
                Assert.Equal(id, routeId);
                waits.Add(t - shownAt);
                keys++;
                if (direction != (byte)HackRoute.DirectionAt(dirs, index)) { errors++; id = Next(id); index = 0; dirs = (byte)(dirs * 7 + 3); }
                else if (++index == HackRoute.MaxLength) { id = Next(id); index = 0; }
                shownAt = t + Dt; // the next view shows the answer one tick later
            }
            t += Dt;
        }
        _output.WriteLine($"keys {keys}, errors {errors} ({errors / (double)keys:P1}), wait {waits.Min():0.00}-{waits.Max():0.00} s, mean {waits.Average():0.000}");
        Assert.InRange(errors / (double)keys, 0.035, 0.065);
        Assert.True(waits.Min() >= RouteSolver.KeyMinSeconds - 1e-9);
        Assert.True(waits.Max() <= RouteSolver.KeyMaxSeconds + Dt + 1e-9);
        Assert.InRange(waits.Average(), 0.24, 0.31);
        // Not hacking, released, or no route: nothing is sent, and the wait starts again.
        var notHacking = View(id, dirs, index, t);
        notHacking.Hacking = false;
        Assert.False(solver.Update(notHacking, true, random, out _, out _));
        Assert.False(solver.Update(View(id, dirs, index, t + 5), false, random, out _, out _));
        Assert.False(solver.Update(View(0, dirs, 0, t + 10), true, random, out _, out _));
    }

    [Fact]
    public void SurvivorBotsSolveTheirRoutesInAnAllBotMatch()
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = 11, BotCount = 4, AutoStart = true });
        var log = new List<string>();
        server.Log += log.Add;
        for (int i = 0; i < 20 * 240; i++) server.Tick(Dt);
        int complete = log.Count(l => l.StartsWith("route ") && l.Contains(" complete "));
        int error = log.Count(l => l.StartsWith("route ") && l.Contains(" error "));
        _output.WriteLine($"route lines: complete {complete}, error {error}");
        Assert.True(complete >= 10, $"complete {complete}");
        Assert.True(error >= 1 && error < complete, $"error {error}");
    }

    // The solo completion time of the bots' solver against the real server (a peer holds Interact at terminal 0 and answers
    // its own SelfState route through RouteSolver exactly as a bot does), from 0 to 100 %.
    [Fact]
    public void ASoloBotSolverFinishesATerminalInAboutFortyToFortyFiveSeconds()
    {
        var times = new List<double>();
        for (int run = 0; run < 24; run++)
        {
            using var m = Hacking(progress: 0);
            var s = m[0];
            var solver = new RouteSolver();
            var random = new Random(1000 + run);
            int ticks = 2;
            var self = s.Last<S2C_SelfState>();
            byte progress = 0;
            while (progress < 100 && ticks < 20 * 120)
            {
                var selves = Fresh<S2C_SelfState>(s);
                if (selves.Count > 0) self = selves[^1];
                var worlds = Fresh<S2C_WorldState>(s);
                if (worlds.Count > 0) progress = worlds[^1].Terminals[0].Progress;
                if (progress >= 100) break;
                var view = View(self.RouteId, self.RouteDirs, self.RouteIndex, ticks * Dt);
                view.RouteLength = self.RouteLength;
                if (solver.Update(view, true, random, out byte id, out byte d)) s.Send(new C2S_HackInput { RouteId = id, Direction = d });
                m.Tick();
                ticks++;
            }
            times.Add(ticks * Dt);
        }
        times.Sort();
        double mean = times.Average();
        _output.WriteLine($"solo bot terminal time: mean {mean:0.0} s, min {times[0]:0.0}, p25 {times[6]:0.0}, median {times[12]:0.0}, p75 {times[18]:0.0}, max {times[^1]:0.0}");
        Assert.InRange(mean, 39, 46);
        Assert.True(times[^1] < GameConstants.TerminalPassiveSeconds, "solving always beats the idle holder");
    }
}
