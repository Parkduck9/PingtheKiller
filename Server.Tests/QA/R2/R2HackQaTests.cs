using PingTheKiller.ServerCore;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.Core;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA.R2;

// R2-hack QA attempt 1 (Fable): adversarial and exploit checks on packet routing. QA only; no product code.
public sealed class R2HackQaTests
{
    private const float Dt = 1f / GameConstants.TickRate;
    private readonly ITestOutputHelper _output;
    public R2HackQaTests(ITestOutputHelper output) { _output = output; }

    private static void Hold(Peer peer, bool on) => peer.Send(new C2S_Interact { Active = (byte)(on ? 1 : 0) });
    private static RouteDirection Right(S2C_SelfState s) => HackRoute.DirectionAt(s.RouteDirs, s.RouteIndex);
    private static void Key(Peer peer, byte id, RouteDirection d) => peer.Send(new C2S_HackInput { RouteId = id, Direction = (byte)d });

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

    // FINDING F1 (major). Expected: a route is advertised only while its owner hacks (brief S1.1 "while a Survivor
    // hacks", HUD_SPEC 13.1 "only while E is held on a route"). Actual: after releasing E mid-route and walking away the
    // own SelfState keeps carrying the route, so the client's RouteKeys.Capturing(E held, ownHacking false, route.Held
    // true) takes WASD on ANY later E hold (exit switch, vault, pull, a Locked terminal) and movement sends zero.
    [Fact]
    public void F1_ARouteIsNotAdvertisedOnceItsOwnerStoppedHackingAndWalkedAway()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        var route = s.Last<S2C_SelfState>();
        Assert.NotEqual(0, route.RouteId);
        Key(s, route.RouteId, Right(route));
        m.Tick();
        Hold(s, false);
        var t = srv.Map.TerminalPositions[0];
        // R2-hack a2 (implementer): (t.X - 12, t.Z) is inside a wall on this map, so PlaceForTests threw before any
        // assert ran. Same intent: the first standable spot 12 m from the terminal (W, E, S, N), nothing in reach there.
        float awayX = t.X, awayZ = t.Z;
        foreach (var (dx, dz) in new[] { (-12f, 0f), (12f, 0f), (0f, -12f), (0f, 12f) })
            if (srv.Map.CanStand(t.X + dx, t.Z + dz)) { awayX = t.X + dx; awayZ = t.Z + dz; break; }
        Assert.NotEqual((t.X, t.Z), (awayX, awayZ));
        srv.PlaceForTests(s.Id, awayX, awayZ);
        m.Tick(40);
        Assert.False((s.Self().Flags & (byte)PlayerFlags.Hacking) != 0);
        // Holding E 12 m from the terminal (nothing in reach): still no hack, and no route should be on the wire.
        Hold(s, true);
        m.Tick(10);
        Assert.False((s.Self().Flags & (byte)PlayerFlags.Hacking) != 0);
        Assert.Equal(0, s.Last<S2C_SelfState>().RouteId);
    }

    [Fact]
    public void RouteKeysFromTheKillerABystanderALaggingSurvivorOrWithGuessedIdsChangeNothing()
    {
        using var m = Hacking(survivors: 2);
        var srv = m.F.Server;
        var route = m[0].Last<S2C_SelfState>();
        double before = srv.TerminalProgressForTests(0);
        var held = srv.RouteForTests(m[0].Id);
        for (int id = 0; id <= 255; id++)
            for (int d = 0; d < 4; d++)
            {
                if (id % 16 == 0 && d == 0) m.Tick(); // stay under the transport's pending-packet cap
                Key(m.Killer, (byte)id, (RouteDirection)d);
                Key(m[1], (byte)id, (RouteDirection)d);
            }
        m.Tick();
        Assert.Equal(held, srv.RouteForTests(m[0].Id));
        Assert.Equal(0, srv.RouteForTests(m[1].Id).Id);
        Assert.False((m[0].Last<S2C_WorldState>().Terminals[0].Flags & (byte)TerminalFlags.Overloaded) != 0);
        Assert.False(m.Killer.Connection.IsClosed);
        Assert.False(m[1].Connection.IsClosed);
        // Only passive progress happened.
        Assert.InRange(srv.TerminalProgressForTests(0) - before, 0, 20 * Dt * 100.0 / GameConstants.TerminalPassiveSeconds + 0.01);
        _output.WriteLine($"route id {route.RouteId} untouched after 2048 foreign keys");
    }

    [Fact]
    public void StaleIdsOfTheOwnEarlierRoutesAreDroppedAndDoNotCountAsErrors()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        var first = s.Last<S2C_SelfState>();
        Key(s, first.RouteId, (RouteDirection)(((int)Right(first) + 1) % 4)); // an error: a new route at once
        m.Tick();
        var second = s.Last<S2C_SelfState>();
        Assert.NotEqual(first.RouteId, second.RouteId);
        double before = srv.TerminalProgressForTests(0);
        for (int i = 0; i < 8; i++) Key(s, first.RouteId, (RouteDirection)(i % 4));
        m.Tick();
        var after = srv.RouteForTests(s.Id);
        Assert.Equal((second.RouteId, (byte)0), (after.Id, after.Index));
        Assert.True(srv.TerminalProgressForTests(0) >= before);
    }

    // The best a scripted client can do: every key right at the rate limit. Reported, and bounded: it must not beat
    // the passive part plus one burst per (gap + 4 keys).
    [Fact]
    public void APerfectScriptedClientNeedsAboutThirtySecondsPerTerminal()
    {
        using var m = Hacking(progress: 0);
        var srv = m.F.Server;
        var s = m[0];
        int ticks = 0;
        while (srv.TerminalProgressForTests(0) < 100 && ticks < 4000)
        {
            var route = s.Last<S2C_SelfState>();
            // One key per tick (20/s offered, 12/s accepted at most).
            if (route.RouteId != 0 && route.RouteIndex < route.RouteLength) Key(s, route.RouteId, Right(route));
            m.Tick();
            ticks++;
        }
        double seconds = ticks * Dt;
        _output.WriteLine($"perfect client: {seconds:0.0} s per terminal");
        Assert.InRange(seconds, 24, 36);
    }

    // Co-hack: passive 1.8x for two, and each hacker's burst adds in full.
    [Fact]
    public void TwoPerfectCoHackersReport()
    {
        using var m = Hacking(survivors: 2, progress: 0, hackers: 2);
        var srv = m.F.Server;
        int ticks = 0;
        while (srv.TerminalProgressForTests(0) < 100 && ticks < 4000)
        {
            for (int i = 0; i < 2; i++)
            {
                var route = m[i].Last<S2C_SelfState>();
                if (route.RouteId != 0 && route.RouteIndex < route.RouteLength) Key(m[i], route.RouteId, Right(route));
            }
            m.Tick();
            ticks++;
        }
        double seconds = ticks * Dt;
        _output.WriteLine($"two perfect co-hackers: {seconds:0.0} s per terminal (old rule: {45 / 1.8:0.0} s)");
        Assert.InRange(seconds, 12, 22);
    }

    // Releasing and re-holding E must not re-roll the route, skip the gap or dodge an error.
    [Fact]
    public void ReleasingEKeepsTheSameRouteAndIndex()
    {
        using var m = Hacking();
        var srv = m.F.Server;
        var s = m[0];
        var route = s.Last<S2C_SelfState>();
        Key(s, route.RouteId, Right(route));
        m.Tick();
        var held = srv.RouteForTests(s.Id);
        Hold(s, false);
        m.Tick(5);
        Hold(s, true);
        m.Tick(3);
        Assert.Equal(held, srv.RouteForTests(s.Id));
    }
    // Solo completion time from 0 by key pace (seconds per key) and error habit (every n-th key wrong; 0 = none).
    // Reported for the balance review: the 3 s gap dominates, so key speed changes little.
    [Theory]
    [InlineData("fast 0.15 s/key", 0.15, 0, 26, 36)]
    [InlineData("slow 0.5 s/key", 0.5, 0, 30, 45)]
    [InlineData("very slow 1.0 s/key", 1.0, 0, 35, 60)]
    [InlineData("sloppy 0.15 s/key, every 5th wrong", 0.15, 5, 30, 60)]
    [InlineData("idle (no keys)", 0, 0, 89, 91)]
    public void SoloCompletionTimeByPace(string name, double keySeconds, int wrongEvery, double min, double max)
    {
        using var m = Hacking(progress: 0);
        var srv = m.F.Server;
        var s = m[0];
        int ticks = 0, keys = 0, errors = 0;
        double readyAt = -1;
        int seen = -1;
        while (srv.TerminalProgressForTests(0) < 100 && ticks < 4000)
        {
            double now = ticks * Dt;
            var route = s.Last<S2C_SelfState>();
            if (keySeconds > 0 && route.RouteId != 0 && route.RouteIndex < route.RouteLength)
            {
                int key = (route.RouteId << 8) | route.RouteIndex;
                if (key != seen) { seen = key; readyAt = now; }
                if (now - readyAt >= keySeconds - 1e-6)
                {
                    keys++;
                    bool wrong = wrongEvery > 0 && keys % wrongEvery == 0;
                    if (wrong) errors++;
                    Key(s, route.RouteId, wrong ? (RouteDirection)(((int)Right(route) + 2) % 4) : Right(route));
                    seen = -1;
                }
            }
            m.Tick();
            ticks++;
        }
        double seconds = ticks * Dt;
        _output.WriteLine($"{name}: {seconds:0.0} s per terminal, {keys} keys ({keys / seconds * 60:0} per minute), {errors} errors");
        Assert.InRange(seconds, min, max);
    }
}
