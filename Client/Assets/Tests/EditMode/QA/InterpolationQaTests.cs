using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    internal static class Snap
    {
        public static S2C_WorldState State(uint tick, params PlayerState[] players)
        {
            var state = new S2C_WorldState { Tick = tick };
            state.Players.AddRange(players);
            return state;
        }

        public static PlayerState P(int id, float x, float z = 0, float yaw = 0)
        {
            return new PlayerState { Id = id, X = x, Z = z, Yaw = yaw };
        }
    }

    // Written from docs/briefs/U4-a1.md scope item 5 and the QA brief item 1:
    // render time = now - 100 ms, snapshot times are in seconds (NetClient.Now domain).
    public sealed class InterpolationQaTests
    {
        private const double RenderDelay = 0.1;

        [Test]
        public void RenderDelayIsOneHundredMilliseconds()
        {
            Assert.That(SnapshotBuffer.Delay, Is.EqualTo(0.1).Within(1e-9));
        }

        [Test]
        public void SnapshotsAtZeroAndFiftyMsRenderedAt125MsGiveMidpoint()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(7, 0f, 0f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(7, 10f, 4f)), 0.05);

            Assert.That(buffer.Sample(7, 0.125, out Vector3 position, out _, out float alpha), Is.True);
            Assert.That(position.x, Is.EqualTo(5f).Within(0.01f));
            Assert.That(position.z, Is.EqualTo(2f).Within(0.01f));
            Assert.That(alpha, Is.EqualTo(1f).Within(1e-4f));
        }

        [TestCase(0.1, 0f)]
        [TestCase(0.1125, 2.5f)]
        [TestCase(0.15, 10f)]
        public void PositionFollowsRenderTimeNowMinus100Ms(double now, float expectedX)
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(7, 0f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(7, 10f)), 0.05);

            Assert.That(buffer.Sample(7, now, out Vector3 position, out _, out _), Is.True);
            Assert.That(position.x, Is.EqualTo(expectedX).Within(0.01f));
        }

        [Test]
        public void RenderTimePastNewestSnapshotHoldsLastKnownPosition()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(7, 0f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(7, 10f)), 0.05);

            Assert.That(buffer.Sample(7, 0.5, out Vector3 position, out _, out float alpha), Is.True);
            Assert.That(position.x, Is.EqualTo(10f).Within(0.01f), "Must not extrapolate beyond the newest snapshot.");
            Assert.That(alpha, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void YawInterpolatesTheShortWayFrom350To10()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(7, 0f, 0f, 350f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(7, 0f, 0f, 10f)), 0.05);

            Assert.That(buffer.Sample(7, 0.125, out _, out float mid, out _), Is.True);
            Assert.That(Mathf.DeltaAngle(0f, mid), Is.EqualTo(0f).Within(0.01f), "Midpoint must be ~0/360, not 180. Got " + mid);

            Assert.That(buffer.Sample(7, 0.1125, out _, out float quarter, out _), Is.True);
            Assert.That(Mathf.DeltaAngle(355f, quarter), Is.EqualTo(0f).Within(0.01f), "Quarter point must be ~355. Got " + quarter);
        }

        [Test]
        public void YawInterpolatesTheShortWayFrom10To350()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(7, 0f, 0f, 10f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(7, 0f, 0f, 350f)), 0.05);

            Assert.That(buffer.Sample(7, 0.125, out _, out float mid, out _), Is.True);
            Assert.That(Mathf.DeltaAngle(0f, mid), Is.EqualTo(0f).Within(0.01f), "Got " + mid);
        }

        [Test]
        public void ClientMathYawHelperTakesTheShortWayToo()
        {
            float yaw = ClientMath.InterpolateYaw(350f, 10f, 0.0, 0.05, 0.025);
            Assert.That(Mathf.DeltaAngle(0f, yaw), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void RenderTimeBeforeFirstSnapshotNeverShowsAnythingElseThanTheFirstSnapshot()
        {
            // Spec wording: "render time before the first snapshot clamps to the first".
            // The implementation may alternatively hide the entity (returns false); that choice is reported in
            // docs/qa/U4-qa-a1.md. Whatever it does, it must not invent a position or throw.
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(7, 3f, 4f, 20f)), 1.0);
            buffer.Add(Snap.State(2, Snap.P(7, 9f, 4f, 20f)), 1.05);

            bool shown = buffer.Sample(7, 1.05, out Vector3 position, out float yaw, out float alpha);
            if (shown)
            {
                Assert.That(position.x, Is.EqualTo(3f).Within(0.01f));
                Assert.That(position.z, Is.EqualTo(4f).Within(0.01f));
                Assert.That(yaw, Is.EqualTo(20f).Within(0.01f));
                Assert.That(alpha, Is.EqualTo(1f).Within(1e-4f));
            }
        }

        [Test]
        public void EmptyBufferAndUnknownEntityAreNotShown()
        {
            var buffer = new SnapshotBuffer();
            Assert.That(buffer.Sample(7, 5.0, out _, out _, out _), Is.False);
            buffer.Add(Snap.State(1, Snap.P(7, 0f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(7, 1f)), 0.05);
            Assert.That(buffer.Sample(99, 0.125, out _, out _, out _), Is.False);
        }

        private static SnapshotBuffer VanishingEntity()
        {
            // Entity 1 is in the first snapshot, missing from every later one (Signal -> Silent for the killer).
            // Entity 2 is always present.
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(1, 3f), Snap.P(2, 0f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(2, 0f)), 0.05);
            buffer.Add(Snap.State(3, Snap.P(2, 0f)), 0.10);
            buffer.Add(Snap.State(4, Snap.P(2, 0f)), 0.15);
            buffer.Add(Snap.State(5, Snap.P(2, 0f)), 0.20);
            buffer.Add(Snap.State(6, Snap.P(2, 0f)), 0.25);
            return buffer;
        }

        // elapsed = time since the first snapshot that omits the entity became the render time.
        private static double NowFor(double elapsedSinceAbsent)
        {
            return 0.05 + elapsedSinceAbsent + RenderDelay;
        }

        [TestCase(0.0375, 0.75f)]
        [TestCase(0.075, 0.5f)]
        [TestCase(0.1125, 0.25f)]
        public void MissingEntityFadesLinearlyOver150Ms(double elapsed, float expectedAlpha)
        {
            var buffer = VanishingEntity();
            Assert.That(buffer.Sample(1, NowFor(elapsed), out Vector3 position, out _, out float alpha), Is.True);
            Assert.That(alpha, Is.EqualTo(expectedAlpha).Within(0.02f));
            Assert.That(position.x, Is.EqualTo(3f).Within(0.01f), "A fading entity stays at its last known position.");
        }

        [Test]
        public void FadeAlphaNeverIncreasesAndReachesZeroAt150Ms()
        {
            var buffer = VanishingEntity();
            float previous = 1f;
            for (int step = 1; step <= 14; step++)
            {
                double elapsed = step * 0.01;
                Assert.That(buffer.Sample(1, NowFor(elapsed), out _, out _, out float alpha), Is.True, "step " + step);
                Assert.That(alpha, Is.LessThan(previous + 1e-4f), "step " + step);
                previous = alpha;
            }

            bool shown = buffer.Sample(1, NowFor(0.15), out _, out _, out float atEnd);
            if (shown) Assert.That(atEnd, Is.LessThanOrEqualTo(0.01f));
        }

        [TestCase(0.16)]
        [TestCase(0.2)]
        public void MissingEntityIsRemovedAfterTheFade(double elapsed)
        {
            var buffer = VanishingEntity();
            Assert.That(buffer.Sample(1, NowFor(elapsed), out _, out _, out _), Is.False);
        }

        [Test]
        public void StillPresentEntityKeepsFullAlphaWhileAnotherFades()
        {
            var buffer = VanishingEntity();
            Assert.That(buffer.Sample(2, NowFor(0.075), out _, out _, out float alpha), Is.True);
            Assert.That(alpha, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void EntityThatReappearsDuringTheFadeReturnsToFullAlpha()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snap.State(1, Snap.P(1, 3f), Snap.P(2, 0f)), 0.0);
            buffer.Add(Snap.State(2, Snap.P(2, 0f)), 0.05);                // absent: fade starts when this becomes render time
            buffer.Add(Snap.State(3, Snap.P(1, 3f), Snap.P(2, 0f)), 0.10); // back 50 ms later, inside the 150 ms window
            buffer.Add(Snap.State(4, Snap.P(1, 3f), Snap.P(2, 0f)), 0.15);

            // render time 0.075: fading (alpha < 1)
            Assert.That(buffer.Sample(1, 0.075 + RenderDelay, out _, out _, out float fading), Is.True);
            Assert.That(fading, Is.LessThan(1f));

            // render time 0.12: the reappearing snapshot is now current -> full alpha
            Assert.That(buffer.Sample(1, 0.12 + RenderDelay, out _, out _, out float back), Is.True);
            Assert.That(back, Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
