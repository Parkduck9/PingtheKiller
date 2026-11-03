using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for the U2b skills (GDD section 4): durations, cooldowns, routing and invalid uses.
public sealed class U2bSkillTests
{
    private static bool Seen(QaRig r, QaPeer s) => r.KillerSees(s) != null;
    private static List<S2C_SkillEffect> Effects(QaPeer p) { p.Drain(); return p.Packets.OfType<S2C_SkillEffect>().ToList(); }

    // Ticks between two accepted casts, spamming UseSkill every tick. `count` reads the caster's own effect counter.
    private static int CastGap(QaRig r, QaPeer caster, byte skill, int limit = 400)
    {
        r.Use(caster, skill);
        r.Tick();
        int c0 = r.Ticks, e0 = Effects(caster).Count;
        Assert.True(e0 >= 1, "first cast must be accepted");
        for (int i = 0; i < limit; i++)
        {
            r.Use(caster, skill);
            r.Tick();
            if (Effects(caster).Count > e0) return r.Ticks - c0;
        }
        return -1;
    }

    private static float KillerX(QaRig r) => r.KW.Players.Single(p => p.Id == r.K.Id).X;

    // ------------------------------------------------------------------ Ping Scanner

    [Fact]
    public void PingScanner_ForcesNearestSurvivorVisibleForOnePointFiveSeconds_RoutesEffectsWithoutKillerCoordinates()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.K, SkillIds.PingScanner);
        r.Place(r.K, 10, 10);
        r.Place(r.S[0], 14, 10);
        r.Place(r.S[1], 30, 10);
        r.Tick(3);
        Assert.False(Seen(r, r.S[0]) || Seen(r, r.S[1]));
        r.Use(r.K, SkillIds.PingScanner);
        r.Tick();
        int frames = 0;
        while (Seen(r, r.S[0]) && frames < 100)
        {
            Assert.Equal((byte)PlayerFlags.Signal, r.KillerSees(r.S[0])!.Value.Flags);
            Assert.False(Seen(r, r.S[1]), "only the nearest survivor is exposed");
            frames++;
            r.Tick();
        }
        Assert.InRange(frames, 29, 31); // 1.5 s
        var k = Effects(r.K);
        Assert.Single(k);
        Assert.Equal(1.5f, k[0].Duration);
        var s0 = Effects(r.S[0]);
        var e = Assert.Single(s0);
        Assert.Equal(r.K.Id, e.CasterId);
        Assert.Equal(SkillIds.PingScanner, e.SkillId);
        Assert.True(e.X == 0 && e.Z == 0, "survivor-bound effect of a killer skill must not carry the killer position");
        Assert.Empty(Effects(r.S[1]));
    }

    [Fact]
    public void PingScanner_OverridesStealth_AndHasATwelveSecondCooldown()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.K, SkillIds.PingScanner);
        r.SetSkill(r.S[0], SkillIds.StealthRelay);
        r.Place(r.K, 10, 10);
        r.Place(r.S[0], 14, 10);
        r.Place(r.S[1], 40, 50);
        r.Use(r.S[0], SkillIds.StealthRelay);
        r.Tick();
        r.Use(r.K, SkillIds.PingScanner);
        for (int i = 0; i < 3; i++) { r.S[0].Move(0, -1); r.Tick(); }
        Assert.True(Seen(r, r.S[0]), "forced exposure is not overridden by Stealth Relay");
        Assert.NotEqual(0, r.Truth(r.S[0]).Flags & (byte)PlayerFlags.Stealthed);
        using var c = new QaRig(2);
        c.SetSkill(c.K, SkillIds.PingScanner);
        c.Place(c.S[0], 14, 10);
        c.Place(c.K, 10, 10);
        Assert.InRange(CastGap(c, c.K, SkillIds.PingScanner), 239, 241); // 12 s
    }

    // ------------------------------------------------------------------ Overclock

    [Fact]
    public void Overclock_BoostsOnePointFiveForThreeSecondsThenStandsStillForOneSecond_ThenCooldownTwelveSeconds()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.Overclock);
        r.Place(r.K, 3, 5);
        r.Tick();
        float baseStep = GameConstants.KillerSpeed / GameConstants.TickRate;
        var steps = new List<float>();
        float x = KillerX(r);
        r.Use(r.K, SkillIds.Overclock);
        for (int i = 0; i < 90; i++)
        {
            r.K.Move(1, 0);
            r.Tick();
            float nx = KillerX(r);
            steps.Add(nx - x);
            x = nx;
        }
        int boosted = steps.Count(s => s > baseStep * 1.4f);
        int frozen = steps.Count(s => Math.Abs(s) < 1e-6f);
        Assert.InRange(boosted, 59, 61);
        Assert.InRange(frozen, 19, 21);
        Assert.Equal(90 - boosted - frozen, steps.Count(s => Math.Abs(s - baseStep) < baseStep * 0.01f));
        // order: normal (cast tick), boosted block, frozen block, normal again
        int firstBoost = steps.FindIndex(s => s > baseStep * 1.4f);
        int firstFrozen = steps.FindIndex(s => Math.Abs(s) < 1e-6f);
        Assert.True(firstBoost <= 2 && firstFrozen == firstBoost + boosted);
        Assert.Empty(Effects(r.S[0])); // never announced to survivors
        var effect = Assert.Single(Effects(r.K));
        Assert.Equal(3f, effect.Duration);
        using var c = new QaRig(1);
        c.SetSkill(c.K, SkillIds.Overclock);
        Assert.InRange(CastGap(c, c.K, SkillIds.Overclock), 239, 241);
    }

    [Fact]
    public void Overclock_StackedWithTrafficOverload_MultipliesSpeedForTwoSeconds()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.Overclock);
        r.SetSkill(r.S[0], SkillIds.TrafficOverload);
        r.Place(r.K, 3, 5);
        r.Tick();
        float b = GameConstants.KillerSpeed / GameConstants.TickRate;
        var steps = new List<float>();
        float x = KillerX(r);
        r.Use(r.K, SkillIds.Overclock);
        r.Use(r.S[0], SkillIds.TrafficOverload);
        for (int i = 0; i < 90; i++)
        {
            r.K.Move(1, 0);
            r.Tick();
            float nx = KillerX(r);
            steps.Add(nx - x);
            x = nx;
        }
        int both = steps.Count(s => Math.Abs(s - b * 0.75f) < b * 0.01f);   // 1.5 x 0.5
        int boostOnly = steps.Count(s => Math.Abs(s - b * 1.5f) < b * 0.01f);
        int frozen = steps.Count(s => Math.Abs(s) < 1e-6f);
        Assert.InRange(both, 39, 41);      // Traffic Overload lasts 2 s
        Assert.InRange(boostOnly, 19, 21); // 3 s boost - 2 s overlap
        Assert.InRange(frozen, 19, 21);    // 1 s recovery is not slowed further or shortened
    }

    // ------------------------------------------------------------------ Firewall Block (see also U2bAdversarialTests)

    [Fact]
    public void FirewallBlock_IsIgnoredWhileTheExitIsClosed_AndCooldownIsTwelveSecondsOnceCast()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.FirewallBlock);
        for (int i = 0; i < 5; i++) { r.Use(r.K, SkillIds.FirewallBlock); r.Tick(); }
        Assert.Empty(Effects(r.K));
        r.OpenExitFlag();
        Assert.InRange(CastGap(r, r.K, SkillIds.FirewallBlock), 239, 241);
    }

    // ------------------------------------------------------------------ Packet Afterimage

    [Fact]
    public void PacketAfterimage_KeepsLastSeenPositionForOneSecond_EvenWhileTheSurvivorKeepsMovingStealthed_FlagsExact()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.PacketAfterimage);
        r.SetSkill(r.S[0], SkillIds.StealthRelay);
        r.Place(r.K, 3, 3);
        r.Place(r.S[0], 8, 20);
        for (int i = 0; i < 10; i++) { r.S[0].Move(0, 1); r.Tick(); }
        var last = r.KillerSees(r.S[0])!.Value;
        Assert.Equal((byte)PlayerFlags.Signal, last.Flags);
        r.Use(r.S[0], SkillIds.StealthRelay);
        int afterimages = 0, absentAfter = 0;
        for (int i = 0; i < 38; i++) // Stealth lasts 40 ticks; afterwards the moving survivor is a normal Signal again
        {
            r.S[0].Move(0, 1); // keeps moving; Stealth hides it
            r.Tick();
            var truth = r.Truth(r.S[0]);
            // U2c migration: the afterimage is no longer flagged (Flags is Signal only), so it is the one anonymous entry
            // that does NOT sit at the survivor's live position.
            var entries = r.KillerSurvivorEntries.ToList();
            Assert.Null(r.KillerSees(r.S[0])); // the live, hidden position is never shown
            if (entries.Count == 1)
            {
                var seen = entries[0];
                Assert.Equal((byte)PlayerFlags.Signal, seen.Flags);
                // The afterimage must be frozen at a position the killer really saw, never the live (hidden) position.
                Assert.True(seen.Z <= last.Z + 0.21f, "afterimage drifted with the hidden survivor: " + seen.Z + " vs " + last.Z);
                Assert.NotEqual(truth.Z, seen.Z);
                byte[] frame = PingTheKiller.Shared.Net.PacketCodec.Encode(r.KW);
                Assert.False(QaWire.ContainsFloat(frame, truth.Z), "live Z of the hidden survivor leaked");
                afterimages++;
            }
            else { Assert.Empty(entries); absentAfter++; }
        }
        Assert.InRange(afterimages, 19, 21); // 1 s
        Assert.True(absentAfter >= 17);      // Stealth still hides it after the afterimage ended
    }

    [Fact]
    public void PacketAfterimage_OnlyForAKillerThatOwnsIt_AndUseSkillOnPassiveIdIsIgnored()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.PingScanner); // a different skill: no afterimage
        r.Place(r.S[0], 8, 20);
        for (int i = 0; i < 5; i++) { r.S[0].Move(0, 1); r.Tick(); }
        // U2c migration: without the skill the tail is 0 ticks. The afterimage flag no longer exists, so "no afterimage" is
        // observed as "no survivor entry at all once the 0.15 s Signal hold (3 ticks) is over".
        int lingering = 0;
        for (int i = 0; i < 40; i++)
        {
            r.Tick();
            if (i >= 4 && r.KillerSurvivorEntries.Any()) lingering++;
        }
        Assert.Equal(0, lingering);
        using var q = new QaRig(1);
        q.SetSkill(q.K, SkillIds.PacketAfterimage);
        for (int i = 0; i < 30; i++) { q.Use(q.K, SkillIds.PacketAfterimage); q.Tick(); }
        Assert.Empty(Effects(q.K));
        Assert.Empty(Effects(q.S[0]));
    }

    // ------------------------------------------------------------------ Dummy Packet

    [Fact]
    public void DummyPacket_ShowsOneAnonymousFakeAtTheCastPointFirst_ThenAGhostSurvivorForAtMostThreeSeconds_NoEffectToKiller()
    {
        // U2c migration (was: a fake with a borrowed Id frozen at the cast point for exactly 3 s). The invariants kept: the
        // fake is born at the caster's position (GDD "current position"), looks exactly like a real Signal, lives at most 3 s,
        // never reaches the killer as an effect and is announced to the other survivors with the caster Id. What changed:
        // no Id, and the fake now walks like a survivor (SurvivorSpeed, walkable) and may fall Silent in pauses like a bot.
        using var r = new QaRig(2);
        r.SetSkill(r.S[0], SkillIds.DummyPacket);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 8, 10);
        r.Place(r.S[1], 30, 10);
        r.Tick(3);
        Assert.Single(r.KW.Players); // everyone silent
        r.Use(r.S[0], SkillIds.DummyPacket);
        r.Tick();
        var map = new FixedMap();
        var first = Assert.Single(r.KillerSurvivorEntries);
        Assert.Equal(8f, first.X);
        Assert.Equal(10f, first.Z);
        int visibleFrames = 0, lastVisibleFrame = 0;
        float px = first.X, pz = first.Z;
        for (int frame = 1; frame <= 70; frame++)
        {
            var entries = r.KillerSurvivorEntries.ToList();
            Assert.True(entries.Count <= 1, "one cast makes one fake");
            if (entries.Count == 1)
            {
                var d = entries[0];
                Assert.Equal(0, d.Id);
                Assert.Equal((byte)Role.Survivor, d.Role);
                Assert.Equal((byte)PlayerStatus.Normal, d.Status);
                Assert.Equal((byte)PlayerFlags.Signal, d.Flags);
                Assert.Equal(0f, d.StatusTimer);
                Assert.Equal(0, d.CaptureCount);
                Assert.Equal(0f, d.Yaw);
                Assert.True(map.CanStand(d.X, d.Z), $"fake inside a wall at ({d.X},{d.Z})");
                Assert.True(RigDist(px, pz, d.X, d.Z) <= GameConstants.SurvivorSpeed / GameConstants.TickRate * 8 + 1e-3f, "fake jumped");
                px = d.X; pz = d.Z;
                visibleFrames++;
                lastVisibleFrame = frame;
            }
            r.Tick();
        }
        Assert.InRange(visibleFrames, 1, 62);       // 3 s lifetime, possibly with Silent pauses
        Assert.InRange(lastVisibleFrame, 1, 61);    // nothing outlives the 3 s skill
        Assert.Empty(Effects(r.K));
        var e = Assert.Single(Effects(r.S[1]));
        Assert.Equal(SkillIds.DummyPacket, e.SkillId);
        Assert.Equal(r.S[0].Id, e.CasterId); // survivor-bound effects keep the caster
        Assert.Equal(8f, e.X);
        Assert.Equal(10f, e.Z);
        Assert.Equal(3f, e.Duration);
    }

    private static float RigDist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    [Fact]
    public void DummyPacket_FakeIsIndependentOfTheCaster_NoIdToTrack_NeverAnExtraEntryAtTheCasterOrADuplicate()
    {
        // U2c migration of the F3 characterisation ("decoy Id jumps back to the cast point"). That tell needed an Id shared
        // between the decoy and the real caster; the Id is gone. What must hold now: while the caster walks away as a real
        // Signal, the killer frame has the caster at its LIVE position, the fake (if shown) is a second anonymous entry,
        // and nothing is ever shown at the caster's position besides the caster itself.
        using var r = new QaRig(1);
        r.SetSkill(r.S[0], SkillIds.DummyPacket);
        r.Place(r.S[0], 8, 10);
        r.Tick(2);
        r.Use(r.S[0], SkillIds.DummyPacket);
        r.Tick();
        Assert.Single(r.KillerSurvivorEntries);
        for (int i = 0; i < 10; i++) // 2 m at 4 m/s
        {
            r.S[0].Move(0, 1);
            r.Tick();
            Assert.NotNull(r.KillerSees(r.S[0]));
            var self = r.S[0].Self();
            int atCaster = r.KillerSurvivorEntries.Count(p => RigDist(p.X, p.Z, self.X, self.Z) < 0.001f);
            Assert.Equal(1, atCaster);
            Assert.InRange(r.KillerSurvivorEntries.Count(), 1, 2);
            Assert.All(r.KillerSurvivorEntries, p => Assert.Equal(0, p.Id));
        }
        float liveZ = r.S[0].Self().Z;
        Assert.InRange(liveZ, 11.9f, 12.1f);
        // Silent again: whatever remains is a fake (or none), never a second entry at the live caster position, never a duplicate.
        for (int i = 0; i < 12; i++)
        {
            r.Tick();
            Assert.InRange(r.KillerSurvivorEntries.Count(), 0, 2);
            Assert.All(r.KillerSurvivorEntries, p => Assert.Equal(0, p.Id));
        }
    }
    // ------------------------------------------------------------------ Stealth Relay

    [Fact]
    public void StealthRelay_HidesCasterAndAlliesWithin5mForTwoSeconds_NotOutOfRangeAllies_NoStealthedFlagInKillerFrame()
    {
        using var r = new QaRig(5);
        r.SetSkill(r.S[0], SkillIds.StealthRelay);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 14, 10);
        r.Place(r.S[1], 17, 10);       // 3 m
        r.Place(r.S[2], 25, 10);       // 11 m
        r.Place(r.S[3], 19, 10);       // exactly 5 m
        r.Place(r.S[4], 8.7f, 10);     // 5.3 m
        void MoveAll() { foreach (var s in r.S) s.Move(0, -1); }
        for (int i = 0; i < 3; i++) { MoveAll(); r.Tick(); }
        foreach (var s in r.S) Assert.True(Seen(r, s));
        r.Use(r.S[0], SkillIds.StealthRelay);
        var hidden = new int[5];
        for (int i = 0; i < 60; i++)
        {
            MoveAll();
            r.Tick();
            var kw = r.KW;
            foreach (var p in kw.Players) Assert.Equal(0, p.Flags & ~(byte)(PlayerFlags.Signal | PlayerFlags.Afterimage));
            for (int s = 0; s < 5; s++) if (!Seen(r, r.S[s])) hidden[s]++;
        }
        Assert.InRange(hidden[0], 39, 41);
        Assert.InRange(hidden[1], 39, 41);
        Assert.InRange(hidden[3], 39, 41);
        Assert.Equal(0, hidden[2]);
        Assert.Equal(0, hidden[4]);
        Assert.Empty(Effects(r.K));
        var e = Effects(r.S[1]).Single();
        Assert.Equal(SkillIds.StealthRelay, e.SkillId);
        Assert.Equal(2f, e.Duration);
    }

    [Fact]
    public void StealthRelay_AlsoHidesHacking_ButNotForcedExposure_AndLaggingStaysVisible()
    {
        using var r = new QaRig(3);
        r.SetSkill(r.S[0], SkillIds.StealthRelay);
        r.SetSkill(r.S[1], SkillIds.TrafficOverload);
        r.Place(r.S[0], 10, 25.6f);
        r.Place(r.S[1], 10.8f, 25.6f);
        r.Place(r.S[2], 11.6f, 25.6f);
        r.Interact(r.S[0], true);
        r.Interact(r.S[1], true);
        r.Tick(4);
        Assert.True(Seen(r, r.S[0]));
        r.Use(r.S[0], SkillIds.StealthRelay);
        r.Tick();
        Assert.False(Seen(r, r.S[0]));
        Assert.False(Seen(r, r.S[1]));
        int before = r.SW.Terminals[0].Progress;
        r.Tick(10);
        Assert.False(Seen(r, r.S[0]));
        Assert.True(r.SW.Terminals[0].Progress > before, "hacking continues while hidden");
        r.Use(r.S[1], SkillIds.TrafficOverload); // forced exposure beats Stealth
        r.Tick();
        Assert.True(Seen(r, r.S[1]));
        Assert.False(Seen(r, r.S[0]));
        // Capture S0 while it is still stealthed: Lagging is always Signal.
        r.Place(r.K, 9.0f, 25.6f);
        r.Tick();
        Assert.True(Seen(r, r.S[0]));
        Assert.True(r.KillerSees(r.S[0])!.Value.Status == (byte)PlayerStatus.Lagging);
    }

    // ------------------------------------------------------------------ Traffic Overload

    [Fact]
    public void TrafficOverload_ExposesCasterTwoSecondsEvenIfStill_HalvesKillerSpeedTwoSeconds_NotifiesKiller()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.S[0], SkillIds.TrafficOverload);
        r.Place(r.K, 3, 5);
        r.Place(r.S[0], 30, 30);
        r.Place(r.S[1], 40, 40);
        r.Tick(3);
        Assert.False(Seen(r, r.S[0]));
        float b = GameConstants.KillerSpeed / GameConstants.TickRate;
        r.Use(r.S[0], SkillIds.TrafficOverload);
        var steps = new List<float>();
        float x = KillerX(r);
        int exposed = 0;
        for (int i = 0; i < 60; i++)
        {
            r.K.Move(1, 0);
            r.Tick();
            float nx = KillerX(r);
            steps.Add(nx - x);
            x = nx;
            if (Seen(r, r.S[0])) { exposed++; Assert.Equal((byte)PlayerFlags.Signal, r.KillerSees(r.S[0])!.Value.Flags); }
            Assert.False(Seen(r, r.S[1]));
        }
        Assert.InRange(exposed, 39, 41);
        int slowed = steps.Count(s => Math.Abs(s - b * 0.5f) < b * 0.01f);
        int normal = steps.Count(s => Math.Abs(s - b) < b * 0.01f);
        Assert.InRange(slowed, 39, 41);
        Assert.Equal(60, slowed + normal + steps.Count(s => s > b * 0.51f && s < b * 0.99f) /* transitional none expected */);
        var ke = Assert.Single(Effects(r.K));
        Assert.Equal(0, ke.CasterId); // U2c: a killer-bound packet never carries a survivor Id
        Assert.Equal(SkillIds.TrafficOverload, ke.SkillId);
        Assert.Equal(2f, ke.Duration);
        Assert.True(ke.X == 0 && ke.Z == 0, "killer-bound Traffic Overload effect must not carry the caster position");
        var se = Effects(r.S[1]).Single();
        Assert.Equal(30f, se.X);
        Assert.Equal(30f, se.Z);
    }

    // ------------------------------------------------------------------ Emergency Restore

    [Fact]
    public void EmergencyRestore_NeedsALaggingAllyWithin1_5m_InstantRescue_NoEffectToKiller_ThenFifteenSecondCooldown()
    {
        using var r = new QaRig(3);
        r.SetSkill(r.S[0], SkillIds.EmergencyRestore);
        r.Place(r.S[0], 10, 10);
        r.Place(r.S[1], 11.6f, 10); // 1.6 m
        r.MakeLagging(r.S[1]);
        r.Place(r.S[2], 40, 50);
        r.Tick();
        r.Use(r.S[0], SkillIds.EmergencyRestore);
        r.Tick();
        Assert.True(r.Truth(r.S[1]).Status == (byte)PlayerStatus.Lagging);
        Assert.Empty(Effects(r.S[0]));
        r.Place(r.S[1], 11.5f, 10); // exactly 1.5 m: no cooldown was consumed by the failed attempt
        r.Tick();
        r.Use(r.S[0], SkillIds.EmergencyRestore);
        r.Tick();
        var t = r.Truth(r.S[1]);
        Assert.Equal((byte)PlayerStatus.Normal, t.Status);
        Assert.Equal(1, t.CaptureCount);
        Assert.Equal(0f, t.StatusTimer);
        Assert.Empty(Effects(r.K));
        Assert.Single(Effects(r.S[0]));
        // 2 s of invulnerability even with the killer on top of the survivor.
        r.Place(r.K, 12.5f, 10);
        r.Tick(30);
        Assert.Equal((byte)PlayerStatus.Normal, r.Truth(r.S[1]).Status);
        // Cooldown 15 s.
        using var c = new QaRig(2);
        c.SetSkill(c.S[0], SkillIds.EmergencyRestore);
        c.Place(c.S[0], 10, 10);
        c.Place(c.S[1], 11, 10);
        c.MakeLagging(c.S[1]);
        c.Tick();
        c.Use(c.S[0], SkillIds.EmergencyRestore);
        c.Tick();
        int c0 = c.Ticks;
        Assert.Equal((byte)PlayerStatus.Normal, c.Truth(c.S[1]).Status);
        int casts = Effects(c.S[0]).Count;
        int gap = -1;
        for (int i = 0; i < 400 && gap < 0; i++)
        {
            c.MakeLagging(c.S[1]); // keep the ally Lagging (contact rescue would otherwise finish it in 3 s)
            c.Use(c.S[0], SkillIds.EmergencyRestore);
            c.Tick();
            if (Effects(c.S[0]).Count > casts) gap = c.Ticks - c0;
        }
        Assert.InRange(gap, 299, 301);
    }

    [Fact]
    public void EmergencyRestore_RestoresOnlyTheNearestLaggingAlly()
    {
        using var r = new QaRig(3);
        r.SetSkill(r.S[0], SkillIds.EmergencyRestore);
        r.Place(r.S[0], 10, 10);
        r.Place(r.S[1], 11.4f, 10);
        r.Place(r.S[2], 10, 11.0f);
        r.MakeLagging(r.S[1]);
        r.MakeLagging(r.S[2]);
        r.Tick();
        r.Use(r.S[0], SkillIds.EmergencyRestore);
        r.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, r.Truth(r.S[1]).Status);
        Assert.Equal((byte)PlayerStatus.Normal, r.Truth(r.S[2]).Status);
    }

    // ------------------------------------------------------------------ White Noise

    [Fact]
    public void WhiteNoise_FiveAnonymousFakesOnWalkablePoints_StaggeredOverHalfASecond_AllGoneAtOnePointFiveSeconds()
    {
        // U2c migration (was: five fakes with distinct borrowed Ids, stable positions, shown together). Kept: five fakes
        // for 1.5 s on walkable points, a plain Signal look, the effect only for survivors. Changed: no Ids, the five
        // appear staggered over 0-0.5 s, and they walk like survivors instead of standing still.
        using var r = new QaRig(6);
        r.SetSkill(r.S[0], SkillIds.WhiteNoise);
        r.Place(r.K, 5, 5);
        for (int i = 0; i < 6; i++) r.Place(r.S[i], 40 + i * 2, 55);
        r.Tick(3);
        Assert.Single(r.KW.Players);
        var map = new FixedMap();
        r.Use(r.S[0], SkillIds.WhiteNoise);
        r.Tick();
        int maxCount = 0, firstVisible = -1, lastVisible = -1;
        for (int frame = 0; frame < 60; frame++)
        {
            var now = r.KillerSurvivorEntries.ToList();
            Assert.InRange(now.Count, 0, 5);
            maxCount = Math.Max(maxCount, now.Count);
            if (now.Count > 0) { if (firstVisible < 0) firstVisible = frame; lastVisible = frame; }
            foreach (var p in now)
            {
                Assert.Equal(0, p.Id);
                Assert.Equal((byte)Role.Survivor, p.Role);
                Assert.Equal((byte)PlayerStatus.Normal, p.Status);
                Assert.Equal((byte)PlayerFlags.Signal, p.Flags);
                Assert.Equal(0f, p.StatusTimer);
                Assert.Equal(0, p.CaptureCount);
                Assert.True(map.CanStand(p.X, p.Z), $"decoy inside a wall at ({p.X},{p.Z})");
                Assert.InRange(p.X, 0f, FixedMap.Size);
                Assert.InRange(p.Z, 0f, FixedMap.Size);
            }
            r.Tick();
        }
        Assert.Equal(5, maxCount);                  // all five are shown at once (staggered, not fewer)
        Assert.InRange(firstVisible, 0, 11);        // the stagger is at most 0.5 s
        Assert.InRange(lastVisible, 28, 30);        // everything is gone 1.5 s after the cast
        Assert.Empty(Effects(r.K));
        var e = Effects(r.S[3]).Single();
        Assert.Equal(SkillIds.WhiteNoise, e.SkillId);
        Assert.Equal(1.5f, e.Duration);
    }

    [Fact]
    public void WhiteNoise_ShowsFiveFakesEvenInAOneVersusOne_NoIdsToRunOutOf_NeverMoreThanTheCap()
    {
        // U2c migration of "with fewer absent Ids than points": fakes no longer borrow Ids, so the count does not depend on
        // how many survivors are absent (the old rule leaked the number of hidden survivors).
        using var r = new QaRig(1);
        r.SetSkill(r.S[0], SkillIds.WhiteNoise);
        r.Place(r.S[0], 40, 55);
        r.Tick(3);
        r.Use(r.S[0], SkillIds.WhiteNoise);
        int max = 0;
        for (int i = 0; i < 12; i++)
        {
            r.Tick();
            max = Math.Max(max, r.KillerSurvivorEntries.Count());
            Assert.True(r.KW.Players.Count <= 16);
        }
        Assert.Equal(5, max);
        // A survivor that becomes visible adds exactly one real entry.
        using var q = new QaRig(2);
        q.SetSkill(q.S[0], SkillIds.WhiteNoise);
        q.Place(q.S[0], 40, 55);
        q.Place(q.S[1], 44, 55);
        q.Tick(3);
        q.Use(q.S[0], SkillIds.WhiteNoise);
        q.Tick(2);
        q.S[1].Move(1, 0);
        q.Tick();
        Assert.NotNull(q.KillerSees(q.S[1]));
        Assert.Equal(1, q.KillerSurvivorEntries.Count(p => RigDist(p.X, p.Z, q.S[1].Self().X, q.S[1].Self().Z) < 0.001f));
        Assert.All(q.KW.Players.Where(p => p.Role == (byte)Role.Survivor), p => Assert.Equal(0, p.Id));
        // Cooldown 15 s.
        using var c = new QaRig(2);
        c.SetSkill(c.S[0], SkillIds.WhiteNoise);
        Assert.InRange(CastGap(c, c.S[0], SkillIds.WhiteNoise), 299, 301);
    }
    // ------------------------------------------------------------------ invalid uses / cooldown consumption

    [Fact]
    public void InvalidUses_WrongIdPassiveIdOutOfRangeIdAreIgnored_AndDoNotConsumeTheCooldown()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.Overclock);
        foreach (byte bad in new byte[] { 0, 2, 3, 4, 5, 100, 200, 255 })
        {
            r.Use(r.K, bad);
            r.Tick();
        }
        Assert.Empty(Effects(r.K));
        r.Use(r.K, SkillIds.Overclock);
        r.Tick();
        Assert.Single(Effects(r.K)); // first valid use is accepted immediately

        r.SetSkill(r.S[0], SkillIds.DummyPacket);
        foreach (byte bad in new byte[] { 1, 2, 3, 4, 5, 200, 255 })
        {
            r.Use(r.S[0], bad);
            r.Tick();
        }
        Assert.Empty(Effects(r.S[0]));
        r.Use(r.S[0], SkillIds.DummyPacket);
        r.Use(r.S[0], SkillIds.DummyPacket); // same tick, twice: only one cast
        r.Tick();
        Assert.Single(Effects(r.S[0]));
    }

    [Fact]
    public void InvalidUses_LaggingSurvivorCannotCast_AndTheAttemptConsumesNoCooldown()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.S[0], SkillIds.WhiteNoise);
        r.Place(r.S[0], 30, 30);
        r.Place(r.S[1], 50, 50);
        r.MakeLagging(r.S[0]);
        for (int i = 0; i < 5; i++) { r.Use(r.S[0], SkillIds.WhiteNoise); r.Tick(); }
        Assert.Empty(Effects(r.S[0]));
        Assert.Single(r.KW.Players, p => p.Id != r.K.Id); // only the Lagging survivor itself, no decoys (S1 is silent)
        Spy.Set(r.P(r.S[0]), "Status", (byte)PlayerStatus.Normal);
        r.Use(r.S[0], SkillIds.WhiteNoise);
        r.Tick();
        Assert.Single(Effects(r.S[0]));
    }

    [Fact]
    public void InvalidUses_SkillOutsideAMatchIsIgnored_NoThrow()
    {
        using var d = new DirectRig(start: false);
        d.K.Push(new C2S_UseSkill { SkillId = 0 });
        d.S.Push(new C2S_UseSkill { SkillId = 3 });
        d.Tick(5);
        Assert.Empty(d.K.All<S2C_SkillEffect>());
        Assert.Empty(d.S.All<S2C_SkillEffect>());
        Assert.False(d.K.IsClosed || d.S.IsClosed);
    }

    // ------------------------------------------------------------------ effect routing matrix (brief rule 10)

    [Theory]
    [InlineData(true, SkillIds.PingScanner)]
    [InlineData(true, SkillIds.Overclock)]
    [InlineData(true, SkillIds.FirewallBlock)]
    [InlineData(false, SkillIds.DummyPacket)]
    [InlineData(false, SkillIds.StealthRelay)]
    [InlineData(false, SkillIds.TrafficOverload)]
    [InlineData(false, SkillIds.EmergencyRestore)]
    [InlineData(false, SkillIds.WhiteNoise)]
    public void EffectRouting_MatchesRule10_AndNoSurvivorBoundEffectCarriesKillerCoordinates(bool killerSkill, byte skill)
    {
        using var r = new QaRig(3);
        var map = new FixedMap();
        r.Place(r.K, 9.37f, 10.21f);
        r.Place(r.S[0], 14, 10);
        r.Place(r.S[1], 30, 30);
        r.Place(r.S[2], 30, 35);
        if (killerSkill) r.SetSkill(r.K, skill); else r.SetSkill(r.S[0], skill);
        if (skill == SkillIds.FirewallBlock && killerSkill) r.OpenExitFlag();
        if (skill == SkillIds.EmergencyRestore && !killerSkill)
        {
            r.Place(r.S[1], 15, 10);
            r.MakeLagging(r.S[1]);
        }
        r.Tick(2);
        r.Use(killerSkill ? r.K : r.S[0], skill);
        r.Tick();
        var k = Effects(r.K);
        var s = r.S.Select(Effects).ToArray();
        if (killerSkill)
        {
            Assert.Single(k);
            switch (skill)
            {
                case SkillIds.PingScanner:
                    Assert.Single(s[0]);
                    Assert.Empty(s[1]);
                    Assert.Empty(s[2]);
                    break;
                case SkillIds.Overclock:
                    Assert.All(s, x => Assert.Empty(x));
                    break;
                case SkillIds.FirewallBlock:
                    Assert.All(s, x => Assert.Single(x));
                    break;
            }
            foreach (var e in s.SelectMany(x => x))
            {
                bool zero = e.X == 0 && e.Z == 0;
                bool exit = e.X == map.Exit.X && e.Z == map.Exit.Z;
                Assert.True(zero || exit, $"survivor-bound killer effect carries ({e.X},{e.Z})");
                Assert.NotEqual(9.37f, e.X);
                Assert.NotEqual(10.21f, e.Z);
            }
        }
        else
        {
            if (skill == SkillIds.TrafficOverload) Assert.Single(k); else Assert.Empty(k);
            Assert.Single(s[0]); // the caster always hears about its own skill
        }
    }
}
