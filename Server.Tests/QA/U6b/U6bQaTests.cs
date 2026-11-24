using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Server.Tests.QA.U6b
{
    public sealed class U6bQaTests
    {
        private readonly ITestOutputHelper _output;
        private readonly FixedMap _map = new FixedMap();

        public U6bQaTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static double Distance(float x1, float z1, float x2, float z2) =>
            Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));

        private static double Distance(MapPoint a, MapPoint b) =>
            Distance(a.X, a.Z, b.X, b.Z);

        [Fact]
        public void Spec1_ObstaclesNeverBlockLOS_AlwaysBlockMovementAndStand()
        {
            Assert.Equal(80f, FixedMap.Size);
            Assert.NotEmpty(_map.Obstacles);

            foreach (var o in _map.Obstacles)
            {
                float cx = (o.MinX + o.MaxX) * 0.5f;
                float cz = (o.MinZ + o.MaxZ) * 0.5f;

                // Center cannot be stood on
                Assert.False(_map.CanStand(cx, cz), $"CanStand true inside obstacle at ({cx},{cz})");

                // Line of sight directly passing through obstacle must NOT be blocked
                bool losBlockedX = _map.SegmentBlocked(o.MinX - 2f, cz, o.MaxX + 2f, cz);
                bool losBlockedZ = _map.SegmentBlocked(cx, o.MinZ - 2f, cx, o.MaxZ + 2f);
                Assert.False(losBlockedX, $"Obstacle at ({cx},{cz}) blocked X LOS segment");
                Assert.False(losBlockedZ, $"Obstacle at ({cx},{cz}) blocked Z LOS segment");

                // Move directly into the obstacle from outside: should be stopped by PlayerRadius
                float startX = o.MinX - 1.5f;
                var moved = _map.Move(startX, cz, 3.0f, 0f);
                Assert.True(moved.X <= o.MinX - FixedMap.PlayerRadius + 0.001f,
                    $"Move penetrated obstacle minX={o.MinX}, reached x={moved.X}");
            }

            // In contrast, every wall must block line of sight
            foreach (var w in _map.Walls)
            {
                float cx = (w.MinX + w.MaxX) * 0.5f;
                float cz = (w.MinZ + w.MaxZ) * 0.5f;
                Assert.False(_map.CanStand(cx, cz));
                Assert.True(_map.SegmentBlocked(w.MinX - 1f, cz, w.MaxX + 1f, cz),
                    $"Wall at ({cx},{cz}) failed to block LOS");
            }
        }

        [Fact]
        public void Spec2_LayoutAndGeometryVerification()
        {
            // 4 border walls enclosing [0, 80]
            var borderWalls = _map.Walls.Take(4).ToArray();
            Assert.Equal(4, borderWalls.Length);
            Assert.True(borderWalls[0].MinX <= 0 && borderWalls[0].MaxX <= 0); // West
            Assert.True(borderWalls[1].MinX >= 80 && borderWalls[1].MaxX >= 80); // East
            Assert.True(borderWalls[2].MinZ <= 0 && borderWalls[2].MaxZ <= 0); // South
            Assert.True(borderWalls[3].MinZ >= 80 && borderWalls[3].MaxZ >= 80); // North

            // 18-28 building boxes (map v2.1), each at least 6 x 6 m
            var buildings = _map.Walls.Skip(4).ToArray();
            Assert.InRange(buildings.Length, 18, 28);
            foreach (var b in buildings)
            {
                float width = b.MaxX - b.MinX;
                float height = b.MaxZ - b.MinZ;
                Assert.True(width >= 6.0f - 0.001f, $"Building width {width} < 6 m");
                Assert.True(height >= 6.0f - 0.001f, $"Building height {height} < 6 m");
            }

            // 14-24 low obstacles (map v2.1)
            Assert.InRange(_map.Obstacles.Count, 14, 24);

            // 2-3 open plazas, each at least 14 x 14 m with no walls
            Assert.InRange(_map.Plazas.Count, 2, 3);
            foreach (var plaza in _map.Plazas)
            {
                float pw = plaza.MaxX - plaza.MinX;
                float ph = plaza.MaxZ - plaza.MinZ;
                Assert.True(pw >= 14.0f && ph >= 14.0f, $"Plaza ({pw}x{ph}) < 14x14 m");

                // No building walls overlap with plaza interior
                foreach (var w in buildings)
                {
                    bool overlap = w.MinX < plaza.MaxX && w.MaxX > plaza.MinX &&
                                   w.MinZ < plaza.MaxZ && w.MaxZ > plaza.MinZ;
                    Assert.False(overlap, $"Building wall overlaps plaza ({plaza.MinX},{plaza.MinZ})-({plaza.MaxX},{plaza.MaxZ})");
                }
            }

            // Clear alleys: minimum clear gap between any two blockers forming an alley is at least 2.5 m
            var allBlockers = _map.Walls.Concat(_map.Obstacles.Select(o => new Wall(o.MinX, o.MinZ, o.MaxX, o.MaxZ))).ToArray();
            for (int i = 0; i < allBlockers.Length; i++)
            {
                for (int j = i + 1; j < allBlockers.Length; j++)
                {
                    var a = allBlockers[i];
                    var b = allBlockers[j];
                    float gapX = Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX);
                    float gapZ = Math.Max(a.MinZ, b.MinZ) - Math.Min(a.MaxZ, b.MaxZ);

                    if (gapX > 0 && gapZ <= 0)
                        Assert.True(gapX >= 2.5f - 0.001f, $"Alley X gap between {i} and {j} is {gapX} < 2.5 m");
                    if (gapZ > 0 && gapX <= 0)
                        Assert.True(gapZ >= 2.5f - 0.001f, $"Alley Z gap between {i} and {j} is {gapZ} < 2.5 m");
                }
            }
        }

        [Fact]
        public void Spec3_TerminalsOrderingSpreadAndInteractionRadius()
        {
            Assert.Equal(8, _map.TerminalPositions.Count);

            // First 5 terminals spread: min pairwise distance >= 14 m
            double minFirst5 = double.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                for (int j = i + 1; j < 5; j++)
                {
                    double d = Distance(_map.TerminalPositions[i], _map.TerminalPositions[j]);
                    minFirst5 = Math.Min(minFirst5, d);
                    Assert.True(d >= 14.0, $"First 5 terminals {i} and {j} distance {d:F2} < 14 m");
                }
            }
            _output.WriteLine($"Min pairwise distance among first 5 terminals: {minFirst5:F2} m");

            // First 7 terminals spread: min pairwise distance >= 14 m
            double minFirst7 = double.MaxValue;
            for (int i = 0; i < 7; i++)
            {
                for (int j = i + 1; j < 7; j++)
                {
                    double d = Distance(_map.TerminalPositions[i], _map.TerminalPositions[j]);
                    minFirst7 = Math.Min(minFirst7, d);
                    Assert.True(d >= 14.0, $"First 7 terminals {i} and {j} distance {d:F2} < 14 m");
                }
            }
            _output.WriteLine($"Min pairwise distance among first 7 terminals: {minFirst7:F2} m");

            // All 8 pairwise distance >= 14 m
            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    double d = Distance(_map.TerminalPositions[i], _map.TerminalPositions[j]);
                    Assert.True(d >= 14.0, $"All 8 terminals {i} and {j} distance {d:F2} < 14 m");
                }
            }

            // Each terminal at least 10 m from each exit
            foreach (var t in _map.TerminalPositions)
            {
                foreach (var e in _map.Exits)
                {
                    double d = Distance(t, e);
                    Assert.True(d >= 10.0, $"Terminal ({t.X},{t.Z}) is too close to exit ({e.X},{e.Z}): {d:F2} m < 10 m");
                }
            }

            // Terminal 1.5 m radius circle fully standable
            for (int i = 0; i < 8; i++)
            {
                var t = _map.TerminalPositions[i];
                Assert.True(_map.CanStand(t.X, t.Z, GameConstants.TerminalRadius + FixedMap.PlayerRadius),
                    $"Terminal {i} at ({t.X},{t.Z}) is not fully standable with radius {GameConstants.TerminalRadius}");

                // Dense perimeter check
                for (int angle = 0; angle < 360; angle += 15)
                {
                    float rad = angle * (float)Math.PI / 180f;
                    float px = t.X + (float)Math.Cos(rad) * GameConstants.TerminalRadius;
                    float pz = t.Z + (float)Math.Sin(rad) * GameConstants.TerminalRadius;
                    Assert.True(_map.CanStand(px, pz, FixedMap.PlayerRadius),
                        $"Terminal {i} circle perimeter not standable at angle {angle} ({px},{pz})");
                }
            }
        }

        [Fact]
        public void Spec3_ExitsAndHatchPoints_PositionsAndReachable()
        {
            Assert.Equal(2, _map.Exits.Count);
            Assert.Equal(_map.Exits[0], _map.Exit);

            // Both exits on opposite edges, at least 2 m inside border
            foreach (var exit in _map.Exits)
            {
                Assert.InRange(exit.X, 2.0f, FixedMap.Size - 2.0f);
                Assert.InRange(exit.Z, 2.0f, FixedMap.Size - 2.0f);

                // Exit 2 m radius circle fully standable
                Assert.True(_map.CanStand(exit.X, exit.Z, GameConstants.ExitRadius + FixedMap.PlayerRadius));
                for (int angle = 0; angle < 360; angle += 15)
                {
                    float rad = angle * (float)Math.PI / 180f;
                    float px = exit.X + (float)Math.Cos(rad) * GameConstants.ExitRadius;
                    float pz = exit.Z + (float)Math.Sin(rad) * GameConstants.ExitRadius;
                    Assert.True(_map.CanStand(px, pz, FixedMap.PlayerRadius),
                        $"Exit ({exit.X},{exit.Z}) perimeter not standable at angle {angle}");
                }
            }

            // Exits at least 60 m apart
            double exitDist = Distance(_map.Exits[0], _map.Exits[1]);
            Assert.True(exitDist >= 60.0, $"Exits distance {exitDist:F2} < 60 m");

            // Hatch points: 4 to 6 points, all standable
            Assert.InRange(_map.HatchPoints.Count, 4, 6);
            foreach (var h in _map.HatchPoints)
            {
                Assert.True(_map.CanStand(h.X, h.Z, FixedMap.PlayerRadius), $"Hatch point ({h.X},{h.Z}) not standable");
            }
        }

        [Fact]
        public void Spec3_Spawns_DistanceAndLOSBlockedFromKiller()
        {
            Assert.Equal(8, _map.SpawnPoints.Count);
            var killer = _map.SpawnPoints[0];

            // Killer near center (within 10 m of 40, 40)
            double killerDistToCenter = Distance(killer.X, killer.Z, 40f, 40f);
            Assert.True(killerDistToCenter <= 10.0, $"Killer spawn ({killer.X},{killer.Z}) > 10 m from center ({killerDistToCenter:F2} m)");

            // 7 survivor spawns
            var survivorSpawns = _map.SpawnPoints.Skip(1).ToArray();
            Assert.Equal(7, survivorSpawns.Length);

            foreach (var s in survivorSpawns)
            {
                double d = Distance(killer, s);
                Assert.True(d > 20.0, $"Survivor spawn ({s.X},{s.Z}) distance to killer {d:F2} <= 20 m");
                Assert.True(_map.SegmentBlocked(killer.X, killer.Z, s.X, s.Z),
                    $"Line of sight from killer to spawn ({s.X},{s.Z}) is NOT blocked by a wall");
            }
        }

        [Fact]
        public void Spec4_NavGrid_WalkedPathNeverCrossesBlockers()
        {
            var nav = new NavGrid(_map);
            var path = new List<MapPoint>(128);
            var rng = new Random(4242);

            MapPoint GetRandomWalkable()
            {
                while (true)
                {
                    int cell = rng.Next(NavGrid.NodeCount);
                    if (nav.IsWalkable(cell)) return NavGrid.Center(cell);
                }
            }

            // Test 50 random path queries
            for (int q = 0; q < 50; q++)
            {
                var start = GetRandomWalkable();
                var goal = GetRandomWalkable();

                Assert.True(nav.FindPath(start.X, start.Z, goal.X, goal.Z, path),
                    $"Failed to find path from ({start.X},{start.Z}) to ({goal.X},{goal.Z})");
                Assert.True(path.Count >= 2);

                // Verify line clear for smoothed path segments
                for (int i = 1; i < path.Count; i++)
                {
                    Assert.True(nav.LineClear(path[i - 1].X, path[i - 1].Z, path[i].X, path[i].Z),
                        $"NavGrid path segment {i - 1}->{i} not line clear");
                }

                // Simulate walking the path with FixedMap.Move in steps of 0.2m (20 Hz at 4.0 m/s)
                float currX = start.X, currZ = start.Z;
                for (int i = 1; i < path.Count; i++)
                {
                    var target = path[i];
                    while (Distance(currX, currZ, target.X, target.Z) > 0.05f)
                    {
                        float dx = target.X - currX;
                        float dz = target.Z - currZ;
                        float len = (float)Math.Sqrt(dx * dx + dz * dz);
                        float step = Math.Min(0.2f, len);
                        var moved = _map.Move(currX, currZ, dx / len * step, dz / len * step);
                        currX = moved.X;
                        currZ = moved.Z;

                        Assert.True(_map.CanStand(currX, currZ, FixedMap.PlayerRadius),
                            $"Player cannot stand at step ({currX:F3},{currZ:F3}) along path");
                    }
                }
            }
        }

        [Fact]
        public void Spec4_PathFollower_ReachesGoalWithinBudget()
        {
            var nav = new NavGrid(_map);
            var follower = new PathFollower();
            var rng = new Random(9876);

            MapPoint GetRandomWalkable()
            {
                while (true)
                {
                    int cell = rng.Next(NavGrid.NodeCount);
                    if (nav.IsWalkable(cell)) return NavGrid.Center(cell);
                }
            }

            const float speed = GameConstants.SurvivorSpeed;
            const float dt = 0.05f;

            for (int t = 0; t < 25; t++)
            {
                var start = GetRandomWalkable();
                var goal = GetRandomWalkable();
                double straight = Distance(start, goal);

                float x = start.X, z = start.Z;
                follower.Pause();

                // Budget: 2.5x straight line time + 4s buffer
                int maxTicks = Math.Max(200, (int)((straight / speed * 2.5 + 4.0) / dt));
                bool reached = false;

                for (int tick = 0; tick < maxTicks; tick++)
                {
                    double time = tick * dt;
                    follower.Direction(nav, x, z, goal.X, goal.Z, speed, time, out float dx, out float dz);

                    var next = _map.Move(x, z, dx * speed * dt, dz * speed * dt);
                    x = next.X;
                    z = next.Z;

                    if (Distance(x, z, goal.X, goal.Z) <= 0.15f)
                    {
                        reached = true;
                        break;
                    }
                }

                Assert.True(reached, $"PathFollower failed to reach goal ({goal.X},{goal.Z}) from ({start.X},{start.Z}) " +
                                     $"within {maxTicks * dt:F1}s (straight distance: {straight:F1}m, final distance: {Distance(x, z, goal.X, goal.Z):F2}m)");
            }
        }

        [Fact]
        public void Spec6_TerminalTable_Verification()
        {
            // Survivors 1-2: required 3, placed 5
            for (int s = 1; s <= 2; s++)
            {
                GameConstants.TerminalsFor(s, out int req, out int placed);
                Assert.Equal(3, req);
                Assert.Equal(5, placed);
            }

            // Survivors 3-5: required 4, placed 7
            for (int s = 3; s <= 5; s++)
            {
                GameConstants.TerminalsFor(s, out int req, out int placed);
                Assert.Equal(4, req);
                Assert.Equal(7, placed);
            }

            // Survivors 6-7: required 5, placed 8
            for (int s = 6; s <= 7; s++)
            {
                GameConstants.TerminalsFor(s, out int req, out int placed);
                Assert.Equal(5, req);
                Assert.Equal(8, placed);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => GameConstants.TerminalsFor(0, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => GameConstants.TerminalsFor(8, out _, out _));
        }

        [Fact]
        public void Spec4_AllBotMatch_WorstCaseStuckTime_UnderThreshold()
        {
            // Measure worst-case continuous stuck time for bots in 1v7 all-bot matches across 5 seeds.
            // A bot is considered stuck if it is active (Visible, not Lagging, not Hacking, not Rescuing)
            // but its position changes by <= 0.2 m over a sliding window.
            double worstStuckSeconds = 0.0;
            const float dt = 1f / GameConstants.TickRate; // 0.05s

            for (int seed = 1; seed <= 5; seed++)
            {
                using var server = new GameServer(new ServerOptions
                {
                    IsOffline = true,
                    Seed = seed,
                    BotCount = 8,
                    AutoStart = true
                });

                // Per-player position history: (time, X, Z)
                var histories = new Dictionary<int, Queue<(double Time, float X, float Z)>>();
                var activeStuckDurations = new Dictionary<int, double>();

                bool ended = false;
                server.Log += line =>
                {
                    if (line.StartsWith("match end ", StringComparison.Ordinal)) ended = true;
                };

                for (int tick = 0; tick < 3000 && !ended; tick++)
                {
                    server.Tick(dt);
                    var snapshot = server.PlayerSnapshot();

                    foreach (var p in snapshot)
                    {
                        if (!histories.TryGetValue(p.Id, out var q))
                        {
                            q = new Queue<(double, float, float)>();
                            histories[p.Id] = q;
                            activeStuckDurations[p.Id] = 0;
                        }

                        double now = tick * dt;
                        q.Enqueue((now, p.X, p.Z));

                        // Maintain a 1.5-second history window
                        while (q.Count > 0 && now - q.Peek().Time > 1.5)
                        {
                            q.Dequeue();
                        }

                        // Check if actively trying to move (Visible, Normal status, not hacking, not rescuing)
                        bool activelyMoving = p.Visible && p.Status == PlayerStatus.Normal && !p.Hacking && !p.Rescuing;

                        if (activelyMoving && q.Count > 1)
                        {
                            var old = q.Peek();
                            double displacement = Distance(p.X, p.Z, old.X, old.Z);

                            if (displacement <= 0.2)
                            {
                                activeStuckDurations[p.Id] += dt;
                                worstStuckSeconds = Math.Max(worstStuckSeconds, activeStuckDurations[p.Id]);
                            }
                            else
                            {
                                activeStuckDurations[p.Id] = 0;
                            }
                        }
                        else
                        {
                            activeStuckDurations[p.Id] = 0;
                        }
                    }
                }
            }

            _output.WriteLine($"Worst-case measured bot stuck time across seeds 1-5: {worstStuckSeconds:F2} s");
            // Must not be stuck for >= 3.0 s
            Assert.True(worstStuckSeconds < 3.0, $"Bot observed stuck for {worstStuckSeconds:F2} s >= 3.0 s");
        }
    }
}
