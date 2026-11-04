using PingTheKiller.Client.Settings;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // The escape hatch (GDD v2 section 3 / U6a R6): a procedural steel floor hatch about 1.2 m across, drawn for both roles at
    // the position of the last snapshot that carried one. The server sends it to a recipient only while it is open and within
    // 15 m with a clear line, so showing and hiding follow HatchVisible exactly. A raised coaming with a near-black opening, a
    // grate and an open lid; the light rim is the one readability cue: it eases between two flat tones at most once per second
    // (a static mid tone under Reduced motion). Plain toon materials, nothing emissive, nothing glows (SLOP V3).
    public sealed class HatchView
    {
        public const float Width = 1.2f;
        public const float PulseHertz = .8f;
        public static readonly Color Steel = new Color32(74, 80, 88, 255);
        public static readonly Color Opening = new Color32(6, 7, 9, 255);
        public static readonly Color Bars = new Color32(104, 110, 118, 255);
        public static readonly Color RimDim = new Color32(196, 201, 206, 255);
        public static readonly Color RimBright = new Color32(244, 245, 246, 255);

        private const float Coaming = .3f, FrameWidth = .18f, LidHeight = .95f, LidTilt = 80f;

        private readonly WorldMaterials materials;
        private GameObject root;
        private Material rim;
        private bool shown, reduced;
        private float lastX = float.NaN, lastZ = float.NaN;

        public HatchView(WorldMaterials materials)
        {
            this.materials = materials;
        }

        public bool Shown => shown;
        public Vector3 Position => root != null ? root.transform.position : Vector3.zero;
        public bool Built => root != null;
        public GameObject Root => root;
        public Color RimColor => rim != null ? rim.color : Color.black;

        // Overrides the persisted Reduced motion option (tests, and a caller that already holds the player's settings).
        public bool? ReducedMotionOverride { get; set; }

        public void Set(bool visible, float x, float z)
        {
            if (!visible)
            {
                if (shown) { shown = false; root.SetActive(false); }
                return;
            }
            if (root == null) Build();
            if (!shown)
            {
                shown = true;
                root.SetActive(true);
                reduced = ReducedMotionOverride ?? GameSettings.Load(new PlayerPrefsStore()).ReducedMotion;
                lastX = lastZ = float.NaN;
            }
            if (x == lastX && z == lastZ) return;
            lastX = x; lastZ = z;
            root.transform.position = new Vector3(x, 0, z);
        }

        public void Render(double now)
        {
            if (!shown || rim == null) return;
            if (reduced) { rim.color = Color.Lerp(RimDim, RimBright, .5f); return; }
            float wave = .5f - .5f * Mathf.Cos((float)(now * PulseHertz * 2 * System.Math.PI));
            rim.color = Color.Lerp(RimDim, RimBright, wave);
        }

        private void Build()
        {
            root = new GameObject("Hatch");
            root.transform.SetParent(materials.Parent, false);
            float half = Width * .5f;
            // Base plate and coaming: a dark steel block whose top sits above the road markings (nothing in the floor art is taller than 0.15 m).
            Part("Base", PrimitiveType.Cube, new Vector3(0, Coaming * .5f, 0), new Vector3(Width, Coaming, Width), Steel);
            Part("Opening", PrimitiveType.Cube, new Vector3(0, Coaming + .002f, 0), new Vector3(Width - 2 * FrameWidth, .004f, Width - 2 * FrameWidth), Opening);
            for (int i = -1; i <= 1; i++)
                Part("Bar", PrimitiveType.Cube, new Vector3(i * .26f, Coaming + .03f, 0), new Vector3(.06f, .05f, Width - 2 * FrameWidth), Bars);
            // The rim: four flat bars on the top edge, one shared material (the pulse).
            Color start = Color.Lerp(RimDim, RimBright, .5f);
            Material rimMaterial = materials.MakeMaterial(start);
            float edge = half - FrameWidth * .5f, top = Coaming + .03f;
            Part("Rim N", PrimitiveType.Cube, new Vector3(0, top, edge), new Vector3(Width, .06f, FrameWidth), rimMaterial);
            Part("Rim S", PrimitiveType.Cube, new Vector3(0, top, -edge), new Vector3(Width, .06f, FrameWidth), rimMaterial);
            Part("Rim E", PrimitiveType.Cube, new Vector3(edge, top, 0), new Vector3(FrameWidth, .06f, Width - 2 * FrameWidth), rimMaterial);
            Part("Rim W", PrimitiveType.Cube, new Vector3(-edge, top, 0), new Vector3(FrameWidth, .06f, Width - 2 * FrameWidth), rimMaterial);
            rim = rimMaterial;
            // The open lid, hinged along the far edge and leaning slightly past vertical: a silhouette that reads from a distance
            // while the flat parts foreshorten to a sliver.
            var hinge = new GameObject("Lid hinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(0, top, edge + FrameWidth * .5f);
            hinge.transform.localRotation = Quaternion.Euler(-LidTilt, 0, 0);
            GameObject lid = materials.Primitive("Lid", PrimitiveType.Cube, Vector3.zero, new Vector3(Width, .05f, LidHeight), Steel);
            lid.transform.SetParent(hinge.transform, false);
            lid.transform.localPosition = new Vector3(0, 0, LidHeight * .5f);
            lid.transform.localScale = new Vector3(Width, .05f, LidHeight);
            GameObject lidEdge = materials.Primitive("Lid edge", PrimitiveType.Cube, Vector3.zero, Vector3.one, rimMaterial.color);
            lidEdge.GetComponent<Renderer>().sharedMaterial = rimMaterial;
            lidEdge.transform.SetParent(hinge.transform, false);
            lidEdge.transform.localPosition = new Vector3(0, .02f, LidHeight);
            lidEdge.transform.localScale = new Vector3(Width, .07f, .12f);
            root.SetActive(false);
        }

        private void Part(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            GameObject item = materials.Primitive(label, type, position, scale, color);
            item.transform.SetParent(root.transform, true);
        }

        private void Part(string label, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject item = materials.Primitive(label, type, position, scale, Color.white);
            item.GetComponent<Renderer>().sharedMaterial = material;
            item.transform.SetParent(root.transform, true);
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            root = null; rim = null; shown = false;
        }
    }
}
