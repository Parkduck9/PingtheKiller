using System;
using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // A survivor bot. It decides only from its SurvivorBotView: ThreatLevel, teammates, terminals, the exit, and the killer
    // while its own line of sight is clear (plus where it last saw it). It is deliberately imperfect: it re-plans once per
    // reaction interval (a per-bot pace drawn from the private stream), hacks in bursts with Silent pauses, and reacts to
    // danger by slipping away from the killer, going Silent and waiting before it resumes.
    internal sealed class SurvivorBrain : BotBrain
    {
        // Walk and hack pacing. GameServer.Phantoms copies these through the GameServer.Bot* constants, so phantom tracks
        // keep the same burst and pause shape as real survivor bots.
        internal const double WalkBurstMin = 2, WalkBurstMax = 5, WalkPauseMin = 1, WalkPauseMax = 3;
        internal const double HackBurstMin = 2, HackBurstMax = 4, HackPauseMin = 8, HackPauseMax = 16;
        // Hacking spot: inside TerminalRadius but beyond CaptureRadius of the terminal centre, where a patrolling killer stops.
        internal const float HackStopDistance = 1.4f;

        private const double ReactionMin = 0.25, ReactionMax = 0.6;
        // A Silent look around before the first move, letting the killer commit to a direction first.
        private const double OpeningMin = 2, OpeningMax = 8;
        // Danger: flee when the killer in sight walks at this bot (within ComingHalfAngle and ComingRange) shortly after it
        // was Signal near the killer and slipping away is unsafe, or when ThreatLevel reaches ThreatFleeLevel shortly after
        // that while the killer is out of sight. After a flee it holds still and Silent, and it waits (calm) before it
        // resumes work. Otherwise a nearby killer only makes it pause, or slip away from a terminal while that is safe.
        private const float HideSightDistance = 25f;
        private const float WatchRange = 27f, WatchHalfAngle = 70f, ComingHalfAngle = 30f, ComingRange = 32f, SlipDistance = 14f;
        private const double NoticedWindow = 6, BlindNoticedWindow = 4;
        private const float SlipMinDistance = 6f, ExitCaution = 10f;
        private const byte ThreatFleeLevel = 2, HideThreatLevel = 1;
        // ThreatLevel 1 alone is a scare only for a while after it rose; a killer that stays in that band without coming
        // closer (camping, idle) is then ignored like a human player would.
        private const double ThreatScareSeconds = 8;
        private const float TerminalLeaveRadius = 3f, HideMinReach = 5f, HideMaxReach = 14f;
        private const double HoldMin = 3, HoldMax = 6;
        private const double ExposedWindow = 1.0;
        private const double FleeMin = 2.5, FleeMax = 4.5;
        private const float FleeMinReach = 5f, FleeMaxReach = 16f;
        private const double CalmMin = 4, CalmMax = 10;
        private const double KillerMemorySeconds = 3;
        private const double TerminalDangerMemorySeconds = 12;
        private const float TerminalDangerRadius = 10f;
        private const double AvoidTerminalSeconds = 20;
        // Rescue only when the killer was not seen for a while and ThreatLevel is at most 1.
        private const double RescueQuietSeconds = 5;
        private const float RescueRange = 30f;
        // Team spread, judged only from the team entries every survivor receives: the ActiveWorkers Normal survivors with
        // the lowest Ids work terminals (one each, at most TerminalCap teammates near one terminal); the rest stay away
        // from terminals, keep watch and rescue. A captured worker hands its turn to the next Id.
        private const int TerminalCap = 1, ActiveWorkers = 1, LargeTeam = 4;
        private const double UrgentAt = 210, HurryAt = 360;
        // A lower-Id teammate holds a worker turn only while it visibly works (Signal, or at an incomplete terminal) at
        // least once per TeammateActiveSeconds, so an idle teammate (an AFK human) does not block the turn for good.
        private const double TeammateActiveSeconds = 45;
        private const float OccupiedRadius = 3f;
        private const double WaitPauseMin = 25, WaitPauseMax = 50;
        private const float HideoutDistance = 15f;

        private const byte ModePause = 0, ModeWalk = 1, ModeHack = 2;

        private double _reaction;
        private double _thinkAt, _burstUntil, _pauseUntil, _calmUntil, _fleeUntil, _holdUntil;
        private bool _fleeing, _hiding, _interrupted;
        private double _exposedAt = double.NegativeInfinity, _exposedNearAt = double.NegativeInfinity;
        private double _killerSeenAt = double.NegativeInfinity;
        private double _threatRoseAt = double.NegativeInfinity;
        private byte _lastThreat;
        private float _killerX, _killerZ, _killerYaw;
        private bool _killerMoving;
        private byte _mode;
        private float _goalX, _goalZ, _goalStop;
        private bool _goalHack;
        private int _terminal = -1, _avoidTerminal = -1;
        private double _avoidUntil;
        private float _hideX, _hideZ;
        private bool _wantSkill, _coverHack;
        private readonly Dictionary<int, double> _teammateActiveAt = new Dictionary<int, double>();

        internal BotIntent Update(SurvivorBotView view, NavGrid nav, FixedMap map, Random random)
        {
            var intent = new BotIntent();
            double t = view.Time;
            if (!Started)
            {
                Started = true;
                _reaction = Uniform(random, ReactionMin, ReactionMax);
                _pauseUntil = t + Uniform(random, OpeningMin, OpeningMax);
                _burstUntil = _pauseUntil + Uniform(random, WalkBurstMin, WalkBurstMax);
                PickHideout(view, nav, random);
            }
            Remember(view);
            if (view.Status == PlayerStatus.Lagging)
            {
                _terminal = -1;
                UpdateLagging(ref intent, view, nav, map, random);
                return intent;
            }
            if (view.Status != PlayerStatus.Normal)
            {
                Stop(ref intent, view.X, view.Z);
                return intent;
            }
            if (t >= _thinkAt) Think(view, nav, map, random);
            if (_mode == ModeWalk)
            {
                if (MoveTo(ref intent, nav, view.X, view.Z, _goalX, _goalZ, _goalStop, view.Speed, t, random) && _goalHack)
                {
                    _mode = ModeHack;
                    _coverHack = true;
                    intent.Interact = true;
                }
            }
            else
            {
                Stop(ref intent, view.X, view.Z);
                intent.Interact = _mode == ModeHack;
            }
            UseSkills(ref intent, view);
            return intent;
        }

        private void Remember(SurvivorBotView view)
        {
            if (view.Threat > _lastThreat) _threatRoseAt = view.Time;
            _lastThreat = view.Threat;
            foreach (var entry in view.Team)
            {
                if (entry.Id == view.SelfId || entry.Status != (byte)PlayerStatus.Normal) continue;
                // First sighting counts as active (everyone starts out working).
                if ((entry.Flags & (byte)PlayerFlags.Signal) != 0 || NearIncompleteTerminal(view, entry.X, entry.Z, OccupiedRadius) ||
                    !_teammateActiveAt.ContainsKey(entry.Id))
                    _teammateActiveAt[entry.Id] = view.Time;
            }
            foreach (var entry in view.Team)
                if (entry.Id == view.SelfId && (entry.Flags & (byte)PlayerFlags.Signal) != 0)
                {
                    _exposedAt = view.Time;
                    // Signal with the killer this close (ThreatLevel or sight): it may well have noticed this bot.
                    if (view.Threat >= 1 || (view.KillerVisible && Distance(view.X, view.Z, view.KillerX, view.KillerZ) <= GameConstants.ThreatLevel1Distance))
                        _exposedNearAt = view.Time;
                }
            if (!view.KillerVisible) return;
            // Moving: the position changed since the sighting one tick ago (the entry carries no velocity).
            _killerMoving = view.Time - _killerSeenAt < 0.2 &&
                Distance(_killerX, _killerZ, view.KillerX, view.KillerZ) > 0.05f;
            _killerSeenAt = view.Time;
            _killerX = view.KillerX;
            _killerZ = view.KillerZ;
            _killerYaw = view.KillerYaw;
        }

        // Degrees between the killer's facing and the direction from the killer to this bot.
        private float KillerAngleTo(SurvivorBotView view)
        {
            float delta = Math.Abs(YawOf(view.X - _killerX, view.Z - _killerZ) - _killerYaw) % 360f;
            return delta > 180f ? 360f - delta : delta;
        }

        private bool KillerSeenWithin(double t, double seconds) => t - _killerSeenAt <= seconds;

        private void Think(SurvivorBotView view, NavGrid nav, FixedMap map, Random random)
        {
            double t = view.Time;
            _thinkAt = t + _reaction * Uniform(random, 0.7, 1.4);
            bool fresh = KillerSeenWithin(t, KillerMemorySeconds);
            float killerDistance = fresh ? Distance(view.X, view.Z, _killerX, _killerZ) : float.MaxValue;
            bool exposed = t - _exposedAt <= ExposedWindow;

            // The exit is open: head there, unless the killer is in sight close by (then the usual danger rules apply).
            bool exitRun = view.ExitOpen && !(view.KillerVisible && killerDistance <= ExitCaution);
            if (exitRun && t >= _fleeUntil)
            {
                Plan(view.Exit.X, view.Exit.Z, 0, false);
                return;
            }
            if (t < _fleeUntil)
            {
                if (Distance(view.X, view.Z, _goalX, _goalZ) > StopDistance) { _mode = ModeWalk; return; }
                _fleeUntil = t;
            }
            if (_fleeing && t >= _fleeUntil)
            {
                // The flee is over: hold still and Silent (running again next to the killer only shows where it is).
                _fleeing = false;
                _holdUntil = t + Uniform(random, HoldMin, HoldMax);
                _calmUntil = Math.Max(_calmUntil, _holdUntil + Uniform(random, CalmMin, CalmMax));
            }
            bool holding = t < _holdUntil;
            // The killer is in sight, faces this bot and walks at it shortly after this bot was Signal: it is coming.
            float angle = view.KillerVisible ? KillerAngleTo(view) : 180f;
            bool watched = view.KillerVisible && killerDistance <= WatchRange && angle <= WatchHalfAngle;
            bool coming = view.KillerVisible && killerDistance <= ComingRange && _killerMoving && angle <= ComingHalfAngle &&
                t - _exposedNearAt <= NoticedWindow;
            // Moving is safe enough while the killer is in sight and either looks elsewhere or is still far off.
            bool canSlip = view.KillerVisible && killerDistance > SlipMinDistance && (!watched || killerDistance > SlipDistance);
            bool blind = !view.KillerVisible && view.Threat >= ThreatFleeLevel && t - _exposedNearAt <= BlindNoticedWindow;
            if (!holding && ((coming && !canSlip) || blind))
            {
                StartFlee(view, nav, map, random, fresh);
                return;
            }
            // A killer standing still far off (camping, idle) only keeps this bot away from its area (see ChooseTerminal).
            bool threatened = view.Threat > HideThreatLevel || (view.Threat == HideThreatLevel && t - _threatRoseAt <= ThreatScareSeconds);
            bool danger = coming || threatened ||
                (fresh && killerDistance <= HideSightDistance && (_killerMoving || killerDistance <= GameConstants.ThreatLevel2Distance));
            if (holding || danger || t < _calmUntil)
            {
                if (danger) _calmUntil = Math.Max(_calmUntil, t + Uniform(random, CalmMin, CalmMax));
                _interrupted = true;
                // Leave a terminal (where a patrolling killer heads, or where it saw this bot) for a quiet spot, but only
                // while that move is unlikely to be seen; unseen or watched up close it holds still instead.
                if (danger && !_hiding && canSlip && (coming || NearIncompleteTerminal(view, view.X, view.Z, TerminalLeaveRadius)))
                {
                    _hiding = true;
                    _terminal = -1;
                    var spot = PickRefuge(view, nav, map, random, fresh, HideMinReach, HideMaxReach);
                    Plan(spot.X, spot.Z, StopDistance, false);
                    return;
                }
                if (_hiding && _mode == ModeWalk && Distance(view.X, view.Z, _goalX, _goalZ) > StopDistance) return;
                _mode = ModePause;
                return;
            }
            _hiding = false;
            if (_interrupted)
            {
                // Back to work at once after a scare (no extra pause on top of the calm wait).
                _interrupted = false;
                _pauseUntil = t;
                _burstUntil = t + Uniform(random, WalkBurstMin, WalkBurstMax);
            }

            int lagging = RescueTarget(view, t);
            if (lagging >= 0)
            {
                var ally = view.Team[lagging];
                if (Distance(view.X, view.Z, ally.X, ally.Z) <= GameConstants.RescueRadius - 0.5f) _mode = ModePause;
                else Plan(ally.X, ally.Z, GameConstants.RescueRadius - 0.7f, false);
                return;
            }

            int terminal = ChooseTerminal(view, t, random);
            bool atTerminal = terminal >= 0 &&
                Distance(view.X, view.Z, view.Terminals[terminal].X, view.Terminals[terminal].Z) <= HackStopDistance + 0.05f;
            if (t >= _burstUntil)
            {
                double pause = atTerminal ? Uniform(random, HackPauseMin, HackPauseMax)
                    : terminal < 0 ? Uniform(random, WaitPauseMin, WaitPauseMax) : Uniform(random, WalkPauseMin, WalkPauseMax);
                _pauseUntil = t + pause;
                _burstUntil = _pauseUntil + (atTerminal ? Uniform(random, HackBurstMin, HackBurstMax) : Uniform(random, WalkBurstMin, WalkBurstMax));
                if (terminal < 0) PickHideout(view, nav, random);
            }
            if (t < _pauseUntil) { _mode = ModePause; return; }
            if (terminal < 0)
            {
                if (Distance(view.X, view.Z, _hideX, _hideZ) > StopDistance) Plan(_hideX, _hideZ, StopDistance, false);
                else _mode = ModePause;
                return;
            }
            if (atTerminal)
            {
                if (_mode != ModeHack) _coverHack = true;
                _mode = ModeHack;
            }
            else Plan(view.Terminals[terminal].X, view.Terminals[terminal].Z, HackStopDistance, true);
        }

        private void Plan(float x, float z, float stop, bool hack)
        {
            _mode = ModeWalk;
            _goalX = x;
            _goalZ = z;
            _goalStop = stop;
            _goalHack = hack;
        }

        // A lattice spot minReach-maxReach off that does not lead toward the killer (when it was seen recently), away from
        // incomplete terminals (where the killer patrols), preferring far from the killer and out of its line of sight.
        private MapPoint PickRefuge(SurvivorBotView view, NavGrid nav, FixedMap map, Random random, bool known, float minReach, float maxReach)
        {
            float bestScore = float.MinValue, bx = view.X, bz = view.Z;
            float kd = known ? Distance(view.X, view.Z, _killerX, _killerZ) : 0;
            foreach (var spot in nav.Spots)
            {
                float d = Distance(view.X, view.Z, spot.X, spot.Z);
                if (d < minReach || d > maxReach) continue;
                float score = (float)random.NextDouble() * 3f;
                if (NearIncompleteTerminal(view, spot.X, spot.Z, TerminalLeaveRadius)) score -= 8f;
                if (known && kd > 0.01f)
                {
                    float dot = ((spot.X - view.X) * (_killerX - view.X) + (spot.Z - view.Z) * (_killerZ - view.Z)) / (d * kd);
                    if (dot > 0.2f) continue;
                    score += Distance(spot.X, spot.Z, _killerX, _killerZ) - 0.3f * d +
                        (map.SegmentBlocked(_killerX, _killerZ, spot.X, spot.Z) ? 6f : 0f);
                }
                if (score <= bestScore) continue;
                bestScore = score;
                bx = spot.X;
                bz = spot.Z;
            }
            return new MapPoint(bx, bz);
        }

        private static bool NearIncompleteTerminal(SurvivorBotView view, float x, float z, float radius)
        {
            for (int i = 0; i < view.TerminalCount; i++)
                if (view.Terminals[i].Progress < 100 && Distance(x, z, view.Terminals[i].X, view.Terminals[i].Z) <= radius) return true;
            return false;
        }

        // Run: visible while it moves, so it goes for a refuge and then holds still and Silent.
        private void StartFlee(SurvivorBotView view, NavGrid nav, FixedMap map, Random random, bool known)
        {
            double t = view.Time;
            float kd = known ? Distance(view.X, view.Z, _killerX, _killerZ) : 0;
            var spot = PickRefuge(view, nav, map, random, known, FleeMinReach, FleeMaxReach);
            Plan(spot.X, spot.Z, StopDistance, false);
            _fleeUntil = t + Uniform(random, FleeMin, FleeMax);
            _fleeing = true;
            _hiding = false;
            _interrupted = true;
            if (_terminal >= 0) { _avoidTerminal = _terminal; _avoidUntil = t + AvoidTerminalSeconds; }
            _terminal = -1;
            _wantSkill = view.Skill == SkillIds.DummyPacket || view.Skill == SkillIds.StealthRelay || view.Skill == SkillIds.WhiteNoise ||
                (view.Skill == SkillIds.TrafficOverload && known && kd <= 8f);
        }

        // The nearest Lagging teammate within RescueRange when this bot is the closest Normal survivor to it (ties: lower
        // Id), the killer has not been seen for RescueQuietSeconds and ThreatLevel is at most 1. Index into view.Team.
        private int RescueTarget(SurvivorBotView view, double t)
        {
            if (view.Threat > 1 || KillerSeenWithin(t, RescueQuietSeconds)) return -1;
            int best = -1;
            float bestDistance = RescueRange;
            for (int i = 0; i < view.Team.Count; i++)
            {
                var ally = view.Team[i];
                if (ally.Id == view.SelfId || ally.Status != (byte)PlayerStatus.Lagging) continue;
                float mine = Distance(view.X, view.Z, ally.X, ally.Z);
                if (mine >= bestDistance) continue;
                bool closer = false;
                foreach (var other in view.Team)
                {
                    if (other.Id == view.SelfId || other.Status != (byte)PlayerStatus.Normal) continue;
                    float theirs = Distance(other.X, other.Z, ally.X, ally.Z);
                    if (theirs < mine || (theirs == mine && other.Id < view.SelfId)) { closer = true; break; }
                }
                if (closer) continue;
                bestDistance = mine;
                best = i;
            }
            return best;
        }

        private int Occupants(SurvivorBotView view, int terminal)
        {
            int count = 0;
            foreach (var other in view.Team)
                if (other.Id != view.SelfId && other.Status == (byte)PlayerStatus.Normal &&
                    Distance(other.X, other.Z, view.Terminals[terminal].X, view.Terminals[terminal].Z) <= OccupiedRadius) ++count;
            return count;
        }

        // Keeps its terminal while it is incomplete and not near where the killer was just seen; otherwise picks the
        // nearest incomplete terminal with room, limiting how many terminals the team works at once.
        private int ChooseTerminal(SurvivorBotView view, double t, Random random)
        {
            int rank = 0, normal = 0;
            foreach (var entry in view.Team)
            {
                if (entry.Status != (byte)PlayerStatus.Normal) continue;
                ++normal;
                if (entry.Id != view.SelfId && entry.Id < view.SelfId &&
                    _teammateActiveAt.TryGetValue(entry.Id, out double active) && t - active <= TeammateActiveSeconds) ++rank;
            }
            // More hands as the match clock runs down (every client shows the clock).
            int workers = (normal >= LargeTeam ? ActiveWorkers + 1 : ActiveWorkers) + (t >= UrgentAt ? 1 : 0) + (t >= HurryAt ? 1 : 0);
            if (rank >= workers) { _terminal = -1; return -1; }
            bool danger = KillerSeenWithin(t, TerminalDangerMemorySeconds);
            if (_terminal >= 0 && _terminal < view.TerminalCount && view.Terminals[_terminal].Progress < 100 &&
                !(danger && Distance(view.Terminals[_terminal].X, view.Terminals[_terminal].Z, _killerX, _killerZ) < TerminalDangerRadius))
                return _terminal;
            _terminal = -1;
            float best = float.MaxValue;
            for (int pass = 0; pass < 2 && _terminal < 0; pass++)
                for (int i = 0; i < view.TerminalCount; i++)
                {
                    var terminal = view.Terminals[i];
                    if (terminal.Progress >= 100) continue;
                    if (pass == 0 && i == _avoidTerminal && t < _avoidUntil) continue;
                    if (danger && Distance(terminal.X, terminal.Z, _killerX, _killerZ) < TerminalDangerRadius) continue;
                    int occupants = Occupants(view, i);
                    if (occupants >= TerminalCap) continue;
                    float score = Distance(view.X, view.Z, terminal.X, terminal.Z) + (float)random.NextDouble() * 4f;
                    if (score < best) { best = score; _terminal = i; }
                }
            return _terminal;
        }

        // A random lattice spot (NavGrid.Spots) at least HideoutDistance away (the farthest one if none is).
        private void PickHideout(SurvivorBotView view, NavGrid nav, Random random)
        {
            int options = nav.Spots.Count;
            if (options == 0) { _hideX = view.X; _hideZ = view.Z; return; }
            int start = random.Next(options);
            float bestDistance = -1;
            for (int k = 0; k < options; k++)
            {
                var spot = nav.Spots[(start + k) % options];
                float distance = Distance(view.X, view.Z, spot.X, spot.Z);
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                _hideX = spot.X;
                _hideZ = spot.Z;
                if (distance >= HideoutDistance) break;
            }
        }

        // Lagging: always visible and slow, so it moves openly toward the nearest Normal teammate, away from a killer it sees.
        private void UpdateLagging(ref BotIntent intent, SurvivorBotView view, NavGrid nav, FixedMap map, Random random)
        {
            double t = view.Time;
            if (view.KillerVisible && Distance(view.X, view.Z, view.KillerX, view.KillerZ) <= 6f)
            {
                float dx = view.X - view.KillerX, dz = view.Z - view.KillerZ;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len > 0.01f && MoveTo(ref intent, nav, view.X, view.Z, view.X + dx / len * 3, view.Z + dz / len * 3, 0.2f, view.Speed, t, random)) { }
                return;
            }
            float best = float.MaxValue, tx = view.X, tz = view.Z;
            foreach (var ally in view.Team)
            {
                if (ally.Id == view.SelfId || ally.Status != (byte)PlayerStatus.Normal) continue;
                float d = Distance(view.X, view.Z, ally.X, ally.Z);
                if (d < best) { best = d; tx = ally.X; tz = ally.Z; }
            }
            if (best == float.MaxValue) Stop(ref intent, view.X, view.Z);
            else MoveTo(ref intent, nav, view.X, view.Z, tx, tz, StopDistance, view.Speed, t, random);
        }

        private void UseSkills(ref BotIntent intent, SurvivorBotView view)
        {
            if (!view.SkillReady) return;
            // A flee skill is wanted only while that flee lasts. A decoy also covers the start of a hack burst (hacking is
            // Signal, so fake Signals elsewhere make the real one harder to pick).
            bool use = _wantSkill && view.Time < _fleeUntil;
            if (_mode != ModeHack) _coverHack = false;
            else if (_coverHack && (view.Skill == SkillIds.DummyPacket || view.Skill == SkillIds.WhiteNoise))
            {
                use = true;
                _coverHack = false;
            }
            if (view.Skill == SkillIds.EmergencyRestore)
            {
                use = false;
                foreach (var ally in view.Team)
                    if (ally.Id != view.SelfId && ally.Status == (byte)PlayerStatus.Lagging &&
                        Distance(view.X, view.Z, ally.X, ally.Z) <= SkillIds.EmergencyRestoreRadius) use = true;
            }
            else if (view.Skill == SkillIds.StealthRelay && _mode == ModeHack && view.Threat >= 1)
            {
                foreach (var ally in view.Team)
                    if (ally.Id != view.SelfId && ally.Status == (byte)PlayerStatus.Normal &&
                        Distance(view.X, view.Z, ally.X, ally.Z) <= SkillIds.StealthRelayRadius) use = true;
            }
            if (!use) return;
            intent.UseSkill = true;
            _wantSkill = false;
        }
    }
}
