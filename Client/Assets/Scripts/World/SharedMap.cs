using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // The client draws Server.Core's own FixedMap, so the rendered map and the server's collision map cannot drift.
    // The map spans [0, Size] on x and z.
    public static class SharedMap
    {
        public static readonly FixedMap Map = new FixedMap();
        public static Vector3 Center => new Vector3(FixedMap.Size * .5f, 0, FixedMap.Size * .5f);
        public static Vector3 Exit => new Vector3(Map.Exit.X, .04f, Map.Exit.Z);

        // FixedMap walls are min/max boxes; Unity cubes want a centre and a size.
        public static void WallBox(Wall wall, out Vector3 center, out Vector3 size)
        {
            center = new Vector3((wall.MinX + wall.MaxX) * .5f, 0, (wall.MinZ + wall.MaxZ) * .5f);
            size = new Vector3(wall.MaxX - wall.MinX, 0, wall.MaxZ - wall.MinZ);
        }
    }
}
