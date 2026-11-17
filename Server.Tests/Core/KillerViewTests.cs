using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// One idle human killer (any skill, or a required one) and seven survivor bots, recorded tick by tick with ground truth.
internal sealed class KillerMatchRecorder : IDisposable
{
    internal KillerMatchRecorder(int seed, int killerSkill = -1, int searchLimit = 200)
    {
        for (int s = seed; s < seed + searchLimit; s++)
        {
            F = new CoreFixture(seed: s);
            Killer = F.Join("K");
            Killer.Send(new C2S_SetBots { Count = 7 });
            Killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            Killer.Send(new C2S_StartMatch());
            F.Tick();
            if (killerSkill < 0 || Killer.Last<S2C_MatchStart>().SkillId == killerSkill) { Seed = s; return; }
            F.Dispose();
        }
        throw new InvalidOperationException("no seed assigns the killer skill");
    }
    internal CoreFixture F { get; private set; } = null!;
    internal Peer Killer { get; private set; } = null!;
    internal int Seed { get; }
    public void Dispose() => F.Dispose();

    // Runs to the end of the match (or maxTicks); onTick sees every killer-bound packet of the tick.
    internal void Run(Action<List<IPacket>> onTick, int maxTicks = 12100)
    {
        Killer.Packets.Clear();
        for (int tick = 0; tick < maxTicks; tick++)
        {
            F.Tick();
            Killer.Drain();
            var packets = Killer.Packets.ToList();
            Killer.Packets.Clear();
            onTick(packets);
            if (packets.OfType<S2C_GameResult>().Any()) return;
        }
    }
}

public sealed class KillerViewTests
{
    private const int HeaderBytes = 4 + 2;
    private const int EntryBytes = 25; // v0.7: + Chase

    [Fact]
    public void NoKillerBoundFrameCarriesASurvivorIdAndEntriesAreAnonymous()
    {
        int frames = 0, entries = 0, effects = 0;
        foreach (int seed in new[] { 1, 40, 80 })
        {
            using var r = new KillerMatchRecorder(seed);
            var survivorIds = r.F.Server.PlayerSnapshot().Where(p => p.Role == Role.Survivor).Select(p => p.Id).ToList();
            int killerId = r.Killer.Id;
            r.Run(packets =>
            {
                foreach (var packet in packets)
                {
                    byte[] bytes = PacketCodec.Encode(packet);
                    if (packet is S2C_SkillEffect effect)
                    {
                        Assert.DoesNotContain(effect.CasterId, survivorIds);
                        ++effects;
                    }
                    if (packet is not S2C_WorldState world) continue;
                    ++frames;
                    // v0.4 byte offsets: header, 14 fixed bytes (Tick, TimeLeft, Threat, Required, CollapseLeft), exits
                    // (count + 11 bytes each), HatchVisible (+8 when 1), terminals (count + 11 bytes each), player count.
                    int offset = HeaderBytes + 14 + 1 + 11 * world.Exits.Count + 1 + (world.HatchVisible == 1 ? 8 : 0) +
                        1 + 11 * world.Terminals.Count + 1;
                    for (int i = 0; i < world.Players.Count; i++, offset += EntryBytes)
                    {
                        int id = BitConverter.ToInt32(bytes, offset);
                        byte role = bytes[offset + 4];
                        if (role == (byte)Role.Killer) { Assert.Equal(killerId, id); continue; }
                        ++entries;
                        Assert.Equal(0, id);
                        Assert.Equal(0f, BitConverter.ToSingle(bytes, offset + 14)); // Yaw
                        Assert.Equal((byte)PlayerFlags.Signal, bytes[offset + 18]);
                        Assert.Equal(0f, BitConverter.ToSingle(bytes, offset + 19)); // StatusTimer
                        Assert.Equal(0, bytes[offset + 23]); // CaptureCount
                        Assert.Equal(0, bytes[offset + 24]); // v0.7 Chase: never on an anonymous entry
                    }
                    offset += 1 + world.Shutters.Count; // v0.7 shutter list ends the frame
                    Assert.Equal(bytes.Length, offset);
                    Assert.All(world.Terminals, t => Assert.True(t.Progress == 0 || t.Progress == 100));
                }
            });
        }
        Assert.True(frames > 3000 && entries > 2000, $"{frames} frames, {entries} entries, {effects} effects");
    }

