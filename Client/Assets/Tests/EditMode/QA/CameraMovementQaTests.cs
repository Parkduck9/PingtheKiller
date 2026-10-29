using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test item 10. HUD_SPEC 4.10: "C2S_MoveInput.Yaw = the character's facing, not the camera's. Standing still
    // keeps the last facing, so orbiting the camera never turns the character or makes it Signal." HUD_SPEC 6.0: Killer
    // MoveInput.Yaw = view yaw. The loop below is the same sequence ClientApp.Update runs each frame.
    [Category("U4cQaA1")]
    public sealed class CameraMovementQaTests
    {
        private const double Frame = 1.0 / 60;

        [Test]
        public void ASurvivorPacketCarriesTheCharacterFacingAndACameraRelativeDirection()
        {
            var input = new MovementInput();
            Assert.That(input.TryCreate(true, 0, 1, 135, 20, MovementInput.Interval, out C2S_MoveInput packet), Is.True);
            Assert.That(packet.Yaw, Is.EqualTo(20f), "Yaw is the facing, not the camera yaw 135");
            Assert.That(packet.DirX, Is.EqualTo(Mathf.Sin(135 * Mathf.Deg2Rad)).Within(1e-3f), "W at camera yaw 135 moves away from the camera");
            Assert.That(packet.DirZ, Is.EqualTo(Mathf.Cos(135 * Mathf.Deg2Rad)).Within(1e-3f));
        }

        [Test]
        public void ASurvivorStrafeIsRelativeToTheCameraToo()
        {
            var input = new MovementInput();
            Assert.That(input.TryCreate(true, 1, 0, 90, 0, MovementInput.Interval, out C2S_MoveInput packet), Is.True);
            // D at camera yaw 90 (camera looks +x, so its right is -z).
            Assert.That(packet.DirX, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(packet.DirZ, Is.EqualTo(-1f).Within(1e-3f));
        }

        [Test]
        public void AKillerPacketCarriesTheViewYawAsItsYawWithTheSameDirectionRule()
        {
            var input = new MovementInput();
            Assert.That(input.TryCreate(true, 0, 1, 77f, MovementInput.Interval, out C2S_MoveInput packet), Is.True);
            Assert.That(packet.Yaw, Is.EqualTo(77f), "Killer: the four-argument form uses the view yaw for both");
            Assert.That(packet.DirX, Is.EqualTo(Mathf.Sin(77 * Mathf.Deg2Rad)).Within(1e-3f));
            Assert.That(packet.DirZ, Is.EqualTo(Mathf.Cos(77 * Mathf.Deg2Rad)).Within(1e-3f));
            var second = new MovementInput();
            Assert.That(second.TryCreate(true, 0, 1, 77f, 77f, MovementInput.Interval, out C2S_MoveInput same), Is.True);
            Assert.That(same.Yaw, Is.EqualTo(77f));
        }

        [Test]
        public void OrbitingTheCameraWhileStandingStillSendsNothingAndNeverTurnsTheCharacter()
        {
            var input = new MovementInput();
            var facing = new CharacterFacing();
            facing.Reset(40);
            float camera = 0;
            for (int i = 0; i < 600; i++)
            {
                camera = Mathf.Repeat(camera + 7.3f, 360); // the mouse keeps orbiting
                float facingYaw = facing.Update(Vector3.zero, (float)Frame);
                Assert.That(input.TryCreate(false, 0, 0, camera, facingYaw, Frame, out C2S_MoveInput packet), Is.False, "frame " + i);
                Assert.That(facingYaw, Is.EqualTo(40f).Within(1e-4f), "frame " + i);
            }
        }

        [Test]
        public void TheFirstStepAfterOrbitingSendsTheFacingTheCharacterIsTurningToNotTheCameraYaw()
        {
            var input = new MovementInput();
            var facing = new CharacterFacing();
            facing.Reset(0);
            const float cameraYaw = 90;
            Vector3 move = ClientMath.InputDirection(0, 1, cameraYaw);          // W: +x
            float facingYaw = facing.Update(move, (float)Frame);                // 720 deg/s * 1/60 s = 12 degrees
            Assert.That(input.TryCreate(true, 0, 1, cameraYaw, facingYaw, Frame, out C2S_MoveInput packet), Is.True);
            Assert.That(packet.Yaw, Is.EqualTo(12f).Within(.01f));
            Assert.That(packet.Yaw, Is.Not.EqualTo(cameraYaw));
            Assert.That(packet.DirX, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void ReleasingTheKeysAfterAWalkSendsTwoZeroPacketsWithTheFacingThenSilenceEvenWhileOrbiting()
        {
            var input = new MovementInput();
            var sent = new List<C2S_MoveInput>();
            float camera = 30;
            for (int i = 0; i < 30; i++)
                if (input.TryCreate(true, 0, 1, camera, 55, Frame, out C2S_MoveInput p)) sent.Add(p);
            Assert.That(sent.Count, Is.GreaterThanOrEqualTo(10), "held: about 30 packets per second");
            int heldCount = sent.Count;
            for (int i = 0; i < 40; i++)
            {
                camera += 11;
                if (input.TryCreate(false, 0, 0, camera, 55, Frame, out C2S_MoveInput p)) sent.Add(p);
            }
            Assert.That(sent.Count - heldCount, Is.EqualTo(2), "release: one zero and one backup zero");
            for (int i = heldCount; i < sent.Count; i++)
            {
                Assert.That(sent[i].DirX, Is.EqualTo(0f)); Assert.That(sent[i].DirZ, Is.EqualTo(0f));
                Assert.That(sent[i].Yaw, Is.EqualTo(55f), "the zero packets carry the facing, not the orbiting camera yaw");
            }
            for (int i = 1; i < sent.Count; i++) Assert.That(sent[i].Seq, Is.GreaterThan(sent[i - 1].Seq), "Seq only goes up");
            for (int i = 0; i < 200; i++)
            {
                camera += 5;
                Assert.That(input.TryCreate(false, 0, 0, camera, 55, Frame, out _), Is.False, "silence after the backup zero, frame " + i);
            }
        }

        [Test]
        public void ClientAppFeedsMovementInputTheCameraYawForTheKeysAndTheCharacterFacingForSurvivors()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(code, Does.Match(@"float\s+facingYaw\s*=\s*survivor\s*\?\s*facing\.Update\(move,\s*dt,\s*position,\s*hackingTerminal\)\s*:\s*rig\.Yaw\s*;"),
                "Survivor: the character's facing; Killer: the view yaw");
            Assert.That(code, Does.Match(@"movement\.TryCreate\(held,\s*right,\s*forward,\s*rig\.Yaw,\s*facingYaw,\s*dt,\s*out C2S_MoveInput input\)"),
                "keys rotate by the camera yaw, Yaw sent is facingYaw");
            Assert.That(code, Does.Match(@"Vector3\s+move\s*=\s*held\s*\?\s*ClientMath\.InputDirection\(right,\s*forward,\s*rig\.Yaw\)\s*:\s*Vector3\.zero"),
                "the character turns toward the camera-relative move");
        }

        [Test]
        public void TheKillerPlayMenuAndLostFocusStopTheCharacterWithAZeroInput()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string menu = QaSource.MethodText(code, "private void SetMenu(");
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu, Does.Contain("movement.Final(rig.Yaw)"), "opening the menu sends one zero MoveInput");
            Assert.That(menu, Does.Contain("rig.SetCursor(!open)"), "and releases the cursor");
            Assert.That(code, Does.Match(@"bool\s+active\s*=\s*Application\.isFocused\s*&&\s*!releasedCursor\s*&&\s*!menuOpen"), "no look and no keys while the menu is open");
        }
    }
}
