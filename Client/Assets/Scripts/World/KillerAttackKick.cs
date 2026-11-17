using System;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // U4j / U8: the first-person Killer's attack feedback on its own capture (KillerScene's CaptureConfirm). The Killer has
    // no body or hands (HUD_SPEC 6.0), so the view does the swing:
    // - wind-up, WindUpSeconds: the view pulls back PullMetres and the FOV widens WindUpFov (ease-in);
    // - strike: the view lunges PushMetres forward and the FOV narrows FovDegrees, peaking StrikePeak after the wind-up
    //   ends, then eases out by Seconds;
    // - a small trauma shake (game-feel): trauma starts at Trauma and falls linearly to 0 at Seconds; the shake is
    //   trauma squared times summed sines (no per-frame noise), at most ShakeMetres sideways / up and ShakeRoll degrees.
    // Reduced motion (the -ptk-reduce-stutter option or the settings toggle): no kick and no shake at all.
    // R1 (11/17) swing sweep (PlaySwing, a2: on the local press, before the server's verdict): no lunge, no shake; the view
    // yaws SweepDegrees right and swings back past centre over SweepSeconds with a little roll (a first-person arm sweep
    // without arms). A hit adds the capture kick (Play) on top; a miss adds nothing. Reduced motion: none.
    // a2: every term is exactly 0 outside its own window and never NaN (a sweep before any Play used to evaluate the shake at
    // an infinite time: Sin(inf) = NaN put the camera at NaN and blacked the world out).
    //
    // One deterministic path, no coroutine: every LateUpdate (after RoleCamera placed the camera in ClientApp.Update) the
    // camera is set to base + offset, a pure function of the time since Play. The base is the camera as the rig left it;
    // when the rig did not place it this frame (it still holds exactly what the kick wrote), the previous base is kept, so
    // offsets never accumulate. When the kick ends, or the component is disabled or destroyed (KillerWorldLayer.OnDestroy),
    // the camera goes back to its base.
    public sealed class KillerAttackKick : MonoBehaviour
    {
        public const float WindUpSeconds = .07f, StrikePeak = .04f, Seconds = .34f;
        public const float FovDegrees = 5, WindUpFov = 2, PushMetres = .15f, PullMetres = .05f;
        public const float Trauma = .5f, ShakeMetres = .06f, ShakeRoll = 2.5f;
        public const string ReduceFlag = "-ptk-reduce-stutter";
        // The process-wide reduced-motion option; read once from the command line. A settings screen may set it.
        public static bool ReducedMotion { get; set; } = Array.IndexOf(Environment.GetCommandLineArgs(), ReduceFlag) >= 0;

        public const float SweepDegrees = 4f, SweepRoll = 1.5f, SweepSeconds = .3f;
        private double startedAt = double.NegativeInfinity, swingAt = double.NegativeInfinity;
        public int Swings { get; private set; }
        private Camera applied;
        private float baseFov, leftFov;
        private Vector3 basePosition, leftPosition;
        private Quaternion baseRotation, leftRotation;
        public int Played { get; private set; }

        // A capture by the local Killer. False (and nothing changes) with reduced motion on.
        public bool Play() => Play(Time.unscaledTimeAsDouble);

        public bool Play(double now)
        {
            if (ReducedMotion) return false;
            startedAt = now; Played++;
            return true;
        }

        // R1 (11/17) a2: the own swing press was sent (ClientApp, KillerSwingWatch.Predict). False with reduced motion on.
        public bool PlaySwing(double now)
        {
            if (ReducedMotion) return false;
            swingAt = now; Swings++;
            return true;
        }

        // The swing sweep yaw (degrees) at `elapsed` seconds after PlaySwing: one sine period under a falling envelope.
        public static float Sweep(double elapsed, bool reduced)
        {
            if (reduced || !(elapsed >= 0 && elapsed < SweepSeconds)) return 0;
            float x = (float)(elapsed / SweepSeconds);
            return SweepDegrees * Mathf.Sin(2 * Mathf.PI * x) * (1 - x);
        }

        // The swing's roll (degrees), following the sweep.
        public static float SweepRollAt(double elapsed, bool reduced) => Sweep(elapsed, reduced) * (SweepRoll / SweepDegrees);

        // The wind-up, 0..1 (ease-in) over WindUpSeconds; 0 outside.
        public static float WindUp(double elapsed)
        {
            if (elapsed < 0 || elapsed >= WindUpSeconds) return 0;
            float x = (float)(elapsed / WindUpSeconds);
            return x * x;
        }

        // The strike, 0..1: linear rise to 1 at WindUpSeconds + StrikePeak, quadratic ease-out to 0 at Seconds; 0 outside.
        public static float Envelope(double elapsed)
        {
            double s = elapsed - WindUpSeconds;
            if (s < 0 || elapsed >= Seconds) return 0;
            if (s < StrikePeak) return (float)(s / StrikePeak);
            float d = (float)((Seconds - elapsed) / (Seconds - WindUpSeconds - StrikePeak));
            return d * d;
        }

        // The shake trauma at `elapsed`: Trauma at the strike, linear to 0 at Seconds.
        public static float TraumaAt(double elapsed)
        {
            if (elapsed < WindUpSeconds || elapsed >= Seconds) return 0;
            return Trauma * (float)((Seconds - elapsed) / (Seconds - WindUpSeconds));
        }

        // The camera offset at `elapsed` seconds after Play: the FOV change (degrees, negative = narrower) and the forward push.
        public static void Offset(double elapsed, bool reduced, out float fov, out float push)
        {
            if (reduced || !(elapsed >= 0 && elapsed < Seconds)) { fov = 0; push = 0; return; }
            float w = WindUp(elapsed), e = Envelope(elapsed);
            fov = WindUpFov * w - FovDegrees * e; push = PushMetres * e - PullMetres * w;
        }

        // The shake at `elapsed`: sideways and up (metres, camera space) and roll (degrees).
        public static void Shake(double elapsed, bool reduced, out float right, out float up, out float roll)
        {
            float trauma = reduced || !(elapsed >= 0 && elapsed < Seconds) ? 0 : TraumaAt(elapsed);
            if (trauma <= 0) { right = up = roll = 0; return; }
            float shake = trauma * trauma, t = (float)elapsed * 30;
            right = ShakeMetres * shake * Mathf.Sin(t * 1.7f);
            up = ShakeMetres * .7f * shake * Mathf.Sin(t * 2.3f + 1);
            roll = ShakeRoll * shake * Mathf.Sin(t * 1.1f + 2);
        }

        public bool Active(double now) => !ReducedMotion && ((now - startedAt >= 0 && now - startedAt < Seconds) || (now - swingAt >= 0 && now - swingAt < SweepSeconds));

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;

        private void LateUpdate() => Apply(Camera.main, Time.unscaledTimeAsDouble);

        // Sets `camera` to its base plus the offset at `now` (tests call it directly).
        public void Apply(Camera camera, double now)
        {
            if (camera != applied) Restore();
            if (camera == null) return;
            Transform eye = camera.transform;
            bool untouched = camera == applied && camera.fieldOfView == leftFov && eye.position == leftPosition && eye.rotation == leftRotation;
            if (!untouched) { baseFov = camera.fieldOfView; basePosition = eye.position; baseRotation = eye.rotation; }
            double elapsed = now - startedAt;
            Offset(elapsed, ReducedMotion, out float fov, out float push);
            Shake(elapsed, ReducedMotion, out float right, out float up, out float roll);
            float sweep = Finite(Sweep(now - swingAt, ReducedMotion));
            roll = Finite(roll) + Finite(SweepRollAt(now - swingAt, ReducedMotion));
            fov = Finite(fov); push = Finite(push); right = Finite(right); up = Finite(up);
            if (!Active(now) || (fov == 0 && push == 0 && right == 0 && up == 0 && roll == 0 && sweep == 0)) { Restore(); return; }
            camera.fieldOfView = baseFov + fov;
            eye.SetPositionAndRotation(basePosition + baseRotation * new Vector3(right, up, push), baseRotation * Quaternion.Euler(0, sweep, roll));
            applied = camera; leftFov = camera.fieldOfView; leftPosition = eye.position; leftRotation = eye.rotation;
        }

        // Puts the camera back to its base if it still holds what the kick wrote (a rig placement in between wins).
        // KillerWorldLayer.OnDestroy calls it before destroying the component; OnDisable covers every other teardown.
        public void Restore()
        {
            if (applied != null && applied.fieldOfView == leftFov && applied.transform.position == leftPosition && applied.transform.rotation == leftRotation)
            {
                applied.fieldOfView = baseFov;
                applied.transform.SetPositionAndRotation(basePosition, baseRotation);
            }
            applied = null;
        }

        private void OnDisable() => Restore();
    }
}
