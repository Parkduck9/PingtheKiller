using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

public sealed class MatchLengthTests
{
    private readonly ITestOutputHelper _output;
    public MatchLengthTests(ITestOutputHelper output) { _output = output; }

    internal sealed class MatchRun
    {
        internal int Ticks;
        internal S2C_GameResult? Result;
        internal int Completed, Required;
        internal double LastProgressAt;
        internal string Timeline = "";
        internal string End = "";
    }

    // One idle human (killer or survivor, chosen by the host role) and seven bots; the private seed is fixed by the hook.
    internal static MatchRun Run(int seed, RoleChoice humanRole)
    {
        using var f = new CoreFixture(seed: seed);
        var human = f.Join("Human");
        human.Send(new C2S_SetBots { Count = 7 });
        human.Send(new C2S_SetRole { RoleChoice = (byte)humanRole });
        human.Send(new C2S_StartMatch());
        f.Tick();
        human.Packets.Clear();
        var run = new MatchRun();
        int lastCompleted = 0;
        var timeline = new System.Text.StringBuilder();
        while (run.Result == null && run.Ticks <= 18001)
        {
            f.Tick();
            ++run.Ticks;
            human.Drain();
            foreach (var packet in human.Packets)
            {
                if (packet is S2C_GameResult result) run.Result = result;
                if (packet is not S2C_WorldState world) continue;
                int completed = world.Terminals.Count(t => t.Progress >= 100);
                run.Required = world.TerminalsRequired;
                if (completed != lastCompleted) timeline.Append($" {completed}@{run.Ticks / 20.0:0.0}s");
                if (completed != lastCompleted) run.LastProgressAt = run.Ticks / 20.0;
                lastCompleted = run.Completed = completed;
            }
            if (run.Result == null) run.End = string.Join(" ", f.Server.PlayerSnapshot().Where(p => p.Role == Role.Survivor)
                .Select(p => $"{(p.IsBot ? "b" : "H")}:{p.Status.ToString()[0]}"));
            human.Packets.Clear();
        }
        run.Timeline = timeline.ToString();
        return run;
    }

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    // Before U2c (perfect bots, straight-line steering): idle human killer 22.9 s, killer bot 53.7 s with 4 of 15
    // matches stalled at 3/4 terminals and one with the exit open but nobody escaping (seeds 3, 4, 5, 11, 15).
    // The killer-bot floor is below the 180 s target on purpose: the measured median is reported, not tuned to the gate.
    // U2d re-target: the idle-killer floor follows the U2d §3 target (median 2-6 min; was 180 s), and a bot time-out is
    // no longer a stall by definition: U2d allows up to 15 % time-outs against the killer bot, so at most 2 of the 15
    // runs may time out with a Normal bot, and only after real progress (at least one terminal).
    [Theory]
    [InlineData(RoleChoice.Killer, 120)]
    [InlineData(RoleChoice.Survivor, 120)]
    public void MedianMatchLengthReport(RoleChoice humanRole, double floor)
    {
        var lengths = new List<double>();
        int botTimeouts = 0;
        for (int seed = 1; seed <= 15; seed++)
        {
            var run = Run(seed, humanRole);
            lengths.Add(run.Ticks / 20.0);
            _output.WriteLine($"seed {seed}: {run.Ticks / 20.0:0.0}s winner={(Role)run.Result!.WinnerRole} reason={(GameEndReason)run.Result.Reason} terminals {run.Completed}/{run.Required}{run.Timeline} end [{run.End}]");
            // No stall: an idle killer always loses to the bots (GDD v2: they escape one by one; a straggler may still be
            // caught by the collapse); a time-out only happens when the last Normal survivor is the idle (hidden) human.
            if (humanRole == RoleChoice.Killer) Assert.Equal((byte)Role.Survivor, run.Result.WinnerRole);
            if (humanRole == RoleChoice.Killer) Assert.NotEqual((byte)GameEndReason.TimeUp, run.Result.Reason);
            if (run.Result.Reason == (byte)GameEndReason.TimeUp && run.End.Contains("b:N"))
            {
                ++botTimeouts;
                Assert.True(run.Completed >= 1, $"seed {seed}: time-out without any terminal");
            }
        }
        double median = Median(lengths);
        _output.WriteLine($"median {median:0.0}s, time-outs with a Normal bot {botTimeouts}");
        // U6a gate under GDD v2: at most 6 of 15 time-ups (raised from 4 by the orchestrator in U6a attempt 2: without
        // Lagging executions survivors live longer and more matches reach the limit). U6d sets the tuned v2 targets.
        Assert.True(botTimeouts <= 6, $"time-outs with a Normal bot {botTimeouts} of 15");
        Assert.True(median >= floor, $"median match length {median:0.0}s");
        // GDD v2: the U2d 360 s ceiling no longer applies (45 s terminals, 15 s switch); U6d sets the v2 length targets.
        if (humanRole == RoleChoice.Killer) Assert.True(median <= GameConstants.MatchLengthSeconds, $"median match length {median:0.0}s");
    }

