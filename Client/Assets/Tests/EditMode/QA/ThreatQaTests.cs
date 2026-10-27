using NUnit.Framework;
using PingTheKiller.Client.World;

namespace PingTheKiller.Client.QA.Tests
{
    // GDD 3.2: level 0 none, 1 -> 1.2 s, 2 -> 0.6 s, 3 -> 0.25 s.
    // The 880 Hz / 60 ms clip is generated inside ThreatAudio.Awake (private, MonoBehaviour-bound) and is not
    // reachable from EditMode without reflection; see docs/qa/U4-qa-a1.md (finding QA-04).
    public sealed class ThreatQaTests
    {
        [Test]
        public void LevelZeroSchedulesNoBeep()
        {
            Assert.That(float.IsPositiveInfinity(ClientMath.BeepInterval(0)), Is.True);
        }

        [TestCase((byte)1, 1.2f)]
        [TestCase((byte)2, 0.6f)]
        [TestCase((byte)3, 0.25f)]
        public void BeepIntervalMatchesGdd(byte level, float seconds)
        {
            Assert.That(ClientMath.BeepInterval(level), Is.EqualTo(seconds).Within(1e-4f));
        }

        [Test]
        public void HigherThreatBeepsFaster()
        {
            Assert.That(ClientMath.BeepInterval(1), Is.GreaterThan(ClientMath.BeepInterval(2)));
            Assert.That(ClientMath.BeepInterval(2), Is.GreaterThan(ClientMath.BeepInterval(3)));
            Assert.That(ClientMath.BeepInterval(3), Is.GreaterThan(0f));
        }

        [TestCase((byte)4)]
        [TestCase((byte)5)]
        [TestCase((byte)127)]
        [TestCase((byte)255)]
        public void OutOfRangeLevelDoesNotThrowAndNeverProducesARunawayInterval(byte level)
        {
            float interval = 0f;
            Assert.DoesNotThrow(() => interval = ClientMath.BeepInterval(level));
            Assert.That(float.IsNaN(interval), Is.False);
            Assert.That(interval, Is.GreaterThan(0f), "A zero or negative interval would beep every frame.");
        }
    }
}
