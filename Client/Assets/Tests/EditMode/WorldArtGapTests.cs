using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Editor.Setup;
using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // U4l: no see-through gaps up close, at eye level; the art fills its collision boxes (brief U4l-gapfill-a1). The scan is
    // WorldArtGapScan at a reduced density (2 m eye spacing, 15 degree yaw steps, one eye distance), run once for the class.
    public sealed class WorldArtGapTests
    {
        private static FixedMap Map => SharedMap.Map;
        private WorldArtGapScan.Report report;

        [OneTimeSetUp]
        public void ScanOnce()
        {
            WorldArt art = WorldArt.Load();
            Assert.That(art, Is.Not.Null, "Resources/WorldArt.asset (Ping the Killer/Setup World Art)");
            Assert.That(art.Matches(Map), Is.True, "the catalog was built for this map");
            report = WorldArtGapScan.Scan(art, WorldArtGapScan.Density.Reduced, true);
        }

        private static string List(WorldArtGapScan.Report r)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < r.Clusters.Count && i < 15; i++)
            {
                WorldArtGapScan.Cluster c = r.Clusters[i];
                text.Append($"\n{c.Kind} {c.Box} {c.Face} at {c.Height:0.0} m, {c.From:0.0}-{c.To:0.0}: {c.Rays} rays, {c.WorstInside:0.00} m inside");
            }
            return text.ToString();
        }

        [Test]
        public void NoEyeLevelRaySeesThroughAWallOrObstacleBox()
        {
            Assert.That(report.EyePoints, Is.GreaterThan(500), "the scan covers the streets");
            Assert.That(report.Rays, Is.GreaterThan(20000));
            Assert.That(report.SeeThrough, Is.EqualTo(0), List(report));
        }

        [Test]
        public void NoBuildingGroupHasAThroughGapAboveThePodium()
        {
            Assert.That(report.UpperRays, Is.GreaterThan(100));
            Assert.That(report.UpperGaps, Is.EqualTo(0), string.Join("\n", report.Upper));
        }

        [Test]
        public void NoSkyShowsBetweenTheSkylinePiecesOverTheBorderWalls()
        {
            Assert.That(report.SkyRays, Is.GreaterThan(1000));
            Assert.That(report.SkyGaps, Is.EqualTo(0), string.Join("\n", report.Sky));
        }

        [Test]
        public void EveryObstacleFillsItsBoxToNinetyFivePercentPerAxisAndStaysInsideIt()
        {
            Assert.That(report.Obstacles.Count, Is.EqualTo(Map.Obstacles.Count));
            foreach (WorldArtGapScan.Fill fill in report.Obstacles)
            {
                string name = "obstacle " + fill.Box;
                Assert.That(fill.X, Is.GreaterThanOrEqualTo(.95f), name + " x");
                Assert.That(fill.Z, Is.GreaterThanOrEqualTo(.95f), name + " z");
                Assert.That(fill.Y, Is.GreaterThanOrEqualTo(.95f), name + " height against MapGeometry.ObstacleHeight");
                Assert.That(fill.Over, Is.LessThanOrEqualTo(.05f), name + " exceeds its box");
                Assert.That(fill.Top, Is.LessThanOrEqualTo(WorldArtSetup.MaxObstacleHeight + .001f), name + " top");
            }
        }

        [Test]
        public void ThePodiumCoversEveryWallFaceFromTheFloorToWallHeight()
        {
            Assert.That(report.Podiums.Count, Is.EqualTo(Map.Walls.Count), "every wall has a podium");
            foreach (WorldArtGapScan.Fill fill in report.Podiums)
            {
                string name = "wall " + fill.Box;
                Assert.That(fill.X, Is.GreaterThanOrEqualTo(.999f), name + " x");
                Assert.That(fill.Z, Is.GreaterThanOrEqualTo(.999f), name + " z");
                Assert.That(fill.Y, Is.GreaterThanOrEqualTo(.999f), name + " 0 to 3 m");
            }
        }
    }
}
