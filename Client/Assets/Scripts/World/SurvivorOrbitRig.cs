using UnityEngine;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 4.10: third-person orbit camera around a look target 0.7 m above the own Survivor. Yaw is free (mouse or
    // right stick only), pitch -10..+60 (default +20), boom 4 m, vertical FOV 50 (Hor+). The boom is pulled in by a 0.2 m
    // sphere cast against the static walls and eases back out over 150 ms. Presentation only: nothing here is sent.
    public sealed class SurvivorOrbitRig
    {
        public const float TargetHeight = .7f;
        public const float Boom = 4f;
        public const float MinPitch = -10, MaxPitch = 60, DefaultPitch = 20;
        public const float VerticalFov = 50;
        public const float ProbeRadius = .2f;
        public const float EaseSeconds = .15f;
        public const float DitherBelow = 1f;
        private readonly BoomFollow boom = new BoomFollow(Boom, EaseSeconds);
        public float Pitch { get; private set; } = DefaultPitch;
        public float BoomLength => boom.Length;
        // Below 1 m the own character is drawn with the screen-door dither so it never blocks the view.
        public bool Dither => boom.Length < DitherBelow;

        public void Reset() { Pitch = DefaultPitch; boom.Reset(); }
        public void LookPitch(float delta) => Pitch = Mathf.Clamp(Pitch - delta, MinPitch, MaxPitch);

        public static Vector3 Target(Vector3 ground) => new Vector3(ground.x, TargetHeight, ground.z);

        // Boom direction (target -> camera) for a yaw and pitch.
        public static Vector3 Back(float yaw, float pitch) => Quaternion.Euler(pitch, yaw, 0) * Vector3.back;

        public ViewPose Place(Camera camera, float yaw, Vector3 ground, float dt)
        {
            Vector3 target = Target(ground), back = Back(yaw, Pitch);
            float clear = MapGeometry.SphereCast(SharedMap.Map, target, back, Boom, ProbeRadius);
            float length = boom.Update(clear, dt);
            var pose = new ViewPose(target + back * length, yaw, Pitch, VerticalFov, camera.aspect);
            camera.fieldOfView = VerticalFov;
            camera.transform.SetPositionAndRotation(pose.Eye, pose.Rotation);
            return pose;
        }
    }

    // Boom length: shortens at once to the clear distance and eases back out (ease-out) over a fixed time.
    public sealed class BoomFollow
    {
        private readonly float full, ease;
        private float from, elapsed;
        public float Length { get; private set; }

        public BoomFollow(float full, float easeSeconds) { this.full = full; ease = easeSeconds; Reset(); }

        public void Reset() { Length = from = full; elapsed = 0; }

        public float Update(float clear, float dt)
        {
            if (!(dt > 0) || float.IsInfinity(dt)) dt = 0;
            float target = Mathf.Clamp(float.IsNaN(clear) ? full : clear, 0, full);
            if (target < Length - 1e-5f)
            {
                Length = from = target; elapsed = 0; // pull in at once
            }
            else if (target > Length + 1e-5f)
            {
                elapsed += dt;
                float k = ease > 0 ? Mathf.Clamp01(elapsed / ease) : 1;
                Length = k >= 1 ? target : Mathf.Lerp(from, target, 1 - (1 - k) * (1 - k));
                if (k >= 1) { from = Length; elapsed = 0; }
            }
            else { Length = target; from = Length; elapsed = 0; }
            return Length;
        }
    }

    // HUD_SPEC 4.10: the own character turns toward the movement direction at 720 degrees per second. Standing still
    // keeps the facing (so orbiting never turns it); while hacking it faces the terminal.
    public sealed class CharacterFacing
    {
        public const float TurnRate = 720;
        public float Yaw { get; private set; }

        public void Reset(float yaw) => Yaw = Mathf.Repeat(yaw, 360);

        // move = world move direction (zero when standing still). faceTarget: an optional point to face instead (hacking).
        public float Update(Vector3 move, float dt, Vector3? faceFrom = null, Vector3? faceTarget = null)
        {
            if (!(dt > 0) || float.IsInfinity(dt)) dt = 0;
            float? goal = null;
            if (move.x * move.x + move.z * move.z > 1e-6f) goal = Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg;
            else if (faceFrom.HasValue && faceTarget.HasValue)
            {
                Vector3 d = faceTarget.Value - faceFrom.Value;
                if (d.x * d.x + d.z * d.z > 1e-4f) goal = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            if (goal.HasValue) Yaw = Mathf.Repeat(Mathf.MoveTowardsAngle(Yaw, goal.Value, TurnRate * dt), 360);
            return Yaw;
        }
    }
}
