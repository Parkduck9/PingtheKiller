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
        }
        private sealed class Avatar
        {
            public GameObject Root;
            public CharacterInstance Art;
            public Color Tint;
            public byte Status, Flags, CaptureCount;
            public CharacterMotion Motion;
        }

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
                var instance = new KillerModel { Root = model, Art = new CharacterInstance(model), Head = model.transform.Find("Killer visor")?.GetComponent<Renderer>(), Motion = motion };
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
                Motion = new CharacterMotion(null, MotionView.Survivor) // capsule: attacks counted, not animated
            };
        }

        // Survivor client: the Killer model from the Killer entry only (present, held + fading, or hidden); never while the
        // own Survivor is spectating.
        // U4j: a capture seen this frame (any Survivor's Normal -> Lagging / Normal -> Eliminated) plays the swipe (1.2 s,
        // upper body) on the Killer model, only if the model is drawn now (HUD_SPEC 4.10: line of sight).
        private void RenderKiller(double now, SnapshotBuffer snapshots, bool captured)
        {
            KillerAlpha = 0; KillerVisorAlpha = 0;
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
            if (!seen) return;
            killerModel.Root.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, yaw, 0));
            killerModel.Motion.Observe(now, (byte)PlayerStatus.Normal, 0, 0, killerModel.Art.Speed);
            killerModel.Motion.Drive(now);
            killerModel.Art.Animate(new Vector3(position.x, 0, position.z), now, false, false);
            killerModel.Motion.Ground();
            Camera camera = Camera.main;
            bool close = camera != null && killerModel.Art.NearCamera(camera.transform.position);
            killerModel.Art.Look(Killer, alpha, close);
            KillerAlpha = alpha;
            KillerVisorAlpha = killerModel.Head != null ? killerModel.Head.sharedMaterial.color.a : alpha;
        }

        public void Accept(S2C_WorldState state)
        {
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
                    var imported = new Avatar { Root = model, Art = new CharacterInstance(model), Tint = tint, Status = player.Status, Flags = player.Flags, CaptureCount = player.CaptureCount, Motion = motion };
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
                    Motion = new CharacterMotion(null, MotionView.Survivor) }; // capsule: tracked, not animated
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
            bool captured = false;
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
                if (!visible) continue;
                avatar.Root.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, own ? localFacing : yaw, 0));
                // Lagging Survivors use the Signal amber so the team can find them to rescue (team info). The own
                // character carries no state tint (HUD_SPEC 4.10: the meter is the only indicator).
                Color color = !own && avatar.Status == (byte)PlayerStatus.Lagging ? Signal : avatar.Tint;
                bool lagging = status == (byte)PlayerStatus.Lagging;
                avatar.Motion.Drive(now);
                avatar.Art.Animate(avatar.Root.transform.position, now, lagging, false);
                avatar.Motion.Ground();
                // The knock-down / crawl clips replace the old Lagging lean; the capsule fallback keeps it.
                avatar.Root.transform.rotation *= Quaternion.Euler(lagging && !avatar.Motion.Drives ? 12 : 0, 0, 0);
                bool close = !own && camera != null && avatar.Art.NearCamera(camera.transform.position);
                avatar.Art.Look(color, alpha, own ? localDither : close);
            }
            RenderKiller(now, snapshots, captured);
        }

        // Dev capture / tests: the Killer model's attacks so far and whether one plays now.
        public int KillerAttacks => killerModel?.Motion.Attack.Count ?? 0;
        public bool KillerAttacking(double now) => killerModel != null && killerModel.Motion.Attack.Active(now);

        public void Dispose()
        {
            killerModel?.Art.Dispose();
            killerModel?.Motion.Dispose();
            foreach (Avatar avatar in avatars.Values) { avatar.Art.Dispose(); avatar.Motion.Dispose(); }
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
