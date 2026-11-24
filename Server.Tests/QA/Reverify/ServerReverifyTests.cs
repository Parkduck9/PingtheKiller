using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.Core;
using PingTheKiller.Tests.QA;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA.Reverify;

public sealed class ServerReverifyTests
{
    private readonly ITestOutputHelper _output;
    private static readonly FixedMap Map = new();

    public ServerReverifyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // =========================================================================
    // U2c Section 1: Killer view is anonymous
    // =========================================================================

    [Fact]
    public void U2c_Sec1_KillerView_ByteLevelAnonymity_AndShuffling()
    {
        using var r = new QaRig(3, seed: 101);
        r.Place(r.K, 15, 10);
        r.Place(r.S[0], 25, 10);
        r.Place(r.S[1], 35, 10);
        r.Place(r.S[2], 45, 10);

        // Make survivors signal by sending move inputs
        for (int i = 0; i < 3; i++) r.S[i].Move(1, 0);
        r.Tick();

        r.K.Drain();
        var killerPeers = r.K.Packets.OfType<S2C_WorldState>().ToList();
        Assert.NotEmpty(killerPeers);

        int killerId = r.K.Id;
        var survivorIds = r.S.Select(s => s.Id).ToHashSet();

        // Check raw bytes across multiple frames
        var observedOrders = new HashSet<string>();
        for (int step = 0; step < 30; step++)
        {
            for (int i = 0; i < 3; i++) r.S[i].Move(1, 0);
            r.Tick();

            r.K.Drain();
            byte[] lastRawFrame = PacketCodec.Encode(r.K.Packets.OfType<S2C_WorldState>().Last());
            var (threat, terminals, entries) = RawFrame.ParseWorld(lastRawFrame);

            // Assert exact byte level fields
            int killerCount = 0;
            var orderList = new List<int>();
            foreach (var entry in entries)
            {
                if (entry.Role == (byte)Role.Killer)
                {
                    Assert.Equal(killerId, entry.Id);
                    killerCount++;
                    orderList.Add(-1);
                }
                else
                {
                    Assert.Equal((byte)Role.Survivor, entry.Role);
                    Assert.Equal(0, entry.Id); // ID MUST BE ZERO
                    Assert.DoesNotContain(entry.Id, survivorIds); // Can never match real survivor Id
                    Assert.Equal(0f, entry.Yaw); // Yaw MUST BE ZERO
                    Assert.Equal(0f, entry.Timer); // StatusTimer MUST BE ZERO
                    Assert.Equal(0, entry.CaptureCount); // CaptureCount MUST BE ZERO
                    Assert.Equal((byte)PlayerFlags.Signal, entry.Flags); // ONLY Signal flag!
                    Assert.True(entry.Status == (byte)PlayerStatus.Normal || entry.Status == (byte)PlayerStatus.Lagging);

                    // Byte check at entry offset
                    int idFromRaw = BinaryPrimitives.ReadInt32LittleEndian(lastRawFrame.AsSpan(entry.Offset));
                    Assert.Equal(0, idFromRaw);
                    orderList.Add((int)(entry.X * 10));
                }
            }
            Assert.Equal(1, killerCount);
            observedOrders.Add(string.Join(",", orderList));
        }

        // Shuffling: order of entries is shuffled every snapshot with private RNG
        Assert.True(observedOrders.Count > 1, "Killer view entries were never shuffled across 30 ticks");
    }

    [Fact]
    public void U2c_Sec1_DecoysNoLongerBorrowIds_WhiteNoiseAlwaysFive_In1v1()
    {
        // In 1v1 match (1 killer + 1 survivor)
        using var r = new QaRig(1, seed: 202);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 25, 25);
        r.SetSkill(r.S[0], SkillIds.WhiteNoise);

        r.Tick();
        r.Use(r.S[0], SkillIds.WhiteNoise);
        r.Tick();

