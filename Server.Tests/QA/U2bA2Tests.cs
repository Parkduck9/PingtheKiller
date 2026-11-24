using System.Security.Cryptography;
using System.Text;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA;

// U2b QA attempt 2: adversarial re-verification of the attempt-3 fixes.
//   F0  execution grace, retargeted (GDD v2 §4, 2026-11-03: no execution; touches never advance a Lagging survivor)
//   F4  wire seed no longer drives gameplay randomness (trace invariance, stream scan, reseed per match)
//   F2  Seq filter and rate window reset at match start (two and three matches back to back, reconnect)
// Every test is deterministic: hooks fix the seeds that a test depends on. Where production (CSPRNG) seeding must be
// shown to DIFFER, the compared traces carry far more than 50 bits of entropy, so a false collision is ~2^-50.
public sealed class U2bA2Tests
{
    private readonly ITestOutputHelper _out;
    public U2bA2Tests(ITestOutputHelper output) { _out = output; }
    private static bool Is(PlayerState p, PlayerStatus s) => p.Status == (byte)s;

    // ------------------------------------------------------------------ F0: execution grace
    // GDD v2 §4 (user decision 2026-11-03): there is no execution any more. The F0 scenarios are kept and now prove that
    // no touch pattern (inside or after the old 2 s grace, continuous, re-entry, per survivor, across matches) changes a
    // Lagging survivor's stage or status.

    private static QaRig Captured(int survivors = 2, float dt = 0.05f)
    {
        var r = new QaRig(survivors) { Dt = dt };
        r.Place(r.S[0], 13, 10);
        r.SetCaptureCount(r.S[0], 1); // the second capture: Lagging 30 s, count 2
        r.Place(r.K, 11.9f, 10);
        r.Tick(); // tick 0: the capture
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        return r;
    }
    private static void Away(QaRig r, int ticks) { for (int i = 0; i < ticks; i++) { r.Place(r.K, 5, 5); r.Tick(); } }
    private static void Touch(QaRig r) { r.Place(r.K, 12.0f, 10); r.Tick(); }
    private static void Unchanged(QaRig r, int survivor, string because)
    {
        var t = r.Truth(r.S[survivor]);
        Assert.True(Is(t, PlayerStatus.Lagging), because);
        Assert.Equal(2, t.CaptureCount);
    }

    [Theory]
    [InlineData(0.05f, 2, false)]
    [InlineData(0.05f, 10, false)]
    [InlineData(0.05f, 38, false)]
    [InlineData(0.05f, 39, false)] // 1.95 s after the capture
    [InlineData(0.05f, 40, true)]  // exactly 2.00 s: the old grace was over
    [InlineData(0.05f, 41, true)]
    [InlineData(0.05f, 60, true)]
    [InlineData(0.1f, 19, false)]
    [InlineData(0.1f, 20, true)]
    [InlineData(0.025f, 79, false)]
    [InlineData(0.025f, 80, true)]
    public void Grace_LeaveAfterCaptureThenTouchAgain_NeverExecutes(float dt, int touchTick, bool oldRuleExecuted)
    {
        using var r = Captured(dt: dt);
        Away(r, touchTick - 1);
        Touch(r);
        Unchanged(r, 0, $"touch at tick {touchTick} (old rule executed: {oldRuleExecuted})");
        r.Tick((int)(10 / dt));
        Unchanged(r, 0, "10 s of contact");
        Away(r, 1);
        Touch(r);
        Unchanged(r, 0, "a re-entry after leaving");
    }

    [Fact]
    public void Grace_TouchesAndReEntriesInsideAndAfterTheOldGrace_NeverExecute()
    {
        using var r = Captured();
        Away(r, 4);
        Touch(r);
        Away(r, 1);
        Touch(r);
        Unchanged(r, 0, "inside the old grace");
        Away(r, 33);
        Touch(r);     // 2.05 s
        Unchanged(r, 0, "after the old grace");
    }

