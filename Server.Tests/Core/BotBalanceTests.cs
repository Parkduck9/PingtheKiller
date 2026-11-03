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
        // GDD v2 (U6a): the U2d win and length bands are reported only until U6d sets the v2 targets; the gate is that 1v7
        // all-bot matches end through the rules (at most 30 % time-ups).
        new object[] { "1v7", 7, 0.35, 0.65, 180.0, 480.0, 0.30 },
        new object[] { "1v3", 3, 0.25, 0.75, 120.0, 480.0, 1.0 },
        new object[] { "1v1", 1, 0.20, 0.80, 90.0, 480.0, 1.0 },
        new object[] { "idle killer + 7", 0, 0.90, 1.0, 120.0, 360.0, 1.0 },
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
        _output.WriteLine($"U2d bands (not gated under GDD v2): win {winMin:P0}-{winMax:P0}, median {medianMin:0}-{medianMax:0} s");
        Assert.Equal(Seeds, summary.Matches);
        Assert.True(summary.TimeoutRate <= timeoutMax, $"timeouts {summary.TimeoutRate:P0}");
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
