using NUnit.Framework;
using PingTheKiller.Client.Killer;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // HUD_SPEC 6.2 edge-indicator placement, steps 1-6, on the 1600x900 canvas (720p).
    public sealed class EdgePlacementTests
    {
        private static readonly Vector2 Screen = new Vector2(1600, 900);
        private static readonly Rect Safe = EdgePlacement.SafeRect(Screen, false);
        // Killer cluster in normal play: 320 x 152 u, bottom-left, margin 24.
        private static readonly Rect KillerKeepOut = EdgePlacement.KeepOut(new Rect(24, 900 - 24 - 152, 320, 152));

        private static Vector2 Place(Vector2 directionUp, Rect keepOut, bool nearest = false)
            => EdgePlacement.Place(Screen, directionUp.normalized, Safe, keepOut, nearest, out _);

        [Test]
        public void TheSafeRectangleIsInsetTwentyFourAndFiftySixUnderTheBand()
        {
            Assert.That(Safe, Is.EqualTo(Rect.MinMaxRect(24, 24, 1576, 876)));
            Assert.That(EdgePlacement.SafeRect(Screen, true).yMin, Is.EqualTo(56f));
            Assert.That(KillerKeepOut, Is.EqualTo(Rect.MinMaxRect(16, 716, 352, 884)));
        }

        [Test]
        public void TheDirectionKeepsTheSignOfCameraSpaceXAndYEvenBehindTheCamera()
        {
            Assert.That(EdgePlacement.Direction(new Vector3(.5f, 0, -1)), Is.EqualTo(new Vector2(1, 0)));
            Assert.That(EdgePlacement.Direction(new Vector3(-2, 0, -5)), Is.EqualTo(new Vector2(-1, 0)));
            Assert.That(EdgePlacement.Direction(new Vector3(0, 0, -1)), Is.EqualTo(new Vector2(0, -1)), "straight behind: bottom edge");
            Assert.That(EdgePlacement.Direction(new Vector3(0, 3, 1)), Is.EqualTo(new Vector2(0, 1)));
        }

        [Test]
        public void TheChevronSitsWhereTheRayFromTheCentreMeetsTheSafeRectangle()
        {
            Vector2 right = EdgePlacement.Place(Screen, new Vector2(1, 0), Safe, KillerKeepOut, false, out Vector2 pointing);
            Assert.That(right, Is.EqualTo(new Vector2(1576, 450)));
            Assert.That(pointing, Is.EqualTo(new Vector2(1, 0)));
            Vector2 up = EdgePlacement.Place(Screen, new Vector2(0, 1), Safe, KillerKeepOut, false, out pointing);
            Assert.That(up, Is.EqualTo(new Vector2(800, 24)));
            Assert.That(pointing.y, Is.EqualTo(-1f), "screen y points down");
            Vector2 diagonal = Place(new Vector2(1, 1), KillerKeepOut);
            Assert.That(diagonal.y, Is.EqualTo(24f).Within(1e-3f));
            Assert.That(diagonal.x, Is.EqualTo(800 + 426f).Within(1e-2f));
        }

        [Test]
        public void OnTheLeftEdgeAChevronInTheKillerKeepOutMovesUp()
        {
            Vector2 p = Place(new Vector2(-.9f, -.436f), KillerKeepOut);
            Assert.That(p.x, Is.EqualTo(24f).Within(1e-3f));
            Assert.That(p.y, Is.EqualTo(716f).Within(1e-3f));
        }

        [Test]
        public void OnTheBottomEdgeAChevronInTheKillerKeepOutMovesRight()
        {
            Vector2 p = Place(new Vector2(-.73f, -.683f), KillerKeepOut);
            Assert.That(p.y, Is.EqualTo(876f).Within(1e-3f));
            Assert.That(p.x, Is.EqualTo(352f).Within(1e-3f));
            Vector2 corner = Place(new Vector2(-776, -426), KillerKeepOut);
            Assert.That(corner, Is.EqualTo(new Vector2(352, 876)), "the bottom-left corner slides right along the bottom edge");
        }

        [Test]
        public void OnTheBottomEdgeTheSurvivorKeepOutSlidesToTheNearerSide()
        {
            Rect survivor = EdgePlacement.KeepOut(new Rect(464, 900 - 24 - 72, 672, 72));
            Vector2 left = Place(new Vector2(-.05f, -1), survivor, true);
            Assert.That(left, Is.EqualTo(new Vector2(456, 876)));
            Vector2 right = Place(new Vector2(.05f, -1), survivor, true);
            Assert.That(right, Is.EqualTo(new Vector2(1144, 876)));
        }

        [Test]
        public void AChevronOutsideTheKeepOutIsNotMoved()
        {
            Vector2 p = Place(new Vector2(-.3f, -.954f), KillerKeepOut);
            Assert.That(p.y, Is.EqualTo(876f).Within(1e-3f));
            Assert.That(p.x, Is.GreaterThan(352f));
        }

        [Test]
        public void AnOnScreenMarkerInsideTheKeepOutClampsToTheNearestEdgeThatStaysInTheSafeRectangle()
        {
            Assert.That(EdgePlacement.ClampOutside(new Vector2(100, 800), KillerKeepOut, Safe), Is.EqualTo(new Vector2(100, 716)));
            Assert.That(EdgePlacement.ClampOutside(new Vector2(340, 870), KillerKeepOut, Safe), Is.EqualTo(new Vector2(352, 870)));
            Assert.That(EdgePlacement.ClampOutside(new Vector2(800, 450), KillerKeepOut, Safe), Is.EqualTo(new Vector2(800, 450)), "outside: unchanged");
        }

        [Test]
        public void TagsSitOnTheScreenCentreSideOfTheirChevron()
        {
            Vector2 tag = EdgePlacement.TagAnchor(new Vector2(1576, 450), Screen, 30);
            Assert.That(tag, Is.EqualTo(new Vector2(1546, 450)));
        }
    }
}
