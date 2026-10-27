using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // Scene lighting, built by script. The client scene has no baked lighting data, so Unity's ambient probe stays black
    // and every face turned away from the key light rendered pure black. URP reads RenderSettings.ambientProbe for the
    // ambient term of objects without light probes, so the probe is set directly here (dark-palette trilight: brighter
    // from above, so wall tops read lighter than wall sides), plus a weak fill light from the opposite side.
    // No skybox: the camera clears to the palette background.
    public static class SceneLighting
    {
        // Linear-space ambient: Equator everywhere plus one lobe from above that brings upward faces to about Sky.
        // A negative lobe from below would ring through L2 and turn downward faces negative, so ground = equator.
        public static readonly Color Sky = new Color(.40f, .42f, .46f);
        public static readonly Color Equator = new Color(.28f, .29f, .31f);

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
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Sky.gamma;
            RenderSettings.ambientEquatorColor = Equator.gamma;
            RenderSettings.ambientGroundColor = Equator.gamma;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.ambientProbe = AmbientProbe();
            RenderSettings.sun = Directional(parent, "Key light", Quaternion.Euler(55, -30, 0), 1.2f, new Color(1, .98f, .95f));
            Directional(parent, "Fill light", Quaternion.Euler(30, 150, 0), .35f, new Color(.85f, .9f, 1));
        }

        private static Light Directional(Transform parent, string label, Quaternion rotation, float intensity, Color color)
        {
            var item = new GameObject(label, typeof(Light));
            item.transform.SetParent(parent, false);
            item.transform.rotation = rotation;
            var light = item.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            return light;
        }
    }
}
