using System;

namespace PingTheKiller.Client.Audio
{
    // Shutter and Rail cues (HUD_SPEC 15, Q5): ShutterSlam when a Shutter goes Down, ShutterCrunch when it breaks, VaultThud
    // when anyone the viewer can see starts a vault. Procedural like V2CueSynth (download nothing): mono, fixed seeds,
    // deterministic, amplitude 0.4 = the 0 dB reference, every clip starts and ends at exactly 0, nothing reaches 0.95.
    public enum ChasePropCue : byte { ShutterSlam, ShutterCrunch, VaultThud }

    public static class ChasePropSynth
    {
        public const float Reference = V2CueSynth.Reference;
        public const float EdgeSeconds = V2CueSynth.EdgeSeconds;
        // Positional range (metres): full level up to 2 m, then the SkillFeedback.Attenuation curve to -40 dB here.
        public const float SlamRange = 30f, CrunchRange = 30f, ThudRange = 14f;

        public static float Seconds(ChasePropCue cue)
            => cue == ChasePropCue.ShutterSlam ? .7f : cue == ChasePropCue.ShutterCrunch ? .45f : .16f;

        public static float Range(ChasePropCue cue)
            => cue == ChasePropCue.ShutterSlam ? SlamRange : cue == ChasePropCue.ShutterCrunch ? CrunchRange : ThudRange;

        public static int Length(ChasePropCue cue, int rate) => Math.Max(2, (int)(rate * Seconds(cue)));

        public static float[] Build(ChasePropCue cue, int rate)
        {
            int n = Length(cue, rate);
            var s = new float[n];
            var noise = new Random(71 + (int)cue); // fixed seed: the same sound every time
            float edge = Math.Max(1, rate * EdgeSeconds), held = 0, low = 0;
            double p1 = 0, p2 = 0, p3 = 0, p4 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1), time = (float)i / rate, v;
                switch (cue)
                {
                    case ChasePropCue.ShutterSlam:
                        // A sheet-metal slam: a hard noise knock, an inharmonic ringing plate (110 Hz family) and a rattle
                        // of the slats that dies out over the clip.
                        p1 += 2 * Math.PI * 110 / rate; p2 += 2 * Math.PI * 110 * 2.32 / rate;
                        p3 += 2 * Math.PI * 110 * 4.17 / rate; p4 += 2 * Math.PI * 110 * 6.73 / rate;
                        float plate = (float)(Math.Sin(p1) * Math.Exp(-5 * time) + .6 * Math.Sin(p2) * Math.Exp(-7 * time)
                            + .4 * Math.Sin(p3) * Math.Exp(-11 * time) + .25 * Math.Sin(p4) * Math.Exp(-16 * time));
                        float knock = (float)(noise.NextDouble() * 2 - 1) * (float)Math.Exp(-45 * time);
                        if (i % 220 == 0) held = (float)(noise.NextDouble() * 2 - 1);
                        float rattle = held * (float)(Math.Exp(-6 * time) * (.5 + .5 * Math.Sin(2 * Math.PI * 23 * time)));
                        v = Ramp(time, .0015f) * (.55f * plate + .7f * knock + .18f * rattle);
                        break;
                    case ChasePropCue.ShutterCrunch:
                        // Bending metal: low-passed noise bursts over a falling groan 140 -> 70 Hz.
                        low += .2f * ((float)(noise.NextDouble() * 2 - 1) - low);
                        float bursts = (float)Math.Exp(-8 * time) + .6f * (float)Math.Exp(-30 * Math.Max(0, time - .12f)) * (time >= .12f ? 1 : 0);
                        p1 += 2 * Math.PI * (140 - 70 * t) / rate;
                        float groan = (float)Math.Tanh(2 * Math.Sin(p1)) * (float)Math.Exp(-4 * time);
                        v = Ramp(time, .002f) * (1.1f * low * bursts + .45f * groan);
                        break;
                    default: // VaultThud: a soft low step, 70 Hz with a short noise scuff
                        float thump = Sin(70 * (1 - .3f * t) * time) * (float)Math.Exp(-22 * time);
                        float scuff = (float)(noise.NextDouble() * 2 - 1) * (float)Math.Exp(-70 * time);
                        v = Ramp(time, .003f) * (.85f * thump + .2f * scuff);
                        break;
                }
                float fade = Math.Min(1, Math.Min(i / edge, (n - 1 - i) / edge));
                s[i] = Math.Max(-.94f, Math.Min(.94f, Reference * fade * v));
            }
            return s;
        }

        private static float Sin(float cycles) => (float)Math.Sin(2 * Math.PI * cycles);

        private static float Ramp(float time, float attack) => Math.Min(1, time / attack);
    }
}
