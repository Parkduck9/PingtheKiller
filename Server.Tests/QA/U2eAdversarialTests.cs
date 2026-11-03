using System.Numerics;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA;

// Independent QA for U2e (GDD §2, 10/30): a survivor-bound WorldState carries the killer entry only while the recipient is
// Normal or Lagging and the 2D segment between the two server positions of that tick touches no (closed, uninflated) wall.
// Everything here goes through GameServer + InMemoryTransport (the bytes each connection receives, decoded by the shared
// codec). The geometry reference below is written from scratch: exact rational arithmetic (BigInteger over the float bit
// patterns) and a separating-axis test; it shares no code with FixedMap.SegmentBlocked.
public sealed class U2eAdversarialTests
{
    private readonly ITestOutputHelper _out;
    public U2eAdversarialTests(ITestOutputHelper output) { _out = output; }

    private static readonly FixedMap Map = new();

    // ------------------------------------------------------------------ independent exact reference

    private static BigInteger Exact(float v)
    {
        // value * 2^149 as an integer: every finite float is mant * 2^(exp-150).
        int bits = BitConverter.SingleToInt32Bits(v);
        bool negative = bits < 0;
        int exp = (bits >> 23) & 0xFF, mant = bits & 0x7FFFFF;
        Assert.NotEqual(0xFF, exp);
        if (exp == 0) exp = 1; else mant |= 0x800000;
        var n = new BigInteger(mant) << (exp - 1);
        return negative ? -n : n;
    }

    private static bool RefHitsWall(float ax, float az, float bx, float bz, Wall w)
    {
        // Cheap exact rejects first (float comparisons are exact): bounding boxes must overlap on both axes (closed).
        if (Math.Max(ax, bx) < w.MinX || Math.Min(ax, bx) > w.MaxX) return false;
        if (Math.Max(az, bz) < w.MinZ || Math.Min(az, bz) > w.MaxZ) return false;
        BigInteger Ax = Exact(ax), Az = Exact(az), dx = Exact(bx) - Ax, dz = Exact(bz) - Az;
        int pos = 0, neg = 0;
        foreach (var (cx, cz) in new[] { (w.MinX, w.MinZ), (w.MaxX, w.MinZ), (w.MaxX, w.MaxZ), (w.MinX, w.MaxZ) })
        {
            int s = (dx * (Exact(cz) - Az) - dz * (Exact(cx) - Ax)).Sign;
            if (s > 0) pos++; else if (s < 0) neg++;
        }
        // The line through the segment strictly separates the box only when all four corners are strictly on one side.
        return pos != 4 && neg != 4;
    }

    private static bool RefBlocked(float ax, float az, float bx, float bz)
    {
        if (ax == bx && az == bz) return false; // contract: zero-length is never blocked
        foreach (var w in Map.Walls)
            if (RefHitsWall(ax, az, bx, bz, w)) return true;
        return false;
    }

    // ------------------------------------------------------------------ 1. geometry

    [Theory]
    [InlineData(12f,14f,12f,34f,true)]
    [InlineData(24f,14f,24f,34f,true)]
    [InlineData(10f,12f,26f,12f,true)]
    [InlineData(0f,5f,0f,50f,true)]
    [InlineData(5f,0f,55f,0f,true)]
    [InlineData(11.999999f,14f,11.999999f,34f,false)]
    [InlineData(24.000002f,14f,24.000002f,34f,false)]
    [InlineData(10f,11.999999f,26f,11.999999f,false)]
    [InlineData(10f,14f,14f,10f,true)]
    [InlineData(10f,14f,14f,9.999999f,false)]
    [InlineData(10f,14f,14f,10.000001f,true)]
    [InlineData(0.5f,0.5f,-3f,-3f,true)]
    [InlineData(12f,20f,10f,20f,true)]
    [InlineData(10f,20f,12f,20f,true)]
    [InlineData(24f,20f,26f,20f,true)]
    [InlineData(19f,12f,19f,5f,true)]
    [InlineData(11.999999f,20f,10f,20f,false)]
    [InlineData(19f,20f,10f,20f,true)]
    [InlineData(19f,20f,19f,25f,true)]
    [InlineData(19f,20f,37f,40f,true)]
    [InlineData(28f,40f,28f,40.5f,true)]
    [InlineData(-5f,-5f,-3f,-3f,false)]
    [InlineData(-5f,10f,5f,10f,true)]
    [InlineData(-100f,-100f,-50f,200f,false)]
    [InlineData(-1e6f,30f,1e6f,30f,true)]
    [InlineData(-1e6f,100f,1e6f,100.5f,false)]
    [InlineData(-1e6f,-1e6f,1e6f,1e6f,true)]
    [InlineData(-1e5f,82f,1e5f,82f,false)]
    [InlineData(5f,26f,5f,5f,false)]
    [InlineData(22f,22f,22f,22f,false)]
    [InlineData(19f,20f,19f,20f,false)]    public void ExplicitCasesAgreeWithBothTheReferenceAndTheProduct(float ax, float az, float bx, float bz, bool blocked)
    {
        Assert.Equal(blocked, RefBlocked(ax, az, bx, bz)); // the reference itself is right on the hand-checked cases
        Assert.Equal(blocked, Map.SegmentBlocked(ax, az, bx, bz));
        Assert.Equal(blocked, Map.SegmentBlocked(bx, bz, ax, az)); // symmetric
    }

