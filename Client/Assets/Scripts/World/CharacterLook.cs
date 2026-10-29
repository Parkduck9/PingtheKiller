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
