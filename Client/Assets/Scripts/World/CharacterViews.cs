using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // On a Survivor client the Survivor characters (Id-based SnapshotBuffer interpolation) and the Killer model. The Killer
    // model is drawn only from the Killer entry of the snapshots (SnapshotBuffer.SampleKiller). The Killer client builds no
    // character here (Accept breaks before reading an entry).
    public sealed class CharacterViews
    {
        // Imported Kyle, or the original capsule when the catalog is unavailable.
        private sealed class KillerModel
        {
            public GameObject Root;
            public CharacterInstance Art;
            public Renderer Head;
            public CharacterMotion Motion;
            // U10: the jam slam (Survivor view) and its glitch burst.
            public InteractionMotion Interaction;
            public HoloPropFx Props;
            public readonly JamTracker Jam = new JamTracker();
            public int BurstFor;
        }
        private sealed class Avatar
        {
            public GameObject Root;
            public CharacterInstance Art;
            public Color Tint;
            public byte Status, Flags, CaptureCount;
            public CharacterMotion Motion;
            // This frame's sampled pose, and the eased draw-only offset that keeps it out of other bodies (VisualSeparation).
            public Vector3 Position;
            public float Yaw, Alpha;
            public Vector2 Offset;
            public bool Lagging;
            // U10: the interaction layer (hack, exit switch, restore) and its pooled holo props.
            public InteractionMotion Interaction;
            public HoloPropFx Props;
        }

        // The Killer model's key among the separation bodies (Survivor ids are positive).
        private const int KillerSeparationId = int.MinValue;

        public static readonly Vector3 KillerBodyScale = new Vector3(CharacterLook.Width, CharacterLook.Height * .55f, CharacterLook.Width);
        private static readonly Color Killer = new Color32(229, 72, 77, 255);
        private static readonly Color Survivor = WorldMaterials.Survivor;
        private static readonly Color Signal = new Color32(255, 176, 32, 255);

        private readonly WorldMaterials materials;
        private readonly Transform transform;
        private readonly int localId;
        private readonly Role role;
        private readonly CharacterArt art;
        private readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();
        private KillerModel killerModel;
        private float localFacing;
        private bool localDither;
        private double startedAt = double.NaN;
        private double lastRender = double.NaN;
        private readonly List<SeparationBody> bodies = new List<SeparationBody>();
        private readonly List<Avatar> drawn = new List<Avatar>();
        private Vector2[] separation = new Vector2[10]; // 8 players + the Killer model, grown if ever needed
        private bool killerShown;
        private Vector3 killerGround;
        // U10: the newest snapshot's terminals (for the hack stream source and the jam), and the own hold action.
        private float[] terminalX = new float[8], terminalZ = new float[8];
        private bool[] terminalRegressing = new bool[8];
        private int terminalCount;
        private bool terminalsFresh;
        private byte ownAction;

        // Alpha the Killer model was drawn with in the last Render (0 = hidden); read by the dev capture.
        public float KillerAlpha { get; private set; }
        // Alpha of the Killer's visor material after the last Render; it fades with the body (KillerAlpha).
        public float KillerVisorAlpha { get; private set; }

        public CharacterViews(WorldMaterials materials, int localId, Role role)
            : this(materials, localId, role, CharacterArt.Load()) { }

        public CharacterViews(WorldMaterials materials, int localId, Role role, CharacterArt art)
        {
            this.materials = materials;
            transform = materials.Parent;
            this.localId = localId;
            this.role = role;
            this.art = art;
        }

        public void Initialize()
        {
            if (role == Role.Survivor) killerModel = MakeKillerModel();
        }

        // A spectator (own status Eliminated or Escaped) never sees the Killer model, even if an entry arrives (U4e Q-04b).
        public static bool KillerDrawable(byte ownStatus)
            => ownStatus != (byte)PlayerStatus.Eliminated && ownStatus != (byte)PlayerStatus.Escaped;

        private KillerModel MakeKillerModel()
        {
            if (art != null && art.KillerPrefab != null)
            {
                GameObject model = Object.Instantiate(art.KillerPrefab, transform);
                model.name = "Killer (line of sight)";
                CharacterMotion motion = CharacterMotion.Attach(model, MotionView.Survivor); // U4j: locomotion + the attack layer
                var instance = new KillerModel { Root = model, Art = new CharacterInstance(model), Head = model.transform.Find("Killer visor")?.GetComponent<Renderer>(), Motion = motion,
                    Interaction = AttachInteraction(model, motion), Props = new HoloPropFx(transform) };
                model.SetActive(false);
                return instance;
            }
            var root = new GameObject("Killer (line of sight)");
            root.transform.SetParent(transform, false);
            GameObject body = FallbackPart("Killer body", PrimitiveType.Capsule, KillerBodyScale, Killer, true);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * KillerBodyScale.y;
            // Transparent like the body, so it fades with it (U4e Q-03).
            GameObject visor = FallbackPart("Killer visor", PrimitiveType.Cube, CharacterLook.VisorScale, CharacterLook.Visor, true);
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = CharacterLook.VisorOffset + Vector3.up * (KillerBodyScale.y * 2 - CharacterLook.Height);
            root.SetActive(false);
            return new KillerModel
            {
                Root = root, Art = new CharacterInstance(root), Head = visor.GetComponent<Renderer>(),
                Motion = new CharacterMotion(null, MotionView.Survivor), // capsule: attacks counted, not animated
                Interaction = new InteractionMotion(null), Props = new HoloPropFx(transform)
            };
        }

        // Survivor client: the Killer model from the Killer entry only (present, held + fading, or hidden); never while the
        // own Survivor is spectating.
        // U4j: a capture seen this frame (any Survivor's Normal -> Lagging / Normal -> Eliminated) plays the swipe (1.2 s,
        // upper body) on the Killer model, only if the model is drawn now (HUD_SPEC 4.10: line of sight).
        private void RenderKiller(double now, SnapshotBuffer snapshots, bool captured)
        {
            KillerAlpha = 0; KillerVisorAlpha = 0; killerShown = false;
            if (killerModel == null) return;
            bool seen = snapshots.SampleKiller(now, out Vector3 position, out float yaw, out float alpha)
                && KillerDrawable(avatars.TryGetValue(localId, out Avatar self) ? self.Status : (byte)PlayerStatus.Normal);
            if (killerModel.Root.activeSelf != seen) killerModel.Root.SetActive(seen);
            AttackTracker attack = killerModel.Motion.Attack;
            if (captured) attack.Trigger(now, seen);
            if (MotionPreview.Active == MotionPreviewClip.Swipe)
            {
                // Dev preview: one attack per showing of the (injected) Killer model, from StartSeconds on.
                if (seen && now - startedAt >= MotionPreview.StartSeconds && !attack.Active(now) && attack.Count == 0)
                    attack.Trigger(now, true);
            }
            // U10: a terminal next to the drawn model turning Regressing is the end of this Killer's kick: one slam.
            if (terminalsFresh)
            {
                killerModel.Jam.Observe(now, terminalCount, terminalX, terminalZ, terminalRegressing, seen, position.x, position.z);
                terminalsFresh = false;
            }
            if (!seen) { killerModel.Props.Hide(now); return; }
            // The Killer model stays where its entry puts it (a pinned separation body): Survivors near it step aside instead.
            killerShown = true; killerGround = new Vector3(position.x, 0, position.z);
            killerModel.Root.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, yaw, 0));
            killerModel.Motion.Observe(now, (byte)PlayerStatus.Normal, 0, 0, killerModel.Art.Speed);
            killerModel.Motion.Drive(now);
            RenderJam(now);
            killerModel.Art.Animate(new Vector3(position.x, 0, position.z), now, false, false);
            killerModel.Motion.Ground();
            Camera camera = Camera.main;
            bool close = camera != null && killerModel.Art.NearCamera(camera.transform.position);
            killerModel.Art.Look(Killer, alpha, close);
            KillerAlpha = alpha;
            KillerVisorAlpha = killerModel.Head != null ? killerModel.Head.sharedMaterial.color.a : alpha;
        }

        // U10 (Survivor view): the slam on the Killer model and its `danger` burst at the impact.
        private void RenderJam(double now)
        {
            JamTracker jam = killerModel.Jam;
            bool active = jam.Active(now) && jam.Terminal >= 0 && jam.Terminal < terminalCount;
            Vector3 top = active ? new Vector3(terminalX[jam.Terminal], InteractionTiming.JamTop, terminalZ[jam.Terminal]) : Vector3.zero;
            killerModel.Interaction.Update(now, active ? InteractionKind.Jam : InteractionKind.None, top, active ? jam.Elapsed(now) : 0);
            if (active && killerModel.BurstFor != jam.Count && jam.Elapsed(now) >= InteractionTiming.JamSeconds * InteractionTiming.JamImpact)
            {
                killerModel.BurstFor = jam.Count;
                killerModel.Props.Burst(top, now);
            }
            else killerModel.Props.Hide(now);
        }

        private static InteractionMotion AttachInteraction(GameObject model, CharacterMotion motion)
        {
            if (!motion.Drives) return new InteractionMotion(null);
            motion.InteractionDriven = true;
            return InteractionMotion.Attach(model.GetComponentInChildren<Animator>(true));
        }

        // U10: the own S2C_SelfState.ActionKind (the exit switch hold is own-only knowledge).
        public void SetOwnAction(byte kind) => ownAction = kind;

        public void Accept(S2C_WorldState state)
        {
            if (state.Terminals != null)
            {
                terminalCount = state.Terminals.Count;
                if (terminalX.Length < terminalCount) { terminalX = new float[terminalCount]; terminalZ = new float[terminalCount]; terminalRegressing = new bool[terminalCount]; }
                for (int i = 0; i < terminalCount; i++)
                {
                    TerminalState t = state.Terminals[i];
                    terminalX[i] = t.X; terminalZ[i] = t.Z; terminalRegressing[i] = (t.Flags & (byte)TerminalFlags.Regressing) != 0;
                }
                terminalsFresh = true;
            }
            foreach (PlayerState player in state.Players)
            {
                if (role == Role.Survivor && player.Role == (byte)Role.Killer) continue;
                if (role == Role.Killer) break; // the Killer view never builds a character from an entry (no Ids, no body)
                if (avatars.TryGetValue(player.Id, out Avatar known))
                {
                    // Status is the latest authoritative value; it is not interpolated. So are the flags and the capture
                    // count the motion reads (Survivor view: full state, U4j).
                    known.Status = player.Status; known.Flags = player.Flags; known.CaptureCount = player.CaptureCount;
                    continue;
                }
                Color tint = player.Role == (byte)Role.Killer ? Killer : Survivor;
                if (art != null && art.SurvivorPrefab != null)
                {
                    GameObject model = Object.Instantiate(art.SurvivorPrefab, transform);
                    model.name = "Player " + player.Id;
                    CharacterMotion motion = CharacterMotion.Attach(model, MotionView.Survivor); // U4j: before CharacterInstance binds it
                    var imported = new Avatar { Root = model, Art = new CharacterInstance(model), Tint = tint, Status = player.Status, Flags = player.Flags, CaptureCount = player.CaptureCount, Motion = motion,
                        Interaction = AttachInteraction(model, motion), Props = new HoloPropFx(transform) };
                    model.SetActive(false);
                    avatars.Add(player.Id, imported);
                    continue;
                }
                var root = new GameObject("Player " + player.Id);
                root.transform.SetParent(transform, false);
                GameObject body = FallbackPart("Body", PrimitiveType.Capsule, CharacterLook.BodyScale, tint, true);
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = Vector3.up * CharacterLook.Height * .5f;
                GameObject visor = FallbackPart("Visor", PrimitiveType.Cube, CharacterLook.VisorScale, CharacterLook.Visor);
                visor.transform.SetParent(root.transform, false);
                visor.transform.localPosition = CharacterLook.VisorOffset;
                var avatar = new Avatar { Root = root, Art = new CharacterInstance(root), Tint = tint, Status = player.Status, Flags = player.Flags, CaptureCount = player.CaptureCount,
                    Motion = new CharacterMotion(null, MotionView.Survivor), Interaction = new InteractionMotion(null), Props = new HoloPropFx(transform) }; // capsule: tracked, not animated
                root.SetActive(false);
                avatars.Add(player.Id, avatar);
            }
        }

        // Survivor view: the own character faces the local facing (HUD_SPEC 4.10) and is dithered while the boom is short.
        public void SetLocalLook(float facing, bool dither)
        {
            localFacing = facing; localDither = dither;
        }

        public void Render(double now, SnapshotBuffer snapshots)
        {
            Camera camera = Camera.main;
            if (double.IsNaN(startedAt)) startedAt = now;
            float dt = double.IsNaN(lastRender) ? 0 : (float)(now - lastRender);
            lastRender = now;
            bool captured = false;
            bodies.Clear(); drawn.Clear();
            // Pass 1: motion tracking, visibility and this frame's sampled pose.
            foreach (var entry in avatars)
            {
                Avatar avatar = entry.Value;
                bool own = entry.Key == localId;
                // U4j motion (Survivor view, full state): tracked every frame, also while the character is not drawn, so a
                // status change is never missed. The dev preview (-ptk-preview-motion) scripts only the own character's inputs.
                byte status = avatar.Status, flags = avatar.Flags, count = avatar.CaptureCount;
                if (own && MotionPreview.Active != MotionPreviewClip.None) MotionPreview.Inputs(MotionPreview.Active, now - startedAt, ref status, ref flags, ref count);
                avatar.Motion.Observe(now, status, flags, count, avatar.Art.Speed);
                captured |= avatar.Motion.Tracker.CaptureEdge;
                // An eliminated Survivor stays drawn while the fall plays and its last pose holds (MotionTiming.DeathShownSeconds),
                // then despawns; without the imported character (capsules) it vanishes at once, as before.
                bool body = avatar.Status != (byte)PlayerStatus.Escaped && (avatar.Status != (byte)PlayerStatus.Eliminated
                    || (avatar.Motion.Drives && avatar.Motion.Tracker.Pose == MotionPose.Death && avatar.Motion.Tracker.DeathShown(now)));
                bool visible = snapshots.Sample(entry.Key, now, out Vector3 position, out float yaw, out float alpha) && body;
                avatar.Root.SetActive(visible);
                if (!visible) { avatar.Offset = Vector2.zero; avatar.Interaction.Reset(); avatar.Props.Hide(now); continue; }
                avatar.Position = position; avatar.Yaw = yaw; avatar.Alpha = alpha; avatar.Lagging = status == (byte)PlayerStatus.Lagging;
                // The own avatar is pinned: the camera follows it, so only the other body steps aside.
                bodies.Add(new SeparationBody(entry.Key, position.x, position.z, own));
                drawn.Add(avatar);
            }
            RenderKiller(now, snapshots, captured);
            if (killerShown) bodies.Add(new SeparationBody(KillerSeparationId, killerGround.x, killerGround.z, true));
            if (separation.Length < bodies.Count) separation = new Vector2[bodies.Count];
            VisualSeparation.Resolve(bodies, VisualSeparation.MinDistance, separation);
            // Pass 2: draw each body at its sampled position plus the eased separation offset (draw only; snapshots, input
            // and everything sent to the server keep the authoritative position).
            for (int i = 0; i < drawn.Count; i++)
            {
                Avatar avatar = drawn[i];
                bool own = bodies[i].Id == localId;
                Vector3 position = avatar.Position;
                float yaw = avatar.Yaw, alpha = avatar.Alpha;
                avatar.Offset = VisualSeparation.Ease(avatar.Offset, separation[i], dt);
                avatar.Root.transform.SetPositionAndRotation(new Vector3(position.x + avatar.Offset.x, 0, position.z + avatar.Offset.y), Quaternion.Euler(0, own ? localFacing : yaw, 0));
                // Lagging Survivors use the Signal amber so the team can find them to rescue (team info). The own
                // character carries no state tint (HUD_SPEC 4.10: the meter is the only indicator).
                Color color = !own && avatar.Status == (byte)PlayerStatus.Lagging ? Signal : avatar.Tint;
                bool lagging = avatar.Lagging;
                avatar.Motion.Drive(now);
                RenderInteraction(avatar, own, bodies[i].Id, now);
                avatar.Art.Animate(avatar.Root.transform.position, now, lagging, false);
                avatar.Motion.Ground();
                // The knock-down / crawl clips replace the old Lagging lean; the capsule fallback keeps it.
                avatar.Root.transform.rotation *= Quaternion.Euler(lagging && !avatar.Motion.Drives ? 12 : 0, 0, 0);
                bool close = !own && camera != null && avatar.Art.NearCamera(camera.transform.position);
                avatar.Art.Look(color, alpha, own ? localDither : close);
            }
        }

        // U10 (Survivor view, full state): the interaction pose and props of one drawn Survivor, from its received flags
        // (Hacking, Rescuing), the own exit switch hold and static or received positions. HUD_SPEC 4.10 (11/17, U10).
        private void RenderInteraction(Avatar avatar, bool own, int id, double now)
        {
            InteractionKind kind = InteractionMap.Resolve(MotionView.Survivor, avatar.Status, avatar.Flags, avatar.Motion.Tracker.Pose, avatar.Art.Speed, own, own ? ownAction : (byte)0);
            Vector3 at = avatar.Root.transform.position, target = at + avatar.Root.transform.forward, source = target;
            switch (kind)
            {
                case InteractionKind.Hack:
                {
                    int t = InteractionMap.Nearest(at.x, at.z, terminalX, terminalZ, terminalCount, GameConstants.TerminalRadius + .6f);
                    if (t >= 0) source = target = new Vector3(terminalX[t], 0, terminalZ[t]);
                    break;
                }
                case InteractionKind.Restore:
                {
                    float best = (GameConstants.RescueRadius + .6f) * (GameConstants.RescueRadius + .6f);
                    for (int k = 0; k < drawn.Count; k++)
                    {
                        Avatar mate = drawn[k];
                        if (mate == avatar || mate.Status != (byte)PlayerStatus.Lagging) continue;
                        Vector3 p = mate.Root.transform.position;
                        float sq = (p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z);
                        if (sq <= best) { best = sq; target = p; }
                    }
                    break;
                }
                case InteractionKind.Switch:
                {
                    var switches = SharedMap.Map.ExitSwitches;
                    float best = float.MaxValue;
                    for (int k = 0; k < switches.Count; k++)
                    {
                        float sq = (switches[k].X - at.x) * (switches[k].X - at.x) + (switches[k].Z - at.z) * (switches[k].Z - at.z);
                        if (sq < best) { best = sq; target = new Vector3(switches[k].X, 0, switches[k].Z); }
                    }
                    break;
                }
            }
            double clock = now + (id & 7) * .37; // beats and streams are not in lockstep across characters
            avatar.Interaction.Update(now, kind, target, clock);
            avatar.Props.Show(avatar.Interaction.Kind, avatar.Interaction.Weight, avatar.Root.transform, source, target, clock, now);
        }

        public InteractionKind InteractionOf(int id) => avatars.TryGetValue(id, out Avatar avatar) ? avatar.Interaction.Kind : InteractionKind.None;
        public HoloPropFx PropsOf(int id) => avatars.TryGetValue(id, out Avatar avatar) ? avatar.Props : null;
        public HoloPropFx KillerProps => killerModel?.Props;
        public int KillerJams => killerModel?.Jam.Count ?? 0;

        // Tests / dev capture (U10): a Survivor character's tracked pose and measured speed.
        public MotionPose PoseOf(int id) => avatars.TryGetValue(id, out Avatar avatar) ? avatar.Motion.Tracker.Pose : MotionPose.Locomotion;
        public float SpeedOf(int id) => avatars.TryGetValue(id, out Avatar avatar) ? avatar.Art.Speed : 0;

        // Dev capture / tests: the Killer model's attacks so far and whether one plays now.
        public int KillerAttacks => killerModel?.Motion.Attack.Count ?? 0;
        public bool KillerAttacking(double now) => killerModel != null && killerModel.Motion.Attack.Active(now);

        public void Dispose()
        {
            killerModel?.Art.Dispose();
            killerModel?.Motion.Dispose();
            killerModel?.Props.Dispose();
            foreach (Avatar avatar in avatars.Values) { avatar.Art.Dispose(); avatar.Motion.Dispose(); avatar.Props.Dispose(); }
        }

        private GameObject FallbackPart(string label, PrimitiveType type, Vector3 scale, Color color, bool transparent = false)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = label;
            item.transform.SetParent(transform, false);
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = materials.MakeMaterial(color, transparent);
            CharacterLook.Release(item.GetComponent<Collider>());
            return item;
        }
    }
}
