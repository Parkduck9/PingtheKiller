using UnityEngine;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 6.0: first-person Killer camera. Eye 1.7 m, yaw free, pitch -70..+70, horizontal FOV 90 at 16:9
    // (vertical 58.7, Hor+). No body, no hands, no crosshair: nothing but the camera exists for the Killer.
    public sealed class KillerViewRig
    {
        public const float EyeHeight = 1.7f;
        public const float MaxPitch = 70;
        public static readonly float VerticalFov = ViewPose.VerticalFromHorizontal(90, 16f / 9);
        public float Pitch { get; private set; }

        public void Reset() => Pitch = 0;
        public void LookPitch(float delta) => Pitch = Mathf.Clamp(Pitch - delta, -MaxPitch, MaxPitch);

        public ViewPose Place(Camera camera, float yaw, Vector3 ground)
        {
            var pose = new ViewPose(new Vector3(ground.x, EyeHeight, ground.z), yaw, Pitch, VerticalFov, camera.aspect);
            camera.fieldOfView = VerticalFov;
            camera.transform.SetPositionAndRotation(pose.Eye, pose.Rotation);
            return pose;
        }
    }
}
