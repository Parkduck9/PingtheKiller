using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.QA;
using Xunit;

namespace PingTheKiller.Tests.Core;

// U6f-sync-timing: a sync press is timed by its arrival on the server clock, not by the tick that handles it, against
// the moment the check left, minus the full RTT. The fixture clock is moved by hand; ticks are scheduled every Step.
public sealed class SyncTimingTests
{
    private const float Dt = 1f / GameConstants.TickRate;
    private const double Step = Dt;
    private const double Duration = GameConstants.SyncCheckDurationSeconds;
    private const double Wall0 = 1000;
    private const double MaxAge = 0.25; // the brief's clamp; GameServer.MaxPressAgeSeconds must match
    private static readonly double Tol = GameConstants.SyncToleranceSeconds / Duration;

    private static Player P(RulesMatch m, Peer peer) => (Player)Spy.Player(m.F.Server, peer.Id);
    private static double Now(RulesMatch m) => Spy.Server<double>(m.F.Server, "_now");

    private static List<T> Fresh<T>(Peer peer) where T : IPacket
    {
        peer.Drain();
        var list = peer.Packets.OfType<T>().ToList();
        peer.Packets.RemoveAll(p => p is T);
        return list;
    }

    private static double Mod(double value, double m) => ((value % m) + m) % m;

    // Fractions of the sweep named by the check.
    private static double GreatMiddle(S2C_SyncCheck c) => c.ZoneStart + c.GreatSize / 2.0;
    private static double GoodMiddle(S2C_SyncCheck c) => c.ZoneStart + (c.GreatSize + c.ZoneSize) / 2.0;

    // One survivor hacks terminal 0; its check is sent by the tick scheduled at Wall0, which starts checkLate after its
    // schedule and spends sendDelay before the check leaves.
    private sealed class Scenario : IDisposable
    {
        internal RulesMatch M = null!;
        internal Peer S = null!;
        internal S2C_SyncCheck Check = null!;
        internal double SentWall, SentSim, NextTick;
        private GameServer Server => M.F.Server;
        private TestClock Clock => M.F.Clock;
        public void Dispose() => M.Dispose();

        internal static Scenario Start(double sendDelay, double checkLate = 0)
        {
            var sc = new Scenario { M = RulesMatch.Start(1) };
            var m = sc.M;
            var srv = m.F.Server;
            sc.S = m[0];
            var t = srv.Map.TerminalPositions[0];
            srv.SyncChecksEnabled = true;
            srv.PlaceForTests(m.Killer.Id, 48.5f, 60);
            srv.PlaceForTests(sc.S.Id, t.X - .5f, t.Z);
            srv.SetTerminalProgressForTests(0, 50);
            sc.S.Send(new C2S_Interact { Active = 1 });
            m.Tick();
            Assert.Empty(Fresh<S2C_SyncCheck>(sc.S));
            srv.SetSyncTimerForTests(sc.S.Id, 0.01);
            double simStart = Now(m);
            m.F.Clock.Now = Wall0 + checkLate;
            m.F.Clock.Then = Wall0 + checkLate + sendDelay;
            srv.Tick(Dt, checkLate);
            sc.Check = Assert.Single(Fresh<S2C_SyncCheck>(sc.S));
            sc.SentWall = Wall0 + checkLate + sendDelay;
            sc.SentSim = P(m, sc.S).SyncSentAt;
            // Stamped when it leaves (tick start + lateness + time in the tick), not at the end of the tick.
            Assert.Equal(simStart + checkLate + sendDelay, sc.SentSim, 9);
            sc.NextTick = Wall0 + Step;
            return sc;
        }

        // Clock time at which a client with no frame delay sees the needle at `fraction`, plus the return trip.
        internal double At(double fraction, double rtt = 0) => SentWall + fraction * Duration + rtt;

        // On-schedule ticks run until the next tick (started `late` after its schedule) is the first to start at or
        // after `arrival`; no result may come before. The press is queued at `arrival` and that tick handles it.
        internal SyncResult Press(double arrival, double late = 0)
        {
            while (NextTick + late < arrival - 1e-9)
            {
                Clock.Now = NextTick;
                Server.Tick(Dt, 0);
                NextTick += Step;
                Assert.Empty(Fresh<S2C_SyncResult>(S));
            }
            Clock.Now = arrival;
            S.Send(new C2S_SyncPress { CheckId = Check.CheckId });
            Clock.Now = NextTick + late;
            Server.Tick(Dt, late);
            NextTick += Step;
            var result = Assert.Single(Fresh<S2C_SyncResult>(S));
            Assert.Equal(Check.CheckId, result.CheckId);
            return (SyncResult)result.Result;
        }
    }

