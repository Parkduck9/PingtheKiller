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
        private void BroadcastWorld()
        {
            Player killer = null;
            foreach (var player in _players) if (player.Role == Role.Killer) killer = player;
            foreach (var recipient in _players)
            {
                if (recipient.ConnectionId == null || !_connections.TryGetValue(recipient.ConnectionId, out var binding)) continue;
                var world = new S2C_WorldState
                {
                    Tick = _tick, TimeLeft = Math.Max(0, GameConstants.MatchLengthSeconds - (float)_matchTime),
                    ExitOpen = 0, ExitLocked = 1, TerminalsRequired = _terminalsRequired
                };
                world.Terminals.AddRange(_terminals);
                if (recipient.Role == Role.Survivor && killer != null)
                {
                    float dx = recipient.X - killer.X, dz = recipient.Z - killer.Z;
                    world.ThreatLevel = ThreatForDistance((float)Math.Sqrt(dx * dx + dz * dz));
                }
                foreach (var player in _players)
                {
                    if (recipient.Role == Role.Killer)
                    {
                        if (player.Role == Role.Survivor && !player.IsSignal(_now)) continue;
                    }
                    else if (player.Role == Role.Killer) continue;
                    world.Players.Add(player.State(_now));
                }
                Send(binding.Connection, world);
            }
            // TODO U2b: objective progress, exit transitions, status timers and win/lose.
        }

        private void UpdateBots()
        {
            foreach (var bot in _players)
            {
                if (!bot.IsBot) continue;
                float targetX = 0, targetZ = 0;
                bool pause = false;
                if (bot.Role == Role.Survivor)
                {
                    if (_now < bot.BotPauseUntil) pause = true;
                    else if (bot.BotTarget < 0 || _now >= bot.BotWalkUntil)
                    {
                        if (bot.BotTarget >= 0)
                        {
                            bot.BotTarget = -1;
                            bot.BotPauseUntil = _now + 1 + _random.NextDouble() * 2;
                            pause = true;
                        }
                        else
                        {
                            bot.BotTarget = _random.Next(_terminals.Length);
                            bot.BotWalkUntil = _now + 3 + _random.NextDouble() * 5;
                        }
                    }
                    if (!pause && bot.BotTarget >= 0)
                    {
                        targetX = _terminals[bot.BotTarget].X;
                        targetZ = _terminals[bot.BotTarget].Z;
                        if (DistanceSquared(bot.X, bot.Z, targetX, targetZ) < 0.64f)
                        {
                            bot.BotWalkUntil = _now;
                            pause = true;
                        }
                    }
                }
                else
                {
                    float closest = float.MaxValue;
                    foreach (var survivor in _players)
                    {
                        if (survivor.Role != Role.Survivor || !survivor.IsSignal(_now)) continue;
                        float distance = DistanceSquared(bot.X, bot.Z, survivor.X, survivor.Z);
                        if (distance >= closest) continue;
                        closest = distance;
                        bot.HasLastSeen = true;
                        bot.LastSeenX = survivor.X;
                        bot.LastSeenZ = survivor.Z;
                    }
                    if (bot.HasLastSeen && DistanceSquared(bot.X, bot.Z, bot.LastSeenX, bot.LastSeenZ) < 0.64f)
                        bot.HasLastSeen = false;
                    if (bot.HasLastSeen) { targetX = bot.LastSeenX; targetZ = bot.LastSeenZ; }
                    else
                    {
                        if (bot.BotTarget < 0 || DistanceSquared(bot.X, bot.Z, _terminals[bot.BotTarget].X, _terminals[bot.BotTarget].Z) < 0.64f || _now >= bot.BotWalkUntil)
                        {
                            bot.BotTarget = _random.Next(_terminals.Length);
                            bot.BotWalkUntil = _now + 10;
                        }
                        targetX = _terminals[bot.BotTarget].X;
                        targetZ = _terminals[bot.BotTarget].Z;
                    }
                }
                ApplyMove(bot, new C2S_MoveInput
                {
                    DirX = pause ? 0 : targetX - bot.X,
                    DirZ = pause ? 0 : targetZ - bot.Z,
                    Seq = bot.Seq + 1
                });
            }
        }
        private static float DistanceSquared(float x, float z, float tx, float tz)
        {
            float dx = x - tx, dz = z - tz;
            return dx * dx + dz * dz;
        }
    }
}
