using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test item 3. Written from HUD_SPEC 6.2 "Edge-indicator placement" steps 1-6 on the 1920x1080 canvas
    // (the existing implementer tests use 1600x900, so the two sets do not share numbers).
    [Category("U4cQaA1")]
    public sealed class EdgePlacementQaTests
    {
        private static readonly Vector2 Screen = new Vector2(1920, 1080);
        // Killer cluster in normal play: info 72 + 8 + skill 72 = 152 u tall, 320 u wide, margin 24 u (HUD_SPEC 6.1).
        private static readonly Rect KillerCluster = new Rect(24, 1080 - 24 - 152, 320, 152);
        // Survivor cluster: 672 x 72 u, bottom centre, 24 u above the bottom edge (HUD_SPEC 4.0).
        private static readonly Rect SurvivorCluster = new Rect((1920 - 672) / 2f, 1080 - 24 - 72, 672, 72);

        private static void Near(Vector2 actual, float x, float y, string what)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(.05f), what + " x");
            Assert.That(actual.y, Is.EqualTo(y).Within(.05f), what + " y");
        }

        private static bool StrictlyInside(Rect r, Vector2 p) => p.x > r.xMin + 1e-3f && p.x < r.xMax - 1e-3f && p.y > r.yMin + 1e-3f && p.y < r.yMax - 1e-3f;

        // ---- steps 1-2: camera space and direction, including behind the camera ------------------------------------------

        [Test]
        public void TheScreenDirectionIgnoresTheSignOfDepthSoAPointBehindTheCameraMirrorsOntoTheSameEdge()
        {
            foreach (var xy in new[] { (3f, 2f), (-3f, 2f), (3f, -2f), (-3f, -2f), (.4f, 0f), (0f, -2f) })
            {
                Vector2 front = EdgePlacement.Direction(new Vector3(xy.Item1, xy.Item2, 5));
                Vector2 behind = EdgePlacement.Direction(new Vector3(xy.Item1, xy.Item2, -5));
                Assert.That(behind.x, Is.EqualTo(front.x).Within(1e-5f), xy.ToString());
                Assert.That(behind.y, Is.EqualTo(front.y).Within(1e-5f), xy.ToString());
                Assert.That(behind.magnitude, Is.EqualTo(1f).Within(1e-5f), "unit direction");
                if (xy.Item1 != 0) Assert.That(behind.x * xy.Item1, Is.GreaterThan(0f), "x keeps its sign, " + xy);
                if (xy.Item2 != 0) Assert.That(behind.y * xy.Item2, Is.GreaterThan(0f), "y keeps its sign, " + xy);
            }
        }

        [Test]
        public void ADepthOnlyPointStraightBehindTheCameraGoesToTheBottomEdgeCentre()
        {
            Vector2 d = EdgePlacement.Direction(new Vector3(0, 0, -7));
            Assert.That(d.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(d.y, Is.EqualTo(-1f).Within(1e-6f), "y up: -1 is the bottom");
            Rect safe = EdgePlacement.SafeRect(Screen, false);
            Vector2 p = EdgePlacement.Place(Screen, d, safe, EdgePlacement.KeepOut(KillerCluster), false, out Vector2 pointing);
            Near(p, 960, 1056, "bottom centre of the safe rectangle");
            Assert.That(pointing.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(pointing.y, Is.EqualTo(1f).Within(1e-5f), "screen y points down");
        }

        [Test]
        public void ABlipBehindAndToTheRightGoesToTheRightEdgeAtTheCentreHeight()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false);
            Vector2 d = EdgePlacement.Direction(new Vector3(4, 0, -6));
            Vector2 p = EdgePlacement.Place(Screen, d, safe, EdgePlacement.KeepOut(KillerCluster), false, out Vector2 pointing);
            Near(p, 1896, 540, "right edge");
            Assert.That(pointing.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(pointing.y, Is.EqualTo(0f).Within(1e-5f));
        }

        // ---- step 3: the safe rectangle ---------------------------------------------------------------------------------------

        [Test]
        public void TheSafeRectangleIsTheScreenInsetTwentyFourWithItsTopAtFiftySixWhileTheConnectionBandShows()
        {
            Rect plain = EdgePlacement.SafeRect(Screen, false), band = EdgePlacement.SafeRect(Screen, true);
            Assert.That(plain.xMin, Is.EqualTo(24f)); Assert.That(plain.yMin, Is.EqualTo(24f));
            Assert.That(plain.xMax, Is.EqualTo(1896f)); Assert.That(plain.yMax, Is.EqualTo(1056f));
            Assert.That(band.yMin, Is.EqualTo(56f));
            Assert.That(band.xMin, Is.EqualTo(24f)); Assert.That(band.xMax, Is.EqualTo(1896f)); Assert.That(band.yMax, Is.EqualTo(1056f));
            Vector2 up = EdgePlacement.Place(Screen, new Vector2(0, 1), band, EdgePlacement.KeepOut(KillerCluster), false, out _);
            Near(up, 960, 56, "a chevron straight up sits under the band");
        }

        // ---- step 4: the ray from the centre ----------------------------------------------------------------------------------

        [Test]
        public void TheChevronSitsWhereTheRayFromTheScreenCentreMeetsTheSafeRectangleAndPointsAlongTheRay()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false);
            Rect keep = EdgePlacement.KeepOut(KillerCluster);
            // Up-right at 45 degrees on a 16:9 screen reaches the top edge first (516 up vs 936 right).
            Vector2 d = new Vector2(1, 1).normalized;
            Vector2 p = EdgePlacement.Place(Screen, d, safe, keep, false, out Vector2 pointing);
            Near(p, 960 + 516, 24, "top edge");
            Assert.That(pointing.x, Is.EqualTo(d.x).Within(1e-5f));
            Assert.That(pointing.y, Is.EqualTo(-d.y).Within(1e-5f), "screen y is down");
            // Flat right reaches the right edge first.
            Vector2 flat = EdgePlacement.Place(Screen, new Vector2(1, .1f).normalized, safe, keep, false, out _);
            Assert.That(flat.x, Is.EqualTo(1896f).Within(.05f));
            Assert.That(flat.y, Is.EqualTo(540 - 93.6f).Within(.1f), "10 percent of the horizontal run, going up");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void EveryDirectionLandsInsideTheSafeRectangleAndNeverStrictlyInsideTheKeepOut(bool nearestSlide, bool band)
        {
            Rect safe = EdgePlacement.SafeRect(Screen, band);
            foreach (Rect cluster in new[] { KillerCluster, SurvivorCluster, new Rect(24, 1080 - 24 - 472, 320, 472) })
            {
                Rect keep = EdgePlacement.KeepOut(cluster);
                for (int degrees = 0; degrees < 360; degrees += 2)
                {
                    Vector2 d = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
                    Vector2 p = EdgePlacement.Place(Screen, d, safe, keep, nearestSlide, out Vector2 pointing);
                    string what = "direction " + degrees + " cluster " + cluster;
                    Assert.That(p.x, Is.InRange(safe.xMin - .01f, safe.xMax + .01f), what);
                    Assert.That(p.y, Is.InRange(safe.yMin - .01f, safe.yMax + .01f), what);
                    Assert.That(StrictlyInside(keep, p), Is.False, "a chevron never sits in the cluster, " + what);
                    Assert.That(pointing.magnitude, Is.EqualTo(1f).Within(1e-4f), what);
                    Assert.That(pointing.x, Is.EqualTo(d.x).Within(1e-4f), "pointing follows the direction, " + what);
                    Assert.That(pointing.y, Is.EqualTo(-d.y).Within(1e-4f), what);
                }
            }
        }

        // ---- step 5: the keep-out ----------------------------------------------------------------------------------------------

        [Test]
        public void TheKeepOutIsTheClusterExpandedByEightOnEverySide()
        {
            Rect k = EdgePlacement.KeepOut(KillerCluster);
            Assert.That(k.xMin, Is.EqualTo(KillerCluster.xMin - 8));
            Assert.That(k.yMin, Is.EqualTo(KillerCluster.yMin - 8));
            Assert.That(k.xMax, Is.EqualTo(KillerCluster.xMax + 8));
            Assert.That(k.yMax, Is.EqualTo(KillerCluster.yMax + 8));
            Assert.That(k.width, Is.EqualTo(KillerCluster.width + 16));
            Assert.That(k.height, Is.EqualTo(KillerCluster.height + 16));
        }

        [Test]
        public void OnTheLeftEdgeAChevronInTheKillerKeepOutSlidesUpUntilItClearsIt()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false), keep = EdgePlacement.KeepOut(KillerCluster);
            Vector2 d = new Vector2(-1, -.4f).normalized; // left edge, 914 u down: inside the keep-out (896..1064)
            Vector2 p = EdgePlacement.Place(Screen, d, safe, keep, false, out _);
            Assert.That(p.x, Is.EqualTo(24f).Within(.05f), "stays on the left edge");
            Assert.That(p.y, Is.LessThanOrEqualTo(keep.yMin + .05f), "moved up, off the cluster");
            Assert.That(p.y, Is.GreaterThan(keep.yMin - 2f), "and only as far as needed");
            Assert.That(StrictlyInside(keep, p), Is.False);
        }

        [Test]
        public void OnTheBottomEdgeAChevronInTheKillerKeepOutSlidesRightUntilItClearsIt()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false), keep = EdgePlacement.KeepOut(KillerCluster);
            Vector2 d = new Vector2(-760, -516).normalized; // bottom edge at x = 200
            Vector2 p = EdgePlacement.Place(Screen, d, safe, keep, false, out _);
            Near(p, keep.xMax, 1056, "bottom edge, right of the keep-out");
            Assert.That(p.x, Is.GreaterThan(KillerCluster.xMax), "clear of the cluster itself with the 8 u margin");
        }

        [Test]
        public void ForASurvivorTheBottomEdgeSlidesToWhicheverSideOfTheKeepOutIsNearer()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false), keep = EdgePlacement.KeepOut(SurvivorCluster);
            Assert.That(keep.xMin, Is.EqualTo(616f)); Assert.That(keep.xMax, Is.EqualTo(1304f));
            Vector2 slightlyLeft = EdgePlacement.Place(Screen, new Vector2(-.1f, -1).normalized, safe, keep, true, out _);
            Near(slightlyLeft, 616, 1056, "left of centre goes to the left side");
            Vector2 slightlyRight = EdgePlacement.Place(Screen, new Vector2(.12f, -1).normalized, safe, keep, true, out _);
            Near(slightlyRight, 1304, 1056, "right of centre goes to the right side");
        }

        [Test]
        public void AChevronAlreadyOutsideTheKeepOutIsLeftWhereTheRayPutIt()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false), keep = EdgePlacement.KeepOut(KillerCluster);
            Vector2 d = new Vector2(-.2f, -1).normalized; // bottom edge near x = 960 - 103
            Vector2 p = EdgePlacement.Place(Screen, d, safe, keep, false, out _);
            Near(p, 960 - 516 * .2f, 1056, "unchanged");
        }

        [Test]
        public void TheTagSitsOnTheScreenCentreSideOfItsChevron()
        {
            Vector2 chevron = new Vector2(1896, 540);
            Vector2 tag = EdgePlacement.TagAnchor(chevron, Screen, 30);
            Near(tag, 1866, 540, "tag moves toward the centre");
            Vector2 corner = EdgePlacement.TagAnchor(new Vector2(24, 1056), Screen, 30);
            Assert.That((corner - new Vector2(24, 1056)).magnitude, Is.EqualTo(30f).Within(.01f));
            Assert.That(corner.x, Is.GreaterThan(24f));
            Assert.That(corner.y, Is.LessThan(1056f));
        }

        // ---- step 6: on-screen markers inside the keep-out ------------------------------------------------------------------

        [Test]
        public void AnOnScreenMarkerInsideTheKeepOutMovesToTheNearestOutsideEdgeThatStaysInTheSafeRectangle()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false), keep = EdgePlacement.KeepOut(KillerCluster);
            // (100, 1000): the top edge (896) is 104 away; bottom (1064) and left (16) fall outside the safe rectangle; right is 252 away.
            Near(EdgePlacement.ClampOutside(new Vector2(100, 1000), keep, safe), 100, 896, "nearest usable edge is the top");
            // (340, 1050): the right edge (352) is 12 away.
            Near(EdgePlacement.ClampOutside(new Vector2(340, 1050), keep, safe), 352, 1050, "nearest usable edge is the right");
            // Outside: untouched.
            Near(EdgePlacement.ClampOutside(new Vector2(960, 540), keep, safe), 960, 540, "outside the keep-out");
            Near(EdgePlacement.ClampOutside(new Vector2(353, 1000), keep, safe), 353, 1000, "just outside");
        }

        [Test]
        public void NoMarkerEndsStrictlyInsideTheKeepOutAndEveryMarkerStaysInTheSafeRectangle()
        {
            Rect safe = EdgePlacement.SafeRect(Screen, false), keep = EdgePlacement.KeepOut(KillerCluster);
            for (float x = 0; x <= 1920; x += 40)
                for (float y = 0; y <= 1080; y += 40)
                {
                    var point = new Vector2(x, y);
                    if (!safe.Contains(point)) continue; // the marker rule is about points that were on screen
                    Vector2 p = EdgePlacement.ClampOutside(point, keep, safe);
                    Assert.That(StrictlyInside(keep, p), Is.False, "from " + point);
                    Assert.That(p.x, Is.InRange(safe.xMin - .01f, safe.xMax + .01f), "from " + point);
                    Assert.That(p.y, Is.InRange(safe.yMin - .01f, safe.yMax + .01f), "from " + point);
                }
        }

        // ---- nearest six with voiced first --------------------------------------------------------------------------------

        // The Killer at (30, 30) looks along +z (yaw 0). Nine Survivors stand behind it, 2.5 m apart along -z, so all are off-screen.
        private static readonly ViewPose LookingNorth = new ViewPose(new Vector3(30, KillerViewRig.EyeHeight, 30), 0, 0, KillerViewRig.VerticalFov, 16f / 9);

        private static KillerScene SceneWithNineBehind(out List<BlipView> views)
        {
            var scene = new KillerScene(new System.Random(7), null);
            var state = new S2C_WorldState { Tick = 1 };
            state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 30, Z = 30 });
            for (int k = 0; k < 9; k++)
                state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, X = 30 + (k % 2) * .5f, Z = 27.5f - 2.5f * k });
            scene.Accept(state, 0);
            views = new List<BlipView>(scene.Frame(LookingNorth, .05, null));
            return scene;
        }

        [Test]
        public void OnlyTheSixNearestOffScreenInstancesGetAnEdgeIndicatorWhenNoneIsVoiced()
        {
            SceneWithNineBehind(out List<BlipView> views);
            Assert.That(views.Count, Is.EqualTo(9));
            foreach (BlipView v in views) Assert.That(v.Class, Is.EqualTo(BlipClass.OffScreen));
            var distances = new List<float>();
            foreach (BlipView v in views) distances.Add(v.Distance);
            distances.Sort();
            float sixth = distances[5];
            int shown = 0;
            foreach (BlipView v in views)
            {
                Assert.That(v.Edge, Is.EqualTo(v.Distance <= sixth + 1e-4f), "distance " + v.Distance);
                if (v.Edge) shown++;
            }
            Assert.That(shown, Is.EqualTo(6));
        }

        [Test]
        public void VoicedInstancesAreChosenFirstThenTheNearestFillTheRemainingEdgeSlots()
        {
            KillerScene scene = SceneWithNineBehind(out List<BlipView> first);
            first.Sort((a, b) => b.Distance.CompareTo(a.Distance));
            var voiced = new HashSet<int> { first[0].Handle, first[1].Handle, first[2].Handle, first[3].Handle }; // the four farthest
            IReadOnlyList<BlipView> views = scene.Frame(LookingNorth, .06, h => voiced.Contains(h));
            var shown = new HashSet<int>();
            foreach (BlipView v in views) if (v.Edge) shown.Add(v.Handle);
            Assert.That(shown.Count, Is.EqualTo(6));
            foreach (int h in voiced) Assert.That(shown, Does.Contain(h), "a voiced instance always gets its indicator");
            // The two remaining slots go to the two nearest of the others.
            var rest = new List<BlipView>();
            foreach (BlipView v in views) if (!voiced.Contains(v.Handle)) rest.Add(v);
            rest.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            Assert.That(rest[0].Edge, Is.True); Assert.That(rest[1].Edge, Is.True);
            for (int i = 2; i < rest.Count; i++) Assert.That(rest[i].Edge, Is.False);
        }

        [Test]
        public void InViewAndOccludedInstancesNeverTakeAnEdgeSlotAndFewerThanSixAllShow()
        {
            var scene = new KillerScene(new System.Random(9), null);
            var pose = new ViewPose(new Vector3(10, KillerViewRig.EyeHeight, 23), 90, 0, KillerViewRig.VerticalFov, 16f / 9);
            var state = new S2C_WorldState { Tick = 1 };
            state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 10, Z = 23 });
            state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, X = 15, Z = 21 });   // in view
            state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, X = 26, Z = 22 });   // behind the wall
            state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, X = 4, Z = 24 });    // behind the camera
            state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, X = 3, Z = 27 });    // behind the camera
            scene.Accept(state, 0);
            IReadOnlyList<BlipView> views = scene.Frame(pose, .05, null);
            int edges = 0;
            foreach (BlipView v in views)
            {
                if (v.Class == BlipClass.OffScreen) { Assert.That(v.Edge, Is.True); edges++; }
                else Assert.That(v.Edge, Is.False, v.Class.ToString());
            }
            Assert.That(edges, Is.EqualTo(2));
            Assert.That(scene.Count(BlipClass.InView), Is.EqualTo(1));
            Assert.That(scene.Count(BlipClass.Occluded), Is.EqualTo(1));
        }
    }
}
