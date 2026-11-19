using System;
using PingTheKiller.Shared.Game;
using MapWall = PingTheKiller.ServerCore.Map.Wall;

namespace PingTheKiller.ServerCore
{
    // R1 (11/17): the Killer swing replaces the automatic proximity capture. A capture happens only at the end of a
    // swing's wind-up. Everything here runs on the game-loop thread.
    //
    //   C2S_Attack (or a bot intent) -> request; the next rules pass starts the swing when the Killer is Normal, not
    //   stunned, vaulting or breaking, and not already swinging. Wind-up SwingWindUpSeconds at half speed; at its end one
    //   hit check (Normal, non-invulnerable Survivors within SwingRange in 2D, within SwingHalfAngleDegrees of the
    //   current look yaw, line of sight through walls and Down shutters clear); the nearest is captured. Recovery: after
    //   a hit 0 % speed, after a miss SwingMissSpeedMultiplier. A shutter stun during the wind-up interrupts the swing with no hit.
    public sealed partial class GameServer
    {
        private const byte SwingNone = 0, SwingWindUp = 1, SwingRecovery = 2;
        // Swing log lines: at most one per player per SwingLogSeconds; the rest are counted on the next line.
        public const double SwingLogSeconds = 0.5;

        // C2S_Attack from a client: stale Seq dropped, then the shared per-second throttle.
        // Server tests only (internal, never reachable through the protocol or the console host): the pre-R1 rule tests
        // walk a human Killer into a Survivor and expect the capture in that tick. With this on, a human Killer that is not
        // swinging and has a Normal, non-invulnerable Survivor within the old CaptureRadius swings at once: the look yaw is
        // aimed at it and the wind-up is skipped. The hit check (range, arc, one capture), the capture consequences and
        // the recovery are the real ones; the 0.2 s delay and the line-of-sight test are not (several legacy QA scenarios
        // teleport bodies into building boxes, which the old contact rule ignored).
        internal bool ContactSwingForTests { get; set; }

        private void ApplyAttack(Player player, uint seq)
        {
            if (player.HasAttackSeq && unchecked((int)(seq - player.AttackSeq)) <= 0) return;
            player.HasAttackSeq = true;
            player.AttackSeq = seq;
            RequestAttack(player);
        }

        // Clients and bots alike: a request is a press, kept until the next rules pass decides on it.
        private void RequestAttack(Player player)
        {
            if (_sandbox || player.Role != Role.Killer) return;
            while (player.AttackTimes.Count > 0 && _now - player.AttackTimes.Peek() >= 1 - TimeEpsilon)
                player.AttackTimes.Dequeue();
            if (player.AttackTimes.Count >= GameConstants.MaxAttacksPerSecond) return;
            player.AttackTimes.Enqueue(_now);
            player.AttackRequested = true;
        }

        internal bool Swinging(Player player) => player.SwingPhase != SwingNone;

        private bool CanStartSwing(Player killer) =>
            killer.Status == PlayerStatus.Normal && !Stunned(killer) && !killer.InChaseAction && killer.BreakShutter < 0 &&
            !Swinging(killer);

        // Runs after ChaseProps (a shutter stun of this tick is already applied) and replaces the old Capture().
        private void Swing()
        {
            var killer = _killer;
            if (killer == null) return;
            bool requested = killer.AttackRequested;
            killer.AttackRequested = false;
            bool contact = ContactSwingForTests && !killer.IsBot && !requested && killer.SwingPhase == SwingNone &&
                CanStartSwing(killer) && NearestCandidate(killer, GameConstants.CaptureRadius) != null;
            if (killer.SwingPhase == SwingWindUp && Stunned(killer))
            {
                // A shutter stun interrupts the wind-up: no hit, and the stun itself is the penalty.
                killer.SwingPhase = SwingNone;
                LogSwing(killer, "interrupted", null);
            }
            bool recoveryEnded = false;
            if (killer.SwingPhase == SwingRecovery && !Active(killer.SwingEnd)) { killer.SwingPhase = SwingNone; recoveryEnded = true; }
            // M1: the flag falls for one tick between chained swings, so each swing is its own span; a press made now waits one tick.
            if (recoveryEnded && requested) killer.AttackRequested = true;
            if (contact)
            {
                var aim = NearestCandidate(killer, GameConstants.CaptureRadius);
                killer.Yaw = (float)(Math.Atan2(aim.X - killer.X, aim.Z - killer.Z) * 180 / Math.PI);
                killer.SwingPhase = SwingWindUp;
                killer.SwingStart = killer.SwingHitAt = _now;
            }
            else if (killer.SwingPhase == SwingNone && requested && !recoveryEnded && CanStartSwing(killer))
            {
                killer.SwingPhase = SwingWindUp;
                killer.SwingStart = _now;
                killer.SwingHitAt = _now + GameConstants.SwingWindUpSeconds;
                killer.SwingHit = false;
                // A swing takes the hands: any kick or hatch-close hold restarts.
                killer.KickHeld = killer.HatchHeld = 0;
                killer.KickTerminal = -1;
                return;
            }
            if (killer.SwingPhase != SwingWindUp || Active(killer.SwingHitAt)) return;
            var target = SwingTarget(killer, ContactSwingForTests && !killer.IsBot && killer.SwingStart == killer.SwingHitAt);
            killer.SwingHit = target != null;
            killer.SwingPhase = SwingRecovery;
            killer.SwingEnd = _now + (target != null ? GameConstants.SwingHitRecoverySeconds : GameConstants.SwingMissRecoverySeconds);
            if (target != null) CaptureSurvivor(target);
            LogSwing(killer, target != null ? "hit" : "miss", target);
        }

