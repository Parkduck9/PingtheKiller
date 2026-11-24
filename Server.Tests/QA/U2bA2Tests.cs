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
//   F0  2 s execution grace            (grace boundary ticks, spent re-entry, per-survivor timers, three matches)
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

    private static QaRig Captured(int survivors = 2, float dt = 0.05f)
    {
        var r = new QaRig(survivors) { Dt = dt };
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick(); // tick 0: the capture
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        return r;
    }
    private static void Away(QaRig r, int ticks) { for (int i = 0; i < ticks; i++) { r.Place(r.K, 5, 5); r.Tick(); } }
    private static void Touch(QaRig r) { r.Place(r.K, 12.0f, 10); r.Tick(); }

    [Theory]
    [InlineData(0.05f, 2, false)]
    [InlineData(0.05f, 10, false)]
    [InlineData(0.05f, 38, false)]
    [InlineData(0.05f, 39, false)] // 1.95 s after the capture
    [InlineData(0.05f, 40, true)]  // exactly 2.00 s: the grace is over
    [InlineData(0.05f, 41, true)]
    [InlineData(0.05f, 60, true)]
    [InlineData(0.1f, 19, false)]
    [InlineData(0.1f, 20, true)]
    [InlineData(0.025f, 79, false)]
    [InlineData(0.025f, 80, true)]
    public void Grace_LeaveAfterCaptureThenTouchAgain_ExecutesOnlyFromTwoSecondsOn(float dt, int touchTick, bool executes)
    {
        using var r = Captured(dt: dt);
        Away(r, touchTick - 1); // ticks 1 .. touchTick-1: out of range (armed)
        Touch(r);               // tick touchTick
        Assert.Equal(executes, Is(r.Truth(r.S[0]), PlayerStatus.Eliminated));
        if (executes) return;
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        // The touch inside the grace is spent: staying in contact for 10 more seconds never executes...
        r.Tick((int)(10 / dt));
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), "a touch inside the grace must use up the re-entry");
        // ...and a genuine re-entry after the grace does.
        Away(r, 1);
        Touch(r);
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated));
    }

    [Fact]
    public void Grace_TouchInsideGraceThenLeavingRearmsAtOnce_ReEntryAfterTheGraceExecutes()
    {
        using var r = Captured();
        Away(r, 4);   // ticks 1-4
        Touch(r);     // tick 5, 0.25 s: spent
        Away(r, 1);   // tick 6: left, armed
        Touch(r);     // tick 7: still inside the grace, spent again
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Away(r, 1);   // tick 8: armed
        r.Tick(2);    // ticks 9-10 (killer stays away: armed)
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Away(r, 30);  // up to tick 40
        Touch(r);     // tick 41, 2.05 s
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated));
    }

    [Fact]
    public void Grace_ContinuousContactFromTheCaptureNeverExecutes_ForTenSeconds_ThenLeaveAndTouchDoes()
    {
        using var r = Captured();
        for (int i = 0; i < 200; i++)
        {
            r.Place(r.K, 11.9f + (i % 5) * 0.1f, 10); // jitter inside 1.2 m
            r.Tick();
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), $"executed by continuing contact at tick {i + 1}");
        }
        Away(r, 1);
        Touch(r);
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated));
    }

    [Fact]
    public void Grace_EachSurvivorHasItsOwnTwoSecondTimer()
    {
        using var r = new QaRig(3);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick();                                  // tick 0: S0 captured
        Away(r, 19);                               // ticks 1-19
        r.Place(r.K, 11.9f, 10);
        r.Place(r.S[1], 11.9f, 11.0f);
        r.Tick();                                  // tick 20: S1 captured, S0 touched inside its grace
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        Away(r, 21);                               // ticks 21-41: both armed
        r.Place(r.K, 12.4f, 10.5f);                // 0.78 m from S0, 0.71 m from S1
        r.Tick();                                  // tick 42: S0 is 2.1 s old, S1 1.1 s
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated), "S0's grace was over");
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging), "S1 is still inside its own grace");
        Away(r, 18);                               // ticks 43-60
        r.Place(r.K, 12.4f, 10.5f);
        r.Tick();                                  // tick 61: S1 is 2.05 s old
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Eliminated));
        Assert.True(Is(r.Truth(r.S[2]), PlayerStatus.Normal));
    }

    [Fact]
    public void Grace_ThreeMatchesBackToBack_NoStaleArmedOrGraceState_AcrossMatches()
    {
        using var r = new QaRig(2);
        for (int match = 1; match <= 3; match++)
        {
            if (match > 1) r.NextMatch();
            r.Place(r.S[0], 13, 10);
            r.Place(r.K, 11.9f, 10);
            r.Tick();                                        // capture
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), $"match {match}: capture");
            Away(r, 3);
            Touch(r);                                        // 0.2 s: inside the grace
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), $"match {match}: touch at 0.2 s must not execute");
            Away(r, 1);                                      // armed again
            if (match < 3) continue;                         // matches 1 and 2 end with S0 Lagging and armed
            Away(r, 40);
            Touch(r);
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated), "match 3: execution after the grace");
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
        r.Place(r.S[0], 40, 36);
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
        r.Place(r.S[0], 40, 36);
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
            if (!ended) { Spy.SetServer(f.Server, "_matchTime", 599.9); f.Tick(3); Absorb(); }
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
