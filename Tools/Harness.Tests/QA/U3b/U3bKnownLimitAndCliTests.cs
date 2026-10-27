using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3b;

// ---- Known limits of the by-spec scan --------------------------------------------------------------------
// These tests CHARACTERISE behaviour that the U3b spec accepts (the scan tolerates "legitimate" survivor fields
// and drains for ~1 s). They assert the current outcome so that any change is noticed; each is a documented
// finding in docs/qa/U3b-qa-a1.md, NOT a claim that the behaviour is desirable.
public sealed class U3bKnownLimitLaunderedThroughSurvivorEntryTests
{
    [Fact]
    public async Task KnownLimit_KillerXZ_CopiedIntoOneSurvivorEntryXZ_IsNotDetected()
    {
        await using var server = new QaRulesServer(new Knobs
        {
            SurvivorFrame = Leaks.Once(c =>
            {
                var list = c.World.Players;
                int b = list.FindIndex(p => p.Id == 3);
                var entry = list[b];
                entry.X = c.KillerX;
                entry.Z = c.KillerZ;
                list[b] = entry;
            })
        });
        var (h6, _) = await U3bRun.H6Async(server, "KNOWN-LIMIT laundered-survivor-entry");
        Assert.True(h6.Passed, "characterisation changed: " + h6.Evidence);
    }
}

public sealed class U3bKnownLimitLaunderedThroughSkillEffectCasterTests
{
    [Fact]
    public async Task KnownLimit_KillerXZ_InSkillEffectWithASurvivorCasterId_IsNotDetected()
    {
        await using var server = new QaRulesServer(new Knobs
        {
            SurvivorFrame = Leaks.Once(c => c.Send(new S2C_SkillEffect { CasterId = 2, SkillId = 0, X = c.KillerX, Z = c.KillerZ, Duration = 1 }))
        });
        var (h6, _) = await U3bRun.H6Async(server, "KNOWN-LIMIT laundered-skilleffect-caster");
        Assert.True(h6.Passed, "characterisation changed: " + h6.Evidence);
    }
}

public sealed class U3bKnownLimitDuplicateResultAfterDrainTests
{
    [Fact]
    public async Task KnownLimit_SecondGameResultMoreThanOneSecondLater_IsNotSeen()
    {
        // 30 ticks = 1.5 s after the first copy, beyond the 1 s drain the spec asks for.
        await using var server = new QaRulesServer(new Knobs
        {
            ResultPlan = (id, b) => id == 3 ? new[] { (b, 0), (b, 30) } : new[] { (b, 0) }
        });
        var (h6, _) = await U3bRun.H6Async(server, "KNOWN-LIMIT duplicate-result-after-drain");
        Assert.True(h6.Passed, "characterisation changed: " + h6.Evidence);
    }
}

// ---- Timing sensitivity of the capture/retreat (depends on pending U2b rule decision) ---------------------
// The rules fake executes a Lagging survivor the next tick if the killer is still within 1.2 m (U2b contract).
// A fake that applies inputs N ticks late shows how much latency H6's "reverse on the first Lagging frame"
// tolerates. Contract asserted: H6 passes if and only if the fake executed nobody (never a false pass, never a
// spurious failure for another reason). Outcome per delay is recorded in the report.
public abstract class U3bInputDelayBase(int delayTicks)
{
    [Fact]
    public async Task H6PassesExactlyWhenNoExecutionHappened()
    {
        await using var server = new QaRulesServer(new Knobs { InputDelayTicks = delayTicks });
        var (h6, _) = await U3bRun.H6Async(server, $"input-delay-{delayTicks}-ticks (depends on pending U2b rule decision)");
        if (server.Executions > 0)
        {
            Assert.False(h6.Passed, $"fake executed {server.Executions} survivor(s) but H6 passed: {h6.Evidence}");
            Assert.Equal(0, h6.Score);
        }
        else Assert.True(h6.Passed, $"no execution happened but H6 failed: {h6.Evidence}");
    }
}

public sealed class U3bInputDelay1TickTests() : U3bInputDelayBase(1);
public sealed class U3bInputDelay2TicksTests() : U3bInputDelayBase(2);

// ---- CLI / options / scorer -------------------------------------------------------------------------------

public sealed class U3bCliTests
{
    private sealed class CountingListener : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private int connections;
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public int Connections => Volatile.Read(ref connections);