    // The zone of the scenario's check (the match seed and the private sync stream are fixed).
    private static S2C_SyncCheck ProbeCheck()
    {
        using var sc = Scenario.Start(0);
        return sc.Check;
    }

    [Theory]
    [InlineData(0.000)]
    [InlineData(0.010)]
    [InlineData(0.025)]
    [InlineData(0.040)]
    [InlineData(0.049)]
    public void APressInTheMiddleOfTheGreatPartIsGreatWhereverItArrivesInTheTick(double offsetInTick)
    {
        var probe = ProbeCheck();
        // The send delay puts the arrival offsetInTick after a tick's scheduled start.
        double sendDelay = Mod(offsetInTick - GreatMiddle(probe) * Duration, Step);
        using var sc = Scenario.Start(sendDelay);
        Assert.Equal(probe.ZoneStart, sc.Check.ZoneStart);
        double arrival = sc.At(GreatMiddle(sc.Check));
        Assert.Equal(offsetInTick, Mod(arrival - Wall0 + 1e-9, Step) - 1e-9, 6);
        Assert.Equal(SyncResult.Great, sc.Press(arrival));
    }

    [Theory]
    [InlineData(0.000)]
    [InlineData(0.010)]
    [InlineData(0.025)]
    [InlineData(0.040)]
    [InlineData(0.049)]
    public void APressInTheMiddleOfTheRestOfTheZoneIsGoodWhereverItArrivesInTheTick(double offsetInTick)
    {
        var probe = ProbeCheck();
        double sendDelay = Mod(offsetInTick - GoodMiddle(probe) * Duration, Step);
        using var sc = Scenario.Start(sendDelay);
        Assert.Equal(SyncResult.Good, sc.Press(sc.At(GoodMiddle(sc.Check))));
    }

    // The loop wakes up late (Run passes how late): neither a late check tick nor a late press tick moves the judgement.
    [Theory]
    [InlineData(0.015, 0.0)]
    [InlineData(0.0, 0.015)]
    [InlineData(0.030, 0.004)]
    [InlineData(0.004, 0.045)]
    public void ALateTickStartDoesNotMoveTheJudgement(double checkLate, double pressLate)
    {
        using var sc = Scenario.Start(0.007, checkLate);
        Assert.Equal(SyncResult.Great, sc.Press(sc.At(GreatMiddle(sc.Check)), pressLate));
    }

    // where: fraction offset from an edge, in fractions of the sweep (0.0005 = 0.55 ms).
    [Theory]
    [InlineData("great start", 0.0005, SyncResult.Great)]
    [InlineData("great start", -0.0005, SyncResult.Good)]       // the leading tolerance band is Good
    [InlineData("great end", -0.0005, SyncResult.Great)]
    [InlineData("great end", 0.0005, SyncResult.Good)]
    [InlineData("lead tolerance", 0.0005, SyncResult.Good)]
    [InlineData("lead tolerance", -0.0005, SyncResult.Fail)]
    [InlineData("trail tolerance", -0.0005, SyncResult.Good)]
    [InlineData("trail tolerance", 0.0005, SyncResult.Fail)]
    public void EdgesAreJudgedOnTheArrivalNotOnTheTick(string edge, double delta, SyncResult expected)
    {
        foreach (double sendDelay in new[] { 0.0, 0.021, 0.049 })
        {
            using var sc = Scenario.Start(sendDelay);
            var c = sc.Check;
            double at = edge switch
            {
                "great start" => c.ZoneStart,
                "great end" => c.ZoneStart + c.GreatSize,
                "lead tolerance" => c.ZoneStart - Tol,
                _ => c.ZoneStart + c.ZoneSize + Tol
            };
            Assert.True(expected == sc.Press(sc.At(at + delta)), $"{edge} {delta:+0.0000;-0.0000} send delay {sendDelay}");
        }
    }

    [Fact]
    public void TheFullRttIsSubtracted()
    {
        using (var sc = Scenario.Start(0.012))
        {
            sc.M.F.Server.SetRttForTests(sc.S.Id, 0.1);
            // Arriving one RTT after the in-great moment: Great (without the RTT it would be 0.09 late, Good).
            Assert.Equal(SyncResult.Great, sc.Press(sc.At(GreatMiddle(sc.Check), 0.1)));
        }
        using (var sc = Scenario.Start(0.012))
        {
            sc.M.F.Server.SetRttForTests(sc.S.Id, 0.1);
            // Arriving only half an RTT after it: the judge dates it a further 50 ms back, into the leading tolerance.
            Assert.Equal(SyncResult.Good, sc.Press(sc.At(GreatMiddle(sc.Check), 0.05)));
        }
    }

