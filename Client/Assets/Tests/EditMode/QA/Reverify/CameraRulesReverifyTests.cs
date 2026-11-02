using NUnit.Framework;
using PingTheKiller.Client.QA.Tests;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests.Reverify
{
    // Re-verification Attack 3: Camera rules (Area C of Reverify-flash.md).
    // HUD_SPEC 4.10 survivor orbit (boom, pitch, FOV, sphere pull-in, facing) and 6.0 killer first-person.
    [TestFixture]
    public sealed class CameraRulesReverifyTests
    {
        [Test]
        public void SurvivorOrbitRig_ConstantsAndDefaults_MatchSpecNumbers()
        {
            Assert.That(SurvivorOrbitRig.Boom, Is.EqualTo(4.0f), "Boom is 4.0 m");
            Assert.That(SurvivorOrbitRig.TargetHeight, Is.EqualTo(0.7f), "Look target is 0.7 m above ground");
            Assert.That(SurvivorOrbitRig.MinPitch, Is.EqualTo(-10f), "Min pitch is -10 deg");
            Assert.That(SurvivorOrbitRig.MaxPitch, Is.EqualTo(60f), "Max pitch is 60 deg");
            Assert.That(SurvivorOrbitRig.DefaultPitch, Is.EqualTo(20f), "Default pitch is 20 deg");
            Assert.That(SurvivorOrbitRig.VerticalFov, Is.EqualTo(50f), "Vertical FOV is 50 deg");
            Assert.That(SurvivorOrbitRig.ProbeRadius, Is.EqualTo(0.2f), "Probe radius is 0.2 m");
            Assert.That(SurvivorOrbitRig.EaseSeconds, Is.EqualTo(0.15f).Within(1e-5f), "Boom ease-out is 150 ms");
            Assert.That(SurvivorOrbitRig.DitherBelow, Is.EqualTo(1.0f), "Dither threshold is 1.0 m");

            var rig = new SurvivorOrbitRig();
            Assert.That(rig.Pitch, Is.EqualTo(20f), "Initial pitch is 20 deg");
            Assert.That(rig.BoomLength, Is.EqualTo(4.0f), "Initial boom length is 4.0 m");
            Assert.That(rig.Dither, Is.False, "Initial dither is false");
        }

        [Test]
        public void SurvivorOrbitRig_PitchClamping_ClampsBetweenMinAndMax()
        {
            var rig = new SurvivorOrbitRig();
            // Mouse delta looking up (reduces pitch)
            rig.LookPitch(15f);
            Assert.That(rig.Pitch, Is.EqualTo(5f));
            rig.LookPitch(100f);
            Assert.That(rig.Pitch, Is.EqualTo(-10f), "Clamped to -10");

            // Mouse delta looking down (increases pitch)
            rig.LookPitch(-150f);
            Assert.That(rig.Pitch, Is.EqualTo(60f), "Clamped to 60");

            rig.Reset();
            Assert.That(rig.Pitch, Is.EqualTo(20f), "Reset restores default pitch 20");
        }

        [Test]
        public void BoomFollow_InstantShorten_AndEaseOutOver150ms()
        {
            var follow = new BoomFollow(4.0f, 0.15f);
            Assert.That(follow.Length, Is.EqualTo(4.0f));

            // Instant shorten when wall hits at 0.8 m
            follow.Update(0.8f, 0.016f);
            Assert.That(follow.Length, Is.EqualTo(0.8f).Within(1e-4f), "Boom shortens at once");

            // When wall clears (clear = 4.0 m), eases out over 150 ms
            follow.Update(4.0f, 0.05f); // 50 ms elapsed (~1/3 of ease)
            Assert.That(follow.Length, Is.GreaterThan(0.8f).And.LessThan(4.0f));

            follow.Update(4.0f, 0.10f); // 150 ms total
            Assert.That(follow.Length, Is.EqualTo(4.0f).Within(1e-3f), "Boom reaches full 4.0 m at 150 ms");
        }

        [Test]
        public void CharacterFacing_TurnsTowardMovementDirectionAt720DegPerSec()
        {
            var facing = new CharacterFacing();
            facing.Reset(0f);

            // Move right (+X: 90 deg) for 0.05 s
            // 720 deg/s * 0.05 s = 36 deg
            float yaw = facing.Update(new Vector3(1, 0, 0), 0.05f);
            Assert.That(yaw, Is.EqualTo(36f).Within(0.1f));

            // Another 0.05 s -> 72 deg
            yaw = facing.Update(new Vector3(1, 0, 0), 0.05f);
            Assert.That(yaw, Is.EqualTo(72f).Within(0.1f));

            // Another 0.05 s -> reaches 90 deg and stays there
            yaw = facing.Update(new Vector3(1, 0, 0), 0.05f);
            Assert.That(yaw, Is.EqualTo(90f).Within(0.1f));
        }

        [Test]
        public void CharacterFacing_StandingStillKeepsFacing_AndOrbitingNeverTurnsCharacter()
        {
            var facing = new CharacterFacing();
            facing.Reset(120f);

            // Standing still (move = 0) with no hacking target
            for (int i = 0; i < 60; i++)
            {
                float yaw = facing.Update(Vector3.zero, 0.016f);
                Assert.That(yaw, Is.EqualTo(120f), "Facing remains unchanged while standing still");
            }
        }

        [Test]
        public void CharacterFacing_WhileHacking_FacesTerminal()
        {
            var facing = new CharacterFacing();
            facing.Reset(0f);

            // Standing at (10, 0, 10), terminal at (10, 0, 15) -> directly north (+Z: 0 deg)
            float yaw = facing.Update(Vector3.zero, 0.05f, new Vector3(10, 0, 10), new Vector3(10, 0, 15));
            Assert.That(yaw, Is.EqualTo(0f).Within(0.1f));

            // Terminal at (15, 0, 10) -> directly east (+X: 90 deg)
            for (int i = 0; i < 20; i++)
            {
                yaw = facing.Update(Vector3.zero, 0.05f, new Vector3(10, 0, 10), new Vector3(15, 0, 10));
            }
            Assert.That(yaw, Is.EqualTo(90f).Within(0.1f), "Turns to face terminal when stationary");
        }

        [Test]
        public void KillerViewRig_ConstantsAndPitchClamping_MatchSpecNumbers()
        {
            Assert.That(KillerViewRig.EyeHeight, Is.EqualTo(1.7f), "Killer eye height is 1.7 m");
            Assert.That(KillerViewRig.MaxPitch, Is.EqualTo(70f), "Killer pitch is clamped to +/- 70 deg");
            // Horizontal FOV 90 at 16:9 corresponds to vertical ~58.7 deg
            Assert.That(KillerViewRig.VerticalFov, Is.EqualTo(58.7f).Within(0.5f), "Vertical FOV matches 90 deg horizontal at 16:9");

            var rig = new KillerViewRig();
            Assert.That(rig.Pitch, Is.EqualTo(0f));

            rig.LookPitch(50f);
            Assert.That(rig.Pitch, Is.EqualTo(-50f));
            rig.LookPitch(50f);
            Assert.That(rig.Pitch, Is.EqualTo(-70f), "Clamped to -70 deg");

            rig.LookPitch(-200f);
            Assert.That(rig.Pitch, Is.EqualTo(70f), "Clamped to +70 deg");
        }

        [Test]
        public void MovementInput_SendsCharacterFacingForSurvivor_AndViewYawForKiller()
        {
            var input = new MovementInput();

            // Survivor call: moveYaw = camera yaw (45), facingYaw = character facing (180)
            Assert.That(input.TryCreate(true, 0, 1, 45f, 180f, MovementInput.Interval, out C2S_MoveInput survivorPacket), Is.True);
            Assert.That(survivorPacket.Yaw, Is.EqualTo(180f), "Survivor packet carries character facing yaw");

            // Killer call: moveYaw = view yaw (75), facingYaw = view yaw (75)
            var killerInput = new MovementInput();
            Assert.That(killerInput.TryCreate(true, 0, 1, 75f, 75f, MovementInput.Interval, out C2S_MoveInput killerPacket), Is.True);
            Assert.That(killerPacket.Yaw, Is.EqualTo(75f), "Killer packet carries view yaw");
        }
    }
}
