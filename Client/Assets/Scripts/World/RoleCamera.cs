using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PingTheKiller.Client.World
{
    // Owns the one Unity camera and picks the role's rig: SurvivorOrbitRig (third person) or KillerViewRig (first
    // person). Yaw is the camera yaw for both; the Survivor's movement is relative to it, the character's facing is not.
    public sealed class RoleCamera
    {
        public const float Sensitivity = .12f; // degrees per mouse count
        private readonly Camera camera;
        private readonly SurvivorOrbitRig orbit = new SurvivorOrbitRig();
        private readonly KillerViewRig eye = new KillerViewRig();
        private Role role;
        public float Yaw { get; private set; }
        public ViewPose Pose { get; private set; }
        // Survivor only: the boom is shorter than 1 m, so the own character is drawn dithered.
        public bool Dither => role == Role.Survivor && orbit.Dither;
        public RoleCamera(Camera camera) { this.camera = camera; }
        public void Begin(Role value)
        {
            role = value; Yaw = 0;
            orbit.Reset(); eye.Reset();
            camera.nearClipPlane = ViewPose.Near;
            camera.farClipPlane = 180;
            camera.orthographic = false;
            // Until the first own position arrives the camera waits at the map centre at eye height.
            camera.transform.SetPositionAndRotation(SharedMap.Center + Vector3.up * KillerViewRig.EyeHeight, Quaternion.identity);
            SetCursor(true);
        }
        // Both roles use mouse look, so both capture the cursor while playing.
        public void SetCursor(bool playing)
        {
            bool locked = playing && Application.isFocused;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
        public void Look(bool active)
        {
            if (!active || Cursor.lockState != CursorLockMode.Locked || Mouse.current == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue() * Sensitivity;
            Yaw = Mathf.Repeat(Yaw + delta.x, 360);
            if (role == Role.Survivor) orbit.LookPitch(delta.y); else eye.LookPitch(delta.y);
        }
        // Scripted yaw (dev wander autopilot, orbit and killer-look shots); mouse look keeps adding to it.
        public void Turn(float yaw) => Yaw = Mathf.Repeat(yaw, 360);
        // ground = the own character's position (y ignored).
        public void Place(Vector3 ground, float dt)
        {
            Pose = role == Role.Survivor ? orbit.Place(camera, Yaw, ground, dt) : eye.Place(camera, Yaw, ground);
        }
    }
}
