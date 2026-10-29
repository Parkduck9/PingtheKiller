using System.Diagnostics;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3c;

internal static class U3cRun
{
    public static async Task<(CheckResult Item, QaViewServer Server)> H3Async(ViewKnobs knobs, string label, double timeoutSeconds = 2)
    {
        var fake = new QaViewServer(knobs);
        try
        {
            using var hard = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var report = await HarnessRunner.RunAsync(new Options { Port = fake.Port, Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Only = new HashSet<string> { "H3" } }, hard.Token);
            var item = Assert.Single(report.Items);
            Dump(label, item);
            return (item, fake);
        }
        finally { await fake.DisposeAsync(); }
    }

    public static void Dump(string label, CheckResult item)
    {
        string? file = Environment.GetEnvironmentVariable("QA_U3C_EVIDENCE_FILE");
        if (file == null) return;
        lock (typeof(U3cRun))
            File.AppendAllText(file, $"{label}\t{item.Id}\t{(item.Passed ? "PASS" : "FAIL")}\t{item.Evidence.ReplaceLineEndings(" ")}\n");
    }

    // Runs the cases concurrently (each on its own fake and port); returns the failures of the expectation, if any.
    public static async Task<List<string>> RunCasesAsync(IEnumerable<Case> cases, string mode, int concurrency = 4)
    {
        var problems = new List<string>();
        using var gate = new SemaphoreSlim(concurrency);
        await Task.WhenAll(cases.Select(async c =>
        {
            await gate.WaitAsync();
            try
            {
                var (item, server) = await H3Async(c.Knobs, $"{mode}/{c.Name}");
                if (item.Passed != c.ExpectPass)
                    problems.Add($"[{mode}/{c.Name}] expected {(c.ExpectPass ? "PASS" : "FAIL")} but got {(item.Passed ? "PASS" : "FAIL")}: {item.Evidence}");
                else
                    foreach (string text in c.Evidence)
                        if (!item.Evidence.Contains(text)) problems.Add($"[{mode}/{c.Name}] evidence lacks '{text}': {item.Evidence}");
                if (!item.Passed && item.Score != 0) problems.Add($"[{mode}/{c.Name}] failed H3 with nonzero score {item.Score}");
                if (c.Premise != null && c.Premise(server) is string broken) problems.Add($"[{mode}/{c.Name}] fake premise broken: {broken}");
            }
            catch (Exception ex) { problems.Add($"[{mode}/{c.Name}] threw {ex.GetType().Name}: {ex.Message}"); }
            finally { gate.Release(); }
        }));
        return problems;
    }
}

internal sealed record Case(string Name, ViewKnobs Knobs, bool ExpectPass, string[]? EvidenceOrNull = null, Func<QaViewServer, string?>? Premise = null)
{
    public string[] Evidence => EvidenceOrNull ?? Array.Empty<string>();
}

// ---- False fails: H3 is a gate, so each of these correct servers must PASS (both modes) -------------------------------

public abstract class U3cH3FalseFailTests(bool anonymous)
{
    private string Mode => anonymous ? "anon" : "id";

    private ViewKnobs K(BotScript bots = BotScript.Walkers, float distance = 0.3f) => new() { Anonymous = anonymous, Bots = bots, BotDistance = distance };

    private static readonly string Clean = "0 unexplained, 0 to stopped survivor 2";

    private static Func<QaViewServer, string?> Closest(float below) =>
        s => s.MinBotDistanceWhileStill < below ? null : $"closest bot pass {s.MinBotDistanceWhileStill} m, wanted < {below}";

