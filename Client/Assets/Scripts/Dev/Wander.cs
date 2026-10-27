using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.Dev
{
    // Dev survivor autopilot (-ptk-wander): the caller holds W while this turns the yaw slowly. When a wall is within
    // LookAhead in front, the heading reverses and the turn direction flips. Pure: position in, yaw out.
    public sealed class Wander
    {
        public const float TurnRate = 25; // degrees per second
        public const float LookAhead = 2; // metres
        private const float Step = .25f;
        private int turn = 1;
        public float Yaw { get; private set; }

        public Wander(float yaw) { Yaw = Mathf.Repeat(yaw, 360); }

        public float Next(FixedMap map, float x, float z, float dt)
        {
            if (!(dt > 0) || float.IsInfinity(dt)) dt = 0;
            Yaw = Mathf.Repeat(Yaw + turn * TurnRate * dt, 360);
            if (!WallAhead(map, x, z, Yaw)) return Yaw;
            turn = -turn;
            float reversed = Mathf.Repeat(Yaw + 180, 360);
            // In a corner the reversed heading can be blocked too: sweep in 30 degree steps to the first clear one.
            for (int i = 0; i < 12 && WallAhead(map, x, z, reversed); i++) reversed = Mathf.Repeat(reversed + turn * 30, 360);
            return Yaw = reversed;
        }

        // Yaw 0 faces +z and 90 faces +x (ClientMath.InputDirection). CanStand includes the player radius.
        public static bool WallAhead(FixedMap map, float x, float z, float yaw)
        {
            float radians = yaw * Mathf.Deg2Rad, dx = Mathf.Sin(radians), dz = Mathf.Cos(radians);
            for (float distance = Step; distance <= LookAhead + .0001f; distance += Step)
                if (!map.CanStand(x + dx * distance, z + dz * distance)) return true;
            return false;
        }
    }
}
