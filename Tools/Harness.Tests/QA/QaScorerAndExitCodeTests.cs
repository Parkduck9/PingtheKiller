using System.Text.Json;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

public sealed class QaScorerAndExitCodeTests
{
    private static readonly string[] Ids = { "H1", "H2", "H3", "H4", "H5", "H6" };
    private static readonly int[] Max = { 25, 20, 20, 15, 10, 10 };

    [Fact]
    public void AllSixtyFourPassFailCombinationsHaveExactTotalsAndGateFlag()
    {
        for (int mask = 0; mask < 64; mask++)
        {
            var results = Ids.Select((id, i) => Scorer.Result(id, (mask & (1 << i)) != 0, "x")).ToArray();
            var report = Scorer.Summarize(results);
            int expected = Enumerable.Range(0, 6).Where(i => (mask & (1 << i)) != 0).Sum(i => Max[i]);
            Assert.Equal(expected, report.Total);
            bool gates = (mask & 1) != 0 && (mask & 4) != 0 && (mask & 8) != 0; // H1, H3, H4
            Assert.Equal(gates, report.GatesPassed);
            Assert.Equal(expected, report.Items.Sum(i => i.Score));
            Assert.All(report.Items, i => Assert.Equal(i.Passed ? i.Max : 0, i.Score));
        }
        Assert.Equal(100, Max.Sum());
    }

    [Fact]
    public void ThresholdEdgeNinetyPassesAndEightyNineFails()
    {
        var items = new[] { Scorer.Result("H1", true, "x") };
        Assert.True(new ScoreReport(90, true, items).MeetsThreshold(90));
        Assert.False(new ScoreReport(89, true, items).MeetsThreshold(90));
        Assert.True(new ScoreReport(91, true, items).MeetsThreshold(90));
        Assert.True(new ScoreReport(0, true, items).MeetsThreshold(0));
    }

    [Fact]
    public void FullHarnessResultsOfH1ToH5AreExactlyNinety()
    {
        var report = Scorer.Summarize(Ids.Select(id => Scorer.Result(id, id != "H6", "x")));
        Assert.Equal(90, report.Total);
        Assert.True(report.MeetsThreshold(90));
        Assert.False(report.MeetsThreshold(91));
    }

    [Fact]
    public void FailedGateBlocksEvenWhenTotalMeetsTheThreshold()
    {
        // H3 fails: total 70, threshold 70 -> total >= threshold, but a gate failed.
        var report = Scorer.Summarize(Ids.Select(id => Scorer.Result(id, id is not ("H3" or "H6"), "x")));
        Assert.Equal(70, report.Total);
        Assert.False(report.GatesPassed);
        Assert.False(report.MeetsThreshold(70));
        Assert.False(report.MeetsThreshold(0));
    }

    [Fact]
    public void ScorerRejectsForgedItems()
    {
        Assert.Throws<ArgumentException>(() => Scorer.Summarize(new[] { new CheckResult("H1", 25, 25, false, true, "gate flag forged") }));
        Assert.Throws<ArgumentException>(() => Scorer.Summarize(new[] { new CheckResult("H5", 99, 10, false, true, "inflated") }));
        Assert.Throws<ArgumentException>(() => Scorer.Summarize(new[] { new CheckResult("H5", 5, 10, false, false, "partial") }));
        Assert.Throws<ArgumentException>(() => Scorer.Summarize(new[] { new CheckResult("H9", 0, 0, false, false, "unknown") }));
    }

    [Fact]
    public async Task H6IsSkippedScoreZeroNotPassedAndNeverCounted()
    {
        // U3b: H6 is a real check now (no "skipped"); with no server at the port it fails in setup.
        var (report, _) = await Qa.RunAsync(SilentServer.ClosedPort(), new[] { "H6" });
        var h6 = Assert.Single(report.Items);
        Assert.Equal("H6", h6.Id);
        Assert.Equal(0, h6.Score);
        Assert.Equal(10, h6.Max);
        Assert.False(h6.Passed);
        Assert.Contains("[setup]", h6.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, report.Total);
        Assert.False(report.GatesPassed);
    }

    [Fact]
    public void JsonShapeIsExactlyTotalGatesPassedItems()
    {
        var report = Scorer.Summarize(Ids.Select(id => Scorer.Result(id, id == "H1", "ev \"q\" é")));
        AssertJsonShape(report.ToJson(), 6);
    }

    internal static void AssertJsonShape(string json, int itemCount)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(new[] { "total", "gatesPassed", "items" }, root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("total").ValueKind);
        Assert.True(root.GetProperty("gatesPassed").ValueKind is JsonValueKind.True or JsonValueKind.False);
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(itemCount, items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal(new[] { "id", "score", "max", "gate", "passed", "evidence" },
                item.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Equal(JsonValueKind.String, item.GetProperty("id").ValueKind);
            Assert.Equal(JsonValueKind.Number, item.GetProperty("score").ValueKind);
            Assert.Equal(JsonValueKind.Number, item.GetProperty("max").ValueKind);
            Assert.True(item.GetProperty("gate").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(item.GetProperty("passed").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.Equal(JsonValueKind.String, item.GetProperty("evidence").ValueKind);
        }
    }

    // ---- end-to-end through the real executable: exit codes + report file ----

    [Fact]
    public async Task ExecutableExitsZeroWhenAllGatesPassAndThresholdMet_AndWritesSpecJson()
    {
        await using var server = new QaServer();
        string report = Path.Combine(Path.GetTempPath(), $"qa-harness-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output, error) = await Qa.RunHarnessProcessAsync(new[]
            {
                "--port", server.Port.ToString(), "--only", "H1,H3,H4", "--threshold", "60", "--timeout", "3", "--report", report
            }, TimeSpan.FromSeconds(90));
            Assert.True(exit == 0, $"exit={exit}\n{output}\n{error}");
            string json = await File.ReadAllTextAsync(report);
            AssertJsonShape(json, 3);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(60, doc.RootElement.GetProperty("total").GetInt32());
            Assert.True(doc.RootElement.GetProperty("gatesPassed").GetBoolean());
            Assert.Equal(new[] { "H1", "H3", "H4" }, doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetString()).ToArray());
        }
        finally { File.Delete(report); }
    }

