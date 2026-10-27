using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    // Sandbox bots keep the U2a behaviour: survivors walk to random terminals and pause for 1-3 s,
    // the killer chases the last Signal it saw or patrols terminals.
    public sealed partial class GameServer
    {
        private void UpdateSandboxBot(Player bot)
        {
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
                    if (survivor.Role != Role.Survivor || !survivor.InputHeld(_now)) continue;
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
}