        // The nearest Normal, non-invulnerable Survivor in range, inside the arc and in line of sight; null on a miss.
        private Player SwingTarget(Player killer, bool ignoreSight = false)
        {
            double yaw = killer.Yaw * Math.PI / 180;
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);
            float cosLimit = (float)Math.Cos(GameConstants.SwingHalfAngleDegrees * Math.PI / 180);
            Player best = null;
            float bestDistance = float.MaxValue;
            foreach (var survivor in _players)
            {
                if (survivor.Role != Role.Survivor || survivor.Status != PlayerStatus.Normal || Active(survivor.InvulnerableUntil)) continue;
                if (!Within(survivor, killer.X, killer.Z, GameConstants.SwingRange)) continue;
                float dx = survivor.X - killer.X, dz = survivor.Z - killer.Z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                // Standing on top of each other counts as in front.
                if (distance > 0.0001f && (dx * fx + dz * fz) / distance < cosLimit - 0.000001f) continue;
                if (!ignoreSight && SwingBlocked(killer.X, killer.Z, survivor.X, survivor.Z)) continue;
                if (distance < bestDistance) { bestDistance = distance; best = survivor; }
            }
            return best;
        }

        private Player NearestCandidate(Player killer, float radius)
        {
            Player best = null;
            float bestDistance = float.MaxValue;
            foreach (var survivor in _players)
            {
                if (survivor.Role != Role.Survivor || survivor.Status != PlayerStatus.Normal || Active(survivor.InvulnerableUntil)) continue;
                if (!Within(survivor, killer.X, killer.Z, radius)) continue;
                float distance = DistanceSquared(killer.X, killer.Z, survivor.X, survivor.Z);
                if (distance < bestDistance) { bestDistance = distance; best = survivor; }
            }
            return best;
        }

        // The sight helper (walls) plus Down shutters: a closed shutter stops a swing even though it never blocks sight.
        private bool SwingBlocked(float ax, float az, float bx, float bz)
        {
            if (Map.SegmentBlocked(ax, az, bx, bz)) return true;
            var shutters = WorldChaseProps.Shutters;
            for (int i = 0; i < shutters.Count; i++)
            {
                if (Map.ShutterAt(i) != ShutterState.Down) continue;
                var box = new MapWall(shutters[i].MinX, shutters[i].MinZ, shutters[i].MaxX, shutters[i].MaxZ);
                if (box.IntersectsSegment(ax, az, bx, bz)) return true;
            }
            return false;
        }

        // GDD v2 §4 capture stages, unchanged: one more capture (1 -> Lagging 60 s, 2 -> Lagging 30 s, 3 -> Eliminated).
        private void CaptureSurvivor(Player survivor)
        {
            if (survivor.CaptureCount < byte.MaxValue) survivor.CaptureCount++;
            if (survivor.CaptureCount >= GameConstants.EliminatingCaptureCount)
                SetStatus(survivor, PlayerStatus.Eliminated, "third capture");
            else SetStatus(survivor, PlayerStatus.Lagging, "capture");
        }

        // Movement multiplier of the swing for a movement interval starting at start.
        private static float SwingSpeedMultiplier(Player player, double start)
        {
            if (player.SwingPhase == SwingNone) return 1;
            if (player.SwingPhase == SwingWindUp) return GameConstants.SwingWindUpSpeedMultiplier;
            if (start >= player.SwingEnd - TimeEpsilon) return 1;
            return player.SwingHit ? 0 : GameConstants.SwingMissSpeedMultiplier;
        }

        private void LogSwing(Player killer, string result, Player target)
        {
            if (_now - killer.SwingLoggedAt < SwingLogSeconds - TimeEpsilon) { killer.SwingsUnlogged++; return; }
            string unlogged = killer.SwingsUnlogged > 0 ? " unlogged=" + killer.SwingsUnlogged : string.Empty;
            killer.SwingLoggedAt = _now;
            killer.SwingsUnlogged = 0;
            string hit = target != null ? " target=" + target.Id : string.Empty;
            WriteLog($"swing player={killer.Id} result={result}{hit}{unlogged}");
        }

        // Server tests only.
        internal void SetYawForTests(int playerId, float yaw)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) player.Yaw = yaw;
        }

        internal void StunForTests(int playerId, double seconds)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) player.StunnedUntil = _now + seconds;
        }

        internal bool SwingingForTests(int playerId)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) return Swinging(player);
            return false;
        }
    }
}