        public CountingListener()
        {
            listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using var client = await listener.AcceptTcpClientAsync();
                        Interlocked.Increment(ref connections);
                    }
                }
                catch (Exception) { }
            });
        }

        public void Dispose() => listener.Stop();
    }

    [Fact]
    public async Task HelpDocumentsH6PortAndTheTwoServerRun()
    {
        var (exit, output, _) = await Qa.RunHarnessProcessAsync(new[] { "--help" }, TimeSpan.FromSeconds(30));
        Assert.Equal(0, exit);
        Assert.Contains("--h6-port", output);
        Assert.Contains("--h6-host", output);
        Assert.Contains("--sandbox", output);
        Assert.Contains("two server instances", output);
        Assert.Contains("H6 together with any of H2-H5 fails at once", output);
        Assert.Contains("--only H6 without --h6-port uses --port", output);
        Assert.DoesNotContain("skipped", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task H6FailFastOpensNoConnectionForH6()
    {
        // Baseline: H5 alone connects n times to a server that accepts and hangs up. Adding H6 (conflict, no
        // --h6-port) must not add a single connection.
        int baseline, withH6;
        using (var counter = new CountingListener())
        {
            await HarnessRunner.RunAsync(new Options { Port = counter.Port, Timeout = TimeSpan.FromSeconds(0.5), Only = new HashSet<string> { "H5" } });
            await Task.Delay(300);
            baseline = counter.Connections;
        }
        ScoreReport report;
        using (var counter = new CountingListener())
        {
            report = await HarnessRunner.RunAsync(new Options { Port = counter.Port, Timeout = TimeSpan.FromSeconds(0.5), Only = new HashSet<string> { "H5", "H6" } });
            await Task.Delay(300);
            withH6 = counter.Connections;
        }
        Assert.True(baseline >= 1, "baseline must connect at least once");
        Assert.Equal(baseline, withH6);
        Assert.Equal(ScenarioCheck.NeedsOwnServer, report.Items.Single(i => i.Id == "H6").Evidence);
    }

    [Fact]
    public async Task H6AloneDoesConnectToPortWhenNoH6PortIsGiven()
    {
        using var counter = new CountingListener();
        var report = await HarnessRunner.RunAsync(new Options { Port = counter.Port, Timeout = TimeSpan.FromSeconds(0.5), Only = new HashSet<string> { "H6" } });
        await Task.Delay(300);
        Assert.True(counter.Connections >= 1);
        Assert.NotEqual(ScenarioCheck.NeedsOwnServer, report.Items.Single().Evidence);
    }

    [Fact]
    public async Task DefaultFullRunWithoutH6PortReportsSixItemsH6FailsFastAndNothingIsSkipped()
    {
        string report = Path.Combine(Path.GetTempPath(), $"u3b-qa-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output, _) = await Qa.RunHarnessProcessAsync(
                new[] { "--port", SilentServer.ClosedPort().ToString(), "--timeout", "0.5", "--duration", "0.5", "--report", report }, TimeSpan.FromSeconds(60));
            Assert.Equal(1, exit);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            var root = doc.RootElement;
            Assert.Equal(new[] { "gatesPassed", "items", "total" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
            var items = root.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(new[] { "H1", "H2", "H3", "H4", "H5", "H6" }, items.Select(i => i.GetProperty("id").GetString()).ToArray());
            Assert.All(items, i => Assert.Equal(new[] { "evidence", "gate", "id", "max", "passed", "score" },
                i.EnumerateObject().Select(p => p.Name).Order().ToArray()));
            var h6 = items[5];
            Assert.Equal(ScenarioCheck.NeedsOwnServer, h6.GetProperty("evidence").GetString());
            Assert.Equal(0, h6.GetProperty("score").GetInt32());
            Assert.DoesNotContain("skipped", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("H6 completed: FAIL", output);
        }
        finally { try { File.Delete(report); } catch { } }
    }

    [Fact]
    public void H6PortOptionRulesBehaveExactlyAsSpecified()
    {
        // --only H6 without --h6-port uses --port (and --host).
        var o = Options.Parse(new[] { "--only", "h6", "--host", "10.0.0.5", "--port", "9000" });
        Assert.Null(o.H6Conflict());
        Assert.Equal(("10.0.0.5", 9000), (o.ForH6().Host, o.ForH6().Port));
        // H6 with each of H2..H5 and no --h6-port: conflict. With H1 only: none.
        foreach (string other in new[] { "H2", "H3", "H4", "H5" })
            Assert.Equal(ScenarioCheck.NeedsOwnServer, Options.Parse(new[] { "--only", $"{other},H6" }).H6Conflict());
        Assert.Null(Options.Parse(new[] { "--only", "H1,H6" }).H6Conflict());
        // equal port on the same host (case-insensitive) conflicts; a different port or host does not.
        Assert.Equal(ScenarioCheck.NeedsOwnServer, Options.Parse(new[] { "--host", "Example", "--port", "7", "--h6-host", "example", "--h6-port", "7" }).H6Conflict());
        Assert.Null(Options.Parse(new[] { "--port", "7", "--h6-port", "8" }).H6Conflict());
        Assert.Null(Options.Parse(new[] { "--port", "7", "--h6-port", "7", "--h6-host", "other" }).H6Conflict());
        // --h6-host alone (no --h6-port) keeps the port and still conflicts when it is the same host name.
        Assert.Equal(ScenarioCheck.NeedsOwnServer, Options.Parse(new[] { "--h6-host", "127.0.0.1" }).H6Conflict());
        // invalid values.
        foreach (var bad in new[] { new[] { "--h6-port" }, new[] { "--h6-port", "-1" }, new[] { "--h6-port", "70000" }, new[] { "--h6-host", "" } })
            Assert.Throws<ArgumentException>(() => Options.Parse(bad));
    }

    [Fact]
    public void ScorerSixPassesIs100AndShapeUnchanged()
    {
        var all = Scorer.Summarize(Scorer.Ids.Select(id => Scorer.Result(id, true, "ok")));
        Assert.Equal(100, all.Total);
        Assert.True(all.GatesPassed);
        Assert.True(all.MeetsThreshold(90));
        Assert.Equal(new[] { 25, 20, 20, 15, 10, 10 }, all.Items.Select(i => i.Max).ToArray());
        Assert.Equal(90, new Options().Threshold);
        var withoutH6 = Scorer.Summarize(Scorer.Ids.Select(id => Scorer.Result(id, id != "H6", id)));
        Assert.Equal(90, withoutH6.Total);
        Assert.True(withoutH6.MeetsThreshold(90));
        Assert.Equal(10, Scorer.Result("H6", true, "x").Score);
        Assert.False(Scorer.Spec("H6").Gate);
    }
}

// ---- Known limit: the scenario does not assert rule magnitudes -------------------------------------------
public sealed class U3bKnownLimitFastRulesTests
{
    [Fact]
    public async Task KnownLimit_Terminals20xFasterAndRescueIn0_1s_StillPass()
    {
        await using var server = new QaRulesServer(new Knobs { HackRateFactor = 20, RescueSeconds = 0.1f });
        var (h6, _) = await U3bRun.H6Async(server, "KNOWN-LIMIT fast-hack-fast-rescue");
        Assert.True(h6.Passed, "characterisation changed: " + h6.Evidence);
    }
}

// ---- H6 repeatability under concurrent load -------------------------------------------------------------
// N correct fakes, each with its own full H6 run, all at the same time (CPU/scheduler pressure). Every run must
// pass. N defaults to 6; set U3B_STRESS_N for a bigger sweep.
public sealed class U3bConcurrentControlRunsTests
{
    [Fact]
    public async Task ManyConcurrentCorrectRunsAllPass()
    {
        int n = int.TryParse(Environment.GetEnvironmentVariable("U3B_STRESS_N"), out int parsed) ? parsed : 6;
        var runs = Enumerable.Range(0, n).Select(async i =>
        {
            await using var server = new QaRulesServer(new Knobs { ExecutionGraceSeconds = i % 2 == 0 ? 0 : 2 }); // both server versions
            var (h6, elapsed) = await U3bRun.H6Async(server, $"concurrent-control-{i}/{n}");
            return (h6, server.Executions, elapsed);
        }).ToArray();
        var results = await Task.WhenAll(runs);
        var failures = results.Where(r => !r.h6.Passed).Select(r => r.h6.Evidence[..Math.Min(260, r.h6.Evidence.Length)]).ToArray();
        Assert.True(failures.Length == 0, $"{failures.Length}/{n} correct-fake runs failed:\n{string.Join("\n", failures)}");
    }
}
