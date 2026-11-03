using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PingTheKiller.Client.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.Tests
{
    // U4i: toon shading for characters and world art, no outlines, no rim, no specular, URP Lit fallback.
    public sealed class ToonShadingTests
    {
        private const string LitName = "Universal Render Pipeline/Lit";
        // Documented exceptions: lit windows and signs are unlit colours by design (U4h), the Exit marker keeps its particle
        // material, and the dither variant is its own shader (PtkDither, with matching bands).
        private static readonly string[] UnlitByDesign = { "Glass lit warm", "Glass lit cool", "Sign warm", "Sign cool" };

        private static IEnumerable<Material> ArtMaterials(params string[] folders)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", folders))
                yield return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 1e-3f;

        private static bool IsException(Material material)
            => (material.shader.name == "Universal Render Pipeline/Unlit" && System.Array.IndexOf(UnlitByDesign, material.name) >= 0)
               || (material.name.StartsWith("Exit marker ") && material.shader.name.StartsWith("Shader Graphs/HS_"));

        [Test]
        public void EveryWorldAndCharacterMaterialIsToonExceptTheDocumentedOnes()
        {
            int toon = 0;
            foreach (Material material in ArtMaterials("Assets/Art/World/Materials", "Assets/Art/Characters"))
            {
                if (IsException(material)) continue;
                Assert.That(ToonShading.IsToon(material), Is.True, material.name + " uses " + material.shader.name);
                toon++;
            }
            Assert.That(toon, Is.GreaterThan(150));
            WorldArt art = WorldArt.Load();
            Assert.That(art, Is.Not.Null);
            var prefabs = new List<GameObject> { art.Ground, art.Backdrop, art.Terminal, art.ExitMarker };
            foreach (WorldArt.Piece piece in art.Walls) prefabs.Add(piece.Prefab);
            foreach (WorldArt.Piece piece in art.Obstacles) prefabs.Add(piece.Prefab);
            CharacterArt characters = CharacterArt.Load();
            prefabs.Add(characters.SurvivorPrefab); prefabs.Add(characters.KillerPrefab);
            foreach (GameObject prefab in prefabs)
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                        Assert.That(ToonShading.IsToon(material) || IsException(material), Is.True, prefab.name + " / " + renderer.name + ": " + material.shader.name);
        }

        [Test]
        public void CharactersUseThreeBandsAndWorldArtTwo()
        {
            CharacterArt art = CharacterArt.Load();
            foreach (Material material in new[] { art.SurvivorMaterial, art.KillerMaterial, art.NeutralMaterial })
            {
                Assert.That(ToonShading.IsToon(material), Is.True, material.name);
                Assert.That(material.IsKeywordEnabled("_USE_SECOND_THRESHOLD"), Is.True, material.name);
            }
            foreach (Material material in ArtMaterials("Assets/Art/World/Materials"))
            {
                if (!ToonShading.IsToon(material)) continue;
                Assert.That(material.IsKeywordEnabled("_USE_SECOND_THRESHOLD"), Is.False, material.name);
                Assert.That(material.IsKeywordEnabled("_RECEIVE_SHADOWS_ON"), Is.True, material.name);
            }
        }

        [Test]
        public void TheGroundHasASoftShadowEdgeAndBuildingsAHardOne()
        {
            var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/World/Materials/Asphalt.mat");
            var wall = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/World/Materials/Roof.mat");
            Vector4 soft = ground.GetVector("_ShadowThresholds"), hard = wall.GetVector("_ShadowThresholds");
            Assert.That(soft.y - soft.x, Is.GreaterThanOrEqualTo(.3f), "soft edge on the ground");
            // Attempt 2: buildings take the soft-shadow (PCF) ramp through a window wide enough to anti-alias the edge, but
            // still fully dark at full shadow and fully lit well before full light, so the cast shadow stays a toon shape.
            Assert.That(hard.y - hard.x, Is.InRange(.2f, .5f), "anti-aliased but still a toon edge on buildings");
            Assert.That(hard.y, Is.LessThan(.9f), "fully lit before the light reaches full attenuation");
            // The key light's shadow strength .75 leaves attenuation .25 in shadow: both edges must sit above it.
            Assert.That(soft.y, Is.GreaterThan(.25f)); Assert.That(hard.x, Is.GreaterThan(.25f));
        }

        [Test]
        public void TheNightGradeDarkensAndDesaturatesWithoutCrushingTheDarks()
        {
            var wall = new Color32(188, 178, 158, 255);
            Color graded = ToonShading.NightGrade(wall);
            Color.RGBToHSV(wall, out _, out float s0, out float v0);
            Color.RGBToHSV(graded, out _, out float s1, out float v1);
            Assert.That(v1, Is.InRange(v0 * .6f, v0 * .72f), "value about x.65 for the bright walls");
            Assert.That(s1, Is.LessThan(s0 * .55f), "desaturated by about half");
            Assert.That(graded.b - graded.r, Is.GreaterThan(((Color)wall).b - ((Color)wall).r), "slightly cooler");
            var asphalt = new Color32(27, 28, 31, 255);
            Assert.That(ToonShading.NightGrade(asphalt).maxColorComponent, Is.GreaterThan(((Color)asphalt).maxColorComponent * .95f), "dark ground keeps its value");
            Assert.That(ToonShading.NightGrade(Color.white).maxColorComponent, Is.GreaterThan(.6f), "never near black");
        }

        [Test]
        public void TheLitWindowsStayUngradedAndBrighterThanEveryWorldWall()
        {
            float window = float.MaxValue;
            foreach (string name in UnlitByDesign)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/World/Materials/" + name + ".mat");
                Assert.That(material, Is.Not.Null, name);
                Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"), name);
                window = Mathf.Min(window, material.GetColor("_BaseColor").linear.grayscale);
            }
            var warm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/World/Materials/Glass lit warm.mat").GetColor("_BaseColor");
            Assert.That(warm.r, Is.GreaterThan(warm.b), "the lit windows stay warm");
            // Upper bound of a lit wall on screen: albedo x (light tint at full key light + the ambient from above).
            float light = ToonShading.WorldLight.grayscale + SceneLighting.Sky.grayscale;
            // Every wall colour the current buildings use (unused leftovers of earlier map versions are not on screen).
            WorldArt art = WorldArt.Load();
            Assert.That(art, Is.Not.Null);
            var seen = new HashSet<Material>();
            foreach (WorldArt.Piece piece in art.Walls)
                foreach (Renderer renderer in piece.Prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (!ToonShading.IsToon(material) || !material.name.StartsWith("Wall ") || !seen.Add(material)) continue;
                        Assert.That(material.GetColor("_BaseColor").linear.grayscale * light, Is.LessThan(window), material.name + " stays darker than the lit windows");
                    }
            Assert.That(seen.Count, Is.GreaterThan(3));
        }

        [Test]
        public void TheShadowTintIsCoolDarkAndNeverBlack()
        {
            foreach (Color shadow in new[] { ToonShading.WorldShadow, ToonShading.CharacterShadow })
            {
                Assert.That(shadow.maxColorComponent, Is.GreaterThan(.05f), "never pure black");
                Assert.That(shadow.maxColorComponent, Is.LessThan(.4f), "dark");
                Assert.That(shadow.b, Is.GreaterThan(shadow.r), "cool");
            }
            Assert.That(ToonShading.WorldLight.grayscale, Is.GreaterThan(ToonShading.WorldShadow.grayscale + .2f), "two bands that read");
        }

        [Test]
        public void RimAndSpecularAreOffOnEveryToonMaterial()
        {
            var all = new List<Material>(ArtMaterials("Assets/Art", "Assets/Resources"));
            Assert.That(all.Count, Is.GreaterThan(0));
            foreach (Material material in all)
            {
                if (!ToonShading.IsToon(material)) continue;
                Assert.That(ToonShading.RimOff(material), Is.True, material.name + " rim");
                Assert.That(material.GetVector("_RimThresholds").x, Is.GreaterThanOrEqualTo(.99f), material.name + " rim threshold");
                Assert.That(ToonShading.SpecularOff(material), Is.True, material.name + " specular");
                Assert.That(material.GetFloat("_Smoothness"), Is.Zero, material.name);
            }
        }

        [Test]
        public void NoMaterialOrRendererUsesAPackageOutline()
        {
            string package = "Assets/ThirdParty/Toon Shaders Pro/Shaders/Outline/";
            var banned = new List<string>();
            foreach (string name in new[] { "Resources/HullOutlines.shader", "Resources/Outline.shader", "Resources/MaskObject.shader", "Scripts/OutlineFeature.cs" })
                banned.Add(AssetDatabase.AssetPathToGUID(package + name));
            Assert.That(banned, Has.None.Empty, "package outline assets found");
            foreach (string path in Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories))
            {
                if (!(path.EndsWith(".mat") || path.EndsWith(".asset") || path.EndsWith(".prefab"))) continue;
                if (path.Replace('\\', '/').Contains("/ThirdParty/")) continue;
                string text = File.ReadAllText(path);
                foreach (string guid in banned) Assert.That(text, Does.Not.Contain(guid), path);
            }
            Material outline = CharacterLook.Outline(CharacterLook.Signal);
            Assert.That(outline.shader.name, Is.EqualTo("PingTheKiller/Outline"), "the Killer view's signal outline stays");
            Object.DestroyImmediate(outline);
        }

        [Test]
        public void TheToonShaderShipsThroughResourcesAndOnlyFixesTheDoubleAlbedo()
        {
            Shader shader = ToonShading.Find();
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(shader.name, Is.EqualTo(ToonShading.ShaderName));
            foreach (string template in new[] { ToonShading.Template, ToonShading.FadeTemplate })
                Assert.That(Resources.Load<Material>(template).shader, Is.SameAs(shader), template);
            Assert.That(Resources.Load<Material>(ToonShading.FadeTemplate).GetFloat("_Surface"), Is.EqualTo(1));
            string fork = File.ReadAllText(Path.Combine(Application.dataPath, "Resources/Shaders/PtkToon.shader"));
            string vendor = File.ReadAllText(Path.Combine(Application.dataPath, "ThirdParty/Toon Shaders Pro/Shaders/Toon/Toon.shader"));
            Assert.That(vendor, Does.Contain("baseColor.rgb *= lightColor;"), "vendor file untouched");
            Assert.That(fork, Does.Not.Contain("baseColor.rgb *= lightColor;"));
            Assert.That(fork, Does.Contain("baseColor.rgb = lightColor;"));
            Assert.That(fork, Does.Contain("Toon Shaders Pro/Shaders/Toon/ToonFunctions.hlsl"), "vendor lighting functions reused");
            Assert.That(fork, Does.Not.Contain("Cull Front"), "no inverted-hull pass");
        }

        [Test]
        public void TheLitFallbackIsUsedWhenTheToonShaderIsMissing()
        {
            var made = new List<Material>
            {
                WorldMaterials.NewMaterial(false, null), WorldMaterials.NewMaterial(true, null),
                WorldMaterials.NewMaterial(false, ToonShading.Find()), WorldMaterials.NewMaterial(true, ToonShading.Find())
            };
            try
            {
                Assert.That(made[0].shader.name, Is.EqualTo(LitName));
                Assert.That(made[1].shader.name, Is.EqualTo(LitName));
                Assert.That(made[1].GetFloat("_Surface"), Is.EqualTo(1), "WorldLitFade");
                Assert.That(ToonShading.IsToon(made[2]), Is.True);
                Assert.That(ToonShading.IsToon(made[3]), Is.True);
                string killer = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Killer/KillerWorldLayer.cs"));
                Assert.That(killer, Does.Contain("WorldMaterials.NewMaterial(false, ToonShading.Find())"), "Killer-view fallback body uses the same path");
            }
            finally { foreach (Material material in made) Object.DestroyImmediate(material); }
        }

        [Test]
        public void RuntimeMaterialsAreToonAndKeepTheirExactColour()
        {
            var root = new GameObject("toon");
            var materials = new WorldMaterials(root.transform);
            Material opaque = null, fade = null;
            try
            {
                opaque = materials.MakeMaterial(WorldMaterials.Survivor);
                fade = materials.MakeMaterial(WorldMaterials.Survivor, true);
                Assert.That(ToonShading.IsToon(opaque) && ToonShading.IsToon(fade), Is.True);
                Assert.That(Near(opaque.color, WorldMaterials.Survivor), Is.True, "SetTerminalLook/SetExitLook colours are unchanged");
                Assert.That(fade.GetFloat("_Surface"), Is.EqualTo(1));
                Assert.That(fade.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            }
            finally
            {
                if (opaque != null) Object.DestroyImmediate(opaque);
                if (fade != null) Object.DestroyImmediate(fade);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TheKillerViewSurvivorMaterialStaysSharedAndNeutral()
        {
            CharacterArt art = CharacterArt.Load();
            Material neutral = art.NeutralMaterial;
            Color c = neutral.GetColor("_BaseColor");
            Assert.That(Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b), Is.LessThan(.03f), "achromatic");
            Assert.That(neutral.GetTexture("_BaseMap"), Is.Null, "no per-player texture");
            var roots = new List<GameObject>();
            try
            {
                for (int k = 0; k < 2; k++)
                {
                    GameObject model = Object.Instantiate(art.SurvivorPrefab);
                    roots.Add(model);
                    var instance = new CharacterInstance(model, true);
                    foreach (Renderer renderer in instance.Renderers)
                    {
                        Material[] slots = renderer.sharedMaterials;
                        for (int i = 0; i < slots.Length; i++) slots[i] = neutral;
                        renderer.sharedMaterials = slots;
                    }
                    instance.Look(Color.red, .5f, true); // a shared neutral instance ignores looks
                    foreach (Renderer renderer in instance.Renderers)
                        foreach (Material material in renderer.sharedMaterials) Assert.That(material, Is.SameAs(neutral));
                }
            }
            finally { foreach (GameObject root in roots) Object.DestroyImmediate(root); }
        }

        [Test]
        public void FadeAndDitherVariantsKeepTheToonBands()
        {
            CharacterArt art = CharacterArt.Load();
            GameObject model = Object.Instantiate(art.SurvivorPrefab);
            var instance = new CharacterInstance(model);
            try
            {
                instance.Look(WorldMaterials.Survivor, .5f, false);
                foreach (Renderer renderer in instance.Renderers)
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(ToonShading.IsToon(material), Is.True, "fade stays toon");
                        Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1));
                    }
                instance.Look(WorldMaterials.Survivor, 1, true);
                foreach (Renderer renderer in instance.Renderers)
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material.shader.name, Is.EqualTo("PingTheKiller/Dither"));
                        Assert.That(material.GetFloat("_Toon"), Is.EqualTo(1), "dither uses the toon bands");
                        Assert.That(Near(material.GetColor("_ShadowTint"), art.SurvivorMaterial.GetColor("_ShadowTint")), Is.True);
                    }
            }
            finally { instance.Dispose(); Object.DestroyImmediate(model); }
        }

    }
}
