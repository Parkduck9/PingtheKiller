using System;
using NUnit.Framework;
using PingTheKiller.Client.World;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4-a1.md item 8 and docs/briefs/U4-a2.md items 5 and 6.
    public sealed class BeepSeamQaTests
    {
        private static int ExpectedLength(int rate) => (int)Math.Round(rate * 0.06, MidpointRounding.AwayFromZero);

        private static double Goertzel(float[] samples, int rate, double frequency)
        {
            double omega = 2 * Math.PI * frequency / rate;
            double coefficient = 2 * Math.Cos(omega), s1 = 0, s2 = 0;
            foreach (float x in samples)
            {
                double s0 = x + coefficient * s1 - s2;
                s2 = s1; s1 = s0;
            }
            return Math.Sqrt(s1 * s1 + s2 * s2 - coefficient * s1 * s2);
        }

        // ---- BuildBeepSamples ----------------------------------------------------------------------------------------

        [TestCase(44100)]
        [TestCase(48000)]
        [TestCase(22050)]
        [TestCase(32000)]
        [TestCase(16000)]
        [TestCase(8000)]
        public void SampleCountIsRateTimesSixtyMilliseconds(int rate)
        {
            float[] samples = ThreatAudio.BuildBeepSamples(rate);
            Assert.That(samples.Length, Is.EqualTo(ExpectedLength(rate)));
        }

        [TestCase(44100)]
        [TestCase(48000)]
        [TestCase(22050)]
        public void ZeroCrossingsMatchEightHundredEightyHertzOverSixtyMilliseconds(int rate)
        {
            float[] samples = ThreatAudio.BuildBeepSamples(rate);
            int crossings = 0, previousSign = 0;
            foreach (float x in samples)
            {
                int sign = x > 0 ? 1 : x < 0 ? -1 : 0;
                if (sign == 0) continue;
                if (previousSign != 0 && sign != previousSign) crossings++;
                previousSign = sign;
            }
            double expected = 2 * 880 * 0.06; // 105.6
            Assert.That(crossings, Is.InRange((int)Math.Floor(expected) - 2, (int)Math.Ceiling(expected) + 2), "zero crossings at " + rate + " Hz");
        }

        [TestCase(44100)]
        [TestCase(48000)]
        public void DominantFrequencyIs880Hertz(int rate)
        {
            float[] samples = ThreatAudio.BuildBeepSamples(rate);
            double at880 = Goertzel(samples, rate, 880);
            foreach (double other in new[] { 440.0, 660.0, 1100.0, 1320.0, 1760.0 })
                Assert.That(at880, Is.GreaterThan(3 * Goertzel(samples, rate, other)), "880 Hz must dominate " + other + " Hz");
        }

        [TestCase(44100)]
        [TestCase(48000)]
        public void BeepIsAudibleBoundedAndFreeOfNaN(int rate)
        {
            float[] samples = ThreatAudio.BuildBeepSamples(rate);
            float peak = 0;
            foreach (float x in samples)
            {
                Assert.That(float.IsNaN(x) || float.IsInfinity(x), Is.False);
                peak = Mathf.Max(peak, Mathf.Abs(x));
            }
            Assert.That(peak, Is.InRange(0.05f, 1f), "audible but not clipping");
        }

        [TestCase(44100)]
        [TestCase(48000)]
        public void BeepStartsAndEndsNearSilenceSoItDoesNotClick(int rate)
        {
            float[] samples = ThreatAudio.BuildBeepSamples(rate);
            Assert.That(Mathf.Abs(samples[0]), Is.LessThan(0.02f), "first sample");
            Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(0.02f), "last sample");
            // The body (middle third) carries the full level.
            float body = 0;
            for (int i = samples.Length / 3; i < samples.Length * 2 / 3; i++) body = Mathf.Max(body, Mathf.Abs(samples[i]));
            float edge = 0;
            for (int i = 0; i < 10; i++) edge = Mathf.Max(edge, Mathf.Abs(samples[i]));
            Assert.That(edge, Is.LessThan(body * 0.5f), "short attack envelope");
        }

        [Test]
        public void BuildBeepSamplesIsDeterministic()
        {
            Assert.That(ThreatAudio.BuildBeepSamples(44100), Is.EqualTo(ThreatAudio.BuildBeepSamples(44100)));
        }

        [Test]
        public void SetLevelBeforeAwakeDoesNotThrow()
        {
            // EditMode does not run Awake for a plain MonoBehaviour, which is exactly the "before Awake" state of a2 item 5.
            var go = new GameObject("QA threat audio");
            try
            {
                var audio = go.AddComponent<ThreatAudio>();
                Assert.DoesNotThrow(() =>
                {
                    audio.SetLevel(3);
                    audio.SetLevel(1);
                    audio.SetLevel(0);
                    audio.SetLevel(255);
                    audio.SetLevel(0);
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- NextBeepAfterChange ------------------------------------------------------------------------------------

        [TestCase((byte)0, (byte)1)]
        [TestCase((byte)0, (byte)3)]
        [TestCase((byte)1, (byte)2)]
        [TestCase((byte)1, (byte)3)]
        [TestCase((byte)2, (byte)3)]
        public void RisingThreatBeepsNowRegardlessOfTheOldSchedule(byte from, byte to)
        {
            Assert.That(ThreatAudio.NextBeepAfterChange(from, to, 10.0, 9.9, 11.5), Is.EqualTo(10.0));
            Assert.That(ThreatAudio.NextBeepAfterChange(from, to, 10.0, double.NegativeInfinity, double.PositiveInfinity), Is.EqualTo(10.0));
        }

        [Test]
        public void FallingThreatDoesNotBeepImmediately()
        {
            double now = 10.1;
            // 3 -> 2: old schedule 10.25 (lastBeep 10.0 + 0.25) is earlier than lastBeep + 0.6 = 10.6.
            double next = ThreatAudio.NextBeepAfterChange(3, 2, now, 10.0, 10.25);
            Assert.That(next, Is.GreaterThan(now), "no immediate beep when threat falls");
            Assert.That(next, Is.EqualTo(10.25).Within(1e-6), "min(scheduledNext, lastBeep + newInterval)");
        }

        [Test]
        public void FallingThreatNeverMovesTheNextBeepLaterThanTheNewIntervalAfterTheLastBeep()
        {
            // 2 -> 1 with a schedule that is far in the future: capped at lastBeep + 1.2.
            double next = ThreatAudio.NextBeepAfterChange(2, 1, 10.1, 10.0, 12.0);
            Assert.That(next, Is.EqualTo(11.2).Within(1e-6));
        }

        [Test]
        public void FallingThreatKeepsAnEarlierScheduledBeep()
        {
            double next = ThreatAudio.NextBeepAfterChange(3, 1, 10.1, 10.0, 10.25);
            Assert.That(next, Is.EqualTo(10.25).Within(1e-6), "1.2 s after the last beep would be 11.2, the schedule wins");
        }

        [Test]
        public void FallingThreatBeforeAnyBeepKeepsTheScheduleInsteadOfBeepingAtOnce()
        {
            double next = ThreatAudio.NextBeepAfterChange(2, 1, 10.0, double.NegativeInfinity, 10.4);
            Assert.That(double.IsNegativeInfinity(next), Is.False);
            Assert.That(next, Is.EqualTo(10.4).Within(1e-6));
        }

        [TestCase((byte)1)]
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        public void FallingToZeroSchedulesNoBeepAtAll(byte from)
        {
            double next = ThreatAudio.NextBeepAfterChange(from, 0, 10.0, 9.9, 10.5);
            Assert.That(double.IsPositiveInfinity(next), Is.True, "level 0 = silence");
        }

        [Test]
        public void IntervalsUsedByTheRuleMatchTheGddTable()
        {
            // GDD 3.2 intervals: level 1 = 1.2 s, level 2 = 0.6 s, level 3 = 0.25 s. Falling 3->2 and 2->1 with a distant schedule
            // exposes lastBeep + interval(newLevel).
            Assert.That(ThreatAudio.NextBeepAfterChange(3, 2, 20.0, 10.0, 99.0), Is.EqualTo(10.6).Within(1e-6));
            Assert.That(ThreatAudio.NextBeepAfterChange(2, 1, 20.0, 10.0, 99.0), Is.EqualTo(11.2).Within(1e-6));
        }
    }
}
