using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // The static map (floor, walls), the terminals and the Exit ring.
    public sealed class WorldProps
    {
        private sealed class Terminal
        {
            public GameObject Root;
            public Material Panel;
        }

        private static readonly Color Survivor = WorldMaterials.Survivor;
        // Terminals: a square dark plinth with a muted panel on top, so from above they read as squares, not capsules.
        private static readonly Color Plinth = new Color32(66, 70, 77, 255);
        private static readonly Color PanelIdle = new Color32(154, 152, 144, 255);
        private static readonly Color Neutral = new Color32(232, 230, 225, 255); // `text`: the Killer's done terminal and open Exit

        private readonly WorldMaterials materials;
        private readonly Transform transform;
        private readonly Role role;
        private readonly Dictionary<byte, Terminal> terminals = new Dictionary<byte, Terminal>();
        private readonly HashSet<byte> retained = new HashSet<byte>();
        private readonly List<byte> removed = new List<byte>();
        private GameObject exit;

        public WorldProps(WorldMaterials materials, Role role)
        {
            this.materials = materials;
            transform = materials.Parent;
            this.role = role;
        }

        public void Initialize()
        {
            FixedMap map = SharedMap.Map;
            float plane = FixedMap.Size / 10; // a Unity plane is 10 x 10
            materials.Primitive("Floor", PrimitiveType.Plane, SharedMap.Center, new Vector3(plane, 1, plane), new Color32(26, 28, 31, 255));
            foreach (Wall wall in map.Walls)
            {
                SharedMap.WallBox(wall, out Vector3 center, out Vector3 size);
                materials.Primitive("Wall", PrimitiveType.Cube, center + Vector3.up * MapGeometry.WallHeight * .5f, size + Vector3.up * MapGeometry.WallHeight, new Color32(80, 83, 87, 255));
            }
            // Initial terminals are replaced by the first authoritative terminal list.
            for (byte i = 0; i < map.TerminalPositions.Count; i++)
                terminals.Add(i, MakeTerminal(i, new Vector3(map.TerminalPositions[i].X, 0, map.TerminalPositions[i].Z)));
            exit = Ring("Exit", SharedMap.Exit, GameConstants.ExitRadius, .16f, new Color32(154, 152, 144, 255));
        }

        public void Accept(S2C_WorldState state)
        {
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
                if (!retained.Contains(terminal.Key)) { Object.Destroy(terminal.Value.Root); removed.Add(terminal.Key); }
            foreach (byte id in removed) terminals.Remove(id);
            bool open = state.ExitOpen != 0 && state.ExitLocked == 0;
            exit.GetComponent<MeshRenderer>().sharedMaterial.color = open ? (role == Role.Survivor ? Survivor : Neutral) : new Color32(154, 152, 144, 255); // Q11: ally ring for Survivors, text ring for the Killer
        }

        private Terminal MakeTerminal(byte id, Vector3 position)
        {
            var root = new GameObject("Terminal " + id);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            GameObject plinth = materials.Primitive("Plinth", PrimitiveType.Cube, position + Vector3.up * .45f, new Vector3(1.2f, .9f, 1.2f), Plinth);
            GameObject panel = materials.Primitive("Panel", PrimitiveType.Cube, position + Vector3.up * .93f, new Vector3(.8f, .06f, .8f), PanelIdle);
            plinth.transform.SetParent(root.transform, true);
            panel.transform.SetParent(root.transform, true);
            return new Terminal { Root = root, Panel = panel.GetComponent<Renderer>().sharedMaterial };
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
            item.GetComponent<MeshRenderer>().sharedMaterial = materials.MakeMaterial(color);
            return item;
        }

        public void Dispose()
        {
            if (exit == null) return;
            MeshFilter filter = exit.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
        }
    }
}
