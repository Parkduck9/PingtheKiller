using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// U2e (GDD §2, 10/30): survivors see the killer only while the 2D segment between the two server positions of the tick
// touches no wall. Walls are closed boxes without inflation; touching an edge or a corner blocks; a zero-length segment
// never does.
public sealed class LineOfSightTests
{
    private static readonly FixedMap Map = new();

    // ------------------------------------------------------------------ FixedMap.SegmentBlocked

    [Theory]
    // The southwest building is the closed box (12,12)-(24,36).
    [InlineData(10f,11.9f,26f,11.9f,false)]
    [InlineData(10f,12f,26f,12f,true)]
    [InlineData(11.9f,14f,11.9f,34f,false)]
    [InlineData(12f,14f,12f,34f,true)]
    [InlineData(10f,20f,12f,20f,true)]
    [InlineData(10f,20f,11.99f,20f,false)]
    [InlineData(10f,14f,14f,10f,true)]
    [InlineData(10f,14f,14f,9.75f,false)]
    [InlineData(10f,14f,14f,10.25f,true)]
    [InlineData(16f,20f,10f,20f,true)]
    [InlineData(16f,20f,20f,25f,true)]
    [InlineData(1f,1f,79f,79f,true)]
    [InlineData(1f,1f,79f,1f,false)]
    [InlineData(5f,26f,5f,5f,false)]
    [InlineData(10f,10f,10f,10f,false)]
    [InlineData(16f,20f,16f,20f,false)]    public void SegmentBlockedMatchesTheClosedBoxRule(float ax, float az, float bx, float bz, bool blocked)
    {
        Assert.Equal(blocked, Map.SegmentBlocked(ax, az, bx, bz));
        Assert.Equal(blocked, Map.SegmentBlocked(bx, bz, ax, az));
    }

    [Fact]
    public void SegmentBlockedAgreesWithAnExactOrientationOracleOnRandomAndSnappedSegments()
    {
        // The oracle is a different method (endpoint containment + closed edge-segment intersection by orientation signs):
        // with float inputs, every product is exact in double, so its answer on touching cases is exact too.
        var random = new Random(2030);
        var snaps = new List<float>();
        foreach (var wall in Map.Walls) { snaps.Add(wall.MinX); snaps.Add(wall.MaxX); snaps.Add(wall.MinZ); snaps.Add(wall.MaxZ); }
        int blocked = 0, clear = 0;
        for (int i = 0; i < 40000; i++)
        {
            float Coordinate() => random.Next(4) == 0 ? snaps[random.Next(snaps.Count)] : (float)(random.NextDouble() * FixedMap.Size);
            float ax = Coordinate(), az = Coordinate(), bx = Coordinate(), bz = Coordinate();
            if (ax == bx && az == bz) continue;
            bool expected = Map.Walls.Any(w => OracleBlocked(ax, az, bx, bz, w));
            Assert.True(expected == Map.SegmentBlocked(ax, az, bx, bz), $"({ax:R},{az:R})-({bx:R},{bz:R}) expected blocked={expected}");
            if (expected) blocked++; else clear++;
        }
        Assert.True(blocked > 5000 && clear > 2000, $"blocked {blocked}, clear {clear}");
    }

    private static bool OracleBlocked(double ax, double az, double bx, double bz, Wall w)
    {
        if (Inside(ax, az, w) || Inside(bx, bz, w)) return true;
        var corners = new[] { (w.MinX, w.MinZ), (w.MaxX, w.MinZ), (w.MaxX, w.MaxZ), (w.MinX, w.MaxZ) };
        for (int i = 0; i < 4; i++)
        {
            var (px, pz) = corners[i];
            var (qx, qz) = corners[(i + 1) % 4];
            if (SegmentsTouch(ax, az, bx, bz, px, pz, qx, qz)) return true;
        }
        return false;
    }

    private static bool Inside(double x, double z, Wall w) => x >= w.MinX && x <= w.MaxX && z >= w.MinZ && z <= w.MaxZ;

    private static int Orientation(double px, double pz, double qx, double qz, double rx, double rz) =>
        Math.Sign((qx - px) * (rz - pz) - (qz - pz) * (rx - px));

    private static bool Between(double px, double pz, double qx, double qz, double rx, double rz) =>
        rx >= Math.Min(px, qx) && rx <= Math.Max(px, qx) && rz >= Math.Min(pz, qz) && rz <= Math.Max(pz, qz);

