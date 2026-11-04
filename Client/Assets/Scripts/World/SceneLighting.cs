using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // Scene lighting, built by script: a night city. The client scene has no baked lighting data, so Unity's ambient probe
    // stays black and every face turned away from the key light rendered pure black. URP reads RenderSettings.ambientProbe
    // for the ambient term of objects without light probes, so the probe is set directly here (dark blue-grey trilight:
    // brighter from above, so roofs read lighter than walls). The key light is a cool moonlight, the only light that casts
    // shadows (one cascade, the pipeline's shadow distance); a weak warm fill from the opposite side stands for the street
    // glow. Lit shop windows are unlit colours in the world art, not lights. No skybox: the camera clears to the palette
    // background, and linear distance fog toward that same colour lets the skyline sink into the night.
    public static class SceneLighting
    {
        // Linear-space ambient: Equator everywhere plus one lobe from above that brings upward faces to about Sky.
        // A negative lobe from below would ring through L2 and turn downward faces negative, so ground = equator.
        public static readonly Color Sky = new Color(.30f, .33f, .40f);
        public static readonly Color Equator = new Color(.21f, .22f, .25f);
        // The palette background #101113 (ClientApp's clear colour); fog fades toward it.
        public static readonly Color Background = new Color32(16, 17, 19, 255);
        public const float FogStart = 30, FogEnd = 160;

        public static SphericalHarmonicsL2 AmbientProbe()
        {
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(Equator);
            probe.AddDirectionalLight(Vector3.up, Sky - Equator, .5f);
            return probe;
        }

        public static void Apply(Transform parent)
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Background;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Sky.gamma;
            RenderSettings.ambientEquatorColor = Equator.gamma;
            RenderSettings.ambientGroundColor = Equator.gamma;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.ambientProbe = AmbientProbe();
            RenderSettings.sun = Directional(parent, "Key light", Quaternion.Euler(52, -35, 0), 1.0f, new Color(.78f, .85f, 1), LightShadows.Hard);
            Directional(parent, "Fill light", Quaternion.Euler(28, 145, 0), .3f, new Color(1, .86f, .7f), LightShadows.None);
        }

        private static Light Directional(Transform parent, string label, Quaternion rotation, float intensity, Color color, LightShadows shadows)
        {
            var item = new GameObject(label, typeof(Light));
            item.transform.SetParent(parent, false);
            item.transform.rotation = rotation;
            var light = item.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = shadows;
            light.shadowStrength = .75f;
            light.shadowBias = .05f;
            light.shadowNormalBias = .4f;
            return light;
        }
    }
}
