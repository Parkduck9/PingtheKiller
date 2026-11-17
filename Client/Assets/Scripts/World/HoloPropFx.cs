using System;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // U10: the holographic props of one character, built once on first use and then only moved, scaled and toggled
    // (pooled per character: nothing is instantiated per frame).
    // - Hack: a translucent `ally` keyboard panel floating in front of the hands, pulsing on each keystroke beat, fed by a
    //   thin stream of small `ally` packet squares from the terminal top.
    // - Restore: an `ally` link line from the rescuer's hands to the Lagging ally, with packets travelling down it.
    // - Exit switch: packets streaming from the pressed palm into the switch.
    // - Killer jam: a `danger` glitch burst of squares at the terminal top on the slam's impact.
    // Colours are HUD_SPEC 0.4 tokens with one meaning each: `ally` = Survivor-team information (Survivor clients only),
    // `danger` = Killer-caused threat. The shape language is panels, lines and squares: no rings (the U9 skill effects
    // use rings and Hovl bursts).
    public sealed class HoloPropFx
    {
        public static readonly Color Ally = new Color32(63, 184, 175, 255), Danger = new Color32(229, 72, 77, 255);
        public const float PanelAlpha = .5f;
        // Every GameObject any HoloPropFx has built (tests: a pool builds once, then reuses).
        public static int Built { get; private set; }

        private readonly Transform parent;
        private GameObject root, panel, linkObject;
        private LineRenderer link;
        private readonly Transform[] packets = new Transform[InteractionTiming.Packets];
        private readonly Transform[] shards = new Transform[InteractionTiming.Shards];
        private GameObject shardRoot;
        private int lastBeat = -1;
        private double pulseAt = double.NegativeInfinity, burstAt = double.NegativeInfinity;
        private Vector3 burstCenter;
        private readonly Vector3[] linkPoints = new Vector3[2];

        public HoloPropFx(Transform parent) { this.parent = parent; }

        public bool Ready => root != null;
        public bool PanelShown => panel != null && panel.activeInHierarchy;
        public bool LinkShown => linkObject != null && linkObject.activeInHierarchy;
        public bool PacketsShown => packets[0] != null && packets[0].gameObject.activeInHierarchy;
        public bool BurstShown => shardRoot != null && shardRoot.activeInHierarchy;
        public Color PanelColor => panel != null ? panel.GetComponent<Renderer>().sharedMaterial.color : Color.clear;
        public Color LinkColor => link != null ? link.sharedMaterial.color : Color.clear;
        public Color PacketColor => packets[0] != null ? packets[0].GetComponent<Renderer>().sharedMaterial.color : Color.clear;
        public Color ShardColor => shards[0] != null ? shards[0].GetComponent<Renderer>().sharedMaterial.color : Color.clear;
        public Vector3 PanelPosition => panel != null ? panel.transform.position : Vector3.zero;

        private void Build()
        {
            if (root != null) return;
            root = new GameObject("Holo props"); Built++;
            root.transform.SetParent(parent, false);
            panel = GameObject.CreatePrimitive(PrimitiveType.Quad); Built++;
            panel.name = "Holo panel";
            Release(panel);
            panel.transform.SetParent(root.transform, false);
            panel.GetComponent<Renderer>().sharedMaterial = HoloArt.Panel;
            linkObject = new GameObject("Holo link"); Built++;
            linkObject.transform.SetParent(root.transform, false);
            link = linkObject.AddComponent<LineRenderer>();
            link.sharedMaterial = HoloArt.AllySolid;
            link.useWorldSpace = true; link.positionCount = 2; link.widthMultiplier = .022f;
            link.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; link.receiveShadows = false;
            for (int i = 0; i < packets.Length; i++) packets[i] = Square("Packet " + i, root.transform, HoloArt.AllySolid, InteractionTiming.PacketSize);
            shardRoot = new GameObject("Jam burst"); Built++;
            shardRoot.transform.SetParent(parent, false);
            for (int i = 0; i < shards.Length; i++) shards[i] = Square("Shard " + i, shardRoot.transform, HoloArt.DangerSolid, InteractionTiming.ShardSize);
            panel.SetActive(false); linkObject.SetActive(false); SetPackets(false); shardRoot.SetActive(false);
        }

        private static Transform Square(string name, Transform under, Material material, float size)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube); Built++;
            item.name = name;
            Release(item);
            item.transform.SetParent(under, false);
            item.transform.localScale = new Vector3(size, size, size * .25f);
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item.transform;
        }

        private static void Release(GameObject item)
        {
            Collider collider = item.GetComponent<Collider>();
            if (collider != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider); }
            Renderer renderer = item.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private void SetPackets(bool on)
        {
            for (int i = 0; i < packets.Length; i++) if (packets[i].gameObject.activeSelf != on) packets[i].gameObject.SetActive(on);
        }

        // The props for this frame. `character` is the drawn character root (its forward = facing); `source` the stream
        // start (terminal), `target` the ally / switch point. `clock` is the beat clock of the interaction.
        public void Show(InteractionKind kind, float weight, Transform character, Vector3 source, Vector3 target, double clock, double now)
        {
            if (kind == InteractionKind.None || kind == InteractionKind.Jam || weight <= 0 || character == null) { Hide(now); return; }
            Build();
            if (!root.activeSelf) root.SetActive(true);
            Vector3 origin = character.position, forward = character.forward, up = Vector3.up;
            float grow = Ease(weight);
            switch (kind)
            {
                case InteractionKind.Hack:
                {
                    int beat = InteractionMotion.Beat(clock, out _);
                    if (beat != lastBeat) { lastBeat = beat; pulseAt = now; }
                    float pulse = Pulse(now - pulseAt);
                    Vector3 centre = origin + up * InteractionTiming.PanelHeight + forward * InteractionTiming.PanelForward;
                    if (!panel.activeSelf) panel.SetActive(true);
                    // Facing the character, tilted back toward its eyes; double-sided, so the camera behind sees it too.
                    panel.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(forward, up) * Quaternion.Euler(-InteractionTiming.PanelTilt, 0, 0));
                    panel.transform.localScale = new Vector3(InteractionTiming.PanelWidth * grow * (1 + pulse), InteractionTiming.PanelTall * grow * (1 + pulse), 1);
                    if (linkObject.activeSelf) linkObject.SetActive(false);
                    Stream(new Vector3(source.x, .82f, source.z), centre - forward * .02f, .25f, clock, grow);
                    break;
                }
                case InteractionKind.Restore:
                {
                    Vector3 dir = target - origin; dir.y = 0;
                    dir = dir.sqrMagnitude > .0001f ? dir.normalized : forward;
                    Vector3 from = origin + dir * .45f + up * .55f, to = new Vector3(target.x, .3f, target.z);
                    if (panel.activeSelf) panel.SetActive(false);
                    if (!linkObject.activeSelf) linkObject.SetActive(true);
                    linkPoints[0] = from; linkPoints[1] = Vector3.Lerp(from, to, grow);
                    link.SetPositions(linkPoints);
                    Stream(from, linkPoints[1], 0, clock, grow);
                    break;
                }
                case InteractionKind.Switch:
                {
                    Vector3 dir = target - origin; dir.y = 0;
                    dir = dir.sqrMagnitude > .0001f ? dir.normalized : forward;
                    Vector3 palm = origin + dir * InteractionTiming.SwitchReach + up * InteractionTiming.SwitchHeight;
                    if (panel.activeSelf) panel.SetActive(false);
                    if (linkObject.activeSelf) linkObject.SetActive(false);
                    Stream(palm, new Vector3(target.x, .05f, target.z), .15f, clock, grow);
                    break;
                }
            }
            UpdateBurst(now);
        }

        // Packets along source -> target with an upward arc, evenly phased, popping in and shrinking at the end.
        private void Stream(Vector3 from, Vector3 to, float arc, double clock, float grow)
        {
            SetPackets(true);
            for (int i = 0; i < packets.Length; i++)
            {
                float t = (float)((Math.Max(0, clock) / InteractionTiming.PacketSeconds + i / (double)packets.Length) % 1.0);
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.up * (arc * 4 * t * (1 - t));
                float s = InteractionTiming.PacketSize * grow * Mathf.Min(1, Mathf.Min(t / .15f, (1 - t) / .2f));
                packets[i].SetPositionAndRotation(p, Quaternion.LookRotation((to - from).sqrMagnitude > .0001f ? to - from : Vector3.forward));
                packets[i].localScale = new Vector3(s, s, s * .25f);
            }
        }

        public void Hide(double now)
        {
            if (root != null && root.activeSelf) { root.SetActive(false); lastBeat = -1; }
            UpdateBurst(now);
        }

        // Killer jam: a red glitch burst at `at` (the terminal top), shown for ShardSeconds.
        public void Burst(Vector3 at, double now)
        {
            Build();
            burstAt = now; burstCenter = at;
            UpdateBurst(now);
        }

        private void UpdateBurst(double now)
        {
            if (shardRoot == null) return;
            double t = now - burstAt;
            bool on = t >= 0 && t < InteractionTiming.ShardSeconds;
            if (shardRoot.activeSelf != on) shardRoot.SetActive(on);
            if (!on) return;
            float x = (float)(t / InteractionTiming.ShardSeconds);
            int step = (int)Math.Floor(t * 24); // glitch: offsets jump at 24 Hz instead of gliding
            for (int i = 0; i < shards.Length; i++)
            {
                float angle = (i * 137.5f + step * 23f * (i % 3)) * Mathf.Deg2Rad;
                float reach = InteractionTiming.ShardSpread * (1 - (1 - x) * (1 - x)) * (.5f + .5f * Hash(i, step));
                Vector3 offset = new Vector3(Mathf.Cos(angle) * reach, .05f + .35f * Hash(i + 11, step) * (1 - x), Mathf.Sin(angle) * reach);
                float s = InteractionTiming.ShardSize * (1 - x) * (i % 2 == 0 ? 1.6f : 1f);
                shards[i].position = burstCenter + offset;
                shards[i].rotation = Quaternion.identity;
                shards[i].localScale = new Vector3(s * (1 + Hash(i, step + 3)), s, s);
            }
        }

        private static float Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        // The keystroke pulse: +PulseScale at the beat, easing out over PulseSeconds.
        public static float Pulse(double since)
        {
            if (since < 0 || since >= InteractionTiming.PulseSeconds) return 0;
            float d = 1 - (float)(since / InteractionTiming.PulseSeconds);
            return InteractionTiming.PulseScale * d * d;
        }

        private static float Ease(float x) { x = Mathf.Clamp01(x); return 1 - (1 - x) * (1 - x); }

        public void Dispose()
        {
            if (root != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root); }
            if (shardRoot != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(shardRoot); else UnityEngine.Object.DestroyImmediate(shardRoot); }
            root = shardRoot = null;
        }
    }

    // The shared holo materials: copies of the Art/Holo/Resources materials (URP Unlit; HoloFade is transparent and
    // double-sided), built once per process. The panel carries a generated keyboard-grid texture.
    public static class HoloArt
    {
        private static Material panel, allySolid, dangerSolid;
        private static Texture2D grid;
        public const string FadeResource = "HoloFade", SolidResource = "HoloSolid";

        public static Material Panel => panel != null ? panel : panel = Make(true, new Color(HoloPropFx.Ally.r, HoloPropFx.Ally.g, HoloPropFx.Ally.b, HoloPropFx.PanelAlpha), Grid);
        public static Material AllySolid => allySolid != null ? allySolid : allySolid = Make(false, HoloPropFx.Ally, null);
        public static Material DangerSolid => dangerSolid != null ? dangerSolid : dangerSolid = Make(false, HoloPropFx.Danger, null);

        private static Material Make(bool fade, Color color, Texture texture)
        {
            Material template = Resources.Load<Material>(fade ? FadeResource : SolidResource);
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.name = fade ? "Holo panel" : "Holo solid";
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (texture != null) { material.mainTexture = texture; if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture); }
            material.hideFlags = HideFlags.DontSave;
            return material;
        }

        // A 12 x 5 key grid: bright 1-pixel key edges, faint key faces, a brighter top bar (the "screen" strip).
        public static Texture2D Grid
        {
            get
            {
                if (grid != null) return grid;
                const int w = 96, h = 52;
                grid = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Holo grid", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        bool frame = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                        bool screen = y >= h - 12;
                        bool key = !screen && (x % 8 == 0 || y % 8 == 0);
                        byte a = frame ? (byte)255 : screen ? (byte)(y == h - 12 ? 255 : 120) : key ? (byte)230 : (byte)55;
                        pixels[y * w + x] = new Color32(255, 255, 255, a);
                    }
                grid.SetPixels32(pixels);
                grid.Apply(false, true);
                return grid;
            }
        }
    }
}
