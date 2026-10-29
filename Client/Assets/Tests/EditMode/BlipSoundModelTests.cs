using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;

namespace PingTheKiller.Client.Tests
{
    public sealed class BlipSoundModelTests
    {
        private static readonly Func<int, bool> Clear = _ => false;

        private static Blip B(int handle, float x, float z, float speed, BlipStatus status = BlipStatus.Normal) =>
            new Blip { Handle = handle, X = x, Z = z, PrevX = x, PrevZ = z, Speed = speed, Status = status, FacingZ = 1f };

        private static int StepsFor(BlipSoundModel m, int handle)
        {
            int c = 0;
            for (int i = 0; i < m.Steps.Count; i++) if (m.Steps[i].Handle == handle) c++;
            return c;
        }

        private static VoiceState? VoiceFor(BlipSoundModel m, int handle)
        {
            for (int i = 0; i < m.Voices.Count; i++) if (m.Voices[i].Handle == handle) return m.Voices[i];
            return null;
        }

        // Runs `seconds` of frames and returns the number of steps of `handle`.
        private static int Run(BlipSoundModel m, List<Blip> blips, float seconds, int handle, float dt = 0.01f, Func<int, bool> occ = null)
        {
            int steps = 0;
            int frames = (int)Math.Round(seconds / dt);
            for (int i = 0; i < frames; i++)
            {
                m.Update(dt, blips, 0f, 0f, occ ?? Clear);
                steps += StepsFor(m, handle);
            }
            return steps;
        }

        // --- Static rules -----------------------------------------------------------------------

        [TestCase(-20f, 0.5f, 20f, -20f)]
        [TestCase(-20f, 2f, 20f, -20f)]
        [TestCase(-20f, 11f, 20f, -30f)]
        [TestCase(-20f, 20f, 20f, -40f)]
        [TestCase(-30f, 4f, 6f, -35f)]
        [TestCase(-24f, 5f, 8f, -32f)]
        [TestCase(-24f, 10f, 10f, -40f)]
        public void AttenuationIsLinearInDbFromTwoMetresToTheLimit(float level, float distance, float range, float expected)
        {
            Assert.That(BlipSoundModel.AttenuationDb(level, distance, range), Is.EqualTo(expected).Within(1e-4));
        }

        [TestCase(20.01f, 20f)]
        [TestCase(100f, 20f)]
        [TestCase(6.5f, 6f)]
        [TestCase(float.NaN, 20f)]
        public void AttenuationMutesBeyondTheLimit(float distance, float range)
        {
            Assert.That(float.IsNegativeInfinity(BlipSoundModel.AttenuationDb(-20f, distance, range)), Is.True);
        }

        [TestCase(BlipStatus.Normal, 4f, 0.3f)]
        [TestCase(BlipStatus.Normal, 10f, 0.25f)]  // clamp low
        [TestCase(BlipStatus.Normal, 1f, 0.9f)]    // 1.2 clamped high
        [TestCase(BlipStatus.Normal, 0.45f, 0.9f)]
        [TestCase(BlipStatus.Normal, 2f, 0.6f)]
        [TestCase(BlipStatus.Lagging, 1f, 0.8f)]
        [TestCase(BlipStatus.Lagging, 4f, 0.5f)]   // clamp low
        [TestCase(BlipStatus.Lagging, 0.3f, 1.5f)] // clamp high
        [TestCase(BlipStatus.Lagging, 0.64f, 1.25f)]
        public void StepIntervalClamps(BlipStatus status, float speed, float expected)
        {
            Assert.That(BlipSoundModel.StepInterval(status, speed), Is.EqualTo(expected).Within(1e-5));
        }

        // --- Footsteps --------------------------------------------------------------------------

        [Test]
        public void NormalFootstepsUseHysteresis()
        {
            var m = new BlipSoundModel(new Random(1));
            Blip b = B(1, 3f, 0f, 0.45f);
            var blips = new List<Blip> { b };
            Assert.That(Run(m, blips, 3f, 1), Is.Zero, "0.45 m/s has not crossed the 0.5 start threshold");
            b.Speed = 0.5f;
            Assert.That(Run(m, blips, 3f, 1), Is.Zero, "start needs v > 0.5");
            b.Speed = 0.6f;
            Assert.That(Run(m, blips, 3f, 1), Is.InRange(2, 4), "interval 0.9 s");
            b.Speed = 0.45f;
            Assert.That(Run(m, blips, 3f, 1), Is.InRange(3, 4), "still stepping above the 0.4 stop threshold");
            b.Speed = 0.39f;
            Assert.That(Run(m, blips, 3f, 1), Is.Zero, "stopped below 0.4");
            b.Speed = 0.45f;
            Assert.That(Run(m, blips, 3f, 1), Is.Zero, "does not restart below 0.5");
        }

