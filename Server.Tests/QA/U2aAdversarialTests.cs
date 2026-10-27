using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for unit U2a (Server.Core), deterministic part: GameServer.Tick(dt) + InMemoryTransport.
// Expected values derive from docs/briefs/U2a-a1.md and docs/GDD.md section 3, not from the implementation.
// Fixture geometry (public contract of the brief): first player spawns at (5,5), second at (5,10).
public sealed class U2aAdversarialTests
{
    private const float Dt = 0.05f;

    private static int FilledSlots(S2C_LobbyState lobby) => lobby.Slots.Count(s => s.PlayerId != 0);
    private static int BotSlots(S2C_LobbyState lobby) => lobby.Slots.Count(s => s.PlayerId != 0 && s.IsBot == 1);
    private static float Dist(PlayerState a, PlayerState b) =>
        (float)Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Z - b.Z) * (a.Z - b.Z));
    private static bool IsSignal(PlayerState p) => (p.Flags & (byte)PlayerFlags.Signal) != 0;

    // ------------------------------------------------------------------ 1. masking bytes

    [Fact]
    public void Masking_StationarySurvivorOmittedFromKillerFrameBytes_EdgeAt015()
    {
        using var f = new QaFixture();
        var (killer, survivor) = f.StartPair();
        for (int i = 0; i < 5; i++) { survivor.Move(0.6f, 0.8f); f.Tick(); }
        var pos = survivor.Self();
        int id = survivor.Id;

        // Precondition: non-round coordinates, so a coincidental float match with terminals/time is impossible.
        Assert.NotEqual(MathF.Round(pos.X), pos.X);
        Assert.NotEqual(MathF.Round(pos.Z), pos.Z);

        // Age 0.05 s: Signal, entry and its coordinate bytes present (also validates the byte-search method).
        // U2c migration: killer-bound survivor entries are anonymous (Id 0), so "the survivor's entry" is the one survivor
        // entry of this 1v1 frame (killer skill neutralised below so a Packet Afterimage cannot keep it alive).
        Spy.Set(Spy.Player(f.Server, killer.Id), "Skill", (byte)255);
        for (int i = 0; i < 3; i++) { survivor.Move(0.6f, 0.8f); f.Tick(); }
        pos = survivor.Self();
        var w = killer.World();
        var entry = Assert.Single(w.Players, p => p.Role == (byte)Role.Survivor);
        Assert.Equal(0, entry.Id);
        Assert.True(IsSignal(entry));
        Assert.Equal(pos.X, entry.X);
        byte[] frame = PingTheKiller.Shared.Net.PacketCodec.Encode(w);
        Assert.True(QaBytes.ContainsFloat(frame, entry.X) && QaBytes.ContainsFloat(frame, entry.Z));

        // Age 0.10 s (a zero input arrives at this tick; position must not change): still Signal.
        survivor.Move(0, 0); f.Tick();
        pos = survivor.Self();
        w = killer.World();
        Assert.Contains(w.Players, p => p.Role == (byte)Role.Survivor && IsSignal(p));

        // Age 0.15 s: gone (brief: stationary >= 0.15 s is omitted). Then 0.20 s and a long silence.
        for (int i = 0; i < 40; i++)
        {
            f.Tick();
            w = killer.World();
            Assert.DoesNotContain(w.Players, p => p.Role == (byte)Role.Survivor);
            frame = PingTheKiller.Shared.Net.PacketCodec.Encode(w);
            var legit = QaBytes.LegitFloatBits(w);
            foreach (float v in new[] { pos.X, pos.Z })
                if (!legit.Contains(BitConverter.SingleToInt32Bits(v)))
                    Assert.False(QaBytes.ContainsFloat(frame, v), "stationary tick " + i + ": hidden coordinate " + v + " found in the frame");
            // The killer still sees itself.
            Assert.Contains(w.Players, p => p.Id == killer.Id);
        }

        // Moving again: entry is present again with the new coordinates.
        survivor.Move(0.6f, 0.8f); f.Tick();
        w = killer.World();
        var back = Assert.Single(w.Players, p => p.Role == (byte)Role.Survivor);
        Assert.True(IsSignal(back));
        Assert.NotEqual(pos.X, back.X);
    }

    [Fact]
    public void Masking_MovingSurvivorIsNeverOmittedEvenWithoutFurtherInputs()
    {
        // Core rule (GDD 3.1): only a *stationary* survivor may be hidden from the killer. The server keeps applying the
        // last direction, so a survivor that keeps moving after a single input must not become invisible.
        using var f = new QaFixture();
        var (killer, survivor) = f.StartPair();
        survivor.Move(1, 0);
        float prevX = survivor.Self().X;
        int hiddenWhileMoving = 0, firstHiddenTick = -1;
        for (int tick = 0; tick < 40; tick++)
        {
            f.Tick();
            float x = survivor.Self().X;
            bool moved = Math.Abs(x - prevX) > 1e-4f;
            prevX = x;
            // U2c migration: anonymous killer view; the pair has exactly one survivor, so any survivor entry is it.
            bool visible = killer.World().Players.Any(p => p.Role == (byte)Role.Survivor);
            if (moved && !visible)
            {
                hiddenWhileMoving++;
                if (firstHiddenTick < 0) firstHiddenTick = tick;
            }
        }
        Assert.True(hiddenWhileMoving == 0,
            $"survivor kept moving at 4 m/s but was omitted from the killer-bound state on {hiddenWhileMoving}/40 ticks " +
            $"(first at tick {firstHiddenTick}) after sending a single non-zero MoveInput");
    }

    [Fact]
    public void Masking_BotSurvivorsAlternateSignalAndSilent_KillerNeverSeesNonSignalEntry()
    {
        using var f = new QaFixture(seed: 99);
        var killer = f.Join("K");
        killer.Send(new C2S_SetBots { Count = 3 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal((byte)Role.Killer, killer.Last<S2C_MatchStart>().Role);
        int presentTicks = 0, absentTicks = 0;
        for (int i = 0; i < 800; i++) // 40 simulated seconds
        {
            f.Tick();
            var w = killer.World();
            foreach (var p in w.Players.Where(p => p.Id != killer.Id))
                Assert.True(IsSignal(p), "killer-bound frame contains a survivor without the Signal flag at tick " + i);
            int survivorsSeen = w.Players.Count(p => p.Id != killer.Id);
            if (survivorsSeen < 3) absentTicks++; else presentTicks++;
        }
        Assert.True(presentTicks > 0, "bot survivors never became visible (never moved) in 40 s");
        Assert.True(absentTicks > 0, "bot survivors never went Silent in 40 s");
    }

    // ------------------------------------------------------------------ 2. killer hidden from survivors

    [Fact]
    public void KillerHidden_MovingHumanKiller_NoSurvivorFrameContainsEntryOrCoordinateBytes_ThreatMatchesBands()
    {
        // U2b migration: under the U2b rules a bot's Traffic Overload halves the killer's speed, so the "threat level
        // should vary" sanity check legitimately fails. The invariant protected here (no killer coordinates in any survivor
        // frame, threat bands from the true distance) is U2a core behaviour, so this runs in sandbox mode, which keeps the
        // exact U2a rules. The same leak/threat invariants under the full U2b rules are covered by U2bAdversarialTests.
        using var f = new QaFixture(seed: 4242, sandbox: true);
        var killer = f.Join("K");
        var s1 = f.Join("S1");
        var s2 = f.Join("S2");
        killer.Send(new C2S_SetBots { Count = 2 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal((byte)Role.Killer, killer.Last<S2C_MatchStart>().Role);

        var killerDirs = new[] { (0.6f, 0.8f), (-0.8f, 0.6f), (0.7f, -0.7f), (-0.6f, -0.8f) };
        var s1Dirs = new[] { (1f, 0f), (0f, 1f), (-1f, 0f), (0f, -1f) };
        var distinctThreat = new HashSet<byte>();
        int killerMovedTicks = 0, idleHidden = 0;
        PlayerState prev = killer.Self();
        for (int tick = 0; tick < 240; tick++) // 12 s
        {
            var kd = killerDirs[tick / 30 % killerDirs.Length];
            killer.Move(kd.Item1, kd.Item2);
            var sd = s1Dirs[tick / 25 % s1Dirs.Length];
            s1.Move(sd.Item1, sd.Item2);
            // s2 stays idle after the first 20 ticks of movement
            if (tick < 20) s2.Move(0.5f, 0.5f); else if (tick == 20) s2.Move(0, 0);
            f.Tick();

            var kw = killer.World();
            var kself = kw.Players.Single(p => p.Id == killer.Id);
            if (Math.Abs(kself.X - prev.X) + Math.Abs(kself.Z - prev.Z) > 1e-4f) killerMovedTicks++;
            prev = kself;
            if (tick > 40 && !kw.Players.Any(p => p.Id == s2.Id)) idleHidden++;

            foreach (var s in new[] { s1, s2 })
            {
                var sw = s.World();
                QaBytes.AssertAbsent(sw, killer.Id, kself.X, kself.Z, "survivor " + s.Id + " tick " + tick);
                Assert.DoesNotContain(sw.Players, p => p.Role == (byte)Role.Killer);
                Assert.Equal(4, sw.Players.Count); // all four survivors (2 humans + 2 bots) incl. Silent ones
                var self = sw.Players.Single(p => p.Id == s.Id);
                float d = Dist(self, kself);
                byte expected = d > 18f ? (byte)0 : d > 12f ? (byte)1 : d > 6f ? (byte)2 : (byte)3;
                bool nearBoundary = Math.Abs(d - 18f) < 1e-3f || Math.Abs(d - 12f) < 1e-3f || Math.Abs(d - 6f) < 1e-3f;
                if (!nearBoundary) Assert.True(expected == sw.ThreatLevel, $"tick {tick} d={d}: expected threat {expected}, got {sw.ThreatLevel}");
                distinctThreat.Add(sw.ThreatLevel);
            }
        }
        Assert.True(killerMovedTicks > 150, "scenario sanity: killer must actually move");
        Assert.True(idleHidden > 100, "scenario sanity: idle survivor must be masked from the killer");
        Assert.True(distinctThreat.Count >= 2, "scenario sanity: threat level should vary");
    }

    [Fact]
    public void KillerHidden_BotKiller_SurvivorFramesAlwaysListExactlyTheSurvivors()
    {
        // Find a seed where the killer is a bot while both humans are survivors.
        for (int seed = 1; seed <= 60; seed++)
        {
            using var f = new QaFixture(seed: seed);
            var h1 = f.Join("H1");
            var h2 = f.Join("H2");
            h1.Send(new C2S_SetBots { Count = 3 });
            h1.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
            h1.Send(new C2S_StartMatch());
            f.Tick();
            if (h2.Last<S2C_MatchStart>().Role != (byte)Role.Survivor) continue;
            Assert.Equal((byte)Role.Survivor, h1.Last<S2C_MatchStart>().Role);
            var slots = h1.Lobby().Slots.Where(s => s.PlayerId != 0).ToList();
            Assert.Equal(5, slots.Count);
            var allIds = slots.Select(s => s.PlayerId).ToHashSet();

            HashSet<int>? seen = null;
            for (int i = 0; i < 400; i++) // 20 s
            {
                if (i % 40 == 0) h1.Move(0.6f, 0.8f); else if (i % 40 == 20) h1.Move(0, 0);
                f.Tick();
                foreach (var h in new[] { h1, h2 })
                {
                    var w = h.World();
                    Assert.Equal(4, w.Players.Count);
                    Assert.All(w.Players, p => Assert.Equal((byte)Role.Survivor, p.Role));
                    var ids = w.Players.Select(p => p.Id).ToHashSet();
                    Assert.Equal(4, ids.Count);
                    seen ??= ids;
                    Assert.True(seen.SetEquals(ids), "survivor list membership changed");
                }
            }
            Assert.Single(allIds.Except(seen!)); // exactly one id (the killer) never appears
            return;
        }
        Assert.Fail("no seed in 1..60 produced a bot killer with two human survivors (scenario problem)");
    }

    // ------------------------------------------------------------------ 3. threat bands

    private static void Place(QaFixture f, QaPeer survivor, float targetZ)
    {
        for (int i = 0; i < 600; i++)
        {
            float delta = targetZ - survivor.Self().Z;
            if (Math.Abs(delta) < 2e-4f) break;
            survivor.Move(0, Math.Clamp(delta / 0.2f, -1f, 1f));
            f.Tick();
        }
        survivor.Move(0, 0);
        f.Tick();
    }

    [Theory]
    [InlineData(20f, 0)]
    [InlineData(18.01f, 0)]
    [InlineData(17.99f, 1)]
    [InlineData(15f, 1)]
    [InlineData(12.01f, 1)]
    [InlineData(11.99f, 2)]
    [InlineData(9f, 2)]
    [InlineData(6.01f, 2)]
    [InlineData(5.99f, 3)]
    [InlineData(3f, 3)]
    public void ThreatBands_FollowDistanceToKiller(float distance, int expectedLevel)
    {
        using var f = new QaFixture();
        var (killer, survivor) = f.StartPair();
        float kz = killer.Self().Z;
        Place(f, survivor, kz + distance);
        float actual = Dist(survivor.Self(), killer.Self());
        Assert.InRange(actual, distance - 2e-3f, distance + 2e-3f);
        Assert.Equal(expectedLevel, survivor.World().ThreatLevel);
    }

    [Fact]
    public void ThreatBands_ExactBoundaryInclusivity_FollowsGddWording()
    {
        // GDD 3.2: level 0 is "more than 18 m" (so exactly 18 -> 1) and level 3 is "within 6 m" (so exactly 6 -> 3).
        // 12 m belongs to both "12-18" and "6-12" in the GDD table: unspecified, only a valid level is required.
        Assert.Equal(1, GameServer.ThreatForDistance(18f));
        Assert.Equal(3, GameServer.ThreatForDistance(6f));
        Assert.Equal(0, GameServer.ThreatForDistance(18.0001f));
        Assert.InRange((int)GameServer.ThreatForDistance(12f), 1, 2);
        Assert.Equal(0, GameServer.ThreatForDistance(1000f));
        Assert.Equal(3, GameServer.ThreatForDistance(0f));
    }

    [Fact]
    public void Threat_KillerOwnFrameHasNoThreatAndOnlyKillerRecipientsSeeThemselves()
    {
        using var f = new QaFixture();
        var (killer, _) = f.StartPair();
        f.Tick();
        Assert.Equal(0, killer.World().ThreatLevel);
    }

    // ------------------------------------------------------------------ 4. input sanitizing

    [Theory]
    [InlineData(float.NaN, float.NaN, false)]
    [InlineData(float.NaN, 0f, false)]
    [InlineData(float.PositiveInfinity, 0f, false)]
    [InlineData(float.NegativeInfinity, 0f, false)]
    [InlineData(float.PositiveInfinity, float.NaN, false)]
    [InlineData(float.NaN, 1f, false)]
    [InlineData(1f, float.NegativeInfinity, false)]
    [InlineData(0f, 0f, false)]
    [InlineData(1e30f, 1e30f, true)]
    [InlineData(float.MaxValue, -float.MaxValue, true)]
    [InlineData(10f, 0f, true)]
    [InlineData(1f, 1f, true)]
    [InlineData(1e-30f, 0f, true)]
    public void InputSanitizing_NeverNaN_NeverFasterThanRoleSpeedTimesDt(float dx, float dz, bool expectMotion)
    {
        foreach (bool killerRole in new[] { true, false })
        {
            using var f = new QaFixture();
            var (killer, survivor) = f.StartPair();
            var mover = killerRole ? killer : survivor;
            float cap = (killerRole ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) * Dt;
            mover.Move(1, 0); // baseline motion so a "stop" is observable
            f.Tick();
            var prev = mover.Self();
            // A2 REWRITE (spec change, a2 fix 1: a held direction lives only SignalHoldSeconds): the adversarial input is
            // resent every tick (fresh increasing Seq, like a real client holding a key) so that every one of the 6 ticks
            // is driven by it. Attempt 1 sent it once and observed 6 ticks, which relied on the unbounded hold.
            bool oversize = dx == 10f || dx == 1f && dz == 1f || dx == 1e30f || dx == float.MaxValue;
            for (int i = 0; i < 7; i++)
            {
                mover.Move(dx, dz, float.NaN);
                f.Tick();
                var cur = mover.Self();
                Assert.False(float.IsNaN(cur.X) || float.IsNaN(cur.Z) || float.IsInfinity(cur.X) || float.IsInfinity(cur.Z) || float.IsNaN(cur.Yaw));
                float step = (float)Math.Sqrt((double)(cur.X - prev.X) * (cur.X - prev.X) + (double)(cur.Z - prev.Z) * (cur.Z - prev.Z));
                Assert.True(step <= cap * 1.005f, $"role killer={killerRole} step {step} > cap {cap}");
                if (!expectMotion) Assert.Equal(0f, step);
                // oversize vectors are clamped to length 1 (full speed), not rejected (now checked on every tick)
                if (oversize) Assert.InRange(step, cap * 0.98f, cap * 1.005f);
                prev = cur;
            }
        }
    }

    // ------------------------------------------------------------------ 5. seq ordering

    [Fact]
    public void Seq_OutOfOrderAndDuplicateAreIgnored_FirstSeqZeroAccepted()
    {
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        float x0 = s.Self().X, z0 = s.Self().Z;
        s.Send(new C2S_MoveInput { DirX = 1, Seq = 5 }); f.Tick();
        float x1 = s.Self().X;
        Assert.True(x1 > x0 + 0.19f);

        // A2 REWRITE (spec change, a2 fix 1): attempt 1 sent only the stale packet and relied on the +X direction being
        // held forever. Now a fresh accepted +X input (Seq 7) renews the hold and the stale packets follow it in the same tick.
        s.Send(new C2S_MoveInput { DirX = 1, Seq = 7 });  // fresh: accepted, renews the 0.15 s hold
        s.Send(new C2S_MoveInput { DirX = -1, Seq = 3 }); // older than 7 (and 5): ignored
        s.Send(new C2S_MoveInput { DirZ = 1, Seq = 5 });  // older: ignored
        f.Tick();
        float x2 = s.Self().X;
        Assert.True(x2 > x1 + 0.19f, "Seq 3 after Seq 7 must be ignored (kept moving +X)");
        Assert.Equal(z0, s.Self().Z);

        s.Send(new C2S_MoveInput { DirX = 1, Seq = 8 });  // fresh
        s.Send(new C2S_MoveInput { DirZ = 1, Seq = 8 });  // duplicate of the just-accepted Seq: ignored
        f.Tick();
        Assert.Equal(z0, s.Self().Z);
        Assert.True(s.Self().X > x2 + 0.19f);

        s.Send(new C2S_MoveInput { DirZ = 1, Seq = 9 }); f.Tick(); // newer: accepted (positive control)
        Assert.True(s.Self().Z > z0 + 0.19f);

        using var g = new QaFixture();
        var (_, t) = g.StartPair();
        float tx = t.Self().X;
        t.Send(new C2S_MoveInput { DirX = 1, Seq = 0 }); g.Tick();
        Assert.True(t.Self().X > tx + 0.19f, "the very first input (Seq 0) must be accepted");
    }

    // A2 REWRITE (spec change, a2 fix 4: serial-number comparison, accept only if (int)(new - last) > 0).
    // Attempt 1 characterised the old behaviour ("Seq 0 after uint.MaxValue is dropped"); the new spec requires the opposite.
    // Decision for the ambiguous case: a jump of exactly 2^31 gives (int)int.MinValue <= 0 and is DROPPED; 2^31 - 1 is accepted.
    private static (float dx, float dz) Step(QaFixture f, QaPeer s, params (float x, float z, uint seq)[] inputs)
    {
        var before = s.Self();
        foreach (var i in inputs) s.Send(new C2S_MoveInput { DirX = i.x, DirZ = i.z, Seq = i.seq });
        f.Tick();
        var after = s.Self();
        return (after.X - before.X, after.Z - before.Z);
    }

    [Fact]
    public void Seq_UintWraparound_IsAcceptedInOrder_StaleAfterWrapDropped()
    {
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        const float step = GameConstants.SurvivorSpeed * Dt;
        // Each accepted input steers in its own direction; direction of the next step reveals which inputs were applied.
        var a = Step(f, s, (1, 0, uint.MaxValue - 1));      // first ever input: accepted (+X)
        Assert.InRange(a.dx, step * 0.98f, step * 1.01f); Assert.Equal(0f, a.dz);
        var b = Step(f, s, (0, 1, uint.MaxValue));          // +1: accepted (+Z)
        Assert.InRange(b.dz, step * 0.98f, step * 1.01f); Assert.Equal(0f, b.dx);
        var c = Step(f, s, (-1, 0, 0));                     // wrapped to 0: accepted (-X)
        Assert.InRange(c.dx, -step * 1.01f, -step * 0.98f); Assert.Equal(0f, c.dz);
        var d = Step(f, s, (0, -1, 1));                     // 1: accepted (-Z)
        Assert.InRange(d.dz, -step * 1.01f, -step * 0.98f); Assert.Equal(0f, d.dx);

        // Stale values after the wrap are dropped: a fresh Seq 2 (+X) renews the hold, then older ones must not steer.
        var e = Step(f, s, (1, 0, 2), (0, 1, uint.MaxValue), (0, 1, uint.MaxValue - 1), (0, 1, 1), (0, 1, 0), (0, 1, 2));
        Assert.InRange(e.dx, step * 0.98f, step * 1.01f);
        Assert.Equal(0f, e.dz);
    }

    [Fact]
    public void Seq_HalfRangeJump_Exactly2Pow31IsDropped_2Pow31MinusOneIsAccepted()
    {
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        const float step = GameConstants.SurvivorSpeed * Dt;
        var a = Step(f, s, (1, 0, 10));
        Assert.InRange(a.dx, step * 0.98f, step * 1.01f);
        // 10 + 2^31: (int)(new - last) == int.MinValue -> not "newer". The held +X (age 0.10 s) continues; +Z must not steer.
        var b = Step(f, s, (0, 1, 10u + 0x80000000u));
        Assert.Equal(0f, b.dz);
        Assert.InRange(b.dx, step * 0.98f, step * 1.01f);
        // 10 + 2^31 - 1: (int)(new - last) == int.MaxValue -> newer, accepted.
        var c = Step(f, s, (0, 1, 10u + 0x7FFFFFFFu));
        Assert.InRange(c.dz, step * 0.98f, step * 1.01f);
        Assert.Equal(0f, c.dx);
    }

    [Fact]
    public void Seq_FirstInputWithUintMaxValue_IsAcceptedAndSessionKeepsWorking()
    {
        // The attempt-1 hazard: one Seq = uint.MaxValue froze the session. Now Seq 0 right after it is the next valid one.
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        float x = s.Self().X;
        s.Send(new C2S_MoveInput { DirX = 1, Seq = uint.MaxValue }); f.Tick();
        Assert.True(s.Self().X > x + 0.19f);
        x = s.Self().X;
        s.Send(new C2S_MoveInput { DirX = -1, Seq = 0 }); f.Tick();
        Assert.True(s.Self().X < x - 0.19f, "Seq 0 after Seq uint.MaxValue must be accepted (serial-number comparison)");
    }

    // ------------------------------------------------------------------ 6. rate cap

    [Fact]
    public void RateCap_AtMost60InputsPerSecondAccepted_ThenWindowReopens()
    {
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        float x0 = s.Self().X;
        // 200 inputs inside one simulated second: the first 60 steer +X, the other 140 would steer -X.
        for (int i = 0; i < 200; i++)
            s.Send(new C2S_MoveInput { DirX = i < 60 ? 1f : -1f, Seq = (uint)(i + 1) });
        // A2 REWRITE (spec change, a2 fix 1): the 60th accepted input (+X) now holds only 0.15 s, i.e. 2 movement ticks
        // (~0.4 m), not the ~0.95 s / 3.8 m of attempt 1. If any of the 140 later -X inputs were applied, x would go backwards.
        f.Tick();
        Assert.True(s.Self().X > x0 + 0.19f, "60th accepted input (+X) must steer on the burst tick");
        f.Tick();
        float x1 = s.Self().X;
        Assert.True(x1 > x0 + 0.39f, $"inputs beyond the 60/s cap were applied (x moved {x1 - x0}, expected ~+0.4 m)");
        for (int i = 0; i < 17; i++) f.Tick(); // 19 ticks = server clock T+0.95 s since the burst

        // New (kept intent, sharper): an input inside the same 1 s window is still rejected (window not reopened early).
        float xr = s.Self().X;
        s.Send(new C2S_MoveInput { DirX = -1, Seq = 500 });
        f.Tick();
        Assert.Equal(xr, s.Self().X); // accepted would have moved -0.2 m (the hold from the burst is long expired)

        // After the window has passed (>= 1 s) a new input is accepted again.
        f.Tick();
        float x2 = s.Self().X;
        s.Send(new C2S_MoveInput { DirX = -1, Seq = 1000 });
        f.Tick();
        Assert.True(s.Self().X < x2 - 0.19f, "input after the 1 s window must be accepted again");
    }

    [Fact]
    public void RateCap_LegitimateTwentyHertzClientIsNeverThrottled()
    {
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        for (int i = 0; i < 100; i++) // 5 s of one input per tick, direction alternates every tick
        {
            s.Move(i % 2 == 0 ? 1 : -1, 0);
            float before = s.Self().X;
            f.Tick();
            float after = s.Self().X;
            if (i % 2 == 0) Assert.True(after > before + 0.19f, "tick " + i);
            else Assert.True(after < before - 0.19f, "tick " + i);
        }
    }

    // ------------------------------------------------------------------ 7. reconnect window

    private static void Disconnect(QaFixture f, QaPeer peer)
    {
        peer.Connection.Close();
        f.Tick(); // server processes the close here: the 10 s window starts at this tick
    }

    private static (QaFixture f, QaPeer killer, QaPeer survivor, int id, ulong token, PlayerState pos) PrepareReconnect()
    {
        var f = new QaFixture();
        var (killer, survivor) = f.StartPair();
        for (int i = 0; i < 10; i++) { survivor.Move(0.6f, 0.8f); f.Tick(); }
        survivor.Move(0, 0); f.Tick(); f.Tick();
        var pos = survivor.Self();
        int id = survivor.Id;
        ulong token = survivor.Last<S2C_JoinResult>().ReconnectToken;
        Assert.NotEqual(0UL, token);
        Disconnect(f, survivor);
        return (f, killer, survivor, id, token, pos);
    }

    private static S2C_JoinResult TryJoin(QaFixture f, QaPeer? tokenPeer, ulong token, out QaPeer peer)
    {
        peer = f.Connect();
        peer.Send(new C2S_Join { Nickname = "Back", ReconnectToken = token });
        f.Tick();
        return peer.Last<S2C_JoinResult>();
    }

    [Fact]
    public void Reconnect_At9_9s_RestoresPlayerIdAndPosition()
    {
        var (f, killer, _, id, token, pos) = PrepareReconnect();
        using (f)
        {
            f.Ticks(197); // 0.05 + 197 * 0.05 = 9.90 s after the disconnect was processed
            var result = TryJoin(f, null, token, out var peer);
            Assert.Equal(0, result.Result);
            Assert.Equal(id, result.PlayerId);
            var self = peer.World().Players.Single(p => p.Id == id);
            Assert.Equal(pos.X, self.X);
            Assert.Equal(pos.Z, self.Z);
            Assert.True(peer.Has<S2C_MatchStart>());
            Assert.Equal((byte)Role.Survivor, self.Role);
            // slot stays human
            Assert.Equal(0, BotSlots(killer.Lobby()));
        }
    }

    [Fact]
    public void Reconnect_At10_1s_BotOwnsSlot_TokenNoLongerReclaimsIt()
    {
        var (f, killer, _, id, token, pos) = PrepareReconnect();
        using (f)
        {
            f.Ticks(201); // 10.10 s
            var lobby = killer.Lobby();
            var slot = Assert.Single(lobby.Slots, s => s.PlayerId == id);
            Assert.Equal(1, slot.IsBot);

            var result = TryJoin(f, null, token, out var peer);
            Assert.False(result.Result == 0 && result.PlayerId == id && result.ReconnectToken == token,
                "stale token reclaimed the slot after the 10 s window");

            // The bot actually drives the slot: it walks, so the killer sees it Signal at some position away from the stop point.
            // U2c migration: the killer entry is anonymous (the only survivor in this pair), and a survivor bot waits 2-8 s
            // before its first walk (burst pacing), so the window is 20 s instead of 10 s.
            bool movedAway = false;
            for (int i = 0; i < 400 && !movedAway; i++)
            {
                f.Tick();
                var e = killer.World().Players.FirstOrDefault(p => p.Role == (byte)Role.Survivor);
                if (e.Role == (byte)Role.Survivor && Math.Abs(e.X - pos.X) + Math.Abs(e.Z - pos.Z) > 1f) movedAway = true;
            }
            Assert.True(movedAway, "bot never moved the expired player's character");
        }
    }

    [Fact]
    public void Reconnect_WrongTokensNeverGrabASlot_RealTokenStillWorks()
    {
        var (f, killer, _, id, token, pos) = PrepareReconnect();
        using (f)
        {
            f.Ticks(20);
            foreach (ulong wrong in new[] { 0xDEADBEEFUL, token ^ 1UL, ~token, 1UL })
            {
                var r = TryJoin(f, null, wrong, out var intruder);
                Assert.NotEqual(0, r.Result);
                Assert.False(intruder.Has<S2C_MatchStart>());
                Assert.False(intruder.Has<S2C_WorldState>());
            }
            // killer slot untouched, survivor slot still reserved for the real token
            Assert.Equal(2, FilledSlots(killer.Lobby()));
            var result = TryJoin(f, null, token, out var peer);
            Assert.Equal(0, result.Result);
            Assert.Equal(id, result.PlayerId);
            Assert.Equal(pos.X, peer.World().Players.Single(p => p.Id == id).X);
        }
    }

    // ------------------------------------------------------------------ 8. lobby authority

    [Fact]
    public void Lobby_NonHostCommandsAreIgnored()
    {
        using var f = new QaFixture();
        var host = f.Join("Host");
        var guest = f.Join("Guest");
        host.Send(new C2S_SetBots { Count = 2 }); f.Tick();
        var before = host.Lobby();
        Assert.Equal(4, FilledSlots(before));
        guest.Send(new C2S_SetBots { Count = 5 });
        guest.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        guest.Send(new C2S_StartMatch());
        f.Ticks(3);
        var after = host.Lobby();
        Assert.Equal(4, FilledSlots(after));
        Assert.Equal(before.HostRoleChoice, after.HostRoleChoice);
        Assert.False(host.Has<S2C_MatchStart>());
        Assert.False(guest.Has<S2C_MatchStart>());
        Assert.False(host.Has<S2C_WorldState>());
        // positive control: the host can
        host.Send(new C2S_StartMatch()); f.Tick();
        Assert.True(host.Has<S2C_MatchStart>());
        Assert.True(guest.Has<S2C_MatchStart>());
    }

    [Fact]
    public void Lobby_SetBotsClampsTotalBetween2And8()
    {
        using var f = new QaFixture();
        var host = f.Join("Host");
        host.Send(new C2S_SetBots { Count = 0 }); f.Tick();
        Assert.Equal(2, FilledSlots(host.Lobby())); // 1 human + at least 1 bot
        host.Send(new C2S_SetBots { Count = 7 }); f.Tick();
        Assert.Equal(8, FilledSlots(host.Lobby()));
        host.Send(new C2S_SetBots { Count = 3 }); f.Tick();
        Assert.Equal(4, FilledSlots(host.Lobby()));
        Assert.Equal(3, BotSlots(host.Lobby()));

        var guest = f.Join("Guest");
        host.Send(new C2S_SetBots { Count = 0 }); f.Tick();
        Assert.Equal(2, FilledSlots(host.Lobby())); // two humans, zero bots is legal
        host.Send(new C2S_SetBots { Count = 7 }); f.Tick();
        Assert.Equal(8, FilledSlots(host.Lobby()));
        Assert.Equal(6, BotSlots(host.Lobby()));
        Assert.Equal(8, FilledSlots(guest.Lobby()));
    }

    [Fact]
    public void Lobby_NinthHumanIsRejectedWithoutDisturbingTheMatch_AndEightHumansCanPlay()
    {
        using var f = new QaFixture();
        var humans = new List<QaPeer>();
        for (int i = 0; i < 8; i++) humans.Add(f.Join("H" + i));
        Assert.Equal(8, FilledSlots(humans[0].Lobby()));
        var ninth = f.Connect();
        ninth.Send(new C2S_Join { Nickname = "H8" });
        f.Tick();
        Assert.NotEqual(0, ninth.Last<S2C_JoinResult>().Result);
        Assert.Equal(8, FilledSlots(humans[0].Lobby()));
        Assert.Equal(8, humans.Select(h => h.Id).Distinct().Count());
        // rejected peer is still a functioning connection
        ninth.Send(new C2S_Ping { Timestamp = 99 });
        f.Tick();
        Assert.Equal(99, ninth.Last<S2C_Pong>().Timestamp);

        humans[0].Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal(1, humans.Count(h => h.Last<S2C_MatchStart>().Role == (byte)Role.Killer));
        f.Ticks(60);
        foreach (var h in humans)
        {
            var w = h.World();
            // the killer sees only itself (all 7 idle survivors are Silent = omitted); survivors see the 7 survivors
            if (h.Last<S2C_MatchStart>().Role == (byte)Role.Killer) Assert.Single(w.Players);
            else Assert.Equal(7, w.Players.Count);
            GameConstants.TerminalsFor(7, out int required, out int placed);
            Assert.Equal(placed, w.Terminals.Count);
            Assert.Equal(required, w.TerminalsRequired);
        }
        var starts = humans.Select(h => h.Self()).Select(p => (p.X, p.Z)).Distinct().Count();
        Assert.True(starts >= 7, "players share spawn points");
        Assert.False(ninth.Has<S2C_MatchStart>());
        Assert.False(ninth.Has<S2C_WorldState>());
    }

    [Fact]
    public void Lobby_HumanJoiningAFullBotLobbyReplacesABot()
    {
        using var f = new QaFixture();
        var host = f.Join("Host");
        host.Send(new C2S_SetBots { Count = 7 }); f.Tick();
        Assert.Equal(8, FilledSlots(host.Lobby()));
        var guest = f.Join("Guest");
        var lobby = host.Lobby();
        Assert.Equal(8, FilledSlots(lobby));
        Assert.Equal(6, BotSlots(lobby));
        Assert.Equal(1, lobby.Slots.Count(s => s.IsHost == 1));
        Assert.Equal(host.Id, lobby.Slots.Single(s => s.IsHost == 1).PlayerId);
    }

    [Theory]
    [InlineData("A\u0000B\tC\nD", "ABCD")]
    [InlineData("\u0007\u0007Bob\u001b[31m", "Bob[31m")]
    [InlineData("  Bob  ", "Bob")]
    [InlineData(" Bob ", "Bob")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData("\t\r\n", null)]
    [InlineData("\u0000\u0001\u0002", null)]
    public void Lobby_NicknameIsSanitized(string nick, string? expected)
    {
        using var f = new QaFixture();
        var p = f.Join(nick);
        var slot = p.Lobby().Slots.Single(s => s.PlayerId == p.Id);
        if (expected == null) Assert.Equal("Player " + p.Id, slot.Nickname);
        else Assert.Equal(expected, slot.Nickname);
        Assert.DoesNotContain(slot.Nickname, c => char.IsControl(c));
    }

    // ------------------------------------------------------------------ extra: map bounds & walls

    [Fact]
    public void Movement_NeverLeavesMapOrEntersWalls_InAnyDirection()
    {
        using var f = new QaFixture();
        var (_, s) = f.StartPair();
        var dirs = new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f), (1f, 0f), (0f, 1f), (-1f, 0f), (0f, -1f) };
        foreach (var d in dirs)
        {
            for (int i = 0; i < 400; i++) // 20 s per direction
            {
                s.Move(d.Item1, d.Item2);
                f.Tick();
                var p = s.Self();
                Assert.True(f.Server.Map.CanStand(p.X, p.Z), $"inside a wall at ({p.X},{p.Z}) walking {d}");
                Assert.InRange(p.X, 0.399f, 59.601f);
                Assert.InRange(p.Z, 0.399f, 59.601f);
            }
        }
    }
}
