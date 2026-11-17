using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Tests.Core;

// U2d measurement harness: runs whole matches in-process on the private seed hook and tallies them from the server log.
// All-bot matches use AutoStart with BotCount = survivors + 1 and no connection at all, so the killer is a bot too.
internal static class BotBalance
{
    internal const int MaxTicks = 18000 + 5;

    internal sealed class MatchStats
    {
        internal int Seed;
        internal int Ticks;
        internal string Winner = "";
        internal string Reason = "";
        internal int Terminals, Required;
        internal int Captures, Rescues, Executions, SecondCaptures, LagTimeouts, Escaped;
        internal int ExitEscapes, CollapseLosses;
        // U6d-a3 progress invariant: windows of StallSeconds or more without any progress event, the longest gap, and
        // the match time at which the first such window started (-1 when none).
        internal int StallWindows;
        internal double LongestStallSeconds, FirstStallAt = -1;
        internal double PoweredAt = -1, OpenAt = -1;
        internal double Seconds => Ticks / (double)GameConstants.TickRate;
        internal bool SurvivorWin => Winner == "Survivor";
        internal bool TimeUp => Reason == "TimeUp";
        // Reported only, not asserted: a time-up with no capture. A lone survivor that keeps evading the killer and keeps
        // working can lose the race against the clock without any freeze (user decision 2026-11-08, after the U6d-a3
        // flee fix); the StallSeconds window catches freezes and the balance bands cap time-ups.
        internal bool SilentTimeUp => TimeUp && Captures == 0;
    }

