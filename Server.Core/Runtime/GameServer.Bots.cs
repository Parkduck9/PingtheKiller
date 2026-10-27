using System;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    // Bots act only on what a human in the same role receives: the killer bot reads its masked view
    // (decoys and afterimages included); survivor bots read the survivor view and ThreatLevel, never the killer.
    public sealed partial class GameServer
    {
        private const float BotStopDistance = 1.0f;
        private const float BotAllyRescueRange = 10f;
        private const float BotStuckFraction = 0.3f;

        private void UpdateBots()
        {
            foreach (var bot in _players)
            {
                if (!bot.IsBot) continue;
                if (_sandbox) UpdateSandboxBot(bot);
                else if (bot.Role == Role.Killer) UpdateKillerBot(bot);
                else UpdateSurvivorBot(bot);
            }
        }

        private void UpdateSurvivorBot(Player bot)
        {
            bot.Interacting = false;
            if (!bot.IsActiveSurvivor) return;
            if (bot.Status == PlayerStatus.Lagging)
            {
                var ally = NearestSurvivor(bot, PlayerStatus.Normal, float.MaxValue);
                if (ally == null) BotStop(bot);
                else BotMoveTo(bot, ally.X, ally.Z, BotStopDistance);
                return;
            }
            if (ThreatFor(bot) >= 2) BotStop(bot);
            else if (_exitOpen) BotMoveTo(bot, Map.Exit.X, Map.Exit.Z, 0);
            else
            {
                var lagging = NearestSurvivor(bot, PlayerStatus.Lagging, BotAllyRescueRange);
                if (lagging != null) BotMoveTo(bot, lagging.X, lagging.Z, BotStopDistance);
                else
                {
                    int terminal = NearestIncompleteTerminal(bot);
                    if (terminal < 0) BotStop(bot);
                    else if (BotMoveTo(bot, _terminals[terminal].X, _terminals[terminal].Z, BotStopDistance))
                        SetInteract(bot, 1);
                }
            }
            bool precondition = bot.Skill != SkillIds.EmergencyRestore ||
                NearestSurvivor(bot, PlayerStatus.Lagging, SkillIds.EmergencyRestoreRadius) != null;
            if (precondition && SkillReady(bot)) RequestSkill(bot, bot.Skill);
        }

        private void UpdateKillerBot(Player bot)
        {
            bool anyVisible = false;
            float closest = float.MaxValue;
            foreach (var entry in _killerView)
            {
                if (entry.Role != (byte)Role.Survivor) continue;
                anyVisible = true;
                // A Lagging survivor is executed only by a fresh touch; the bot hunts Normal Signals instead.
                if (entry.Status != (byte)PlayerStatus.Normal) continue;
                float distance = DistanceSquared(bot.X, bot.Z, entry.X, entry.Z);
                if (distance >= closest) continue;
                closest = distance;
                bot.HasLastSeen = true;
                bot.LastSeenX = entry.X;
                bot.LastSeenZ = entry.Z;
            }
            if (bot.HasLastSeen && DistanceSquared(bot.X, bot.Z, bot.LastSeenX, bot.LastSeenZ) < 0.64f)
                bot.HasLastSeen = false;
            if (bot.HasLastSeen) BotMoveTo(bot, bot.LastSeenX, bot.LastSeenZ, 0);
            else if (_terminals.Length > 0)
            {
                if (bot.BotTarget < 0 || bot.BotTarget >= _terminals.Length || _now >= bot.BotWalkUntil ||
                    DistanceSquared(bot.X, bot.Z, _terminals[bot.BotTarget].X, _terminals[bot.BotTarget].Z) < 0.64f)
                {
                    bot.BotTarget = _random.Next(_terminals.Length);
                    bot.BotWalkUntil = _now + 10;
                }
                BotMoveTo(bot, _terminals[bot.BotTarget].X, _terminals[bot.BotTarget].Z, 0);
            }
            if (!SkillReady(bot)) return;
            bool precondition = bot.Skill switch
            {
                SkillIds.PingScanner => !anyVisible,
                SkillIds.FirewallBlock => _exitOpen,
                SkillIds.PacketAfterimage => false,
                _ => true
            };
            if (precondition) RequestSkill(bot, bot.Skill);
        }

        private Player NearestSurvivor(Player self, PlayerStatus status, float range)
        {
            Player nearest = null;
            float best = range == float.MaxValue ? float.MaxValue : range * range + 0.000001f;
            foreach (var other in _players)
            {
                if (other == self || other.Role != Role.Survivor || other.Status != status) continue;
                float distance = DistanceSquared(self.X, self.Z, other.X, other.Z);
                if (distance > best) continue;
                best = distance;
                nearest = other;
            }
            return nearest;
        }

        private int NearestIncompleteTerminal(Player bot)
        {
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _terminals.Length; i++)
            {
                if (_terminals[i].Progress >= 100) continue;
                float distance = DistanceSquared(bot.X, bot.Z, _terminals[i].X, _terminals[i].Z);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        private void BotStop(Player bot)
        {
            bot.BotDetourUntil = 0;
            ApplyMove(bot, new C2S_MoveInput { Yaw = bot.Yaw, Seq = bot.Seq + 1 });
            bot.BotLastX = bot.X;
            bot.BotLastZ = bot.Z;
        }

        // Walks straight at the target with a short sidestep when a wall blocks progress. Returns true when arrived.
        private bool BotMoveTo(Player bot, float x, float z, float stopDistance)
        {
            float dx = x - bot.X, dz = z - bot.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance <= Math.Max(stopDistance, 0.05f))
            {
                BotStop(bot);
                return true;
            }
            dx /= distance;
            dz /= distance;
            float expected = SpeedAt(bot, _now) / GameConstants.TickRate;
            bool wasMoving = bot.InputHeld(_now);
            float moved = (float)Math.Sqrt(DistanceSquared(bot.X, bot.Z, bot.BotLastX, bot.BotLastZ));
            if (wasMoving && expected > 0 && moved < expected * BotStuckFraction && _now >= bot.BotDetourUntil)
            {
                float side = _random.Next(2) == 0 ? 1 : -1;
                bot.BotDetourX = -dz * side;
                bot.BotDetourZ = dx * side;
                bot.BotDetourUntil = _now + 0.5 + _random.NextDouble() * 0.5;
            }
            if (_now < bot.BotDetourUntil) { dx = bot.BotDetourX; dz = bot.BotDetourZ; }
            float yaw = (float)(Math.Atan2(dx, dz) * 180 / Math.PI);
            if (yaw < 0) yaw += 360;
            ApplyMove(bot, new C2S_MoveInput { DirX = dx, DirZ = dz, Yaw = yaw, Seq = bot.Seq + 1 });
            bot.BotLastX = bot.X;
            bot.BotLastZ = bot.Z;
            return false;
        }
    }
}
