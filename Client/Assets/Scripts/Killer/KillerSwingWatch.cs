using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Killer
{
    // LateHit: the swing was already shown as Missed, but the own flag's length says it hit (an eliminating hit on a
    // Survivor this client never saw). Counted, logged; nothing more is shown.
    public enum SwingEvent : byte { None, Started, Hit, Missed, LateHit }

    // R1 (11/17): the own swing as the Killer client sees it. Pure (no UnityEngine).
    // - Predict: a press was sent; the client plays the swing sweep and whoosh at once (a2) unless a swing is already up
    //   or a press was predicted within PredictGapSeconds (the server takes one swing at a time).
    // - Started: the own Killer entry's ChaseFlags.Swinging rose (the server accepted a press and the wind-up runs).
    // - Hit: a CaptureConfirm (the tracker saw a Normal blip turn Lagging) while a swing is pending; the U8 kick, impact
    //   and hit-stop play from the CaptureConfirm path as before. Never before the server's result is visible.
    // - a2, eliminating hit: the third capture removes the entry, so no CaptureConfirm comes. When a Normal blip within
    //   BlipTracker.SwingCaptureRadius vanishes during a pending swing (OnVanish), the verdict waits for the flag's fall:
    //   the server holds Swinging through the recovery, hit = wind-up + SwingHitRecoverySeconds (16 ticks), miss =
    //   wind-up + SwingMissRecoverySeconds (20 ticks), a stun interrupt <= the wind-up (4 ticks). The length in server
    //   ticks (WorldState.Tick, exact on the wire) decides; a fall within HitTicksMax ticks (and over the wind-up) is a hit.
    // - Missed: no CaptureConfirm within ResultSeconds of the start and no vanish (the wind-up plus two snapshots of
    //   slack), or a held verdict whose flag fell at miss length (or never within HoldSeconds).
    public sealed class KillerSwingWatch
    {
        public const double TickSeconds = 0.05;
        public const double ResultSeconds = GameConstants.SwingWindUpSeconds + .35;
        public const double HoldSeconds = GameConstants.SwingWindUpSeconds + GameConstants.SwingMissRecoverySeconds + .5;
        public const double PredictGapSeconds = .4;
        public static readonly int WindUpTicks = Ticks(GameConstants.SwingWindUpSeconds);
        public static readonly int HitTicks = Ticks(GameConstants.SwingWindUpSeconds + GameConstants.SwingHitRecoverySeconds);
        public static readonly int MissTicks = Ticks(GameConstants.SwingWindUpSeconds + GameConstants.SwingMissRecoverySeconds);
        // Midway between the hit and the miss length.
        public static readonly int HitTicksMax = (HitTicks + MissTicks) / 2;

        private bool swinging, pending, held;
        private uint riseTick;
        private SwingEvent result;
        private double startedAt = double.NegativeInfinity, predictedAt = double.NegativeInfinity;
        public int Swings { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public bool Pending => pending;
        public bool Held => held;

        private static int Ticks(double seconds) => (int)System.Math.Round(seconds / TickSeconds);

        // The own flag's length in server ticks: a hit's recovery (not a miss, not an interrupted wind-up).
        public static bool HitLength(int ticks) => ticks > WindUpTicks && ticks <= HitTicksMax;

        public void Reset()
        {
            swinging = pending = held = false; riseTick = 0; result = SwingEvent.None;
            startedAt = predictedAt = double.NegativeInfinity;
            Swings = Hits = Misses = 0;
        }

        // A press was sent at `now`: true when the client should play the swing feedback at once.
        public bool Predict(double now)
        {
            if (swinging || (now >= predictedAt && now - predictedAt < PredictGapSeconds)) return false;
            predictedAt = now;
            return true;
        }

        // Every WorldState: the own entry's Swinging flag and the snapshot's server tick, at receive time.
        public SwingEvent Observe(bool swingingFlag, uint tick, double now)
        {
            if (swingingFlag && !swinging)
            {
                swinging = true; riseTick = tick;
                if (pending) { pending = false; Misses++; } // a new swing settles a still-open one as a miss
                pending = true; held = false; result = SwingEvent.None; startedAt = now; Swings++;
                return SwingEvent.Started;
            }
            if (swingingFlag || !swinging) return SwingEvent.None;
            swinging = false;
            bool hit = HitLength(unchecked((int)(tick - riseTick)));
            if (pending)
            {
                pending = held = false;
                if (hit) { Hits++; return result = SwingEvent.Hit; }
                Misses++;
                return result = SwingEvent.Missed;
            }
            if (result == SwingEvent.Missed && hit) { Misses--; Hits++; result = SwingEvent.Hit; return SwingEvent.LateHit; }
            return SwingEvent.None;
        }

        // A CaptureConfirm of the own tracker.
        public SwingEvent OnCapture(double now)
        {
            if (!pending || now < startedAt) return SwingEvent.None;
            pending = held = false; Hits++;
            return result = SwingEvent.Hit;
        }

        // a2: a Normal blip within SwingCaptureRadius vanished in this snapshot (BlipTracker.NormalVanishedNear).
        public void OnVanish(double now)
        {
            if (pending && now >= startedAt) held = true;
        }

        // Every frame: Missed once the result window passed without a capture (a held verdict waits for the flag's fall).
        public SwingEvent Tick(double now)
        {
            if (!pending || now - startedAt < (held ? HoldSeconds : ResultSeconds)) return SwingEvent.None;
            pending = held = false; Misses++;
            return result = SwingEvent.Missed;
        }
    }
}