    // A match keeps moving: no StallSeconds window passes without terminal or switch progress rising, a capture, a
    // rescue, an escape or a hatch event. Terminal and switch progress are not in the log, so they are sampled from the
    // server state after every tick (test code only).
    internal const double StallSeconds = 150;
    private static readonly FieldInfo TerminalProgressField =
        typeof(GameServer).GetField("_progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo SwitchSecondsField =
        typeof(GameServer).GetField("_switchSeconds", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed class ProgressWatch
    {
        private double[] _terminals = Array.Empty<double>(), _switches = Array.Empty<double>();
        private int _lastTick;

        internal void Mark(MatchStats stats, int tick)
        {
            double gap = (tick - _lastTick) / (double)GameConstants.TickRate;
            if (gap > stats.LongestStallSeconds) stats.LongestStallSeconds = gap;
            if (gap >= StallSeconds && stats.StallWindows++ == 0) stats.FirstStallAt = _lastTick / (double)GameConstants.TickRate;
            _lastTick = tick;
        }

        internal void Sample(GameServer server, MatchStats stats, int tick)
        {
            bool terminals = Rising((double[])TerminalProgressField.GetValue(server)!, ref _terminals);
            bool switches = Rising((double[])SwitchSecondsField.GetValue(server)!, ref _switches);
            if (terminals || switches) Mark(stats, tick);
        }

        private static bool Rising(double[] now, ref double[] before)
        {
            bool rose = false;
            if (before.Length != now.Length) before = new double[now.Length];
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] > before[i] + 1e-9) rose = true;
                before[i] = now[i];
            }
            return rose;
        }
    }

    // Parses the server log lines written by SetStatus, Hack and FinishMatch.
    private sealed class Tally
    {
        internal readonly MatchStats Stats = new();
        internal readonly ProgressWatch Watch = new();
        internal bool Ended;
        internal void Line(string m)
        {
            if (ProgressEvent(m)) Watch.Mark(Stats, Stats.Ticks);
            if (m.StartsWith("exits powered", StringComparison.Ordinal)) Stats.PoweredAt = Stats.Seconds;
            if (m.StartsWith("exit ", StringComparison.Ordinal) && m.EndsWith(" open", StringComparison.Ordinal) && Stats.OpenAt < 0) Stats.OpenAt = Stats.Seconds;
            if (m.StartsWith("status ", StringComparison.Ordinal))
            {
                if (m.EndsWith("reason=capture", StringComparison.Ordinal)) Stats.Captures++;
                else if (m.EndsWith("reason=second capture", StringComparison.Ordinal)) { Stats.Captures++; Stats.SecondCaptures++; }
                else if (m.EndsWith("reason=execution", StringComparison.Ordinal)) Stats.Executions++;
                else if (m.EndsWith("reason=timeout", StringComparison.Ordinal)) Stats.LagTimeouts++;
                else if (m.EndsWith("reason=rescue", StringComparison.Ordinal) || m.EndsWith("reason=emergency restore", StringComparison.Ordinal)) Stats.Rescues++;
                else if (m.EndsWith("Escaped reason=exit", StringComparison.Ordinal)) Stats.ExitEscapes++;
                else if (m.EndsWith("Eliminated reason=collapse", StringComparison.Ordinal)) Stats.CollapseLosses++;
            }
            else if (m.StartsWith("terminal ", StringComparison.Ordinal) && m.EndsWith(" complete", StringComparison.Ordinal)) Stats.Terminals++;
            else if (m.StartsWith("match end ", StringComparison.Ordinal))
            {
                foreach (var part in m.Split(' '))
                {
                    if (part.StartsWith("winner=", StringComparison.Ordinal)) Stats.Winner = part.Substring(7);
                    else if (part.StartsWith("reason=", StringComparison.Ordinal)) Stats.Reason = part.Substring(7);
                    else if (part.StartsWith("escaped=", StringComparison.Ordinal)) Stats.Escaped = int.Parse(part.Substring(8));
                }
                Ended = true;
                // The end of the match closes the last window (a time-up tail with no progress counts as a stall).
                Watch.Mark(Stats, Stats.Ticks);
            }
        }

        // Progress events visible in the log: captures, rescues, escapes, hatch events, completed terminals, open exits.
        private static bool ProgressEvent(string m) =>
            (m.StartsWith("status ", StringComparison.Ordinal) &&
             (m.EndsWith("reason=capture", StringComparison.Ordinal) || m.EndsWith("reason=second capture", StringComparison.Ordinal) ||
              m.EndsWith("reason=rescue", StringComparison.Ordinal) || m.EndsWith("reason=emergency restore", StringComparison.Ordinal) ||
              m.Contains(" Escaped reason=", StringComparison.Ordinal))) ||
            m.StartsWith("hatch ", StringComparison.Ordinal) ||
            (m.StartsWith("terminal ", StringComparison.Ordinal) && m.EndsWith(" complete", StringComparison.Ordinal)) ||
            (m.StartsWith("exit ", StringComparison.Ordinal) && m.EndsWith(" open", StringComparison.Ordinal));
    }

    // 1 killer bot + survivors survivor bots, no human.
    internal static MatchStats RunAllBots(int seed, int survivors)
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = survivors + 1, AutoStart = true });
        var tally = new Tally();
        server.Log += tally.Line;
        GameConstants.TerminalsFor(survivors, out int required, out _);
        tally.Stats.Required = required;
        while (!tally.Ended && tally.Stats.Ticks < MaxTicks)
        {
            server.Tick(1f / GameConstants.TickRate);
            tally.Stats.Ticks++;
            if (!tally.Ended) tally.Watch.Sample(server, tally.Stats, tally.Stats.Ticks);
        }
        tally.Stats.Seed = seed;
        if (!tally.Ended) throw new InvalidOperationException($"seed {seed}: no match end after {MaxTicks} ticks");
        return tally.Stats;
    }

    // An idle human killer (never moves, never casts) and seven survivor bots.
    internal static MatchStats RunIdleKiller(int seed, int survivors = 7)
    {
        using var f = new CoreFixture(seed: seed);
        var tally = new Tally();
        f.Server.Log += tally.Line;
        var human = f.Join("Idle killer");
        human.Send(new C2S_SetBots { Count = (byte)survivors });
        human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        human.Send(new C2S_StartMatch());
        f.Tick();
        GameConstants.TerminalsFor(survivors, out int required, out _);
        tally.Stats.Required = required;
        // The tick that started the match already advanced the match clock.
        tally.Stats.Ticks = 1;
        while (!tally.Ended && tally.Stats.Ticks < MaxTicks)
        {
            f.Tick();
            tally.Stats.Ticks++;
            if (!tally.Ended) tally.Watch.Sample(f.Server, tally.Stats, tally.Stats.Ticks);
            // Drain so the in-memory connection never hits its receive cap and gets replaced by a bot.
            human.Drain();
            human.Packets.Clear();
        }
        tally.Stats.Seed = seed;
        if (!tally.Ended) throw new InvalidOperationException($"seed {seed}: no match end after {MaxTicks} ticks");
        return tally.Stats;
    }

    // U6d-a3: one idle human survivor (never moves, never interacts) with six survivor bots and a killer bot, so four of
    // the seven survivors must escape to win (the live offline check of attempt 2 stalled in this setup).
    internal static MatchStats RunIdleSurvivor(int seed, int botSurvivors = 6)
    {
        using var f = new CoreFixture(seed: seed);
        var tally = new Tally();
        f.Server.Log += tally.Line;
        var human = f.Join("Idle survivor");
        human.Send(new C2S_SetBots { Count = (byte)(botSurvivors + 1) });
        human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
        human.Send(new C2S_StartMatch());
        f.Tick();
        GameConstants.TerminalsFor(botSurvivors + 1, out int required, out _);
        tally.Stats.Required = required;
        tally.Stats.Ticks = 1;
        while (!tally.Ended && tally.Stats.Ticks < MaxTicks)
        {
            f.Tick();
            tally.Stats.Ticks++;
            if (!tally.Ended) tally.Watch.Sample(f.Server, tally.Stats, tally.Stats.Ticks);
            // Q5a: once every bot survivor is resolved only the idle human (who never acts) is left, so no progress is
            // possible by construction; that wait is not a bot stall (seed 1012 reached it after the chase props).
            if (!tally.Ended && !f.Server.PlayerSnapshot().Any(p => p.IsBot && p.Role == Role.Survivor &&
                    (p.Status == PlayerStatus.Normal || p.Status == PlayerStatus.Lagging)))
                tally.Watch.Mark(tally.Stats, tally.Stats.Ticks);
            human.Drain();
            human.Packets.Clear();
        }
        tally.Stats.Seed = seed;
        if (!tally.Ended) throw new InvalidOperationException($"seed {seed}: no match end after {MaxTicks} ticks");
        return tally.Stats;
    }

    internal static List<MatchStats> Sweep(IEnumerable<int> seeds, Func<int, MatchStats> run)
    {
        var results = new ConcurrentBag<MatchStats>();
        Parallel.ForEach(seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
            seed => results.Add(run(seed)));
        return results.OrderBy(r => r.Seed).ToList();
    }

    internal sealed class Summary
    {
        internal int Matches;
        internal double WinRate, Median, P25, P75, TimeoutRate, RescueShare;
        internal double Terminals, Captures, Rescues, Executions, LagTimeouts;
        internal string Reasons = "";

        internal string Row(string config) =>
            $"| {config} | {WinRate * 100:0}% | {Median:0} s ({P25:0}/{P75:0}) | {Reasons} | {Terminals:0.00} | " +
            $"{Captures:0.00} / {Rescues:0.00} / {Executions:0.00} | {TimeoutRate * 100:0}% | {RescueShare * 100:0}% |";
    }

    internal const string Header =
        "| Config | Survivor win | Median length (p25/p75) | End reasons | Terminals done | Captures / rescues / executions per match | Timeouts | Matches with >= 1 rescue |\n" +
        "|---|---|---|---|---|---|---|---|";

    internal static Summary Summarize(IReadOnlyList<MatchStats> runs)
    {
        var lengths = runs.Select(r => r.Seconds).OrderBy(v => v).ToList();
        return new Summary
        {
            Matches = runs.Count,
            WinRate = runs.Count(r => r.SurvivorWin) / (double)runs.Count,
            Median = Percentile(lengths, 0.5),
            P25 = Percentile(lengths, 0.25),
            P75 = Percentile(lengths, 0.75),
            TimeoutRate = runs.Count(r => r.TimeUp) / (double)runs.Count,
            RescueShare = runs.Count(r => r.Rescues > 0) / (double)runs.Count,
            Terminals = runs.Average(r => r.Terminals),
            Captures = runs.Average(r => r.Captures),
            Rescues = runs.Average(r => r.Rescues),
            Executions = runs.Average(r => r.Executions),
            LagTimeouts = runs.Average(r => r.LagTimeouts),
            Reasons = string.Join(", ", runs.GroupBy(r => r.Reason).OrderByDescending(g => g.Count()).Select(g => $"{Short(g.Key)} {g.Count()}"))
        };
    }

    private static string Short(string reason) => reason switch
    {
        "AllResolved" => "resolved",
        "Collapse" => "collapse",
        "AllSurvivorsDown" => "all down",
        "TimeUp" => "time up",
        _ => reason
    };

    // Linear interpolation between closest ranks; values must be sorted.
    internal static double Percentile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return double.NaN;
        double position = (sorted.Count - 1) * q;
        int low = (int)Math.Floor(position), high = (int)Math.Ceiling(position);
        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }

    internal static string Detail(MatchStats r) =>
        $"seed {r.Seed,3}: {r.Seconds,6:0.0}s {r.Winner,-8} {r.Reason,-16} terminals {r.Terminals}/{r.Required} " +
        $"cap {r.Captures} (2nd {r.SecondCaptures}) res {r.Rescues} exe {r.Executions} lagTimeout {r.LagTimeouts} escaped {r.Escaped} " +
        $"pw {r.PoweredAt:0} op {r.OpenAt:0} stall {r.StallWindows} (longest {r.LongestStallSeconds:0} s{(r.FirstStallAt >= 0 ? $" from {r.FirstStallAt:0} s" : "")})";
}
