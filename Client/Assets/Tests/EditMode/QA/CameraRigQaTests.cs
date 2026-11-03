using System;
using NUnit.Framework;
using PingTheKiller.Client.World;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test item 5. Written from HUD_SPEC 4.10 (Survivor third person) and 6.0 (Killer first person); the map
    // is the real FixedMap, so every expected number below is worked out by hand from the spec. The wall under test is the
    // west building of map v2 (Walls[4], x 12..24, z 12..36, formerly the 60 m map's x 18..20, z 14..32): the boom comes from its
    // east face into the 4 m alley, 2.5 m off the face as before; Walls[10] (x 56..66, z 10..20) stands in for "the wall's end".
    [Category("U4cQaA1")]
    public sealed class CameraRigQaTests
    {
        private static ServerCore.Map.Wall Block => SharedMap.Map.Walls[4];
        private static ServerCore.Map.Wall EndBlock => SharedMap.Map.Walls[10];
        // The Survivor's look target 2.5 m east of the east face of Block, level with its middle (formerly x 22.5, z 23).
        private static Vector3 AlleyTarget => new Vector3(Block.MaxX + 2.5f, .7f, (Block.MinZ + Block.MaxZ) * .5f);

        private static void WithCamera(Action<Camera> body)
        {
            var go = new GameObject("qa-camera", typeof(Camera));
            try { body(go.GetComponent<Camera>()); }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- Survivor orbit rig -----------------------------------------------------------------------------------------------

        [Test]
        public void TheSurvivorRigStartsAtPitchTwentyAndTheLookTargetIsSevenTenthsOfAMetreAboveTheCharacter()
        {
            var rig = new SurvivorOrbitRig();
            Assert.That(rig.Pitch, Is.EqualTo(20f));
            Vector3 target = SurvivorOrbitRig.Target(new Vector3(30, 0, 40));
            Assert.That(target.x, Is.EqualTo(30f)); Assert.That(target.y, Is.EqualTo(.7f)); Assert.That(target.z, Is.EqualTo(40f));
            Assert.That(SurvivorOrbitRig.Boom, Is.EqualTo(4f));
            Assert.That(SurvivorOrbitRig.VerticalFov, Is.EqualTo(50f));
            Assert.That(SurvivorOrbitRig.DitherBelow, Is.EqualTo(1f));
            Assert.That(SurvivorOrbitRig.EaseSeconds, Is.EqualTo(.15f).Within(1e-6f));
            Assert.That(SurvivorOrbitRig.ProbeRadius, Is.EqualTo(.2f).Within(1e-6f));
        }

        [Test]
        public void TheSurvivorPitchIsClampedToMinusTenAndSixtyAndResetsToTwenty()
        {
            var rig = new SurvivorOrbitRig();
            rig.LookPitch(10);
            Assert.That(rig.Pitch, Is.EqualTo(10f), "mouse up looks up: the pitch gets smaller");
            rig.LookPitch(1000);
            Assert.That(rig.Pitch, Is.EqualTo(-10f), "looking up stops at -10");
            rig.LookPitch(-1000);
            Assert.That(rig.Pitch, Is.EqualTo(60f), "looking down stops at +60");
            rig.Reset();
            Assert.That(rig.Pitch, Is.EqualTo(20f));
        }

        [Test]
        public void TheFramingCheckOfTheSpecHoldsAtTheDefaultPitchAndFullBoom()
        {
            // HUD_SPEC 4.10: camera height 2.07 m, horizontal distance 3.76 m, feet at ~67 % of the screen height from the top
            // (8.8 degrees below the centre), head at ~19 %.
            Vector3 ground = new Vector3(30, 0, 30);
            Vector3 eye = SurvivorOrbitRig.Target(ground) + SurvivorOrbitRig.Back(0, 20) * SurvivorOrbitRig.Boom;
            Assert.That(eye.y, Is.EqualTo(2.07f).Within(.01f));
            Assert.That(new Vector2(eye.x - ground.x, eye.z - ground.z).magnitude, Is.EqualTo(3.76f).Within(.01f));
            var pose = new ViewPose(eye, 0, 20, 50, 16f / 9);
            Assert.That(pose.ToViewport(ground, out Vector2 feet), Is.True);
            Assert.That(1 - feet.y, Is.EqualTo(.67f).Within(.02f), "feet, fraction of screen height from the top");
            Assert.That(pose.ToViewport(ground + Vector3.up * 1.8f, out Vector2 head), Is.True);
            Assert.That(1 - head.y, Is.EqualTo(.19f).Within(.02f), "head, fraction of screen height from the top");
            Assert.That(feet.x, Is.EqualTo(.5f).Within(.001f), "the character is centred horizontally");
        }

        [Test]
        public void TheBoomPointsBehindTheViewYawAndAboveForPositivePitch()
        {
            Vector3 back0 = SurvivorOrbitRig.Back(0, 20);
            Assert.That(back0.z, Is.EqualTo(-Mathf.Cos(20 * Mathf.Deg2Rad)).Within(1e-4f));
            Assert.That(back0.y, Is.EqualTo(Mathf.Sin(20 * Mathf.Deg2Rad)).Within(1e-4f));
            Vector3 back90 = SurvivorOrbitRig.Back(90, 0);
            Assert.That(back90.x, Is.EqualTo(-1f).Within(1e-4f), "camera yaw 90 looks +x, so the camera sits at -x of the character");
            Assert.That(back90.magnitude, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ASphereCastAgainstTheWallBoxStopsAtTheWallFaceExpandedByTheRadius()
        {
            // Target east of the wall (Block.MaxX = 24), boom pointing west: contact when the sphere centre reaches x = 24.2.
            float level = MapGeometry.SphereCast(SharedMap.Map, AlleyTarget, SurvivorOrbitRig.Back(90, 0), 4, .2f);
            Assert.That(level, Is.EqualTo(2.3f).Within(.005f));
            // At the default pitch the boom also rises, so the same face is reached 1/cos(20) farther along it.
            float pitched = MapGeometry.SphereCast(SharedMap.Map, AlleyTarget, SurvivorOrbitRig.Back(90, 20), 4, .2f);
            Assert.That(pitched, Is.EqualTo(2.3f / Mathf.Cos(20 * Mathf.Deg2Rad)).Within(.01f));
            // Open ground: the whole boom.
            Assert.That(MapGeometry.SphereCast(SharedMap.Map, new Vector3(8, .7f, 40), SurvivorOrbitRig.Back(90, 0), 4, .2f), Is.EqualTo(4f).Within(1e-4f));
            // Walls are boxes: a boom passing beside the wall's end (z beyond MaxZ + radius) is not shortened. EndBlock ends at
            // z = 20 and the alley beyond it is open for 4 m; the target stands 2.5 m east of its east face, 1 m past its end.
            Assert.That(MapGeometry.SphereCast(SharedMap.Map, new Vector3(EndBlock.MaxX + 2.5f, .7f, EndBlock.MaxZ + 1f), SurvivorOrbitRig.Back(90, 0), 4, .2f), Is.EqualTo(4f).Within(1e-4f));
            // And the same height level with that wall is shortened, so the open result above is the wall's end and not a missing wall.
            Assert.That(MapGeometry.SphereCast(SharedMap.Map, new Vector3(EndBlock.MaxX + 2.5f, .7f, EndBlock.MaxZ - 1f), SurvivorOrbitRig.Back(90, 0), 4, .2f), Is.EqualTo(2.3f).Within(.005f));
        }

        [Test]
        public void TheBoomShortensAtOnceAndEasesBackOutOverOneHundredFiftyMilliseconds()
        {
            var boom = new BoomFollow(4, .15f);
            Assert.That(boom.Length, Is.EqualTo(4f));
            Assert.That(boom.Update(2.3f, .016f), Is.EqualTo(2.3f).Within(1e-5f), "the first frame already shows the pulled-in boom");
            float previous = 2.3f;
            float atSeventy = 0, atHundred = 0;
            for (int step = 1; step <= 20; step++)
            {
                float length = boom.Update(4, .01f);
                Assert.That(length, Is.GreaterThanOrEqualTo(previous - 1e-5f), "never goes backwards while easing out, step " + step);
                Assert.That(length, Is.LessThanOrEqualTo(4f + 1e-5f), "never longer than the boom");
                if (step == 7) atSeventy = length;
                if (step == 10) atHundred = length;
                if (step <= 14) Assert.That(length, Is.LessThan(4f - 1e-4f), "not back at full length before 150 ms, step " + step);
                previous = length;
            }
            Assert.That(atSeventy, Is.GreaterThan(2.3f + .3f).And.LessThan(4f - .1f), "partway at 70 ms");
            Assert.That(atHundred, Is.GreaterThan(atSeventy));
            Assert.That(boom.Length, Is.EqualTo(4f).Within(1e-4f), "full length by 200 ms");
        }

        [Test]
        public void AWallThatReappearsWhileTheBoomIsEasingOutShortensItAtOnceAgain()
        {
            var boom = new BoomFollow(4, .15f);
            boom.Update(1.5f, .016f);
            for (int i = 0; i < 6; i++) boom.Update(4, .01f);
            Assert.That(boom.Length, Is.GreaterThan(1.5f).And.LessThan(4f));
            Assert.That(boom.Update(1.0f, .01f), Is.EqualTo(1.0f).Within(1e-5f), "pull-in is immediate even mid-ease");
            float restart = boom.Update(4, .01f);
            Assert.That(restart, Is.GreaterThan(1.0f).And.LessThan(2f), "and the ease-out restarts from the shortened length, not from 4");
        }

        [Test]
        public void TheRigPlacesTheCameraAtTheClearBoomAndShortensItAtOnceAgainstTheWall()
        {
            WithCamera(cam =>
            {
                var rig = new SurvivorOrbitRig();
                // Open ground: the camera sits at the full boom from the look target.
                ViewPose open = rig.Place(cam, 90, new Vector3(8, 0, 40), 1f / 60);
                Assert.That(rig.BoomLength, Is.EqualTo(4f).Within(1e-4f));
                Assert.That((open.Eye - SurvivorOrbitRig.Target(new Vector3(8, 0, 40))).magnitude, Is.EqualTo(4f).Within(1e-3f));
                Assert.That(cam.transform.position.x, Is.EqualTo(open.Eye.x).Within(1e-4f), "the Unity camera is put exactly where the pose says");
                Assert.That(cam.fieldOfView, Is.EqualTo(50f).Within(1e-3f));
                Assert.That(rig.Dither, Is.False);

                // The same rig stepped next to the wall: the boom is pulled in on the very next Place.
                ViewPose pulled = rig.Place(cam, 90, new Vector3(AlleyTarget.x, 0, AlleyTarget.z), 1f / 60);
                Assert.That(rig.BoomLength, Is.EqualTo(2.3f / Mathf.Cos(20 * Mathf.Deg2Rad)).Within(.01f));
                Assert.That(pulled.Eye.x, Is.GreaterThanOrEqualTo(Block.MaxX + .2f - 1e-3f), "the camera never ends up inside the 0.2 m skin of the wall");
            });
        }

        [Test]
        public void TheOwnCharacterIsDitheredExactlyWhileTheBoomIsShorterThanOneMetre()
        {
            // At the default pitch the boom meets the wall face at x = Block.MaxX + 0.2 after (gx - that) / cos(20) metres.
            float cos = Mathf.Cos(20 * Mathf.Deg2Rad);
            foreach (var c in new[] { (length: .5f, dither: true), (length: .95f, dither: true), (length: 1.05f, dither: false), (length: 2f, dither: false) })
                WithCamera(cam =>
                {
                    var rig = new SurvivorOrbitRig(); // a fresh rig: no ease-out in progress
                    rig.Place(cam, 90, new Vector3(Block.MaxX + .2f + c.length * cos, 0, AlleyTarget.z), 1f / 60);
                    Assert.That(rig.BoomLength, Is.EqualTo(c.length).Within(.01f));
                    Assert.That(rig.Dither, Is.EqualTo(c.dither), "boom " + c.length + " m");
                });
        }

        // ---- the character's facing ------------------------------------------------------------------------------------------

        private static Vector3 Heading(float yawDegrees) => new Vector3(Mathf.Sin(yawDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(yawDegrees * Mathf.Deg2Rad));

        [Test]
        public void TheCharacterTurnsAtSevenHundredTwentyDegreesPerSecond()
        {
            var facing = new CharacterFacing();
            facing.Reset(0);
            Assert.That(facing.Update(Heading(90), .0625f), Is.EqualTo(45f).Within(.01f), "62.5 ms is 45 degrees");
            Assert.That(facing.Update(Heading(90), .0625f), Is.EqualTo(90f).Within(.01f), "and it stops on the target");
            facing.Reset(0);
            // 179 degrees, not 180: exactly opposite has no shorter way round.
            Assert.That(facing.Update(Heading(179), .2f), Is.EqualTo(144f).Within(.01f));
            Assert.That(facing.Update(Heading(179), .05f), Is.EqualTo(179f).Within(.01f), "a half turn takes about 250 ms");
        }

        [Test]
        public void TheCharacterTurnsTheShortWayAcrossTheWrap()
        {
            var facing = new CharacterFacing();
            facing.Reset(10);
            float yaw = facing.Update(Heading(350), .01f);
            Assert.That(yaw, Is.EqualTo(2.8f).Within(.01f), "10 -> 350 is 20 degrees through 0, so 7.2 degrees in 10 ms");
        }

        [Test]
        public void StandingStillNeverTurnsTheCharacterNoMatterHowLongOrWhatTheCameraDoes()
        {
            var facing = new CharacterFacing();
            facing.Reset(0);
            facing.Update(Heading(90), 1);
            Assert.That(facing.Yaw, Is.EqualTo(90f).Within(.01f));
            for (int i = 0; i < 600; i++) facing.Update(Vector3.zero, i % 7 == 0 ? .5f : .016f);
            Assert.That(facing.Yaw, Is.EqualTo(90f).Within(.001f), "orbiting the camera is invisible to CharacterFacing: only a move vector turns it");
        }

        [Test]
        public void WhileHackingAStandingCharacterFacesTheTerminalButAMoveStillWins()
        {
            var facing = new CharacterFacing();
            facing.Reset(0);
            float yaw = facing.Update(Vector3.zero, 1, new Vector3(10, 0, 10), new Vector3(10, 0, 0));
            Assert.That(yaw, Is.EqualTo(180f).Within(.01f), "the terminal is at -z");
            yaw = facing.Update(Heading(90), 1, new Vector3(10, 0, 10), new Vector3(10, 0, 0));
            Assert.That(yaw, Is.EqualTo(90f).Within(.01f), "a move vector decides the facing");
        }

        [Test]
        public void ABadFrameTimeNeverTurnsOrBreaksTheFacing()
        {
            var facing = new CharacterFacing();
            facing.Reset(33);
            foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                Assert.That(facing.Update(Heading(200), dt), Is.EqualTo(33f).Within(1e-4f), "dt " + dt);
        }

        // ---- Killer first-person rig --------------------------------------------------------------------------------------------

        [Test]
        public void TheKillerEyeIsOnePointSevenMetresAboveTheGroundAndTheCameraIsPlacedThere()
        {
            Assert.That(KillerViewRig.EyeHeight, Is.EqualTo(1.7f));
            WithCamera(cam =>
            {
                var rig = new KillerViewRig();
                ViewPose pose = rig.Place(cam, 33, new Vector3(12, 0, 15));
                Assert.That(pose.Eye.x, Is.EqualTo(12f)); Assert.That(pose.Eye.y, Is.EqualTo(1.7f)); Assert.That(pose.Eye.z, Is.EqualTo(15f));
                Assert.That(cam.transform.position.y, Is.EqualTo(1.7f).Within(1e-4f));
                Assert.That(pose.Yaw, Is.EqualTo(33f));
                Assert.That(cam.fieldOfView, Is.EqualTo(KillerViewRig.VerticalFov).Within(1e-3f));
            });
        }

        [Test]
        public void TheKillerPitchIsClampedToPlusAndMinusSeventy()
        {
            var rig = new KillerViewRig();
            Assert.That(rig.Pitch, Is.EqualTo(0f));
            rig.LookPitch(10); rig.LookPitch(10); rig.LookPitch(10);
            Assert.That(rig.Pitch, Is.EqualTo(-30f), "mouse up: the pitch goes negative (looking up)");
            rig.LookPitch(5000);
            Assert.That(rig.Pitch, Is.EqualTo(-70f));
            rig.LookPitch(-5000);
            Assert.That(rig.Pitch, Is.EqualTo(70f));
            rig.Reset();
            Assert.That(rig.Pitch, Is.EqualTo(0f));
        }

        [Test]
        public void NinetyDegreesHorizontalAtSixteenByNineIsAboutFiftyEightPointSevenVertical()
        {
            Assert.That(KillerViewRig.VerticalFov, Is.EqualTo(58.7f).Within(.05f));
            Assert.That(ViewPose.VerticalFromHorizontal(90, 16f / 9), Is.EqualTo(KillerViewRig.VerticalFov).Within(1e-3f));
            // Round trip: a camera with that vertical FOV at 16:9 sees exactly 45 degrees either side of its axis.
            var pose = new ViewPose(new Vector3(10, 1.7f, 23), 90, 0, KillerViewRig.VerticalFov, 16f / 9);
            Assert.That(pose.ToViewport(new Vector3(20, 1.7f, 13), out Vector2 edgeRight), Is.True);
            Assert.That(edgeRight.x, Is.EqualTo(1f).Within(.002f), "45 degrees to the right (-z when facing +x) is the right screen edge");
            Assert.That(pose.ToViewport(new Vector3(20, 1.7f, 33), out Vector2 edgeLeft), Is.True);
            Assert.That(edgeLeft.x, Is.EqualTo(0f).Within(.002f));
            Assert.That(pose.InFrustum(new Vector3(20, 1.7f, 12.5f)), Is.False, "just past 45 degrees is outside");
            Assert.That(ViewPose.VerticalFromHorizontal(90, 4f / 3), Is.EqualTo(73.74f).Within(.05f), "narrower screens get a taller view (Hor+)");
        }

        [Test]
        public void AWiderScreenKeepsTheVerticalFovAndSeesMoreToTheSides()
        {
            var wide = new ViewPose(new Vector3(10, 1.7f, 23), 90, 0, KillerViewRig.VerticalFov, 21f / 9);
            Assert.That(wide.ToViewport(new Vector3(20, 1.7f, 13), out Vector2 at45), Is.True);
            Assert.That(at45.x, Is.LessThan(.95f), "the 45 degree point is no longer at the edge at 21:9");
            Assert.That(wide.InFrustum(new Vector3(20, 1.7f, 12)), Is.True, "more than 45 degrees is visible");
            Assert.That(wide.ToViewport(new Vector3(20, 1.7f + 5.8f, 23), out Vector2 up), Is.True);
            Assert.That(up.y, Is.EqualTo(.5f + .5f * (5.8f / 10f) / Mathf.Tan(KillerViewRig.VerticalFov * .5f * Mathf.Deg2Rad)).Within(.002f), "the vertical extent is unchanged");
        }
    }
}
