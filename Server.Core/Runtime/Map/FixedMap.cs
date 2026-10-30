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
        public const float Size = 60;
        public const float PlayerRadius = 0.4f;
        private readonly Wall[] _walls =
        {
            new Wall(-1, -1, 0, 61), new Wall(60, -1, 61, 61),
            new Wall(0, -1, 60, 0), new Wall(0, 60, 60, 61),
            new Wall(18, 14, 20, 32), new Wall(36, 28, 38, 47),
            new Wall(24, 42, 32, 44), new Wall(40, 14, 49, 16)
        };
        public FixedMap() { Walls = Array.AsReadOnly(_walls); }
        public IReadOnlyList<Wall> Walls { get; }
        public IReadOnlyList<MapPoint> SpawnPoints { get; } = Array.AsReadOnly(new[]
        {
            // Killer first, then seven survivor spawns strictly beyond the threat band.
            new MapPoint(5, 5), new MapPoint(5, 26), new MapPoint(10, 26), new MapPoint(5, 36),
            new MapPoint(10, 36), new MapPoint(45, 50), new MapPoint(50, 50), new MapPoint(45, 55)
        });
        public IReadOnlyList<MapPoint> TerminalPositions { get; } = Array.AsReadOnly(new[]
        {
            new MapPoint(10, 25), new MapPoint(28, 12), new MapPoint(49, 25),
            new MapPoint(12, 49), new MapPoint(30, 52), new MapPoint(50, 42)
        });
        public MapPoint Exit { get; } = new MapPoint(58, 30);

        public TerminalState[] CreateTerminals(int survivors, out byte required)
        {
            GameConstants.TerminalsFor(survivors, out int count, out int placed);
            required = (byte)count;
            var result = new TerminalState[placed];
            for (int i = 0; i < placed; i++)
                result[i] = new TerminalState { Id = (byte)i, X = TerminalPositions[i].X, Z = TerminalPositions[i].Z };
            return result;
        }

        public bool CanStand(float x, float z)
        {
            foreach (var wall in Walls)
                if (wall.IntersectsCircle(x, z, PlayerRadius)) return false;
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