    [Fact]
    public void Grace_ContinuousContactAndThenLeaveAndTouch_NeverExecutes()
    {
        using var r = Captured();
        for (int i = 0; i < 200; i++)
        {
            r.Place(r.K, 11.9f + (i % 5) * 0.1f, 10); // jitter inside 1.2 m
            r.Tick();
            Unchanged(r, 0, $"continuing contact at tick {i + 1}");
        }
        Away(r, 1);
        Touch(r);
        Unchanged(r, 0, "leave and touch");
    }

    [Fact]
    public void Grace_TwoLaggingSurvivorsTouchedTogether_NeitherIsExecuted_OnlyTheirOwnTimersEndThem()
    {
        using var r = new QaRig(3);
        r.SetCaptureCount(r.S[0], 1);
        r.SetCaptureCount(r.S[1], 1);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick();                                  // tick 0: S0 captured
        Away(r, 19);
        r.Place(r.K, 11.9f, 10);
        r.Place(r.S[1], 11.9f, 11.0f);
        r.Tick();                                  // tick 20: S1 captured
        Away(r, 21);
        r.Place(r.K, 12.4f, 10.5f);                // 0.78 m from S0, 0.71 m from S1
        r.Tick();
        Unchanged(r, 0, "S0 touched at 2.1 s");
        Unchanged(r, 1, "S1 touched at 1.1 s");
        // Each 30 s timer runs out on its own: S0 first, S1 20 ticks later.
        double s0Left = r.Truth(r.S[0]).StatusTimer, s1Left = r.Truth(r.S[1]).StatusTimer;
        Assert.InRange(s1Left - s0Left, 0.99, 1.01);
        Assert.True(Is(r.Truth(r.S[2]), PlayerStatus.Normal));
    }

