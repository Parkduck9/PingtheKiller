using System.Collections.Generic;
using System.Diagnostics;
using PingTheKiller.Client.UI;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 14.1 world effects and skill sounds: plays SkillFeedback records from pools built once at warm-up. U9: each
    // skill draws its own signature (SkillSignature: shape + motion, palette tint) from generated wire meshes, plus its
    // HUD_SPEC 8 glyph floating above the effect for 1 s. SkillFeedback still decides who sees what and where; a signature
    // is placed only at SkillFeedback.VisualPoint (or, for Overclock, the own first-person camera). After Warm nothing is
    // instantiated or allocated per effect; an exhausted pool reuses its oldest instance. Reduced motion shows each
    // signature still at its final pose (Overclock streaks and the Killer's Ping sweep are off).
    public sealed class SkillFx : MonoBehaviour
    {
        public const int PerShape = 2, GlyphCount = 4, PositionalCount = 4, StreakCount = 10, StaticCount = 30, GlyphPixels = 64;
        public const float RingHeight = .02f, DuckSeconds = .3f, DuckGain = .5f, TierCutFade = .03f;
        public const float BandBottom = .5f, BandTop = .85f, PulseStart = 2f, PulseSeconds = .7f, PulseStagger = .15f, MinBoom = 1f, GlyphTopMargin = 24;
        public const float WallRadius = 1.5f, WallHeight = 2f, DoubleRadius = .5f, DoubleHeight = 1.8f, DomeRadius = 5f;
        public const float SpikeRadius = 1f, BeamRadius = .35f, BeamHeight = 3.2f, StaticSpread = 1.5f, GlyphStroke = 3f;
        public static readonly Color Signal = new Color32(255, 176, 32, 255), Ally = new Color32(63, 184, 175, 255);
        public static readonly Color Danger = new Color32(229, 72, 77, 255), Text = new Color32(232, 230, 225, 255);
        private static readonly FxShape[] Shapes = { FxShape.PulseRings, FxShape.Streaks, FxShape.HexWall, FxShape.WireDouble, FxShape.Dome, FxShape.Spikes, FxShape.Beam, FxShape.Static, FxShape.TraceRing };

        private sealed class Rig
        {
            public FxShape Shape;
            public GameObject Root;
            public Material Material;
            public Transform[] Parts;
            public bool[] On;
            public float[] Shown;
            public FxPlan Plan;
            public Vector3 At;
            public double Start;
            public bool Busy;
        }

        private sealed class GlyphView
        {
            public GameObject Root;
            public Material Material;
            public Vector3 At;
            public double Start;
            public bool Busy, Still;
        }

        private sealed class Voice
        {
            public AudioSource Source;
            public double Start, Stop, FadeFrom;
            public float Volume;
            public bool Busy, Positional;
        }

        private readonly Dictionary<FxShape, List<Rig>> rigs = new Dictionary<FxShape, List<Rig>>();
        private readonly List<GlyphView> glyphs = new List<GlyphView>();
        private readonly Dictionary<int, Texture2D> glyphTextures = new Dictionary<int, Texture2D>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Voice> voices = new List<Voice>();
        // Two skill-tier voices: a new skill-tier cue starts on the idle one while the cut one fades out over TierCutFade.
        private readonly Voice[] skillVoices = new Voice[2];
        private int skillSlot;
        private Voice highVoice;
        private readonly Dictionary<float, AnimationCurve> rolloffs = new Dictionary<float, AnimationCurve>();
        private SkillFxArt art;
        private double duckUntil = double.NegativeInfinity;
        private Mesh glyphQuad;
        // Dev evidence (brief U9 item 5): warm-up cost and the frame time of the first cast.
        private int prewarmUntil = -1, firstCastFrame = -1;
        private string firstCastName;
        private float frameAverage;

        public bool Warmed { get; private set; }
        public bool Reduced { get; set; }
        // GameObjects created by this component (tests: no growth after Warm).
        public int Created { get; private set; }
        public double WarmMilliseconds { get; private set; }
        public int BusyRigs { get { int n = 0; foreach (List<Rig> list in rigs.Values) foreach (Rig r in list) if (r.Busy) n++; return n; } }
        public int BusyGlyphs { get { int n = 0; foreach (GlyphView g in glyphs) if (g.Busy) n++; return n; } }
        public int BusyVoices { get { int n = 0; foreach (Voice v in voices) if (v.Busy) n++; return n; } }
        public int RigCount(FxShape shape) => rigs.TryGetValue(shape, out List<Rig> list) ? list.Count : 0;
        public int GlyphTextureCount => glyphTextures.Count;
        public FxShape LastShape { get; private set; }
        public Color LastTint { get; private set; }
        public Vector3 LastPoint { get; private set; }       // where the last signature was placed (FollowCamera: its start)
        public Vector3 LastGlyphPoint { get; private set; }
        public bool LastGlyph { get; private set; }

        public static Color Tint(FxTint tint)
        {
            switch (tint)
            {
                case FxTint.Signal: return Signal;
                case FxTint.Ally: return Ally;
                case FxTint.Danger: return Danger;
                default: return Text;
            }
        }

        // Builds every pooled instance, mesh, material and glyph texture. Safe to call again (no-op once warmed). art may
        // be null: built-in fallback materials, no glyphs and no sound.
        public void Warm(SkillFxArt source)
        {
            if (Warmed) return;
            var clock = Stopwatch.StartNew();
            Warmed = true; art = source;
            Material wireTemplate = art != null ? art.Ring : null;
            foreach (FxShape shape in Shapes)
            {
                var list = new List<Rig>();
                int count = shape == FxShape.Streaks ? 1 : PerShape;
                Mesh[] shared = Meshes(shape);
                for (int i = 0; i < count; i++) list.Add(NewRig(shape, i, shared, wireTemplate));
                rigs[shape] = list;
            }
            if (art != null && art.Glyph != null)
            {
                foreach ((bool killerSkill, byte skillId) in SkillSignature.All)
                    glyphTextures[SkillGlyph.Key(killerSkill, skillId)] = GlyphTexture(SkillGlyph.For(killerSkill, skillId));
                glyphQuad = Track(SkillShapes.GlyphQuad());
                for (int i = 0; i < GlyphCount; i++) glyphs.Add(NewGlyph(i));
            }
            skillVoices[0] = NewVoice("skill fx 2D skill a", false);
            skillVoices[1] = NewVoice("skill fx 2D skill b", false);
            highVoice = NewVoice("skill fx 2D high", false);
            for (int i = 0; i < PositionalCount; i++) NewVoice("skill fx positional " + i, true);
            Prewarm();
            WarmMilliseconds = clock.Elapsed.TotalMilliseconds;
            Debug.Log("[fx] warm ms=" + WarmMilliseconds.ToString("0.0") + " objects=" + Created + " meshes=" + meshes.Count + " materials=" + materials.Count + " glyphs=" + glyphTextures.Count);
        }

        private Mesh Track(Mesh mesh) { meshes.Add(mesh); return mesh; }

        private Mesh[] Meshes(FxShape shape)
        {
            switch (shape)
            {
                case FxShape.PulseRings: return new[] { Track(SkillShapes.Band(BandBottom, BandTop, 64)) };
                case FxShape.Streaks: return new[] { Track(SkillShapes.Streak(1.2f)) };
                case FxShape.HexWall: return new[] { Track(SkillShapes.HexWall(WallRadius, WallHeight)) };
                case FxShape.WireDouble: return new[] { Track(SkillShapes.WireDouble(DoubleRadius, DoubleHeight)) };
                case FxShape.Dome: return new[] { Track(SkillShapes.Dome(DomeRadius)) };
                case FxShape.Spikes: return new[] { Track(SkillShapes.Spikes(SpikeRadius, 0)), Track(SkillShapes.Spikes(SpikeRadius, 1)) };
                case FxShape.Beam: return new[] { Track(SkillShapes.BeamColumns(BeamRadius, BeamHeight)), Track(SkillShapes.Ring("skill fx beam ring", BeamRadius + .15f)) };
                case FxShape.Static: return new[] { Track(SkillShapes.FilledCross()) };
                case FxShape.TraceRing: return new[] { Track(SkillShapes.Ring("skill fx trace ring", 1f)) };
                default: return System.Array.Empty<Mesh>();
            }
        }

        private GameObject Child(string label, Transform parent)
        {
            var item = new GameObject(label);
            item.transform.SetParent(parent, false);
            Created++;
            return item;
        }

        private Material NewMaterial(Material template, string label, bool doubleSided)
        {
            Material material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.name = label;
            if (doubleSided && material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            materials.Add(material);
            return material;
        }

        private Transform Part(Rig rig, string label, Mesh mesh)
        {
            GameObject item = Child(label, rig.Root.transform);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = rig.Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return item.transform;
        }

        private Rig NewRig(FxShape shape, int index, Mesh[] shared, Material template)
        {
            var rig = new Rig { Shape = shape, Root = Child("skill fx " + shape + " " + index, transform) };
            rig.Material = NewMaterial(template, "skill fx " + shape, true);
            var parts = new List<Transform>();
            switch (shape)
            {
                case FxShape.PulseRings:
                    for (int i = 0; i < 3; i++) parts.Add(Part(rig, "pulse " + i, shared[0]));
                    break;
                case FxShape.Streaks:
                    for (int i = 0; i < StreakCount; i++) parts.Add(Part(rig, "streak " + i, shared[0]));
                    break;
                case FxShape.Spikes:
                    parts.Add(Part(rig, "spikes a", shared[0]));
                    parts.Add(Part(rig, "spikes b", shared[1]));
                    break;
                case FxShape.Beam:
                    parts.Add(Part(rig, "columns", shared[0]));
                    for (int i = 0; i < 3; i++) parts.Add(Part(rig, "climbing ring " + i, shared[1]));
                    break;
                case FxShape.Static:
                    for (int i = 0; i < StaticCount; i++)
                    {
                        Transform puff = Part(rig, "static " + i, shared[0]);
                        float a = SkillShapes.Hash(i, 11) * Mathf.PI * 2, r = Mathf.Sqrt(SkillShapes.Hash(i, 12)) * StaticSpread;
                        puff.localPosition = new Vector3(Mathf.Cos(a) * r, .2f + SkillShapes.Hash(i, 13) * 1.5f, Mathf.Sin(a) * r);
                        puff.localRotation = Quaternion.Euler(0, SkillShapes.Hash(i, 14) * 180, 0);
                        puff.localScale = Vector3.one * (.12f + SkillShapes.Hash(i, 15) * .18f); // 0.12-0.3 m (F2)
                        parts.Add(puff);
                    }
                    break;
                default:
                    parts.Add(Part(rig, shape.ToString(), shared[0]));
                    break;
            }
            rig.Parts = parts.ToArray();
            rig.On = new bool[rig.Parts.Length];
            rig.Shown = new float[rig.Parts.Length];
            for (int i = 0; i < rig.On.Length; i++) { rig.On[i] = true; rig.Shown[i] = -1; }
            rig.Root.SetActive(false);
            return rig;
        }

        private Texture2D GlyphTexture(SkillGlyph.Prim[] glyph)
        {
            float[] mask = SkillGlyph.Rasterize(glyph, GlyphPixels, GlyphStroke);
            var texture = new Texture2D(GlyphPixels, GlyphPixels, TextureFormat.RGBA32, false) { name = "skill fx glyph", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[mask.Length];
            for (int i = 0; i < mask.Length; i++) pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(mask[i] * 255));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private GlyphView NewGlyph(int index)
        {
            GameObject item = Child("skill fx glyph " + index, transform);
            item.AddComponent<MeshFilter>().sharedMesh = glyphQuad;
            var renderer = item.AddComponent<MeshRenderer>();
            Material material = NewMaterial(art.Glyph, "skill fx glyph", false);
            foreach (Texture2D texture in glyphTextures.Values) { material.mainTexture = texture; break; }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            item.SetActive(false);
            return new GlyphView { Root = item, Material = material };
        }

        private Voice NewVoice(string label, bool positional)
        {
            GameObject item = Child(label, transform);
            var source = item.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.dopplerLevel = 0; source.spread = 0;
            source.spatialBlend = positional ? 1 : 0;
            var voice = new Voice { Source = source, Positional = positional };
            voices.Add(voice);
            return voice;
        }

        // Shows every rig and glyph for two frames, tiny, just in front of the camera, so each material's pipeline state is
        // built at warm-up instead of on the first cast (unity-graphics: warm shaders before first use).
        private void Prewarm()
        {
            Camera eye = Camera.main;
            if (eye == null || !Application.isPlaying) return;
            Vector3 at = eye.transform.position + eye.transform.forward * 2;
            foreach (List<Rig> list in rigs.Values)
                foreach (Rig rig in list)
                {
                    rig.Root.transform.position = at; rig.Root.transform.localScale = Vector3.one * 1e-3f;
                    rig.Root.SetActive(true);
                }
            foreach (GlyphView glyph in glyphs) { glyph.Root.transform.position = at; glyph.Root.transform.localScale = Vector3.one * 1e-3f; glyph.Root.SetActive(true); }
            prewarmUntil = Time.frameCount + 2;
        }

        private void EndPrewarm()
        {
            prewarmUntil = -1;
            foreach (List<Rig> list in rigs.Values)
                foreach (Rig rig in list)
                {
                    rig.Root.transform.localScale = Vector3.one;
                    if (!rig.Busy) rig.Root.SetActive(false);
                }
            foreach (GlyphView glyph in glyphs) if (!glyph.Busy) glyph.Root.SetActive(false);
        }

        public void Play(in FxRecord record, Vector3 own) => Play(record, own, Time.unscaledTimeAsDouble);

        // One record. `own` is the own rendered ground position; the signature and a positional sound are placed by
        // SkillFeedback.VisualPoint / SoundPoint (a positional sound always at the effect's world point).
        public void Play(in FxRecord record, Vector3 own, double now)
        {
            if (!Warmed) return;
            if (prewarmUntil >= 0) EndPrewarm();
            FxPlan plan = SkillSignature.Plan(record, Reduced);
            SkillFeedback.VisualPoint(record, own.x, own.z, out float vx, out float vz);
            SkillFeedback.SoundPoint(record, own.x, own.z, out float sx, out float sz);
            if (plan.Shown) Show(plan, plan.FollowCamera ? own : new Vector3(vx, 0, vz), now);
            if (record.Sound.Shown) Sound(record.Sound, new Vector3(sx, 0, sz), now);
        }

        // Read-back for tests: where the last positional voice was placed, and a skill-tier voice's stop time and volume.
        public Vector3 LastSoundPosition { get; private set; }
        public double SkillVoiceStop(int slot) => skillVoices[slot].Busy ? skillVoices[slot].Stop : double.NegativeInfinity;
        public float SkillVoiceVolume(int slot) => skillVoices[slot].Source.volume;
        public int SkillSlot => skillSlot;

        private void Show(in FxPlan plan, Vector3 at, double now)
        {
            if (!rigs.TryGetValue(plan.Shape, out List<Rig> list) || list.Count == 0) return;
            Color tint = Tint(plan.Tint);
            LastShape = plan.Shape; LastTint = tint; LastPoint = at; LastGlyph = false;
            Rig rig = Oldest(list);
            rig.Busy = true; rig.Plan = plan; rig.At = at; rig.Start = now;
            for (int i = 0; i < rig.Shown.Length; i++) rig.Shown[i] = -1;
            rig.Material.color = tint;
            rig.Root.transform.position = at + Vector3.up * RingHeight;
            rig.Root.transform.localRotation = Quaternion.identity; rig.Root.transform.localScale = Vector3.one;
            rig.Root.SetActive(true);
            Animate(rig, now);
            if (firstCastFrame < 0 && Application.isPlaying) { firstCastFrame = Time.frameCount; firstCastName = plan.Shape.ToString(); }
            if (!plan.Glyph || glyphs.Count == 0 || !glyphTextures.TryGetValue(SkillGlyph.Key(plan.KillerSkill, plan.SkillId), out Texture2D texture)) return;
            GlyphView glyph = OldestGlyph();
            glyph.Busy = true; glyph.Start = now; glyph.At = at; glyph.Still = plan.Still;
            glyph.Material.mainTexture = texture; glyph.Material.color = tint;
            glyph.Root.transform.localScale = Vector3.one * SkillSignature.GlyphSize;
            glyph.Root.SetActive(true);
            AnimateGlyph(glyph, now);
            LastGlyph = true; LastGlyphPoint = glyph.Root.transform.position;
        }

        private static Rig Oldest(List<Rig> list)
        {
            Rig pick = null;
            foreach (Rig rig in list)
            {
                if (!rig.Busy) return rig;
                if (pick == null || rig.Start < pick.Start) pick = rig;
            }
            return pick;
        }

        private GlyphView OldestGlyph()
        {
            GlyphView pick = null;
            foreach (GlyphView glyph in glyphs)
            {
                if (!glyph.Busy) return glyph;
                if (pick == null || glyph.Start < pick.Start) pick = glyph;
            }
            return pick;
        }

        private static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t); }

        // game-feel: an overshooting "back" ease for the Dummy packet pop (settles at exactly 1).
        private static float BackOut(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1;
            float u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }

        private void SetPart(Rig rig, int i, bool on)
        {
            if (rig.On[i] == on) return;
            rig.On[i] = on; rig.Parts[i].gameObject.SetActive(on);
        }

        // One frame of a signature at `now`; ends it at its time.
        private void Animate(Rig rig, double now)
        {
            FxPlan plan = rig.Plan;
            float t = (float)(now - rig.Start);
            if (t < 0 || t >= plan.Seconds) { EndRig(rig); return; }
            bool still = plan.Still;
            Transform root = rig.Root.transform;
            switch (rig.Shape)
            {
                case FxShape.PulseRings:
                    // F1: three vertical bands (0.5-0.85 m high) that sweep out from 2 m (Killer) or close in on the own
                    // feet (scanned Survivor), staggered 0.15 s, 0.7 s each.
                    for (int i = 0; i < 3; i++)
                    {
                        float radius;
                        if (still) radius = .45f + .3f * i;
                        else
                        {
                            float p = (t - PulseStagger * i) / PulseSeconds;
                            if (p < 0 || p >= 1) { SetPart(rig, i, false); continue; }
                            float e = EaseOut(p);
                            radius = plan.Inward ? Mathf.Lerp(plan.Diameter * .5f, .35f, e) : Mathf.Lerp(PulseStart, plan.Diameter * .5f, e);
                        }
                        SetPart(rig, i, true);
                        if (!Mathf.Approximately(radius, rig.Shown[i])) { rig.Shown[i] = radius; rig.Parts[i].localScale = new Vector3(radius, 1, radius); }
                    }
                    break;
                case FxShape.Streaks:
                {
                    Camera eye = Camera.main;
                    if (eye != null) { root.position = eye.transform.position; root.rotation = Quaternion.Euler(0, eye.transform.eulerAngles.y, 0); }
                    else root.position = rig.At + Vector3.up * 1.6f;
                    for (int i = 0; i < rig.Parts.Length; i++)
                    {
                        float side = SkillShapes.Hash(i, 1) < .5f ? -1 : 1;
                        float x = side * (.9f + SkillShapes.Hash(i, 2) * .9f), y = -.9f + SkillShapes.Hash(i, 3) * 1.4f;
                        float z = 8 - Mathf.Repeat(t * 14 + SkillShapes.Hash(i, 4) * 10, 10);
                        rig.Parts[i].localPosition = new Vector3(x, y, z);
                    }
                    break;
                }
                case FxShape.HexWall:
                {
                    float spread = Mathf.Max(.1f, plan.Diameter * .5f) / WallRadius;
                    root.localScale = new Vector3(spread, still ? 1 : Mathf.Max(.001f, EaseOut(t / .2f)), spread);
                    SetPart(rig, 0, still || t < .72f || t >= .82f); // one blink before it drops (WCAG 2.3.1: < 3 per second)
                    break;
                }
                case FxShape.WireDouble:
                {
                    float s = still ? 1 : BackOut(t / .22f);
                    float left = plan.Seconds - t;
                    if (!still && left < .12f) s *= Mathf.Max(0, left / .12f);
                    root.localScale = Vector3.one * Mathf.Max(.001f, s);
                    // F4: hidden while the camera is closer than 1 m to its look target (the 4.10 short-boom rule).
                    Camera eye = Camera.main;
                    SetPart(rig, 0, eye == null || Vector3.Distance(eye.transform.position, rig.At + Vector3.up * .7f) >= MinBoom);
                    break;
                }
                case FxShape.Dome:
                {
                    float grow = still ? 1 : Mathf.Clamp01(t / SkillFeedback.RelayGrowSeconds);
                    root.localScale = Vector3.one * Mathf.Max(.001f, grow * plan.Diameter * .5f / DomeRadius);
                    root.localRotation = Quaternion.Euler(0, still ? 0 : t * 20, 0);
                    break;
                }
                case FxShape.Spikes:
                {
                    float s = still ? 1 : EaseOut(t / .1f);
                    root.localScale = Vector3.one * Mathf.Max(.001f, s * Mathf.Max(1, plan.Diameter * .5f));
                    bool first = still || ((int)(t * 12) & 1) == 0; // jitter between two jagged frames
                    SetPart(rig, 0, first); SetPart(rig, 1, !first);
                    break;
                }
                case FxShape.Beam:
                {
                    rig.Parts[0].localScale = new Vector3(1, still ? 1 : Mathf.Max(.001f, EaseOut(t / .15f)), 1);
                    for (int i = 1; i < rig.Parts.Length; i++)
                    {
                        float y = still ? .8f * i : Mathf.Repeat(t * 5 + (i - 1) * BeamHeight / 3, BeamHeight);
                        rig.Parts[i].localPosition = new Vector3(0, y, 0);
                    }
                    break;
                }
                case FxShape.TraceRing:
                {
                    // R1: grows from the record's start radius to its final radius over TraceGrowSeconds, then holds.
                    float grow = still ? 1 : EaseOut(Mathf.Clamp01(t / SkillFeedback.TraceGrowSeconds));
                    float radius = Mathf.Max(.001f, Mathf.Lerp(Mathf.Max(.5f, plan.StartDiameter * .5f), plan.Diameter * .5f, grow));
                    root.localScale = new Vector3(radius, 1, radius);
                    break;
                }
                case FxShape.Static:
                {
                    int slot = (int)(t * 14);
                    for (int i = 0; i < rig.Parts.Length; i++) SetPart(rig, i, still || SkillShapes.Hash(i, slot) < .5f); // about half on (F2)
                    break;
                }
            }
        }

        private void AnimateGlyph(GlyphView glyph, double now)
        {
            float t = (float)(now - glyph.Start);
            if (t < 0 || t >= SkillSignature.GlyphSeconds) { glyph.Busy = false; glyph.Root.SetActive(false); return; }
            float rise = glyph.Still ? SkillSignature.GlyphRise * .5f : SkillSignature.GlyphRise * EaseOut(t / SkillSignature.GlyphSeconds);
            Transform item = glyph.Root.transform;
            item.position = glyph.At + Vector3.up * (SkillSignature.GlyphHeight + rise);
            Camera eye = Camera.main;
            if (eye != null)
            {
                ClampBelowTop(eye, item, SkillSignature.GlyphSize * .5f);
                Vector3 away = item.position - eye.transform.position;
                if (away.sqrMagnitude > 1e-4f) item.rotation = Quaternion.LookRotation(away, Vector3.up); // billboard: front (-Z) to the camera
            }
        }

        // F3: keeps the glyph's top at least 24 u (HUD_SPEC 0.2 panel scale) inside the top screen edge by lowering it.
        private static void ClampBelowTop(Camera eye, Transform item, float halfSize)
        {
            float scale = Mathf.Clamp(Mathf.Min(eye.pixelWidth / 1920f, eye.pixelHeight / 1080f), .8f, 1f);
            float limit = eye.pixelHeight - GlyphTopMargin * scale;
            Vector3 top = eye.WorldToScreenPoint(item.position + Vector3.up * halfSize);
            if (top.z <= 0 || top.y <= limit) return;
            float perMetre = top.y - eye.WorldToScreenPoint(item.position + Vector3.up * (halfSize - 1)).y;
            if (perMetre > 1e-3f) item.position -= Vector3.up * ((top.y - limit) / perMetre);
        }

        private static void EndRig(Rig rig)
        {
            rig.Busy = false;
            rig.Root.SetActive(false);
        }

        private void Sound(FxSound sound, Vector3 at, double now)
        {
            AudioClip clip = art != null ? art.Clip(sound.Clip) : null;
            Voice voice;
            if (!sound.Positional)
            {
                if (sound.Tier == FxTier.High) voice = highVoice;
                else
                {
                    // 14.1: a new skill-tier cue cuts the previous one with a 30 ms fade, on the other pooled voice.
                    Voice previous = skillVoices[skillSlot];
                    if (previous.Busy && previous.Stop > now + TierCutFade) { previous.FadeFrom = now; previous.Stop = now + TierCutFade; }
                    skillSlot = 1 - skillSlot;
                    voice = skillVoices[skillSlot];
                }
                duckUntil = now + DuckSeconds;
            }
            else
            {
                voice = null;
                foreach (Voice v in voices)
                {
                    if (!v.Positional) continue;
                    if (voice == null || (voice.Busy && !v.Busy) || (voice.Busy == v.Busy && v.Start < voice.Start)) voice = v;
                }
                voice.Source.transform.position = at;
                voice.Source.rolloffMode = AudioRolloffMode.Custom;
                voice.Source.maxDistance = sound.Range;
                if (!rolloffs.TryGetValue(sound.Range, out AnimationCurve curve)) rolloffs[sound.Range] = curve = Rolloff(sound.Range);
                voice.Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
                LastSoundPosition = at;
            }
            voice.Busy = true; voice.Start = now;
            voice.Volume = SkillFeedback.Volume(sound.Clip, sound.Db);
            float length = clip != null ? clip.length : 0;
            float cut = sound.Cut > 0 ? sound.Cut : length;
            voice.Stop = now + cut;
            voice.FadeFrom = voice.Stop - Mathf.Max(sound.Fade, 0);
            voice.Source.Stop();
            voice.Source.volume = voice.Volume;
            if (clip == null) return;
            voice.Source.clip = clip;
            voice.Source.Play();
        }

        private static AnimationCurve Rolloff(float range)
        {
            var keys = new Keyframe[12];
            for (int i = 0; i < 11; i++)
            {
                float x = i / 10f * .999f;
                keys[i] = new Keyframe(x, SkillFeedback.Attenuation(x * range, range));
            }
            keys[11] = new Keyframe(1, 0);
            return new AnimationCurve(keys);
        }

        private void Update()
        {
            if (prewarmUntil >= 0 && Time.frameCount >= prewarmUntil) EndPrewarm();
            float dt = Time.unscaledDeltaTime * 1000;
            if (firstCastFrame >= 0 && Time.frameCount == firstCastFrame + 1)
                Debug.Log("[fx] first cast " + firstCastName + " frame ms=" + dt.ToString("0.0") + " (average before " + frameAverage.ToString("0.0") + ")");
            if (firstCastFrame < 0 || Time.frameCount <= firstCastFrame) frameAverage = frameAverage <= 0 ? dt : frameAverage * .95f + dt * .05f;
            Tick(Time.unscaledTimeAsDouble);
        }

        // Moves and ends signatures and glyphs, fades voices to their cut and ducks positional voices.
        public void Tick(double now)
        {
            foreach (List<Rig> list in rigs.Values)
                foreach (Rig rig in list)
                    if (rig.Busy) Animate(rig, now);
            foreach (GlyphView glyph in glyphs) if (glyph.Busy) AnimateGlyph(glyph, now);
            bool ducked = now < duckUntil;
            foreach (Voice voice in voices)
            {
                if (!voice.Busy) continue;
                if (now >= voice.Stop) { voice.Source.Stop(); voice.Busy = false; continue; }
                float gain = now >= voice.FadeFrom && voice.Stop > voice.FadeFrom ? (float)((voice.Stop - now) / (voice.Stop - voice.FadeFrom)) : 1;
                if (voice.Positional && ducked) gain *= DuckGain;
                voice.Source.volume = voice.Volume * gain;
            }
        }

        // MatchStart, GameResult, disconnect: everything stops at once.
        public void Clear()
        {
            if (prewarmUntil >= 0) EndPrewarm();
            foreach (List<Rig> list in rigs.Values) foreach (Rig rig in list) EndRig(rig);
            foreach (GlyphView glyph in glyphs) { glyph.Busy = false; glyph.Root.SetActive(false); }
            foreach (Voice voice in voices) { voice.Busy = false; voice.Source.Stop(); }
            duckUntil = double.NegativeInfinity;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials) Release(material);
            foreach (Mesh mesh in meshes) Release(mesh);
            foreach (Texture2D texture in glyphTextures.Values) Release(texture);
        }

        private static void Release(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }
    }
}