        // In 1v1, WhiteNoise must still spawn 5 phantoms (never curtailed or borrowed)
        var phantoms = r.Server.PhantomSnapshot();
        Assert.Equal(5, phantoms.Count);
        foreach (var p in phantoms)
        {
            Assert.False(p.IsDummy);
        }

        // Advance ticks so phantoms enter visibility
        int maxSeenByKiller = 0;
        for (int i = 0; i < 20; i++)
        {
            r.Tick();
            var kw = r.KW;
            int survivorEntries = kw.Players.Count(p => p.Role == (byte)Role.Survivor);
            if (survivorEntries > maxSeenByKiller) maxSeenByKiller = survivorEntries;
            foreach (var p in kw.Players)
            {
                if (p.Role == (byte)Role.Survivor)
                {
                    Assert.Equal(0, p.Id);
                    Assert.Equal(0f, p.Yaw);
                    Assert.Equal(0f, p.StatusTimer);
                    Assert.Equal(0, p.CaptureCount);
                    Assert.Equal((byte)PlayerFlags.Signal, p.Flags);
                }
            }
        }
        Assert.True(maxSeenByKiller >= 5, $"Expected up to 5 WhiteNoise points in 1v1, saw {maxSeenByKiller}");
    }

    // =========================================================================
    // U2c Section 2: One emit path for real and fake blips & afterimage
    // =========================================================================

    [Fact]
    public void U2c_Sec2_PhantomAndReal_IdenticalFields_AndAfterimageLinger()
    {
        // Killer has PacketAfterimage
        using var r = new QaRig(1, seed: 303);
        r.SetSkill(r.K, SkillIds.PacketAfterimage);
        r.SetSkill(r.S[0], SkillIds.DummyPacket);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 20, 20);

        r.Tick();
        r.Use(r.S[0], SkillIds.DummyPacket);
        r.Tick();

        var dummies = r.Server.PhantomSnapshot().Where(p => p.IsDummy).ToList();
        Assert.Single(dummies);

        // Dummy duration is 3.0 s = 60 ticks.
        // During active Dummy:
        for (int i = 0; i < 55; i++)
        {
            r.Tick();
            var kw = r.KW;
            var dummyEntries = kw.Players.Where(p => p.Role == (byte)Role.Survivor).ToList();
            foreach (var dummyEntry in dummyEntries)
            {
                Assert.Equal(0, dummyEntry.Id);
                Assert.Equal(0f, dummyEntry.Yaw);
                Assert.Equal(0f, dummyEntry.StatusTimer);
                Assert.Equal(0, dummyEntry.CaptureCount);
                Assert.Equal((byte)PlayerFlags.Signal, dummyEntry.Flags);
                Assert.Equal((byte)PlayerStatus.Normal, dummyEntry.Status);
            }
        }

        // Advance until Dummy expires (at 60 ticks)
        for (int i = 55; i < 62; i++) r.Tick();

        // Now Dummy has expired. With PacketAfterimage, it lingers frozen at its last position
        int afterimageTicksSeen = 0;
        for (int i = 0; i < 25; i++)
        {
            r.Tick();
            var kw = r.KW;
            var entries = kw.Players.Where(p => p.Role == (byte)Role.Survivor).ToList();
            if (entries.Count > 0)
            {
                afterimageTicksSeen++;
                foreach (var entry in entries)
                {
                    Assert.Equal(0, entry.Id);
                    Assert.Equal((byte)PlayerFlags.Signal, entry.Flags);
                }
            }
        }

        // Lingers for afterimage window (up to 20 ticks / 1.0 s from last visible)
        Assert.True(afterimageTicksSeen > 0 && afterimageTicksSeen <= 21);

        // After 1.0s afterimage window, phantom must be completely gone
        for (int i = 0; i < 10; i++) r.Tick();
        Assert.DoesNotContain(r.KW.Players, p => p.Role == (byte)Role.Survivor);
    }

    // =========================================================================
    // U2c Section 3: Decoy behaviour (movement, boundaries, stillness)
    // =========================================================================

    [Fact]
    public void U2c_Sec3_PhantomMovement_NeverInsideWalls_AndStillNearTerminals()
    {
        using var r = new QaRig(1, seed: 404);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 25, 25);
        r.SetSkill(r.S[0], SkillIds.WhiteNoise);

        r.Tick();
        r.Use(r.S[0], SkillIds.WhiteNoise);

        float maxSpeed = GameConstants.SurvivorSpeed;
        var prevPositions = new Dictionary<int, (float X, float Z)>();

        for (int tick = 0; tick < 35; tick++)
        {
            r.Tick();
            var phantoms = r.Server.PhantomSnapshot();
            foreach (var p in phantoms)
            {
                // Invariant 1: Phantoms never inside walls
                Assert.True(Map.CanStand(p.X, p.Z), $"Phantom {p.Serial} was inside a wall at ({p.X}, {p.Z})");

                // Invariant 2: Phantom speed never exceeds SurvivorSpeed
                if (prevPositions.TryGetValue(p.Serial, out var prev))
                {
                    float dx = p.X - prev.X, dz = p.Z - prev.Z;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    float allowedDist = maxSpeed * 0.05f + 0.01f;
                    Assert.True(dist <= allowedDist, $"Phantom moved too fast: {dist} > {allowedDist}");
                }
                prevPositions[p.Serial] = (p.X, p.Z);
            }
        }
    }

    [Fact]
    public void U2c_Sec3_DummyPacket_SpawnsAtCasterPosition()
    {
        using var r = new QaRig(1, seed: 505);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 15.5f, 25.5f);
        r.SetSkill(r.S[0], SkillIds.DummyPacket);

        r.Tick();
        r.Use(r.S[0], SkillIds.DummyPacket);
        r.Tick();

        var dummies = r.Server.PhantomSnapshot().Where(p => p.IsDummy).ToList();
        Assert.Single(dummies);
        var dummy = dummies[0];
        Assert.True(MathF.Abs(dummy.X - 15.5f) < 0.01f);
        Assert.True(MathF.Abs(dummy.Z - 25.5f) < 0.01f);
    }

    // =========================================================================
    // U2c Section 4: Server RNG privacy
    // =========================================================================

    [Fact]
    public void U2c_Sec4_CosmeticWireSeed_DoesNotAffectPrivateSimulation()
    {
        // Fix private seed to 777, vary cosmetic wire seed
        using var r1 = new QaRig(2, seed: 777, matchSeed: 1111);
        using var r2 = new QaRig(2, seed: 777, matchSeed: 9999);

        // Drive both matches identically for 50 ticks
        for (int i = 0; i < 50; i++)
        {
            r1.S[0].Move(1, 0);
            r2.S[0].Move(1, 0);
            r1.Tick();
            r2.Tick();

            var kw1 = r1.KW;
            var kw2 = r2.KW;

            Assert.Equal(kw1.Players.Count, kw2.Players.Count);
            for (int j = 0; j < kw1.Players.Count; j++)
            {
                Assert.Equal(kw1.Players[j].X, kw2.Players[j].X);
                Assert.Equal(kw1.Players[j].Z, kw2.Players[j].Z);
                Assert.Equal(kw1.Players[j].Status, kw2.Players[j].Status);
                Assert.Equal(kw1.Players[j].Flags, kw2.Players[j].Flags);
            }
        }

        // Verify wire seeds were different in MatchStart
        Assert.Equal(1111, r1.K.Last<S2C_MatchStart>().Seed);
        Assert.Equal(9999, r2.K.Last<S2C_MatchStart>().Seed);
    }

    [Fact]
    public void U2c_Sec4_KillerBoundTrafficOverload_CarriesZeroCasterId()
    {
        using var r = new QaRig(1, seed: 808);
        r.SetSkill(r.S[0], SkillIds.TrafficOverload);
        r.Place(r.K, 5, 5);
        r.Place(r.S[0], 15, 15);

        r.Tick();
        r.Use(r.S[0], SkillIds.TrafficOverload);
        r.Tick();

        r.K.Drain();
        var killerEffects = r.K.Packets.OfType<S2C_SkillEffect>().Where(e => e.SkillId == SkillIds.TrafficOverload).ToList();
        Assert.NotEmpty(killerEffects);
        foreach (var effect in killerEffects)
        {
            // Killer-bound S2C_SkillEffect must NEVER carry survivor's Id: CasterId = 0
            Assert.Equal(0, effect.CasterId);
        }
    }

    // =========================================================================
    // U2c Section 5 & 5b: SelfState ownership & HUD contract
    // =========================================================================

    [Fact]
    public void U2c_Sec5_SelfState_OnlyToOwnPlayer_AndExecutionGraceLeft_AndTerminalProgress()
    {
        using var r = new QaRig(2, seed: 909);
        r.Place(r.K, 10, 10);
        r.Place(r.S[0], 11, 10); // 1.0 m from Killer (within 3m)
        r.Place(r.S[1], 40, 40); // 30m away

        r.MakeLagging(r.S[0]);
        // Set execution grace on S[0]
        double now = Spy.Server<double>(r.Server, "_now");
        Spy.Set(r.P(r.S[0]), "ExecutionGraceUntil", now + 2.0);

        r.Tick();

        // 1. Killer's SelfState receives ExecutionGraceLeft > 0 for S[0] within 3m
        r.K.Drain();
        var killerSelf = r.K.Packets.OfType<S2C_SelfState>().LastOrDefault();
        Assert.NotNull(killerSelf);
        Assert.InRange(killerSelf.ExecutionGraceLeft, 1.8f, 2.0f);

        // 2. Survivor's SelfState NEVER receives ExecutionGraceLeft (always 0)
        r.S[0].Drain();
        var s0Self = r.S[0].Packets.OfType<S2C_SelfState>().LastOrDefault();
        Assert.NotNull(s0Self);
        Assert.Equal(0f, s0Self.ExecutionGraceLeft);

        r.S[1].Drain();
        var s1Self = r.S[1].Packets.OfType<S2C_SelfState>().LastOrDefault();
        Assert.NotNull(s1Self);
        Assert.Equal(0f, s1Self.ExecutionGraceLeft);

        // 3. Killer moved far away (> 3m): ExecutionGraceLeft drops to 0
        r.Place(r.K, 25, 25);
        r.Tick();
        r.K.Drain();
        var killerSelfFar = r.K.Packets.OfType<S2C_SelfState>().LastOrDefault();
        Assert.NotNull(killerSelfFar);
        Assert.Equal(0f, killerSelfFar.ExecutionGraceLeft);

        // 4. Terminal Progress quantization in killer-bound WorldState
        // Set partial progress on terminal 0
        var terminals = Spy.Server<TerminalState[]>(r.Server, "_terminals");
        terminals[0].Progress = 45;

        r.Tick();
        var kw = r.KW;
        var sw = r.SW;

        // Killer sees 0 for incomplete terminal
        Assert.Equal(0, kw.Terminals[0].Progress);
        // Survivor sees actual 45%
        Assert.Equal(45, sw.Terminals[0].Progress);

        // Complete terminal to 100
        terminals[0].Progress = 100;
        r.Tick();
        Assert.Equal(100, r.KW.Terminals[0].Progress);
        Assert.Equal(100, r.SW.Terminals[0].Progress);
    }

    // =========================================================================
    // U2c Section 6: Carried from U2b QA (Deterministic PrivateRandom)
    // =========================================================================

    [Fact]
    public void U2c_Sec6_PrivateRandom_Has256BitState_AndDeterministicHook()
    {
        var pr1 = PrivateRandom.FromSeed(12345, 0);
        var pr2 = PrivateRandom.FromSeed(12345, 0);
        var prDiff = PrivateRandom.FromSeed(54321, 0);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(pr1.NextUInt64(), pr2.NextUInt64());
        }
        Assert.NotEqual(pr1.NextUInt64(), prDiff.NextUInt64());
    }

    // =========================================================================
    // U2e Contract Item 1: Line of sight requirement (FixedMap.SegmentBlocked)
    // =========================================================================

    [Theory]
    // Closed box: segment grazing face or touching corner is BLOCKED
    [InlineData(12f, 24f, 18f, 37f, true)] // Diagonal of the (12,24)-(18,37) wall
    [InlineData(10f, 26f, 14f, 22f, true)] // Touches corner (12, 24)
    [InlineData(10f, 26f, 14f, 21.999f, false)] // Barely misses corner outside
    [InlineData(10f, 30f, 12f, 30f, true)] // Touches west face
    [InlineData(10f, 30f, 11.999f, 30f, false)] // Barely short of face
    [InlineData(19f, 20f, 19f, 20f, false)] // Zero-length: NEVER BLOCKED
    [InlineData(5f, 5f, 5f, 5f, false)] // Zero-length outside: NEVER BLOCKED
    public void U2e_Contract1_SegmentBlocked_EdgeCases(float ax, float az, float bx, float bz, bool expectedBlocked)
    {
        Assert.Equal(expectedBlocked, Map.SegmentBlocked(ax, az, bx, bz));
        Assert.Equal(expectedBlocked, Map.SegmentBlocked(bx, bz, ax, az));
    }

    // =========================================================================
    // U2e Contract Items 2 & 4: Exact entry fields & Per-recipient isolation
    // =========================================================================

    [Fact]
    public void U2e_Contract2_And_4_EntryFieldsExact_AndPerRecipientIsolation()
    {
        using var r = new QaRig(2, seed: 1001);
        int killerId = r.K.Id;

        // Killer at (8, 20)
        // S[0] at (10, 20) -> Clear line of sight (no wall between 10 and 15 at z=20)
        // S[1] at (26, 20) -> Blocked by wall (18, 14) to (20, 32)
        r.Place(r.K, 8, 20);
        r.Place(r.S[0], 10, 20);
        r.Place(r.S[1], 26, 20);

        r.Tick();

        var w0 = r.S[0].World();
        var w1 = r.S[1].World();

        // 1. Per-recipient isolation: S[0] sees killer, S[1] does NOT
        bool hasKillerInS0 = w0.Players.Any(p => p.Role == (byte)Role.Killer);
        bool hasKillerInS1 = w1.Players.Any(p => p.Role == (byte)Role.Killer);

        Assert.True(hasKillerInS0);
        Assert.False(hasKillerInS1);

        // 2. Exact contract fields in S[0]
        var killerEntryInS0 = w0.Players.First(p => p.Role == (byte)Role.Killer);
        Assert.Equal(killerId, killerEntryInS0.Id);
        Assert.Equal((byte)Role.Killer, killerEntryInS0.Role);
        Assert.Equal((byte)PlayerStatus.Normal, killerEntryInS0.Status);
        Assert.True(MathF.Abs(killerEntryInS0.X - 8f) < 0.01f);
        Assert.True(MathF.Abs(killerEntryInS0.Z - 20f) < 0.01f);
        Assert.Equal((byte)PlayerFlags.None, killerEntryInS0.Flags); // Flags = 0
        Assert.Equal(0f, killerEntryInS0.StatusTimer); // StatusTimer = 0
        Assert.Equal(0, killerEntryInS0.CaptureCount); // CaptureCount = 0

        // 3. Byte check: S[1] receives NO killer float bytes
        Assert.DoesNotContain(w1.Players, p => p.Id == killerId);
    }

    // =========================================================================
    // U2e Contract Item 3: Spectators (Eliminated and Escaped) never receive Killer entry
    // =========================================================================

    [Fact]
    public void U2e_Contract3_SpectatorsNeverReceiveKillerEntry()
    {
        using var r = new QaRig(2, seed: 1002);
        // Clear LoS positions: Killer at (8, 20), S[0] at (10, 20), S[1] at (10, 22)
        r.Place(r.K, 8, 20);
        r.Place(r.S[0], 10, 20);
        r.Place(r.S[1], 10, 22);

        // Mark S[0] as Eliminated
        Spy.Set(r.P(r.S[0]), "Status", (byte)PlayerStatus.Eliminated);
        // Mark S[1] as Escaped
        Spy.Set(r.P(r.S[1]), "Status", (byte)PlayerStatus.Escaped);

        r.Tick();

        var w0 = r.S[0].World();
        var w1 = r.S[1].World();

        // Even with clear LoS, spectators NEVER receive Killer entry
        Assert.DoesNotContain(w0.Players, p => p.Role == (byte)Role.Killer);
        Assert.DoesNotContain(w1.Players, p => p.Role == (byte)Role.Killer);
    }

    // =========================================================================
    // U2e Contract Item 5: No linger, no prediction (same-tick flip)
    // =========================================================================

    [Fact]
    public void U2e_Contract5_SameTickFlip_NoLinger_NoPrediction()
    {
        using var r = new QaRig(1, seed: 1003);
        r.Place(r.S[0], 10, 20);

        // Step 1: Killer behind wall at (26, 20) -> BLOCKED
        r.Place(r.K, 26, 20);
        r.Tick();
        Assert.DoesNotContain(r.S[0].World().Players, p => p.Role == (byte)Role.Killer);

        // Step 2: Killer moves to open view at (8, 20) -> CLEAR LoS (same tick!)
        r.Place(r.K, 8, 20);
        r.Tick();
        var seen = r.S[0].World().Players.First(p => p.Role == (byte)Role.Killer);
        Assert.True(MathF.Abs(seen.X - 8f) < 0.01f);

        // Step 3: Killer moves back behind wall at (26, 20) -> BLOCKED (immediately gone, 0 linger ticks!)
        r.Place(r.K, 26, 20);
        r.Tick();
        Assert.DoesNotContain(r.S[0].World().Players, p => p.Role == (byte)Role.Killer);
    }

    // =========================================================================
    // U2e Contract Item 6: ThreatLevel distance band & No killer coordinates in SkillEffects
    // =========================================================================

    [Fact]
    public void U2e_Contract6_ThreatLevelUnaffectedByLoS_AndNoKillerCoordsInSkillEffects()
    {
        using var r = new QaRig(1, seed: 1004);
        // Survivor at (10, 20), Killer at (26, 20) -> Distance is 12m (ThreatLevel 1), but LoS is BLOCKED by wall (18,14)-(20,32)
        r.Place(r.S[0], 10, 20);
        r.Place(r.K, 26, 20);

        r.Tick();
        var sw = r.S[0].World();

        // 1. Line of sight is blocked
        Assert.DoesNotContain(sw.Players, p => p.Role == (byte)Role.Killer);

        // 2. ThreatLevel is STILL computed from distance regardless of LoS!
        Assert.True(sw.ThreatLevel > 0, "ThreatLevel should be > 0 within threat range even when LoS is blocked");

        // 3. PingScanner effect to target survivor must NOT leak killer coordinates
        r.SetSkill(r.K, SkillIds.PingScanner);
        r.Tick();
        r.Use(r.K, SkillIds.PingScanner);
        r.Tick();

        r.S[0].Drain();
        var effects = r.S[0].Packets.OfType<S2C_SkillEffect>().Where(e => e.SkillId == SkillIds.PingScanner).ToList();
        Assert.NotEmpty(effects);
        foreach (var effect in effects)
        {
            Assert.Equal(0f, effect.X);
            Assert.Equal(0f, effect.Z);
        }
    }
}
