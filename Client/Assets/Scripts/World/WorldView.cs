using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // The static map plus, on a Survivor client, the Survivor characters (Id-based SnapshotBuffer interpolation) and the
    // Killer model. The Killer model is drawn only from the Killer entry of the snapshots (SnapshotBuffer.SampleKiller),
    // which the server sends to a Survivor only while its line of sight to the Killer is clear (HUD_SPEC 4.10 (10/30)): no
    // marker, outline or through-wall indicator, and nothing else (threat level, effects) ever places it.
    // The Killer client draws no character here: it has no body, and Survivors reach it only as anonymous blips
    // (Killer/KillerWorldLayer). Its own position is read by Role.
    public sealed class WorldView : MonoBehaviour
    {
        // Placeholder Killer model until the D3 assets: the Survivor capsule shape, 10 % taller, in the Killer red, with the visor.
        private sealed class KillerModel
        {
            public GameObject Root;
            public Material Material;
            public Material Visor;
        }
        private KillerModel killerModel;
        public static readonly Vector3 KillerBodyScale = new Vector3(CharacterLook.Width, CharacterLook.Height * .55f, CharacterLook.Width);
        // Alpha the Killer model was drawn with in the last Render (0 = hidden); read by the dev capture.
        public float KillerAlpha { get; private set; }
        // Alpha of the Killer's visor material after the last Render; it fades with the body (KillerAlpha).
        public float KillerVisorAlpha { get; private set; }

        // A spectator (own status Eliminated or Escaped) never sees the Killer model, even if an entry arrives (U4e Q-04b).
        public static bool KillerDrawable(byte ownStatus)
            => ownStatus != (byte)PlayerStatus.Eliminated && ownStatus != (byte)PlayerStatus.Escaped;
        private sealed class Avatar
        {
            public GameObject Root;
            public Renderer Body;
            public Material Material;
            public Color Tint;
            public byte Status;
        }
        private readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();
        private sealed class Terminal
        {
            public GameObject Root;
            public Material Panel;
        }
        private readonly Dictionary<byte, Terminal> terminals = new Dictionary<byte, Terminal>();
        private readonly List<Material> materials = new List<Material>();
        private readonly SnapshotBuffer snapshots = new SnapshotBuffer();
        private readonly HashSet<byte> retained = new HashSet<byte>();
        private readonly List<byte> removed = new List<byte>();
        private GameObject exit;
        private int localId;
        private Role role;
        private float localFacing;
        private bool localDither;
        private Material ditherMaterial;
        private static readonly Color Killer = new Color32(229, 72, 77, 255);
        private static readonly Color Survivor = new Color32(63, 184, 175, 255);
        private static readonly Color Signal = new Color32(255, 176, 32, 255);
        // Terminals: a square dark plinth with a muted panel on top, so from above they read as squares, not capsules.
        private static readonly Color Plinth = new Color32(66, 70, 77, 255);
        private static readonly Color PanelIdle = new Color32(154, 152, 144, 255);
        private static readonly Color Neutral = new Color32(232, 230, 225, 255); // `text`: the Killer's done terminal and open Exit

        public void Initialize(int id, Role localRole)
        {
            localId = id; role = localRole;
            FixedMap map = SharedMap.Map;
            float plane = FixedMap.Size / 10; // a Unity plane is 10 x 10
            Primitive("Floor", PrimitiveType.Plane, SharedMap.Center, new Vector3(plane, 1, plane), new Color32(26, 28, 31, 255));
            foreach (Wall wall in map.Walls)
            {
                SharedMap.WallBox(wall, out Vector3 center, out Vector3 size);
                Primitive("Wall", PrimitiveType.Cube, center + Vector3.up * MapGeometry.WallHeight * .5f, size + Vector3.up * MapGeometry.WallHeight, new Color32(80, 83, 87, 255));
            }
            // Initial terminals are replaced by the first authoritative terminal list.
            for (byte i = 0; i < map.TerminalPositions.Count; i++)
                terminals.Add(i, MakeTerminal(i, new Vector3(map.TerminalPositions[i].X, 0, map.TerminalPositions[i].Z)));
            exit = Ring("Exit", SharedMap.Exit, GameConstants.ExitRadius, .16f, new Color32(154, 152, 144, 255));
            if (role == Role.Survivor) killerModel = MakeKillerModel();
        }

        private KillerModel MakeKillerModel()
        {
            var root = new GameObject("Killer (line of sight)");
            root.transform.SetParent(transform, false);
            GameObject body = Primitive("Killer body", PrimitiveType.Capsule, Vector3.zero, KillerBodyScale, Killer, true);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * KillerBodyScale.y;
            // Transparent like the body, so it fades with it (U4e Q-03).
            GameObject visor = Primitive("Killer visor", PrimitiveType.Cube, Vector3.zero, CharacterLook.VisorScale, CharacterLook.Visor, true);
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
        private void RenderKiller(double now)
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

        public void Accept(S2C_WorldState state, double time)
        {
            snapshots.Add(state, time);
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
                GameObject body = Primitive("Body", PrimitiveType.Capsule, Vector3.zero, CharacterLook.BodyScale, tint, true);
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = Vector3.up * CharacterLook.Height * .5f;
                GameObject visor = Primitive("Visor", PrimitiveType.Cube, Vector3.zero, CharacterLook.VisorScale, CharacterLook.Visor);
                visor.transform.SetParent(root.transform, false);
                visor.transform.localPosition = CharacterLook.VisorOffset;
                var renderer = body.GetComponent<Renderer>();
                var avatar = new Avatar { Root = root, Body = renderer, Material = renderer.sharedMaterial, Tint = tint, Status = player.Status };
                root.SetActive(false);
                avatars.Add(player.Id, avatar);
            }
            retained.Clear(); removed.Clear();
            foreach (TerminalState terminal in state.Terminals)
            {
                retained.Add(terminal.Id);
                if (!terminals.TryGetValue(terminal.Id, out Terminal item))
                    terminals.Add(terminal.Id, item = MakeTerminal(terminal.Id, new Vector3(terminal.X, 0, terminal.Z)));
                item.Root.transform.position = new Vector3(terminal.X, 0, terminal.Z);
                item.Panel.color = terminal.Progress == 100 ? (role == Role.Survivor ? Survivor : Neutral) : PanelIdle; // done: team teal for Survivors, `text` for the Killer (HUD_SPEC 0.4)
            }
            foreach (var terminal in terminals)
                if (!retained.Contains(terminal.Key)) { Destroy(terminal.Value.Root); removed.Add(terminal.Key); }
            foreach (byte id in removed) terminals.Remove(id);
            bool open = state.ExitOpen != 0 && state.ExitLocked == 0;
            exit.GetComponent<MeshRenderer>().sharedMaterial.color = open ? (role == Role.Survivor ? Survivor : Neutral) : new Color32(154, 152, 144, 255); // Q11: ally ring for Survivors, text ring for the Killer
        }

        // Survivor view: the own character faces the local facing (HUD_SPEC 4.10) and is dithered while the boom is short.
        public void SetLocalLook(float facing, bool dither)
        {
            localFacing = facing; localDither = dither;
            if (dither && ditherMaterial == null) ditherMaterial = CharacterLook.Dither(Survivor, .4f);
        }

        public void Render(double now)
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
            RenderKiller(now);
        }

        // Survivor: the own entry by Id. Killer: the own entry by Role (HUD_SPEC 6.2).
        public bool LocalPosition(double now, out Vector3 position)
        {
            return snapshots.Sample(role == Role.Killer ? SnapshotBuffer.KillerKey : localId, now, out position, out _, out _);
        }

        private Terminal MakeTerminal(byte id, Vector3 position)
        {
            var root = new GameObject("Terminal " + id);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            GameObject plinth = Primitive("Plinth", PrimitiveType.Cube, position + Vector3.up * .45f, new Vector3(1.2f, .9f, 1.2f), Plinth);
            GameObject panel = Primitive("Panel", PrimitiveType.Cube, position + Vector3.up * .93f, new Vector3(.8f, .06f, .8f), PanelIdle);
            plinth.transform.SetParent(root.transform, true);
            panel.transform.SetParent(root.transform, true);
            return new Terminal { Root = root, Panel = panel.GetComponent<Renderer>().sharedMaterial };
        }

        private Material MakeMaterial(Color color, bool transparent = false)
        {
            var template = Resources.Load<Material>(transparent ? "WorldLitFade" : "WorldLit");
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color;
            if (transparent)
            {
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            materials.Add(material);
            return material;
        }

        private GameObject Primitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color, bool transparent = false)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = label;
            item.transform.SetParent(transform, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color, transparent);
            Destroy(item.GetComponent<Collider>()); // Positions and collisions are server-owned.
            return item;
        }

        private GameObject Ring(string label, Vector3 position, float radius, float width, Color color)
        {
            const int segments = 64;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                vertices[i * 2] = direction * radius;
                vertices[i * 2 + 1] = direction * (radius - width);
                int next = (i + 1) % segments * 2, t = i * 6;
                triangles[t] = i * 2; triangles[t + 1] = next; triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = next; triangles[t + 4] = next + 1; triangles[t + 5] = i * 2 + 1;
            }
            var mesh = new Mesh { name = label, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(transform, false);
            item.transform.position = position;
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            item.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);
            return item;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials) Destroy(material);
            if (ditherMaterial != null) Destroy(ditherMaterial);
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.name == "Exit") Destroy(filter.sharedMesh);
        }
    }
}
