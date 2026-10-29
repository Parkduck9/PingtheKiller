using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // HUD_SPEC 4.10 (Survivor orbit) and 6.0 (Killer first person): the numbers, the boom, the facing and the projection.
    public sealed class CameraRigTests
    {
        [Test]
        public void TheRigNumbersAreTheSpecStartingValues()
        {
            Assert.That(SurvivorOrbitRig.TargetHeight, Is.EqualTo(.7f));
            Assert.That(SurvivorOrbitRig.Boom, Is.EqualTo(4f));
            Assert.That(SurvivorOrbitRig.MinPitch, Is.EqualTo(-10f));
            Assert.That(SurvivorOrbitRig.MaxPitch, Is.EqualTo(60f));
            Assert.That(SurvivorOrbitRig.DefaultPitch, Is.EqualTo(20f));
            Assert.That(SurvivorOrbitRig.VerticalFov, Is.EqualTo(50f));
            Assert.That(SurvivorOrbitRig.ProbeRadius, Is.EqualTo(.2f));
            Assert.That(SurvivorOrbitRig.EaseSeconds, Is.EqualTo(.15f));
            Assert.That(SurvivorOrbitRig.DitherBelow, Is.EqualTo(1f));
            Assert.That(KillerViewRig.EyeHeight, Is.EqualTo(1.7f));
            Assert.That(KillerViewRig.MaxPitch, Is.EqualTo(70f));
            Assert.That(KillerViewRig.VerticalFov, Is.EqualTo(58.72f).Within(.01f), "90 degrees horizontal at 16:9");
        }

        [Test]
        public void SurvivorPitchIsClampedAndMouseUpLooksUp()
        {
            var rig = new SurvivorOrbitRig();
            Assert.That(rig.Pitch, Is.EqualTo(20f));
            rig.LookPitch(100);
            Assert.That(rig.Pitch, Is.EqualTo(-10f));
            rig.LookPitch(-500);
            Assert.That(rig.Pitch, Is.EqualTo(60f));
            var killer = new KillerViewRig();
            killer.LookPitch(500);
            Assert.That(killer.Pitch, Is.EqualTo(-70f));
            killer.LookPitch(-900);
            Assert.That(killer.Pitch, Is.EqualTo(70f));
        }

        [Test]
        public void TheBoomPointsBackAndUpAtTheDefaultPitch()
        {
            Vector3 back = SurvivorOrbitRig.Back(0, 20);
            Assert.That(back.z, Is.LessThan(0), "behind the target at yaw 0");
            Assert.That(back.y, Is.EqualTo(Mathf.Sin(20 * Mathf.Deg2Rad)).Within(1e-4f), "camera above the target");
            Assert.That(.7f + 4 * back.y, Is.EqualTo(2.07f).Within(.01f), "HUD_SPEC framing check: camera height 2.07 m");
        }

        [Test]
        public void TheBoomShortensAtOnceAndEasesOutOverOneHundredFiftyMilliseconds()
        {
            var boom = new BoomFollow(4, .15f);
            Assert.That(boom.Update(1.5f, .016f), Is.EqualTo(1.5f), "pull-in is immediate");
            float half = boom.Update(4, .075f);
            Assert.That(half, Is.GreaterThan(1.5f).And.LessThan(4f));
            Assert.That(half, Is.EqualTo(1.5f + 2.5f * .75f).Within(1e-3f), "ease-out: 75 % of the way at half time");
            Assert.That(boom.Update(4, .075f), Is.EqualTo(4f).Within(1e-4f), "back to full after 150 ms");
            Assert.That(boom.Update(9, .5f), Is.EqualTo(4f), "never longer than the boom");
            Assert.That(boom.Update(float.NaN, .1f), Is.EqualTo(4f));
        }

        [Test]
        public void ASphereCastStopsAtAWallExpandedByTheRadiusAndAtTheFloor()
        {
            // Wall x 18..20, z 14..32 (FixedMap). From x = 17 toward +x the 0.2 m sphere touches at x = 17.8.
            float hit = MapGeometry.SphereCast(SharedMap.Map, new Vector3(17, .7f, 23), Vector3.right, 4, .2f);
            Assert.That(hit, Is.EqualTo(.8f).Within(1e-4f));
            Assert.That(MapGeometry.SphereCast(SharedMap.Map, new Vector3(10, .7f, 23), Vector3.forward, 4, .2f), Is.EqualTo(4f), "clear");
            Assert.That(MapGeometry.SphereCast(SharedMap.Map, new Vector3(10, .7f, 23), Vector3.down, 4, .2f), Is.EqualTo(.5f).Within(1e-4f), "floor");
            // Above the 3 m walls the cast passes over them.
            Assert.That(MapGeometry.SphereCast(SharedMap.Map, new Vector3(17, 3.5f, 23), Vector3.right, 4, .2f), Is.EqualTo(4f));
        }

        [Test]
        public void TheOcclusionRayTreatsWallsAsFullHeight()
        {
            Assert.That(MapGeometry.SegmentBlocked(SharedMap.Map, 10, 23, 25, 23), Is.True, "through the wall at x 18..20");
            Assert.That(MapGeometry.SegmentBlocked(SharedMap.Map, 10, 23, 15, 23), Is.False);
            Assert.That(MapGeometry.SegmentBlocked(SharedMap.Map, 10, 40, 25, 40), Is.False, "past the wall's end (z 32)");
        }

        [Test]
        public void TheCharacterTurnsAtSevenHundredTwentyDegreesPerSecondAndStandingStillKeepsTheFacing()
        {
            var facing = new CharacterFacing();
            facing.Reset(0);
            Assert.That(facing.Update(Vector3.right, .05f), Is.EqualTo(36f).Within(1e-3f));
            Assert.That(facing.Update(Vector3.right, 1), Is.EqualTo(90f).Within(1e-3f));
            Assert.That(facing.Update(Vector3.zero, 1), Is.EqualTo(90f).Within(1e-3f), "orbiting or standing never turns it");
            // Hacking: no move, the character turns to the terminal.
            float yaw = facing.Update(Vector3.zero, 1, new Vector3(10, 0, 10), new Vector3(10, 0, 0));
            Assert.That(yaw, Is.EqualTo(180f).Within(1e-3f));
        }

        [Test]
        public void MoveInputCarriesTheCameraRelativeDirectionAndTheCharacterFacing()
        {
            var input = new MovementInput();
            Assert.That(input.TryCreate(true, 0, 1, 90, 30, MovementInput.Interval, out C2S_MoveInput packet), Is.True);
            Assert.That(packet.DirX, Is.EqualTo(1f).Within(1e-4f), "W at camera yaw 90 moves +x");
            Assert.That(packet.DirZ, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(packet.Yaw, Is.EqualTo(30f), "Yaw = the character facing, not the camera");
            var idle = new MovementInput();
            Assert.That(idle.TryCreate(false, 0, 0, 200, 30, 1, out _), Is.False, "orbiting while standing sends nothing");
        }

        [Test]
        public void TheViewPoseProjectsLikeTheCamera()
        {
            var pose = new ViewPose(new Vector3(10, 1.7f, 23), 90, 0, KillerViewRig.VerticalFov, 16f / 9);
            Assert.That(pose.InFrustum(new Vector3(20, 1.2f, 23)), Is.True, "straight ahead");
            Assert.That(pose.InFrustum(new Vector3(0, 1.2f, 23)), Is.False, "behind");
            Assert.That(pose.InFrustum(new Vector3(11, 1.2f, 30)), Is.False, "far to the left: outside the 90 degree FOV");
            Assert.That(pose.ToViewport(new Vector3(20, 1.7f, 23), out Vector2 centre), Is.True);
            Assert.That(centre.x, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(centre.y, Is.EqualTo(.5f).Within(1e-4f));
            Vector3 left = pose.ToCamera(new Vector3(10, 1.7f, 30));
            Assert.That(left.x, Is.LessThan(0), "+z is on the left when facing +x");
            // At the horizontal FOV edge (45 degrees) the viewport x is 1.
            pose.ToViewport(new Vector3(20, 1.7f, 13), out Vector2 edge);
            Assert.That(edge.x, Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