    private static bool SegmentsTouch(double ax, double az, double bx, double bz, double px, double pz, double qx, double qz)
    {
        int o1 = Orientation(ax, az, bx, bz, px, pz), o2 = Orientation(ax, az, bx, bz, qx, qz);
        int o3 = Orientation(px, pz, qx, qz, ax, az), o4 = Orientation(px, pz, qx, qz, bx, bz);
        if (o1 != o2 && o3 != o4 && o1 * o2 <= 0 && o3 * o4 <= 0) return true;
        return (o1 == 0 && Between(ax, az, bx, bz, px, pz)) || (o2 == 0 && Between(ax, az, bx, bz, qx, qz)) ||
            (o3 == 0 && Between(px, pz, qx, qz, ax, az)) || (o4 == 0 && Between(px, pz, qx, qz, bx, bz));
    }

    // ------------------------------------------------------------------ survivor-bound WorldState

    private static void Place(RulesMatch m, Peer peer, float x, float z) => m.F.Server.PlaceForTests(peer.Id, x, z);

    // Three idle ticks so no held input moves anyone, then the exact positions.
    private static void Setup(RulesMatch m, (float X, float Z) killer, (float X, float Z) a, (float X, float Z) b)
    {
        m.Tick(4);
        Place(m, m.Killer, killer.X, killer.Z);
        Place(m, m[0], a.X, a.Z);
        Place(m, m[1], b.X, b.Z);
        m.Tick();
    }

    private static PlayerState KillerTruth(RulesMatch m) => m.Killer.Self();

