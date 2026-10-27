using System.Diagnostics;
using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA;

internal static class Qa
{
    public static Options Opts(int port, string[] only, double timeoutSeconds = 2, double durationSeconds = 2) => new()
    {
        Port = port,
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        Duration = TimeSpan.FromSeconds(durationSeconds),
        Only = new HashSet<string>(only)
    };

    // Runs the harness through its public entry point with a hard test-side deadline.
    public static async Task<(ScoreReport Report, TimeSpan Elapsed)> RunAsync(int port, string[] only,
        double timeoutSeconds = 2, double durationSeconds = 2, double hardLimitSeconds = 45)
    {
        using var hard = new CancellationTokenSource(TimeSpan.FromSeconds(hardLimitSeconds));
        var clock = Stopwatch.StartNew();
        var report = await HarnessRunner.RunAsync(Opts(port, only, timeoutSeconds, durationSeconds), hard.Token);
        return (report, clock.Elapsed);
    }

    public static CheckResult Item(ScoreReport report, string id) => report.Items.Single(i => i.Id == id);

    // A broken fake must not earn any points for the targeted check and must trip the gate where applicable.
    public static void AssertFailed(ScoreReport report, string id, string? evidenceContains = null, string? label = null)
    {
        var item = Item(report, id);
        // Optional evidence dump for the QA report: set QA_EVIDENCE_FILE to collect why each broken fake failed.
        string? dump = Environment.GetEnvironmentVariable("QA_EVIDENCE_FILE");
        if (dump != null)
            lock (typeof(Qa)) File.AppendAllText(dump, $"{label ?? "-"}\t{id}\t{item.Evidence.ReplaceLineEndings(" ")}\n");
        Assert.False(item.Passed, $"FALSE PASS: {id} passed against a broken fake. Evidence: {item.Evidence}");
        Assert.True(item.Score < item.Max, $"{id} score {item.Score}/{item.Max}");
        Assert.Equal(0, item.Score);
        Assert.False(string.IsNullOrWhiteSpace(item.Evidence));
        if (item.Gate) Assert.False(report.GatesPassed);
        Assert.DoesNotContain(report.Items, i => i.Id == id && i.Passed);
        if (evidenceContains != null)
            Assert.Contains(evidenceContains, item.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertPassed(ScoreReport report, string id)
    {
        var item = Item(report, id);
        Assert.True(item.Passed, $"{id} failed against a correct fake: {item.Evidence}");
        Assert.Equal(item.Max, item.Score);
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunHarnessProcessAsync(
        IEnumerable<string> args, TimeSpan limit)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "Harness.dll");
        Assert.True(File.Exists(dll), $"Harness.dll not found next to test assembly: {dll}");
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(limit);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            Assert.Fail($"Harness process did not exit within {limit} (hang). Output so far: {await stdout}");
        }
        return (process.ExitCode, await stdout, await stderr);
    }
}
