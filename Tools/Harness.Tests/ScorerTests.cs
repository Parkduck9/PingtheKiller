using System.Text.Json;
using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class ScorerTests
{
    [Fact]
    public void FailedH6Scores90AndAllSixPassingScores100()
    {
        var withoutH6 = Scorer.Summarize(new[]
        {
            Scorer.Result("H1", true, "framing"), Scorer.Result("H2", true, "sync"),
            Scorer.Result("H3", true, "masking"), Scorer.Result("H4", true, "abuse"),
            Scorer.Result("H5", true, "reconnect"), Scorer.Result("H6", false, "[capture: killer to A] A captured ... not observed")
        });
        Assert.Equal(90, withoutH6.Total);
        Assert.True(withoutH6.GatesPassed);
        Assert.True(withoutH6.MeetsThreshold(90)); // default --threshold stays 90
        Assert.False(withoutH6.MeetsThreshold(91));
        Assert.Equal(0, withoutH6.Items[5].Score);
        Assert.False(withoutH6.Items[5].Passed);

        var all = Scorer.Summarize(Scorer.Ids.Select(id => Scorer.Result(id, true, "ok")));
        Assert.Equal(100, all.Total);
        Assert.True(all.GatesPassed);
        Assert.True(all.MeetsThreshold(100));
        Assert.Equal(10, all.Items.Single(i => i.Id == "H6").Score);
        Assert.Equal(90, new Options().Threshold);
    }

    [Fact]
    public void JsonOfAPassingH6IsARealCheckResult()
    {
        var report = Scorer.Summarize(new[] { Scorer.Result("H6", true, "setup: 3 humans ... GameResult x1 per human") });
        using var json = JsonDocument.Parse(report.ToJson());
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("H6", item.GetProperty("id").GetString());
        Assert.Equal(10, item.GetProperty("score").GetInt32());
        Assert.Equal(10, item.GetProperty("max").GetInt32());
        Assert.False(item.GetProperty("gate").GetBoolean());
        Assert.True(item.GetProperty("passed").GetBoolean());
        Assert.DoesNotContain("skipped", item.GetProperty("evidence").GetString());
        Assert.Equal(10, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public void FailedGateBlocksEvenWhenThresholdIsZero()
    {
        var report = Scorer.Summarize(new[]
        {
            Scorer.Result("H1", true, "ok"), Scorer.Result("H2", true, "ok"),
            Scorer.Result("H3", false, "leak"), Scorer.Result("H4", true, "ok"),
            Scorer.Result("H5", true, "ok"), Scorer.Result("H6", false, ScenarioCheck.NeedsOwnServer)
        });
        Assert.Equal(70, report.Total);
        Assert.False(report.GatesPassed);
        Assert.False(report.MeetsThreshold(0));
    }

    [Fact]
    public void UnselectedGateIsUnverifiedAndScoresAreNotRescaled()
    {
        var report = Scorer.Summarize(new[] { Scorer.Result("H1", true, "ok") });
        Assert.Equal(25, report.Total);
        Assert.False(report.GatesPassed);
        Assert.False(report.MeetsThreshold(25));
    }

    [Fact]
    public void JsonHasExactlyTheSpecifiedLiteralShape()
    {
        var report = Scorer.Summarize(new[] { Scorer.Result("H1", true, "10000 ordered") });
        using var json = JsonDocument.Parse(report.ToJson());
        var root = json.RootElement;
        Assert.Equal(new[] { "total", "gatesPassed", "items" }, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.False(root.GetProperty("gatesPassed").GetBoolean());
        var item = root.GetProperty("items")[0];
        Assert.Equal(new[] { "id", "score", "max", "gate", "passed", "evidence" }, item.EnumerateObject().Select(p => p.Name));
        Assert.Equal("H1", item.GetProperty("id").GetString());
        Assert.Equal(25, item.GetProperty("score").GetInt32());
        Assert.Equal(25, item.GetProperty("max").GetInt32());
        Assert.True(item.GetProperty("gate").GetBoolean());
        Assert.True(item.GetProperty("passed").GetBoolean());
        Assert.Equal("10000 ordered", item.GetProperty("evidence").GetString());
    }

    [Fact]
    public void InconsistentOrDuplicateScoresAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Scorer.Summarize(new[] { new CheckResult("H3", 20, 20, true, false, "fake pass") }));
        Assert.Throws<ArgumentException>(() => Scorer.Summarize(new[] { Scorer.Result("H1", true, "a"), Scorer.Result("H1", true, "b") }));
    }

    [Theory]
    [InlineData("--duration", "NaN")]
    [InlineData("--duration", "0")]
    [InlineData("--timeout", "Infinity")]
    [InlineData("--only", "H7")]
    [InlineData("--only", "H1,,H3")]
    [InlineData("--threshold", "101")]
    [InlineData("--port", "0")]
    public void InvalidOptionsFail(string key, string value) => Assert.Throws<ArgumentException>(() => Options.Parse(new[] { key, value }));
}
