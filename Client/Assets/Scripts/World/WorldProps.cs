using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // How an Exit is drawn. Closed covers both "not open yet" and Locked: ring only, muted.
    public enum ExitLook : byte { Closed, Open }

    // The static map (floor, buildings, obstacles), the terminals and the Exit rings. With the WorldArt catalog the map is the
    // imported city, fitted to the FixedMap boxes; without it (or with a catalog built for another map) it is the primitives.
    public sealed class WorldProps
    {
        private sealed class Terminal
        {
            public GameObject Root;
            public Material Panel;
        }

        private sealed class Exit
        {
            public GameObject Ring;
            public Material RingMaterial;
            public GameObject Marker;
            public ParticleSystem[] Particles;
            public ExitLook Look = ExitLook.Open; // differs from the first SetExitLook(Closed), so that call applies
        }

        private static readonly Color Survivor = WorldMaterials.Survivor;
        // Terminals: a square dark plinth with a muted panel on top, so from above they read as squares, not capsules.
        private static readonly Color Plinth = new Color32(66, 70, 77, 255);
        private static readonly Color PanelIdle = new Color32(154, 152, 144, 255);
        private static readonly Color Neutral = new Color32(232, 230, 225, 255); // `text`: the Killer's done terminal and open Exit
        private static readonly Color Muted = new Color32(154, 152, 144, 255); // `muted`: a closed or Locked Exit

        private readonly WorldMaterials materials;
        private readonly Transform transform;
        private readonly Role role;
        private readonly Dictionary<byte, Terminal> terminals = new Dictionary<byte, Terminal>();
        private readonly HashSet<byte> retained = new HashSet<byte>();
        private readonly List<byte> removed = new List<byte>();
        private readonly List<Mesh> batched = new List<Mesh>();
        private readonly Exit[] exits = new Exit[2];
        private readonly WorldArt catalog;
        private WorldArt art;

        public WorldProps(WorldMaterials materials, Role role) : this(materials, role, WorldArt.Load()) { }

        // catalog: the art to draw (null, or one built for another map, draws the primitives).
        public WorldProps(WorldMaterials materials, Role role, WorldArt catalog)
        {
            this.materials = materials;
            transform = materials.Parent;
            this.role = role;
            this.catalog = catalog;
        }

        // True when the imported city is drawn.
        public bool UsesArt => art != null;

        public void Initialize()
        {
            FixedMap map = SharedMap.Map;
            if (catalog != null && catalog.Matches(map)) BuildCity(catalog, map);
            else
            {
                if (catalog != null) Debug.LogWarning("[WorldProps] WorldArt was built for another map; drawing primitives. Run Ping the Killer/Setup World Art.");
                BuildPrimitives(map);
            }
            // Initial terminals are replaced by the first authoritative terminal list.
            for (byte i = 0; i < map.TerminalPositions.Count; i++)
                terminals.Add(i, MakeTerminal(i, new Vector3(map.TerminalPositions[i].X, 0, map.TerminalPositions[i].Z)));
            exits[0] = MakeExit("Exit", SharedMap.Exit);
            exits[1] = MakeExit("Exit 2", SharedMap.ExitAt(1));
            SetExitLook(0, ExitLook.Closed);
            SetExitLook(1, ExitLook.Closed);
        }

        private void BuildPrimitives(FixedMap map)
        {
            float plane = FixedMap.Size / 10; // a Unity plane is 10 x 10
            materials.Primitive("Floor", PrimitiveType.Plane, SharedMap.Center, new Vector3(plane, 1, plane), new Color32(26, 28, 31, 255));
            foreach (Wall wall in map.Walls)
            {
                SharedMap.WallBox(wall, out Vector3 center, out Vector3 size);
                materials.Primitive("Wall", PrimitiveType.Cube, center + Vector3.up * MapGeometry.WallHeight * .5f, size + Vector3.up * MapGeometry.WallHeight, new Color32(80, 83, 87, 255));
            }
            foreach (Obstacle obstacle in map.Obstacles)
            {
                var center = new Vector3((obstacle.MinX + obstacle.MaxX) * .5f, MapGeometry.ObstacleHeight * .5f, (obstacle.MinZ + obstacle.MaxZ) * .5f);
                var size = new Vector3(obstacle.MaxX - obstacle.MinX, MapGeometry.ObstacleHeight, obstacle.MaxZ - obstacle.MinZ);
                materials.Primitive("Obstacle", PrimitiveType.Cube, center, size, new Color32(80, 83, 87, 255));
            }
        }

        // The city: every piece is a prefab fitted to its FixedMap box by WorldArtSetup. It is static, so in play mode it is
        // combined into static batches once (edit-mode tests read the individual renderers).
        private void BuildCity(WorldArt pieces, FixedMap map)
        {
            art = pieces;
            var city = new GameObject("City");
            city.transform.SetParent(transform, false);
            Place(pieces.Ground, city.transform, Vector3.zero, "Ground");
            Place(pieces.Backdrop, city.transform, Vector3.zero, "Backdrop");
            for (int i = 0; i < map.Walls.Count; i++)
            {
                SharedMap.WallBox(map.Walls[i], out Vector3 center, out _);
                Place(pieces.Walls[i].Prefab, city.transform, center, "Building " + i);
            }
            for (int i = 0; i < map.Obstacles.Count; i++)
            {
                Obstacle obstacle = map.Obstacles[i];
                Place(pieces.Obstacles[i].Prefab, city.transform, new Vector3((obstacle.MinX + obstacle.MaxX) * .5f, 0, (obstacle.MinZ + obstacle.MaxZ) * .5f), "Obstacle " + i);
            }
            if (!Application.isPlaying) return;
            StaticBatchingUtility.Combine(city);
            foreach (MeshFilter filter in city.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("Combined Mesh") && !batched.Contains(filter.sharedMesh)) batched.Add(filter.sharedMesh);
        }

        private static void Place(GameObject prefab, Transform parent, Vector3 position, string label)
        {
            if (prefab == null) return;
            GameObject item = Object.Instantiate(prefab, parent);
            item.name = label;
            item.transform.localPosition = position;
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
                SetTerminalLook(item, terminal.Progress == 100);
            }
            foreach (var terminal in terminals)
                if (!retained.Contains(terminal.Key)) { Object.Destroy(terminal.Value.Root); removed.Add(terminal.Key); }
            foreach (byte id in removed) terminals.Remove(id);
            bool open = state.ExitOpen != 0 && state.ExitLocked == 0;
            SetExitLook(0, open ? ExitLook.Open : ExitLook.Closed);
        }

        // The terminal state -> look mapping (HUD_SPEC 0.4 / 6.2): the screen of a done terminal shows team teal to Survivors and
        // `text` to the Killer; idle is the muted panel. No numbers, no blinking.
        private void SetTerminalLook(Terminal item, bool done)
        {
            if (item == null || item.Panel == null) { WarnOnce("SetTerminalLook: no such terminal"); return; }
            item.Panel.color = done ? (role == Role.Survivor ? Survivor : Neutral) : PanelIdle;
        }

        // The Exit state -> look mapping (Q11): open = ally ring and marker for Survivors, `text` ring and marker for the Killer;
        // closed or Locked = the muted ring only. The ring is the exact escape boundary in both looks. The marker's particles
        // are started and cleared here explicitly (the prefab neither plays on awake nor relies on its activation).
        public void SetExitLook(int exit, ExitLook look)
        {
            if (exit < 0 || exit >= exits.Length || exits[exit] == null) { WarnOnce("SetExitLook: no Exit " + exit); return; }
            Exit item = exits[exit];
            if (item.Look == look) return;
            item.Look = look;
            item.RingMaterial.color = look == ExitLook.Open ? (role == Role.Survivor ? Survivor : Neutral) : Muted;
            if (item.Marker == null) return;
            if (look == ExitLook.Open)
            {
                Color tint = item.RingMaterial.color;
                item.Marker.SetActive(true);
                for (int i = 0; i < item.Particles.Length; i++)
                {
                    ParticleSystem.MainModule main = item.Particles[i].main;
                    main.startColor = new Color(tint.r, tint.g, tint.b, main.startColor.color.a);
                    item.Particles[i].Play(false);
                }
            }
            else
            {
                for (int i = 0; i < item.Particles.Length; i++) item.Particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                item.Marker.SetActive(false);
            }
        }

        public ExitLook ExitLookOf(int exit) => exit >= 0 && exit < exits.Length && exits[exit] != null ? exits[exit].Look : ExitLook.Closed;

        private bool warned;

        private void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[WorldProps] " + message + " (ignored)");
        }

        private Terminal MakeTerminal(byte id, Vector3 position)
        {
            if (art != null) return MakeArtTerminal(id, position);
            var root = new GameObject("Terminal " + id);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            GameObject plinth = materials.Primitive("Plinth", PrimitiveType.Cube, position + Vector3.up * .45f, new Vector3(1.2f, .9f, 1.2f), Plinth);
            GameObject panel = materials.Primitive("Panel", PrimitiveType.Cube, position + Vector3.up * .93f, new Vector3(.8f, .06f, .8f), PanelIdle);
            plinth.transform.SetParent(root.transform, true);
            panel.transform.SetParent(root.transform, true);
            return new Terminal { Root = root, Panel = panel.GetComponent<Renderer>().sharedMaterial };
        }

        // The imported terminal: a device on a plinth. Its screen gets an own material through the shared material path.
        private Terminal MakeArtTerminal(byte id, Vector3 position)
        {
            GameObject root = Object.Instantiate(art.Terminal, transform);
            root.name = "Terminal " + id;
            root.transform.position = position;
            Material panel = materials.MakeMaterial(PanelIdle);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                if (renderer.name == WorldArt.ScreenName) renderer.sharedMaterial = panel;
            return new Terminal { Root = root, Panel = panel };
        }

        private Exit MakeExit(string label, Vector3 position)
        {
            var item = new Exit { Ring = Ring(label, position, GameConstants.ExitRadius, .16f, Muted) };
            item.RingMaterial = item.Ring.GetComponent<MeshRenderer>().sharedMaterial;
            if (art == null) return item;
            item.Marker = Object.Instantiate(art.ExitMarker, transform);
            item.Marker.name = label + " marker";
            item.Marker.transform.position = new Vector3(position.x, 0, position.z);
            item.Marker.SetActive(false);
            item.Particles = item.Marker.GetComponentsInChildren<ParticleSystem>(true);
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
            item.GetComponent<MeshRenderer>().sharedMaterial = materials.MakeMaterial(color);
            return item;
        }

        public void Dispose()
        {
            foreach (Exit item in exits) DisposeRing(item?.Ring);
            foreach (Mesh mesh in batched) Object.Destroy(mesh);
            batched.Clear();
        }

        private static void DisposeRing(GameObject ring)
        {
            if (ring == null) return;
            MeshFilter filter = ring.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
        }
    }
}