    [Fact]
    public async Task BotsWalkingPastAndStoppingNextToTheStoppedSurvivorNeverFailH3()
    {
        var cases = new List<Case>
        {
            new("walkers-only", K(), true, new[] { Clean }),
            new("pass-by-0.1", K(BotScript.PassBy, 0.1f), true, new[] { Clean }, Closest(0.12f)),
            new("pass-by-0.3", K(BotScript.PassBy, 0.3f), true, new[] { Clean }, Closest(0.32f)),
            new("pass-by-0.5", K(BotScript.PassBy, 0.5f), true, new[] { Clean }, Closest(0.52f)),
            new("stop-next-to-0.04", K(BotScript.StopNear, 0.04f), true, new[] { Clean }),
            new("stop-next-to-0.1", K(BotScript.StopNear, 0.1f), true, new[] { Clean }),
            new("stop-next-to-0.3", K(BotScript.StopNear, 0.3f), true, new[] { Clean }),
            new("stop-next-to-0.5", K(BotScript.StopNear, 0.5f), true, new[] { Clean }),
            new("cluster-ring-0.1", K(BotScript.Cluster, 0.1f), true, new[] { Clean }),
            new("cluster-ring-0.3", K(BotScript.Cluster, 0.3f), true, new[] { Clean }),
            new("cluster-walking-0.1", K(BotScript.ClusterWalking, 0.1f), true, new[] { Clean }),
            new("cluster-walking-0.3", K(BotScript.ClusterWalking, 0.3f), true, new[] { Clean }),
        };
        var problems = await U3cRun.RunCasesAsync(cases, Mode);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task SkewedTimingDroppedTicksAndPerSnapshotShuffleNeverFailH3()
    {
        var cases = new List<Case>
        {
            new("killer-stream-200ms-late", K(BotScript.PassBy, 0.3f) with { KillerDelayMs = 200 }, true, new[] { Clean }),
            new("survivor-stream-200ms-late", K(BotScript.PassBy, 0.3f) with { SurvivorDelayMs = 200 }, true, new[] { Clean }),
            new("jitter-120ms-both", K(BotScript.PassBy, 0.3f) with { KillerJitterMs = 120, SurvivorJitterMs = 120 }, true, new[] { Clean }),
            new("jitter-killer-only", K(BotScript.ClusterWalking, 0.3f) with { KillerJitterMs = 150 }, true, new[] { Clean }),
            new("survivor-ticks-dropped-1-in-8", K(BotScript.PassBy, 0.3f) with { DropMod = 8, DropCount = 1 }, true, new[] { Clean }),
            new("everything-at-once", K(BotScript.PassBy, 0.1f) with { KillerJitterMs = 60, SurvivorJitterMs = 60, DropMod = 8, DropCount = 1, KillerDelayMs = 80 }, true, new[] { Clean }),
            new("shuffle-every-tick", K(BotScript.PassBy, 0.3f) with { ShuffleInIdMode = true }, true, new[] { Clean }),
        };
        if (!anonymous)
        {
            cases.Add(new("random-nonzero-fake-ids-pass-by", K(BotScript.PassBy, 0.3f) with { RandomFakeIds = true }, true, new[] { Clean }));
            cases.Add(new("random-nonzero-fake-ids-cluster", K(BotScript.Cluster, 0.1f) with { RandomFakeIds = true }, true, new[] { Clean }));
        }
        var problems = await U3cRun.RunCasesAsync(cases, Mode);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}

public sealed class U3cH3FalseFailIdModeTests() : U3cH3FalseFailTests(false);
public sealed class U3cH3FalseFailAnonymousTests() : U3cH3FalseFailTests(true);

// ---- False passes: each broken fake must FAIL H3 with score 0 (both modes) --------------------------------------------

public abstract class U3cH3FalsePassTests(bool anonymous)
{
    private string Mode => anonymous ? "anon" : "id";

    private ViewKnobs K(StoppedLeak leak, BotScript bots = BotScript.Walkers) => new() { Anonymous = anonymous, Bots = bots, Leak = leak };

    private static readonly string[] Leaked = { "Silent survivor", "leak" };

    [Fact]
    public async Task StoppedSurvivorLeakedInAnyShapeFailsH3()
    {
        var cases = new List<Case>
        {
            new("exact-anon-position", K(StoppedLeak.Exact), false, Leaked, s => s.LeakEntriesSent > 50 ? null : $"only {s.LeakEntriesSent} leak entries"),
            new("exact-with-bots-passing-by", K(StoppedLeak.Exact, BotScript.PassBy), false, Leaked),
            new("rounded-0.01", K(StoppedLeak.Rounded), false, Leaked),
            new("offset-0.036", K(StoppedLeak.Offset), false, Leaked),
            new("offset-0.0489-just-inside-eps", K(StoppedLeak.Offset) with { OffsetX = 0.0346f, OffsetZ = 0.0346f }, false, Leaked),
            new("every-2nd-tick", K(StoppedLeak.EveryNth) with { EveryN = 2 }, false, Leaked),
            new("every-5th-tick", K(StoppedLeak.EveryNth) with { EveryN = 5 }, false, Leaked),
            new("every-20th-tick", K(StoppedLeak.EveryNth) with { EveryN = 20 }, false, Leaked),
            new("offset-0.036-every-3rd-tick", K(StoppedLeak.OffsetEveryNth) with { EveryN = 3 }, false, Leaked),
            new("offset-0.036-every-20th-tick", K(StoppedLeak.OffsetEveryNth) with { EveryN = 20 }, false, Leaked),
            new("under-a-bots-real-id", K(StoppedLeak.UnderBotId), false, Leaked),
            // a nearby bot (0.03 m) must not hide an exact leak: nearest true position is still the stopped survivor (0.0 m)
            new("exact-while-bot-stands-0.03m-away", K(StoppedLeak.Exact, BotScript.StopNear) with { BotDistance = 0.03f }, false, Leaked),
            new("exact-while-bot-stands-0.04m-away", K(StoppedLeak.Exact, BotScript.StopNear) with { BotDistance = 0.04f }, false, Leaked),
            new("exact-with-cluster-ring-0.1", K(StoppedLeak.Exact, BotScript.Cluster) with { BotDistance = 0.1f }, false, Leaked),
            new("offset-while-clustered-bots-0.3", K(StoppedLeak.Offset, BotScript.Cluster) with { BotDistance = 0.3f }, false, Leaked),
        };
        var problems = await U3cRun.RunCasesAsync(cases, Mode);
        Assert.True(problems.Count == 0, "FALSE PASS (blocker) or wrong evidence:\n" + string.Join("\n", problems));
    }

    [Fact]
    public async Task UnexplainedAndStaleEntriesFailH3()
    {
        var unexplained = new[] { "unexplained killer-view entry" };
        var cases = new List<Case>
        {
            new("extra-unexplained-entry", K(StoppedLeak.None) with { ExtraEntry = true }, false, unexplained),
            new("offset-0.0511-just-outside-eps", K(StoppedLeak.Offset) with { OffsetX = 0.0361f, OffsetZ = 0.0361f }, false, unexplained),
            new("offset-0.5", K(StoppedLeak.Offset) with { OffsetX = 0.5f, OffsetZ = 0.3f }, false, unexplained),
            new("offset-3m", K(StoppedLeak.Offset) with { OffsetX = 3f, OffsetZ = 2f }, false, unexplained),
            new("bots-stale-by-one-tick-in-killer-view", K(StoppedLeak.None) with { BotsStaleInKillerView = true }, false, unexplained), // the "stale or early snapshot" neighbour hint is best-effort (the neighbour tick may predate the capture window)
            new("bots-0.06m-off-in-killer-view", K(StoppedLeak.None) with { BotOffsetInKillerView = 0.06f }, false, unexplained),
            new("survivor-never-appears-while-moving", K(StoppedLeak.None) with { NeverShowMoving = true }, false, new[] { "Moving baseline missing" }),
        };
        var problems = await U3cRun.RunCasesAsync(cases, Mode);
        Assert.True(problems.Count == 0, "FALSE PASS (blocker) or wrong evidence:\n" + string.Join("\n", problems));
    }
}

public sealed class U3cH3FalsePassIdModeTests() : U3cH3FalsePassTests(false);
public sealed class U3cH3FalsePassAnonymousTests() : U3cH3FalsePassTests(true);

// ---- Coverage and attribution limits ----------------------------------------------------------------------------------

public sealed class U3cH3CoverageAndLimitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TooFewJoinedTicksFailsH3WithCoverageEvidence(bool anonymous)
    {
        // 60% of survivor-stream ticks never arrive: fewer than 50 ticks can be joined. Must fail (never pass on thin evidence).
        var (item, _) = await U3cRun.H3Async(new ViewKnobs { Anonymous = anonymous, DropMod = 10, DropCount = 6 }, $"{(anonymous ? "anon" : "id")}/coverage-60pct-dropped");
        Assert.False(item.Passed, item.Evidence);
        Assert.Contains("Insufficient masking coverage", item.Evidence);
    }

    // LIMIT (documented, minor): a leak emitted at an offset <= epsilon while a bot stands closer to that leaked position
    // than the stopped survivor's true position is attributed to the bot, so H3 passes. An EXACT leak is always caught
    // (see "exact-while-bot-stands-0.03m-away"). Asserts today's behaviour; flip it when the limit is closed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gap_OffsetLeakNearerToABotThanToTheStoppedSurvivorIsAttributedToTheBot(bool anonymous)
    {
        var (item, server) = await U3cRun.H3Async(new ViewKnobs { Anonymous = anonymous, Bots = BotScript.StopNear, Leak = StoppedLeak.NearerToBot },
            $"{(anonymous ? "anon" : "id")}/GAP-offset-leak-nearer-to-bot");
        Assert.True(server.LeakEntriesSent > 50);
        Assert.True(item.Passed, "The documented attribution limit has been closed (or behaviour changed): update docs/qa/U3c-qa-a1.md and flip this test. " + item.Evidence);
    }

    // LIMIT (documented, minor): the killer's own entry is recognised by real Id + Role=Killer and skipped. A SECOND entry
    // with that Id/Role at an offset stopped-survivor position is not attributed, not counted unexplained, and has no exact
    // X/Z bytes, so H3 passes.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gap_OffsetLeakDisguisedAsADuplicateOfTheKillersOwnEntryIsNotDetected(bool anonymous)
    {
        var (item, server) = await U3cRun.H3Async(new ViewKnobs { Anonymous = anonymous, Leak = StoppedLeak.KillerOwnEntryDuplicate },
            $"{(anonymous ? "anon" : "id")}/GAP-leak-as-duplicate-own-entry");
        Assert.True(server.LeakEntriesSent > 50);
        Assert.True(item.Passed, "The documented own-entry limit has been closed (or behaviour changed): update docs/qa/U3c-qa-a1.md and flip this test. " + item.Evidence);
    }

    // LIMIT (informational, pre-existing windows, not a U3c regression): H3 settles 0.15 s grace + 0.25 s margin on RECEIVE time.
    // A survivor stream more than ~250 ms behind the server fails "Survivor remained Signal"; a killer stream that late still
    // shows the survivor's Signal-hold entries inside the capture window, which the raw byte scan reports as a leak.
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Limit_StreamsMoreThanTheSignalMarginLateFailH3(bool anonymous, bool killerLate)
    {
        var knobs = new ViewKnobs { Anonymous = anonymous, KillerDelayMs = killerLate ? 500 : 0, SurvivorDelayMs = killerLate ? 0 : 500 };
        var (item, _) = await U3cRun.H3Async(knobs, $"{(anonymous ? "anon" : "id")}/LIMIT-{(killerLate ? "killer" : "survivor")}-stream-500ms-late");
        Assert.False(item.Passed, "Behaviour changed: a 500 ms late stream no longer fails H3. " + item.Evidence);
    }

    // SPEC TENSION (minor): a CORRECT server whose bot shares only the stopped survivor's exact Z float fails H3, because the
    // unchanged raw scan searches X and Z independently over whole frames. Needs bit-equal floats (negligible with integrated
    // positions); the spec's "ambiguity is evidence, not a failure" does not cover it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tension_CorrectServerWithABotSharingOnlyTheExactZFloatFailsH3(bool anonymous)
    {
        var (item, _) = await U3cRun.H3Async(new ViewKnobs { Anonymous = anonymous, Bots = BotScript.StopNearSameZ },
            $"{(anonymous ? "anon" : "id")}/TENSION-bot-shares-exact-Z");
        Assert.False(item.Passed, "Behaviour changed: a bot sharing only Z no longer fails H3. " + item.Evidence);
        Assert.Contains("Z=", item.Evidence);
    }

    // SPEC TENSION (minor): the spec says an exact tie (a bot bit-identical to the stopped survivor) is evidence, not a failure.
    // The unchanged raw X/Z byte scan runs first over every killer frame and fails on the bot's own coordinate bytes.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tension_BotBitIdenticalToTheStoppedSurvivorFailsOnTheRawByteScanNotAsAmbiguous(bool anonymous)
    {
        var (item, _) = await U3cRun.H3Async(new ViewKnobs { Anonymous = anonymous, Bots = BotScript.StandExact },
            $"{(anonymous ? "anon" : "id")}/TENSION-bot-bit-identical");
        Assert.False(item.Passed, "Behaviour changed: an exact-tie bot no longer fails H3. " + item.Evidence);
        Assert.Contains("bytes leaked in S2C_WorldState", item.Evidence);
        Assert.DoesNotContain("ambiguous (", item.Evidence);
    }
}

// ---- Stress: repeated runs must be deterministic in outcome (no flaky false fail on a correct server, no flaky false pass) ----

public sealed class U3cH3StressTests
{
    public const int RunsPerMode = 16;

