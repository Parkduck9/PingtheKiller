using System.Collections.Generic;
using PingTheKiller.Client.Settings;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 15.1 (Q5): the Shutters and Rails, drawn for both roles at the WorldChaseProps boxes. The prefabs come from the
    // ChaseProps catalog (Editor/Setup/ChasePropSetup); without it, or with one built for other boxes, the same
    // ChasePropShapes are built from primitives. Shutter looks follow the WorldState shutter bytes only: the first snapshot
    // (a match start, a late join or a reconnect) sets every look at once; a later Up -> Down change drops the panel out of
    // the housing in DropSeconds (at once under Reduced motion). Props carry no colliders: movement and sight are server-owned.
    public sealed class ChasePropsView
    {
        public const float DropSeconds = .12f;

        private sealed class Shutter
        {
            public GameObject Root, Up, Down, Broken;
            public ShutterLook Look;
            public double DropStart = double.NegativeInfinity;
        }

        private readonly WorldMaterials materials;
        private readonly ChasePropArt catalog;
        private readonly Dictionary<ChasePropShapes.Paint, Material> paints = new Dictionary<ChasePropShapes.Paint, Material>();
        private readonly List<Shutter> shutters = new List<Shutter>();
        private readonly List<GameObject> rails = new List<GameObject>();
        private GameObject root;
        private bool known;

        public ChasePropsView(WorldMaterials materials) : this(materials, ChasePropArt.Load()) { }

        public ChasePropsView(WorldMaterials materials, ChasePropArt catalog)
        {
            this.materials = materials;
            this.catalog = catalog;
        }

        public bool UsesArt { get; private set; }
        public bool? ReducedMotionOverride { get; set; }
        public GameObject Root => root;
        public int ShutterCount => shutters.Count;
        public int RailCount => rails.Count;
        public ShutterLook LookOf(int id) => id >= 0 && id < shutters.Count ? shutters[id].Look : ShutterLook.Up;
        public GameObject ShutterRoot(int id) => shutters[id].Root;
        public GameObject RailRoot(int id) => rails[id];
        // The group shown for a shutter (exactly one of Up / Down / Broken is active).
        public bool GroupShown(int id, ShutterLook look) => Group(shutters[id], look).activeSelf;
        public float DropOffset(int id) => shutters[id].Down.transform.localPosition.y;

        public void Initialize()
        {
            root = new GameObject("Chase props");
            root.transform.SetParent(materials.Parent, false);
            UsesArt = catalog != null && catalog.Matches();
            if (catalog != null && !UsesArt) Debug.LogWarning("[ChaseProps] The ChaseProps catalog was built for other boxes; drawing primitives. Run Ping the Killer/Setup Chase Props.");
            IReadOnlyList<ChaseProp> list = WorldChaseProps.Shutters;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject item = UsesArt ? Place(catalog.Shutters[i].Prefab, list[i], "Shutter " + i) : Build(ChasePropShapes.ShutterParts(list[i]), list[i], "Shutter " + i);
                var shutter = new Shutter
                {
                    Root = item,
                    Up = item.transform.Find(ChasePropShapes.Up).gameObject,
                    Down = item.transform.Find(ChasePropShapes.Down).gameObject,
                    Broken = item.transform.Find(ChasePropShapes.Broken).gameObject,
                };
                shutters.Add(shutter);
                Show(shutter, ShutterLook.Up);
            }
            list = WorldChaseProps.Rails;
            for (int i = 0; i < list.Count; i++)
                rails.Add(UsesArt ? Place(catalog.Rails[i].Prefab, list[i], "Rail " + i) : Build(ChasePropShapes.RailParts(list[i]), list[i], "Rail " + i));
        }

        // Each WorldState: the shutter bytes (missing or short lists draw Up).
        public void Accept(IReadOnlyList<byte> states, double now)
        {
            bool reduced = ReducedMotionOverride ?? GameSettings.Load(new PlayerPrefsStore()).ReducedMotion;
            for (int i = 0; i < shutters.Count; i++)
            {
                ShutterLook look = ChasePropLooks.Of(states, i);
                Shutter shutter = shutters[i];
                if (look == shutter.Look && known) continue;
                bool drop = known && look == ShutterLook.Down && shutter.Look == ShutterLook.Up && !reduced;
                Show(shutter, look);
                shutter.DropStart = drop ? now : double.NegativeInfinity;
                SetDrop(shutter, drop ? 0 : 1);
            }
            known = true;
        }

        public void Render(double now)
        {
            foreach (Shutter shutter in shutters)
            {
                if (double.IsNegativeInfinity(shutter.DropStart)) continue;
                float u = Mathf.Clamp01((float)((now - shutter.DropStart) / DropSeconds));
                SetDrop(shutter, u);
                if (u >= 1) shutter.DropStart = double.NegativeInfinity;
            }
        }

        // u = 0: the panel still under the housing; 1: on the floor. Ease-in (it falls).
        private static void SetDrop(Shutter shutter, float u)
        {
            float lift = (ChasePropShapes.PostHeight - ChasePropShapes.PanelHeight) * (1 - u * u);
            shutter.Down.transform.localPosition = new Vector3(0, lift, 0);
        }

        private static void Show(Shutter shutter, ShutterLook look)
        {
            shutter.Look = look;
            shutter.Up.SetActive(look == ShutterLook.Up);
            shutter.Down.SetActive(look == ShutterLook.Down);
            shutter.Broken.SetActive(look == ShutterLook.Broken);
        }

        private static GameObject Group(Shutter shutter, ShutterLook look) => look == ShutterLook.Down ? shutter.Down : look == ShutterLook.Broken ? shutter.Broken : shutter.Up;

        private GameObject Place(GameObject prefab, ChaseProp prop, string label)
        {
            GameObject item = Object.Instantiate(prefab, root.transform);
            item.name = label;
            item.transform.SetPositionAndRotation(ChasePropShapes.Center(prop), Quaternion.Euler(0, ChasePropShapes.Yaw(prop), 0));
            return item;
        }

        // The primitive fallback: the same parts, one shared material per paint.
        private GameObject Build(List<ChasePropShapes.Part> parts, ChaseProp prop, string label)
        {
            var item = new GameObject(label);
            item.transform.SetParent(root.transform, false);
            item.transform.SetPositionAndRotation(ChasePropShapes.Center(prop), Quaternion.Euler(0, ChasePropShapes.Yaw(prop), 0));
            var groups = new Dictionary<string, Transform>();
            foreach (ChasePropShapes.Part part in parts)
            {
                if (!groups.TryGetValue(part.Group, out Transform group))
                {
                    group = new GameObject(part.Group).transform;
                    group.SetParent(item.transform, false);
                    groups.Add(part.Group, group);
                }
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = part.Name;
                CharacterLook.Release(piece.GetComponent<Collider>()); // positions and collisions are server-owned
                piece.GetComponent<Renderer>().sharedMaterial = Paint(part.Paint);
                piece.transform.SetParent(group, false);
                piece.transform.localPosition = part.Center;
                piece.transform.localRotation = Quaternion.Euler(part.Euler);
                piece.transform.localScale = part.Size;
            }
            return item;
        }

        private Material Paint(ChasePropShapes.Paint paint)
        {
            if (paints.TryGetValue(paint, out Material material)) return material;
            Color colour = ChasePropShapes.Colour(paint);
            material = materials.MakeMaterial(ChasePropShapes.Graded(paint) ? ToonShading.NightGrade(colour) : colour);
            paints.Add(paint, material);
            return material;
        }

        public void Dispose()
        {
            CharacterLook.Release(root);
            root = null; shutters.Clear(); rails.Clear(); known = false;
        }
    }
}