    [Fact]
    public void EntryOrderIsUniformlyShuffledEverySnapshot()
    {
        using var m = RulesMatch.Start(3);
        const int snapshots = 4000;
        var counts = new int[4, 4]; // label (killer, s0, s1, s2) x wire position
        var labels = new[] { m.Killer.Id, m[0].Id, m[1].Id, m[2].Id };
        for (int i = 0; i < snapshots; i++)
        {
            // Keep everybody Signal by walking back and forth in place.
            float dir = (i / 10) % 2 == 0 ? 1 : -1;
            foreach (var peer in m.Survivors) peer.Move(dir, 0);
            m.Tick();
            // Real clients drain their socket; an undrained in-memory peer is closed once its queue overflows.
            foreach (var peer in m.Survivors.Append(m.Killer)) { peer.Drain(); peer.Packets.Clear(); }
            var sources = m.F.Server.KillerViewSources;
            Assert.True(4 == sources.Count, $"snapshot {i}: {sources.Count} entries; truth " +
                string.Join(" ", m.F.Server.PlayerSnapshot().Select(p => $"{p.Id}:{p.Role}:{p.Status}:{p.Visible}")));
            for (int position = 0; position < 4; position++)
                counts[Array.IndexOf(labels, sources[position].Label), position]++;
        }
        double chi = 0, expected = snapshots / 4.0;
        for (int l = 0; l < 4; l++)
            for (int p = 0; p < 4; p++)
                chi += (counts[l, p] - expected) * (counts[l, p] - expected) / expected;
        // 9 degrees of freedom; 27.88 is the 0.1 % critical value. Deterministic for the fixed private seed.
        Assert.True(chi < 27.88, $"chi-square {chi:0.00}");
        // And consecutive snapshots are not just a rotation: the killer's own position changes often.
        Assert.InRange(counts[0, 0], snapshots / 4 - 200, snapshots / 4 + 200);
    }

