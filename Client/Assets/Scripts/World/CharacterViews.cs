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
        // Placeholder Killer model until the D3 assets: the Survivor capsule shape, 10 % taller, in the Killer red, with the visor.
        private sealed class KillerModel
        {
            public GameObject Root;
            public Material Material;
            public Material Visor;
        }
        private sealed class Avatar
        {
            public GameObject Root;
            public Renderer Body;
            public Material Material;
            public Color Tint;
            public byte Status;
        }

        public static readonly Vector3 KillerBodyScale = new Vector3(CharacterLook.Width, CharacterLook.Height * .55f, CharacterLook.Width);
        private static readonly Color Killer = new Color32(229, 72, 77, 255);
        private static readonly Color Survivor = WorldMaterials.Survivor;
        private static readonly Color Signal = new Color32(255, 176, 32, 255);

        private readonly WorldMaterials materials;
        private readonly Transform transform;
        private readonly int localId;
        private readonly Role role;
        private readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();
        private KillerModel killerModel;
        private float localFacing;
        private bool localDither;
        private Material ditherMaterial;

        // Alpha the Killer model was drawn with in the last Render (0 = hidden); read by the dev capture.
        public float KillerAlpha { get; private set; }
        // Alpha of the Killer's visor material after the last Render; it fades with the body (KillerAlpha).
        public float KillerVisorAlpha { get; private set; }

        public CharacterViews(WorldMaterials materials, int localId, Role role)
        {
            this.materials = materials;
            transform = materials.Parent;
            this.localId = localId;
            this.role = role;
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
            var root = new GameObject("Killer (line of sight)");
            root.transform.SetParent(transform, false);
            GameObject body = materials.Primitive("Killer body", PrimitiveType.Capsule, Vector3.zero, KillerBodyScale, Killer, true);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * KillerBodyScale.y;
            // Transparent like the body, so it fades with it (U4e Q-03).
            GameObject visor = materials.Primitive("Killer visor", PrimitiveType.Cube, Vector3.zero, CharacterLook.VisorScale, CharacterLook.Visor, true);
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = CharacterLook.VisorOffset + Vector3.up * (KillerBodyScale.y * 2 - CharacterLook.Height);
            root.SetActive(false);
            return new KillerModel
            {
                Root = root, Material = body.GetComponent<Renderer>().sharedMaterial, Visor = visor.GetComponent<Renderer>().sharedMaterial
            };
        }

        // Survivor client: the Killer model from the Killer entry only (present, held + fading, or hidden); never while the
        // own Survivor is spectating.
        private void RenderKiller(double now, SnapshotBuffer snapshots)
        {
            KillerAlpha = 0; KillerVisorAlpha = 0;
            if (killerModel == null) return;
            bool seen = snapshots.SampleKiller(now, out Vector3 position, out float yaw, out float alpha)
                && KillerDrawable(avatars.TryGetValue(localId, out Avatar self) ? self.Status : (byte)PlayerStatus.Normal);
            if (killerModel.Root.activeSelf != seen) killerModel.Root.SetActive(seen);
            if (!seen) return;
            killerModel.Root.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, yaw, 0));
            Color color = Killer;
            color.a = alpha;
            killerModel.Material.color = color;
            Color visor = CharacterLook.Visor;
            visor.a = alpha;
            killerModel.Visor.color = visor;
            KillerAlpha = alpha; KillerVisorAlpha = killerModel.Visor.color.a;
        }

        public void Accept(S2C_WorldState state)
        {
            foreach (PlayerState player in state.Players)
            {
                if (role == Role.Survivor && player.Role == (byte)Role.Killer) continue;
                if (role == Role.Killer) break; // the Killer view never builds a character from an entry (no Ids, no body)
                if (avatars.TryGetValue(player.Id, out Avatar known))
                {
                    // Status is the latest authoritative value; it is not interpolated.
                    known.Status = player.Status;
                    continue;
                }
                Color tint = player.Role == (byte)Role.Killer ? Killer : Survivor;
                var root = new GameObject("Player " + player.Id);
                root.transform.SetParent(transform, false);
                GameObject body = materials.Primitive("Body", PrimitiveType.Capsule, Vector3.zero, CharacterLook.BodyScale, tint, true);
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = Vector3.up * CharacterLook.Height * .5f;
                GameObject visor = materials.Primitive("Visor", PrimitiveType.Cube, Vector3.zero, CharacterLook.VisorScale, CharacterLook.Visor);
                visor.transform.SetParent(root.transform, false);
                visor.transform.localPosition = CharacterLook.VisorOffset;
                var renderer = body.GetComponent<Renderer>();
                var avatar = new Avatar { Root = root, Body = renderer, Material = renderer.sharedMaterial, Tint = tint, Status = player.Status };
                root.SetActive(false);
                avatars.Add(player.Id, avatar);
            }
        }

        // Survivor view: the own character faces the local facing (HUD_SPEC 4.10) and is dithered while the boom is short.
        public void SetLocalLook(float facing, bool dither)
        {
            localFacing = facing; localDither = dither;
            if (dither && ditherMaterial == null) ditherMaterial = CharacterLook.Dither(Survivor, .4f);
        }

        public void Render(double now, SnapshotBuffer snapshots)
        {
            foreach (var entry in avatars)
            {
                Avatar avatar = entry.Value;
                bool own = entry.Key == localId;
                bool visible = snapshots.Sample(entry.Key, now, out Vector3 position, out float yaw, out float alpha)
                    && avatar.Status != (byte)PlayerStatus.Escaped && avatar.Status != (byte)PlayerStatus.Eliminated;
                avatar.Root.SetActive(visible);
                avatar.Root.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, own ? localFacing : yaw, 0));
                // Lagging Survivors use the Signal amber so the team can find them to rescue (team info). The own
                // character carries no state tint (HUD_SPEC 4.10: the meter is the only indicator).
                Color color = !own && avatar.Status == (byte)PlayerStatus.Lagging ? Signal : avatar.Tint;
                color.a = alpha;
                avatar.Material.color = color;
                Material wanted = own && localDither && ditherMaterial != null ? ditherMaterial : avatar.Material;
                if (avatar.Body.sharedMaterial != wanted) avatar.Body.sharedMaterial = wanted;
            }
            RenderKiller(now, snapshots);
        }

        public void Dispose()
        {
            if (ditherMaterial != null) Object.Destroy(ditherMaterial);
        }
    }
}
