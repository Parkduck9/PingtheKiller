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
        // Closes a visible hatch from this close (inside GameConstants.HatchRadius).
        private const float HatchReach = GameConstants.HatchRadius - 0.3f;

        private double _thinkAt, _targetUntil, _lookUntil, _patrolUntil;
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
            // GDD v2 §4: a Lagging survivor cannot be touched into the next stage, so Lagging blips are never targets.
            if (t >= _thinkAt) Look(view, random);
            if (view.HatchVisible)
            {
                // GDD v2 §3: a hatch in sight is closed at once (hold Interact standing still).
                if (Distance(view.X, view.Z, view.HatchX, view.HatchZ) <= HatchReach)
                {
                    Stop(ref intent, view.X, view.Z);
                    intent.Interact = true;
                }
                else MoveTo(ref intent, nav, view.X, view.Z, view.HatchX, view.HatchZ, HatchReach - 0.2f, view.Speed, t, random);
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

        // Terminals that are not done weigh three times as much as done ones; powered exits weigh most.
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
                bool powered = Powered(view);
                if (powered) total += 4;
                double pick = random.NextDouble() * total;
                _patrol = view.TerminalCount;
                for (int i = 0; i < view.TerminalCount; i++)
                {
                    pick -= view.Terminals[i].Done ? 1 : 3;
                    if (pick < 0) { _patrol = i; break; }
                }
                if (_patrol == view.TerminalCount && !powered) _patrol = view.TerminalCount - 1;
                float cx, cz;
                if (_patrol < view.TerminalCount) { cx = view.Terminals[_patrol].X; cz = view.Terminals[_patrol].Z; }
                else
                {
                    // A powered exit: its switch, where survivors have to stand.
                    var exit = view.Exits[random.Next(view.ExitCount)];
                    cx = exit.SwitchX;
                    cz = exit.SwitchZ;
                }
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
                    for (int i = 0; i < view.ExitCount; i++)
                    {
                        var exit = view.Exits[i];
                        if (exit.Open || exit.Progress == 0) continue;
                        foreach (var blip in view.Blips)
                            if (!blip.Lagging && Distance(blip.X, blip.Z, exit.SwitchX, exit.SwitchZ) <= FirewallExitDistance) intent.UseSkill = true;
                    }
                    break;
            }
        }

        private static bool Powered(KillerBotView view)
        {
            for (int i = 0; i < view.ExitCount; i++) if (view.Exits[i].Powered) return true;
            return false;
        }
    }
}
