using System;
using NUnit.Framework;
using PingTheKiller.Client.World;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    public sealed class ThreatAudioTests
    {
        [TestCase(44100)]
        [TestCase(48000)]
        public void BeepIsSixtyMillisecondsOf880Hertz(int rate)
        {
            float[] samples = ThreatAudio.BuildBeepSamples(rate);
            Assert.That(samples.Length, Is.EqualTo(Mathf.RoundToInt(rate * .06f)));
            int crossings = 0;
            for (int i = 1; i < samples.Length; i++) if ((samples[i - 1] < 0) != (samples[i] < 0)) crossings++;
            Assert.That(crossings, Is.InRange(104, 107), "2 x 880 x 0.06 = 105.6 +- 2");
            Assert.That(samples[0], Is.Zero.Within(1e-6), "attack starts silent");
            Assert.That(samples[samples.Length - 1], Is.Zero.Within(1e-6), "release ends silent");
            foreach (float sample in samples) Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(.15f + 1e-6f));
        }

        [Test]
        public void RisingThreatBeepsNow()
        {
            Assert.That(ThreatAudio.NextBeepAfterChange(0, 1, 10, double.NegativeInfinity, double.PositiveInfinity), Is.EqualTo(10));
            Assert.That(ThreatAudio.NextBeepAfterChange(1, 3, 10, 9.5, 10.7), Is.EqualTo(10));
        }

        [Test]
        public void FallingThreatNeverBeepsImmediately()
        {
            Assert.That(ThreatAudio.NextBeepAfterChange(3, 1, 10, 9.9, 10.15), Is.EqualTo(10.15).Within(1e-9));
            Assert.That(ThreatAudio.NextBeepAfterChange(3, 2, 10, 9.9, 20), Is.EqualTo(9.9 + .6).Within(1e-6));
            Assert.That(ThreatAudio.NextBeepAfterChange(2, 1, 10, double.NegativeInfinity, 10.4), Is.EqualTo(10.4));
        }

        [Test]
        public void OscillatingAroundAThresholdDoesNotBeepPerChange()
        {
            double next = ThreatAudio.NextBeepAfterChange(1, 2, 10, 9, 10.2);
            Assert.That(next, Is.EqualTo(10));
            double lastBeep = next; next = lastBeep + ClientMath.BeepInterval(2);
            next = ThreatAudio.NextBeepAfterChange(2, 1, 10.1, lastBeep, next);
            Assert.That(next, Is.EqualTo(10.6).Within(1e-6), "dropping back keeps the running schedule");
        }

        [Test]
        public void ThreatZeroStopsTheSchedule()
            => Assert.That(ThreatAudio.NextBeepAfterChange(2, 0, 10, 9.9, 10.5), Is.EqualTo(double.PositiveInfinity));
    }
}
