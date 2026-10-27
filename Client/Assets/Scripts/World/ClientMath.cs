using UnityEngine;

namespace PingTheKiller.Client.World
{
    public static class ClientMath
    {
        public static Vector3 InputDirection(float right, float forward, float yaw)
        {
            Vector3 input = Vector3.ClampMagnitude(new Vector3(right, 0, forward), 1);
            float radians = yaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
            return new Vector3(input.x * cos + input.z * sin, 0, input.z * cos - input.x * sin);
        }

        public static Vector3 Interpolate(Vector3 a, Vector3 b, double aTime, double bTime, double renderTime)
        {
            float amount = bTime <= aTime ? 1 : Mathf.Clamp01((float)((renderTime - aTime) / (bTime - aTime)));
            return Vector3.LerpUnclamped(a, b, amount);
        }

        public static float InterpolateYaw(float a, float b, double aTime, double bTime, double renderTime)
        {
            float amount = bTime <= aTime ? 1 : Mathf.Clamp01((float)((renderTime - aTime) / (bTime - aTime)));
            return Mathf.LerpAngle(a, b, amount);
        }

        public static float BeepInterval(byte threat) => threat == 1 ? 1.2f : threat == 2 ? .6f : threat == 3 ? .25f : float.PositiveInfinity;
        public static float Fade(double renderTime, double absentTime) => Mathf.Clamp01(1 - (float)((renderTime - absentTime) / .15));
    }
}
