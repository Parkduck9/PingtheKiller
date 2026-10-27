using UnityEngine;

namespace PingTheKiller.Client.World
{
    // The camera as plain numbers (eye, yaw, pitch, vertical FOV, aspect). The rigs place the Unity camera from exactly
    // these values, so pure code (classification, edge indicators, tests) projects the same way the camera renders.
    public readonly struct ViewPose
    {
        public const float Near = .05f;
        public readonly Vector3 Eye;
        public readonly float Yaw, Pitch, VerticalFov, Aspect;
        private readonly Quaternion rotation, inverse;
        private readonly float tanHalf;

        public ViewPose(Vector3 eye, float yaw, float pitch, float verticalFov, float aspect)
        {
            Eye = eye; Yaw = yaw; Pitch = pitch; VerticalFov = verticalFov; Aspect = aspect > 0 ? aspect : 16f / 9;
            rotation = Quaternion.Euler(pitch, yaw, 0);
            inverse = Quaternion.Inverse(rotation);
            tanHalf = Mathf.Tan(verticalFov * .5f * Mathf.Deg2Rad);
        }

        public Quaternion Rotation => rotation;

        // Camera space: x right, y up, z forward.
        public Vector3 ToCamera(Vector3 world) => inverse * (world - Eye);

        // Viewport (0..1, origin bottom-left). False behind the near plane.
        public bool ToViewport(Vector3 world, out Vector2 viewport)
        {
            Vector3 c = ToCamera(world);
            if (c.z <= Near) { viewport = default; return false; }
            viewport = new Vector2(.5f + c.x / (c.z * tanHalf * Aspect) * .5f, .5f + c.y / (c.z * tanHalf) * .5f);
            return true;
        }

        public bool InFrustum(Vector3 world)
            => ToViewport(world, out Vector2 v) && v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;

        // Hor+: the vertical FOV for a horizontal FOV at a reference aspect (HUD_SPEC 6.0: 90 degrees at 16:9 -> 58.7).
        public static float VerticalFromHorizontal(float horizontal, float aspect)
            => 2 * Mathf.Atan(Mathf.Tan(horizontal * .5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;

        // World size of one screen pixel at a camera-space depth (for the 1 px outline hull).
        public float PixelSize(float depth, float screenHeight) => depth * 2 * tanHalf / Mathf.Max(1, screenHeight);
    }
}