    [Fact]
    public async Task FullRunAgainstCorrectFakeScoresExactlyNinetyAndH6IsNotCounted()
    {
        await using var server = new QaServer();
        string report = Path.Combine(Path.GetTempPath(), $"qa-harness-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output, error) = await Qa.RunHarnessProcessAsync(new[]
            {
                "--port", server.Port.ToString(), "--duration", "1.5", "--threshold", "90", "--timeout", "3", "--report", report
            }, TimeSpan.FromSeconds(120));
            Assert.True(exit == 0, $"exit={exit}\n{output}\n{error}");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.Equal(90, doc.RootElement.GetProperty("total").GetInt32());
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!);
            Assert.Equal(6, items.Count);
            foreach (string id in new[] { "H1", "H2", "H3", "H4", "H5" })
                Assert.True(items[id].GetProperty("passed").GetBoolean(), $"{id}: {items[id].GetProperty("evidence").GetString()}");
            Assert.False(items["H6"].GetProperty("passed").GetBoolean());
            Assert.Equal(0, items["H6"].GetProperty("score").GetInt32());
            Assert.Equal(1, server.MatchStarts);
            Assert.False(server.UnexpectedFlow);
        }
        finally { File.Delete(report); }
    }

    [Fact]
    public async Task ExecutableExitsNonZeroWhenAGateFailsEvenIfTotalMeetsTheThreshold()
    {
        // H3 leaks (gate fails) but H1+H4 still total 40 >= threshold 40.
        await using var server = new QaServer(new QaFaults { LeakFlagOnly = true });
        string report = Path.Combine(Path.GetTempPath(), $"qa-harness-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output, error) = await Qa.RunHarnessProcessAsync(new[]
            {
                "--port", server.Port.ToString(), "--only", "H1,H3,H4", "--threshold", "40", "--timeout", "3", "--report", report
            }, TimeSpan.FromSeconds(90));
            Assert.NotEqual(0, exit);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.Equal(40, doc.RootElement.GetProperty("total").GetInt32());
            Assert.False(doc.RootElement.GetProperty("gatesPassed").GetBoolean());
        }
        finally { File.Delete(report); }
    }

    [Fact]
    public async Task ExecutableExitsNonZeroWhenThresholdIsOnePointAboveTotal()
    {
        await using var server = new QaServer();
        string report = Path.Combine(Path.GetTempPath(), $"qa-harness-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output, error) = await Qa.RunHarnessProcessAsync(new[]
            {
                "--port", server.Port.ToString(), "--only", "H1,H3,H4", "--threshold", "61", "--timeout", "3", "--report", report
            }, TimeSpan.FromSeconds(90));
            Assert.Equal(1, exit);
        }
        finally { File.Delete(report); }
    }

    [Fact]
    public async Task ExecutableExitsNonZeroForH6OnlyEvenWithZeroThreshold()
    {
        string report = Path.Combine(Path.GetTempPath(), $"qa-harness-{Guid.NewGuid():N}.json");
        try
        {
            // U3b: H6 now talks to a server, so never let it hit a developer's server on the default port 7777.
            var (exit, _, _) = await Qa.RunHarnessProcessAsync(new[] { "--only", "H6", "--threshold", "0", "--report", report,
                "--port", SilentServer.ClosedPort().ToString() }, TimeSpan.FromSeconds(30));
            Assert.NotEqual(0, exit);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
        }
        finally { File.Delete(report); }
    }

    [Theory]
    [InlineData("--only", "H7")]
    [InlineData("--threshold", "abc")]
    [InlineData("--bogus", "1")]
    public async Task ExecutableExitsTwoOnInvalidArguments(string key, string value)
    {
        var (exit, _, _) = await Qa.RunHarnessProcessAsync(new[] { key, value }, TimeSpan.FromSeconds(30));
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task ExecutableAgainstSilentServerExitsNonZeroInBoundedTime()
    {
        await using var server = new SilentServer();
        string report = Path.Combine(Path.GetTempPath(), $"qa-harness-{Guid.NewGuid():N}.json");
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var (exit, _, _) = await Qa.RunHarnessProcessAsync(new[]
            {
                "--port", server.Port.ToString(), "--timeout", "0.3", "--duration", "1", "--threshold", "0", "--report", report
            }, TimeSpan.FromSeconds(60));
            Assert.Equal(1, exit);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(6, doc.RootElement.GetProperty("items").GetArrayLength());
        }
        finally { File.Delete(report); }
    }
}
