using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore.Map
{
    public readonly struct MapPoint
    {
        public MapPoint(float x, float z) { X = x; Z = z; }
        public float X { get; }
        public float Z { get; }
    }

    public readonly struct Wall
    {
        public Wall(float minX, float minZ, float maxX, float maxZ)
        { MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; }
        public float MinX { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxZ { get; }
        public bool IntersectsCircle(float x, float z, float radius)
        {
            float dx = x - Math.Max(MinX, Math.Min(MaxX, x));
            float dz = z - Math.Max(MinZ, Math.Min(MaxZ, z));
            return dx * dx + dz * dz < radius * radius - 0.000001f;
        }

        // Slab test of the segment (ax, az)-(bx, bz) against the closed box: touching an edge or a corner intersects.
        public bool IntersectsSegment(float ax, float az, float bx, float bz)
        {
            double enter = 0, exit = 1;
            return Slab(ax, (double)bx - ax, MinX, MaxX, ref enter, ref exit) &&
                Slab(az, (double)bz - az, MinZ, MaxZ, ref enter, ref exit);
        }

        private static bool Slab(double start, double delta, double min, double max, ref double enter, ref double exit)
        {
            if (delta == 0) return start >= min && start <= max;
            double t1 = (min - start) / delta, t2 = (max - start) / delta;
            if (t1 > t2) { double swap = t1; t1 = t2; t2 = swap; }
            if (t1 > enter) enter = t1;
            if (t2 < exit) exit = t2;
            return enter <= exit;
        }
    }

    public sealed class FixedMap
    {
        public const float Size = 80;
        public const float PlayerRadius = 0.4f;
        private readonly Wall[] _walls =
        {
            new Wall(-1, -1, 0, Size + 1), new Wall(Size, -1, Size + 1, Size + 1),
            new Wall(0, -1, Size, 0), new Wall(0, Size, Size, Size + 1),
            // x < 12, z < 39 stays open: the 12 m approach road to exit 0, also the rule-test lane.
            // SW L loop, then the south U block whose 4 m dead end opens onto the south edge street.
            new Wall(12, 8, 20, 14), new Wall(12, 14, 19, 21),
            new Wall(23, 4, 29, 15), new Wall(33, 4, 39, 15), new Wall(23, 15, 39, 21),
            new Wall(43, 4, 52, 12), new Wall(57, 4, 71, 11), new Wall(64, 11, 71, 18),
            // Middle band: west loop, central T, the single-box loop south of the centre crossroads.
            new Wall(12, 24, 18, 37),
            new Wall(21, 30, 35, 37), new Wall(25, 24, 31, 30), new Wall(42, 16, 53, 30),
            new Wall(24, 40, 31, 54), new Wall(31, 47, 37, 54), new Wall(47, 40, 56, 47), new Wall(47, 47, 53, 54),
            new Wall(60, 41, 72, 48), new Wall(65, 48, 72, 56),
            // North band: the north U block (4 m dead end open to the south), a T loop and two loops by exit 1.
            new Wall(4, 61, 13, 72),
            new Wall(17, 59, 23, 76), new Wall(27, 59, 33, 68), new Wall(23, 68, 33, 76),
            new Wall(38, 64, 52, 72), new Wall(42, 58, 48, 64), new Wall(56, 59, 63, 70),
            new Wall(67, 59, 76, 65), new Wall(67, 65, 74, 71)
        };
        private readonly Obstacle[] _obstacles =
        {
            // Plaza cars and edge containers.
            new Obstacle(9, 46, 11, 50.5f), new Obstacle(13, 52, 17, 54), new Obstacle(63, 26, 65, 30.5f),
            new Obstacle(66, 32, 70, 34), new Obstacle(75, 25, 77, 31), new Obstacle(74.5f, 3, 77, 9),
            new Obstacle(44, 75, 48, 77), new Obstacle(58, 73, 62, 75), new Obstacle(12, 75, 14, 77),
            // Junk at the centre crossroads, at loop corners and at street mouths.
            new Obstacle(37.5f, 40, 39.5f, 42), new Obstacle(44.5f, 33, 46.5f, 37), new Obstacle(60, 33, 62, 35.5f),
            new Obstacle(59, 13.5f, 61, 15.5f), new Obstacle(56.5f, 51, 58.5f, 55), new Obstacle(14, 44, 16, 46)
        };
        public FixedMap()
        {
            Walls = Array.AsReadOnly(_walls);
            Obstacles = Array.AsReadOnly(_obstacles);
        }
        public IReadOnlyList<Wall> Walls { get; }
        public IReadOnlyList<Obstacle> Obstacles { get; }
        // Wall-free plazas, both 16 x 16 m; low obstacles provide movement cover only.
        public IReadOnlyList<Wall> Plazas { get; } = Array.AsReadOnly(new[]
        { new Wall(4, 41, 20, 57), new Wall(56, 21, 72, 37) });
        public IReadOnlyList<MapPoint> SpawnPoints { get; } = Array.AsReadOnly(new[]
        {
            new MapPoint(34, 43.5f),
            new MapPoint(44, 1.5f), new MapPoint(48, 1.5f), new MapPoint(52, 1.5f),
            new MapPoint(4, 76), new MapPoint(8, 76), new MapPoint(76, 46), new MapPoint(76, 52)
        });
        public IReadOnlyList<MapPoint> TerminalPositions { get; } = Array.AsReadOnly(new[]
        {
            new MapPoint(10, 25), new MapPoint(58, 27), new MapPoint(64.5f, 74),
            new MapPoint(19, 49), new MapPoint(41, 44), new MapPoint(54.5f, 8),
            new MapPoint(35.5f, 60), new MapPoint(76, 40)
        });
        public IReadOnlyList<MapPoint> Exits { get; } = Array.AsReadOnly(new[]
        { new MapPoint(4, 4), new MapPoint(76, 76) });
        public MapPoint Exit => Exits[0];
        // GDD v2 §3: one switch per exit (index-aligned), ExitSwitchOffset metres from the exit toward the map centre.
        public const float ExitSwitchOffset = 4f;
        public IReadOnlyList<MapPoint> ExitSwitches { get; } = Array.AsReadOnly(new[]
        { SwitchFor(4, 4), SwitchFor(76, 76) });
        // Every hatch point is at least 6 m from every exit and switch (MapV2Tests).
        public IReadOnlyList<MapPoint> HatchPoints { get; } = Array.AsReadOnly(new[]
        { new MapPoint(31, 10), new MapPoint(25, 63), new MapPoint(12, 39), new MapPoint(66, 38.5f), new MapPoint(56, 76) });

        private static MapPoint SwitchFor(float x, float z)
        {
            float dx = Size / 2 - x, dz = Size / 2 - z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            return new MapPoint(x + dx / length * ExitSwitchOffset, z + dz / length * ExitSwitchOffset);
        }

        public TerminalState[] CreateTerminals(int survivors, out byte required)
        {
            GameConstants.TerminalsFor(survivors, out int count, out int placed);
            required = (byte)count;
            var result = new TerminalState[placed];
            for (int i = 0; i < placed; i++)
                result[i] = new TerminalState { Id = (byte)i, X = TerminalPositions[i].X, Z = TerminalPositions[i].Z };
            return result;
        }

        public bool CanStand(float x, float z) => CanStand(x, z, PlayerRadius);

        public bool CanStand(float x, float z, float radius)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || x < radius || z < radius || x > Size - radius || z > Size - radius)
                return false;
            for (int i = 0; i < _walls.Length; i++)
                if (_walls[i].IntersectsCircle(x, z, radius)) return false;
            for (int i = 0; i < _obstacles.Length; i++)
                if (_obstacles[i].IntersectsCircle(x, z, radius)) return false;
            return true;
        }

        // Line of sight (GDD §2): true when the segment touches or crosses any wall, including when an endpoint is
        // inside one. A zero-length segment is never blocked. Walls are not inflated. Allocation-free.
        public bool SegmentBlocked(float ax, float az, float bx, float bz)
        {
            if (ax == bx && az == bz) return false;
            for (int i = 0; i < _walls.Length; i++)
                if (_walls[i].IntersectsSegment(ax, az, bx, bz)) return true;
            return false;
        }

        public MapPoint Move(float x, float z, float dx, float dz)
        {
            // Short substeps prevent tunnelling, including deterministic tests with large dt.
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dz)) / 0.1f));
            dx /= steps;
            dz /= steps;
            for (int i = 0; i < steps; i++)
            {
                if (CanStand(x + dx, z)) x += dx;
                if (CanStand(x, z + dz)) z += dz;
            }
            return new MapPoint(x, z);
        }
    }
}