        [Test]
        public void LaggingFootstepsUseHysteresis()
        {
            var m = new BlipSoundModel(new Random(2));
            Blip b = B(1, 3f, 0f, 0.18f, BlipStatus.Lagging);
            var blips = new List<Blip> { b };
            Assert.That(Run(m, blips, 4f, 1), Is.Zero);
            b.Speed = 0.25f;
            Assert.That(Run(m, blips, 4.5f, 1), Is.InRange(2, 3), "interval 1.5 s (clamped)");
            b.Speed = 0.17f;
            Assert.That(Run(m, blips, 4.5f, 1), Is.InRange(2, 3), "still stepping above 0.15");
            b.Speed = 0.14f;
            Assert.That(Run(m, blips, 4f, 1), Is.Zero);
        }

        [Test]
        public void StepsAreSpacedByTheIntervalAndCarryVariantSampleAndPitch()
        {
            var m = new BlipSoundModel(new Random(3));
            var blips = new List<Blip> { B(7, 3f, 0f, 4f) };
            const float dt = 0.001f;
            var times = new List<float>();
            var samples = new bool[4];
            for (int i = 0; i < 20000; i++)
            {
                m.Update(dt, blips, 0f, 0f, Clear);
                for (int s = 0; s < m.Steps.Count; s++)
                {
                    StepEvent e = m.Steps[s];
                    times.Add(i * dt);
                    Assert.That(e.Handle, Is.EqualTo(7));
                    Assert.That(e.Variant, Is.EqualTo(StepVariant.Normal));
                    Assert.That(e.Sample, Is.InRange(0, 3));
                    Assert.That(e.Pitch, Is.InRange(0.95f, 1.05f));
                    Assert.That(e.Occluded, Is.False);
                    Assert.That(e.GainDb, Is.EqualTo(-20f - 20f / 18f).Within(1e-4)); // 3 m
                    samples[e.Sample] = true;
                }
            }
            Assert.That(times.Count, Is.InRange(65, 67), "20 s / 0.3 s");
            for (int i = 1; i < times.Count; i++) Assert.That(times[i] - times[i - 1], Is.EqualTo(0.3f).Within(0.0021f));
            Assert.That(samples, Is.All.True, "all four samples get used");
        }

        [Test]
        public void LaggingStepsUseTheLaggingVariant()
        {
            var m = new BlipSoundModel(new Random(4));
            var blips = new List<Blip> { B(1, 1f, 0f, 1f, BlipStatus.Lagging) };
            bool any = false;
            for (int i = 0; i < 200; i++)
            {
                m.Update(0.01f, blips, 0f, 0f, Clear);
                for (int s = 0; s < m.Steps.Count; s++)
                {
                    Assert.That(m.Steps[s].Variant, Is.EqualTo(StepVariant.Lagging));
                    Assert.That(m.Steps[s].GainDb, Is.EqualTo(-20f));
                    any = true;
                }
            }
            Assert.That(any, Is.True);
        }

        [Test]
        public void FirstStepDelayIsRandomInZeroToInterval()
        {
            const float dt = 0.001f;
            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            const int runs = 1000;
            for (int seed = 0; seed < runs; seed++)
            {
                var m = new BlipSoundModel(new Random(seed));
                var blips = new List<Blip> { B(1, 2f, 0f, 4f) }; // interval 0.3 s
                float first = -1f;
                for (int i = 0; i < 400 && first < 0f; i++)
                {
                    m.Update(dt, blips, 0f, 0f, Clear);
                    if (m.Steps.Count > 0) first = i * dt;
                }
                Assert.That(first, Is.InRange(0f, 0.3f), $"seed {seed}");
                min = Math.Min(min, first);
                max = Math.Max(max, first);
                sum += first;
            }
            Assert.That(min, Is.LessThan(0.02f));
            Assert.That(max, Is.GreaterThan(0.28f));
            Assert.That(sum / runs, Is.EqualTo(0.15f).Within(0.015f));
        }

        [Test]
        public void StepsStopBeyondTwentyMetres()
        {
            var m = new BlipSoundModel(new Random(5));
            var blips = new List<Blip> { B(1, 20.5f, 0f, 4f) };
            Assert.That(Run(m, blips, 3f, 1), Is.Zero);
            Assert.That(m.IsVoiced(1), Is.True, "voiced but silent");
        }