    private static float Up(float v, int n = 1) { for (int i = 0; i < n; i++) v = MathF.BitIncrement(v); return v; }
    private static float Down(float v, int n = 1) { for (int i = 0; i < n; i++) v = MathF.BitDecrement(v); return v; }

    [Fact]
    public void EverySegmentPassingExactlyThroughAWallCornerOrBoundaryPointIsBlocked()
    {
        // Constructed on the 0.25 grid so the corner lies exactly on the segment: corner - k1*d ... corner + k2*d.
        int cases = 0;
        foreach (var w in Map.Walls)
            foreach (var (cx, cz) in new[] { (w.MinX, w.MinZ), (w.MaxX, w.MinZ), (w.MaxX, w.MaxZ), (w.MinX, w.MaxZ) })
                for (int dx = -7; dx <= 7; dx++)
                    for (int dz = -7; dz <= 7; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        foreach (var (k1, k2) in new[] { (1, 1), (3, 2), (5, 7), (2, 9), (0, 4), (4, 0) })
                        {
                            float ax = cx - k1 * dx * 0.25f, az = cz - k1 * dz * 0.25f, bx = cx + k2 * dx * 0.25f, bz = cz + k2 * dz * 0.25f;
                            if (ax == bx && az == bz) continue;
                            Assert.True(Map.SegmentBlocked(ax, az, bx, bz), $"({ax},{az})-({bx},{bz}) passes through corner ({cx},{cz})");
                            Assert.True(RefBlocked(ax, az, bx, bz));
                            cases++;
                        }
                    }
        // A point on a face (quarter grid), reached from every direction.
        foreach (var w in Map.Walls)
            for (float t = 0; t <= 1; t += 0.125f)
            {
                float px = w.MinX + (w.MaxX - w.MinX) * t, pz = w.MinZ;
                foreach (var (ox, oz) in new[] { (px - 3f, pz - 5f), (px + 7f, pz - 1.5f), (px, pz - 20f), (px - 40f, pz) })
                {
                    Assert.True(Map.SegmentBlocked(ox, oz, px, pz), $"ends on the south face of {w.MinX},{w.MinZ}");
                    Assert.True(Map.SegmentBlocked(px, pz, ox, oz));
                    cases++;
                }
            }
        Assert.True(cases > 10000, "cases " + cases);
        _out.WriteLine("constructed through-corner / on-boundary cases: " + cases);
    }

    [Fact]
    public void SegmentBlockedAgreesWithTheExactReferenceOnAdversarialSegments()
    {
        var random = new Random(20261030);
        var snaps = new List<float>();
        foreach (var w in Map.Walls) { snaps.Add(w.MinX); snaps.Add(w.MaxX); snaps.Add(w.MinZ); snaps.Add(w.MaxZ); }
        float Coord()
        {
            switch (random.Next(8))
            {
                case 0: return (float)(random.NextDouble() * 80 - 10);                       // uniform around the map
                case 1: return snaps[random.Next(snaps.Count)];                               // exactly on a wall coordinate
                case 2: { float s = snaps[random.Next(snaps.Count)]; return random.Next(2) == 0 ? Up(s, random.Next(1, 4)) : Down(s, random.Next(1, 4)); }
                case 3: return random.Next(-12, 253) * 0.25f;                                 // quarter grid
                case 4: return random.Next(-10, 71);                                          // integers
                case 5: return (float)(random.NextDouble() * 4000 - 2000);                    // far outside
                case 6: return snaps[random.Next(snaps.Count)] + random.Next(-8, 9) * 0.0625f;
                default: return (float)(random.NextDouble() * 60);                            // inside the map
            }
        }
        int blocked = 0, clear = 0, nearTies = 0, outOfRange = 0;
        var failures = new List<string>();
        var outOfRangeFailures = new List<string>();
        var farOnly = new List<string>();
        for (int i = 0; i < 120000; i++)
        {
            float ax = Coord(), az = Coord(), bx = Coord(), bz = Coord();
            switch (random.Next(6)) // structured families: share a coordinate (axis-parallel / grazing) or repeat an endpoint
            {
                case 0: bx = ax; break;
                case 1: bz = az; break;
                case 2: bx = snaps[random.Next(snaps.Count)]; ax = bx; break;
                default: break;
            }
            bool expected = RefBlocked(ax, az, bx, bz), actual = Map.SegmentBlocked(ax, az, bx, bz);
            // Strict range: coordinates a player can ever have (inside the map, never denormal). Everything else (far outside the
            // map, denormals produced by stepping below 0) is reported separately: positions there are unreachable.
            bool reachable = new[] { ax, az, bx, bz }.All(v => v >= -5 && v <= 65 && (v == 0 || Math.Abs(v) >= 0.0009765625f));
            var bucket = reachable ? failures : outOfRangeFailures;
            if (!reachable) outOfRange++;
            bool denormalOrTiny = new[] { ax, az, bx, bz }.Any(v => v != 0 && Math.Abs(v) < 0.0009765625f);
            if (expected != actual && !reachable && !denormalOrTiny) farOnly.Add($"({ax:R},{az:R})-({bx:R},{bz:R})");
            if (expected != actual) bucket.Add($"({ax:R},{az:R})-({bx:R},{bz:R}) reference blocked={expected}, product={actual}");
            if (actual != Map.SegmentBlocked(bx, bz, ax, az)) bucket.Add($"asymmetric ({ax:R},{az:R})-({bx:R},{bz:R})");
            if (expected) blocked++; else clear++;
            if (Math.Abs(ax) < 100 && Math.Abs(bx) < 100 && Math.Abs(az) < 100 && Math.Abs(bz) < 100 && (ax % 1 == 0 || az % 1 == 0)) nearTies++;
        }
        _out.WriteLine($"blocked {blocked}, clear {clear}, near-tie candidates {nearTies}, out-of-range segments {outOfRange}, " +
            $"mismatches in range {failures.Count}, out of range {outOfRangeFailures.Count} (of which far-but-not-denormal {farOnly.Count}; first: {string.Join(" | ", outOfRangeFailures.Take(3))})");
        Assert.True(blocked > 20000 && clear > 20000, $"blocked {blocked}, clear {clear}");
        Assert.True(failures.Count == 0, failures.Count + " mismatches, first: " + string.Join(" | ", failures.Take(5)));
    }

