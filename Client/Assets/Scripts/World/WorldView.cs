using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    public sealed class WorldView : MonoBehaviour
    {
        private sealed class Avatar
        {
            public GameObject Body;
            public Material Material;
            public GameObject Marker;
            public Color Tint;
            public byte Status;
        }
        private readonly Dictionary<int, Avatar> avatars = new Dictionary<int, Avatar>();
        private readonly Dictionary<byte, GameObject> terminals = new Dictionary<byte, GameObject>();
        private readonly List<Material> materials = new List<Material>();
        private readonly SnapshotBuffer snapshots = new SnapshotBuffer();
        private readonly HashSet<byte> retained = new HashSet<byte>();
        private readonly List<byte> removed = new List<byte>();
        private GameObject exit;
        private int localId;
        private Role role;
        private static readonly Color Killer = new Color32(229, 72, 77, 255);
        private static readonly Color Survivor = new Color32(63, 184, 175, 255);
        private static readonly Color Signal = new Color32(255, 176, 32, 255);

        public void Initialize(int id, Role localRole)
        {
            localId = id; role = localRole;
            FixedMap map = SharedMap.Map;
            float plane = FixedMap.Size / 10; // a Unity plane is 10 x 10
            Primitive("Floor", PrimitiveType.Plane, SharedMap.Center, new Vector3(plane, 1, plane), new Color32(26, 28, 31, 255));
            foreach (Wall wall in map.Walls)
            {
                SharedMap.WallBox(wall, out Vector3 center, out Vector3 size);
                Primitive("Wall", PrimitiveType.Cube, center + Vector3.up * 1.5f, size + Vector3.up * 3, new Color32(80, 83, 87, 255));
            }
            // Initial terminals are replaced by the first authoritative terminal list.
            for (byte i = 0; i < map.TerminalPositions.Count; i++)
                terminals.Add(i, MakeTerminal(i, new Vector3(map.TerminalPositions[i].X, 0, map.TerminalPositions[i].Z)));
            exit = Ring("Exit", SharedMap.Exit, GameConstants.ExitRadius, .16f, new Color32(154, 152, 144, 255));
        }

        public void Accept(S2C_WorldState state, double time)
        {
            snapshots.Add(state, time);
            foreach (PlayerState player in state.Players)
            {
                if (role == Role.Survivor && player.Role == (byte)Role.Killer) continue;
                if (avatars.TryGetValue(player.Id, out Avatar known))
                {
                    // Status is the latest authoritative value; it is not interpolated.
                    known.Status = player.Status;
                    continue;
                }
                Color tint = player.Role == (byte)Role.Killer ? Killer : Survivor;
                GameObject body = Primitive("Player " + player.Id, PrimitiveType.Capsule, new Vector3(player.X, 1, player.Z), Vector3.one, tint, true);
                var avatar = new Avatar { Body = body, Material = body.GetComponent<Renderer>().sharedMaterial, Tint = tint, Status = player.Status };
                if (player.Id == localId)
                    avatar.Marker = Ring("Local player", new Vector3(player.X, .05f, player.Z), .7f, .08f, new Color32(232, 230, 225, 255));
                body.SetActive(false);
                avatars.Add(player.Id, avatar);
            }
            retained.Clear(); removed.Clear();
            foreach (TerminalState terminal in state.Terminals)
            {
                retained.Add(terminal.Id);
                if (!terminals.TryGetValue(terminal.Id, out GameObject item))
                    terminals.Add(terminal.Id, item = MakeTerminal(terminal.Id, new Vector3(terminal.X, 0, terminal.Z)));
                item.transform.position = new Vector3(terminal.X, .4f, terminal.Z);
                item.GetComponent<Renderer>().sharedMaterial.color = terminal.Progress == 100 ? Survivor : new Color32(154, 152, 144, 255);
            }
            foreach (var terminal in terminals)
                if (!retained.Contains(terminal.Key)) { Destroy(terminal.Value); removed.Add(terminal.Key); }
            foreach (byte id in removed) terminals.Remove(id);
            bool open = state.ExitOpen != 0 && state.ExitLocked == 0;
            exit.GetComponent<MeshRenderer>().sharedMaterial.color = open ? Survivor : new Color32(154, 152, 144, 255);
        }

        public void Render(double now)
        {
            foreach (var entry in avatars)
            {
                Avatar avatar = entry.Value;
                bool visible = snapshots.Sample(entry.Key, now, out Vector3 position, out float yaw, out float alpha)
                    && avatar.Status != (byte)PlayerStatus.Escaped && avatar.Status != (byte)PlayerStatus.Eliminated;
                avatar.Body.SetActive(visible && !(role == Role.Survivor && entry.Key == localId));
                avatar.Body.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
                // Survivor view: Lagging allies use the Signal amber so the team can find them to rescue (team info).
                Color color = role == Role.Survivor && avatar.Status == (byte)PlayerStatus.Lagging ? Signal : avatar.Tint;
                color.a = alpha;
                avatar.Material.color = color;
                if (avatar.Marker != null)
                {
                    avatar.Marker.SetActive(visible && role == Role.Killer);
                    avatar.Marker.transform.position = new Vector3(position.x, .05f, position.z);
                }
            }
        }

        public bool LocalPosition(double now, out Vector3 position)
        {
            return snapshots.Sample(localId, now, out position, out _, out _);
        }

        private GameObject MakeTerminal(byte id, Vector3 position) => Primitive("Terminal " + id, PrimitiveType.Cylinder,
            position + Vector3.up * .4f, new Vector3(.8f, .4f, .8f), new Color32(154, 152, 144, 255));

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
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && (filter.name == "Exit" || filter.name == "Local player")) Destroy(filter.sharedMesh);
        }
    }
}
