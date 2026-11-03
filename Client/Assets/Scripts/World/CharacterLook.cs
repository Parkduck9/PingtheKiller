using UnityEngine;

namespace PingTheKiller.Client.World
{
    // The Survivor character's shared shape and its two special materials. The body is a capsule 1.8 m tall and 0.8 m
    // wide with a small dark visor on the front at head height, so facing reads in both views.
    // Shaders live in Resources/Shaders (loaded by path, so they are always in the player build).
    public static class CharacterLook
    {
        public const float Height = 1.8f, Width = .8f;
        public static readonly Vector3 BodyScale = new Vector3(Width, Height * .5f, Width);
        // Relative to the character's ground point (the body's centre is at Height / 2).
        public static readonly Vector3 VisorOffset = new Vector3(0, 1.5f, .34f);
        public static readonly Vector3 VisorScale = new Vector3(.36f, .12f, .1f);
        public static readonly Color Visor = new Color32(44, 47, 51, 255);
        public static readonly Color Signal = new Color32(255, 176, 32, 255);

        // Screen-door dither (HUD_SPEC 4.10); null when the shader is missing (caller falls back to the normal material).
        public static Material Dither(Color color, float discard)
        {
            Shader shader = Resources.Load<Shader>("Shaders/PtkDither");
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Discard", discard);
            return material;
        }

        public static Material Dither(Material source, float discard)
        {
            Material material = Dither(source.color, discard);
            if (material == null) return new Material(source);
            if (source.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            }
            // U4i: a toon source keeps its light bands while dithered, so switching to the dither shows no shading pop.
            if (ToonShading.IsToon(source))
            {
                material.SetFloat("_Toon", 1);
                foreach (string tint in new[] { "_LightTint", "_MiddleTint", "_ShadowTint" }) material.SetColor(tint, source.GetColor(tint));
                material.SetVector("_DiffuseThresholds", source.GetVector("_DiffuseThresholds"));
                material.SetFloat("_UseSecondThreshold", source.GetFloat("_UseSecondThreshold"));
            }
            return material;
        }

        public static void Transparent(Material material)
        {
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        public static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }

        // Conservative AABB/plane test, including the near plane. Marker classification stays point-based.
        public static bool InModelFrustum(ViewPose view, Vector3 ground)
        {
            float tan = Mathf.Tan(view.VerticalFov * .5f * Mathf.Deg2Rad);
            Vector3 centre = view.ToCamera(ground + Vector3.up * Height * .5f);
            Quaternion inverse = Quaternion.Inverse(view.Rotation);
            Vector3 right = inverse * Vector3.right * Width * .5f;
            Vector3 up = inverse * Vector3.up * Height * .5f;
            Vector3 forward = inverse * Vector3.forward * Width * .5f;
            return InsidePlane(centre, right, up, forward, Vector3.forward, -ViewPose.Near)
                && InsidePlane(centre, right, up, forward, new Vector3(1, 0, tan * view.Aspect), 0)
                && InsidePlane(centre, right, up, forward, new Vector3(-1, 0, tan * view.Aspect), 0)
                && InsidePlane(centre, right, up, forward, new Vector3(0, 1, tan), 0)
                && InsidePlane(centre, right, up, forward, new Vector3(0, -1, tan), 0);
        }

        private static bool InsidePlane(Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 normal, float offset)
            => Vector3.Dot(normal, centre) + offset + Mathf.Abs(Vector3.Dot(normal, right))
                + Mathf.Abs(Vector3.Dot(normal, up)) + Mathf.Abs(Vector3.Dot(normal, forward)) >= 0;

        // Inverted-hull outline in one flat colour (HUD_SPEC 6.2 `signal` outline); null when the shader is missing.
        public static Material Outline(Color color)
        {
            Shader shader = Resources.Load<Shader>("Shaders/PtkOutline");
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader);
            material.SetColor("_BaseColor", color);
            return material;
        }

        // Hull scale (relative to the body) that adds `thickness` metres around the 0.4 m radius and the 0.9 m half height.
        public static Vector3 HullScale(float thickness)
            => new Vector3(1 + thickness / (Width * .5f), 1 + thickness / (Height * .5f), 1 + thickness / (Width * .5f));
    }
}
