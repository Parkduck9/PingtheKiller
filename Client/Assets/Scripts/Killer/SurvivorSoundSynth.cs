using System;

namespace PingTheKiller.Client.Killer
{
    // Placeholder Survivor sounds (HUD_SPEC §6.5.1) as pure float[] generators; B wraps them into AudioClips.
    // Quiet, dull (low-passed noise), and every buffer starts and ends at exactly 0 so there are no clicks.
    // Noise comes from a fixed per-variant seed: deterministic audio content, unrelated to wire data.
    public static class SurvivorSoundSynth
    {
        public const int VariantCount = 4;
        public const float StepPeak = 0.25f;
        public const float LaggingStepPeak = 0.2f;
        public const float BreathPeak = 0.12f;

        // Short noise-burst footstep, 85-115 ms depending on variant (0..3).
        public static float[] Step(int variant, int sampleRate)
        {
            int v = Wrap(variant);
            float duration = 0.085f + 0.01f * v;
            int n = Length(duration, sampleRate);
            var buffer = new float[n];
            uint seed = 0x9E3779B9u ^ (uint)(v * 7919 + 17);
            float a1 = LowPassCoef(900f + 150f * v, sampleRate);
            float a2 = LowPassCoef(1400f, sampleRate);
            float thumpHz = 70f + 6f * v;
            float attack = 0.004f * sampleRate;
            float tau = (0.022f + 0.004f * v) * sampleRate;
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float noise = NextNoise(ref seed);
                y1 += a1 * (noise - y1);
                y2 += a2 * (y1 - y2);
                float t = (float)i / sampleRate;
                float thump = 0.6f * (float)Math.Sin(2.0 * Math.PI * thumpHz * t);
                float env = Math.Min(1f, i / attack) * (float)Math.Exp(-i / tau);
                buffer[i] = (y2 * 3f + thump) * env;
            }
            ApplyEdgeFade(buffer, 0.003f * sampleRate);
            Normalize(buffer, StepPeak);
            return buffer;
        }

        // Longer dragging scuff, 300-390 ms depending on variant (0..3).
        public static float[] LaggingStep(int variant, int sampleRate)
        {
            int v = Wrap(variant);
            float duration = 0.30f + 0.03f * v;
            int n = Length(duration, sampleRate);
            var buffer = new float[n];
            uint seed = 0x85EBCA6Bu ^ (uint)(v * 104729 + 3);
            float a1 = LowPassCoef(600f + 80f * v, sampleRate);
            float a2 = LowPassCoef(900f, sampleRate);
            float grainHz = 24f + 5f * v;
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float noise = NextNoise(ref seed);
                y1 += a1 * (noise - y1);
                y2 += a2 * (y1 - y2);
                float p = (float)i / (n - 1);
                // Skewed swell: rises over the first ~35 %, drags out over the rest.
                float swell = p < 0.35f ? (float)Math.Sin(0.5 * Math.PI * p / 0.35f) : (float)Math.Cos(0.5 * Math.PI * (p - 0.35f) / 0.65f);
                float grain = 0.7f + 0.3f * (float)Math.Sin(2.0 * Math.PI * grainHz * i / sampleRate);
                buffer[i] = y2 * swell * swell * grain;
            }
            ApplyEdgeFade(buffer, 0.005f * sampleRate);
            Normalize(buffer, LaggingStepPeak);
            return buffer;
        }

        // Seamless breathing loop, 2.2-3.0 s. None returns an empty buffer.
        public static float[] Breath(BreathVariant variant, int sampleRate)
        {
            float period;
            float[] puffs; // (start, end, amplitude) triples in seconds, all strictly inside (0, period)
            float cutoff;
            float tremoloHz = 0f;
            uint seed;
            switch (variant)
            {
                case BreathVariant.Calm:
                    period = 3.0f; cutoff = 1300f; seed = 0xC2B2AE35u;
                    puffs = new[] { 0.15f, 1.25f, 0.6f, 1.5f, 2.8f, 1.0f };
                    break;
                case BreathVariant.Heavy:
                    period = 2.4f; cutoff = 1600f; seed = 0x27D4EB2Fu;
                    puffs = new[] { 0.05f, 0.5f, 0.7f, 0.6f, 1.15f, 1.0f, 1.25f, 1.7f, 0.7f, 1.8f, 2.35f, 1.0f };
                    break;
                case BreathVariant.Ragged:
                    period = 2.2f; cutoff = 1500f; seed = 0x165667B1u; tremoloHz = 9f;
                    puffs = new[] { 0.05f, 0.35f, 0.8f, 0.42f, 0.8f, 1.0f, 0.95f, 1.15f, 0.6f, 1.25f, 1.7f, 1.0f, 1.8f, 2.12f, 0.7f };
                    break;
                default:
                    return new float[0];
            }

            int n = Length(period, sampleRate);
            var buffer = new float[n];
            float a1 = LowPassCoef(cutoff, sampleRate);
            float a2 = LowPassCoef(cutoff * 1.5f, sampleRate);
            float hp = 0f, y1 = 0f, y2 = 0f;
            float aHp = LowPassCoef(250f, sampleRate);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float noise = NextNoise(ref seed);
                hp += aHp * (noise - hp);
                float band = noise - hp; // remove rumble, keep a hiss
                y1 += a1 * (band - y1);
                y2 += a2 * (y1 - y2);
                float env = 0f;
                for (int p = 0; p + 2 < puffs.Length; p += 3)
                {
                    float s = puffs[p], e = puffs[p + 1];
                    if (t <= s || t >= e) continue;
                    float x = (float)Math.Sin(Math.PI * (t - s) / (e - s));
                    env += puffs[p + 2] * x * x;
                }
                if (tremoloHz > 0f) env *= 0.75f + 0.25f * (float)Math.Sin(2.0 * Math.PI * tremoloHz * t);
                buffer[i] = y2 * env;
            }
            Normalize(buffer, BreathPeak);
            return buffer;
        }

        private static int Wrap(int variant) => ((variant % VariantCount) + VariantCount) % VariantCount;

        private static int Length(float seconds, int sampleRate)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            return Math.Max(2, (int)Math.Round(seconds * sampleRate));
        }

        private static float LowPassCoef(float cutoffHz, int sampleRate) =>
            1f - (float)Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate);

        // xorshift32 -> [-1, 1)
        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) * (2f / 16777216f) - 1f;
        }

        // Linear fade-in from exactly 0 and fade-out to exactly 0 over `samples`.
        private static void ApplyEdgeFade(float[] buffer, float samples)
        {
            int n = buffer.Length;
            int fade = Math.Max(1, Math.Min(n / 2, (int)samples));
            for (int i = 0; i < fade; i++)
            {
                float g = (float)i / fade;
                buffer[i] *= g;
                buffer[n - 1 - i] *= g;
            }
        }

        private static void Normalize(float[] buffer, float peak)
        {
            float max = 0f;
            for (int i = 0; i < buffer.Length; i++) max = Math.Max(max, Math.Abs(buffer[i]));
            if (max <= 0f) return;
            float g = peak / max;
            for (int i = 0; i < buffer.Length; i++) buffer[i] *= g;
        }
    }
}