    // A press made at the very end of the tolerance arrives one RTT later, possibly just after a tick started; the
    // check must still be pending then.
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.2)]
    public void APressMadeAtTheEndOfTheToleranceStillArrivesInTime(double rtt)
    {
        var probe = ProbeCheck();
        double end = probe.ZoneStart + probe.ZoneSize + Tol - 0.0005;
        // The arrival lands 1 ms after a tick's scheduled start, so it waits almost a whole tick.
        double sendDelay = Mod(0.001 - end * Duration - rtt, Step);
        using var sc = Scenario.Start(sendDelay);
        sc.M.F.Server.SetRttForTests(sc.S.Id, rtt);
        Assert.Equal(SyncResult.Good, sc.Press(sc.At(end, rtt)));
    }

    [Fact]
    public void APressQueuedLongBeforeTheTickIsClampedAndJudged()
    {
        Assert.Equal(MaxAge, GameServer.MaxPressAgeSeconds);
        using var sc = Scenario.Start(0.0);
        var m = sc.M;
        var srv = m.F.Server;
        var c = sc.Check;
        // On-schedule ticks until the next tick's simulated start is at least 0.25 s past the great middle.
        double clampedTarget = sc.SentSim + GreatMiddle(c) * Duration + MaxAge;
        while (Now(m) < clampedTarget)
        {
            m.F.Clock.Now = sc.NextTick;
            srv.Tick(Dt, 0);
            sc.NextTick += Step;
        }
        Assert.Empty(Fresh<S2C_SyncResult>(sc.S));
        // The RTT absorbs the overshoot, so the clamped press lands exactly on the great middle.
        srv.SetRttForTests(sc.S.Id, Now(m) - clampedTarget);
        // The loop stalls: the press is queued, and the next tick starts 0.4 s later (Run then drops its backlog).
        double arrival = sc.NextTick - Step + 0.001;
        m.F.Clock.Now = arrival;
        sc.S.Send(new C2S_SyncPress { CheckId = c.CheckId });
        m.F.Clock.Now = arrival + 0.4;
        srv.Tick(Dt, 0);
        // Unclamped, the press would be dated 0.15 s earlier still (Good); clamped to 0.25 s it is Great.
        Assert.Equal((byte)SyncResult.Great, Assert.Single(Fresh<S2C_SyncResult>(sc.S)).Result);
    }

    // Survivor bots press inside the server at the middle of the great part or of the rest of the zone, so their
    // shares follow the success rate and BotSyncGreatShare. An idle human killer and seven survivor bots, fixed seeds.
    [Fact]
    public void BotSyncSharesAreUnchanged()
    {
        int great = 0, good = 0, fail = 0;
        // Faster v2 matches produce fewer checks per match; retain the statistical sample floor with more seeds.
        for (int seed = 11; seed < 51; seed++)
        {
            using var f = new CoreFixture(seed: seed);
            bool ended = false;
            f.Server.Log += line =>
            {
                if (line.StartsWith("match end ", StringComparison.Ordinal)) ended = true;
                if (!line.StartsWith("sync player=", StringComparison.Ordinal)) return;
                if (line.EndsWith("result=Great", StringComparison.Ordinal)) great++;
                else if (line.EndsWith("result=Good", StringComparison.Ordinal)) good++;
                else if (line.EndsWith("result=Fail", StringComparison.Ordinal)) fail++;
            };
            var killer = f.Join("Idle killer");
            killer.Send(new C2S_SetBots { Count = 7 });
            killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            killer.Send(new C2S_StartMatch());
            for (int i = 0; i < 18005 && !ended; i++)
            {
                f.Tick();
                killer.Drain();
                killer.Packets.Clear();
            }
        }
        int total = great + good + fail;
        Assert.True(total >= 150, $"only {total} bot sync results");
        double successShare = (great + good) / (double)total;
        double greatShare = great / (double)(great + good);
        // Expected: 0.85 success, 0.2 of the successes Great (bounds at about 4 sigma for >= 150 results).
        Assert.InRange(successShare, GameServer.DefaultBotSyncSuccessRate - 0.12, GameServer.DefaultBotSyncSuccessRate + 0.12);
        Assert.InRange(greatShare, GameServer.BotSyncGreatShare - 0.1, GameServer.BotSyncGreatShare + 0.1);
    }
}
