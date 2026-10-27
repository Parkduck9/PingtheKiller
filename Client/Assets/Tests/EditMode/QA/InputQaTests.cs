using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // Unity is Y-up and yaw is measured clockwise from +Z (so yaw 90 faces +X).
    // Direction is reported as (DirX, DirZ).
    public sealed class InputQaTests
    {
        private const float Tolerance = 1e-4f;

        // right, forward, yaw -> expected (x, z)
        [TestCase(0f, 1f, 0f, 0f, 1f)]       // W at yaw 0
        [TestCase(0f, 1f, 90f, 1f, 0f)]      // W at yaw 90
        [TestCase(0f, 1f, 180f, 0f, -1f)]    // W at yaw 180
        [TestCase(0f, 1f, 270f, -1f, 0f)]    // W at yaw 270
        [TestCase(0f, 1f, 45f, 0.70710678f, 0.70710678f)]
        [TestCase(1f, 0f, 0f, 1f, 0f)]       // D at yaw 0
        [TestCase(-1f, 0f, 0f, -1f, 0f)]     // A at yaw 0
        [TestCase(0f, -1f, 0f, 0f, -1f)]     // S at yaw 0
        [TestCase(1f, 0f, 90f, 0f, -1f)]     // D at yaw 90: right of +X is -Z
        [TestCase(0f, -1f, 90f, -1f, 0f)]    // S at yaw 90
        [TestCase(1f, 1f, 0f, 0.70710678f, 0.70710678f)]   // W+D normalised
        [TestCase(1f, 1f, 90f, 0.70710678f, -0.70710678f)] // W+D at yaw 90
        [TestCase(-1f, 1f, 0f, -0.70710678f, 0.70710678f)] // W+A
        public void InputIsRotatedByCameraYaw(float right, float forward, float yaw, float expectedX, float expectedZ)
        {
            Vector3 d = ClientMath.InputDirection(right, forward, yaw);
            Assert.That(d.x, Is.EqualTo(expectedX).Within(Tolerance));
            Assert.That(d.z, Is.EqualTo(expectedZ).Within(Tolerance));
            Assert.That(d.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(90f)]
        [TestCase(137f)]
        [TestCase(270f)]
        public void NoKeysGiveZeroVectorAtAnyYaw(float yaw)
        {
            Vector3 d = ClientMath.InputDirection(0f, 0f, yaw);
            Assert.That(d.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(d.z, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(0f)]
        [TestCase(30f)]
        [TestCase(90f)]
        [TestCase(200f)]
        [TestCase(315f)]
        public void DiagonalHasLengthOneAtAnyYaw(float yaw)
        {
            Vector3 d = ClientMath.InputDirection(1f, 1f, yaw);
            Assert.That(new Vector2(d.x, d.z).magnitude, Is.EqualTo(1f).Within(Tolerance));
            d = ClientMath.InputDirection(1f, 0f, yaw);
            Assert.That(new Vector2(d.x, d.z).magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void PacketCarriesRotatedDirectionAndLookYaw()
        {
            var input = new MovementInput();
            Assert.That(input.TryCreate(true, 0f, 1f, 90f, out C2S_MoveInput packet), Is.True);
            Assert.That(packet.DirX, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(packet.DirZ, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(packet.Yaw, Is.EqualTo(90f).Within(Tolerance));
        }

        [Test]
        public void NothingIsSentWhileIdleFromTheStart()
        {
            var input = new MovementInput();
            for (int frame = 0; frame < 5; frame++)
                Assert.That(input.TryCreate(false, 0f, 0f, 0f, out C2S_MoveInput _), Is.False);
        }

        // OneSendPerHeldFrameThenExactlyOneZeroSendThenSilence was superseded by the 30 Hz rule
        // (orchestrator ruling, U4-a2 item 1); see MovementRateQaTests for the replacement tests.

        [Test]
        public void HeldWithOpposingKeysStillSendsWhenASlotIsDue()
        {
            // Any key held => a send per due slot (legacy overload: one call = one slot), even if the net direction is zero.
            var input = new MovementInput();
            for (int frame = 0; frame < 3; frame++)
            {
                Assert.That(input.TryCreate(true, 0f, 0f, 0f, out C2S_MoveInput packet), Is.True);
                Assert.That(packet.DirX, Is.EqualTo(0f));
                Assert.That(packet.DirZ, Is.EqualTo(0f));
            }
        }

        // SeqIsStrictlyIncreasingAcrossHeldFramesZeroSendAndALaterPress was superseded by the 30 Hz rule
        // (orchestrator ruling, U4-a2 item 1); see MovementRateQaTests for the replacement tests.
    }
}
