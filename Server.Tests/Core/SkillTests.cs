using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class SkillTests
{
    private static void Use(Peer peer, byte skill) => peer.Send(new C2S_UseSkill { SkillId = skill });

    private static void AssertEffect(S2C_SkillEffect effect, int caster, byte skill, float x, float z, float duration)
    {
        Assert.Equal(caster, effect.CasterId);
        Assert.Equal(skill, effect.SkillId);
        Assert.Equal(x, effect.X);
        Assert.Equal(z, effect.Z);
        Assert.Equal(duration, effect.Duration);
    }

    private static int CountWhile(RulesMatch m, Func<bool> condition, int limit = 1000)
    {
        int frames = 0;
        while (condition() && frames < limit) { m.Tick(); ++frames; }
        return frames;
    }

    [Fact]
    public void PingScannerExposesTheNearestSurvivorForOnePointFiveSecondsWithATwelveSecondCooldown()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner);
        m.Tick(25);
        // Wrong ids are ignored without consuming the cooldown.
        Use(m.Killer, SkillIds.Overclock);
        Use(m.Killer, 200);
        m.Tick();
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        Use(m.Killer, SkillIds.PingScanner);
        m.Tick();
        // Survivor 0 at (5,26) is 21 m away, survivor 1 at (10,26) is 21.6 m away.
        var seen = m.KillerSees(m[0]);
        Assert.NotNull(seen);
        Assert.Equal((byte)PlayerFlags.Signal, seen.Value.Flags);
        Assert.Null(m.KillerSees(m[1]));
        AssertEffect(Assert.Single(m.Killer.All<S2C_SkillEffect>()), m.Killer.Id, SkillIds.PingScanner, 5, 5, 1.5f);
        // The exposed survivor learns about it, without the killer's coordinates; nobody else does.
        AssertEffect(Assert.Single(m[0].All<S2C_SkillEffect>()), m.Killer.Id, SkillIds.PingScanner, 0, 0, 1.5f);
        Assert.Empty(m[1].All<S2C_SkillEffect>());
        Assert.Equal((byte)PlayerFlags.Signal, m.Truth(m[0]).Flags);
        int frames = CountWhile(m, () => m.KillerSees(m[0]) != null); // frames 0..29 visible
        Assert.Equal(30, frames);
        // Cooldown: 12 s after the cast frame.
        m.Tick(238 - frames);
        Use(m.Killer, SkillIds.PingScanner);
        m.Tick(); // frame 239
        Assert.Single(m.Killer.All<S2C_SkillEffect>());
        Use(m.Killer, SkillIds.PingScanner);
        m.Tick(); // frame 240
        Assert.Equal(2, m.Killer.All<S2C_SkillEffect>().Count);
    }

    [Fact]
    public void OverclockGivesOnePointFiveSpeedForThreeSecondsThenOneSecondStandstill()
    {
        using var m = RulesMatch.Start(1, SkillIds.Overclock);
        m.F.Server.PlaceForTests(m.Killer.Id, 3, 2); // the open south edge street: 25 m of straight run
        m.Tick();
        float step = GameConstants.KillerSpeed / GameConstants.TickRate;
        var steps = new List<float>();
        float x = m.Killer.Self().X;
        Use(m.Killer, SkillIds.Overclock);
        for (int i = 0; i < 82; i++)
        {
            m.Killer.Move(1, 0);
            if (i == 10) Use(m.Killer, SkillIds.Overclock); // on cooldown: ignored
            m.Tick();
            float now = m.Killer.Self().X;
            steps.Add(now - x);
            x = now;
        }
        Assert.InRange(steps[0], step * 0.999f, step * 1.001f); // the cast executes after this tick's movement
        for (int i = 1; i <= 60; i++) Assert.InRange(steps[i], step * 1.499f, step * 1.501f);
        for (int i = 61; i <= 80; i++) Assert.Equal(0f, steps[i]);
        Assert.InRange(steps[81], step * 0.999f, step * 1.001f);
        var effect = Assert.Single(m.Killer.All<S2C_SkillEffect>());
        Assert.Equal(SkillIds.Overclock, effect.SkillId);
        Assert.Equal(3f, effect.Duration);
        Assert.Empty(m[0].All<S2C_SkillEffect>());
    }

    [Fact]
    public void PacketAfterimageIsPassiveAndKeepsTheLastSeenPositionForOneSecond()
    {
        using var m = RulesMatch.Start(2, SkillIds.PacketAfterimage);
        Use(m.Killer, SkillIds.PacketAfterimage);
        m.Tick(25);
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        m.WalkTo(m[0], 5, 27, GameConstants.SurvivorSpeed);
        var last = m.KillerSees(m[0])!.Value;
        Assert.Equal((byte)PlayerFlags.Signal, last.Flags);
        int signal = CountWhile(m, () => m.KillerSees(m[0]) != null && !m.KillerSeesAfterimage(m[0]));
        Assert.InRange(signal, 1, 3); // the 0.15 s input hold
        int afterimage = 0;
        while (m.KillerSees(m[0]) is { } entry && afterimage < 100)
        {
            // The killer frame no longer marks afterimages: the entry looks exactly like a live Signal.
            Assert.True(m.KillerSeesAfterimage(m[0]));
            Assert.Equal((byte)PlayerFlags.Signal, entry.Flags);
            Assert.Equal(0, entry.Id);
            Assert.Equal(last.X, entry.X);
            Assert.Equal(last.Z, entry.Z);
            m.Tick();
            ++afterimage;
        }
        Assert.Equal(20, afterimage);
        Assert.Equal(0, m.Truth(m[0]).Flags & (byte)PlayerFlags.Afterimage); // a killer-only detail
        Assert.Null(m.KillerSees(m[1]));
    }

    [Fact]
    public void DummyPacketShowsAWalkingFakeSignalFromTheCastPositionForThreeSecondsEvenWhileTheCasterIsSignal()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.DummyPacket);
        var a = m[0];
        m.Tick(25);
        Use(a, SkillIds.DummyPacket);
        m.Tick(); // frame 0
        var decoy = Assert.Single(m.KillerPhantoms());
        Assert.Equal(5f, decoy.X);
        Assert.Equal(26f, decoy.Z);
        Assert.Null(m.KillerSees(a));
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        foreach (var survivor in m.Survivors)
            AssertEffect(Assert.Single(survivor.All<S2C_SkillEffect>()), a.Id, SkillIds.DummyPacket, 5, 26, 3);
        // The caster walking (Signal) and the dummy are two separate anonymous entries.
        var previous = decoy;
        for (int frame = 1; frame <= 10; frame++)
        {
            a.Move(0, 1);
            m.Tick();
            Assert.NotNull(m.KillerSees(a));
            var now = Assert.Single(m.KillerPhantoms());
            Assert.InRange(RulesMatch.Distance(now, previous.X, previous.Z), 0, GameConstants.SurvivorSpeed / GameConstants.TickRate + 1e-4f);
            previous = now;
        }
        a.Move(0, 0);
        // (5,26) is 5.1 m from terminal 0 at (10,25): the dummy walks there and stands where a survivor bot hacks, 1.4 m
        // off the centre (U2d re-target: SurvivorBrain.HackStopDistance, was 1 m), pausing Silently like a bot when its
        // 2-5 s burst ends. It never outlives the 3 s skill.
        bool arrived = false;
        for (int frame = 11; frame < 60; frame++)
        {
            m.Tick();
            if (m.KillerPhantoms() is not { Count: 1 } shown) { Assert.Empty(m.KillerPhantoms()); continue; }
            if (RulesMatch.Distance(shown[0], 10, 25) <= 1.45f) { arrived = true; Assert.True(RulesMatch.Distance(shown[0], 10, 25) >= 1.15f); }
        }
        Assert.True(arrived);
        m.Tick(); // frame 60: cast + 3 s
        Assert.Empty(m.KillerPhantoms());
        // Cooldown 15 s.
        Use(a, SkillIds.DummyPacket);
        m.Tick();
        Assert.Empty(m.KillerPhantoms());
        Assert.Single(a.All<S2C_SkillEffect>());
    }

    [Fact]
    public void StealthRelayHidesNearbyAlliesForTwoSecondsButNotForcedExposureOrLagging()
    {
        using var m = RulesMatch.Start(3, SkillIds.PingScanner, SkillIds.StealthRelay);
        var (a, b, c) = (m[0], m[1], m[2]); // (5,26), (10,26) is exactly 5 m away, (5,36) is 10 m away
        m.Tick(25);
        Use(a, SkillIds.StealthRelay);
        m.Tick(); // frame 0
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        AssertEffect(Assert.Single(c.All<S2C_SkillEffect>()), a.Id, SkillIds.StealthRelay, 5, 26, 2);
        int hidden = 0;
        for (int frame = 1; frame <= 45; frame++)
        {
            foreach (var peer in m.Survivors) peer.Move(1, 0);
            if (frame == 10) Use(m.Killer, SkillIds.PingScanner); // forced exposure overrides Stealth
            m.Tick();
            var truthA = m.Truth(a);
            Assert.NotNull(m.KillerSees(c));
            Assert.Equal(0, m.Truth(c).Flags & (byte)PlayerFlags.Stealthed);
            if (frame < 40)
            {
                Assert.Equal((byte)PlayerFlags.Stealthed, m.Truth(b).Flags);
                Assert.Null(m.KillerSees(b));
                bool exposed = frame >= 10 && frame < 40;
                Assert.Equal(exposed, m.KillerSees(a) != null);
                Assert.Equal(exposed ? (byte)(PlayerFlags.Signal | PlayerFlags.Stealthed) : (byte)PlayerFlags.Stealthed, truthA.Flags);
                ++hidden;
            }
            else
            {
                Assert.NotNull(m.KillerSees(b));
                Assert.Equal((byte)PlayerFlags.Signal, m.Truth(b).Flags);
            }
        }
        Assert.Equal(39, hidden);
    }

    [Fact]
    public void StealthRelayDoesNotHideALaggingSurvivor()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.StealthRelay);
        m.WalkTo(m.Killer, 5, 24.4f, GameConstants.KillerSpeed); // 1.6 m short of survivor 0
        m.Tick(3);
        Use(m[0], SkillIds.StealthRelay);
        m.Tick();
        Assert.Null(m.KillerSees(m[0]));
        m.Step(m.Killer, 0, 1);
        m.Step(m.Killer, 0, 1);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.Equal((byte)PlayerFlags.Signal, m.Truth(m[0]).Flags);
        Assert.NotNull(m.KillerSees(m[0]));
    }

    [Fact]
    public void TrafficOverloadExposesTheCasterAndHalvesKillerSpeedMultiplyingWithOverclock()
    {
        using var m = RulesMatch.Start(2, SkillIds.Overclock, SkillIds.TrafficOverload);
        var a = m[0];
        m.Tick(25);
        float step = GameConstants.KillerSpeed / GameConstants.TickRate;
        Use(m.Killer, SkillIds.Overclock);
        Use(a, SkillIds.TrafficOverload);
        m.Killer.Move(1, 0);
        m.Tick(); // frame 0: both skills execute after movement
        Assert.NotNull(m.KillerSees(a));
        // The killer copy carries neither the caster's position nor its Id.
        AssertEffect(m.Killer.All<S2C_SkillEffect>().Single(e => e.SkillId == SkillIds.TrafficOverload),
            0, SkillIds.TrafficOverload, 0, 0, 2);
        foreach (var survivor in m.Survivors)
            AssertEffect(Assert.Single(survivor.All<S2C_SkillEffect>()), a.Id, SkillIds.TrafficOverload, 5, 26, 2);
        float x = m.Killer.Self().X;
        int exposed = 1;
        for (int frame = 1; frame <= 60; frame++)
        {
            m.Killer.Move(1, 0);
            m.Tick();
            float now = m.Killer.Self().X;
            float expected = frame <= 40 ? step * 1.5f * 0.5f : step * 1.5f;
            Assert.InRange(now - x, expected * 0.999f, expected * 1.001f);
            x = now;
            if (m.KillerSees(a) != null) ++exposed;
        }
        Assert.Equal(40, exposed); // still, yet Signal for frames [cast, cast + 2 s)
        Assert.Equal((byte)PlayerFlags.None, m.Truth(a).Flags);
    }

    [Fact]
    public void EmergencyRestoreInstantlyRescuesALaggingAllyWithinOnePointFiveMetres()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.EmergencyRestore);
        var (a, b) = (m[0], m[1]);
        Use(a, SkillIds.EmergencyRestore); // no Lagging ally: ignored, no cooldown
        m.Tick();
        Assert.Empty(a.All<S2C_SkillEffect>());
        m.WalkTo(m.Killer, 10, 25, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(b).Status);
        m.WalkTo(a, 8.6f, 26, GameConstants.SurvivorSpeed);
        Use(a, SkillIds.EmergencyRestore);
        m.Tick();
        var restored = m.Truth(b);
        Assert.Equal((byte)PlayerStatus.Normal, restored.Status);
        Assert.Equal(1, restored.CaptureCount);
        foreach (var survivor in m.Survivors)
            AssertEffect(Assert.Single(survivor.All<S2C_SkillEffect>()), a.Id, SkillIds.EmergencyRestore, 10, 26, 0);
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        // Same result as a 3 s rescue: two seconds of protection from the killer standing 1 m away, then the second
        // capture (GDD v2 §4: Lagging again with the 30 s timer).
        m.Tick(39);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(b).Status);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(b).Status);
        Assert.Equal(2, m.Truth(b).CaptureCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void WhiteNoiseShowsFiveAnonymousFakeSignalsStaggeredOverHalfASecondUntilOnePointFiveSeconds(int survivors)
    {
        using var m = RulesMatch.Start(survivors, SkillIds.PingScanner, SkillIds.WhiteNoise);
        var map = new FixedMap();
        m.Tick(25);
        Use(m[0], SkillIds.WhiteNoise);
        m.Tick(); // frame 0
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        foreach (var survivor in m.Survivors)
            AssertEffect(Assert.Single(survivor.All<S2C_SkillEffect>()), m[0].Id, SkillIds.WhiteNoise, 5, 26, 1.5f);
        var counts = new List<int>();
        for (int frame = 0; frame < 30; frame++)
        {
            if (frame > 0) m.Tick();
            var world = m.Killer.Last<S2C_WorldState>();
            // Every survivor is Silent: all survivor-shaped entries are the noise points.
            var entries = world.Players.Where(p => p.Role == (byte)Role.Survivor).ToList();
            Assert.Equal(entries.Count, m.KillerPhantoms().Count);
            foreach (var decoy in entries)
            {
                Assert.True(map.CanStand(decoy.X, decoy.Z));
                Assert.Equal((byte)PlayerFlags.Signal, decoy.Flags);
                Assert.Equal(0, decoy.Id);
                Assert.Equal(0, decoy.Yaw);
                Assert.Equal(0, decoy.CaptureCount);
            }
            counts.Add(entries.Count);
        }
        // Staggered spawns over at most 0.5 s (frames 0..10), then all five until the shared expiry.
        Assert.All(counts.Skip(10), c => Assert.Equal(5, c));
        Assert.True(counts.Zip(counts.Skip(1)).All(p => p.Second >= p.First));
        m.Tick(); // frame 30: cast + 1.5 s
        Assert.DoesNotContain(m.Killer.Last<S2C_WorldState>().Players, p => p.Role == (byte)Role.Survivor);
    }

    [Fact]
    public void InvalidUsesAreIgnoredAndNeverConsumeTheCooldown()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.DummyPacket);
        var (a, b) = (m[0], m[1]);
        Use(a, SkillIds.WhiteNoise); // a valid survivor id, but not this survivor's skill
        Use(a, 200);
        m.Tick();
        Assert.Empty(a.All<S2C_SkillEffect>());
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(a).Status);
        Use(a, SkillIds.DummyPacket); // Lagging cannot use skills
        m.Tick();
        Assert.Empty(a.All<S2C_SkillEffect>());
        m.WalkTo(b, 6.4f, 26, GameConstants.SurvivorSpeed);
        m.Tick(60);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(a).Status);
        Use(a, SkillIds.DummyPacket);
        m.Tick();
        Assert.Single(a.All<S2C_SkillEffect>());
        Use(a, SkillIds.DummyPacket); // on cooldown
        m.Tick();
        Assert.Single(a.All<S2C_SkillEffect>());
    }
}
