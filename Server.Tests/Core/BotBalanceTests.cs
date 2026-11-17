using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

// U2d balance targets (docs/briefs/U2d-a1.md §3), measured on 40 fixed private seeds per configuration with the
// all-bot hook (no human) or an idle human killer. Filter the long sweeps out with --filter "Category!=BalanceSweep".
public sealed class BotBalanceTests
{
    private readonly ITestOutputHelper _output;
    public BotBalanceTests(ITestOutputHelper output) { _output = output; }

    internal const int Seeds = 40;

    public static IEnumerable<object[]> Configs() => new[]
    {
        // config, survivors (0 = idle human killer + 7 bots), win min/max, median min/max seconds, max timeout share.
        // U6d: enforce the GDD v2 win, median and timeout bands on both fixed seed sets.
        // B1 (11/18): a stronger Killer at 1v7 and 1v3 (target 40-55 %). These 40-seed sets are SMOKE checks only: their
        // +-8 point noise means the 1v7 30-55 % band also passes the pre-B1 balance (main scores 50.0 / 52.5 here). The real 1v7
        // balance target is the large-seed mean, checked by LargeSeedOneVersusSevenSurvivorWinRateIsOnTarget.
        new object[] { "1v7", 7, 0.30, 0.55, 540.0, 660.0, 0.10 },
        new object[] { "1v3", 3, 0.30, 0.65, 0.0, 660.0, 0.20 },
        new object[] { "1v1", 1, 0.15, 0.85, 0.0, 900.0, 0.30 },
        new object[] { "idle killer + 7", 0, 0.90, 1.0, 0.0, 900.0, 1.0 },
    };

    [Theory]
    [Trait("Category", "BalanceSweep")]
    [MemberData(nameof(Configs))]
    public void BalanceSweepMeetsTheTargets(string config, int survivors, double winMin, double winMax,
        double medianMin, double medianMax, double timeoutMax)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var runs = BotBalance.Sweep(Enumerable.Range(1, Seeds),
            seed => survivors == 0 ? BotBalance.RunIdleKiller(seed) : BotBalance.RunAllBots(seed, survivors));
        var summary = BotBalance.Summarize(runs);
        foreach (var run in runs) _output.WriteLine(BotBalance.Detail(run));
        _output.WriteLine(BotBalance.Header);
        _output.WriteLine(summary.Row(config));
        _output.WriteLine($"lagging timeouts per match {summary.LagTimeouts:0.00}; sweep {watch.Elapsed.TotalSeconds:0.0} s");
        _output.WriteLine($"GDD v2 bands: win {winMin:P0}-{winMax:P0}, median {medianMin:0}-{medianMax:0} s");
        Assert.Equal(Seeds, summary.Matches);
        Assert.InRange(summary.WinRate, winMin, winMax);
        Assert.InRange(summary.Median, medianMin, medianMax);
        Assert.True(summary.TimeoutRate <= timeoutMax, $"timeouts {summary.TimeoutRate:P0}");
        if (survivors == 0)
        {
            int exits = runs.Sum(r => r.ExitEscapes), collapse = runs.Sum(r => r.CollapseLosses);
            _output.WriteLine($"idle survivor outcomes: exit escapes {exits}/{Seeds * 7}, collapse losses {collapse}/{Seeds * 7}");
            Assert.True(exits > Seeds * 7 / 2, "most survivors must escape through exits");
            Assert.True(exits > collapse);
        }
    }

    // integ-1118 F1: the 1v7 target is the mean over many seeds (about 42 %; standard error about 1.1 points at 2000 seeds). Main
    // scores 50.5 % on these seeds and fails the 38-46 % band. About 45 s.
    [Fact]
    [Trait("Category", "BalanceSweep")]
    public void LargeSeedOneVersusSevenSurvivorWinRateIsOnTarget()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var runs = BotBalance.Sweep(Enumerable.Range(20001, 2000), seed => BotBalance.RunAllBots(seed, 7));
        var summary = BotBalance.Summarize(runs);
        _output.WriteLine(BotBalance.Header);
        _output.WriteLine(summary.Row("1v7 x2000"));
        _output.WriteLine($"sweep {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(2000, summary.Matches);
        Assert.InRange(summary.WinRate, 0.38, 0.46);
    }

    // The all-bot hook is deterministic per private seed: the same seed replays the same match.
    [Fact]
    public void AllBotMatchesAreDeterministicPerPrivateSeed()
    {
        foreach (int seed in new[] { 3, 17 })
        {
            var a = BotBalance.Detail(BotBalance.RunAllBots(seed, 7));
            var b = BotBalance.Detail(BotBalance.RunAllBots(seed, 7));
            _output.WriteLine(a);
            Assert.Equal(a, b);
        }
    }
}
