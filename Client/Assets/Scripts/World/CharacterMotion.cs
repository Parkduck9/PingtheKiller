using System;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // Motion.controller names (built by Editor/Setup/MotionSetup).
    public static class MotionStates
    {
        public const string SpeedParameter = "Speed", ActionLayer = "Action";
        public const string Locomotion = "Locomotion", Injured = "Injured", Hit = "Hit", KnockedDown = "KnockedDown", Crawl = "Crawl";
        public const string GettingUp = "GettingUp", Death = "Death", Working = "ButtonPushing";
        public const string Empty = "Empty", Swipe = "Swipe";
        // Clips used only inside the Injured blend tree.
        public const string InjuredIdle = "InjuredIdle", InjuredWalk = "InjuredWalk";
    }

    public static class MotionTiming
    {
        // CharacterInstance.Animate advances a Lagging character at this rate (stepped at 8 Hz in the Killer view).
        public const float LaggingRate = .55f;
        // Ground speed of the in-place Crawling clip at rate 1 (measured from its planted hands and knees).
        public const float CrawlMetresPerSecond = .47f;
        // HitReaction plays this long before it blends into KnockedDown (the first part of the capture reaction).
        public const float HitSeconds = .3f;
        // Imported clip lengths (CharacterMotionTests checks them against the assets).
        public const float KnockedDownSeconds = 2.133f, GettingUpSeconds = 2.7f, DeathSeconds = 2.6f;
        // U8: the Killer swipe is trimmed on import to the Mixamo take from SwipeFromSeconds for SwipeClipSeconds (40
        // frames). It is not played at rate 1: SwipeClipTime maps the real seconds since the capture onto the clip.
        // Keys chosen from every imported frame (docs/qa/U8-screens/a2/swipe-frames.png, right-hand speed in
        // docs/qa/U8-runs/a2-swipe-frames.log): the arm is cocked high from clip 0.40 s, the cock ends at 0.90 s, the hand
        // peaks at 9.5 m/s over 0.90-1.03 s, the strike pose is at 1.03 s, the follow-through settles by 1.33 s.
        // On screen: a fast 0.15 s wind-up (0.40 -> 0.90), the strike reaching contact at SwipeContactSeconds (0.27 s),
        // the strike pose held SwipeHoldSeconds, then a slow follow-through (0.4x) to SwipeSeconds.
        public const float SwipeFromSeconds = .3f, SwipeClipSeconds = 1.3333f;
        public static readonly float[] SwipeRealKeys = { 0, .15f, .27f, .39f, 1.15f };
        public static readonly float[] SwipeClipKeys = { .4f, .9f, 1.0333f, 1.0333f, 1.3333f };
        public const float SwipeStrikeAt = .27f, SwipeContactSeconds = .27f, SwipeHoldSeconds = .12f, SwipeSeconds = 1.15f;

        // The swipe clip time (seconds) shown `elapsed` real seconds after the capture; clamped to the clip.
        public static float SwipeClipTime(double elapsed)
        {
            if (elapsed <= 0) return SwipeClipKeys[0];
            for (int i = 1; i < SwipeRealKeys.Length; i++)
            {
                if (elapsed > SwipeRealKeys[i]) continue;
                float span = SwipeRealKeys[i] - SwipeRealKeys[i - 1];
                float x = span <= 0 ? 1 : (float)(elapsed - SwipeRealKeys[i - 1]) / span;
                return SwipeClipKeys[i - 1] + (SwipeClipKeys[i] - SwipeClipKeys[i - 1]) * x;
            }
            return SwipeClipKeys[SwipeClipKeys.Length - 1];
        }
        // An eliminated body stays on the ground this long after the fall started, then despawns (Survivor view).
        public const float DeathShownSeconds = 6;
        // "Speed about 0": below this measured speed a hacking Survivor plays ButtonPushing.
        public const float StillSpeed = .3f;
        // ButtonPushing on Rio presses at 0.80..1.02 m (a panel at chest height); the terminal top is 0.72 m with its
        // devices on it. A right-hand IK goal ButtonDrop lower, never below ButtonFloor, puts the press on the devices.
        public const float ButtonDrop = .3f, ButtonFloor = .74f, ButtonReachIn = .25f;
        // Down poses keep the hips within this radius of the authoritative position (see MotionGrounding).
        public const float HipsRadius = .1f;
        // The Killer-view Lagging tag over a down body: this far above the head bone, kept within TagMin..standing height.
        public const float TagAboveHead = .25f, TagMin = .7f;
    }

    // The Mixamo clips (Art/Characters/Mixamo/Mixamo@<File>.fbx) and the Motion.controller state each one drives.
    public static class MotionLibrary
    {
        public struct Entry
        {
            public readonly string File, State;
            public readonly bool Loop;
            public Entry(string file, string state, bool loop) { File = file; State = state; Loop = loop; }
        }

        public const string ControllerResource = "Motion/Motion";
        // The clip whose imported Humanoid avatar every other Mixamo clip copies.
        public const string AvatarSource = "HitReaction";
        public static readonly Entry[] Clips =
        {
            new Entry("KillerSwipe", MotionStates.Swipe, false),
            new Entry("HitReaction", MotionStates.Hit, false),
            new Entry("KnockedDown", MotionStates.KnockedDown, false),
            new Entry("Crawling", MotionStates.Crawl, true),
            new Entry("InjuredIdle", MotionStates.InjuredIdle, true),
            new Entry("InjuredWalk", MotionStates.InjuredWalk, true),
            new Entry("FallingBackDeath", MotionStates.Death, false),
            new Entry("GettingUp", MotionStates.GettingUp, false),
            new Entry("ButtonPushing", MotionStates.Working, true),
        };

        // Root XZ handling on import (root motion is never applied: positions are server-owned).
        // - Falls (KnockedDown, FallingBackDeath): the XZ travel is NOT baked into the pose and is based on the centre of
        //   mass, so the body falls in place over the authoritative position instead of drifting ~0.9 m backwards (into
        //   a wall behind a cornered Survivor) and popping forward when it blends into Crawl.
        // - Every other clip: baked as authored (all are in place; the crawl keeps its authored limb contacts). The hips
        //   centring in MotionGrounding takes up the remaining offset of the down poses.
        public static bool FallsInPlace(string file) => file == "KnockedDown" || file == "FallingBackDeath";
        public static bool CentredOnMass(string file) => FallsInPlace(file);
    }

    public enum MotionView : byte { Survivor, Killer }
    public enum MotionPose : byte { Locomotion, Injured, Working, Hit, KnockedDown, Crawl, GettingUp, Death }

    // The trigger logic for one character, from state the viewing client already has. Plain C# (no Unity types).
    //
    // Survivor view (full state): Normal -> Lagging = Hit, KnockedDown, then Crawl while Lagging (the Crawl state's rate
    // follows the speed, so a still Survivor holds the last crawl pose); Lagging -> Normal = GettingUp, then locomotion;
    // Eliminated = Death once, the last pose held until DeathShownSeconds (then the body despawns); Hacking and still =
    // ButtonPushing; CaptureCount >= 1 = the injured locomotion.
    //
    // Killer view (HUD_SPEC 6.3): only Status and the matched speed are read. Flags and CaptureCount are discarded on
    // entry, so a decoy, an afterimage, a hacking, rescuing or injured Survivor all animate exactly like a Normal one. The
    // poses are Locomotion, and Hit + KnockedDown + Crawl on a matched Normal -> Lagging change; nothing else (no getting
    // up, no hacking, no injured limp). Eliminated entries never reach the Killer, and an unmatched blip despawns at once
    // (the caller drops it), so no death clip plays there.
    public sealed class MotionTracker
    {
        public readonly MotionView View;
        private bool known, fell, rose, died;
        private byte status;
        private double changedAt = double.NegativeInfinity;
        public MotionPose Pose { get; private set; }
        // When the current pose began (a pose that was already running when the character was first seen: -infinity).
        public double PoseSince { get; private set; } = double.NegativeInfinity;
        // True only on the update that saw a capture: Normal -> Lagging, or Normal -> Eliminated (GDD v2 §4: a capture of
        // an already-captured Survivor eliminates). Execution, timeout, rescue and escape are not captures.
        public bool CaptureEdge { get; private set; }

        public MotionTracker(MotionView view) { View = view; }

        public void Reset()
        {
            known = fell = rose = died = false; status = 0; changedAt = double.NegativeInfinity;
            Pose = MotionPose.Locomotion; PoseSince = double.NegativeInfinity; CaptureEdge = false;
        }

        public MotionPose Update(double now, byte newStatus, byte flags, byte captureCount, float speed)
        {
            if (View == MotionView.Killer)
            {
                // HUD_SPEC 6.3 rule 1: never Flags (Hacking, Rescuing, Afterimage) or CaptureCount; BlipStatus is Normal/Lagging.
                flags = 0; captureCount = 0;
                newStatus = newStatus == (byte)PlayerStatus.Lagging ? (byte)PlayerStatus.Lagging : (byte)PlayerStatus.Normal;
            }
            CaptureEdge = false;
            if (!known)
            {
                // First sight: the current state as it already is, with no transition clip (no spawn animation).
                known = true; status = newStatus; changedAt = double.NegativeInfinity;
            }
            else if (newStatus != status)
            {
                bool wasNormal = status == (byte)PlayerStatus.Normal;
                CaptureEdge = wasNormal && (newStatus == (byte)PlayerStatus.Lagging || newStatus == (byte)PlayerStatus.Eliminated);
                fell = wasNormal && newStatus == (byte)PlayerStatus.Lagging;
                rose = View == MotionView.Survivor && status == (byte)PlayerStatus.Lagging && newStatus == (byte)PlayerStatus.Normal;
                died = newStatus == (byte)PlayerStatus.Eliminated;
                status = newStatus; changedAt = now;
            }
            MotionPose next = Resolve(now, flags, captureCount, speed, out double since);
            if (next != Pose) { Pose = next; PoseSince = since; }
            return Pose;
        }

        private MotionPose Resolve(double now, byte flags, byte captureCount, float speed, out double since)
        {
            double elapsed = now - changedAt;
            since = now;
            switch ((PlayerStatus)status)
            {
                case PlayerStatus.Lagging:
                    if (fell && elapsed < MotionTiming.HitSeconds) { since = changedAt; return MotionPose.Hit; }
                    if (fell && elapsed < MotionTiming.HitSeconds + MotionTiming.KnockedDownSeconds) { since = changedAt + MotionTiming.HitSeconds; return MotionPose.KnockedDown; }
                    if (!fell) since = double.NegativeInfinity;
                    return MotionPose.Crawl;
                case PlayerStatus.Eliminated:
                    since = died ? changedAt : double.NegativeInfinity;
                    return MotionPose.Death;
                default:
                    if (rose && elapsed < MotionTiming.GettingUpSeconds) { since = changedAt; return MotionPose.GettingUp; }
                    if (View == MotionView.Survivor && status == (byte)PlayerStatus.Normal)
                    {
                        if ((flags & (byte)PlayerFlags.Hacking) != 0 && speed < MotionTiming.StillSpeed) return MotionPose.Working;
                        if (captureCount >= 1) return MotionPose.Injured;
                    }
                    return MotionPose.Locomotion;
            }
        }

        // Survivor view: an eliminated body is drawn while its fall plays and its last pose holds, then it despawns. A
        // Survivor first seen already eliminated is not drawn.
        public bool DeathShown(double now)
            => status != (byte)PlayerStatus.Eliminated || (died && now - changedAt < MotionTiming.DeathShownSeconds);
    }

    // The Killer model's attack (Survivor view): one swipe per capture, only while the model is drawn.
    public sealed class AttackTracker
    {
        private double startedAt = double.NegativeInfinity;
        public int Count { get; private set; }
        public float Length => MotionTiming.SwipeSeconds;

        public void Reset() { startedAt = double.NegativeInfinity; Count = 0; }

        // A capture seen this frame. Not drawn: nothing plays (the Survivor cannot see the Killer, HUD_SPEC 4.10).
        public bool Trigger(double now, bool drawn)
        {
            if (!drawn) return false;
            Count++; startedAt = now;
            return true;
        }

        public bool Active(double now) => now >= startedAt && now - startedAt < Length;
        public double Elapsed(double now) => now - startedAt;
        // U8: the clip time to show now (MotionTiming.SwipeClipTime of the elapsed real time).
        public float ClipTime(double now) => MotionTiming.SwipeClipTime(now - startedAt);

        // The upper-body layer weight: 0.08 s in, 0.2 s out.
        public float Weight(double now)
        {
            double t = now - startedAt;
            if (t < 0 || t >= Length) return 0;
            return Mathf.Clamp01(Mathf.Min((float)t / .08f, (float)(Length - t) / .2f));
        }
    }

    // Thin Animator driver over MotionTracker / AttackTracker: assigns Resources/Motion/Motion.controller and cross-fades
    // to the tracked pose. Call Observe every frame (also while the character is hidden: status changes still count), and
    // Drive before CharacterInstance.Animate while the character is drawn (Animate advances the Animator, with the Lagging
    // 0.55 rate and the 8 Hz stepping unchanged).
    public sealed class CharacterMotion
    {
        private static RuntimeAnimatorController controller;
        private static bool loaded;
        private static readonly int[] PoseHashes =
        {
            Animator.StringToHash(MotionStates.Locomotion), Animator.StringToHash(MotionStates.Injured), Animator.StringToHash(MotionStates.Working),
            Animator.StringToHash(MotionStates.Hit), Animator.StringToHash(MotionStates.KnockedDown), Animator.StringToHash(MotionStates.Crawl),
            Animator.StringToHash(MotionStates.GettingUp), Animator.StringToHash(MotionStates.Death)
        };
        private static readonly int SwipeHash = Animator.StringToHash(MotionStates.Swipe);
        private static readonly int EmptyHash = Animator.StringToHash(MotionStates.Empty);

        public readonly MotionTracker Tracker;
        public readonly AttackTracker Attack = new AttackTracker();
        private readonly Animator animator;
        private bool shown, attackShown;
        private MotionPose shownPose;

        public static RuntimeAnimatorController Controller
        {
            get
            {
                if (!loaded) { controller = Resources.Load<RuntimeAnimatorController>(MotionLibrary.ControllerResource); loaded = true; }
                return controller;
            }
        }

        // Puts Motion.controller on the model's Humanoid Animator and measures its rest pose for the grounding. Call it
        // right after Instantiate, before CharacterInstance binds the Animator. Without a Humanoid Animator or the
        // controller, the returned motion only tracks (Drives = false) and the model keeps Locomotion.controller.
        public static CharacterMotion Attach(GameObject model, MotionView view)
        {
            Animator found = model != null ? model.GetComponentInChildren<Animator>(true) : null;
            if (found == null || found.avatar == null || !found.avatar.isHuman || Controller == null) return new CharacterMotion(null, view);
            found.runtimeAnimatorController = Controller;
            MotionReach reach = found.GetComponent<MotionReach>();
            if (reach == null) reach = found.gameObject.AddComponent<MotionReach>();
            return new CharacterMotion(found, view) { grounding = MotionGrounding.Create(found), reach = reach };
        }

        public CharacterMotion(Animator animator, MotionView view)
        {
            this.animator = animator != null && animator.runtimeAnimatorController == Controller && Controller != null ? animator : null;
            Tracker = new MotionTracker(view);
        }

        private MotionGrounding grounding;
        private MotionReach reach;
        private double reachAt = double.NegativeInfinity;

        // After CharacterInstance.Animate: keeps the lowest body contact on the floor (see MotionGrounding).
        public void Ground() => GroundAs(Tracker.Pose);

        // Grounds the current Animator pose as the given tracked pose would be (editor renders and tests).
        public void GroundAs(MotionPose pose)
        {
            if (animator == null || grounding == null) return;
            // Once the crawl has settled (no cross-fade), its hips centring is held constant, so the in-place loop keeps
            // its authored hand and knee contacts (a per-frame centring would make them slide with the hips' sway).
            bool settled = pose == MotionPose.Crawl && !animator.IsInTransition(0)
                && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == PoseHashes[(int)MotionPose.Crawl];
            if (settled && crawlHeld) { grounding.Apply(true, crawlShift); return; }
            grounding.Apply(Down(pose));
            crawlHeld = settled;
            if (settled) crawlShift = grounding.Shift;
        }

        private bool crawlHeld;
        private Vector2 crawlShift;

        private static bool Down(MotionPose pose) => pose == MotionPose.KnockedDown || pose == MotionPose.Crawl || pose == MotionPose.GettingUp || pose == MotionPose.Death;

        // The top of a down body for a world tag (character-root space height): the head bone plus TagAboveHead, kept
        // within TagMin..standing. False while upright or not animated (the caller keeps its own head point).
        public bool TagHeight(float standing, out float height)
        {
            height = standing;
            if (animator == null || !Down(Tracker.Pose)) return false;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head), space = animator.transform.parent;
            if (head == null || space == null) return false;
            height = Mathf.Clamp(space.InverseTransformPoint(head.position).y + MotionTiming.TagAboveHead, MotionTiming.TagMin, standing);
            return true;
        }

        // U10: set when an InteractionMotion drives this character's hands (CharacterViews); MotionReach then stays off.
        public bool InteractionDriven { get; set; }

        // The right-hand reach while ButtonPushing (0..1), eased in and out over ButtonReachIn.
        public float Reach => reach != null ? reach.Weight : 0;

        // Horizontal hips centring applied by the last Ground (down poses only).
        public Vector2 GroundShift => grounding?.Shift ?? Vector2.zero;

        public void Dispose() => grounding?.Dispose();
        public float GroundOffset => grounding?.Offset ?? 0;

        // True when the Animator plays the motion states (the caller then drops its own Lagging tilt).
        public bool Drives => animator != null;

        public void Reset()
        {
            Tracker.Reset(); Attack.Reset(); shown = attackShown = crawlHeld = false; reachAt = double.NegativeInfinity;
            if (reach != null) reach.Weight = 0;
        }

        public MotionPose Observe(double now, byte status, byte flags, byte captureCount, float speed)
            => Tracker.Update(now, status, flags, captureCount, speed);

        public void Drive(double now)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            MotionPose pose = Tracker.Pose;
            int hash = PoseHashes[(int)pose];
            // Re-enabling a GameObject resets its Animator to the default state: put the tracked pose back. (Compared with
            // the pose last shown, so a pose change cross-fades instead of cutting.)
            bool lost = shown && !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).shortNameHash != PoseHashes[(int)shownPose];
            if (!shown || pose != shownPose || lost)
            {
                double since = Tracker.PoseSince;
                if (double.IsNegativeInfinity(since))
                    animator.Play(hash, 0, pose == MotionPose.Crawl ? 0 : 1); // already in that state when first seen: no clip replay
                else
                {
                    float offset = (float)Math.Max(0, now - since) * Rate(pose);
                    if (shown && !lost) animator.CrossFadeInFixedTime(hash, Fade(shownPose, pose), 0, offset);
                    else animator.PlayInFixedTime(hash, 0, offset);
                }
                shown = true; shownPose = pose;
            }
            float weight = Attack.Weight(now);
            if (weight > 0)
            {
                // U8: the Swipe state has speed 0 (MotionSetup); its time is set every frame from the timing map, so the
                // wind-up, the slowed strike and the strike-pose hold play on the presentation clock.
                animator.Play(SwipeHash, 1, Attack.ClipTime(now) / MotionTiming.SwipeClipSeconds);
                attackShown = true;
            }
            else if (weight <= 0 && attackShown)
            {
                animator.Play(EmptyHash, 1, 0);
                attackShown = false;
            }
            animator.SetLayerWeight(1, weight);
            if (reach != null)
            {
                // U10: with the interaction rig (InteractionDriven) the hack is two-handed on a holo panel, not this press.
                float target = pose == MotionPose.Working && !InteractionDriven ? 1 : 0;
                if (double.IsNegativeInfinity(reachAt) && pose == MotionPose.Working && double.IsNegativeInfinity(Tracker.PoseSince)) reach.Weight = 1; // first seen hacking
                else if (!double.IsNegativeInfinity(reachAt))
                    reach.Weight = Mathf.MoveTowards(reach.Weight, target, (float)(Math.Max(0, now - reachAt) / MotionTiming.ButtonReachIn));
                reachAt = now;
            }
        }

        // Clip seconds per real second for a pose (the state speeds MotionSetup writes cancel the Lagging rate).
        private static float Rate(MotionPose pose) => 1;

        private static float Fade(MotionPose from, MotionPose to)
        {
            switch (to)
            {
                case MotionPose.Hit: return .08f;
                case MotionPose.KnockedDown: return .25f;
                case MotionPose.Crawl: return from == MotionPose.KnockedDown ? .35f : .2f;
                case MotionPose.GettingUp: return .2f;
                case MotionPose.Death: return .12f;
                default: return from == MotionPose.GettingUp ? .3f : .25f;
            }
        }
    }

    // Rio and Kyle are retargeted from rigs with other proportions, and the prefabs were placed on padded renderer
    // bounds, so an unmodified Humanoid pose leaves the soles about 0.1 m above the floor, and a lying or crawling pose
    // can put a hand, the bag or a boot under it. After each Animator update this moves the model (its Animator
    // transform, under the character root) vertically, never sideways, so the body touches the floor:
    // - upright poses: the lower sole (foot and toe bones less their rest heights above the rest pose's lowest vertex);
    // - lying, falling, crawling and rising poses: the lowest skinned vertex (baked mesh; no allocation per frame).
    public sealed class MotionGrounding
    {
        private static readonly HumanBodyBones[] Soles = { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
        public const float MaxOffset = .3f;
        private readonly Transform rig, space;
        private Transform hips;
        private readonly Vector3 rest;
        private readonly Transform[] bones;
        private readonly float[] margins;
        private readonly SkinnedMeshRenderer[] skins;
        private readonly Mesh baked = new Mesh();
        private readonly System.Collections.Generic.List<Vector3> vertices = new System.Collections.Generic.List<Vector3>();
        public float Offset { get; private set; }

        private MotionGrounding(Transform rig, Transform[] bones, float[] margins, SkinnedMeshRenderer[] skins)
        {
            this.rig = rig; space = rig.parent; rest = rig.localPosition; this.bones = bones; this.margins = margins; this.skins = skins;
        }

        // Measures the rest pose: call before the Animator first evaluates. Null without a parent or a skinned mesh.
        public static MotionGrounding Create(Animator animator)
        {
            Transform rig = animator.transform, space = rig.parent;
            if (space == null) return null;
            SkinnedMeshRenderer[] skins = space.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) return null;
            var grounding = new MotionGrounding(rig, new Transform[Soles.Length], new float[Soles.Length], skins) { hips = animator.GetBoneTransform(HumanBodyBones.Hips) };
            float sole = grounding.Lowest();
            for (int i = 0; i < Soles.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(Soles[i]); // toes are optional in a Humanoid avatar
                grounding.bones[i] = bone;
                if (bone != null) grounding.margins[i] = Mathf.Max(0, space.InverseTransformPoint(bone.position).y - sole);
            }
            return grounding;
        }

        // The lowest skinned vertex in the character root's space.
        public float Lowest()
        {
            float lowest = float.PositiveInfinity;
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            for (int s = 0; s < skins.Length; s++)
            {
                SkinnedMeshRenderer skin = skins[s];
                if (skin == null || skin.sharedMesh == null) continue;
                skin.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                Matrix4x4 m = toSpace * Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                for (int i = 0; i < vertices.Count; i++)
                {
                    float y = m.m10 * vertices[i].x + m.m11 * vertices[i].y + m.m12 * vertices[i].z + m.m13;
                    if (y < lowest) lowest = y;
                }
            }
            return lowest;
        }

        // wholeBody (down poses) also keeps the hips within HipsRadius of the authoritative position horizontally: the falls
        // are imported in place (centre of mass), and this takes up the rest (the lying body's hips sit behind its centre
        // of mass, the crawling body's in front), so Hit -> KnockedDown -> Crawl never drifts or pops sideways.
        public Vector2 Shift { get; private set; }
        // Editor QA renders of attempt 1 turn the centring off (MotionSetup, PTK_MOTION_A1); always on in the game.
        public static bool CentreHips = true;

        public void Apply(bool wholeBody, Vector2? heldShift = null)
        {
            if (rig == null || space == null) return;
            rig.localPosition = rest;
            Shift = heldShift.HasValue && CentreHips ? heldShift.Value : Vector2.zero;
            if (heldShift.HasValue) rig.localPosition = rest + new Vector3(Shift.x, 0, Shift.y);
            else if (wholeBody && hips != null && CentreHips)
            {
                Vector3 at = space.InverseTransformPoint(hips.position);
                var flat = new Vector2(at.x, at.z);
                float excess = flat.magnitude - MotionTiming.HipsRadius;
                if (excess > 0) Shift = -flat.normalized * excess;
            }
            float lowest = float.PositiveInfinity;
            if (wholeBody) lowest = Lowest();
            else
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) lowest = Mathf.Min(lowest, space.InverseTransformPoint(bones[i].position).y - margins[i]);
            Offset = float.IsInfinity(lowest) ? 0 : Mathf.Clamp(-lowest, -MaxOffset, MaxOffset);
            rig.localPosition = rest + new Vector3(Shift.x, Offset, Shift.y);
        }

        public void Dispose()
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(baked); else UnityEngine.Object.DestroyImmediate(baked);
        }
    }

    // -ptk-preview-motion <clip> (dev screenshots only, set by DevCapture): the own Survivor's motion inputs follow a
    // scripted status change at StartSeconds, or the Killer model attacks once; nothing is sent. KillerDown takes a Killer
    // view shot after a real capture (no override).
    public enum MotionPreviewClip : byte { None, Swipe, Hit, KnockDown, Crawl, GetUp, Death, Button, Injured, KillerDown }

    public static class MotionPreview
    {
        public const double StartSeconds = 3;
        public static readonly string[] Names = { "none", "swipe", "hit", "knockdown", "crawl", "getup", "death", "button", "injured", "killer-down" };
        public static MotionPreviewClip Active { get; set; }

        public static bool TryParse(string value, out MotionPreviewClip clip)
        {
            clip = MotionPreviewClip.None;
            if (value == null) return false;
            int index = Array.IndexOf(Names, value.Trim().ToLowerInvariant());
            if (index <= 0) return false;
            clip = (MotionPreviewClip)index;
            return true;
        }

        public static string Name(MotionPreviewClip clip) => Names[(int)clip];

        // The own Survivor's scripted inputs at t seconds after the views started (the real values otherwise).
        public static void Inputs(MotionPreviewClip clip, double t, ref byte status, ref byte flags, ref byte captureCount)
        {
            bool after = t >= StartSeconds;
            switch (clip)
            {
                case MotionPreviewClip.Hit:
                case MotionPreviewClip.KnockDown:
                case MotionPreviewClip.Crawl:
                    status = (byte)(after ? PlayerStatus.Lagging : PlayerStatus.Normal); break;
                case MotionPreviewClip.GetUp:
                    status = (byte)(after ? PlayerStatus.Normal : PlayerStatus.Lagging); break;
                case MotionPreviewClip.Death:
                    status = (byte)(after ? PlayerStatus.Eliminated : PlayerStatus.Normal); break;
                case MotionPreviewClip.Button:
                    status = (byte)PlayerStatus.Normal; flags |= (byte)PlayerFlags.Hacking; break;
                case MotionPreviewClip.Injured:
                    status = (byte)PlayerStatus.Normal; captureCount = 1; break;
            }
        }

        // Match seconds of the preview shots (mid-clip; Death also once the last pose holds).
        public static double[] ShotTimes(MotionPreviewClip clip)
        {
            switch (clip)
            {
                case MotionPreviewClip.Swipe: return new[] { StartSeconds + MotionTiming.SwipeSeconds * .5 };
                case MotionPreviewClip.Hit: return new[] { StartSeconds + MotionTiming.HitSeconds * .6 };
                case MotionPreviewClip.KnockDown: return new[] { StartSeconds + MotionTiming.HitSeconds + MotionTiming.KnockedDownSeconds * .45 };
                case MotionPreviewClip.Crawl: return new[] { StartSeconds + MotionTiming.HitSeconds + MotionTiming.KnockedDownSeconds + 1.5 };
                case MotionPreviewClip.GetUp: return new[] { StartSeconds + MotionTiming.GettingUpSeconds * .45 };
                case MotionPreviewClip.Death: return new[] { StartSeconds + MotionTiming.DeathSeconds * .5, StartSeconds + MotionTiming.DeathSeconds + 1 };
                case MotionPreviewClip.Button:
                case MotionPreviewClip.Injured: return new[] { StartSeconds + 1.2 };
                default: return Array.Empty<double>();
            }
        }
    }
}
