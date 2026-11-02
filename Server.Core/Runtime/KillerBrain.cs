using System;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // The killer bot. It decides only from its KillerBotView: anonymous blips by position and status (phantoms and
    // afterimages look the same as real Signals), terminals as done or not done, and the exit. It is imperfect like a
    // human: it looks at the view only once per reaction interval, notices blips mainly in front of it (first-person view),
    // chases the last blip it noticed, looks around where the trail ends, and patrols terminals that are not done.
    internal sealed class KillerBrain : BotBrain
    {
        private const double ReactionMin = 0.4, ReactionMax = 0.9;
        private const double ChaseMemorySeconds = 5;
        private const double LookMin = 1.5, LookMax = 4;
        private const float LookTurnDegreesPerSecond = 120f;
        // First-person attention: blips inside the half-angle in front, or within NoticeRadius in any direction.
        private const float FovHalfDegrees = 60f, NoticeRadius = 5f, NoticeRange = 20f;
        private const double MissChance = 0.3;
        // Difficulty scales with the lobby, read from the terminals placed (GameConstants.TerminalsFor: four means one or
        // two survivors, five means three to five): against a small team it is less perceptive, so a lone survivor is not
        // found at once.
        private const int SmallLobbyTerminals = 4, MidLobbyTerminals = 5;
        private const float SmallLobbyNoticeRange = 11f, MidLobbyNoticeRange = 12f;
        private const double SmallLobbyMissChance = 0.55, MidLobbyMissChance = 0.5;
        // Where it thinks a noticed blip was is off by up to this much (reading a blip off the view is not exact).
        private const float SmallLobbyAimError = 2.2f, MidLobbyAimError = 1.2f, AimError = 0.3f;
        private const float ArriveDistance = 0.8f;
        private const double PatrolTimeout = 12;
        private const float PatrolStandOff = 3f;
        private const double ScannerDelaySeconds = 1;
        private const float OverclockMinDistance = 3f, OverclockMaxDistance = 10f;
        private const float FirewallExitDistance = 12f;
        private const float ExecuteRange = 6f, ExecuteTrack = 3f, ExecuteWaitDistance = 1.8f, DeclineRadius = 4f;
        // A big lobby (six terminals placed means six or seven survivors) is thinned out more eagerly.
        private const double ExecuteChance = 0.6, BigLobbyExecuteChance = 0.85, ExecuteCampSeconds = 8, DeclineSeconds = 20;
        private const int BigLobbyTerminals = 6;

        private double _thinkAt, _targetUntil, _lookUntil, _patrolUntil, _execUntil, _declinedUntil;
        private bool _executing;
        private float _execX, _execZ, _declinedX, _declinedZ;
        private double _lastBlipAt = double.NegativeInfinity;
        private bool _hasTarget;
        private float _targetX, _targetZ;
        private int _patrol = -1;
        private float _patrolX, _patrolZ;

        internal BotIntent Update(KillerBotView view, NavGrid nav, FixedMap map, Random random)
        {
            var intent = new BotIntent { Yaw = view.Yaw };
            double t = view.Time;
            if (!Started)
            {
                Started = true;
                Face(ref intent, view.Yaw);
            }
            bool anyNormal = false;
            foreach (var blip in view.Blips) if (!blip.Lagging) anyNormal = true;
            if (anyNormal) _lastBlipAt = t;
            if (t >= _thinkAt) { Look(view, random); WatchLagging(view, random); }
            if (_executing && t >= _execUntil)
            {
                // Done camping: leave that Lagging survivor to its fate (or a rescue) for a while.
                _executing = false;
                _declinedX = _execX;
                _declinedZ = _execZ;
                _declinedUntil = t + DeclineSeconds;
            }
            if (_executing)
            {
                Execute(ref intent, view, nav, random);
                UseSkills(ref intent, view);
                return intent;
            }

            if (_hasTarget && (Distance(view.X, view.Z, _targetX, _targetZ) < ArriveDistance || t >= _targetUntil))
            {
                _hasTarget = false;
                _lookUntil = t + Uniform(random, LookMin, LookMax);
            }
            if (_hasTarget) MoveTo(ref intent, nav, view.X, view.Z, _targetX, _targetZ, 0, view.Speed, t, random);
            else if (t < _lookUntil)
            {
                Stop(ref intent, view.X, view.Z);
                Face(ref intent, Facing + LookTurnDegreesPerSecond / GameConstants.TickRate);
            }
            else Patrol(ref intent, view, nav, map, random);
            UseSkills(ref intent, view);
            return intent;
        }

        // A Lagging blip close by (usually the one just captured) is executed with ExecuteChance: the bot waits out the
        // grace (SelfState.ExecutionGraceLeft) a step away, then touches it again. It camps at most ExecuteCampSeconds.
        private void WatchLagging(KillerBotView view, Random random)
        {
            double t = view.Time;
            float best = _executing ? ExecuteTrack : ExecuteRange;
            float cx = _executing ? _execX : view.X, cz = _executing ? _execZ : view.Z;
            bool found = false;
            float bx = 0, bz = 0;
            foreach (var blip in view.Blips)
            {
                if (!blip.Lagging) continue;
                if (!_executing && t < _declinedUntil && Distance(blip.X, blip.Z, _declinedX, _declinedZ) <= DeclineRadius) continue;
                float d = Distance(cx, cz, blip.X, blip.Z);
                if (d > best) continue;
                best = d;
                bx = blip.X;
                bz = blip.Z;
                found = true;
            }
            if (_executing)
            {
                if (found) { _execX = bx; _execZ = bz; }
                else _executing = false;
                return;
            }
            if (!found) return;
            if (random.NextDouble() < (view.TerminalCount >= BigLobbyTerminals ? BigLobbyExecuteChance : ExecuteChance))
            {
                _executing = true;
                _execUntil = t + ExecuteCampSeconds;
                _execX = bx;
                _execZ = bz;
                _hasTarget = false;
            }
            else
            {
                _declinedX = bx;
                _declinedZ = bz;
                _declinedUntil = t + DeclineSeconds;
            }
        }

        private void Execute(ref BotIntent intent, KillerBotView view, NavGrid nav, Random random)
        {
            float d = Distance(view.X, view.Z, _execX, _execZ);
            if (view.ExecutionGraceLeft > 0)
            {
                // Step out of contact while the grace runs; execution needs a fresh touch.
                if (d < ExecuteWaitDistance && d > 0.01f)
                    MoveTo(ref intent, nav, view.X, view.Z, _execX + (view.X - _execX) / d * ExecuteWaitDistance * 1.5f,
                        _execZ + (view.Z - _execZ) / d * ExecuteWaitDistance * 1.5f, 0.1f, view.Speed, view.Time, random);
                else
                {
                    Stop(ref intent, view.X, view.Z);
                    Face(ref intent, YawOf(_execX - view.X, _execZ - view.Z));
                }
                return;
            }
            MoveTo(ref intent, nav, view.X, view.Z, _execX, _execZ, 0, view.Speed, view.Time, random);
        }

        private bool Noticed(KillerBotView view, float x, float z, Random random)
        {
            float d = Distance(view.X, view.Z, x, z);
            if (d <= NoticeRadius) return true;
            float range = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyNoticeRange
                : view.TerminalCount <= MidLobbyTerminals ? MidLobbyNoticeRange : NoticeRange;
            double miss = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyMissChance
                : view.TerminalCount <= MidLobbyTerminals ? MidLobbyMissChance : MissChance;
            if (d > range || random.NextDouble() < miss) return false;
            float delta = Math.Abs(YawOf(x - view.X, z - view.Z) - Facing) % 360f;
            if (delta > 180f) delta = 360f - delta;
            return delta <= FovHalfDegrees;
        }

        // Chases the closest noticed Normal blip, preferring one near the trail it already follows.
        private void Look(KillerBotView view, Random random)
        {
            double t = view.Time;
            _thinkAt = t + Uniform(random, ReactionMin, ReactionMax);
            float best = float.MaxValue;
            bool found = false;
            float bx = 0, bz = 0;
            foreach (var blip in view.Blips)
            {
                if (blip.Lagging || !Noticed(view, blip.X, blip.Z, random)) continue;
                float score = Distance(view.X, view.Z, blip.X, blip.Z);
                if (_hasTarget) score += 0.5f * Distance(blip.X, blip.Z, _targetX, _targetZ);
                if (score >= best) continue;
                best = score;
                bx = blip.X;
                bz = blip.Z;
                found = true;
            }
            if (!found) return;
            float error = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyAimError
                : view.TerminalCount <= MidLobbyTerminals ? MidLobbyAimError : AimError;
            double angle = random.NextDouble() * 2 * Math.PI, reach = random.NextDouble() * error;
            _hasTarget = true;
            _targetX = bx + (float)(Math.Sin(angle) * reach);
            _targetZ = bz + (float)(Math.Cos(angle) * reach);
            _targetUntil = t + ChaseMemorySeconds;
        }

        // Terminals that are not done weigh three times as much as done ones; an open exit weighs most.
        private void Patrol(ref BotIntent intent, KillerBotView view, NavGrid nav, FixedMap map, Random random)
        {
            double t = view.Time;
            if (view.TerminalCount == 0) { Stop(ref intent, view.X, view.Z); return; }
            bool arrived = _patrol >= 0 && Distance(view.X, view.Z, _patrolX, _patrolZ) < ArriveDistance;
            if (arrived)
            {
                _patrol = -1;
                _lookUntil = t + Uniform(random, LookMin, LookMax);
                Stop(ref intent, view.X, view.Z);
                return;
            }
            if (_patrol < 0 || t >= _patrolUntil)
            {
                double total = 0;
                for (int i = 0; i < view.TerminalCount; i++) total += view.Terminals[i].Done ? 1 : 3;
                if (view.ExitOpen) total += 4;
                double pick = random.NextDouble() * total;
                _patrol = view.TerminalCount;
                for (int i = 0; i < view.TerminalCount; i++)
                {
                    pick -= view.Terminals[i].Done ? 1 : 3;
                    if (pick < 0) { _patrol = i; break; }
                }
                if (_patrol == view.TerminalCount && !view.ExitOpen) _patrol = view.TerminalCount - 1;
                float cx = _patrol < view.TerminalCount ? view.Terminals[_patrol].X : view.Exit.X;
                float cz = _patrol < view.TerminalCount ? view.Terminals[_patrol].Z : view.Exit.Z;
                // It checks a place from a few metres off (any standable side), not from on top of it.
                _patrolX = cx;
                _patrolZ = cz;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    double angle = random.NextDouble() * 2 * Math.PI;
                    float x = cx + (float)Math.Sin(angle) * PatrolStandOff, z = cz + (float)Math.Cos(angle) * PatrolStandOff;
                    if (!map.CanStand(x, z)) continue;
                    _patrolX = x;
                    _patrolZ = z;
                    break;
                }
                _patrolUntil = t + PatrolTimeout;
            }
            MoveTo(ref intent, nav, view.X, view.Z, _patrolX, _patrolZ, 0, view.Speed, t, random);
        }

        private void UseSkills(ref BotIntent intent, KillerBotView view)
        {
            if (!view.SkillReady) return;
            switch (view.Skill)
            {
                case SkillIds.PingScanner:
                    intent.UseSkill = view.Time - _lastBlipAt >= ScannerDelaySeconds;
                    break;
                case SkillIds.Overclock:
                    if (_hasTarget)
                    {
                        float d = Distance(view.X, view.Z, _targetX, _targetZ);
                        intent.UseSkill = d >= OverclockMinDistance && d <= OverclockMaxDistance;
                    }
                    break;
                case SkillIds.FirewallBlock:
                    if (!view.ExitOpen) break;
                    foreach (var blip in view.Blips)
                        if (!blip.Lagging && Distance(blip.X, blip.Z, view.Exit.X, view.Exit.Z) <= FirewallExitDistance) intent.UseSkill = true;
                    break;
            }
        }
    }
}