    // Seeded reproduction of the 3/4 stall (killer-bot mode, private seeds 3 and 5 before U2c): survivor bots were
    // pinned at x = 20.4 and x = 17.6 against the wall (18,14)-(20,32) because straight-line steering with random
    // sidesteps cannot leave a wall whose span contains the target coordinate; in seed 5 a ThreatLevel 2 freeze with
    // the killer bot stuck on the other side of the same wall never released.
    [Theory]
    [InlineData(19.6f, 17f, 10f, 25f)]
    [InlineData(18.6f, 30f, 10f, 25f)]
    [InlineData(11.4f, 22.3f, 4f, 4f)]
    [InlineData(31f, 12f, 25f, 63f)]
    [InlineData(41.6f, 23f, 57.5f, 44f)]
    [InlineData(5f, 5f, 58f, 27f)]
    public void RouteReachesGoalsBehindWalls(float x, float z, float tx, float tz)
    {
        var map = new PingTheKiller.ServerCore.Map.FixedMap();
        var nav = new PingTheKiller.ServerCore.Map.NavGrid(map);
        float step = GameConstants.SurvivorSpeed / GameConstants.TickRate;
        int ticks = 0;
        while ((x - tx) * (x - tx) + (z - tz) * (z - tz) > 1 && ticks++ < 1200)
        {
            Assert.True(nav.Steer(x, z, tx, tz, out float dx, out float dz));
            var next = map.Move(x, z, dx * step, dz * step);
            x = next.X;
            z = next.Z;
        }
        Assert.True(ticks < 1200, $"stuck at ({x},{z})");
    }

    [Fact]
    public void AThreatFreezeAlwaysReleases_SurvivorBotsMoveAgainWhileTheKillerCampsNearby()
    {
        using var f = new CoreFixture(seed: 3);
        var killer = f.Join("K");
        killer.Send(new C2S_SetBots { Count = 4 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        // The killer walks to (7.5, 31) - within 12 m (ThreatLevel 2) of all four survivor spawns - and stays there.
        for (int i = 0; i < 400; i++)
        {
            var self = killer.Self();
            float dx = 7.5f - self.X, dz = 31f - self.Z;
            if (dx * dx + dz * dz < 0.01f) break;
            killer.Move(dx, dz);
            f.Tick();
        }
        killer.Move(0, 0);
        // Bots without a terminal wait hidden for their turn, so the property is: in every 20 s window some Normal
        // survivor bot hacks or travels at least 3 m (a freeze that never released kept every bot still for 600 s).
        for (int window = 0; window < 4; window++)
        {
            var start = f.Server.PlayerSnapshot().Where(p => p.Role == Role.Survivor && p.Status == PlayerStatus.Normal).ToDictionary(p => p.Id);
            if (start.Count == 0) break;
            bool moved = false;
            for (int i = 0; i < 20 * 20; i++)
            {
                f.Tick();
                foreach (var p in f.Server.PlayerSnapshot())
                    if (start.TryGetValue(p.Id, out var s) && (p.Hacking || (p.X - s.X) * (p.X - s.X) + (p.Z - s.Z) * (p.Z - s.Z) > 9)) moved = true;
            }
            Assert.True(moved, $"no survivor bot moved or hacked in window {window}");
        }
    }
}
