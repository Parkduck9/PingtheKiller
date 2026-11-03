using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // The one material path of the world view (every primitive, ring and character part goes through MakeMaterial) and the
    // owner of the materials it creates. Shared by CharacterViews and WorldProps; kept frozen so that the two can change
    // independently.
    public sealed class WorldMaterials
    {
        // The Survivor team colour (`ally`): the Survivor characters, done terminals and the open Exit on a Survivor client.
        public static readonly Color Survivor = new Color32(63, 184, 175, 255);

        private readonly Transform parent;
        private readonly List<Material> materials = new List<Material>();

        public WorldMaterials(Transform parent)
        {
            this.parent = parent;
        }

        public Transform Parent => parent;

        public Material MakeMaterial(Color color, bool transparent = false)
        {
            var material = NewMaterial(transparent, ToonShading.Find());
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

        // A fresh world material (U4i): a copy of the toon template when the toon shader is available, otherwise exactly the
        // earlier URP Lit path (the WorldLit template, or URP Lit itself). `toon` is ToonShading.Find(); null means missing.
        public static Material NewMaterial(bool transparent, Shader toon)
        {
            Material template = toon != null ? Resources.Load<Material>(transparent ? ToonShading.FadeTemplate : ToonShading.Template) : null;
            if (template == null || template.shader != toon) template = Resources.Load<Material>(transparent ? "WorldLitFade" : "WorldLit");
            return template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }

        public GameObject Primitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color, bool transparent = false)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = label;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color, transparent);
            Object.Destroy(item.GetComponent<Collider>()); // Positions and collisions are server-owned.
            return item;
        }

        public void Dispose()
        {
            foreach (Material material in materials) Object.Destroy(material);
            materials.Clear();
        }
    }

    // Toon shading (U4i, user decision 2026-11-03: toon bands, no black outlines). Characters and world art use
    // `PingTheKiller/Toon` (Resources/Shaders/PtkToon.shader): the Toon Shaders Pro URP toon shader with its double albedo
    // multiply fixed, so palette colours render as authored. Flat light bands; no outline pass (the Killer view's `signal`
    // hull stays the only outline), no rim light and no specular (SLOP V3: no glow). The editor builders bake these
    // settings into the .mat assets; the two Resources templates carry them to runtime materials. The shader is loaded from
    // Resources like PtkDither and PtkOutline, so it is always in the player build. When it is missing or unsupported,
    // every runtime path falls back to URP Lit.
    public static class ToonShading
    {
        public const string ShaderName = "PingTheKiller/Toon";
        public const string Template = "WorldToon", FadeTemplate = "WorldToonFade";

        public enum Kind { World, Ground, Character }

        // World art: two bands. Lit faces get LightTint x key light; faces turned away get ShadowTint (cool, dark, never
        // black: the ambient probe is added on top). Thresholds are NdotL edges; the narrow edge keeps the band crisp.
        public static readonly Color WorldLight = new Color(.52f, .52f, .56f);
        public static readonly Color WorldShadow = new Color(.16f, .19f, .27f);
        public static readonly Vector4 WorldDiffuse = new Vector4(0, .04f, .5f, .54f);
        // Cast shadows: the key light's shadow strength (.75) leaves attenuation .25, so the edge sits above it.
        // U4i attempt 2: with soft (PCF) shadows the attenuation ramps across the edge; a wider window turns that ramp into a
        // clean anti-aliased edge instead of the texel saw-tooth of hard shadows.
        public static readonly Vector2 WorldShadowEdge = new Vector2(.35f, .75f);
        // Ground: a soft shadow edge, so the large flat surfaces never show a hard band in fog.
        public static readonly Vector2 GroundShadowEdge = new Vector2(.15f, .65f);
        // Characters: three bands (shadow, middle, light).
        public static readonly Color CharacterLight = new Color(1, 1, 1);
        public static readonly Color CharacterMiddle = new Color(.62f, .64f, .70f);
        public static readonly Color CharacterShadow = new Color(.22f, .25f, .33f);
        public static readonly Vector4 CharacterDiffuse = new Vector4(-.05f, 0, .42f, .48f);

        // Night grade (U4i attempt 2, user feedback "the mood is too bright and pastel"): applied by WorldArtSetup to the
        // base colour of every opaque world building, prop and ground material. Never to characters, role colours (terminal,
        // exit), the HUD or the unlit lit-window and sign materials, so the warm windows stay the brightest world colours.
        // Saturation is cut, value is cut above a knee (dark asphalt and paving keep their value, so the ground never turns
        // into black holes), and a slight cool shift pulls the palette toward moonlight.
        public const float NightSaturation = .45f, NightValue = .64f, NightKneeLow = .12f, NightKneeHigh = .35f;
        public static readonly Color NightCool = new Color(.97f, .99f, 1.03f);

        public static Color NightGrade(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            float t = Mathf.InverseLerp(NightKneeLow, NightKneeHigh, v);
            float value = v * Mathf.Lerp(1, NightValue, t * t * (3 - 2 * t));
            Color graded = Color.HSVToRGB(h, s * NightSaturation, value);
            return new Color(Mathf.Clamp01(graded.r * NightCool.r), Mathf.Clamp01(graded.g * NightCool.g), Mathf.Clamp01(graded.b * NightCool.b), color.a);
        }

        public static Shader Find()
        {
            Shader shader = Resources.Load<Shader>("Shaders/PtkToon");
            return shader != null && shader.isSupported ? shader : null;
        }

        public static bool IsToon(Material material) => material != null && material.shader != null && material.shader.name == ShaderName;

        // Applies the U4i look to a material that already uses the toon shader. Base colour and maps are left as they are.
        public static void Configure(Material material, Kind kind)
        {
            bool character = kind == Kind.Character;
            material.SetColor("_LightTint", character ? CharacterLight : WorldLight);
            material.SetColor("_MiddleTint", character ? CharacterMiddle : Color.Lerp(WorldShadow, WorldLight, .5f));
            material.SetColor("_ShadowTint", character ? CharacterShadow : WorldShadow);
            material.SetVector("_DiffuseThresholds", character ? CharacterDiffuse : WorldDiffuse);
            Vector2 edge = kind == Kind.Ground ? GroundShadowEdge : WorldShadowEdge;
            material.SetVector("_ShadowThresholds", new Vector4(edge.x, edge.y, 0, 0));
            Toggle(material, "_UseSecondThreshold", "_USE_SECOND_THRESHOLD", character);
            // Characters never receive shadows (CharacterInstance turns receiveShadows off on every renderer).
            Toggle(material, "_ReceiveShadows", "_RECEIVE_SHADOWS_ON", !character);
            Toggle(material, "_UseVertexColors", "_USE_VERTEX_COLORS", false);
            // Specular off: the specular workflow with a black specular colour has zero reflectance (no highlight, no
            // environment reflection); smoothness 0 keeps the grazing term at 0 as well.
            material.SetFloat("_WorkflowMode", 0);
            material.EnableKeyword("_SPECULAR_SETUP");
            material.SetColor("_SpecularColor", Color.black);
            material.SetFloat("_Smoothness", 0);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_SpecularBoost", 1);
            // Rim off: black colour and a threshold no surface reaches.
            material.SetColor("_RimColor", new Color(0, 0, 0, 0));
            material.SetVector("_RimThresholds", new Vector4(.999f, 1, 0, 0));
            material.SetFloat("_RimExtension", 0);
            material.SetFloat("_GIStrength", 1);
            material.SetFloat("_BumpScale", 1);
        }

        public static bool RimOff(Material material)
        {
            Color rim = material.GetColor("_RimColor");
            return rim.r <= 0 && rim.g <= 0 && rim.b <= 0;
        }

        public static bool SpecularOff(Material material)
            => material.IsKeywordEnabled("_SPECULAR_SETUP") && material.GetColor("_SpecularColor").maxColorComponent <= 0;

        private static void Toggle(Material material, string property, string keyword, bool on)
        {
            material.SetFloat(property, on ? 1 : 0);
            if (on) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
        }
    }
}
