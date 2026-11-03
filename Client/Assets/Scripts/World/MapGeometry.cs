using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // Analytic queries against Server.Core's FixedMap walls. The world has no colliders (positions are server-owned),
    // so the camera boom and the Killer's occlusion ray test the wall boxes directly.
    public static class MapGeometry
    {
        // Rendered wall height (WorldView builds every wall this tall).
        public const float WallHeight = 3f;
        public const float ObstacleHeight = 1.2f;

        // HUD_SPEC 6.2 / N8: walls are full height for occlusion, so a 2D segment test is enough.
        public static bool SegmentBlocked(FixedMap map, float x0, float z0, float x1, float z1)
        {
            return map.SegmentBlocked(x0, z0, x1, z1);
        }

        // Slab test of the segment (x0,z0)->(x1,z1) against an axis-aligned box. t = entry fraction in [0, 1].
        public static bool SegmentHitsBox(float x0, float z0, float x1, float z1, float minX, float minZ, float maxX, float maxZ, out float t)
        {
            float enter = 0, exit = 1;
            t = 0;
            if (!Slab(x0, x1 - x0, minX, maxX, ref enter, ref exit)) return false;
            if (!Slab(z0, z1 - z0, minZ, maxZ, ref enter, ref exit)) return false;
            t = enter;
            return true;
        }

        // HUD_SPEC 4.10: a sphere of the given radius cast from origin along the unit direction, against the wall boxes
        // (0 to WallHeight) and the floor. Returns how far the sphere centre travels before contact (maxDistance when clear).
        public static float SphereCast(FixedMap map, Vector3 origin, Vector3 direction, float maxDistance, float radius)
        {
            float best = maxDistance;
            foreach (Wall wall in map.Walls)
            {
                float enter = 0, exit = maxDistance;
                if (!Slab(origin.x, direction.x, wall.MinX - radius, wall.MaxX + radius, ref enter, ref exit)) continue;
                if (!Slab(origin.z, direction.z, wall.MinZ - radius, wall.MaxZ + radius, ref enter, ref exit)) continue;
                if (!Slab(origin.y, direction.y, float.NegativeInfinity, WallHeight + radius, ref enter, ref exit)) continue;
                if (enter < best) best = enter;
            }
            foreach (Obstacle obstacle in map.Obstacles)
            {
                float enter = 0, exit = maxDistance;
                if (!Slab(origin.x, direction.x, obstacle.MinX - radius, obstacle.MaxX + radius, ref enter, ref exit)) continue;
                if (!Slab(origin.z, direction.z, obstacle.MinZ - radius, obstacle.MaxZ + radius, ref enter, ref exit)) continue;
                if (!Slab(origin.y, direction.y, -radius, ObstacleHeight + radius, ref enter, ref exit)) continue;
                if (enter < best) best = enter;
            }
            // The floor: the sphere may not sink below y = radius (keeps the camera above the ground at low pitch).
            if (direction.y < -1e-6f)
            {
                float floor = (origin.y - radius) / -direction.y;
                if (floor < best) best = floor;
            }
            return Mathf.Max(0, best);
        }

        private static bool Slab(float origin, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) < 1e-7f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            if (a > b) { float swap = a; a = b; b = swap; }
            if (a > enter) enter = a;
            if (b < exit) exit = b;
            return enter <= exit;
        }
    }
}
