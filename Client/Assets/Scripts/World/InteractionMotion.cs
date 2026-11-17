using System;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // U10: the interaction a character shows. Hack (Flags.Hacking), Restore (Flags.Rescuing), Switch (the own exit switch
    // hold, own S2C_SelfState.ActionKind; other Survivors' switch holds are not exposed per player) and Jam (the Killer
    // model's slam, Survivor view, on a terminal's Regressing edge next to the drawn model).
    public enum InteractionKind : byte { None, Hack, Switch, Restore, Jam }

    public static class InteractionTiming
    {
        // Weight ease in and out of an interaction (seconds for 0 -> 1).
        public const float BlendSeconds = .22f;
        // Hack: one keystroke beat per KeySeconds, hands alternating; the panel pulses on each beat.
        public const float KeySeconds = .17f, KeyLift = .045f, PulseSeconds = .12f, PulseScale = .07f;
        // Hack panel, in the character's root space: centre height, distance in front, size and the tilt toward the eyes.
        public const float PanelHeight = 1.18f, PanelForward = .46f, PanelWidth = .62f, PanelTall = .34f, PanelTilt = 32f;
        // Hand goals on the panel: half the spread between the hands, and how far in front of the panel plane they hover.
        public const float HandSpread = .15f, HandHover = .05f;
        // Restore: the hips drop and the torso leans over the ally; both hands reach down to it.
        public const float RestoreDrop = .36f, RestoreLean = 30f, RestoreHandHeight = .32f, RestoreHandSpread = .14f;
        // Exit switch: the right palm on the switch panel at this height; the body leans in.
        public const float SwitchHeight = 1.15f, SwitchLean = 14f, SwitchReach = .55f;
        // Killer jam: one slam (wind-up to JamPeak of the time, impact at JamImpact), the hands above then onto the top.
        public const float JamSeconds = .7f, JamPeak = .38f, JamImpact = .52f, JamTop = .85f, JamRaise = 1.75f, JamDrop = .14f;
        // A terminal turning Regressing counts as this Killer's kick only within this distance of the drawn model.
        public const float JamReach = GameConstants.TerminalRadius + .5f;
        // Data stream: packets travelling terminal -> panel (or rescuer -> ally) per second, and how many are pooled.
        public const int Packets = 6;
        public const float PacketSize = .045f, PacketSeconds = .55f;
        // Killer jam glitch burst: squares, lifetime, spread.
        public const int Shards = 10;
        public const float ShardSize = .07f, ShardSeconds = .4f, ShardSpread = .55f;
    }

    // The flag -> interaction mapping, plain C#. HUD_SPEC 4.10 (11/17, U10): only fields this client receives; the Killer
    // view never reads Hacking or Rescuing (6.3 rule 1), so every Killer-view instance shows None.
    public static class InteractionMap
    {
        public static InteractionKind Resolve(MotionView view, byte status, byte flags, MotionPose pose, float speed, bool own, byte ownAction)
        {
            if (view == MotionView.Killer) return InteractionKind.None;
            if (status != (byte)PlayerStatus.Normal) return InteractionKind.None;
            if (pose != MotionPose.Locomotion && pose != MotionPose.Injured && pose != MotionPose.Working) return InteractionKind.None;
            if (speed >= MotionTiming.StillSpeed) return InteractionKind.None;
            if ((flags & (byte)PlayerFlags.Hacking) != 0) return InteractionKind.Hack;
            if ((flags & (byte)PlayerFlags.Rescuing) != 0) return InteractionKind.Restore;
            if (own && ownAction == (byte)ActionKind.Switch) return InteractionKind.Switch;
            return InteractionKind.None;
        }

        // Index of the nearest point within `reach` of (x, z), or -1.
        public static int Nearest(float x, float z, float[] px, float[] pz, int count, float reach)
        {
            int best = -1; float bestSq = reach * reach;
            for (int i = 0; i < count; i++)
            {
                float dx = px[i] - x, dz = pz[i] - z, sq = dx * dx + dz * dz;
                if (sq <= bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }
    }

    // Survivor view: the Killer model slams once when a terminal within JamReach of it turns Regressing (the end of a kick;
    // Survivors receive that flag and the HUD shows the Regressing tag). Nothing plays while the model is not drawn.
    public sealed class JamTracker
    {
        private bool[] regressing = Array.Empty<bool>();
        private bool known;
        private double startedAt = double.NegativeInfinity;
        public int Count { get; private set; }
        public int Terminal { get; private set; } = -1;

        public void Reset() { known = false; startedAt = double.NegativeInfinity; Count = 0; Terminal = -1; }

        // Feeds the terminals of the newest snapshot. True when a slam starts now.
        public bool Observe(double now, int count, float[] tx, float[] tz, bool[] flags, bool drawn, float killerX, float killerZ)
        {
            if (regressing.Length < count) { Array.Resize(ref regressing, count); known = false; }
            bool started = false;
            for (int i = 0; i < count; i++)
            {
                bool rose = known && flags[i] && !regressing[i];
                regressing[i] = flags[i];
                if (!rose || !drawn) continue;
                float dx = tx[i] - killerX, dz = tz[i] - killerZ;
                if (dx * dx + dz * dz > InteractionTiming.JamReach * InteractionTiming.JamReach) continue;
                startedAt = now; Count++; Terminal = i; started = true;
            }
            known = true;
            return started;
        }

        public bool Active(double now) => now >= startedAt && now - startedAt < InteractionTiming.JamSeconds;
        public double Elapsed(double now) => now - startedAt;
    }

    // One character's interaction layer: eases the IK weight between kinds (out to 0, switch, back in) and drives the
    // InteractionRig (IK) and the pooled HoloPropFx. Created once per character; nothing is allocated per frame.
    public sealed class InteractionMotion
    {
        private readonly InteractionRig rig;
        public InteractionKind Kind { get; private set; }
        public float Weight { get; private set; }
        private double lastAt = double.NaN;

        public InteractionMotion(InteractionRig rig) { this.rig = rig; }

        // Adds the IK rig to a Humanoid Animator (null: tracked only, nothing drawn).
        public static InteractionMotion Attach(Animator animator)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return new InteractionMotion(null);
            InteractionRig rig = animator.GetComponent<InteractionRig>();
            if (rig == null) rig = animator.gameObject.AddComponent<InteractionRig>();
            return new InteractionMotion(rig);
        }

        public bool Rigged => rig != null;

        public void Reset() { Kind = InteractionKind.None; Weight = 0; lastAt = double.NaN; if (rig != null) { rig.Weight = 0; rig.Kind = InteractionKind.None; } }

        // Call every drawn frame before the Animator update. `target` is world space (terminal, ally, switch or terminal top).
        public void Update(double now, InteractionKind wanted, Vector3 target, double clock)
        {
            float dt = double.IsNaN(lastAt) ? 0 : (float)Math.Max(0, Math.Min(.2, now - lastAt));
            lastAt = now;
            float step = dt / InteractionTiming.BlendSeconds;
            if (wanted != Kind)
            {
                if (Kind == InteractionKind.None || Weight <= 0) { Kind = wanted; Weight = wanted == InteractionKind.None ? 0 : Mathf.Min(1, step); }
                else Weight = Mathf.Max(0, Weight - step);
            }
            else if (Kind != InteractionKind.None) Weight = Mathf.Min(1, Weight + step);
            if (rig == null) return;
            rig.Kind = Kind; rig.Weight = Weight; rig.Target = target; rig.Clock = clock;
        }

        // Hack beats: which beat index `clock` is in, and 0..1 into it.
        public static int Beat(double clock, out float phase)
        {
            double beats = Math.Max(0, clock) / InteractionTiming.KeySeconds;
            int index = (int)Math.Floor(beats);
            phase = (float)(beats - index);
            return index;
        }

        // The jam slam envelope at `t` seconds: hand height above the floor and the impact amount (1 at the hit).
        public static float JamHeight(double t)
        {
            float x = (float)(t / InteractionTiming.JamSeconds);
            if (x <= 0 || x >= 1) return InteractionTiming.JamTop;
            if (x < InteractionTiming.JamPeak) return Mathf.Lerp(InteractionTiming.JamTop + .2f, InteractionTiming.JamRaise, Smooth(x / InteractionTiming.JamPeak));
            if (x < InteractionTiming.JamImpact) return Mathf.Lerp(InteractionTiming.JamRaise, InteractionTiming.JamTop, Mathf.Pow((x - InteractionTiming.JamPeak) / (InteractionTiming.JamImpact - InteractionTiming.JamPeak), 2));
            return InteractionTiming.JamTop;
        }

        public static float JamWeight(double t)
        {
            float x = (float)(t / InteractionTiming.JamSeconds);
            if (x <= 0 || x >= 1) return 0;
            return Mathf.Clamp01(Mathf.Min(x / .12f, (1 - x) / .25f));
        }

        private static float Smooth(float x) => x * x * (3 - 2 * x);
    }

    // The IK layer (base layer IK pass, after the clip): poses the hands, the hips and the look for the current kind.
    // ExecuteAlways: editor renders and EditMode tests run it too.
    [ExecuteAlways]
    public sealed class InteractionRig : MonoBehaviour
    {
        public InteractionKind Kind { get; set; }
        public float Weight { get; set; }
        public Vector3 Target { get; set; }
        public double Clock { get; set; }
        private Animator animator;

        private void OnAnimatorIK(int layer)
        {
            if (layer != 0 || Weight <= 0 || Kind == InteractionKind.None) return;
            if (animator == null) animator = GetComponent<Animator>();
            Transform space = transform.parent;
            if (animator == null || space == null) return;
            float w = Weight;
            Vector3 up = space.up, forward = space.forward, right = space.right, origin = space.position;
            switch (Kind)
            {
                case InteractionKind.Hack:
                {
                    // Both hands on a chest-high floating panel, typing: alternating beats lift one hand and press it back.
                    Vector3 panel = origin + up * InteractionTiming.PanelHeight + forward * (InteractionTiming.PanelForward - InteractionTiming.HandHover);
                    int beat = InteractionMotion.Beat(Clock, out float phase);
                    float lift = InteractionTiming.KeyLift * Mathf.Sin(phase * Mathf.PI);
                    bool leftBeat = (beat & 1) == 0;
                    float sway = ((beat * 7) % 5 - 2) * .018f; // hop across the keys, deterministic
                    Vector3 left = panel - right * InteractionTiming.HandSpread - up * .06f + right * (leftBeat ? sway : 0) + up * (leftBeat ? lift : 0);
                    Vector3 rightHand = panel + right * InteractionTiming.HandSpread - up * .06f + right * (leftBeat ? 0 : sway) + up * (leftBeat ? 0 : lift);
                    Hand(AvatarIKGoal.LeftHand, left, w);
                    Hand(AvatarIKGoal.RightHand, rightHand, w);
                    Look(panel + up * .05f, w * .8f);
                    break;
                }
                case InteractionKind.Restore:
                {
                    Vector3 ally = new Vector3(Target.x, origin.y, Target.z);
                    Vector3 toward = ally - origin; toward.y = 0;
                    Vector3 dir = toward.sqrMagnitude > .0001f ? toward.normalized : forward;
                    PinFeet(w);
                    Crouch(w * InteractionTiming.RestoreDrop, dir, w * InteractionTiming.RestoreLean, .12f * w);
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    Vector3 reach = origin + dir * Mathf.Min(.7f, toward.magnitude * .6f) + Vector3.up * InteractionTiming.RestoreHandHeight;
                    Hand(AvatarIKGoal.LeftHand, reach - side * InteractionTiming.RestoreHandSpread, w);
                    Hand(AvatarIKGoal.RightHand, reach + side * InteractionTiming.RestoreHandSpread, w);
                    Look(ally + Vector3.up * .3f, w);
                    break;
                }
                case InteractionKind.Switch:
                {
                    Vector3 at = new Vector3(Target.x, origin.y, Target.z) - origin; at.y = 0;
                    Vector3 dir = at.sqrMagnitude > .0001f ? at.normalized : forward;
                    PinFeet(w);
                    Crouch(w * .05f, dir, w * InteractionTiming.SwitchLean, .08f * w);
                    Vector3 palm = origin + dir * InteractionTiming.SwitchReach + Vector3.up * InteractionTiming.SwitchHeight + Vector3.Cross(Vector3.up, dir) * .08f;
                    Hand(AvatarIKGoal.RightHand, palm, w);
                    Look(palm, w * .7f);
                    break;
                }
                case InteractionKind.Jam:
                {
                    // Clock = seconds since the slam started.
                    Vector3 top = new Vector3(Target.x, origin.y, Target.z) - origin; top.y = 0;
                    Vector3 dir = top.sqrMagnitude > .0001f ? top.normalized : forward;
                    float reachOut = Mathf.Min(.6f, top.magnitude);
                    float height = InteractionMotion.JamHeight(Clock);
                    float impact = Clock >= InteractionTiming.JamSeconds * InteractionTiming.JamImpact ? 1 : 0;
                    PinFeet(w);
                    Crouch(w * InteractionTiming.JamDrop * impact, dir, w * (8 + 14 * impact), .06f * w);
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    Vector3 hands = origin + dir * reachOut + Vector3.up * height;
                    Hand(AvatarIKGoal.LeftHand, hands - side * .16f, w);
                    Hand(AvatarIKGoal.RightHand, hands + side * .16f, w);
                    break;
                }
            }
        }

        private void Hand(AvatarIKGoal goal, Vector3 position, float weight)
        {
            animator.SetIKPosition(goal, position);
            animator.SetIKPositionWeight(goal, weight);
        }

        private void Look(Vector3 at, float weight)
        {
            animator.SetLookAtPosition(at);
            animator.SetLookAtWeight(weight, .25f, .8f, 0, .6f);
        }

        // Keeps both feet where the clip planted them while the hips move.
        private void PinFeet(float weight)
        {
            Vector3 l = animator.GetIKPosition(AvatarIKGoal.LeftFoot), r = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion lr = animator.GetIKRotation(AvatarIKGoal.LeftFoot), rr = animator.GetIKRotation(AvatarIKGoal.RightFoot);
            animator.SetIKPosition(AvatarIKGoal.LeftFoot, l); animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, weight);
            animator.SetIKPosition(AvatarIKGoal.RightFoot, r); animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, weight);
            animator.SetIKRotation(AvatarIKGoal.LeftFoot, lr); animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, weight);
            animator.SetIKRotation(AvatarIKGoal.RightFoot, rr); animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, weight);
        }

        // Lowers the hips by `drop`, moves them `shift` toward `dir` and pitches the torso `lean` degrees over it.
        private void Crouch(float drop, Vector3 dir, float lean, float shift)
        {
            animator.bodyPosition = animator.bodyPosition - Vector3.up * drop + dir * shift;
            Vector3 axis = Vector3.Cross(Vector3.up, dir);
            if (axis.sqrMagnitude > .0001f) animator.bodyRotation = Quaternion.AngleAxis(lean, axis.normalized) * animator.bodyRotation;
        }
    }
}