    private static List<string> PhantomTrace(int privateSeed, int wireSeed)
    {
        using var f = new CoreFixture(seed: privateSeed, matchSeed: wireSeed);
        var killer = f.Join("K");
        killer.Send(new C2S_SetBots { Count = 7 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        var trace = new List<string>();
        for (int tick = 0; tick < 1800; tick++)
        {
            f.Tick();
            foreach (var p in f.Server.PhantomSnapshot())
                trace.Add($"{tick}:{p.Serial}:{p.IsDummy}:{p.SpawnAt:R}:{p.Until:R}:{p.X:R}:{p.Z:R}:{p.Visible}");
            var world = killer.Last<S2C_WorldState>();
            trace.Add(string.Join("|", world.Players.Select(p => $"{p.X:R},{p.Z:R}")));
            killer.Packets.Clear();
        }
        return trace;
    }

    [Fact]
    public void ThePrivateSeedFixesEveryPhantomAndTheViewOrderWhileTheCosmeticSeedChangesNothing()
    {
        var baseline = PhantomTrace(31, 1);
        Assert.True(baseline.Count(l => l.Contains(":True:") || l.Contains(":False:")) > 200, "no phantoms recorded");
        Assert.Equal(baseline, PhantomTrace(31, 1));
        Assert.Equal(baseline, PhantomTrace(31, -987654));
        Assert.NotEqual(baseline, PhantomTrace(32, 1));
    }

    [Fact]
    public void PhantomsStayWalkableMoveAtMostSurvivorSpeedAndStandStillOnlyAtTerminals()
    {
        var map = new FixedMap();
        float maxStep = GameConstants.SurvivorSpeed / GameConstants.TickRate + 1e-4f;
        int moving = 0, standing = 0, finalFrames = 0, whiteNoiseCasts = 0, dummies = 0;
        // Seed 170 adds White Noise casts; the bots of the first four seeds cast only nine skills between them on map v2.1.
        foreach (int seed in new[] { 3, 50, 90, 130, 170 })
        {
            using var r = new KillerMatchRecorder(seed);
            var terminals = r.Killer.Last<S2C_WorldState>().Terminals.ToList();
            var last = new Dictionary<int, GameServer.PhantomInfo>();
            var seen = new HashSet<int>();
            r.Run(_ =>
            {
                double now = r.F.Server.Now;
                var phantoms = r.F.Server.PhantomSnapshot();
                foreach (var cast in phantoms.Where(p => !p.IsDummy && !seen.Contains(p.Serial)).GroupBy(p => p.Until))
                {
                    // White Noise: five points per cast (several bots may cast in the same tick), spawn offsets within
                    // 0.5 s, all expiring at cast + 1.5 s.
                    Assert.Equal(0, cast.Count() % 5);
                    double castAt = cast.Key - 1.5;
                    Assert.All(cast, p => Assert.InRange(p.SpawnAt - castAt, -1e-6, 0.5 + 1e-6));
                    ++whiteNoiseCasts;
                }
                foreach (var p in phantoms.Where(p => p.IsDummy && !seen.Contains(p.Serial)))
                {
                    Assert.Equal(3.0, p.Until - p.SpawnAt, 5);
                    ++dummies;
                }
                foreach (var p in phantoms) seen.Add(p.Serial);
                foreach (var p in phantoms.Where(p => p.Visible))
                {
                    Assert.True(map.CanStand(p.X, p.Z), $"phantom {p.Serial} inside a wall at ({p.X},{p.Z})");
                    if (last.TryGetValue(p.Serial, out var before) && before.Visible)
                    {
                        float step = MathF.Sqrt((p.X - before.X) * (p.X - before.X) + (p.Z - before.Z) * (p.Z - before.Z));
                        Assert.InRange(step, 0, maxStep);
                        bool atTerminal = terminals.Any(t => (t.X - p.X) * (t.X - p.X) + (t.Z - p.Z) * (t.Z - p.Z) <= GameConstants.TerminalRadius * GameConstants.TerminalRadius + 1e-4f);
                        bool finalFrame = now + 0.05 >= p.Until - 1e-6;
                        if (step > 1e-4f) ++moving;
                        else if (finalFrame || p.Holding) ++finalFrames;
                        else
                        {
                            Assert.True(atTerminal, $"phantom {p.Serial} still at ({p.X},{p.Z}) away from every terminal");
                            ++standing;
                        }
                    }
                    last[p.Serial] = p;
                }
            });
        }
        Assert.True(whiteNoiseCasts >= 3 && dummies >= 3 && moving > 500 && standing > 50,
            $"casts {whiteNoiseCasts}, dummies {dummies}, moving {moving}, standing {standing}, final {finalFrames}");
    }

    // Frames a blip stays in the killer view after its last live frame, for a real survivor and for a dummy.
    private static (int real, int phantom) Tails(byte killerSkill)
    {
        using var m = RulesMatch.Start(1, killerSkill, SkillIds.DummyPacket);
        var a = m[0];
        m.Tick(25);
        for (int i = 0; i < 5; i++) { a.Move(0, 1); m.Tick(); }
        a.Move(0, 0);
        int realTail = 0, live = 0;
        for (int i = 0; i < 60; i++)
        {
            m.Tick();
            if (m.KillerSees(a) == null) break;
            if (m.KillerSeesAfterimage(a)) ++realTail; else ++live;
        }
        Assert.InRange(live, 1, 3);
        m.Tick(25);
        a.Send(new C2S_UseSkill { SkillId = SkillIds.DummyPacket });
        // The dummy may pause like a bot (Silent, then an afterimage) and reappear; count the tail after its last live frame.
        var seen = new List<bool?>(); // null: absent, false: live, true: afterimage
        for (int i = 0; i < 120; i++)
        {
            m.Tick();
            var sources = m.F.Server.KillerViewSources.Where(s => s.Phantom).ToList();
            seen.Add(sources.Count == 0 ? null : sources[0].Afterimage);
        }
        int lastLive = seen.LastIndexOf(false);
        Assert.InRange(lastLive, 0, 59);
        Assert.InRange(seen.Count(s => s == false), 1, 60);
        int phantomTail = seen.Skip(lastLive + 1).TakeWhile(s => s == true).Count();
        Assert.All(seen.Skip(lastLive + 1 + phantomTail), s => Assert.Null(s));
        return (realTail, phantomTail);
    }

    [Fact]
    public void AfterimageTailIsTwentyTicksForRealAndPhantomBlipsAlikeAndZeroWithoutTheSkill()
    {
        Assert.Equal((20, 20), Tails(SkillIds.PacketAfterimage));
        Assert.Equal((0, 0), Tails(SkillIds.PingScanner));
    }

    [Fact]
    public void KillerBoundTrafficOverloadCarriesNoCasterId()
    {
        using var m = RulesMatch.Start(1, SkillIds.PingScanner, SkillIds.TrafficOverload);
        m[0].Send(new C2S_UseSkill { SkillId = SkillIds.TrafficOverload });
        m.Tick();
        var effect = Assert.Single(m.Killer.All<S2C_SkillEffect>());
        Assert.Equal(0, effect.CasterId);
        Assert.Equal(m[0].Id, Assert.Single(m[0].All<S2C_SkillEffect>()).CasterId);
    }
}
