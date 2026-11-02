using System.Collections.Concurrent;
using System.Text;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Tests.Core;

// U2d measurement harness: runs whole matches in-process on the private seed hook and tallies them from the server log.
// All-bot matches use AutoStart with BotCount = survivors + 1 and no connection at all, so the killer is a bot too.
internal static class BotBalance
{
    internal const int MaxTicks = 12000 + 5;

    internal sealed class MatchStats
    {
        internal int Seed;
        internal int Ticks;
        internal string Winner = "";
        internal string Reason = "";
        internal int Terminals, Required;
        internal int Captures, Rescues, Executions, SecondCaptures, LagTimeouts, Escaped;
        internal double Seconds => Ticks / (double)GameConstants.TickRate;
        internal bool SurvivorWin => Winner == "Survivor";
        internal bool TimeUp => Reason == "TimeUp";
    }

    // Parses the server log lines written by SetStatus, Hack and FinishMatch.
    private sealed class Tally
    {
        internal readonly MatchStats Stats = new();
        internal bool Ended;
        internal void Line(string m)
        {
            if (m.StartsWith("status ", StringComparison.Ordinal))
            {
                if (m.EndsWith("reason=capture", StringComparison.Ordinal)) Stats.Captures++;
                else if (m.EndsWith("reason=second capture", StringComparison.Ordinal)) { Stats.Captures++; Stats.SecondCaptures++; }
                else if (m.EndsWith("reason=execution", StringComparison.Ordinal)) Stats.Executions++;
                else if (m.EndsWith("reason=timeout", StringComparison.Ordinal)) Stats.LagTimeouts++;
                else if (m.EndsWith("reason=rescue", StringComparison.Ordinal) || m.EndsWith("reason=emergency restore", StringComparison.Ordinal)) Stats.Rescues++;
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
            }
        }
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
            // Drain so the in-memory connection never hits its receive cap and gets replaced by a bot.
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
        "SurvivorEscaped" => "escaped",
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
        $"cap {r.Captures} (2nd {r.SecondCaptures}) res {r.Rescues} exe {r.Executions} lagTimeout {r.LagTimeouts} escaped {r.Escaped}";
}