    [Fact]
    public void Grace_ThreeMatchesBackToBack_TouchesNeverExecute_AndCaptureStateResetsAcrossMatches()
    {
        using var r = new QaRig(2);
        for (int match = 1; match <= 3; match++)
        {
            if (match > 1) r.NextMatch();
            Assert.Equal(0, r.Truth(r.S[0]).CaptureCount);
            r.Place(r.S[0], 13, 10);
            r.Place(r.K, 11.9f, 10);
            r.Tick();                                        // capture
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), $"match {match}: capture");
            Assert.Equal(1, r.Truth(r.S[0]).CaptureCount);
            Away(r, 3);
            Touch(r);
            Away(r, 40);
            Touch(r);
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), $"match {match}: touches never execute");
            Assert.Equal(1, r.Truth(r.S[0]).CaptureCount);
        }
    }

    // ------------------------------------------------------------------ F2: sequence and rate window across matches

    [Fact]
    public void Matches_ThreeBackToBack_BothRolesRestartSeqAtOneEachTime_AndNothingIsHeldOverTheBoundary()
    {
        using var r = new QaRig(1);
        for (int match = 1; match <= 3; match++)
        {
            if (match > 1)
            {
                r.S[0].Seq = 100000; // a sequence far above whatever the next match uses
                r.S[0].Move(1, 0);
                r.K.Seq = 100000;
                r.K.Move(0, 1);
                r.Tick(); // both are moving in the very last tick of the previous match
                r.NextMatch();
                // Nothing may be held over: once the new match runs, both stand still for 3 ticks.
                r.Tick();
                var s0 = r.Truth(r.S[0]);
                float k0 = r.TruthKillerX();
                r.Tick(3);
                Assert.Equal(s0.X, r.Truth(r.S[0]).X);
                Assert.Equal(k0, r.TruthKillerX());
                Assert.Equal(5f, k0);
            }
            r.S[0].Seq = 0;
            r.K.Seq = 0;
            r.Tick();
            float sx = r.Truth(r.S[0]).X, kz = r.KW.Players.Single(p => p.Id == r.K.Id).Z;
            for (int i = 0; i < 10; i++) { r.S[0].Move(-1, 0); r.K.Move(0, 1); r.Tick(); }
            Assert.InRange(sx - r.Truth(r.S[0]).X, 1.7f, 2.01f);
            Assert.True(r.KW.Players.Single(p => p.Id == r.K.Id).Z - kz > 1.7f, $"match {match}: killer ignored Seq 1..10");
        }
    }

    [Fact]
    public void Rate_WindowIsClearedAtMatchStart_ButStillLimitsSixtyInputsPerSecondInsideAMatch()
    {
        using var r = new QaRig(1);
        // Inside a match the limit still exists: 60 accepted, the next 40 are dropped.
        r.Place(r.S[0], 48, 30);
        r.Tick();
        float x0 = r.Truth(r.S[0]).X;
        for (int i = 0; i < 60; i++) r.S[0].Move(1, 0);
        for (int i = 0; i < 40; i++) r.S[0].Move(-1, 0);
        r.Tick();
        Assert.True(r.Truth(r.S[0]).X > x0, "the first 60 inputs (+x) must win; the 40 after the cap are dropped");
        // Saturate the window right before the match ends; the next match starts well inside the same second.
        for (int i = 0; i < 80; i++) r.S[0].Move(0, 0);
        r.Tick();
        r.NextMatch();
        r.S[0].Seq = 0;
        r.Tick();
        float x = r.Truth(r.S[0]).X;
        r.S[0].Move(1, 0);
        r.Tick();
        Assert.True(r.Truth(r.S[0]).X > x, "the first input of the new match was swallowed by the previous match's rate window");
    }

    [Fact]
    public void Reconnect_WithTheTokenMidMatch_AcceptsASequenceRestartingAtOne()
    {
        using var r = new QaRig(1);
        ulong token = r.S[0].Last<S2C_JoinResult>().ReconnectToken;
        int id = r.S[0].Id;
        r.Place(r.S[0], 48, 30);
        r.S[0].Seq = 900;
        r.S[0].Move(0, 0);
        r.Tick();
        r.S[0].Connection.Close();
        r.Tick(5);
        var back = r.F.Connect();
        back.Send(new C2S_Join { Nickname = "S0", ReconnectToken = token });
        r.Tick();
        Assert.Equal(id, back.Last<S2C_JoinResult>().PlayerId);
        float x = back.World().Players.Single(p => p.Id == id).X;
        back.Move(1, 0); // Seq 1
        r.Tick();
        Assert.True(back.World().Players.Single(p => p.Id == id).X > x, "a reconnected client restarting Seq at 1 is ignored");
    }

    // ------------------------------------------------------------------ F4: no path from MatchStart.Seed to server randomness

    // Everything a human can observe for three consecutive matches with 7 bots (WorldState, SkillEffect, results, lobby),
    // except the packets that legitimately differ (MatchStart carries the wire seed, JoinResult a random token), plus the
    // server's ground truth: every player's role and skill at match start and every position every 25 ticks.
    private static (string Hash, string Truth) Trace(int? privateSeed, int? wireSeed, RoleChoice role)
    {
        using var f = new QaFixture(bots: 7, seed: privateSeed ?? 0, matchSeed: wireSeed, csprng: privateSeed == null);
        var h = f.Join("H");
        h.Send(new C2S_SetRole { RoleChoice = (byte)role });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var truth = new StringBuilder();
        void Absorb()
        {
            foreach (var pk in QaWire.Take(h))
                if (pk is not S2C_MatchStart && pk is not S2C_JoinResult) hash.AppendData(PacketCodec.Encode(pk));
        }
        for (int match = 0; match < 3; match++)
        {
            h.Seq = 0;
            h.Send(new C2S_StartMatch());
            f.Tick();
            foreach (var p in Spy.Players(f.Server))
                truth.Append(Spy.Get<int>(p, "Id")).Append(':').Append(Spy.Get(p, "Role")).Append('/').Append(Spy.Get<byte>(p, "Skill")).Append(' ');
            bool ended = false;
            for (int t = 0; t < 400 && !ended; t++)
            {
                h.Move(1, 0.3f);
                f.Tick();
                var packets = QaWire.Take(h);
                foreach (var pk in packets)
                {
                    if (pk is S2C_MatchStart || pk is S2C_JoinResult) continue;
                    hash.AppendData(PacketCodec.Encode(pk));
                    if (pk is S2C_GameResult) ended = true;
                }
                if (t % 25 == 0)
                    foreach (var p in Spy.Players(f.Server))
                        truth.Append(BitConverter.SingleToInt32Bits(Spy.Get<float>(p, "X"))).Append(',').Append(BitConverter.SingleToInt32Bits(Spy.Get<float>(p, "Z"))).Append(' ');
            }
            if (!ended) { Spy.SetServer(f.Server, "_matchTime", 899.9); f.Tick(3); Absorb(); }
            truth.Append('|');
        }
        return (Convert.ToHexString(hash.GetHashAndReset()), truth.ToString());
    }

    public static IEnumerable<object[]> Roles() { yield return new object[] { RoleChoice.Killer }; yield return new object[] { RoleChoice.Survivor }; yield return new object[] { RoleChoice.Random }; }

    [Theory]
    [MemberData(nameof(Roles))]
    public void Seed_WireSeedHasNoInfluenceOnAnyGameplayRandomness_FullTraceIdenticalForEveryWireSeed(RoleChoice role)
    {
        // Deterministic: the private seed is fixed by the hook, only the wire seed varies. Killer assignment, every skill
        // draw, bot patrol/detour/skill decisions, White Noise points and decoy Id picks over three matches must be
        // bit-identical, so no gameplay random draw can be a function of MatchStart.Seed.
        var baseline = Trace(77, 1, role);
        Assert.Equal(baseline, Trace(77, 1, role)); // determinism of the harness itself
        foreach (int wire in new[] { 0, -1, 2, 77, 424242, int.MaxValue, int.MinValue, 77 * 7919 + 17 })
        {
            var t = Trace(77, wire, role);
            Assert.Equal(baseline.Truth, t.Truth);
            Assert.Equal(baseline.Hash, t.Hash);
        }
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void Seed_PrivateSeedChangesTheWholeTrace_SoTheInvarianceAboveIsNotVacuous(RoleChoice role)
    {
        var traces = new[] { 77, 78, 79, 1000, -5, 0 }.Select(s => Trace(s, 1, role)).ToList();
        Assert.Equal(traces.Count, traces.Select(t => t.Hash + t.Truth).Distinct().Count());
        Assert.Equal(traces.Count, traces.Select(t => t.Hash).Distinct().Count());
    }

    [Fact]
    public void Seed_ProductionSeeding_ServersWithTheSameWireSeedPlayDifferentMatches_LongTraceDesign()
    {
        // Deterministic replacement for the implementer's EqualWireSeedsDoNotGiveEqualBotPaths (see report, finding F6):
        // compare THREE matches x 8 players of roles+skills and positions, ~60 bits of entropy, instead of 10 s of survivor positions.
        foreach (var role in new[] { RoleChoice.Random, RoleChoice.Survivor, RoleChoice.Killer })
        {
            var traces = Enumerable.Range(0, 4).Select(_ => Trace(null, 5, role)).ToList();
            Assert.Equal(4, traces.Select(t => t.Hash + t.Truth).Distinct().Count());
        }
    }

    [Fact]
    public void Seed_StreamScan_NoServerRandomStreamIsAnyOffsetOfAStreamSeededFromTheWireSeed_AcrossThreeMatches()
    {
        // U2c migration: the server streams are now xoshiro256** (PrivateRandom), so the scan generates candidate streams with
        // the server's own generator instead of System.Random. Reads the server's three private streams through reflection
        // (ground truth), draws 8 values from each and checks whether they occur anywhere in the first 20 000 outputs of a
        // stream seeded with the wire seed or any simple function of it. A hit would prove a derivation path. A positive
        // control with the legacy coupling must hit.
        using (var control = new QaRig(2, seed: 4711, matchSeed: 4711))
        {
            string[] names = { "_random", "_decoyRandom", "_shuffleRandom" };
            for (int stream = 0; stream < 3; stream++)
            {
                var obs = SeedAttack.Draw(Spy.Server<object>(control.Server, names[stream]), 8);
                Assert.True(SeedAttack.Contains(4711, stream, obs, 20000), "detector self-check: hook seed == wire seed must be found in " + names[stream]);
            }
        }
        foreach (int fixedWire in new[] { 424242, 0, int.MaxValue, int.MinValue, -1 })
        {
            using var r = new QaRig(2, matchSeed: fixedWire, csprng: true);
            for (int match = 1; match <= 3; match++)
            {
                if (match > 1) r.NextMatch();
                Assert.Equal(fixedWire, r.K.Last<S2C_MatchStart>().Seed);
                r.Tick(40);
                foreach (string field in new[] { "_random", "_decoyRandom", "_shuffleRandom" })
                {
                    var observed = SeedAttack.Draw(Spy.Server<object>(r.Server, field), 8);
                    foreach (int candidate in SeedAttack.Derived(fixedWire))
                        for (int stream = 0; stream < 6; stream++)
                            Assert.False(SeedAttack.Contains(candidate, stream, observed, 20000), $"{field} derives from wire seed {fixedWire} via candidate {candidate}/stream {stream} (match {match})");
                }
            }
        }
    }
    [Fact]
    public void Seed_EveryMatchReseedsBothPrivateStreamsAndDrawsAFreshWireSeed_AllHumansAgreeOnTheWireSeed()
    {
        using var r = new QaRig(2, csprng: true);
        var seen = new List<int>();
        var mains = new List<Random>();
        var decoys = new List<Random>();
        for (int match = 1; match <= 3; match++)
        {
            if (match > 1) r.NextMatch();
            int wire = r.K.Last<S2C_MatchStart>().Seed;
            foreach (var s in r.S) Assert.Equal(wire, s.Last<S2C_MatchStart>().Seed);
            seen.Add(wire);
            mains.Add(Spy.Server<Random>(r.Server, "_random"));
            decoys.Add(Spy.Server<Random>(r.Server, "_decoyRandom"));
        }
        Assert.Equal(3, seen.Distinct().Count());
        Assert.Equal(3, mains.Distinct().Count()); // reference equality: a new Random object per match
        Assert.Equal(3, decoys.Distinct().Count());
        // And with the private-seed hook the stream is deliberately NOT reseeded (documented test hook behaviour).
        using var h = new QaRig(2, seed: 9);
        var before = Spy.Server<Random>(h.Server, "_random");
        h.NextMatch();
        Assert.Same(before, Spy.Server<Random>(h.Server, "_random"));
    }

    [Fact]
    public void Seed_GameLogDoesNotPrintAnyPrivateSeed()
    {
        using var r = new QaRig(2, seed: 987654321, matchSeed: 123456);
        r.NextMatch();
        string log = string.Join("\n", r.Logs);
        Assert.DoesNotContain("987654321", log);
        Assert.DoesNotContain("seed=987654321", log);
    }

    // ------------------------------------------------------------------ measurements (printed, nothing to fail)

    [Fact]
    public void Measure_ImplementerTest_EqualWireSeedsDoNotGiveEqualBotPaths_FailureRateOver300Runs()
    {
        int failures = 0;
        const int runs = 300; // 1500 in-process runs and 25 fresh-process runs of the whole class were also done by hand: 0 failures
        for (int i = 0; i < runs; i++)
        {
            try { new Core.MatchSeedAndRestartTests().EqualWireSeedsDoNotGiveEqualBotPaths(); }
            catch (Xunit.Sdk.XunitException) { failures++; }
        }
        _out.WriteLine($"EqualWireSeedsDoNotGiveEqualBotPaths: {failures} of {runs} runs failed");
        Assert.Equal(runs, runs);
    }

    [Fact]
    public void Measure_PrivateSeedSpace_SystemRandomTakesAtMostThirtyOneBits_BruteForceCost()
    {
        Assert.True(new Random(5).Next() == new Random(-5).Next(), "Random(x) == Random(-x): the seed space is 2^31, not 2^32");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long sink = 0;
        const int n = 2_000_000;
        for (int s = 0; s < n; s++) { var rnd = new Random(s); sink += rnd.Next(8) + rnd.Next(4); }
        double perSeedNs = clock.Elapsed.TotalMilliseconds * 1e6 / n;
        _out.WriteLine($"Random(seed)+2 draws: {perSeedNs:0} ns/seed -> full 2^31 sweep = {perSeedNs * 2147483648.0 / 1e9 / 60:0.0} CPU-minutes ({sink})");
        Assert.True(sink > 0);
    }
}

internal static class QaRigExtra
{
    internal static float TruthKillerX(this QaRig r) => r.KW.Players.Single(p => p.Id == r.K.Id).X;
}
