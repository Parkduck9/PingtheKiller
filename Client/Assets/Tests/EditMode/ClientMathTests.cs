using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    public sealed class ClientMathTests
    {
        [Test]
        public void InterpolationUsesLiteralMidpointAndClampsWithoutExtrapolation()
        {
            var a = new Vector3(2, 1, -4); var b = new Vector3(10, 1, 8);
            Assert.That(ClientMath.Interpolate(a, b, 4, 6, 5), Is.EqualTo(new Vector3(6, 1, 2)));
            Assert.That(ClientMath.Interpolate(a, b, 4, 6, 3), Is.EqualTo(a));
            Assert.That(ClientMath.Interpolate(a, b, 4, 6, 7), Is.EqualTo(b));
            Assert.That(ClientMath.Interpolate(a, b, 4, 4, 4), Is.EqualTo(b));
        }

        [Test]
        public void YawCrossesZeroAlongShortestArc()
        {
            Assert.That(Mathf.DeltaAngle(0, ClientMath.InterpolateYaw(350, 10, 0, 1, .5)), Is.EqualTo(0).Within(.001));
        }

        [TestCase(0, 0, 1)]
        [TestCase(90, 1, 0)]
        [TestCase(180, 0, -1)]
        public void ForwardInputIsConvertedToWorldDirection(float yaw, float x, float z)
        {
            Vector3 actual = ClientMath.InputDirection(0, 1, yaw);
            Assert.That(actual.x, Is.EqualTo(x).Within(.0001));
            Assert.That(actual.z, Is.EqualTo(z).Within(.0001));
        }

        [TestCase(0, 1, 0)]
        [TestCase(90, 0, -1)]
        [TestCase(180, -1, 0)]
        public void RightInputIsConvertedToWorldDirection(float yaw, float x, float z)
        {
            Vector3 actual = ClientMath.InputDirection(1, 0, yaw);
            Assert.That(actual.x, Is.EqualTo(x).Within(.0001));
            Assert.That(actual.z, Is.EqualTo(z).Within(.0001));
        }

        [Test]
        public void DiagonalInputCannotExceedUnitLength()
        {
            Assert.That(ClientMath.InputDirection(1, 1, 90).magnitude, Is.EqualTo(1).Within(.0001));
            Assert.That(ClientMath.InputDirection(0, 0, 180), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void LegacyOverloadTreatsEachCallAsOneSlotWithReleaseAndBackupZero()
        {
            // Frame-rate behaviour of the 30 Hz rule lives in MovementInputTests.
            var input = new MovementInput();
            Assert.That(input.TryCreate(false, 0, 0, 0, out _), Is.False);
            Assert.That(input.TryCreate(true, 0, 1, 90, out var first), Is.True);
            Assert.That(first.Seq, Is.EqualTo(1));
            Assert.That(input.TryCreate(true, 1, 1, 90, out var next), Is.True);
            Assert.That(next.Seq, Is.EqualTo(2));
            Assert.That(input.TryCreate(false, 1, 1, 90, out var stop), Is.True);
            Assert.That(stop.DirX, Is.Zero); Assert.That(stop.DirZ, Is.Zero);
            Assert.That(stop.Seq, Is.EqualTo(3));
            Assert.That(input.TryCreate(false, 0, 0, 0, out var backup), Is.True);
            Assert.That(backup.DirZ, Is.Zero); Assert.That(backup.Seq, Is.EqualTo(4));
            Assert.That(input.TryCreate(false, 0, 0, 0, out _), Is.False);
            Assert.That(input.TryCreate(true, 0, 1, 0, out var resumed), Is.True);
            Assert.That(resumed.Seq, Is.EqualTo(5));
        }

        [TestCase("127.0.0.1:7777", true)]
        [TestCase(" 192.168.1.20:65535 ", true)]
        [TestCase("[::1]:7777", true)]
        [TestCase("127.0.0.1:1", true)]
        [TestCase("127.0.0.1:0", false)]
        [TestCase("127.0.0.1:65536", false)]
        [TestCase("127.0.0.1:-1", false)]
        [TestCase("127.0.0.1:abc", false)]
        [TestCase("127.0.0.1", false)]
        [TestCase("127.1:7777", false)]
        [TestCase("999.0.0.1:7777", false)]
        [TestCase("localhost:7777", false)]
        [TestCase("::1:7777", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void EndpointValidation(string value, bool expected)
        {
            Assert.That(EndpointParser.TryParse(value, out var endpoint, out string error), Is.EqualTo(expected));
            if (expected) { Assert.That(endpoint, Is.Not.Null); Assert.That(error, Is.Empty); }
            else Assert.That(error, Is.Not.Empty);
        }

        [TestCase(1, 1.2f)]
        [TestCase(2, .6f)]
        [TestCase(3, .25f)]
        public void ThreatBeepIntervalsMatchGdd(byte level, float seconds)
            => Assert.That(ClientMath.BeepInterval(level), Is.EqualTo(seconds));

        [Test]
        public void ThreatZeroIsSilent() => Assert.That(ClientMath.BeepInterval(0), Is.EqualTo(float.PositiveInfinity));

        [Test]
        public void SnapshotSamplingUsesHundredMillisecondDelayAndFadesOmissions()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Frame(1, 2, 350), 1);
            buffer.Add(Frame(2, 10, 10), 1.1);
            Assert.That(buffer.Sample(7, 1.15, out var position, out float yaw, out float alpha), Is.True);
            Assert.That(position.x, Is.EqualTo(6).Within(.0001));
            Assert.That(Mathf.DeltaAngle(0, yaw), Is.EqualTo(0).Within(.001));
            Assert.That(alpha, Is.EqualTo(1));
            buffer.Add(new S2C_WorldState { Tick = 3 }, 1.2);
            buffer.Sample(7, 1.375, out position, out _, out alpha);
            Assert.That(position.x, Is.EqualTo(10));
            Assert.That(alpha, Is.EqualTo(.5).Within(.001));
            Assert.That(buffer.Sample(7, 1.46, out _, out _, out _), Is.False);
        }

        [Test]
        public void NewOrReappearingEntityDoesNotLeakBeforeItsSnapshotTime()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(new S2C_WorldState { Tick = 1 }, 1);
            buffer.Add(Frame(2, 10, 0), 1.1);
            Assert.That(buffer.Sample(7, 1.15, out _, out _, out _), Is.False);
            Assert.That(buffer.Sample(7, 1.21, out _, out _, out _), Is.True);
        }

        [Test]
        public void MissingEntityKeepsFadingWhenNoNewPacketsArrive()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Frame(1, 10, 0), 1);
            buffer.Add(new S2C_WorldState { Tick = 2 }, 1.1);
            Assert.That(buffer.Sample(7, 1.275, out _, out _, out float alpha), Is.True);
            Assert.That(alpha, Is.EqualTo(.5).Within(.001));
            Assert.That(buffer.Sample(7, 1.36, out _, out _, out _), Is.False);
        }

        private static S2C_WorldState Frame(uint tick, float x, float yaw)
        {
            var state = new S2C_WorldState { Tick = tick };
            state.Players.Add(new PlayerState { Id = 7, X = x, Yaw = yaw });
            return state;
        }
    }
}
