using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 2: the world is built from Server.Core's FixedMap; MapSource and its TODO are gone.
    public sealed class MapQaTests
    {
        [Test]
        public void TheClientDrawsTheServersFixedMapWallForWall()
        {
            var server = new FixedMap();
            Assert.That(SharedMap.Map.Walls.Count, Is.EqualTo(8), "wall count (4 borders + 4 blocks)");
            Assert.That(SharedMap.Map.Walls.Count, Is.EqualTo(server.Walls.Count));
            for (int i = 0; i < server.Walls.Count; i++)
            {
                Assert.That(SharedMap.Map.Walls[i].MinX, Is.EqualTo(server.Walls[i].MinX), "wall " + i + " MinX");
                Assert.That(SharedMap.Map.Walls[i].MinZ, Is.EqualTo(server.Walls[i].MinZ), "wall " + i + " MinZ");
                Assert.That(SharedMap.Map.Walls[i].MaxX, Is.EqualTo(server.Walls[i].MaxX), "wall " + i + " MaxX");
                Assert.That(SharedMap.Map.Walls[i].MaxZ, Is.EqualTo(server.Walls[i].MaxZ), "wall " + i + " MaxZ");
            }
        }

        [Test]
        public void ExitTerminalsSizeAndCentreAreTheServersValues()
        {
            Assert.That(FixedMap.Size, Is.EqualTo(60f));
            Assert.That(SharedMap.Map.Exit.X, Is.EqualTo(58f));
            Assert.That(SharedMap.Map.Exit.Z, Is.EqualTo(30f));
            Assert.That(SharedMap.Exit.x, Is.EqualTo(58f));
            Assert.That(SharedMap.Exit.z, Is.EqualTo(30f));
            Assert.That(SharedMap.Center.x, Is.EqualTo(30f));
            Assert.That(SharedMap.Center.z, Is.EqualTo(30f));
            Assert.That(SharedMap.Center.y, Is.EqualTo(0f));
            Assert.That(SharedMap.Map.TerminalPositions.Count, Is.EqualTo(6), "terminal positions");
            float[,] expected = { { 10, 25 }, { 28, 12 }, { 49, 25 }, { 12, 49 }, { 30, 52 }, { 50, 42 } };
            for (int i = 0; i < 6; i++)
            {
                Assert.That(SharedMap.Map.TerminalPositions[i].X, Is.EqualTo(expected[i, 0]), "terminal " + i + " X");
                Assert.That(SharedMap.Map.TerminalPositions[i].Z, Is.EqualTo(expected[i, 1]), "terminal " + i + " Z");
            }
        }

        [Test]
        public void AWallBoxIsCentredOnAndSizedLikeTheMinMaxBox()
        {
            var wall = new Wall(18, 14, 20, 32);
            Vector3 center, size;
            SharedMap.WallBox(wall, out center, out size);
            Assert.That(center.x, Is.EqualTo(19f).Within(1e-4f));
            Assert.That(center.z, Is.EqualTo(23f).Within(1e-4f));
            Assert.That(size.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(size.z, Is.EqualTo(18f).Within(1e-4f));
            foreach (Wall each in SharedMap.Map.Walls)
            {
                SharedMap.WallBox(each, out center, out size);
                Assert.That(center.x - size.x * .5f, Is.EqualTo(each.MinX).Within(1e-3f));
                Assert.That(center.x + size.x * .5f, Is.EqualTo(each.MaxX).Within(1e-3f));
                Assert.That(center.z - size.z * .5f, Is.EqualTo(each.MinZ).Within(1e-3f));
                Assert.That(center.z + size.z * .5f, Is.EqualTo(each.MaxZ).Within(1e-3f));
            }
        }

        [Test]
        public void NoHardCodedMapCopyOrItsTodoRemains()
        {
            string scripts = QaPaths.ScriptsDir();
            Assert.That(File.Exists(Path.Combine(scripts, "World/MapSource.cs")), Is.False, "MapSource.cs is deleted");
            Assert.That(File.Exists(Path.Combine(scripts, "World/MapSource.cs.meta")), Is.False, "MapSource.cs.meta is deleted");
            foreach (string path in QaPaths.AllScripts())
            {
                string raw = File.ReadAllText(path);
                string stripped = QaSource.StripComments(raw);
                string name = Path.GetFileName(path);
                Assert.That(stripped, Does.Not.Contain("MapSource"), name);
                Assert.That(raw, Does.Not.Contain("TODO(U4b)"), name + " still carries the share-the-map TODO");
                Assert.That(Regex.IsMatch(stripped, @"new\s+Wall\s*\("), Is.False, name + " builds walls by hand");
                Assert.That(Regex.IsMatch(stripped, @"\b58f?\s*,\s*30f?\b"), Is.False, name + " hard-codes the exit position");
                Assert.That(Regex.IsMatch(stripped, @"\b18f?\s*,\s*14f?\s*,\s*20f?\s*,\s*32f?\b"), Is.False, name + " hard-codes a wall");
            }
        }

        [Test]
        public void TheWorldViewBuildsFromTheSharedMap()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("World/WorldView.cs"));
            Assert.That(source, Does.Contain("SharedMap.Map"));
            Assert.That(source, Does.Contain("map.Walls"));
            Assert.That(source, Does.Contain("map.TerminalPositions"));
            Assert.That(source, Does.Contain("SharedMap.Exit"));
            string camera = QaSource.StripComments(QaPaths.ReadScript("World/RoleCamera.cs"));
            Assert.That(camera, Does.Contain("SharedMap.Center"));
        }

        [Test]
        public void TheClientAssemblyStillReferencesServerCoreThroughThePackageManifest()
        {
            string manifest = File.ReadAllText(Path.Combine(Path.GetDirectoryName(QaPaths.Assets()), "Packages/manifest.json"));
            Assert.That(manifest, Does.Contain("\"com.pingthekiller.servercore\": \"file:../../Server.Core\""));
            Assert.That(manifest, Does.Contain("\"com.pingthekiller.shared\": \"file:../../Shared\""));
            string asmdef = File.ReadAllText(Path.Combine(QaPaths.ScriptsDir(), "PingTheKiller.Client.asmdef"));
            Assert.That(asmdef, Does.Contain("PingTheKiller.ServerCore"));
        }
    }
}
