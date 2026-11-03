using System;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // U4j: the first-person Killer's attack feedback on its own capture (KillerScene's CaptureConfirm). The Killer has no
    // body or hands (HUD_SPEC 6.0), so a capture gives a short lunge of the view: the vertical FOV narrows by FovDegrees and
    // the eye moves PushMetres forward, rising in 20 % of Seconds and easing out by Seconds (0.22 s). Reduced motion (the
    // -ptk-reduce-stutter option, later the settings toggle): no kick at all.
    //
    // One deterministic path, no coroutine: every LateUpdate (after RoleCamera placed the camera in ClientApp.Update) the
    // camera is set to base + offset. The base is the camera as the rig left it; when the rig did not place it this frame
    // (it still holds exactly what the kick wrote), the previous base is kept, so offsets never accumulate. When the kick
    // ends, or the component is disabled or destroyed (KillerWorldLayer.OnDestroy), the camera goes back to its base.
    public sealed class KillerAttackKick : MonoBehaviour
    {
        public const float Seconds = .22f, FovDegrees = 4, PushMetres = .12f;
        public const string ReduceFlag = "-ptk-reduce-stutter";
        // The process-wide reduced-motion option; read once from the command line. A settings screen may set it.
        public static bool ReducedMotion { get; set; } = Array.IndexOf(Environment.GetCommandLineArgs(), ReduceFlag) >= 0;

        private double startedAt = double.NegativeInfinity;
        private Camera applied;
        private float baseFov, leftFov;
        private Vector3 basePosition, leftPosition;
        public int Played { get; private set; }

        // A capture by the local Killer. False (and nothing changes) with reduced motion on.
        public bool Play() => Play(Time.unscaledTimeAsDouble);

        public bool Play(double now)
        {
            if (ReducedMotion) return false;
            startedAt = now; Played++;
            return true;
        }

        // 0..1: linear rise over the first 20 %, quadratic ease-out to 0 at Seconds; 0 outside.
        public static float Envelope(double elapsed)
        {
            if (elapsed < 0 || elapsed >= Seconds) return 0;
            float x = (float)(elapsed / Seconds);
            if (x < .2f) return x / .2f;
            float d = (1 - x) / .8f;
            return d * d;
        }

        // The camera offset at `elapsed` seconds after Play: the FOV change (degrees, negative = narrower) and the forward push.
        public static void Offset(double elapsed, bool reduced, out float fov, out float push)
        {
            float e = reduced ? 0 : Envelope(elapsed);
            fov = -FovDegrees * e; push = PushMetres * e;
        }

        public bool Active(double now) => !ReducedMotion && Envelope(now - startedAt) > 0;

        private void LateUpdate() => Apply(Camera.main, Time.unscaledTimeAsDouble);

        // Sets `camera` to its base plus the offset at `now` (tests call it directly).
        public void Apply(Camera camera, double now)
        {
            if (camera != applied) Restore();
            if (camera == null) return;
            bool untouched = camera == applied && camera.fieldOfView == leftFov && camera.transform.position == leftPosition;
            if (!untouched) { baseFov = camera.fieldOfView; basePosition = camera.transform.position; }
            Offset(now - startedAt, ReducedMotion, out float fov, out float push);
            if (fov == 0 && push == 0) { Restore(); return; }
            camera.fieldOfView = baseFov + fov;
            camera.transform.position = basePosition + camera.transform.forward * push;
            applied = camera; leftFov = camera.fieldOfView; leftPosition = camera.transform.position;
        }

        // Puts the camera back to its base if it still holds what the kick wrote (a rig placement in between wins).
        // KillerWorldLayer.OnDestroy calls it before destroying the component; OnDisable covers every other teardown.
        public void Restore()
        {
            if (applied != null && applied.fieldOfView == leftFov && applied.transform.position == leftPosition)
            {
                applied.fieldOfView = baseFov;
                applied.transform.position = basePosition;
            }
            applied = null;
        }

        private void OnDisable() => Restore();
    }
}
