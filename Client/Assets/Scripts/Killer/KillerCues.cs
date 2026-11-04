using System;
using System.Collections.Generic;

namespace PingTheKiller.Client.Killer
{
    // HUD_SPEC 6.5 and 3: Killer UI cues. Every trigger is observable in the Killer view.
    public enum KillerCue : byte { MatchStart, BlipFirst, CaptureConfirm, ExecuteConfirm, ExitOpen, SkillUse, SkillReady, Time60 }

    // Pure trigger rules. The caller plays whatever lands in Pending and clears it.
    public sealed class KillerCueRules
    {
        public const double BlipQuietSeconds = 2;
        public readonly List<KillerCue> Pending = new List<KillerCue>();
        private bool known, exitWasOpen, time60Played, cooldownKnown;
        private float lastTime = float.PositiveInfinity, lastCooldown;
        private int lastCount;
        private double quietSince;

        public void Begin(double now)
        {
            Pending.Clear(); known = exitWasOpen = time60Played = cooldownKnown = false;
            lastTime = float.PositiveInfinity; lastCount = 0; quietSince = now; lastCooldown = 0;
            Pending.Add(KillerCue.MatchStart);
        }

        // Each WorldState. exitOpen = any exit Open in the WorldState (protocol v0.4 exit flags).
        public void OnWorld(float timeLeft, bool exitOpen)
        {
            if (known && exitOpen && !exitWasOpen) Pending.Add(KillerCue.ExitOpen);
            if (!time60Played && lastTime > 60 && timeLeft <= 60 && !float.IsInfinity(lastTime)) { time60Played = true; Pending.Add(KillerCue.Time60); }
            if (timeLeft <= 60 && float.IsInfinity(lastTime)) time60Played = true; // joined late: no cue for a crossing nobody saw
            exitWasOpen = exitOpen; lastTime = timeLeft; known = true;
        }

        // Each snapshot: the number of Signal instances. 0 -> at least 1 after 2 s or more with none.
        public void OnSignalCount(int count, double now)
        {
            if (count > 0 && lastCount == 0 && now - quietSince >= BlipQuietSeconds - 1e-9) Pending.Add(KillerCue.BlipFirst);
            if (count == 0 && lastCount > 0) quietSince = now;
            lastCount = count;
        }

        public void OnTrackerEvent(BlipEventKind kind)
        {
            if (kind == BlipEventKind.CaptureConfirm) Pending.Add(KillerCue.CaptureConfirm);
            else if (kind == BlipEventKind.ExecuteConfirm) Pending.Add(KillerCue.ExecuteConfirm);
        }

        public void OnOwnEffect() => Pending.Add(KillerCue.SkillUse);

        // The SelfState clock's ready edge (SelfStateHolder.TakeSkillReady): the own cooldown reached 0, once per cooldown.
        public void OnSkillReady() => Pending.Add(KillerCue.SkillReady);

        // The same rule over sampled values (a cooldown seen going from above 0 to 0).
        public void OnCooldown(float cooldown)
        {
            if (cooldownKnown && lastCooldown > 0 && cooldown <= 0) Pending.Add(KillerCue.SkillReady);
            lastCooldown = cooldown; cooldownKnown = true;
        }
    }

    // Short procedural placeholder tones (sample generators; KillerAudio wraps them into AudioClips).
    // Amplitude 0.4 is the 0 dB reference (capture_confirm); the rest sit at or below it.
    public static class KillerCueSynth
    {
        public const float Reference = .4f;

        public static float[] Build(KillerCue cue, int rate)
        {
            switch (cue)
            {
                case KillerCue.MatchStart: return Tone(rate, .4f, 110, Db(-6), 0);
                case KillerCue.BlipFirst: return Tone(rate, .12f, 1320, Db(-12), 0);
                case KillerCue.CaptureConfirm: return Hit(rate, .2f, 180, 0, 1);
                case KillerCue.ExecuteConfirm: return Hit(rate, .3f, 90, 0, 2);
                case KillerCue.ExitOpen: return Notes(rate, Db(-6), 523.25f, 659.25f, 783.99f);
                case KillerCue.SkillUse: return Tone(rate, .15f, 660, Db(-9), 0);
                case KillerCue.SkillReady: return Tone(rate, .04f, 1760, Db(-15), 0);
                case KillerCue.Time60: return Tone(rate, .25f, 880, Db(-9), 0);
                default: return new float[1];
            }
        }

        public static float Db(float db) => (float)Math.Pow(10, db / 20);

        // Sine with 5 ms attack and an exponential-ish release, so nothing clicks.
        private static float[] Tone(int rate, float seconds, float hz, float gain, int seed)
        {
            var s = new float[Math.Max(1, (int)(rate * seconds))];
            float attack = rate * .005f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / s.Length;
                float env = Math.Min(1, i / attack) * (1 - t) * (1 - t);
                s[i] = Reference * gain * env * (float)Math.Sin(2 * Math.PI * hz * i / rate);
            }
            return s;
        }

        private static float[] Hit(int rate, float seconds, float hz, float gainDb, int seed)
        {
            var random = new Random(seed); // fixed seed: the same sound every time (not wire data)
            var s = new float[Math.Max(1, (int)(rate * seconds))];
            float gain = Db(gainDb), attack = rate * .002f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / s.Length;
                float env = Math.Min(1, i / attack) * (float)Math.Exp(-6 * t);
                float noise = (float)(random.NextDouble() * 2 - 1) * (float)Math.Exp(-30 * t);
                float body = (float)Math.Sin(2 * Math.PI * hz * (1 - .4f * t) * i / rate);
                s[i] = Reference * gain * env * (.7f * body + .3f * noise);
            }
            return s;
        }

        private static float[] Notes(int rate, float gain, params float[] hz)
        {
            int each = rate / 5; // 200 ms per note, 600 ms total
            var s = new float[each * hz.Length];
            float attack = rate * .005f;
            for (int n = 0; n < hz.Length; n++)
                for (int i = 0; i < each; i++)
                {
                    float t = (float)i / each;
                    float env = Math.Min(1, i / attack) * (1 - t);
                    s[n * each + i] = Reference * gain * env * (float)Math.Sin(2 * Math.PI * hz[n] * i / rate);
                }
            return s;
        }
    }
}
