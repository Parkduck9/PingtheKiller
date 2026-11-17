using System;
using System.Globalization;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.UI
{
    public enum ThreatEffect : byte { None, MeterStrip, ScreenEdge }

    // HUD_SPEC 4.2 threat visuals, pure. Source: S2C_WorldState.ThreatLevel (distance only, no direction). Level 1 and 2:
    // a 1-bit `danger` noise strip along the meter's top edge covering 1/3 and 2/3 of its width from the right; level 3:
    // the same mask in a 24 u band on all four screen edges instead. The mask is static and re-rolled once per level
    // change; the cue is hidden while Eliminated or Escaped. Out-of-range levels read as 0, like the beep.
    public sealed class ThreatVisual
    {
        // (10/30 ruling, U4e Q-01) The meter-edge strip is 8 u tall at 45 % density (levels 1-2); the level-3 band keeps 24 u
        // at 30 %. One mask texel is 2 u square in both, so the strip shows four rows of noise.
        public const float Density = .3f, StripDensity = .45f, StripHeight = 8, BandWidth = 24, Cell = 2, StripCell = 2;
        private readonly Random rng;
        public byte Level { get; private set; }
        public int Seed { get; private set; }
        public int Rolls { get; private set; }

        public ThreatVisual(Random random = null) { rng = random ?? new Random(); }

        public static byte Effective(byte threat, PlayerStatus status)
            => status == PlayerStatus.Eliminated || status == PlayerStatus.Escaped || threat > 3 ? (byte)0 : threat;

        public ThreatEffect Effect => Level == 0 ? ThreatEffect.None : Level == 3 ? ThreatEffect.ScreenEdge : ThreatEffect.MeterStrip;
        public float StripFraction => Level == 1 ? 1f / 3 : Level == 2 ? 2f / 3 : 0;

        // Each snapshot (and each frame; repeated values change nothing). True when the shown level changed; the mask is
        // re-rolled then, and only then, when the new level shows something.
        public bool Update(byte threat, PlayerStatus status)
        {
            byte level = Effective(threat, status);
            if (level == Level) return false;
            Level = level;
            if (level != 0) { Seed = rng.Next(); Rolls++; }
            return true;
        }

        public void Reset() => Level = 0;

        // The 1-bit mask for a seed: true = a `danger` texel, false = transparent; about `density` of the texels are set
        // (default: the band's Density; the strip passes StripDensity).
        public static bool[] Mask(int seed, int width, int height, float density = Density)
        {
            var mask = new bool[Math.Max(0, width) * Math.Max(0, height)];
            var noise = new Random(seed);
            for (int i = 0; i < mask.Length; i++) mask[i] = noise.NextDouble() < density;
            return mask;
        }
    }

    public enum StatusEvent : byte { None, Captured, Rescued, Eliminated, Escaped }
    public enum EliminationReason : byte { None, Collapse, TimedOut, ThirdCapture }

    // Own-status transitions on a Survivor client (HUD_SPEC 4.1 `captured`, 4.5, (10/30) capture banner and elimination
    // reason), pure. Fed with the own entry of every WorldState. The first entry of a match only sets the baseline, so a
    // reconnect that lands in Lagging plays no capture cue.
    public sealed class CaptureWatch
    {
        public const double BannerSeconds = 3;
        public const float TimeoutAtOrBelow = .2f;
        // Starts with the meter's state word (U4e Q-04), so the banner and the meter name the same state.
        // The seconds left come from the stage (SurvivorReadout.LaggingTotal): 60 s on the first capture, 30 s on the second
        // capture or when the first stage runs out and escalates.
        public static readonly string Banner = BannerText(SurvivorReadout.LaggingTotal(1));
        public static readonly string SecondBanner = BannerText(SurvivorReadout.LaggingTotal(2));
        private static string BannerText(float seconds) => "Lagging. A Survivor within " + Num(GameConstants.RescueRadius) + " m for "
            + Num(GameConstants.RescueTimeSeconds) + " s rescues you " + SurvivorReadout.Dot + " " + Num(seconds) + " s left";
        public static string BannerFor(byte captureCount) => SurvivorReadout.SecondStage(captureCount) ? SecondBanner : Banner;
        // GDD v2 4: no execution. The second Lagging stage running out, a third capture, or the collapse eliminates.
        public const string Collapse = "Disconnected: the collapse reached you.";
        // The server's CaptureCount counts Lagging stages, not captures: a first stage that runs out raises it to 2 without a
        // capture. So the capture elimination names a number only when this watch saw every capture (Caught); otherwise
        // this count-free line, which is true for any history.
        public const string ThirdCapture = "Disconnected: caught with no Lagging stage left.";
        public static readonly string TimedOut = "Disconnected: no rescue in the second Lagging stage (" + Num(GameConstants.SecondLaggingTimeoutSeconds) + " s).";
        private bool known;
        private PlayerStatus last;
        private float lastTimer;
        private byte lastCaptures;
        private double bannerUntil = double.NegativeInfinity;
        private byte bannerCaptures;
        public EliminationReason Reason { get; private set; }
        public int Banners { get; private set; }
        // Normal -> Lagging transitions seen after the baseline, and whether a first stage was seen running out into the
        // second. WholeMatch: the baseline was Normal with CaptureCount 0, so nothing happened before this watch started.
        public int Captures { get; private set; }
        public bool Escalated { get; private set; }
        public bool WholeMatch { get; private set; }
        // Captures including the eliminating one, when the history is known and agrees with the server's count; else null.
        public int? CaughtTimes { get; private set; }

        public void Reset()
        {
            known = false; last = PlayerStatus.Normal; lastTimer = 0; lastCaptures = 0;
            bannerUntil = double.NegativeInfinity; bannerCaptures = 0; Reason = EliminationReason.None; Banners = 0;
            Captures = 0; Escalated = false; WholeMatch = false; CaughtTimes = null;
        }

        // The capture-elimination line: "caught 3 times", or "caught 2 times, no Lagging stage left" after a stage ran out;
        // with no known count, ThirdCapture.
        public static string Caught(int? times, bool escalated)
        {
            if (!times.HasValue || times.Value < 1) return ThirdCapture;
            return "Disconnected: caught " + times.Value.ToString(CultureInfo.InvariantCulture) + (times.Value == 1 ? " time" : " times")
                + (escalated ? ", no Lagging stage left." : ".");
        }

        // collapseRunning: the own collapse clock runs (SelfState.CollapseLeft >= 0), so an elimination without a timeout or
        // a third capture is the collapse.
        public StatusEvent Observe(PlayerStatus status, float statusTimer, byte captureCount, double now, bool collapseRunning = false)
        {
            StatusEvent result = StatusEvent.None;
            if (known && status != last)
            {
                if (status == PlayerStatus.Lagging && last == PlayerStatus.Normal)
                {
                    result = StatusEvent.Captured; bannerUntil = now + BannerSeconds; bannerCaptures = captureCount; Banners++;
                    Captures++;
                }
                else if (status == PlayerStatus.Eliminated)
                {
                    result = StatusEvent.Eliminated;
                    Reason = Infer(last, lastTimer, lastCaptures, collapseRunning);
                    // Before the eliminating capture the server's count is one per capture plus one for a stage that ran out.
                    bool agrees = lastCaptures == Captures + (Escalated ? 1 : 0);
                    CaughtTimes = Reason == EliminationReason.ThirdCapture && WholeMatch && agrees ? Captures + 1 : (int?)null;
                }
                else if (status == PlayerStatus.Normal && last == PlayerStatus.Lagging) result = StatusEvent.Rescued;
                else if (status == PlayerStatus.Escaped) result = StatusEvent.Escaped;
                if (status != PlayerStatus.Lagging) bannerUntil = double.NegativeInfinity;
            }
            else if (known && status == PlayerStatus.Lagging && !SurvivorReadout.SecondStage(lastCaptures) && SurvivorReadout.SecondStage(captureCount))
            {
                // The first stage ran out and the server escalated to the second: the banner again, now with 30 s left.
                bannerUntil = now + BannerSeconds; bannerCaptures = captureCount; Banners++;
                Escalated = true;
            }
            if (!known) WholeMatch = status == PlayerStatus.Normal && captureCount == 0;
            known = true; last = status; lastCaptures = captureCount;
            lastTimer = float.IsNaN(statusTimer) ? 0 : statusTimer;
            return result;
        }

        // Lagging -> Eliminated: the last Lagging StatusTimer at or below 0.2 s means the second stage ran out. Normal ->
        // Eliminated with CaptureCount >= 2: a capture with no Lagging stage left (ThirdCapture). Otherwise, while the collapse runs: the collapse. Anything
        // else (the 900 s timeout) is not inferred.
        public static EliminationReason Infer(PlayerStatus before, float lastTimer, byte captureCount, bool collapseRunning = false)
        {
            if (before == PlayerStatus.Lagging && lastTimer <= TimeoutAtOrBelow) return EliminationReason.TimedOut;
            if (before == PlayerStatus.Normal && captureCount >= GameConstants.EliminatingCaptureCount - 1) return EliminationReason.ThirdCapture;
            return collapseRunning ? EliminationReason.Collapse : EliminationReason.None;
        }

        public bool BannerVisible(double now) => now < bannerUntil;

        // The banner for the stage it was shown for (BannerFor).
        public PromptState Prompt => new PromptState { Text = BannerFor(bannerCaptures), Danger = true };

        // The banner takes the prompt row (bottom-center, never a modal) for BannerSeconds, ahead of every prompt.
        public static PromptState BannerPrompt => new PromptState { Text = Banner, Danger = true };

        // The meter reason and the result line for a reason; null when none was inferred.
        public static string ReasonText(EliminationReason reason)
        {
            switch (reason)
            {
                case EliminationReason.Collapse: return Collapse;
                case EliminationReason.TimedOut: return TimedOut;
                case EliminationReason.ThirdCapture: return ThirdCapture;
                default: return null;
            }
        }

        // This match's line: the capture elimination names the captures this watch saw (Caught).
        public string ReasonText() => Reason == EliminationReason.ThirdCapture ? Caught(CaughtTimes, Escalated) : ReasonText(Reason);

        // HUD_SPEC 4.8 skill_ready: only for a Survivor who can use the skill (Normal); never while Lagging (skill off),
        // Eliminated (spectating) or Escaped.
        public static bool SkillReadyAudible(PlayerStatus status) => status == PlayerStatus.Normal;

        private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public enum SurvivorCue : byte { Captured, Eliminated, ThreatScratch }

    // Procedural placeholder Survivor cues (HUD_SPEC 4.1 `captured`, 4.5 `eliminated`, 4.2 `threat_scratch`). Amplitude 0.4 is
    // the 0 dB cue reference, as on the Killer side.
    public static class SurvivorCueSynth
    {
        public const float Reference = .4f;

        public static float Seconds(SurvivorCue cue) => cue == SurvivorCue.Captured ? .25f : cue == SurvivorCue.Eliminated ? .6f : .2f;

        public static float[] Build(SurvivorCue cue, int rate)
        {
            var s = new float[Math.Max(1, (int)(rate * Seconds(cue)))];
            var noise = new Random(7 + (int)cue); // fixed seed: the same sound every time
            float attack = rate * .003f, low = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = (float)i / s.Length, time = (float)i / rate;
                float edge = Math.Min(1, Math.Min(i / attack, (s.Length - 1 - i) / attack));
                switch (cue)
                {
                    case SurvivorCue.Captured: // low hit: a falling 140 Hz body with a short noise click, 250 ms
                        float body = (float)Math.Sin(2 * Math.PI * 140 * (1 - .3f * t) * time);
                        float click = (float)(noise.NextDouble() * 2 - 1) * (float)Math.Exp(-40 * t);
                        s[i] = Reference * edge * (float)Math.Exp(-5 * t) * (.75f * body + .25f * click);
                        break;
                    case SurvivorCue.Eliminated: // falling tone, 600 ms, 440 -> 165 Hz
                        double phase = 2 * Math.PI * (440 * time - 275 * time * time / (2 * .6));
                        s[i] = Reference * Db(-3) * edge * (1 - t) * (float)Math.Sin(phase);
                        break;
                    default: // threat_scratch: low-passed noise, low, -12 dB, 200 ms
                        low += .08f * ((float)(noise.NextDouble() * 2 - 1) - low);
                        s[i] = Reference * Db(-12) * edge * 3 * low * (1 - t);
                        break;
                }
            }
            return s;
        }

        public static float Db(float db) => (float)Math.Pow(10, db / 20);
    }
}
