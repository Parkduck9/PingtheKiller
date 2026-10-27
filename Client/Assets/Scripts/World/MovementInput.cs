using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // One instance per connection, so Seq never restarts while the server keeps its last processed Seq.
    // Held: one send per 30 Hz slot (unscaled dt accumulator, at most one per frame, missed slots dropped).
    // Release: zero now and one backup zero at the next slot, then silence. Hard cap: 31 sends per sliding second.
    public sealed class MovementInput
    {
        public const double Interval = 1.0 / 30;
        public const int MaxPerSecond = 31;
        private readonly double[] history = new double[MaxPerSecond];
        private int oldest;
        private uint sequence;
        private bool wasHeld;
        private int zerosPending;
        private double clock, slot;

        public MovementInput()
        {
            for (int i = 0; i < history.Length; i++) history[i] = double.NegativeInfinity;
        }

        public bool TryCreate(bool held, float right, float forward, float yaw, double dt, out C2S_MoveInput packet)
        {
            packet = null;
            if (!(dt > 0) || double.IsInfinity(dt)) dt = 0;
            clock += dt;
            if (held)
            {
                zerosPending = 0;
                if (wasHeld) slot += dt; else slot = Interval; // a fresh press is due at once
            }
            else if (wasHeld) { zerosPending = 2; slot = Interval; }
            else if (zerosPending > 0) slot += dt;
            wasHeld = held;
            if (!held && zerosPending == 0) return false;
            if (slot < Interval) return false;
            if (clock - history[oldest] <= 1) { slot = Interval; return false; }
            slot -= Interval;
            if (slot >= Interval) slot = 0; // a hitch skipped slots: drop them instead of bursting
            if (!held) zerosPending--;
            history[oldest] = clock; oldest = (oldest + 1) % history.Length;
            Vector3 direction = held ? ClientMath.InputDirection(right, forward, yaw) : Vector3.zero;
            packet = new C2S_MoveInput { DirX = direction.x, DirZ = direction.z, Yaw = yaw, Seq = ++sequence };
            return true;
        }

        // Legacy per-call form: each call counts as exactly one 30 Hz slot.
        public bool TryCreate(bool held, float right, float forward, float yaw, out C2S_MoveInput packet)
            => TryCreate(held, right, forward, yaw, Interval, out packet);

        // Match end: one unconditional zero with the next Seq, then silence until a new press. It bypasses the
        // 31/s cap on purpose (one extra packet stays far under the server's 60/s limit) but is counted in the window.
        public C2S_MoveInput Final(float yaw)
        {
            ResetKeys();
            history[oldest] = clock; oldest = (oldest + 1) % history.Length;
            return new C2S_MoveInput { Yaw = yaw, Seq = ++sequence };
        }

        // New match on the same connection: forget key state, keep Seq.
        public void ResetKeys() { wasHeld = false; zerosPending = 0; slot = 0; }
    }
}
