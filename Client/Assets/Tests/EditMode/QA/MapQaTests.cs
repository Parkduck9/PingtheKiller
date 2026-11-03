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
            Assert.That(SharedMap.Map.Walls.Count, Is.EqualTo(31), "wall count (4 borders + 27 building boxes, U6e map v2.1)");
            Assert.That(SharedMap.Map.Walls.Count, Is.EqualTo(server.Walls.Count));
            Assert.That(SharedMap.Map.Obstacles.Count, Is.EqualTo(15), "obstacle count (U6e map v2.1)");
            Assert.That(SharedMap.Map.Obstacles.Count, Is.EqualTo(server.Obstacles.Count));
            for (int i = 0; i < server.Obstacles.Count; i++)
            {
                Assert.That(SharedMap.Map.Obstacles[i].MinX, Is.EqualTo(server.Obstacles[i].MinX), "obstacle " + i + " MinX");
                Assert.That(SharedMap.Map.Obstacles[i].MinZ, Is.EqualTo(server.Obstacles[i].MinZ), "obstacle " + i + " MinZ");
                Assert.That(SharedMap.Map.Obstacles[i].MaxX, Is.EqualTo(server.Obstacles[i].MaxX), "obstacle " + i + " MaxX");
                Assert.That(SharedMap.Map.Obstacles[i].MaxZ, Is.EqualTo(server.Obstacles[i].MaxZ), "obstacle " + i + " MaxZ");
            }
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
            var server = new FixedMap();
            Assert.That(FixedMap.Size, Is.EqualTo(80f));
            Assert.That(SharedMap.Map.Exits.Count, Is.EqualTo(2), "two exits (U6b)");
            Assert.That(SharedMap.Map.Exit.X, Is.EqualTo(server.Exits[0].X));
            Assert.That(SharedMap.Map.Exit.Z, Is.EqualTo(server.Exits[0].Z));
            Assert.That(SharedMap.Exit.x, Is.EqualTo(server.Exits[0].X));
            Assert.That(SharedMap.Exit.z, Is.EqualTo(server.Exits[0].Z));
            Assert.That(SharedMap.ExitAt(1).x, Is.EqualTo(server.Exits[1].X));
            Assert.That(SharedMap.ExitAt(1).z, Is.EqualTo(server.Exits[1].Z));
            Assert.That(SharedMap.Center.x, Is.EqualTo(40f));
            Assert.That(SharedMap.Center.z, Is.EqualTo(40f));
            Assert.That(SharedMap.Center.y, Is.EqualTo(0f));
            Assert.That(SharedMap.Map.TerminalPositions.Count, Is.EqualTo(8), "terminal positions (8 placed, U6b)");
            Assert.That(SharedMap.Map.TerminalPositions.Count, Is.EqualTo(server.TerminalPositions.Count));
            for (int i = 0; i < server.TerminalPositions.Count; i++)
            {
                Assert.That(SharedMap.Map.TerminalPositions[i].X, Is.EqualTo(server.TerminalPositions[i].X), "terminal " + i + " X");
                Assert.That(SharedMap.Map.TerminalPositions[i].Z, Is.EqualTo(server.TerminalPositions[i].Z), "terminal " + i + " Z");
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
            string source = QaSource.StripComments(WorldSources.ReadAll());
            Assert.That(source, Does.Contain("SharedMap.Map"));
            Assert.That(source, Does.Contain("map.Walls"));
            Assert.That(source, Does.Contain("map.TerminalPositions"));
            Assert.That(source, Does.Contain("SharedMap.Exit"));
            string camera = QaSource.StripComments(QaPaths.ReadScript("World/RoleCamera.cs"));
            Assert.That(camera, Does.Contain("SharedMap.Center"));
        }

        [Test]
        public void TheWorldViewAlsoDrawsTheObstaclesAndBothExits()
        {
            string source = QaSource.StripComments(WorldSources.ReadAll());
            Assert.That(source, Does.Contain("map.Obstacles"));
            Assert.That(source, Does.Contain("SharedMap.ExitAt(1)"));
        }

        [Test]
        public void TheKillerRoomFixtureHasTheLegacyWallBetweenTheKillerAndTheSurvivorsBehindIt()
        {
            // QA support: the legacy 60 m Killer-view scenes are laid on the 80 m map by KillerRoom; check it keeps their relations.
            var map = SharedMap.Map;
            Assert.That(KillerRoom.MapX(18), Is.EqualTo(KillerRoom.Building.MinX), "legacy wall face x 18 is the building's west face");
            Assert.That(KillerRoom.MapX(20), Is.EqualTo(KillerRoom.Building.MaxX), "legacy wall face x 20 is the building's east face");
            Assert.That(KillerRoom.Building.MinX - KillerRoom.X, Is.EqualTo(8f), "the wall stands eight metres ahead of the Killer");
            Assert.That(map.CanStand(KillerRoom.X, KillerRoom.Z), Is.True);
            foreach (float[] inFront in new[] { new[] { 15f, 20f }, new[] { 14f, 26f }, new[] { 15f, 23f } })
            {
                Assert.That(map.CanStand(KillerRoom.MapX(inFront[0]), KillerRoom.MapZ(inFront[1])), Is.True, "in front " + inFront[0] + "," + inFront[1]);
                Assert.That(MapGeometry.SegmentBlocked(map, KillerRoom.X, KillerRoom.Z, KillerRoom.MapX(inFront[0]), KillerRoom.MapZ(inFront[1])), Is.False);
            }
            foreach (float[] behind in new[] { new[] { 26f, 22f }, new[] { 25f, 21f }, new[] { 32f, 27f }, new[] { 26f, 23f } })
            {
                Assert.That(map.CanStand(KillerRoom.MapX(behind[0]), KillerRoom.MapZ(behind[1])), Is.True, "behind " + behind[0] + "," + behind[1]);
                Assert.That(MapGeometry.SegmentBlocked(map, KillerRoom.X, KillerRoom.Z, KillerRoom.MapX(behind[0]), KillerRoom.MapZ(behind[1])), Is.True);
                Assert.That(MapGeometry.SegmentBlocked(map, KillerRoom.MapX(22), KillerRoom.Z, KillerRoom.MapX(behind[0]), KillerRoom.MapZ(behind[1])), Is.False, "from just past the wall");
            }
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
