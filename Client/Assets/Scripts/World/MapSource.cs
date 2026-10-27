using UnityEngine;

namespace PingTheKiller.Client.World
{
    // Mirrors U2a Server.Core/Runtime/Map/FixedMap.cs: the map spans [0, 60] on x and z.
    // TODO(U4b): share the map with Server.Core instead of copying it.
    public static class MapSource
    {
        public const float Size = 60;
        public static readonly Vector3 Center = new Vector3(30, 0, 30);
        public static readonly Vector3 Exit = new Vector3(58, .04f, 30);
        // x/z center, x/z size (FixedMap min/max boxes).
        public static readonly Vector4[] Walls =
        {
            new Vector4(-.5f, 30, 1, 62), new Vector4(60.5f, 30, 1, 62),
            new Vector4(30, -.5f, 60, 1), new Vector4(30, 60.5f, 60, 1),
            new Vector4(19, 23, 2, 18), new Vector4(37, 37.5f, 2, 19),
            new Vector4(28, 43, 8, 2), new Vector4(44.5f, 15, 9, 2)
        };
        public static readonly Vector3[] Terminals =
        {
            new Vector3(10, 0, 25), new Vector3(28, 0, 12), new Vector3(49, 0, 25),
            new Vector3(12, 0, 49), new Vector3(30, 0, 52), new Vector3(50, 0, 42)
        };
    }
}