    [Fact]
    public void SegmentBlockedAllocatesNothing()
    {
        bool sink = false;
        for (int i = 0; i < 1000; i++) sink ^= Map.SegmentBlocked(i * 0.01f, 3, 40, 50 - i * 0.01f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500000; i++) sink ^= Map.SegmentBlocked(1 + (i & 63) * 0.5f, 5, 50, 20 + (i & 31));
        long after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(sink);
        Assert.Equal(0, after - before);
    }

    // ------------------------------------------------------------------ server harness (public API only + PlaceForTests)

    private sealed class Duel : IDisposable
    {
        internal QaFixture F = null!;
        internal QaPeer K = null!;
        internal QaPeer[] S = null!;
        internal int Ticks;

        internal static Duel Start(int survivors, int killerSkill = -1, int seed0 = 1)
        {
            for (int seed = seed0; seed < 20000; seed++)
            {
                var f = new QaFixture(seed: seed);
                f.Server.SyncChecksEnabled = false;
                var killer = f.Join("Killer");
                var list = new QaPeer[survivors];
                for (int i = 0; i < survivors; i++) list[i] = f.Join("S" + i);
                killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
                killer.Send(new C2S_StartMatch());
                f.Tick();
                if (killer.Last<S2C_MatchStart>().Role == (byte)Role.Killer &&
                    (killerSkill < 0 || killer.Last<S2C_MatchStart>().SkillId == killerSkill))
                    return new Duel { F = f, K = killer, S = list, Ticks = 1 };
                f.Dispose();
            }
            throw new InvalidOperationException("no seed gives the requested killer skill");
        }