    // Blocked: no entry with the killer's Id or role, never the killer's (X,Z) byte pair, and each killer coordinate's bytes
    // no more often than other fields of the frame legitimately hold that exact value (a survivor or terminal on the same
    // grid line).
    private static void AssertNoKiller(S2C_WorldState w, PlayerState killer)
    {
        Assert.DoesNotContain(w.Players, p => p.Role == (byte)Role.Killer || p.Id == killer.Id);
        byte[] bytes = PacketCodec.Encode(w);
        byte[] x = BitConverter.GetBytes(killer.X), z = BitConverter.GetBytes(killer.Z);
        Assert.True(bytes.AsSpan().IndexOf(x.Concat(z).ToArray()) < 0, "killer (X,Z) bytes in a blocked frame");
        foreach (var (value, needle) in new[] { (killer.X, x), (killer.Z, z) })
        {
            int legit = (Same(w.TimeLeft, value) ? 1 : 0) +
                w.Terminals.Sum(t => (Same(t.X, value) ? 1 : 0) + (Same(t.Z, value) ? 1 : 0)) +
                w.Players.Sum(p => (Same(p.X, value) ? 1 : 0) + (Same(p.Z, value) ? 1 : 0) + (Same(p.Yaw, value) ? 1 : 0) +
                    (Same(p.StatusTimer, value) ? 1 : 0));
            Assert.True(Count(bytes, needle) <= legit, $"killer coordinate {value:R} bytes in a blocked frame");
        }
    }

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static int Count(byte[] haystack, byte[] needle)
    {
        int count = 0;
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) count++;
        return count;
    }

    private static PlayerState AssertKiller(S2C_WorldState w, PlayerState killer)
    {
        var entry = Assert.Single(w.Players, p => p.Role == (byte)Role.Killer);
        KillerSight.AssertEntry(entry, killer);
        Assert.Single(w.Players, p => p.Id == killer.Id);
        return entry;
    }

    [Fact]
    public void ClearLineOfSightCarriesTheKillerEntryWithExactlyTheContractFields()
    {
        using var m = RulesMatch.Start(2);
        // Killer at its (5,5) spawn walking with a facing; survivor 0 at (5,26) on the open x = 5 line.
        m.Killer.Send(new C2S_MoveInput { DirX = 0.6f, DirZ = 0.8f, Yaw = 37.5f, Seq = m.Killer.NextSeq++ });
        m.Tick();
        var killer = KillerTruth(m);
        Assert.Equal((byte)PlayerFlags.Signal, killer.Flags); // the killer's own view says it is moving
        var w = m[0].Last<S2C_WorldState>();
        var entry = AssertKiller(w, killer);
        Assert.Equal(m.Killer.Id, entry.Id);
        Assert.Equal(37.5f, entry.Yaw);
        Assert.Equal((byte)PlayerStatus.Normal, entry.Status);
        Assert.Equal(0, entry.Flags);
        Assert.Equal(0f, entry.StatusTimer);
        Assert.Equal(0, entry.CaptureCount);
        Assert.Equal(3, w.Players.Count);
        // The killer-bound view is unchanged: its own entry plus nothing else (both survivors idle and Silent).
        Assert.Single(m.Killer.Last<S2C_WorldState>().Players);
    }

    [Fact]
    public void BlockedLineOfSightLeavesNoKillerEntryAndNoKillerCoordinateBytes()
    {
        using var m = RulesMatch.Start(2);
        // Both survivors are west of the (18,14)-(20,32) wall, the killer east of it.
        Setup(m, (26.375f, 20.625f), (5, 26), (10, 26));
        for (int i = 0; i < 10; i++)
        {
            var killer = KillerTruth(m);
            Assert.Equal(26.375f, killer.X);
            foreach (var s in new[] { m[0], m[1] })
            {
                var w = s.Last<S2C_WorldState>();
                AssertNoKiller(w, killer);
                Assert.Equal(2, w.Players.Count);
            }
            m.Tick();
        }
    }

    [Fact]
    public void TouchingAWallCornerBlocksEndToEnd()
    {
        using var m = RulesMatch.Start(2);
        // Survivor 0 at (16,16); the segment to (20,12) touches only the corner (18,14).
        Setup(m, (14, 10), (10, 14), (5, 36));
        AssertNoKiller(m[0].Last<S2C_WorldState>(), KillerTruth(m));
        // A quarter metre lower passes the corner in the open: the entry appears on that tick.
        Place(m, m.Killer, 14, 9.75f);
        m.Tick();
        AssertKiller(m[0].Last<S2C_WorldState>(), KillerTruth(m));
        // A quarter metre higher cuts the corner: gone again on that tick.
        Place(m, m.Killer, 14, 10.25f);
        m.Tick();
        AssertNoKiller(m[0].Last<S2C_WorldState>(), KillerTruth(m));
    }

    [Fact]
    public void OneSurvivorsLineOfSightNeverRevealsTheKillerToAnother()
    {
        using var m = RulesMatch.Start(2);
        // Survivor 0 at (5,26) sees the killer at (5.125,5.375); survivor 1 at (25.5,26) is behind the long wall.
        Setup(m, (5.125f, 5.375f), (5, 26), (25.5f, 26));
        for (int i = 0; i < 10; i++)
        {
            var killer = KillerTruth(m);
            AssertKiller(m[0].Last<S2C_WorldState>(), killer);
            AssertNoKiller(m[1].Last<S2C_WorldState>(), killer);
            m.Tick();
        }
    }

    [Fact]
    public void TheEntryAppearsAndDisappearsOnTheTickTheLineOfSightChanges()
    {
        using var m = RulesMatch.Start(2);
        Setup(m, (5, 5), (5, 26), (5, 36));
        // The killer walks east under the wall and back; from (5,26) the wall hides it east of about x = 13.
        var route = new[] { (26f, 10f), (5f, 5f), (26f, 10f) };
        bool? previous = null;
        int appear = 0, disappear = 0, sight = 0, blocked = 0;
        foreach (var (gx, gz) in route)
            for (int i = 0; i < 400; i++)
            {
                var me = KillerTruth(m);
                float dx = gx - me.X, dz = gz - me.Z, len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 0.05f) break;
                m.Killer.Send(new C2S_MoveInput { DirX = dx / len, DirZ = dz / len, Yaw = i, Seq = m.Killer.NextSeq++ });
                m.Tick();
                var killer = KillerTruth(m);
                var w = m[0].Last<S2C_WorldState>();
                Assert.Equal(m.Killer.Last<S2C_WorldState>().Tick, w.Tick);
                var self = w.Players.Single(p => p.Id == m[0].Id);
                bool expected = !Map.SegmentBlocked(self.X, self.Z, killer.X, killer.Z);
                if (expected) { AssertKiller(w, killer); sight++; }
                else { AssertNoKiller(w, killer); blocked++; }
                // ThreatLevel is the distance band of the same tick, with or without line of sight.
                float distance = MathF.Sqrt((self.X - killer.X) * (self.X - killer.X) + (self.Z - killer.Z) * (self.Z - killer.Z));
                Assert.Equal(GameServer.ThreatForDistance(distance), w.ThreatLevel);
                if (previous == false && expected) appear++;
                if (previous == true && !expected) disappear++;
                previous = expected;
            }
        Assert.True(appear >= 1 && disappear >= 2, $"appear {appear}, disappear {disappear}");
        Assert.True(sight > 20 && blocked > 20, $"sight {sight}, blocked {blocked}");
    }

    [Fact]
    public void ThreatLevelIgnoresLineOfSight()
    {
        using var m = RulesMatch.Start(2);
        // Five metres apart (threat 3) with the wall in between, then five metres apart in the open.
        Setup(m, (53, 38), (43, 38), (5, 36));
        var w = m[0].Last<S2C_WorldState>();
        AssertNoKiller(w, KillerTruth(m));
        Assert.Equal(2, w.ThreatLevel);
        Place(m, m.Killer, 43, 48);
        m.Tick();
        w = m[0].Last<S2C_WorldState>();
        AssertKiller(w, KillerTruth(m));
        Assert.Equal(2, w.ThreatLevel);
        // Far and blocked, far and clear: both 0.
        Place(m, m.Killer, 40, 6);
        Place(m, m[0], 5, 26);
        m.Tick();
        w = m[0].Last<S2C_WorldState>();
        Assert.True(Map.SegmentBlocked(5, 26, 40, 6));
        AssertNoKiller(w, KillerTruth(m));
        Assert.Equal(0, w.ThreatLevel);
        Place(m, m.Killer, 5, 5);
        m.Tick();
        w = m[0].Last<S2C_WorldState>();
        AssertKiller(w, KillerTruth(m));
        Assert.Equal(0, w.ThreatLevel);
    }

    [Fact]
    public void LaggingSurvivorsSeeTheKillerButEliminatedOnesNever()
    {
        using var m = RulesMatch.Start(2);
        // Capture survivor 0 at its (5,26) spawn from (5,25); survivor 1 stays Normal at (10,26).
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        for (int i = 0; i < 10; i++)
        {
            m.Tick();
            AssertKiller(m[0].Last<S2C_WorldState>(), KillerTruth(m));
        }
        m.Tick(30); // the killer keeps standing 1 m away
        // Out of reach, then a fresh touch eliminates.
        for (int i = 0; i < 20; i++) { m[0].Move(0, 1); m.Tick(); }
        m[0].Move(0, 0);
        m.Tick();
        m.WalkTo(m.Killer, 5, 26.2f, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(m[0]).Status);
        for (int i = 0; i < 40; i++)
        {
            if (i % 10 == 0) m.Killer.Move(i % 20 == 0 ? 1 : -1, 0);
            m.Tick();
            var killer = KillerTruth(m);
            var w = m[0].Last<S2C_WorldState>();
            var self = w.Players.Single(p => p.Id == m[0].Id);
            Assert.Equal((byte)PlayerStatus.Eliminated, self.Status);
            Assert.False(Map.SegmentBlocked(self.X, self.Z, killer.X, killer.Z)); // the geometry alone would show it
            AssertNoKiller(w, killer);
            AssertKiller(m[1].Last<S2C_WorldState>(), killer); // the Normal survivor beside it does see the killer
        }
        Assert.Empty(m.Killer.All<S2C_GameResult>());
    }

    [Fact]
    public void EscapedSurvivorsNeverSeeTheKiller()
    {
        using var m = RulesMatch.Start(2);
        m.OpenExitWithTwoSurvivors();
        var a = m[0];
        Place(m,m.Killer,4,12); Place(m,m[1],4,20); Place(m,a,4,4); m.Tick();
        var killer = KillerTruth(m);
        var final = a.Last<S2C_WorldState>();
        var escaped = final.Players.Single(p => p.Id == a.Id);
        Assert.Equal((byte)PlayerStatus.Escaped, escaped.Status);
        Assert.False(Map.SegmentBlocked(escaped.X, escaped.Z, killer.X, killer.Z));
        AssertNoKiller(final, killer);
        // Same tick, Normal survivor 1 at terminal 2 with a clear line: the entry is there.
        var other = m[1].Last<S2C_WorldState>();
        Assert.Equal(final.Tick, other.Tick);
        AssertKiller(other, killer);
    }
}