    private static async Task<(int Passed, int Failed, List<string> Evidence)> RepeatAsync(Func<bool, ViewKnobs> knobs, string name)
    {
        int passed = 0, failed = 0;
        var evidence = new List<string>();
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(Enumerable.Range(0, RunsPerMode * 2).Select(async i =>
        {
            bool anonymous = i % 2 == 1;
            await gate.WaitAsync();
            try
            {
                var (item, _) = await U3cRun.H3Async(knobs(anonymous), $"{(anonymous ? "anon" : "id")}/{name}#{i / 2}");
                lock (evidence)
                {
                    if (item.Passed) passed++; else { failed++; evidence.Add(item.Evidence); }
                }
            }
            finally { gate.Release(); }
        }));
        return (passed, failed, evidence);
    }

    [Fact]
    public async Task CorrectServerWithBotsJitterAndDroppedTicksPassesEveryTimeInBothModes()
    {
        var (passed, failed, evidence) = await RepeatAsync(a => new ViewKnobs { Anonymous = a, Bots = BotScript.PassBy, BotDistance = 0.2f,
            KillerJitterMs = 40, SurvivorJitterMs = 40, DropMod = 10, DropCount = 1, ShuffleInIdMode = true }, "stress-clean");
        U3cRun.Dump("stress/clean", new CheckResult("stress", passed, passed + failed, false, failed == 0, $"{passed} passed, {failed} failed of {passed + failed}"));
        Assert.True(failed == 0, $"{failed} of {passed + failed} correct-server runs FAILED H3 (false fail):\n{string.Join("\n", evidence.Take(5))}");
    }

