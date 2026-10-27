using System;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    // Bots act only on what a human in the same role receives: the killer bot reads its anonymous masked view by
    // position (phantoms and afterimages included, never an Id); survivor bots read the survivor view and ThreatLevel,
    // never the killer. Every walk follows the NavGrid route, so no terminal or exit behind a wall is unreachable.
    public sealed partial class GameServer
    {
        private const float BotStopDistance = 1.0f;
        private const float BotAllyRescueRange = 10f;
        private const float BotStuckFraction = 0.3f;

        private const byte BotPause = 0, BotWalk = 1, BotHack = 2;
        // Survivor bots are deliberately imperfect (U4b play: perfect bots ended matches in about 20 s). They re-plan
        // only every reaction interval, move and hack in short bursts separated by Silent pauses, freeze when the
        // threat rises, and the team works one terminal at a time with one bot on it while the others stay hidden.
        private const double BotReactionMin = 0.25, BotReactionMax = 0.6;
        private const double BotOpeningMin = 2, BotOpeningMax = 8;
        private const double BotWalkBurstMin = 2, BotWalkBurstMax = 5, BotWalkPauseMin = 1, BotWalkPauseMax = 3;
        private const double BotHackBurstMin = 2, BotHackBurstMax = 4, BotHackPauseMin = 8, BotHackPauseMax = 16;
        // ThreatLevel 1 pauses briefly; ThreatLevel 2+ freezes (GDD §8) for at most BotThreatFreezeSeconds. A freeze
        // always ends: the bot then keeps moving for BotBraveSeconds, and after a level 2 freeze it changes terminal.
        private const double BotThreatPauseSeconds = 6, BotThreatFreezeSeconds = 12, BotBraveSeconds = 8;
        // A bot without a free terminal waits hidden and only now and then walks to another hiding spot.
        private const double BotWaitPauseMin = 80, BotWaitPauseMax = 160;
        private const int BotTerminalCap = 1;
        private const int BotActiveTerminals = 1;

        private int[] _claimScratch = Array.Empty<int>();

        private double Uniform(double min, double max) => min + _random.NextDouble() * (max - min);

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
            if (!bot.IsActiveSurvivor) { bot.BotClaim = -1; return; }
            if (bot.Status == PlayerStatus.Lagging)
            {
                bot.BotClaim = -1;
                var ally = NearestSurvivor(bot, PlayerStatus.Normal, float.MaxValue);
                if (ally == null) BotStop(bot);
                else BotMoveTo(bot, ally.X, ally.Z, BotStopDistance);
                return;
            }
            if (_now >= bot.BotThinkAt) ThinkSurvivor(bot);
            if (bot.BotMode == BotWalk)
            {
                if (BotMoveTo(bot, bot.BotGoalX, bot.BotGoalZ, bot.BotGoalStop) && bot.BotGoalHack)
                {
                    bot.BotMode = BotHack;
                    SetInteract(bot, 1);
                }
            }
            else
            {
                BotStop(bot);
                if (bot.BotMode == BotHack) SetInteract(bot, 1);
            }
            bool precondition = bot.Skill != SkillIds.EmergencyRestore ||
                NearestSurvivor(bot, PlayerStatus.Lagging, SkillIds.EmergencyRestoreRadius) != null;
            if (precondition && SkillReady(bot)) RequestSkill(bot, bot.Skill);
        }

        private void ThinkSurvivor(Player bot)
        {
            bot.BotThinkAt = _now + Uniform(BotReactionMin, BotReactionMax);
            if (bot.BotBurstUntil <= 0)
            {
                bot.BotPauseUntil = _now + Uniform(BotOpeningMin, BotOpeningMax);
                bot.BotBurstUntil = bot.BotPauseUntil + Uniform(BotWalkBurstMin, BotWalkBurstMax);
                PickHideout(bot);
            }
            byte threat = ThreatFor(bot);
            if (threat == 0) bot.BotThreatSince = double.NegativeInfinity;
            else if (_now >= bot.BotBraveUntil)
            {
                if (double.IsNegativeInfinity(bot.BotThreatSince)) bot.BotThreatSince = _now;
                double hold = threat >= 2 ? BotThreatFreezeSeconds : BotThreatPauseSeconds;
                if (_now - bot.BotThreatSince < hold) { bot.BotMode = BotPause; return; }
                bot.BotBraveUntil = _now + BotBraveSeconds;
                bot.BotThreatSince = double.NegativeInfinity;
                if (threat >= 2)
                {
                    // Relocate: give up the terminal and slip away to a landmark well clear of this spot.
                    bot.BotAvoid = bot.BotClaim;
                    bot.BotClaim = -1;
                    bot.BotRelocating = PickHideout(bot);
                }
            }
            if (_exitOpen) { BotPlanWalk(bot, Map.Exit.X, Map.Exit.Z, 0, false); return; }
            if (bot.BotRelocating && _now < bot.BotBraveUntil &&
                DistanceSquared(bot.X, bot.Z, bot.BotHideX, bot.BotHideZ) > BotStopDistance * BotStopDistance)
            {
                BotPlanWalk(bot, bot.BotHideX, bot.BotHideZ, BotStopDistance, false);
                return;
            }
            bot.BotRelocating = false;
            // Rescue only when the killer is not near: walking into a camped ally only adds a second capture.
            var lagging = threat == 0 ? NearestSurvivor(bot, PlayerStatus.Lagging, BotAllyRescueRange) : null;
            if (lagging != null) { BotPlanWalk(bot, lagging.X, lagging.Z, BotStopDistance, false); return; }
            int terminal = ClaimTerminal(bot);
            bool atTerminal = terminal >= 0 &&
                DistanceSquared(bot.X, bot.Z, _terminals[terminal].X, _terminals[terminal].Z) <= BotStopDistance * BotStopDistance;
            if (_now >= bot.BotBurstUntil)
            {
                double pause = atTerminal ? Uniform(BotHackPauseMin, BotHackPauseMax)
                    : terminal < 0 ? Uniform(BotWaitPauseMin, BotWaitPauseMax) : Uniform(BotWalkPauseMin, BotWalkPauseMax);
                bot.BotPauseUntil = _now + pause;
                bot.BotBurstUntil = bot.BotPauseUntil + (atTerminal ? Uniform(BotHackBurstMin, BotHackBurstMax) : Uniform(BotWalkBurstMin, BotWalkBurstMax));
                if (terminal < 0) PickHideout(bot);
            }
            if (_now < bot.BotPauseUntil && _now >= bot.BotBraveUntil) { bot.BotMode = BotPause; return; }
            if (terminal < 0)
            {
                // Waiting for a turn: mostly hidden, with an occasional short walk to another hiding spot.
                if (DistanceSquared(bot.X, bot.Z, bot.BotHideX, bot.BotHideZ) > BotStopDistance * BotStopDistance)
                    BotPlanWalk(bot, bot.BotHideX, bot.BotHideZ, BotStopDistance, false);
                else bot.BotMode = BotPause;
                return;
            }
            if (atTerminal) bot.BotMode = BotHack;
            else BotPlanWalk(bot, _terminals[terminal].X, _terminals[terminal].Z, BotStopDistance, true);
        }

        private const float BotHideoutDistance = 15f;

        // A random survivor spawn or terminal at least BotHideoutDistance away (the farthest one if none is).
        private bool PickHideout(Player bot)
        {
            int options = Map.SpawnPoints.Count - 1 + _terminals.Length;
            int start = _random.Next(options);
            float bestDistance = -1;
            for (int k = 0; k < options; k++)
            {
                int i = (start + k) % options;
                float x = i < Map.SpawnPoints.Count - 1 ? Map.SpawnPoints[i + 1].X : _terminals[i - Map.SpawnPoints.Count + 1].X;
                float z = i < Map.SpawnPoints.Count - 1 ? Map.SpawnPoints[i + 1].Z : _terminals[i - Map.SpawnPoints.Count + 1].Z;
                float distance = DistanceSquared(bot.X, bot.Z, x, z);
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                bot.BotHideX = x;
                bot.BotHideZ = z;
                if (distance >= BotHideoutDistance * BotHideoutDistance) break;
            }
            return bestDistance >= 0;
        }

        private void BotPlanWalk(Player bot, float x, float z, float stop, bool hack)
        {
            bot.BotMode = BotWalk;
            bot.BotGoalX = x;
            bot.BotGoalZ = z;
            bot.BotGoalStop = stop;
            bot.BotGoalHack = hack;
        }

        // At most BotTerminalCap bots per terminal and BotActiveTerminals terminals worked by bots at once.
        private int ClaimTerminal(Player bot)
        {
            if (bot.BotClaim >= 0 && bot.BotClaim < _terminals.Length && _terminals[bot.BotClaim].Progress < 100) return bot.BotClaim;
            bot.BotClaim = -1;
            if (_claimScratch.Length != _terminals.Length) _claimScratch = new int[_terminals.Length];
            Array.Clear(_claimScratch, 0, _claimScratch.Length);
            int active = 0;
            foreach (var other in _players)
            {
                if (other == bot || !other.IsBot || other.Role != Role.Survivor || other.Status != PlayerStatus.Normal) continue;
                int claim = other.BotClaim;
                if (claim < 0 || claim >= _terminals.Length || _terminals[claim].Progress >= 100) continue;
                if (_claimScratch[claim]++ == 0) ++active;
            }
            for (int pass = 0; pass < 2 && bot.BotClaim < 0; pass++)
            {
                float best = float.MaxValue;
                for (int i = 0; i < _terminals.Length; i++)
                {
                    if (_terminals[i].Progress >= 100 || (pass == 0 && i == bot.BotAvoid) || _claimScratch[i] >= BotTerminalCap) continue;
                    if (_claimScratch[i] == 0 && active >= BotActiveTerminals) continue;
                    float distance = DistanceSquared(bot.X, bot.Z, _terminals[i].X, _terminals[i].Z);
                    if (distance < best) { best = distance; bot.BotClaim = i; }
                }
            }
            return bot.BotClaim;
        }

        // The killer bot is imperfect too: it notices new Signals only every reaction interval, gives up a chase that
        // has had no fresh sighting for a while, and stops to look around at each patrolled terminal.
        private const double KillerBotReactionMin = 0.4, KillerBotReactionMax = 0.9;
        private const double KillerBotChaseMemorySeconds = 5;
        private const double KillerBotLookMin = 1.5, KillerBotLookMax = 4;

        private void UpdateKillerBot(Player bot)
        {
            bool anyVisible = false;
            bool look = _now >= bot.BotThinkAt;
            if (look) bot.BotThinkAt = _now + Uniform(KillerBotReactionMin, KillerBotReactionMax);
            float closest = float.MaxValue;
            foreach (var entry in _killerView)
            {
                if (entry.Role != (byte)Role.Survivor) continue;
                anyVisible = true;
                // A Lagging survivor is executed only by a fresh touch; the bot hunts Normal Signals instead.
                if (!look || entry.Status != (byte)PlayerStatus.Normal) continue;
                float distance = DistanceSquared(bot.X, bot.Z, entry.X, entry.Z);
                if (distance >= closest) continue;
                closest = distance;
                bot.HasLastSeen = true;
                bot.LastSeenX = entry.X;
                bot.LastSeenZ = entry.Z;
                bot.BotBurstUntil = _now + KillerBotChaseMemorySeconds;
            }
            if (bot.HasLastSeen && (DistanceSquared(bot.X, bot.Z, bot.LastSeenX, bot.LastSeenZ) < 0.64f || _now >= bot.BotBurstUntil))
                bot.HasLastSeen = false;
            if (bot.HasLastSeen) BotMoveTo(bot, bot.LastSeenX, bot.LastSeenZ, 0);
            else if (_now < bot.BotPauseUntil) BotStop(bot);
            else if (_terminals.Length > 0)
            {
                bool arrived = bot.BotTarget >= 0 && bot.BotTarget < _terminals.Length &&
                    DistanceSquared(bot.X, bot.Z, _terminals[bot.BotTarget].X, _terminals[bot.BotTarget].Z) < 0.64f;
                if (arrived) bot.BotPauseUntil = _now + Uniform(KillerBotLookMin, KillerBotLookMax);
                if (bot.BotTarget < 0 || bot.BotTarget >= _terminals.Length || _now >= bot.BotWalkUntil || arrived)
                {
                    bot.BotTarget = _random.Next(_terminals.Length);
                    bot.BotWalkUntil = _now + 10;
                }
                if (arrived) BotStop(bot);
                else BotMoveTo(bot, _terminals[bot.BotTarget].X, _terminals[bot.BotTarget].Z, 0);
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

        private int NearestIncompleteTerminal(float x, float z)
        {
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _terminals.Length; i++)
            {
                if (_terminals[i].Progress >= 100) continue;
                float distance = DistanceSquared(x, z, _terminals[i].X, _terminals[i].Z);
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

        // Follows the NavGrid route with a short sidestep when progress stalls. Returns true when arrived.
        private bool BotMoveTo(Player bot, float x, float z, float stopDistance)
        {
            float distance = (float)Math.Sqrt(DistanceSquared(bot.X, bot.Z, x, z));
            if (distance <= Math.Max(stopDistance, 0.05f) || !Nav.Steer(bot.X, bot.Z, x, z, out float dx, out float dz))
            {
                BotStop(bot);
                return true;
            }
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
