using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class DecoyAndLeakTests
{
    private static void AssertAnonymous(PlayerState entry)
    {
        Assert.Equal(0, entry.Id);
        Assert.Equal((byte)Role.Survivor, entry.Role);
        Assert.Equal(0f, entry.Yaw);
        Assert.Equal((byte)PlayerFlags.Signal, entry.Flags);
        Assert.Equal(0f, entry.StatusTimer);
        Assert.Equal(0, entry.CaptureCount);
        Assert.True(float.IsFinite(entry.X) && float.IsFinite(entry.Z));
    }

    [Fact]
    public void DummyAndRealSignalEntriesAreByteForByteTheSameShapeAndCarryNoIdentity()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.DummyPacket);
        var (a, b) = (m[0], m[1]);
        // Survivor 0 is captured and rescued so its CaptureCount is 1; the killer must not learn it.
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed);
        m.WalkTo(b, 6.4f, 26, GameConstants.SurvivorSpeed);
        m.Tick(60);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(a).Status);
        Assert.Equal(1, m.Truth(a).CaptureCount);
        m.WalkTo(a, 5, 30, GameConstants.SurvivorSpeed);
        m.WalkTo(b, 10, 26, GameConstants.SurvivorSpeed);
        m.Tick(5);
        Use(a, SkillIds.DummyPacket);
        a.Move(0, 1); // the caster stays Signal: real entry and dummy side by side
        b.Move(1, 0);
        m.Tick();
        var world = m.Killer.Last<S2C_WorldState>();
        var decoy = Assert.Single(m.KillerPhantoms());
        var realA = m.KillerSees(a)!.Value;
        var realB = m.KillerSees(b)!.Value;
        foreach (var entry in new[] { decoy, realA, realB }) AssertAnonymous(entry);
        var frames = new[] { decoy, realA, realB }.Select(e => { var w = new PacketWriter(); e.Write(w); return w.WrittenSpan.ToArray(); }).ToList();
        // Apart from the coordinates (bytes 6..13) the three entries are identical on the wire.
        foreach (var bytes in frames) Assert.Equal(frames[0].Take(6).Concat(frames[0].Skip(14)), bytes.Take(6).Concat(bytes.Skip(14)));
        Assert.Equal(4, world.Players.Count);
        Assert.DoesNotContain(m.Killer.All<S2C_SkillEffect>(), e => e.SkillId == SkillIds.DummyPacket);
    }

    [Fact]
    public void WhiteNoiseNeedsNoAbsentSurvivorsAndNeverDuplicatesOrHidesARealSignal()
    {
        // Killer + two Signal survivors + five points fill the eight WorldState entries exactly.
        using var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.WhiteNoise);
        var (a, b) = (m[0], m[1]);
        m.Tick(25);
        Use(a, SkillIds.WhiteNoise);
        for (int frame = 0; frame < 30; frame++)
        {
            a.Move(0, 1);
            b.Move(1, 0);
            m.Tick();
            var world = m.Killer.Last<S2C_WorldState>();
            // Both survivors are Signal, yet every noise point still shows once spawned (no Id budget any more).
            foreach (var peer in new[] { a, b })
            {
                var seen = m.KillerSees(peer)!.Value;
                Assert.Equal(m.Truth(peer).X, seen.X);
                Assert.Equal(m.Truth(peer).Z, seen.Z);
            }
            if (frame >= 10) Assert.Equal(5, m.KillerPhantoms().Count);
            Assert.All(world.Players.Where(p => p.Role == (byte)Role.Survivor), AssertAnonymous);
        }
        Assert.DoesNotContain(m.Killer.All<S2C_SkillEffect>(), e => e.SkillId == SkillIds.WhiteNoise);
    }
    private static void Use(Peer peer, byte skill) => peer.Send(new C2S_UseSkill { SkillId = skill });

    private sealed class SweepStats
    {
        internal int KillerFrames, SurvivorFrames, AbsentChecks, DecoyEntries, AfterimageEntries, Coincidences;
        // U2e: survivor frames with the legal line-of-sight killer entry and frames where it is blocked.
        internal int SightFrames, BlockedFrames;
        internal readonly HashSet<byte> KillerSkills = new(), SurvivorEffects = new(), KillerEffects = new();
    }

    // Seeded 3-minute matches: a scripted human killer that chases what it sees, a scripted human survivor,
    // and six survivor bots. Every frame is checked against the true positions of the same tick.
    [Fact]
    public void LeakSweep_BlockedSurvivorFramesCarryNoKillerCoordinatesAndNoKillerFrameCarriesAbsentSurvivors()
    {
        var stats = new SweepStats();
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 }) Sweep(seed, stats);
        Assert.True(stats.KillerFrames > 20000 && stats.SurvivorFrames > 20000, $"{stats.KillerFrames}/{stats.SurvivorFrames}");
        Assert.True(stats.AbsentChecks > 20000, "absent checks " + stats.AbsentChecks);
        Assert.True(stats.DecoyEntries > 100, "decoy entries " + stats.DecoyEntries);
        Assert.True(stats.AfterimageEntries > 20, "afterimage entries " + stats.AfterimageEntries);
        Assert.True(stats.Coincidences * 20 < stats.SurvivorFrames, "coincidences " + stats.Coincidences);
        Assert.True(stats.BlockedFrames > 5000 && stats.SightFrames > 1000, $"blocked {stats.BlockedFrames} / sight {stats.SightFrames}");
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, stats.KillerSkills.OrderBy(x => x));
        Assert.Superset(new HashSet<byte> { SkillIds.DummyPacket, SkillIds.StealthRelay, SkillIds.TrafficOverload, SkillIds.WhiteNoise },
            stats.SurvivorEffects);
        Assert.Superset(new HashSet<byte> { SkillIds.PingScanner, SkillIds.Overclock, SkillIds.TrafficOverload }, stats.KillerEffects);
    }

    private static void Sweep(int seed, SweepStats stats)
    {
        using var f = new CoreFixture(seed: seed);
        var killer = f.Join("K");
        var survivor = f.Join("S");
        int killerId = killer.Id, survivorId = survivor.Id;
        killer.Send(new C2S_SetBots { Count = 6 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        var rng = new Random(seed);
        var terminals = new[] { (10f, 25f), (28f, 12f), (49f, 25f), (12f, 49f), (30f, 52f), (50f, 42f) };
        int survivorTarget = rng.Next(terminals.Length), killerTarget = rng.Next(terminals.Length), restUntil = 0;
        var dummies = new List<(float X, float Z, int Until)>();
        bool started = false;
        byte killerSkill = 0, survivorSkill = 0;
        for (int tick = 0; tick < 3600; tick++)
        {
            if (!started)
            {
                killer.Send(new C2S_StartMatch());
                killer.Drain();
                survivor.Drain();
                killer.Packets.Clear();
                survivor.Packets.Clear();
                f.Tick();
                killerSkill = killer.Last<S2C_MatchStart>().SkillId;
                survivorSkill = survivor.Last<S2C_MatchStart>().SkillId;
                stats.KillerSkills.Add(killerSkill);
                dummies.Clear();
                Check(f, killer, killerId, survivor, survivorId, tick, dummies, stats);
                started = true;
                continue;
            }
            var kWorld = killer.Packets.OfType<S2C_WorldState>().LastOrDefault();
            var sWorld = survivor.Packets.OfType<S2C_WorldState>().LastOrDefault();
            killer.Packets.Clear();
            survivor.Packets.Clear();
            if (kWorld != null && sWorld != null) DriveKiller(killer, kWorld, terminals, ref killerTarget, rng);
            if (sWorld != null) DriveSurvivor(survivor, survivorId, sWorld, terminals, ref survivorTarget, ref restUntil, tick, rng);
            if (tick % 10 == 0) killer.Send(new C2S_UseSkill { SkillId = killerSkill });
            if (tick % 10 == 5) survivor.Send(new C2S_UseSkill { SkillId = survivorSkill });
            f.Tick();
            killer.Drain();
            survivor.Drain();
            Check(f, killer, killerId, survivor, survivorId, tick, dummies, stats);
            if (survivor.Packets.OfType<S2C_GameResult>().Any()) started = false;
        }
    }

    private static void DriveKiller(Peer killer, S2C_WorldState world, (float, float)[] terminals, ref int target, Random rng)
    {
        var self = world.Players.Single(p => p.Role == (byte)Role.Killer);
        float best = float.MaxValue, tx = terminals[target].Item1, tz = terminals[target].Item2;
        foreach (var p in world.Players)
        {
            if (p.Role != (byte)Role.Survivor || p.Status != (byte)PlayerStatus.Normal) continue;
            float d = (p.X - self.X) * (p.X - self.X) + (p.Z - self.Z) * (p.Z - self.Z);
            if (d < best) { best = d; tx = p.X; tz = p.Z; }
        }
        if (best == float.MaxValue && MathF.Abs(tx - self.X) + MathF.Abs(tz - self.Z) < 1.5f) target = rng.Next(terminals.Length);
        float dx = tx - self.X, dz = tz - self.Z;
        killer.Move(dx + (float)(rng.NextDouble() - 0.5), dz + (float)(rng.NextDouble() - 0.5));
    }

    private static void DriveSurvivor(Peer survivor, int survivorId, S2C_WorldState world, (float, float)[] terminals, ref int target,
        ref int restUntil, int tick, Random rng)
    {
        var self = world.Players.Single(p => p.Id == survivorId);
        if (tick < restUntil) { survivor.Move(0, 0); return; }
        float dx = terminals[target].Item1 - self.X, dz = terminals[target].Item2 - self.Z;
        if (dx * dx + dz * dz < 1f)
        {
            survivor.Send(new C2S_Interact { Active = 1 });
            survivor.Move(0, 0);
            restUntil = tick + 20 + rng.Next(80);
            target = rng.Next(terminals.Length);
            return;
        }
        survivor.Send(new C2S_Interact { Active = 0 });
        if (rng.Next(40) == 0) { restUntil = tick + 10 + rng.Next(30); return; }
        survivor.Move(dx + (float)(rng.NextDouble() - 0.5), dz + (float)(rng.NextDouble() - 0.5));
    }

    private static int Count(byte[] haystack, byte[] needle)
    {
        int count = 0;
        for (int i = haystack.AsSpan().IndexOf(needle); i >= 0;)
        {
            ++count;
            int next = haystack.AsSpan(i + 1).IndexOf(needle);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return count;
    }

    private static void Check(CoreFixture f, Peer killer, int killerId, Peer survivor, int survivorId, int tick, List<(float X, float Z, int Until)> dummies, SweepStats stats)
    {
        var kWorld = killer.Packets.OfType<S2C_WorldState>().SingleOrDefault();
        var sWorld = survivor.Packets.OfType<S2C_WorldState>().SingleOrDefault();
        if (kWorld == null || sWorld == null) return;
        Assert.Equal(kWorld.Tick, sWorld.Tick);
        foreach (var effect in survivor.Packets.OfType<S2C_SkillEffect>())
        {
            stats.SurvivorEffects.Add(effect.SkillId);
            if (effect.SkillId == SkillIds.DummyPacket && effect.CasterId != killerId) dummies.Add((effect.X, effect.Z, tick + 60));
        }
        foreach (var effect in killer.Packets.OfType<S2C_SkillEffect>()) stats.KillerEffects.Add(effect.SkillId);
        Assert.DoesNotContain(killer.Packets.OfType<S2C_SkillEffect>(), e => e.CasterId != killerId &&
            (e.SkillId == SkillIds.DummyPacket || e.SkillId == SkillIds.WhiteNoise || e.SkillId == SkillIds.StealthRelay || e.SkillId == SkillIds.EmergencyRestore));
        dummies.RemoveAll(d => d.Until <= tick);

        var killerSelf = kWorld.Players.Single(p => p.Id == killerId);
        Assert.All(kWorld.Players.Where(p => p.Role != (byte)Role.Killer), AssertAnonymous);
        Assert.Equal(sWorld.Players.Count, sWorld.Players.Select(p => p.Id).Distinct().Count());

        // Survivor-bound frames (U2e): the exact killer entry only with line of sight, otherwise none; outside that one
        // legal entry, never the killer's coordinate bytes.
        byte[] kx = BitConverter.GetBytes(killerSelf.X), kz = BitConverter.GetBytes(killerSelf.Z);
        byte[] pair = kx.Concat(kz).ToArray();
        foreach (var packet in survivor.Packets)
        {
            if (packet is S2C_WorldState w)
            {
                if (KillerSight.Verify(w, survivorId, killerSelf, $"tick {tick}")) stats.SightFrames++;
                else stats.BlockedFrames++;
                var rest = KillerSight.WithoutKiller(w);
                var stripped = new S2C_WorldState { Tick = w.Tick, TimeLeft = w.TimeLeft, ThreatLevel = w.ThreatLevel, Players = rest.Players };
                byte[] bytes = PacketCodec.Encode(stripped);
                Assert.Equal(-1, bytes.AsSpan().IndexOf(pair));
                foreach (var (value, needle) in new[] { (killerSelf.X, kx), (killerSelf.Z, kz) })
                {
                    int legit = stripped.Players.Sum(p => (Same(p.X, value) ? 1 : 0) + (Same(p.Z, value) ? 1 : 0) + (Same(p.Yaw, value) ? 1 : 0) +
                        (Same(p.StatusTimer, value) ? 1 : 0)) + (Same(w.TimeLeft, value) ? 1 : 0);
                    if (legit > 0) stats.Coincidences++;
                    Assert.True(Count(bytes, needle) <= legit, $"tick {tick}: killer coordinate {value:R} in a survivor frame");
                }
            }
            else
            {
                byte[] bytes = PacketCodec.Encode(packet);
                Assert.Equal(-1, bytes.AsSpan().IndexOf(pair));
                if (packet is S2C_SkillEffect e)
                    Assert.False(Same(e.X, killerSelf.X) && Same(e.Z, killerSelf.Z), $"tick {tick}: killer position in a SkillEffect");
            }
            stats.SurvivorFrames++;
        }

        // Killer-bound frames: absent (Silent, alive) survivors' coordinates never appear outside decoy placement.
        var survivors = sWorld.Players.Where(p => p.Role == (byte)Role.Survivor).ToList();
        var truth = survivors.ToDictionary(p => p.Id);
        var absent = survivors.Where(p => p.Status == (byte)PlayerStatus.Normal && (p.Flags & (byte)PlayerFlags.Signal) == 0).ToList();
        var leftover = new List<PlayerState>();
        var sources = f.Server.KillerViewSources;
        if (sources.Count == 0 && killer.Packets.OfType<S2C_GameResult>().Any()) return; // final frame, match already reset
        Assert.Equal(sources.Count, kWorld.Players.Count);
        var dummySerials = f.Server.PhantomSnapshot().Where(p => p.IsDummy).Select(p => p.Serial).ToHashSet();
        for (int i = 0; i < kWorld.Players.Count; i++)
        {
            var entry = kWorld.Players[i];
            if (entry.Role == (byte)Role.Killer) { Assert.Equal(killerId, entry.Id); continue; }
            Assert.Equal((byte)Role.Survivor, entry.Role);
            if (sources[i].Phantom)
            {
                stats.DecoyEntries++;
                // A Dummy Packet track starts at the caster's true position (GDD residual) and may keep one of its
                // coordinates while it walks; only White Noise points must never echo an absent survivor.
                if (dummySerials.Contains(sources[i].Label)) continue;
                leftover.Add(entry);
                continue;
            }
            var t = truth[sources[i].Label];
            Assert.NotEqual((byte)PlayerStatus.Escaped, entry.Status);
            Assert.NotEqual((byte)PlayerStatus.Eliminated, entry.Status);
            if ((t.Flags & (byte)PlayerFlags.Signal) != 0 || t.Status == (byte)PlayerStatus.Lagging)
            {
                Assert.True(Same(entry.X, t.X) && Same(entry.Z, t.Z) && entry.Status == t.Status, $"tick {tick}: visible entry {entry.Id} not at its true position");
                continue;
            }
            Assert.True(sources[i].Afterimage, $"tick {tick}: entry {sources[i].Label} shown although neither Signal nor afterimage");
            stats.AfterimageEntries++;
        }
        var sanitized = new S2C_WorldState { Tick = kWorld.Tick, TimeLeft = kWorld.TimeLeft, ExitOpen = kWorld.ExitOpen, Players = leftover };
        byte[] killerBytes = PacketCodec.Encode(sanitized);
        foreach (var s in absent)
        {
            stats.AbsentChecks++;
            // White Noise walks segments between public landmarks, so a point can share an exact landmark coordinate
            // (x = 10 on the spawn-to-spawn segment) with a survivor standing on that landmark; that reveals nothing.
            if (Landmark(s.X) || Landmark(s.Z)) continue;
            Assert.True(killerBytes.AsSpan().IndexOf(BitConverter.GetBytes(s.X)) < 0, $"tick {tick}: absent survivor {s.Id} X {s.X:R} leaked");
            Assert.True(killerBytes.AsSpan().IndexOf(BitConverter.GetBytes(s.Z)) < 0, $"tick {tick}: absent survivor {s.Id} Z {s.Z:R} leaked");
        }
        stats.KillerFrames += killer.Packets.Count;
        foreach (var packet in killer.Packets)
            if (packet is S2C_SkillEffect e)
                foreach (var s in absent)
                    Assert.False(Same(e.X, s.X) && Same(e.Z, s.Z), $"tick {tick}: absent survivor position in a killer SkillEffect");
    }

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static readonly float[] LandmarkCoordinates = LandmarkValues();
    private static float[] LandmarkValues()
    {
        var map = new PingTheKiller.ServerCore.Map.FixedMap();
        return map.TerminalPositions.Concat(map.SpawnPoints).Append(map.Exit).SelectMany(p => new[] { p.X, p.Z }).Distinct().ToArray();
    }
    private static bool Landmark(float value) => LandmarkCoordinates.Any(c => Same(c, value));
}