        public void Dispose() => F.Dispose();
        internal void Tick(int n = 1) { for (int i = 0; i < n; i++) { F.Tick(); Ticks++; } }
        internal void Place(QaPeer p, float x, float z) => F.Server.PlaceForTests(p.Id, x, z);
        internal PlayerState KillerTruth() => K.World().Players.Single(p => p.Role == (byte)Role.Killer);
    }

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static int Count(byte[] haystack, byte[] needle)
    {
        int c = 0;
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) c++;
        return c;
    }

    private static bool Active(PlayerState self) => self.Status == (byte)PlayerStatus.Normal || self.Status == (byte)PlayerStatus.Lagging;

    // Checks survivor si's latest frame against the same tick's ground truth (the killer's own frame). Returns whether the
    // killer entry was present. Asserts the exact contract either way, including every killer coordinate byte when absent.
    private static bool CheckFrame(Duel m, int si, string ctx)
    {
        var sw = m.S[si].World();
        var kw = m.K.World();
        Assert.True(kw.Tick == sw.Tick, $"{ctx}: frames of different ticks ({kw.Tick} / {sw.Tick})");
        var k = kw.Players.Single(p => p.Role == (byte)Role.Killer);
        var self = sw.Players.Single(p => p.Id == m.S[si].Id);
        bool expectEntry = Active(self) && !RefBlocked(self.X, self.Z, k.X, k.Z);
        var entries = sw.Players.Where(p => p.Role == (byte)Role.Killer || p.Id == k.Id).ToList();
        if (expectEntry)
        {
            Assert.True(entries.Count == 1, $"{ctx}: {entries.Count} killer entries with a clear line of sight");
            var e = entries[0];
            Assert.True(e.Id == k.Id && e.Role == (byte)Role.Killer && e.Status == (byte)PlayerStatus.Normal, $"{ctx}: id/role/status of the entry");
            Assert.True(Same(e.X, k.X) && Same(e.Z, k.Z) && Same(e.Yaw, k.Yaw), $"{ctx}: entry ({e.X:R},{e.Z:R},{e.Yaw:R}) vs truth ({k.X:R},{k.Z:R},{k.Yaw:R})");
            Assert.True(e.Flags == 0 && e.StatusTimer == 0f && e.CaptureCount == 0, $"{ctx}: flags {e.Flags} timer {e.StatusTimer} count {e.CaptureCount}");
            Assert.True(sw.Players.Count(p => p.Role == (byte)Role.Survivor) == m.S.Length, $"{ctx}: survivor list incomplete");
            return true;
        }
        Assert.True(entries.Count == 0, $"{ctx}: killer entry without line of sight / spectator (status {self.Status})");
        byte[] frame = PacketCodec.Encode(sw);
        byte[] bx = BitConverter.GetBytes(k.X), bz = BitConverter.GetBytes(k.Z);
        int legitimatePairs = sw.Terminals.Count(t => Same(t.X,k.X) && Same(t.Z,k.Z)) + sw.Players.Count(p => Same(p.X,k.X) && Same(p.Z,k.Z)) +
            sw.Exits.Count(e => Same(e.X,k.X) && Same(e.Z,k.Z)) + (sw.HatchVisible == 1 && Same(sw.HatchX,k.X) && Same(sw.HatchZ,k.Z) ? 1 : 0);
        Assert.True(Count(frame,bx.Concat(bz).ToArray()) <= legitimatePairs, $"{ctx}: killer (X,Z) byte pair in the frame");
        var legit = new HashSet<int> { BitConverter.SingleToInt32Bits(sw.TimeLeft), BitConverter.SingleToInt32Bits(sw.CollapseLeft) };
        foreach (var e in sw.Exits) { legit.Add(BitConverter.SingleToInt32Bits(e.X)); legit.Add(BitConverter.SingleToInt32Bits(e.Z)); }
        if (sw.HatchVisible == 1) { legit.Add(BitConverter.SingleToInt32Bits(sw.HatchX)); legit.Add(BitConverter.SingleToInt32Bits(sw.HatchZ)); }
        foreach (var t in sw.Terminals) { legit.Add(BitConverter.SingleToInt32Bits(t.X)); legit.Add(BitConverter.SingleToInt32Bits(t.Z)); }
        foreach (var p in sw.Players) { foreach (float f in new[] { p.X, p.Z, p.Yaw, p.StatusTimer }) legit.Add(BitConverter.SingleToInt32Bits(f)); }
        // A coordinate whose four bytes are mostly zeros (2.0f = 00 00 00 40) matches across unrelated field boundaries, so
        // the single-coordinate scan only applies to patterns with at least three distinct bytes; the (X,Z) pair scan above
        // and the decoded-entry checks always apply.
        foreach (var (v, bytes) in new[] { (k.X, bx), (k.Z, bz) })
            if (!legit.Contains(BitConverter.SingleToInt32Bits(v)) && bytes.Distinct().Count() >= 3)
                // A match starting inside the header, Tick or TimeLeft (the first 14 bytes) straddles those legit fields
                // (seen with the v0.4 900 s TimeLeft: Tick high byte 00 + TimeLeft 777.25 = 00 00 50 42 = 52f).
                Assert.True(Count(frame[14..], bytes) == 0, $"{ctx}: killer coordinate {v:R} bytes in the frame");
        return false;
    }

    private static Random Rng(int seed) => new Random(seed);

    private static (float X, float Z) RandomStand(Random r, Func<float, float, bool>? accept = null)
    {
        for (; ; )
        {
            float x = (float)(0.4 + r.NextDouble() * 59.2), z = (float)(0.4 + r.NextDouble() * 59.2);
            if (Map.CanStand(x, z) && (accept == null || accept(x, z))) return (x, z);
        }
    }

    // ------------------------------------------------------------------ 2. map sweep + 4. isolation (3 survivors)

    [Fact]
    public void MapSweep_EntryPresentIffReferenceClear_NeverWhileBlocked_ThreatAndKillerViewUnchanged()
    {
        var r = Rng(77);
        int sight = 0, blocked = 0, perSurvivorMixed = 0, samples = 0;
        Duel? m = null;
        for (int i = 0; i < 3000; i++)
        {
            if (m == null || m.Ticks > 5000) { m?.Dispose(); m = Duel.Start(3, seed0: 1 + i); }
            var k = RandomStand(r);
            var positions = new List<(float X, float Z)>();
            foreach (var _ in m.S) positions.Add(RandomStand(r, (x, z) => Math.Abs(x - k.X) + Math.Abs(z - k.Z) > 3 &&
                positions.All(p => Math.Abs(x - p.X) + Math.Abs(z - p.Z) > 3)));
            m.Place(m.K, k.X, k.Z);
            for (int s = 0; s < 3; s++) m.Place(m.S[s], positions[s].X, positions[s].Z);
            m.Tick();
            int present = 0;
            for (int s = 0; s < 3; s++)
            {
                bool seen = CheckFrame(m, s, $"sample {i} survivor {s}");
                if (seen) { sight++; present++; } else blocked++;
                var sw = m.S[s].World();
                var self = sw.Players.Single(p => p.Id == m.S[s].Id);
                float dx = self.X - k.X, dz = self.Z - k.Z, d = MathF.Sqrt(dx * dx + dz * dz);
                bool boundary = Math.Abs(d - 18f) < 1e-3f || Math.Abs(d - 12f) < 1e-3f || Math.Abs(d - 6f) < 1e-3f;
                if (!boundary) Assert.True(sw.ThreatLevel == GameServer.ThreatForDistance(d), $"sample {i}: threat {sw.ThreatLevel} at d={d}");
                samples++;
            }
            if (present > 0 && present < 3) perSurvivorMixed++;
            // Killer-bound view: only its own (Role Killer) entry; idle survivors stay masked.
            var kw = m.K.World();
            Assert.Single(kw.Players);
            Assert.Equal((byte)Role.Killer, kw.Players[0].Role);
            Assert.Equal(m.K.Id, kw.Players[0].Id);
            Assert.Equal((byte)PlayerFlags.None, kw.Players[0].Flags);
        }
        m?.Dispose();
        _out.WriteLine($"checks {samples}: with sight {sight}, blocked {blocked}, ticks where survivors disagreed {perSurvivorMixed}");
        Assert.True(sight > 1500 && blocked > 1500, $"sight {sight}, blocked {blocked}");
        Assert.True(perSurvivorMixed > 300, "isolation scenario sanity: " + perSurvivorMixed);
    }

    [Fact]
    public void WallHuggingAndCornerAlignedPlacements_MatchTheReference_OneFloatStepEitherSide()
    {
        // Players placed as close to the walls as CanStand allows and exactly collinear with wall corners, plus +-1 ulp.
        var cases = new List<(float kx, float kz, float sx, float sz)>();
        foreach (var w in Map.Walls.Skip(4)) // interior walls
            foreach (var (cx, cz) in new[] { (w.MinX, w.MinZ), (w.MaxX, w.MinZ), (w.MaxX, w.MaxZ), (w.MinX, w.MaxZ) })
                for (int dx = -6; dx <= 6; dx++)
                    for (int dz = -6; dz <= 6; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        foreach (var (k1, k2) in new[] { (3, 3), (5, 2), (2, 6), (8, 8) })
                        {
                            float kx = cx - k1 * dx * 0.5f, kz = cz - k1 * dz * 0.5f, sx = cx + k2 * dx * 0.5f, sz = cz + k2 * dz * 0.5f;
                            cases.Add((kx, kz, sx, sz));
                            cases.Add((Up(kx), kz, sx, sz));
                            cases.Add((Down(kx), Up(kz), sx, sz));
                            cases.Add((kx, kz, sx, Down(sz, 2)));
                        }
                    }
        var r = Rng(5);
        cases = cases.Where(c => Map.CanStand(c.kx, c.kz) && Map.CanStand(c.sx, c.sz)).OrderBy(_ => r.Next()).Take(4000).ToList();
        Assert.True(cases.Count > 2000, "placements " + cases.Count);
        int sight = 0, blocked = 0, thinCorner = 0;
        Duel? m = null;
        foreach (var c in cases)
        {
            if (m == null || m.Ticks > 5000) { m?.Dispose(); m = Duel.Start(1, seed0: 3); }
            if (MathF.Abs(c.kx - c.sx) + MathF.Abs(c.kz - c.sz) < 1.5f) continue; // keep clear of capture contact
            m.Place(m.K, c.kx, c.kz);
            m.Place(m.S[0], c.sx, c.sz);
            m.Tick();
            if (CheckFrame(m, 0, $"killer ({c.kx:R},{c.kz:R}) survivor ({c.sx:R},{c.sz:R})")) sight++; else blocked++;
            if (RefBlocked(c.kx, c.kz, c.sx, c.sz) && !RefBlocked(Up(c.kx), c.kz, c.sx, c.sz)) thinCorner++;
        }
        m?.Dispose();
        _out.WriteLine($"sight {sight}, blocked {blocked}, one-ulp flips {thinCorner}");
        Assert.True(sight > 500 && blocked > 500, $"sight {sight}, blocked {blocked}");
    }

    // ------------------------------------------------------------------ 3. same tick, no linger, no lead

    [Fact]
    public void SameTick_MovingKillerAndMovingSurvivor_EntryFollowsTheSnapshotPositionsExactly()
    {
        using var m = Duel.Start(2);
        // Survivor 0 walks north along x = 14 while the killer stands east of the (18,14)-(20,32) wall; survivor 1 walks the
        // opposite way past the wall's far side; then the killer walks past the wall edges itself.
        m.Place(m.K, 26, 20);
        m.Place(m.S[0], 14, 6);
        m.Place(m.S[1], 42, 50);
        m.Tick();
        var last = new bool?[2];
        int[] flips = new int[2], seen = new int[2], hidden = new int[2];
        void Step(string ctx)
        {
            m.Tick();
            for (int s = 0; s < 2; s++)
            {
                bool now = CheckFrame(m, s, $"{ctx} survivor {s} tick {m.Ticks}");
                if (now) seen[s]++; else hidden[s]++;
                if (last[s] != null && last[s] != now) flips[s]++;
                last[s] = now;
            }
        }
        for (int i = 0; i < 160; i++)
        {
            m.S[0].Move(0, 1);
            m.S[1].Move(0, -1);
            Step("phase 1");
        }
        m.S[0].Move(0, 0); m.S[1].Move(0, 0);
        var truthPhase2 = m.S.Select(p => p.Self()).ToArray();
        for (int phase = 0; phase < 3; phase++)
        {
            // The killer circles the wall's far end: its snapshot position crosses the sight edges while it moves.
            var targets = new[] { (26f, 10f), (8f, 10f), (8f, 44f), (42f, 44f), (42f, 10f), (26f, 10f) };
            foreach (var (gx, gz) in targets)
                for (int i = 0; i < 400; i++)
                {
                    var me = m.KillerTruth();
                    float dx = gx - me.X, dz = gz - me.Z, len = MathF.Sqrt(dx * dx + dz * dz);
                    if (len < 0.02f) break;
                    float scale = len > 0.15f ? 1 / len : 1 / 0.15f; // shorten the last step so the walker settles on the waypoint
                    m.K.Move(dx * scale, dz * scale, i);
                    Step($"phase 2.{phase}");
                }
        }
        _out.WriteLine($"flips {flips[0]}/{flips[1]}, seen {seen[0]}/{seen[1]}, hidden {hidden[0]}/{hidden[1]}");
        for (int s = 0; s < 2; s++)
            Assert.True(flips[s] >= 4 && seen[s] > 30 && hidden[s] > 30, $"survivor {s}: flips {flips[s]} seen {seen[s]} hidden {hidden[s]}");
        GC.KeepAlive(truthPhase2);
    }

    [Fact]
    public void SameTick_OneFloatStepAcrossACornerFlipsTheEntryOnThatTickAndBack()
    {
        using var m = Duel.Start(1);
        // Survivor (16,16); the killer sits on the line through corner (18,14): exactly on it blocks, one step below is clear.
        m.Place(m.K, 14, 10);
        m.Place(m.S[0], 10, 14);
        var seq = new List<(float z, bool expectEntry)>
        {
            (12f, false), (Down(12f), true), (12f, false), (Up(12f), false), (Down(12f, 3), true), (12.5f, false), (11f, true), (12f, false)
        };
        foreach (var (z, entry) in seq)
        {
            m.Place(m.K, 14, z - 2);
            m.Tick();
            bool seen = CheckFrame(m, 0, $"killer z {z:R}");
            Assert.Equal(entry, seen);
        }
    }

    // ------------------------------------------------------------------ 4. spectators, Lagging, reconnect

    private static void OpenExitWith(Duel m, QaPeer a, QaPeer b)
    {
        // a and b co-hack the first TerminalsRequired terminals (placed exactly, no walking); the killer is parked at (5,5).
        var world = m.K.World();
        int required = world.TerminalsRequired;
        for (int t = 0; t < required; t++)
        {
            var terminal = m.S[0].World().Terminals[t];
            m.Place(a, terminal.X, terminal.Z);
            m.Place(b, terminal.X + 0.5f, terminal.Z);
            a.Send(new C2S_Interact { Active = 1 });
            b.Send(new C2S_Interact { Active = 1 });
            for (int i = 0; i < 1000 && m.S[0].World().Terminals[t].Progress < 100; i++) m.Tick();
            Assert.Equal(100, m.S[0].World().Terminals[t].Progress);
            a.Send(new C2S_Interact { Active = 0 });
            b.Send(new C2S_Interact { Active = 0 });
            m.Tick();
        }
        Assert.True(m.S[0].World().Powered());
        // GDD v2 §3: a holds the switch of exit 0 for 15 s, so that exit opens.
        var point = Map.ExitSwitches[0];
        m.Place(a, point.X, point.Z);
        a.Send(new C2S_Interact { Active = 1 });
        for (int i = 0; i < 400 && !m.S[0].World().Open(0); i++) m.Tick();
        a.Send(new C2S_Interact { Active = 0 });
        Assert.True(m.S[0].World().Open(0));
    }

    [Fact]
    public void LaggingRecipientSeesTheKiller_EliminatedAndEscapedNever_OtherSurvivorsUnaffected_ReconnectKeepsTheRule()
    {
        using var m = Duel.Start(3);
        m.Place(m.K, 5, 5);
        m.Place(m.S[0], 5, 26);
        m.Place(m.S[1], 10, 40);
        m.Place(m.S[2], 40, 8);
        m.Tick(2);
        // Capture survivor 0: Lagging, still an active recipient.
        m.Place(m.K, 5, 25.3f);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.S[0].Self().Status);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(CheckFrame(m, 0, "lagging survivor"), "Lagging recipient must receive the entry");
            Assert.True(CheckFrame(m, 1, "normal survivor beside it") == !RefBlocked(10, 40, 5, 25.3f));
            CheckFrame(m, 2, "far survivor");
            m.Tick();
        }
        // GDD v2 §4 (2026-11-03): touches never advance a Lagging survivor; back off and let its timers (60 s + 30 s) run out.
        m.Place(m.K, 5, 21);
        for (int i = 0; i < 1900 && m.S[0].Self().Status == (byte)PlayerStatus.Lagging; i++) m.Tick();
        m.Place(m.K, 5, 25.6f);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Eliminated, m.S[0].Self().Status);
        int spectatorFrames = 0, otherSight = 0;
        var r = Rng(9);
        for (int i = 0; i < 120; i++)
        {
            // Wherever the killer stands with a clear line to the dead survivor, it still gets nothing; survivor 1 follows the rule.
            var spot = RandomStand(r, (x, z) => Math.Abs(x - 5) + Math.Abs(z - 26) > 3 && Math.Abs(x - 10) + Math.Abs(z - 40) > 3 && Math.Abs(x - 40) + Math.Abs(z - 8) > 3);
            m.Place(m.K, spot.X, spot.Z);
            m.Tick();
            Assert.False(CheckFrame(m, 0, "eliminated survivor"));
            if (!RefBlocked(5, 26, spot.X, spot.Z)) spectatorFrames++;
            if (CheckFrame(m, 1, "alive survivor")) otherSight++;
            CheckFrame(m, 2, "alive survivor 2");
        }
        Assert.True(spectatorFrames > 20 && otherSight > 20, $"spectator frames with geometric sight {spectatorFrames}, other sight {otherSight}");

        // Reconnect of the eliminated survivor: still a spectator, still nothing.
        ulong token0 = m.S[0].Last<S2C_JoinResult>().ReconnectToken;
        m.S[0].Dispose();
        m.Place(m.K, 5, 10); // clear line to (5,26)
        m.Tick(3);
        var back0 = m.F.Join("S0", token0);
        m.S[0] = back0;
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Eliminated, back0.Self().Status);
        Assert.False(CheckFrame(m, 0, "reconnected eliminated survivor"));

        // Reconnect of an active survivor: the first frame obeys the same rule, clear and blocked.
        foreach (var (kx, kz, expect) in new[] { (10f, 30f, true), (26.5f, 25f, false), (10f, 30f, true) })
        {
            ulong token = m.S[1].Last<S2C_JoinResult>().ReconnectToken;
            m.S[1].Dispose();
            m.Place(m.K, kx, kz);
            m.Place(m.S[1], 10, 40);
            m.Tick(3);
            var back = m.F.Join("S1", token);
            m.S[1] = back;
            // Join's own tick already broadcast a frame to the new connection; also check the next one.
            Assert.Equal(expect, CheckFrame(m, 1, $"reconnected survivor, killer ({kx},{kz}), join tick"));
            m.Tick();
            Assert.Equal(expect, CheckFrame(m, 1, $"reconnected survivor, killer ({kx},{kz}), next tick"));
        }

        // Escaped: both remaining Normal survivors co-hack, one steps on the exit; the final frame is the escaped one's.
        OpenExitWith(m, m.S[1], m.S[2]);
        var exit = Map.Exit;
        Assert.True(Map.CanStand(exit.X, exit.Z));
        m.Place(m.K, exit.X, exit.Z + 8);
        m.Place(m.S[2], exit.X + 6, exit.Z + 3);
        m.Place(m.S[1], exit.X, exit.Z);
        Assert.False(RefBlocked(exit.X, exit.Z, exit.X, exit.Z + 8));
        m.Tick();
        var escaped = m.S[1].Last<S2C_WorldState>().Players.Single(p => p.Id == m.S[1].Id);
        Assert.Equal((byte)PlayerStatus.Escaped, escaped.Status);
        var final = m.S[1].Last<S2C_WorldState>();
        Assert.DoesNotContain(final.Players, p => p.Role == (byte)Role.Killer || p.Id == m.K.Id);
        Assert.False(RefBlocked(escaped.X, escaped.Z, exit.X, exit.Z + 8)); // geometry alone would have shown it
        var other = m.S[2].Last<S2C_WorldState>();
        Assert.Equal(final.Tick, other.Tick);
        Assert.Single(other.Players, p => p.Role == (byte)Role.Killer && p.Id == m.K.Id);
    }

    [Fact]
    public void ReconnectingActiveSurvivorMidMatchReceivesTheEntryOnlyUnderTheSameRule()
    {
        using var m = Duel.Start(2);
        for (int round = 0; round < 6; round++)
        {
            bool clear = round % 2 == 0;
            ulong token = m.S[0].Last<S2C_JoinResult>().ReconnectToken;
            m.S[0].Dispose();
            m.Tick(2);
            // While disconnected the server keeps simulating; place the pair so the line is clear / blocked on return.
            m.Place(m.K, clear ? 5f : 26.5f, clear ? 5f : 22f);
            m.Place(m.S[0], 5, 26);
            m.Place(m.S[1], 40, 8);
            m.Tick();
            m.S[0] = m.F.Join("S0", token);
            Assert.Equal(clear, CheckFrame(m, 0, "round " + round + " join tick"));
            m.Tick();
            Assert.Equal(clear, CheckFrame(m, 0, "round " + round + " next tick"));
        }
    }

    // ------------------------------------------------------------------ 5. exact entry fields with active skills; SkillEffect unchanged

    [Fact]
    public void ActiveOverclock_KillerEntryStillCarriesZeroFlagsTimerAndCaptureCount()
    {
        using var m = Duel.Start(2, killerSkill: SkillIds.Overclock);
        m.Place(m.K, 5, 5);
        m.Place(m.S[0], 5, 26);
        m.Place(m.S[1], 10, 36);
        m.Tick(2);
        m.K.Send(new C2S_UseSkill { SkillId = SkillIds.Overclock });
        int sight = 0, signalOnKillerSide = 0;
        for (int i = 0; i < 50; i++) // 2.5 s: overclock is active for the first 3 s; the killer keeps moving
        {
            m.K.Move(1, 0, 33.25f);
            m.Tick();
            var own = m.K.World().Players.Single(p => p.Role == (byte)Role.Killer);
            if ((own.Flags & (byte)PlayerFlags.Signal) != 0) signalOnKillerSide++;
            for (int s = 0; s < 2; s++)
                if (CheckFrame(m, s, $"overclock tick {i} survivor {s}")) sight++;
        }
        Assert.True(signalOnKillerSide > 40, "the killer's own view must show Signal while it moves: " + signalOnKillerSide);
        Assert.True(sight > 40, "sight " + sight);
        // The effect was announced to the killer only; no survivor-bound packet of the skill exists.
        Assert.DoesNotContain(m.S[0].Packets.OfType<S2C_SkillEffect>(), e => e.SkillId == SkillIds.Overclock);
    }

    [Fact]
    public void SurvivorBoundSkillEffectsNeverCarryKillerCoordinates_PingScanner()
    {
        using var m = Duel.Start(2, killerSkill: SkillIds.PingScanner);
        m.Place(m.K, 5.37109f, 8.8125f);
        m.Place(m.S[0], 5, 26);
        m.Place(m.S[1], 40, 8);
        m.Tick(2);
        int mark0 = m.S[0].Mark(), mark1 = m.S[1].Mark();
        m.K.Send(new C2S_UseSkill { SkillId = SkillIds.PingScanner });
        m.Tick(3);
        var k = m.KillerTruth();
        var effects = m.S[0].Since<S2C_SkillEffect>(mark0).Concat(m.S[1].Since<S2C_SkillEffect>(mark1)).ToList();
        Assert.NotEmpty(effects); // the nearest survivor is told it was scanned
        foreach (var e in effects)
        {
            byte[] bytes = PacketCodec.Encode(e);
            Assert.Equal(0, Count(bytes, BitConverter.GetBytes(k.X)));
            Assert.Equal(0, Count(bytes, BitConverter.GetBytes(k.Z)));
            Assert.Equal(0f, e.X);
            Assert.Equal(0f, e.Z);
        }
        Assert.True(CheckFrame(m, 0, "scanned survivor") && CheckFrame(m, 1, "other survivor") == !RefBlocked(40, 8, k.X, k.Z));
    }

    [Fact]
    public void SurvivorBoundSkillEffectsNeverCarryKillerCoordinates_FirewallBlock()
    {
        using var m = Duel.Start(2, killerSkill: SkillIds.FirewallBlock);
        m.Place(m.K, 5.37109f, 8.8125f);
        m.Place(m.S[0], 5, 26);
        m.Place(m.S[1], 40, 8);
        m.Tick(2);
        OpenExitWith(m, m.S[0], m.S[1]);
        // GDD v2 §3: Firewall Block needs an unopened exit whose switch has progress; exit 0 is open, so exit 1 gets 1 s.
        Spy.Server<double[]>(m.F.Server, "_switchSeconds")[1] = 1;
        m.Place(m.S[0], 5, 26);
        m.Place(m.S[1], 40, 8);
        int mark0 = m.S[0].Mark(), mark1 = m.S[1].Mark();
        m.K.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick(3);
        var k = m.KillerTruth();
        var effects = m.S[0].Since<S2C_SkillEffect>(mark0).Concat(m.S[1].Since<S2C_SkillEffect>(mark1)).ToList();
        Assert.NotEmpty(effects);
        foreach (var e in effects)
        {
            byte[] bytes = PacketCodec.Encode(e);
            Assert.Equal(0, Count(bytes, BitConverter.GetBytes(k.X)));
            Assert.Equal(0, Count(bytes, BitConverter.GetBytes(k.Z)));
        }
        CheckFrame(m, 0, "firewall survivor 0");
        CheckFrame(m, 1, "firewall survivor 1");
    }

    [Fact]
    public void PerTickAllocation_WithLineOfSightVersusBlocked_IsReported()
    {
        // Informational with a loose cap: the line-of-sight decision itself allocates nothing (SegmentBlockedAllocatesNothing).
        // Measured ~7 KB/tick extra for 7 recipients (13 KB -> 20 KB): one more list slot per recipient plus a larger encoded
        // frame (its buffer crosses a growth step); about 150 KB/s at 20 Hz, so it only guards against a runaway.
        long Measure(bool sight)
        {
            using var m = Duel.Start(7);
            for (int i = 0; i < 7; i++) m.Place(m.S[i], 5 + (i % 3) * 3, 26 + (i / 3) * 4);
            m.Place(m.K, sight ? 5 : 26.5f, sight ? 5 : 22f);
            m.Tick(50);
            long before = GC.GetAllocatedBytesForCurrentThread();
            m.Tick(400);
            return (GC.GetAllocatedBytesForCurrentThread() - before) / 400;
        }
        long blocked = Measure(false), clear = Measure(true);
        _out.WriteLine($"bytes per tick, 7 survivors: blocked {blocked}, with sight {clear}, difference {clear - blocked}");
        Assert.True(clear - blocked < 16384, $"extra allocation per tick with sight: {clear - blocked} bytes");
    }

    [Fact]
    public void ThreatLevelIsTheDistanceBandWithAndWithoutLineOfSight()
    {
        using var m = Duel.Start(2);
        // Same distance (5 m), one pair with the wall in between and one in the open: both threat 3.
        m.Place(m.K, 53, 38);
        m.Place(m.S[0], 43, 38);   // wall between
        m.Place(m.S[1], 53, 48);   // open
        m.Tick();
        Assert.False(CheckFrame(m, 0, "wall"));
        Assert.True(CheckFrame(m, 1, "open"));
        Assert.Equal(2, m.S[0].World().ThreatLevel);
        Assert.Equal(2, m.S[1].World().ThreatLevel);
        // 13 m: band 1 behind the wall and in the open.
        m.Place(m.K, 26f, 20);
        m.Place(m.S[0], 10f, 20);
        m.Place(m.S[1], 26f, 33);
        m.Tick();
        Assert.False(CheckFrame(m, 0, "wall far"));
        Assert.True(CheckFrame(m, 1, "open far"));
        Assert.Equal(1, m.S[0].World().ThreatLevel);
        Assert.Equal(1, m.S[1].World().ThreatLevel);
    }
}