        [Test]
        public void LongFrameDoesNotBurstSteps()
        {
            var m = new BlipSoundModel(new Random(6));
            var blips = new List<Blip> { B(1, 2f, 0f, 4f) };
            m.Update(0.01f, blips, 0f, 0f, Clear);
            m.Update(2f, blips, 0f, 0f, Clear);
            Assert.That(m.Steps.Count, Is.LessThanOrEqualTo(1));
        }

        // --- Breathing --------------------------------------------------------------------------

        [Test]
        public void BreathingVariantsLevelsAndRanges()
        {
            var m = new BlipSoundModel(new Random(7));
            Blip b = B(1, 2f, 0f, 0f);
            var blips = new List<Blip> { b };

            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.Breath, Is.EqualTo(BreathVariant.Calm));
            Assert.That(VoiceFor(m, 1).Value.BreathGainDb, Is.EqualTo(-30f));

            b.X = 6.5f; // Calm range 6 m
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.Breath, Is.EqualTo(BreathVariant.None));
            Assert.That(float.IsNegativeInfinity(VoiceFor(m, 1).Value.BreathGainDb), Is.True);

            b.Speed = 1f; // Heavy, range 8 m
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.Breath, Is.EqualTo(BreathVariant.Heavy));
            Assert.That(VoiceFor(m, 1).Value.BreathGainDb, Is.EqualTo(-24f + (-16f) * 4.5f / 6f).Within(1e-4));
            b.X = 8.5f;
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.Breath, Is.EqualTo(BreathVariant.None));

            b.Status = BlipStatus.Lagging; // Ragged, range 10 m
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.Breath, Is.EqualTo(BreathVariant.Ragged));
            b.X = 1f;
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.BreathGainDb, Is.EqualTo(-24f));
            b.X = 10.5f;
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(VoiceFor(m, 1).Value.Breath, Is.EqualTo(BreathVariant.None));
        }

        [Test]
        public void CalmHeavySwitchUsesHysteresis()
        {
            var m = new BlipSoundModel(new Random(8));
            Blip b = B(1, 2f, 0f, 0.45f);
            var blips = new List<Blip> { b };
            BreathVariant At(float speed)
            {
                b.Speed = speed;
                m.Update(0.01f, blips, 0f, 0f, Clear);
                return VoiceFor(m, 1).Value.Breath;
            }
            Assert.That(At(0.45f), Is.EqualTo(BreathVariant.Calm));
            Assert.That(At(0.5f), Is.EqualTo(BreathVariant.Calm));
            Assert.That(At(0.51f), Is.EqualTo(BreathVariant.Heavy));
            Assert.That(At(0.45f), Is.EqualTo(BreathVariant.Heavy));
            Assert.That(At(0.4f), Is.EqualTo(BreathVariant.Heavy));
            Assert.That(At(0.39f), Is.EqualTo(BreathVariant.Calm));
            Assert.That(At(0.45f), Is.EqualTo(BreathVariant.Calm));
        }

        // --- Voices -----------------------------------------------------------------------------

        [Test]
        public void OnlyTheFourNearestAreVoicedWithOneMetreHysteresis()
        {
            var m = new BlipSoundModel(new Random(9));
            var blips = new List<Blip>();
            for (int i = 1; i <= 6; i++) blips.Add(B(i, i, 0f, 0f)); // handle i at i metres
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(m.Voices.Count, Is.EqualTo(4));
            for (int i = 1; i <= 6; i++) Assert.That(m.IsVoiced(i), Is.EqualTo(i <= 4), $"handle {i}");

            blips[4].X = 3.5f; // handle 5 now 0.5 m nearer than handle 4: not enough
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(m.IsVoiced(4), Is.True);
            Assert.That(m.IsVoiced(5), Is.False);

            blips[4].X = 3f; // 1 m nearer than handle 4: takes its voice
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(m.IsVoiced(4), Is.False);
            Assert.That(m.IsVoiced(5), Is.True);
            Assert.That(m.Voices.Count, Is.EqualTo(4));

            blips[4].X = 4.5f; // now farther than handle 4 but not by 1 m: keeps the voice
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(m.IsVoiced(5), Is.True);
            Assert.That(m.IsVoiced(4), Is.False);

            blips.RemoveAt(0); // handle 1 despawns: the free voice goes to the nearest unvoiced (handle 4)
            m.Update(0.01f, blips, 0f, 0f, Clear);
            Assert.That(m.IsVoiced(1), Is.False);
            Assert.That(m.IsVoiced(4), Is.True);
            Assert.That(m.IsVoiced(5), Is.True);
            Assert.That(m.IsVoiced(6), Is.False);
        }

        [Test]
        public void UnvoicedInstancesEmitNoStepsAndNoBreathing()
        {
            var m = new BlipSoundModel(new Random(10));
            var blips = new List<Blip>();
            for (int i = 1; i <= 7; i++) blips.Add(B(i, i, 0f, 4f));
            var stepped = new int[8];
            for (int f = 0; f < 300; f++)
            {
                m.Update(0.01f, blips, 0f, 0f, Clear);
                Assert.That(m.Voices.Count, Is.EqualTo(4));
                for (int v = 0; v < m.Voices.Count; v++) Assert.That(m.Voices[v].Handle, Is.InRange(1, 4));
                for (int s = 0; s < m.Steps.Count; s++) stepped[m.Steps[s].Handle]++;
            }
            for (int i = 1; i <= 4; i++) Assert.That(stepped[i], Is.GreaterThan(0), $"handle {i}");
            for (int i = 5; i <= 7; i++) Assert.That(stepped[i], Is.Zero, $"handle {i}");
        }

        [Test]
        public void OccludedInstancesAreFourDbQuieterAndFlagged()
        {
            var m = new BlipSoundModel(new Random(11));
            var blips = new List<Blip> { B(1, 5f, 0f, 4f), B(2, -5f, 0f, 4f) };
            Func<int, bool> occ = h => h == 2;
            float? step1 = null, step2 = null;
            for (int f = 0; f < 200; f++)
            {
                m.Update(0.01f, blips, 0f, 0f, occ);
                for (int s = 0; s < m.Steps.Count; s++)
                {
                    StepEvent e = m.Steps[s];
                    if (e.Handle == 1) { step1 = e.GainDb; Assert.That(e.Occluded, Is.False); }
                    else { step2 = e.GainDb; Assert.That(e.Occluded, Is.True); }
                }
            }
            Assert.That(step1.HasValue && step2.HasValue, Is.True);
            Assert.That(step1.Value - step2.Value, Is.EqualTo(4f).Within(1e-5));
            VoiceState v1 = VoiceFor(m, 1).Value, v2 = VoiceFor(m, 2).Value;
            Assert.That(v1.Occluded, Is.False);
            Assert.That(v2.Occluded, Is.True);
            Assert.That(v1.BreathGainDb - v2.BreathGainDb, Is.EqualTo(4f).Within(1e-5));
        }

        [Test]
        public void DespawnedInstanceGoesSilentAndLosesItsVoice()
        {
            var m = new BlipSoundModel(new Random(12));
            var blips = new List<Blip> { B(1, 2f, 0f, 4f) };
            Run(m, blips, 1f, 1);
            Assert.That(m.IsVoiced(1), Is.True);
            blips.Clear();
            Assert.That(Run(m, blips, 1f, 1), Is.Zero);
            Assert.That(m.Voices.Count, Is.Zero);
            Assert.That(m.IsVoiced(1), Is.False);
        }

        // --- Decoy parity -----------------------------------------------------------------------

        [Test]
        public void IdenticalMotionGivesIdenticalTimingDistributionsAndGains()
        {
            // A "real" and a "decoy" instance: same distance, speed and status, different handles and list slots.
            const float dt = 0.002f;
            const int runs = 1000;
            float sumA = 0f, sumB = 0f;
            int countA = 0, countB = 0;
            var histA = new int[10];
            var histB = new int[10];
            for (int seed = 0; seed < runs; seed++)
            {
                var m = new BlipSoundModel(new Random(seed));
                var blips = new List<Blip> { B(11, 6f, 0f, 4f), B(42, 0f, -6f, 4f) };
                float firstA = -1f, firstB = -1f;
                float lastA = -1f, lastB = -1f;
                for (int i = 0; i < 1000; i++) // 2 s
                {
                    m.Update(dt, blips, 0f, 0f, Clear);
                    float t = i * dt;
                    for (int s = 0; s < m.Steps.Count; s++)
                    {
                        StepEvent e = m.Steps[s];
                        Assert.That(e.GainDb, Is.EqualTo(-24.4444447f).Within(1e-4));
                        if (e.Handle == 11)
                        {
                            if (firstA < 0f) firstA = t;
                            else Assert.That(t - lastA, Is.EqualTo(0.3f).Within(dt + 1e-4));
                            lastA = t;
                            countA++;
                        }
                        else
                        {
                            if (firstB < 0f) firstB = t;
                            else Assert.That(t - lastB, Is.EqualTo(0.3f).Within(dt + 1e-4));
                            lastB = t;
                            countB++;
                        }
                    }
                }
                sumA += firstA;
                sumB += firstB;
                histA[Math.Min(9, (int)(firstA / 0.03f))]++;
                histB[Math.Min(9, (int)(firstB / 0.03f))]++;
                VoiceState va = VoiceFor(m, 11).Value, vb = VoiceFor(m, 42).Value;
                Assert.That(va.Breath, Is.EqualTo(vb.Breath));
                Assert.That(va.BreathGainDb, Is.EqualTo(vb.BreathGainDb));
            }
            Assert.That(sumA / runs, Is.EqualTo(sumB / runs).Within(0.015f), "first-step phase distribution");
            Assert.That(Math.Abs(countA - countB), Is.LessThan(runs / 10), "step counts");
            for (int bin = 0; bin < 10; bin++)
            {
                Assert.That(histA[bin], Is.InRange(50, 150), $"A bin {bin}");
                Assert.That(histB[bin], Is.InRange(50, 150), $"B bin {bin}");
            }
        }

        [Test]
        public void SteadyStateUpdateDoesNotAllocate()
        {
            var m = new BlipSoundModel(new Random(13));
            var blips = new List<Blip>();
            for (int i = 1; i <= 8; i++) blips.Add(B(i, i * 2f, 1f, i % 2 == 0 ? 4f : 0f, i == 3 ? BlipStatus.Lagging : BlipStatus.Normal));
            Func<int, bool> occ = h => (h & 1) == 0;
            for (int f = 0; f < 100; f++) m.Update(0.016f, blips, 0f, 0f, occ);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 1000; f++) m.Update(0.016f, blips, 0f, 0f, occ);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThan(4096));
        }

        // --- Synth ------------------------------------------------------------------------------

        [TestCase(44100)]
        [TestCase(48000)]
        public void StepSamplesAreShortQuietAndClickFree(int rate)
        {
            for (int v = 0; v < 4; v++)
            {
                float[] s = SurvivorSoundSynth.Step(v, rate);
                Assert.That(s.Length / (float)rate, Is.InRange(0.08f, 0.12f), $"variant {v}");
                AssertQuietAndClickFree(s, SurvivorSoundSynth.StepPeak);
            }
            Assert.That(SurvivorSoundSynth.Step(0, rate), Is.Not.EqualTo(SurvivorSoundSynth.Step(1, rate)));
        }

        [TestCase(44100)]
        [TestCase(48000)]
        public void LaggingStepsAreLongerScuffs(int rate)
        {
            for (int v = 0; v < 4; v++)
            {
                float[] s = SurvivorSoundSynth.LaggingStep(v, rate);
                Assert.That(s.Length / (float)rate, Is.InRange(0.25f, 0.45f), $"variant {v}");
                Assert.That(s.Length, Is.GreaterThan(SurvivorSoundSynth.Step(v, rate).Length));
                AssertQuietAndClickFree(s, SurvivorSoundSynth.LaggingStepPeak);
            }
        }

        [TestCase(BreathVariant.Calm)]
        [TestCase(BreathVariant.Heavy)]
        [TestCase(BreathVariant.Ragged)]
        public void BreathLoopsAreSeamless(BreathVariant variant)
        {
            const int rate = 44100;
            float[] s = SurvivorSoundSynth.Breath(variant, rate);
            Assert.That(s.Length / (float)rate, Is.InRange(2f, 3.05f));
            AssertQuietAndClickFree(s, SurvivorSoundSynth.BreathPeak);
            Assert.That(Math.Abs(s[s.Length - 1] - s[0]), Is.LessThan(1e-3f), "loop seam");
        }

        [Test]
        public void NoBreathIsEmpty()
        {
            Assert.That(SurvivorSoundSynth.Breath(BreathVariant.None, 44100).Length, Is.Zero);
        }

        private static void AssertQuietAndClickFree(float[] s, float peak)
        {
            Assert.That(Math.Abs(s[0]), Is.LessThan(1e-4f), "starts silent");
            Assert.That(Math.Abs(s[s.Length - 1]), Is.LessThan(1e-3f), "ends silent");
            float max = 0f;
            foreach (float x in s)
            {
                Assert.That(float.IsNaN(x), Is.False);
                max = Math.Max(max, Math.Abs(x));
            }
            Assert.That(max, Is.LessThanOrEqualTo(peak + 1e-5f));
            Assert.That(max, Is.GreaterThan(peak * 0.5f), "not silent");
        }
    }
}
