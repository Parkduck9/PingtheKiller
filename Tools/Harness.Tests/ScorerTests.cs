using System.Text.Json;
using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class ScorerTests
{
    [Fact]
    public void U2aMaximumIs90AndH6IsHonest()
    {
        var report = Scorer.Summarize(new[]
        {
            Scorer.Result("H1", true, "framing"), Scorer.Result("H2", true, "sync"),
            Scorer.Result("H3", true, "masking"), Scorer.Result("H4", true, "abuse"),
            Scorer.Result("H5", true, "reconnect"), Scorer.Result("H6", false, "skipped (needs U2b)")
        });
        Assert.Equal(90, report.Total);
        Assert.True(report.GatesPassed);
        Assert.True(report.MeetsThreshold(90));
        Assert.False(report.MeetsThreshold(91));
        Assert.Equal(0, report.Items[5].Score);
        Assert.False(report.Items[5].Passed);
    }

    [Fact]
    public void FailedGateBlocksEvenWhenThresholdIsZero()
    {
        var report = Scorer.Summarize(new[]
        {
            Scorer.Result("H1", true, "ok"), Scorer.Result("H2", true, "ok"),
            Scorer.Result("H3", false, "leak"), Scorer.Result("H4", true, "ok"),
            Scorer.Result("H5", true, "ok"), Scorer.Result("H6", false, "skipped (needs U2b)")
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