    [Fact]
    public async Task OffsetLeakEveryThirdTickIsCaughtEveryTimeInBothModes()
    {
        var (passed, failed, _) = await RepeatAsync(a => new ViewKnobs { Anonymous = a, Bots = BotScript.PassBy, BotDistance = 0.2f,
            Leak = StoppedLeak.OffsetEveryNth, EveryN = 3, KillerJitterMs = 40, SurvivorJitterMs = 40 }, "stress-leak");
        U3cRun.Dump("stress/leak", new CheckResult("stress", failed, passed + failed, false, passed == 0, $"{failed} caught, {passed} missed of {passed + failed}"));
        Assert.True(passed == 0, $"{passed} of {passed + failed} leaking runs PASSED H3 (false pass, blocker)");
    }

    // LIMIT (minor, contrived): killer ticks the survivor stream never delivered are skipped, so an OFFSET leak that is emitted
    // only on exactly those ticks is invisible to attribution (an exact leak is still caught by the raw byte scan).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gap_OffsetLeakOnlyOnTicksTheSurvivorStreamNeverDeliveredIsNotAttributed(bool anonymous)
    {
        var (item, server) = await U3cRun.H3Async(new ViewKnobs { Anonymous = anonymous, Leak = StoppedLeak.OffsetEveryNth, EveryN = 8, DropMod = 8, DropCount = 1 },
            $"{(anonymous ? "anon" : "id")}/GAP-leak-only-on-skipped-ticks");
        Assert.True(server.LeakEntriesSent > 5);
        Assert.True(item.Passed, "The documented skipped-tick limit has been closed (or behaviour changed): update docs/qa/U3c-qa-a1.md and flip this test. " + item.Evidence);
    }
}
