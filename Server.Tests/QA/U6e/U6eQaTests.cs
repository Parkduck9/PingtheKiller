using System;
using System.Collections.Generic;
using System.Linq;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA.U6e;

public sealed class U6eQaTests(ITestOutputHelper output)
{
    private readonly FixedMap _map = new();

    private static double Distance(MapPoint a, MapPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    private static double Distance(float ax, float az, float bx, float bz) =>
        Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    private static MapPoint SwitchPoint(MapPoint exit)
    {
        float dx = FixedMap.Size / 2f - exit.X;
        float dz = FixedMap.Size / 2f - exit.Z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        return new MapPoint(exit.X + (dx / len) * 4f, exit.Z + (dz / len) * 4f);
    }

    private static double DistanceToBox(Wall w, float x, float z)
    {
        double dx = Math.Max(0, Math.Max(w.MinX - x, x - w.MaxX));
        double dz = Math.Max(0, Math.Max(w.MinZ - z, z - w.MaxZ));
        return Math.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement1_ExitsAndExitSwitches_FullClearanceAndOppositePlacement()
    {
        Assert.Equal(2, _map.Exits.Count);
        var e0 = _map.Exits[0];
        var e1 = _map.Exits[1];

        // Exits must be at least 60 m apart
        double exitDist = Distance(e0, e1);
        Assert.True(exitDist >= 60.0, $"Exit distance {exitDist:F2} < 60 m");

        // Both exits must be near opposite edges and at least 2 m inside border
        Assert.True(e0.X >= 2f && e0.X <= 6f && e0.Z >= 2f && e0.Z <= 6f, $"Exit 0 ({e0.X},{e0.Z}) not near SW corner");
        Assert.True(e1.X >= FixedMap.Size - 6f && e1.X <= FixedMap.Size - 2f &&
                    e1.Z >= FixedMap.Size - 6f && e1.Z <= FixedMap.Size - 2f, $"Exit 1 ({e1.X},{e1.Z}) not near NE corner");

        // Exit full circles standable
        foreach (var exit in _map.Exits)
        {
            Assert.True(_map.CanStand(exit.X, exit.Z, GameConstants.ExitRadius + FixedMap.PlayerRadius));
            for (int deg = 0; deg < 360; deg += 15)
            {
                float rad = deg * MathF.PI / 180f;
                float px = exit.X + MathF.Cos(rad) * GameConstants.ExitRadius;
                float pz = exit.Z + MathF.Sin(rad) * GameConstants.ExitRadius;
                Assert.True(_map.CanStand(px, pz, FixedMap.PlayerRadius), $"Exit at ({exit.X},{exit.Z}) perimeter blocked at {deg} deg");
            }
        }

        // Check Rules v2 Switch points (4 m toward map center)
        var s0 = SwitchPoint(e0);
        var s1 = SwitchPoint(e1);

        output.WriteLine($"Switch 0: ({s0.X:F3}, {s0.Z:F3}); Switch 1: ({s1.X:F3}, {s1.Z:F3})");

        // Required interaction radius clearance for switches: TerminalRadius + PlayerRadius (1.9 m)
        float reqRadius = GameConstants.TerminalRadius + FixedMap.PlayerRadius;
        Assert.True(_map.CanStand(s0.X, s0.Z, reqRadius), $"Switch 0 ({s0.X:F2},{s0.Z:F2}) fails CanStand with radius {reqRadius}");
        Assert.True(_map.CanStand(s1.X, s1.Z, reqRadius), $"Switch 1 ({s1.X:F2},{s1.Z:F2}) fails CanStand with radius {reqRadius}");

        // Detailed radial clearance check around switch points
        for (int deg = 0; deg < 360; deg += 15)
        {
            float rad = deg * MathF.PI / 180f;
            float p0x = s0.X + MathF.Cos(rad) * GameConstants.TerminalRadius;
            float p0z = s0.Z + MathF.Sin(rad) * GameConstants.TerminalRadius;
            Assert.True(_map.CanStand(p0x, p0z, FixedMap.PlayerRadius), $"Switch 0 perimeter blocked at {deg} deg");

            float p1x = s1.X + MathF.Cos(rad) * GameConstants.TerminalRadius;
            float p1z = s1.Z + MathF.Sin(rad) * GameConstants.TerminalRadius;
            Assert.True(_map.CanStand(p1x, p1z, FixedMap.PlayerRadius), $"Switch 1 perimeter blocked at {deg} deg");
        }
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement1_HatchPoints_ClearanceFromExitsSwitchesAndConnectedToNav()
    {
        Assert.InRange(_map.HatchPoints.Count, 5, 6);
        var s0 = SwitchPoint(_map.Exits[0]);
        var s1 = SwitchPoint(_map.Exits[1]);
        var criticalPoints = new (string Name, MapPoint Pt)[]
        {
            ("Exit 0", _map.Exits[0]),
            ("Exit 1", _map.Exits[1]),
            ("Switch 0", s0),
            ("Switch 1", s1)
        };

        var nav = new NavGrid(_map);
        var path = new List<MapPoint>();

        for (int i = 0; i < _map.HatchPoints.Count; i++)
        {
            var h = _map.HatchPoints[i];
            Assert.True(_map.CanStand(h.X, h.Z, FixedMap.PlayerRadius), $"Hatch {i} ({h.X},{h.Z}) not standable");

            // Hatch must be >= 6 m from Exit 0, Exit 1, Switch 0, Switch 1
            foreach (var cp in criticalPoints)
            {
                double d = Distance(h, cp.Pt);
                output.WriteLine($"Hatch {i} ({h.X},{h.Z}) to {cp.Name} ({cp.Pt.X:F2},{cp.Pt.Z:F2}): {d:F2} m");
                Assert.True(d >= 6.0, $"Hatch {i} at ({h.X},{h.Z}) is only {d:F2} m from {cp.Name} (expected >= 6.0 m)");
            }

            // Hatch must be reachable from killer spawn
            Assert.True(nav.FindPath(h.X, h.Z, _map.SpawnPoints[0].X, _map.SpawnPoints[0].Z, path),
                $"Hatch {i} ({h.X},{h.Z}) is not connected via NavGrid to Killer spawn");
        }
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement1_SpawnPoints_DistancesClustersAndAdversarialLosRays()
    {
        Assert.Equal(8, _map.SpawnPoints.Count);
        var killer = _map.SpawnPoints[0];

        // Killer spawn within 10 m of center (40, 40)
        double killerDistToCenter = Distance(killer.X, killer.Z, 40f, 40f);
        Assert.True(killerDistToCenter <= 10.0, $"Killer spawn {killerDistToCenter:F2} m > 10 m from center");

        var survivors = _map.SpawnPoints.Skip(1).ToArray();
        Assert.Equal(7, survivors.Length);

        // Every survivor spawn must be > 20 m from killer spawn
        for (int i = 0; i < survivors.Length; i++)
        {
            var s = survivors[i];
            double d = Distance(killer, s);
            output.WriteLine($"Survivor {i + 1} ({s.X},{s.Z}) distance to Killer: {d:F2} m");
            Assert.True(d > 20.0, $"Survivor {i + 1} at ({s.X},{s.Z}) distance {d:F2} <= 20 m");

            // Direct line of sight must be blocked by a wall
            Assert.True(_map.SegmentBlocked(killer.X, killer.Z, s.X, s.Z),
                $"Line of sight from Killer to Survivor {i + 1} ({s.X},{s.Z}) is not blocked by a wall");

            // Adversarial check: offset rays by +/- PlayerRadius in orthogonal direction must also be blocked
            float dx = s.X - killer.X;
            float dz = s.Z - killer.Z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            float perpX = -dz / len * FixedMap.PlayerRadius;
            float perpZ = dx / len * FixedMap.PlayerRadius;

            Assert.True(_map.SegmentBlocked(killer.X + perpX, killer.Z + perpZ, s.X + perpX, s.Z + perpZ),
                $"Offset ray (+) from Killer to Survivor {i + 1} is not blocked by a wall");
            Assert.True(_map.SegmentBlocked(killer.X - perpX, killer.Z - perpZ, s.X - perpX, s.Z - perpZ),
                $"Offset ray (-) from Killer to Survivor {i + 1} is not blocked by a wall");
        }

        // Clusters verification: 3 clusters: (1,2,3), (4,5), (6,7)
        var cluster1 = new[] { _map.SpawnPoints[1], _map.SpawnPoints[2], _map.SpawnPoints[3] };
        var cluster2 = new[] { _map.SpawnPoints[4], _map.SpawnPoints[5] };
        var cluster3 = new[] { _map.SpawnPoints[6], _map.SpawnPoints[7] };

        // Spread within each cluster must be <= 8 m
        Assert.True(Distance(cluster1[0], cluster1[2]) <= 8.0, "Cluster 1 spread > 8 m");
        Assert.True(Distance(cluster2[0], cluster2[1]) <= 8.0, "Cluster 2 spread > 8 m");
        Assert.True(Distance(cluster3[0], cluster3[1]) <= 8.0, "Cluster 3 spread > 8 m");

        // Distance between distinct clusters must be large (> 30 m)
        Assert.True(Distance(cluster1[0], cluster2[0]) > 30.0, "Cluster 1 and 2 too close");
        Assert.True(Distance(cluster1[0], cluster3[0]) > 30.0, "Cluster 1 and 3 too close");
        Assert.True(Distance(cluster2[0], cluster3[0]) > 30.0, "Cluster 2 and 3 too close");
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement2_BuildingCountFootprintsAndDistinctShapes()
    {
        var buildings = _map.Walls.Skip(4).ToArray();
        Assert.InRange(buildings.Length, 18, 28);

        // Every building box >= 6 x 6 m
        foreach (var b in buildings)
        {
            float w = b.MaxX - b.MinX;
            float h = b.MaxZ - b.MinZ;
            Assert.True(w >= 6f - 0.001f, $"Building width {w} < 6 m");
            Assert.True(h >= 6f - 0.001f, $"Building height {h} < 6 m");
        }

        // Distinct footprints (rounded to nearest 0.1 m for float tolerance)
        var footprintGroups = buildings
            .GroupBy(b => (
                MinDim: MathF.Round(Math.Min(b.MaxX - b.MinX, b.MaxZ - b.MinZ), 1),
                MaxDim: MathF.Round(Math.Max(b.MaxX - b.MinX, b.MaxZ - b.MinZ), 1)
            ))
            .ToArray();

        output.WriteLine($"Distinct footprints: {footprintGroups.Length}");
        foreach (var fg in footprintGroups)
            output.WriteLine($"Footprint ({fg.Key.MinDim} x {fg.Key.MaxDim}): {fg.Count()} times");

        Assert.True(footprintGroups.Length >= 4, $"Only {footprintGroups.Length} distinct footprints (< 4 required)");
        foreach (var fg in footprintGroups)
            Assert.True(fg.Count() <= 3, $"Footprint ({fg.Key.MinDim} x {fg.Key.MaxDim}) repeated {fg.Count()} times (> 3 max allowed)");
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement6_DeadEndsAndAlleyWidths()
    {
        // Check U-block dead ends: south U dead end at x in [29, 33], north U dead end at x in [23, 27]
        // Both are 4 m wide dead ends.
        var deadEnds = new (string Name, float x, float openZ, float closedZ, float wallLeftX, float wallRightX)[]
        {
            ("South U-Block Dead End", 31f, 3f, 15f, 28f, 34f),
            ("North U-Block Dead End", 25f, 58f, 68f, 22f, 28f)
        };

        foreach (var de in deadEnds)
        {
            // Outside entrance must be standable
            Assert.True(_map.CanStand(de.x, de.openZ), $"{de.Name}: entrance at ({de.x},{de.openZ}) not standable");

            // Deep interior (1 m before closed cap) must be standable
            float deepZ = de.closedZ - 1f;
            Assert.True(_map.CanStand(de.x, deepZ), $"{de.Name}: interior at ({de.x},{deepZ}) not standable");

            // At closed cap and beyond must be blocked
            Assert.False(_map.CanStand(de.x, de.closedZ), $"{de.Name}: closed cap at ({de.x},{de.closedZ}) standable");

            // Flanking walls on both sides must be blocked
            Assert.False(_map.CanStand(de.wallLeftX, deepZ), $"{de.Name}: left wall at ({de.wallLeftX},{deepZ}) standable");
            Assert.False(_map.CanStand(de.wallRightX, deepZ), $"{de.Name}: right wall at ({de.wallRightX},{deepZ}) standable");
        }

        // Verify alley width rules: between any two disjoint blockers, the clear gap must be >= 2.5 m
        var blockers = _map.Walls.Concat(_map.Obstacles.Select(o => new Wall(o.MinX, o.MinZ, o.MaxX, o.MaxZ))).ToArray();
        for (int i = 0; i < blockers.Length; i++)
        {
            for (int j = i + 1; j < blockers.Length; j++)
            {
                var a = blockers[i];
                var b = blockers[j];

                float gapX = Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX);
                float gapZ = Math.Max(a.MinZ, b.MinZ) - Math.Min(a.MaxZ, b.MaxZ);

                if (gapX > 0 && gapZ <= 0)
                    Assert.True(gapX >= 2.5f - 0.001f, $"Blockers {i} and {j} have alley gapX {gapX:F3} < 2.5 m");
                if (gapZ > 0 && gapX <= 0)
                    Assert.True(gapZ >= 2.5f - 0.001f, $"Blockers {i} and {j} have alley gapZ {gapZ:F3} < 2.5 m");
            }
        }
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement8_Terminals_WallWithin6mAndExposedPlazaTerminals()
    {
        var nonBorderWalls = _map.Walls.Skip(4).ToArray();
        Assert.Equal(8, _map.TerminalPositions.Count);

        int exposedCount = 0;

        for (int i = 0; i < _map.TerminalPositions.Count; i++)
        {
            var t = _map.TerminalPositions[i];

            // Every terminal must have a wall within 6 m
            double minWallDist = nonBorderWalls.Min(w => DistanceToBox(w, t.X, t.Z));
            output.WriteLine($"Terminal {i} ({t.X},{t.Z}): nearest wall distance {minWallDist:F2} m");
            Assert.True(minWallDist <= 6.001, $"Terminal {i} at ({t.X},{t.Z}) is {minWallDist:F2} m from nearest wall (> 6 m)");

            // Terminal full interaction circle (1.5 m) standable
            Assert.True(_map.CanStand(t.X, t.Z, GameConstants.TerminalRadius + FixedMap.PlayerRadius));

            // Check if terminal is inside an open plaza
            bool insidePlaza = _map.Plazas.Any(p => t.X >= p.MinX && t.X <= p.MaxX && t.Z >= p.MinZ && t.Z <= p.MaxZ);
            if (insidePlaza)
            {
                output.WriteLine($"Terminal {i} ({t.X},{t.Z}) is inside an open plaza");
                exposedCount++;
            }
        }

        output.WriteLine($"Exposed terminals in plazas: {exposedCount}");
        Assert.True(exposedCount >= 2, $"Only {exposedCount} terminals in plazas (< 2 required)");
    }

    [Fact]
    [Trait("Category", "QaU6e")]
    public void Requirement9_Obstacles_CountDimensionsAndNonOcclusion()
    {
        Assert.InRange(_map.Obstacles.Count, 14, 24);

        foreach (var o in _map.Obstacles)
        {
            float cx = (o.MinX + o.MaxX) * 0.5f;
            float cz = (o.MinZ + o.MaxZ) * 0.5f;

            // Center of obstacle cannot be stood on
            Assert.False(_map.CanStand(cx, cz));

            // Obstacles must never block line of sight across them
            Assert.False(_map.SegmentBlocked(o.MinX - 1f, cz, o.MaxX + 1f, cz),
                $"Obstacle at ({cx},{cz}) blocked horizontal line of sight");
            Assert.False(_map.SegmentBlocked(cx, o.MinZ - 1f, cx, o.MaxZ + 1f),
                $"Obstacle at ({cx},{cz}) blocked vertical line of sight");

            // FixedMap.Move must not allow walking into obstacle
            var moved = _map.Move(o.MinX - 1f, cz, 2f, 0f);
            Assert.True(moved.X <= o.MinX - FixedMap.PlayerRadius + 0.001f,
                $"Move penetrated obstacle at X={moved.X} (minX={o.MinX})");
        }
    }
}
