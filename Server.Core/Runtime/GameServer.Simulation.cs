using System;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        private void ApplyMove(Player player, C2S_MoveInput move)
        {
            if (player.HasSeq && unchecked((int)(move.Seq - player.Seq)) <= 0) return;
            while (player.InputTimes.Count > 0 && _now - player.InputTimes.Peek() >= 1 - 0.000001)
                player.InputTimes.Dequeue();
            if (player.InputTimes.Count >= 60) return;
            player.InputTimes.Enqueue(_now);
            player.HasSeq = true;
            player.Seq = move.Seq;
            float x = move.DirX, z = move.DirZ;
            if (!Finite(x) || !Finite(z)) x = z = 0;
            double length = Math.Sqrt((double)x * x + (double)z * z);
            if (length > 1) { x = (float)(x / length); z = (float)(z / length); }
            player.DirX = x;
            player.DirZ = z;
            player.Yaw = Finite(move.Yaw) ? move.Yaw : 0;
            if (x != 0 || z != 0) player.LastSignal = _now;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static byte ThreatForDistance(float distance)
        {
            if (distance <= GameConstants.ThreatLevel3Distance) return 3;
            if (distance <= GameConstants.ThreatLevel2Distance) return 2;
            if (distance <= GameConstants.ThreatLevel1Distance) return 1;
            return 0;
        }

        // start is the instant this tick's movement interval begins; timed speed effects cover [cast, cast + duration).
        private void MovePlayers(double start, float dt)
        {
            foreach (var player in _players)
            {
                // A held direction lives only as long as the hold of the last non-zero input, evaluated with the
                // same _now as BroadcastWorld: a survivor that moves this tick is Signal unless an effect hides it.
                // Clients must therefore resend MoveInput (>= 10 Hz) while a key is held.
                if (!player.InputHeld(_now) || (player.Role == Role.Survivor && !player.IsActiveSurvivor))
                {
                    player.DirX = player.DirZ = 0;
                    continue;
                }
                // GDD v2 section 8: a pull, vault or stun ignores the move input (moving cancels a break hold instead).
                if (player.InChaseAction || Stunned(player)) continue;
                float speed = SpeedAt(player, start);
                if (speed <= 0) continue;
                var position = Map.Move(player.X, player.Z, player.DirX * speed * dt, player.DirZ * speed * dt);
                player.X = position.X;
                player.Z = position.Z;
            }
        }

        private static float SpeedAt(Player player, double start)
        {
            if (player.Role == Role.Survivor)
                return player.Status == PlayerStatus.Lagging
                    ? GameConstants.SurvivorSpeed * GameConstants.LaggingSpeedMultiplier
                    : GameConstants.SurvivorSpeed;
            float speed = GameConstants.KillerSpeed;
            if (start < player.OverclockUntil - TimeEpsilon) speed *= SkillIds.OverclockSpeedMultiplier;
            else if (start < player.RecoveryUntil - TimeEpsilon) return 0;
            if (start < player.SlowedUntil - TimeEpsilon) speed *= SkillIds.TrafficOverloadKillerSpeedMultiplier;
            return speed;
        }

        private static float DistanceSquared(float x, float z, float tx, float tz)
        {
            float dx = x - tx, dz = z - tz;
            return dx * dx + dz * dz;
        }
        private static bool Within(Player a, float x, float z, float radius) =>
            DistanceSquared(a.X, a.Z, x, z) <= radius * radius + 0.000001f;
    }
}
