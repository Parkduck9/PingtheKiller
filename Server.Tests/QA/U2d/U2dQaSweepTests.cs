using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.Core;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA.U2d;

public sealed class U2dQaSweepTests
{
    private readonly ITestOutputHelper _output;
    public U2dQaSweepTests(ITestOutputHelper output) { _output = output; }

    internal const int SeedStart = 1001;
    internal const int SeedCount = 40;

    internal sealed class QaMatchStats
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

    private sealed class LogTally
    {
        internal readonly QaMatchStats Stats = new();
        internal bool Ended;

        internal void OnLine(string m)
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

    private static QaMatchStats RunAllBotsMatch(int seed, int survivors)
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = survivors + 1, AutoStart = true });
        var tally = new LogTally();
        server.Log += tally.OnLine;
        GameConstants.TerminalsFor(survivors, out int required, out _);
        tally.Stats.Required = required;
        while (!tally.Ended && tally.Stats.Ticks < BotBalance.MaxTicks)
        {
            server.Tick(1f / GameConstants.TickRate);
            tally.Stats.Ticks++;
        }
        tally.Stats.Seed = seed;
        if (!tally.Ended) throw new InvalidOperationException($"QA seed {seed}: no match end after {BotBalance.MaxTicks} ticks");
        return tally.Stats;
    }

    private static QaMatchStats RunIdleKillerMatch(int seed, int survivors = 7)
    {
        using var f = new CoreFixture(seed: seed);
        var tally = new LogTally();
        f.Server.Log += tally.OnLine;
        var human = f.Join("QA Idle Killer");
        human.Send(new C2S_SetBots { Count = (byte)survivors });
        human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        human.Send(new C2S_StartMatch());
        f.Tick();
        GameConstants.TerminalsFor(survivors, out int required, out _);
        tally.Stats.Required = required;
        tally.Stats.Ticks = 1;
        while (!tally.Ended && tally.Stats.Ticks < BotBalance.MaxTicks)
        {
            f.Tick();
            tally.Stats.Ticks++;
            human.Drain();
            human.Packets.Clear();
        }
        tally.Stats.Seed = seed;
        if (!tally.Ended) throw new InvalidOperationException($"QA seed {seed}: no match end after {BotBalance.MaxTicks} ticks");
        return tally.Stats;
    }

    internal sealed class QaSummary
    {
        internal int Matches;
        internal double WinRate, Median, P25, P75, TimeoutRate, RescueShare;
        internal double Terminals, Captures, Rescues, Executions, LagTimeouts;
        internal string Reasons = "";

        internal string Row(string config) =>
            $"| {config} | {WinRate * 100:0}% | {Median:0} s ({P25:0}/{P75:0}) | {Reasons} | {Terminals:0.00} | " +
            $"{Captures:0.00} / {Rescues:0.00} / {Executions:0.00} | {TimeoutRate * 100:0}% | {RescueShare * 100:0}% |";
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double idx = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(idx), upper = (int)Math.Ceiling(idx);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (idx - lower) * (sorted[upper] - sorted[lower]);
    }

    private static QaSummary Summarize(IReadOnlyList<QaMatchStats> runs)
    {
        var lengths = runs.Select(r => r.Seconds).OrderBy(v => v).ToList();
        return new QaSummary
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
        "SurvivorsEliminated" => "down",
        "TimeUp" => "time",
        _ => reason
    };

    public static IEnumerable<object[]> SweepConfigs() => new[]
    {
        // config, survivors (0 = idle killer + 7 bots), winMin, winMax, medianMin, medianMax, timeoutMax, reqRescueShare
        new object[] { "1v7", 7, 0.35, 0.65, 180.0, 480.0, 0.15, 0.30 },
        new object[] { "1v3", 3, 0.25, 0.75, 120.0, 480.0, 1.0, 0.0 },
        new object[] { "1v1", 1, 0.20, 0.80, 90.0, 480.0, 1.0, 0.0 },
        new object[] { "idle killer + 7", 0, 0.90, 1.0, 120.0, 360.0, 1.0, 0.0 },
    };

    [Theory]
    [MemberData(nameof(SweepConfigs))]
    public void IndependentSeedSweep_MeetsSection3TargetsOnFreshSeeds(string config, int survivors,
        double winMin, double winMax, double medianMin, double medianMax, double timeoutMax, double reqRescueShare)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var seeds = Enumerable.Range(SeedStart, SeedCount);
        var runsBag = new ConcurrentBag<QaMatchStats>();
        Parallel.ForEach(seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, seed =>
        {
            var r = survivors == 0 ? RunIdleKillerMatch(seed) : RunAllBotsMatch(seed, survivors);
            runsBag.Add(r);
        });
        var runs = runsBag.OrderBy(r => r.Seed).ToList();
        var summary = Summarize(runs);

        _output.WriteLine($"=== QA INDEPENDENT SWEEP: {config} (Seeds {SeedStart}..{SeedStart + SeedCount - 1}) ===");
        _output.WriteLine(BotBalance.Header);
        _output.WriteLine(summary.Row(config));
        _output.WriteLine($"Lagging timeouts per match: {summary.LagTimeouts:0.00}; elapsed: {watch.Elapsed.TotalSeconds:0.0} s");

        string resultPath = Path.Combine(AppContext.BaseDirectory, "qa_sweep_results.txt");
        lock (_output)
        {
            File.AppendAllText(resultPath, summary.Row(config) + Environment.NewLine);
        }

        Assert.InRange(summary.WinRate, winMin, winMax);
        Assert.InRange(summary.Median, medianMin, medianMax);
        Assert.True(summary.TimeoutRate <= timeoutMax, $"timeouts {summary.TimeoutRate:P0} > max {timeoutMax:P0}");
        if (reqRescueShare > 0)
            Assert.True(summary.RescueShare >= reqRescueShare, $"rescue share {summary.RescueShare:P0} < required {reqRescueShare:P0}");
    }

    [Fact]
    public void Determinism_SameSeedProducesBitIdenticalMatch()
    {
        foreach (int seed in new[] { 1005, 1020 })
        {
            var a = RunAllBotsMatch(seed, 7);
            var b = RunAllBotsMatch(seed, 7);
            Assert.Equal(a.Ticks, b.Ticks);
            Assert.Equal(a.Winner, b.Winner);
            Assert.Equal(a.Reason, b.Reason);
            Assert.Equal(a.Terminals, b.Terminals);
            Assert.Equal(a.Captures, b.Captures);
            Assert.Equal(a.Rescues, b.Rescues);
            Assert.Equal(a.Executions, b.Executions);
            Assert.Equal(a.Escaped, b.Escaped);
        }
    }

    [Fact]
    public void RulesUntouched_GameConstantsMatchGddSections3To5()
    {
        // Movement speeds
        Assert.Equal(4.0f, GameConstants.SurvivorSpeed);
        Assert.Equal(4.6f, GameConstants.KillerSpeed);

        // Interaction radii
        Assert.Equal(1.2f, GameConstants.CaptureRadius);
        Assert.Equal(1.5f, GameConstants.RescueRadius);
        Assert.Equal(1.5f, GameConstants.TerminalRadius);
        Assert.Equal(2.0f, GameConstants.ExitRadius);

        // Timers & cooldowns
        Assert.Equal(600f, GameConstants.MatchLengthSeconds);
        Assert.Equal(60f, GameConstants.LaggingTimeoutSeconds);
        Assert.Equal(2f, GameServer.ExecutionGraceSeconds);
        Assert.Equal(3f, GameConstants.RescueTimeSeconds);
        Assert.Equal(2f, GameConstants.PostRescueInvulnerabilitySeconds);
        Assert.Equal(12f, GameConstants.KillerSkillCooldownSeconds);
        Assert.Equal(15f, GameConstants.SurvivorSkillCooldownSeconds);

        // Terminals
        GameConstants.TerminalsFor(1, out int req1, out int placed1);
        Assert.Equal(2, req1); Assert.Equal(4, placed1);
        GameConstants.TerminalsFor(3, out int req3, out int placed3);
        Assert.Equal(3, req3); Assert.Equal(5, placed3);
        GameConstants.TerminalsFor(7, out int req7, out int placed7);
        Assert.Equal(4, req7); Assert.Equal(6, placed7);
    }

    [Fact]
    public void SandboxBot_UsesUpdateSandboxBotPathOnly()
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = 42, BotCount = 4, AutoStart = true, Sandbox = true });
        for (int i = 0; i < 200; i++) server.Tick(0.05f);
        // In sandbox mode, no hacking or capture happens, terminals are untouched, match never ends before 600s
        var snapshot = server.PlayerSnapshot();
        Assert.All(snapshot, p => Assert.False(p.Hacking));
        Assert.All(snapshot, p => Assert.False(p.Rescuing));
        Assert.All(snapshot, p => Assert.Equal(PlayerStatus.Normal, p.Status));
    }
}
