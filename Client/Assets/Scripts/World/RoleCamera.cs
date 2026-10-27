using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PingTheKiller.Client.World
{
    public sealed class RoleCamera
    {
        private readonly Camera camera;
        private Role role;
        private float pitch;
        public float Yaw { get; private set; }
        public RoleCamera(Camera camera) { this.camera = camera; }
        public void Begin(Role value)
        {
            role = value; Yaw = 0; pitch = 0;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 180;
            camera.orthographic = false;
            SetCursor(true);
        }
        public void SetCursor(bool playing)
        {
            bool locked = playing && role == Role.Survivor && Application.isFocused;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
        public void Look(bool active)
        {
            if (!active || role != Role.Survivor || Cursor.lockState != CursorLockMode.Locked || Mouse.current == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue() * .12f;
            Yaw = Mathf.Repeat(Yaw + delta.x, 360);
            pitch = Mathf.Clamp(pitch - delta.y, -85, 85);
        }
        public void Place(Vector3 localPosition)
        {
            if (role == Role.Killer)
            {
                camera.transform.SetPositionAndRotation(MapSource.Center + Vector3.up * 35, Quaternion.Euler(90, 0, 0));
                camera.fieldOfView = 2 * Mathf.Atan(34 / (35 * Mathf.Min(1, camera.aspect))) * Mathf.Rad2Deg;
            }
            else
            {
                camera.fieldOfView = 75;
                camera.transform.SetPositionAndRotation(new Vector3(localPosition.x, 1.6f, localPosition.z), Quaternion.Euler(pitch, Yaw, 0));
            }
        }
    }
}
