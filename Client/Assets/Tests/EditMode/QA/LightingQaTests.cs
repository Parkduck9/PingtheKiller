using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a2.md section 2 and SLOP section 8, by code read (Unity is not launched):
    // URP only, background stays #101113, ambient and fill light set in code, terminals distinct from capsules.
    [Category("U4bQaA2")]
    public sealed class LightingQaTests
    {
        private static string Code(string relative) => QaSource.StripComments(QaPaths.ReadScript(relative));

        // ---- no Built-in shader -----------------------------------------------------------------------------------------------

        [Test]
        public void EveryShaderTheClientNamesIsUniversalRenderPipeline()
        {
            int finds = 0;
            foreach (string path in QaPaths.AllScripts())
            {
                string code = QaSource.StripComments(File.ReadAllText(path));
                foreach (Match match in Regex.Matches(code, @"Shader\.Find\(\s*""([^""]*)""\s*\)"))
                {
                    finds++;
                    Assert.That(match.Groups[1].Value, Does.StartWith("Universal Render Pipeline/"), Path.GetFileName(path) + " names a non-URP shader");
                }
                Assert.That(code, Does.Not.Match(@"Shader\.Find\(\s*[^""\s]"), Path.GetFileName(path) + ": a computed shader name cannot be verified");
            }
            Assert.That(finds, Is.GreaterThanOrEqualTo(1), "the WorldView fallback is the only expected Shader.Find");
        }

        [Test]
        public void NoStringLiteralInTheClientNamesABuiltInShaderOrMaterial()
        {
            string[] builtIn = { "Standard", "Legacy Shaders/", "Unlit/", "Sprites/Default", "Mobile/", "Particles/Standard", "Diffuse", "Hidden/Internal", "Skybox/" };
            foreach (string path in QaPaths.AllScripts())
                foreach (string literal in QaSource.StringLiterals(QaSource.StripComments(File.ReadAllText(path))))
                    foreach (string name in builtIn)
                        Assert.That(literal, Does.Not.StartWith(name), Path.GetFileName(path) + ": literal \"" + literal + "\"");
        }

        [Test]
        public void TheWorldMaterialsInResourcesUseTheSameUrpShaderAndNotTheBuiltInStandardOne()
        {
            string lit = File.ReadAllText(Path.Combine(QaPaths.Assets(), "Resources/WorldLit.mat"));
            string fade = File.ReadAllText(Path.Combine(QaPaths.Assets(), "Resources/WorldLitFade.mat"));
            foreach (string material in new[] { lit, fade })
            {
                Match shader = Regex.Match(material, @"m_Shader:\s*\{fileID:\s*(\d+),\s*guid:\s*([0-9a-f]{32})");
                Assert.That(shader.Success, Is.True, "m_Shader reference found");
                Assert.That(shader.Groups[2].Value, Is.Not.EqualTo("0000000000000000f000000000000000"), "Built-in shaders live in the null-guid library (Standard is fileID 46)");
                Assert.That(shader.Groups[2].Value, Is.EqualTo("933532a4fcc9baf4fa0491de14d08ed7"), "URP Lit");
            }
        }

        [Test]
        public void TheMapRendersThroughOneMaterialPathWorldViewMakeMaterial()
        {
            string world = QaSource.StripComments(WorldSources.ReadAll());
            Assert.That(Regex.Matches(world, @"new Material\(").Count, Is.EqualTo(2), "template copy and the URP/Lit fallback only");
            Assert.That(world, Does.Contain("Resources.Load<Material>"));
            Assert.That(Regex.Matches(world, @"CreatePrimitive\(").Count, Is.EqualTo(1), "all primitives come from Primitive(), which assigns MakeMaterial");
            Assert.That(world, Does.Not.Contain("GetComponent<Renderer>().material ="), "no per-object Built-in default material swap");
        }

        // ---- background and sky -----------------------------------------------------------------------------------------------

        [Test]
        public void TheCameraClearsToThePaletteBackgroundSixteenSeventeenNineteen()
        {
            string app = Code("ClientApp.cs");
            Assert.That(app, Does.Match(@"clearFlags\s*=\s*CameraClearFlags\.SolidColor"));
            Assert.That(app, Does.Match(@"backgroundColor\s*=\s*new Color32\(\s*16\s*,\s*17\s*,\s*19\s*,\s*255\s*\)"), "#101113");
            Assert.That(0x10, Is.EqualTo(16));
            Assert.That(0x11, Is.EqualTo(17));
            Assert.That(0x13, Is.EqualTo(19));
            Assert.That(Code("World/SceneLighting.cs"), Does.Not.Contain("backgroundColor"), "lighting does not repaint the background");
        }

        [Test]
        public void ThereIsNoSkyboxAndNoFog()
        {
            string lighting = Code("World/SceneLighting.cs");
            Assert.That(lighting, Does.Match(@"RenderSettings\.skybox\s*=\s*null\s*;"));
            Assert.That(lighting, Does.Match(@"RenderSettings\.fog\s*=\s*false\s*;"));
            foreach (string path in QaPaths.AllScripts())
            {
                string code = QaSource.StripComments(File.ReadAllText(path));
                foreach (Match match in Regex.Matches(code, @"RenderSettings\.skybox\s*=\s*([^;]+);"))
                    Assert.That(match.Groups[1].Value.Trim(), Is.EqualTo("null"), Path.GetFileName(path) + " sets a skybox");
                Assert.That(code, Does.Not.Contain("CameraClearFlags.Skybox"), Path.GetFileName(path));
            }
        }

        // ---- ambient and fill light are set in code ------------------------------------------------------------------------------

        [Test]
        public void AmbientLightingIsSetInCodeAndAppliedBeforeAnyMatchStarts()
        {
            string lighting = Code("World/SceneLighting.cs");
            Assert.That(lighting, Does.Match(@"RenderSettings\.ambientMode\s*=\s*AmbientMode\.(Flat|Trilight)\s*;"));
            Assert.That(lighting, Does.Contain("RenderSettings.ambientProbe"), "URP reads the probe for objects without light probes");
            Assert.That(lighting, Does.Match(@"RenderSettings\.ambientSkyColor\s*="));
            Assert.That(lighting, Does.Match(@"RenderSettings\.ambientEquatorColor\s*="));
            string app = Code("ClientApp.cs");
            int apply = app.IndexOf("SceneLighting.Apply(", StringComparison.Ordinal);
            int network = app.IndexOf("DevArgs.Parse(", StringComparison.Ordinal);
            Assert.That(apply, Is.GreaterThanOrEqualTo(0), "ClientApp builds the lighting");
            Assert.That(network, Is.GreaterThan(apply), "lighting is built in Awake/Start before the dev script and any match");
            Assert.That(app, Does.Not.Contain("RenderSettings.ambientLight"), "the old flat ambient is gone");
        }

        [Test]
        public void AFillLightExistsBesideTheKeyLightAndIsWeaker()
        {
            string lighting = Code("World/SceneLighting.cs");
            // Directional(parent, label, Quaternion.Euler(...), intensity, colour)
            Match key = Regex.Match(lighting, @"Directional\(\s*parent\s*,\s*""Key light""\s*,\s*Quaternion\.Euler\([^)]*\)\s*,\s*([0-9.]+)f");
            Match fill = Regex.Match(lighting, @"Directional\(\s*parent\s*,\s*""Fill light""\s*,\s*Quaternion\.Euler\([^)]*\)\s*,\s*([0-9.]+)f");
            Assert.That(key.Success, Is.True, "key light");
            Assert.That(fill.Success, Is.True, "fill light");
            float keyIntensity = float.Parse(key.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float fillIntensity = float.Parse(fill.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(fillIntensity, Is.GreaterThan(0f));
            Assert.That(fillIntensity, Is.LessThan(keyIntensity * 0.5f), "weak fill");
            Assert.That(keyIntensity, Is.LessThanOrEqualTo(1.5f), "not brighter than the old single light (1.5)");
            Assert.That(lighting, Does.Match(@"light\.shadows\s*=\s*LightShadows\.None"), "no shadow cost");
        }

        [Test]
        public void TheAmbientProbeIsDarkNeverBlackAndBrightestFromAbove()
        {
            var directions = new System.Collections.Generic.List<Vector3>();
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (x != 0 || y != 0 || z != 0) directions.Add(new Vector3(x, y, z).normalized);
            var results = new Color[directions.Count];
            PingTheKiller.Client.World.SceneLighting.AmbientProbe().Evaluate(directions.ToArray(), results);
            float up = 0, side = 0;
            for (int i = 0; i < results.Length; i++)
            {
                Color c = results[i];
                Assert.That(float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b), Is.False, "direction " + directions[i]);
                Assert.That(c.r + c.g + c.b, Is.GreaterThan(0.1f), "ambient toward " + directions[i] + " would be near black");
                Assert.That(c.grayscale, Is.LessThan(0.8f), "ambient toward " + directions[i] + " stays dark-palette");
                if (directions[i] == Vector3.up) up = c.grayscale;
            }
            var sideColors = new Color[1];
            PingTheKiller.Client.World.SceneLighting.AmbientProbe().Evaluate(new[] { Vector3.forward }, sideColors);
            side = sideColors[0].grayscale;
            Assert.That(up, Is.GreaterThan(side), "wall tops (facing up) get more ambient than wall sides");
        }

        // ---- terminals do not read like capsules ---------------------------------------------------------------------------------

        [Test]
        public void ATerminalIsMadeOfSquareCubesAndNeverOfACylinderOrCapsule()
        {
            string world = QaSource.StripComments(WorldSources.ReadAll());
            string make = QaSource.MethodText(world, "Terminal MakeTerminal(");
            Assert.That(make, Is.Not.Null);
            Assert.That(make, Does.Not.Contain("PrimitiveType.Cylinder"));
            Assert.That(make, Does.Not.Contain("PrimitiveType.Capsule"));
            Assert.That(make, Does.Not.Contain("PrimitiveType.Sphere"));
            MatchCollection cubes = Regex.Matches(make, @"PrimitiveType\.Cube\s*,[^;]*?new Vector3\(\s*([0-9.]+)f\s*,\s*([0-9.]+)f\s*,\s*([0-9.]+)f\s*\)");
            Assert.That(cubes.Count, Is.EqualTo(2), "a plinth and a panel");
            foreach (Match cube in cubes)
                Assert.That(cube.Groups[1].Value, Is.EqualTo(cube.Groups[3].Value), "x equals z, so the terminal is square seen from above");
        }

        // U4c QA a1, orchestrator ruling 2: HUD_SPEC 0.4 says `ally` is "shown to Survivors only", so this test no longer binds
        // a completed terminal to the team teal for every role. It covers the Survivor client (done = ally teal, never Signal
        // amber) and the muted idle colours; the Killer-client side is KillerColourRolesQaTests.
        [Test]
        public void TerminalColoursAreMutedAndNeverAmberAndDoneIsTheTeamTealOnTheSurvivorClientNotSignalAmber()
        {
            string world = QaSource.StripComments(WorldSources.ReadAll());
            foreach (string name in new[] { "Plinth", "PanelIdle" })
            {
                Match color = Regex.Match(world, @"Color\s+" + name + @"\s*=\s*new Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*255\s*\)");
                Assert.That(color.Success, Is.True, name);
                Color32 value = new Color32(byte.Parse(color.Groups[1].Value), byte.Parse(color.Groups[2].Value), byte.Parse(color.Groups[3].Value), 255);
                Color.RGBToHSV(value, out float hue, out float saturation, out float brightness);
                Assert.That(saturation, Is.LessThan(0.25f), name + " is a muted grey, not a signal colour");
                bool amber = hue >= 20f / 360f && hue <= 50f / 360f && saturation > 0.5f;
                Assert.That(amber, Is.False, name + " must not be amber (reserved for Signal)");
                Assert.That(brightness, Is.GreaterThan(0.25f), name + " is readable against the floor (26,28,31)");
            }
            string accept = QaSource.MethodText(QaSource.StripComments(QaPaths.ReadScript(WorldSources.Props)), "void Accept(");
            Assert.That(accept, Does.Match(@"Panel\.color\s*=[^;]*terminal\.Progress == 100[^;]*\bSurvivor\b[^;]*PanelIdle"),
                "on the Survivor client done is the team tint (the statement may also branch on the role)");
            Match ally = Regex.Match(world, @"Color\s+Survivor\s*=\s*new Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*255\s*\)");
            Assert.That(ally.Success, Is.True, "the Survivor colour literal");
            Assert.That(ally.Groups[1].Value + "," + ally.Groups[2].Value + "," + ally.Groups[3].Value, Is.EqualTo("63,184,175"), "`ally` #3FB8AF");
            Assert.That(accept, Does.Not.Contain("Signal"), "terminals never use the Signal colour");
        }

        [Test]
        public void TheFloorWallsAndTerminalsStayDistinguishableByValue()
        {
            string world = QaSource.StripComments(WorldSources.ReadAll());
            float floor = Luma(Regex.Match(world, @"""Floor""[^;]*?new Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)"));
            float wall = Luma(Regex.Match(world, @"""Wall""[^;]*?new Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)"));
            float plinth = Luma(Regex.Match(world, @"Color\s+Plinth\s*=\s*new Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)"));
            float panel = Luma(Regex.Match(world, @"Color\s+PanelIdle\s*=\s*new Color32\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)"));
            Assert.That(wall, Is.GreaterThan(floor * 2f), "walls are clearly lighter than the floor");
            Assert.That(plinth, Is.GreaterThan(floor * 1.5f), "plinth is lighter than the floor");
            Assert.That(panel, Is.GreaterThan(plinth * 1.5f), "the panel is the lightest part of a terminal");
        }

        private static float Luma(Match color)
        {
            Assert.That(color.Success, Is.True, "colour literal found");
            return 0.2126f * int.Parse(color.Groups[1].Value) + 0.7152f * int.Parse(color.Groups[2].Value) + 0.0722f * int.Parse(color.Groups[3].Value);
        }
    }
}
