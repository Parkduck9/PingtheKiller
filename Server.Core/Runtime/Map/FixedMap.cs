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
            // Two U-shaped blocks with 4 m dead-end alleys, open to the south.
            new Wall(12, 12, 24, 36), new Wall(28, 12, 40, 36), new Wall(12, 36, 40, 42),
            new Wall(12, 46, 24, 68), new Wall(28, 46, 40, 68), new Wall(12, 68, 40, 74),
            // Eastern blocks and the central service building. Building gaps are 4 m.
            new Wall(56, 10, 66, 20), new Wall(56, 24, 66, 34), new Wall(56, 38, 66, 48),
            new Wall(56, 52, 66, 62), new Wall(56, 66, 66, 76), new Wall(44, 34, 52, 42)
        };
        private readonly Obstacle[] _obstacles =
        {
            new Obstacle(45, 14, 47, 16), new Obstacle(50, 27.5f, 52, 29.5f),
            new Obstacle(45, 50, 47, 52), new Obstacle(50, 56, 52, 58),
            new Obstacle(4, 52, 6, 54), new Obstacle(4, 64, 6, 66),
            new Obstacle(44, 4, 48, 6), new Obstacle(44, 74, 48, 76),
            new Obstacle(25, 6, 27, 8), new Obstacle(72, 16, 74, 18),
            new Obstacle(72, 52, 74, 54), new Obstacle(70, 74, 72, 76)
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
        { new Wall(40, 12, 56, 28), new Wall(40, 48, 56, 64) });
        public IReadOnlyList<MapPoint> SpawnPoints { get; } = Array.AsReadOnly(new[]
        {
            new MapPoint(46, 45),
            new MapPoint(8, 12), new MapPoint(8, 16), new MapPoint(8, 20),
            new MapPoint(26, 50), new MapPoint(26, 54), new MapPoint(72, 68), new MapPoint(74, 70)
        });
        public IReadOnlyList<MapPoint> TerminalPositions { get; } = Array.AsReadOnly(new[]
        {
            new MapPoint(10, 25), new MapPoint(72, 44), new MapPoint(49, 25),
            new MapPoint(8, 46), new MapPoint(48, 60), new MapPoint(26, 28),
            new MapPoint(26, 60), new MapPoint(72, 60)
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
        { new MapPoint(8, 38), new MapPoint(26, 16), new MapPoint(48, 46), new MapPoint(70, 8), new MapPoint(76, 30) });

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
