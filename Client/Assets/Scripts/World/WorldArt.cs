using System;
using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // All imported world art enters the player through this Resources catalog (built by WorldArtSetup). Every wall and
    // obstacle piece records the FixedMap box it was fitted to, so a catalog built for another map is never drawn: WorldProps
    // falls back to the primitives instead.
    public sealed class WorldArt : ScriptableObject
    {
        [Serializable]
        public struct Piece
        {
            public GameObject Prefab;
            // The FixedMap box the prefab fills: x = MinX, y = MinZ, z = MaxX, w = MaxZ.
            public Vector4 Box;
        }

        // Index = FixedMap.Walls / FixedMap.Obstacles index. Prefab origins sit at the box centre on the floor.
        public Piece[] Walls = new Piece[0];
        public Piece[] Obstacles = new Piece[0];
        // Floor (roads, pavement, plazas, markings; nothing above 0.15 m) and the skyline outside the border walls.
        public GameObject Ground, Backdrop;
        // Terminal: origin at the server position; its renderer named ScreenName carries the state material.
        public GameObject Terminal;
        // Shown on an Exit only while it is open; tinted per role at runtime.
        public GameObject ExitMarker;

        public const string ScreenName = "Screen";
        public const float FitTolerance = .01f;

        public static WorldArt Load() => Resources.Load<WorldArt>("WorldArt");

        // True when every piece exists and was fitted to exactly this map's boxes.
        public bool Matches(FixedMap map)
        {
            if (Ground == null || Terminal == null || ExitMarker == null) return false;
            if (Walls == null || Walls.Length != map.Walls.Count || Obstacles == null || Obstacles.Length != map.Obstacles.Count) return false;
            for (int i = 0; i < Walls.Length; i++)
            {
                Wall wall = map.Walls[i];
                if (Walls[i].Prefab == null || !Same(Walls[i].Box, wall.MinX, wall.MinZ, wall.MaxX, wall.MaxZ)) return false;
            }
            for (int i = 0; i < Obstacles.Length; i++)
            {
                Obstacle obstacle = map.Obstacles[i];
                if (Obstacles[i].Prefab == null || !Same(Obstacles[i].Box, obstacle.MinX, obstacle.MinZ, obstacle.MaxX, obstacle.MaxZ)) return false;
            }
            return true;
        }

        private static bool Same(Vector4 box, float minX, float minZ, float maxX, float maxZ)
        {
            return Mathf.Abs(box.x - minX) <= FitTolerance && Mathf.Abs(box.y - minZ) <= FitTolerance
                && Mathf.Abs(box.z - maxX) <= FitTolerance && Mathf.Abs(box.w - maxZ) <= FitTolerance;
        }
    }
}
