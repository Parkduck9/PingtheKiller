using System;
using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // GDD v2 section 8 bot layer, applied after the role brain decided. It reads only the role's view (fair-information
    // rule: shutter states are public, the killer is used only while visible to the survivor, the killer reads blips)
    // and may override the brain's intent for this tick. Rail vaults on an ordinary route come from BotBrain.MoveTo
    // (A* links); the chase plans below make the props a deliberate tool (Q5a attempt 2, QA finding F2).
    internal sealed class ChaseTactics
    {
        // Survivor: the killer visible within this range counts as a chase.
        internal const float ChasedDistance = 8f;
        // A prop is planned for when its approach point is this close and the survivor reaches it first by a margin.
        internal const float PlanReach = 9f;
        internal const float ArriveMarginMeters = 1.0f;
        internal const float PlanDetourFactor = 1.4f;
        internal const double PlanCheckSeconds = 0.25;
        internal const double PlanSlackSeconds = 2.5;
        // Pull once the killer is this close to the shutter box (4.6 m/s x 0.3 s pull lands it inside the 1.0 m stun
        // zone), or after waiting PullWaitSeconds across the gap.
        internal const float PullTriggerDistance = 2.3f;
        internal const double PullWaitSeconds = 2.0;
        // Killer: break a Down shutter when going around is this much longer than going through.
        internal const float BreakDetourMeters = 8f;
        internal const float BreakMemorySeconds = 3f;
        internal const float BreakSightDistance = 12f;
        internal const float BreakPlanReach = 6f;
        internal const double BreakApproachSeconds = 3.0;
        // Killer: a target that crossed the same rail LoopFlips times within LoopSeconds is looping it; going around
        // again only gets mirrored, so the killer vaults after it from within LoopApproach metres of the vault point.
        internal const int LoopFlips = 2;
        internal const double LoopSeconds = 12;
        internal const float LoopRailReach = 4f;
        internal const float LoopApproach = 4f;

        private const float KillerSpeedRatio = GameConstants.KillerSpeed / GameConstants.SurvivorSpeed;
        private const int PlanNone = 0, PlanPull = 1, PlanRail = 2, PlanDownShutter = 3;

        private int _plan = PlanNone, _planId = -1;
        private float _planX, _planZ;
        private double _planUntil, _planCheckedAt = double.NegativeInfinity, _acrossAt = double.NegativeInfinity;
        private int _breakShutter = -1;
        private double _breakUntil;
        private bool _breakApproach;
        private float _breakX, _breakZ;
        private float _targetX, _targetZ;
        private double _targetSeenAt = double.NegativeInfinity;
        private double _breakCheckedAt = double.NegativeInfinity;
        private int _loopRail = -1, _loopFlips;
        private float _loopSide;
        private double _loopSince;
        private readonly List<MapPoint> _path = new List<MapPoint>(128);

        // Survivor: when chased, pick an Up shutter, a rail or a Down shutter on its side of the killer that it reaches
        // first, run to it, and either pull the shutter behind itself once the killer is close or vault across.
        internal void Survivor(SurvivorBotView view, NavGrid nav, ref BotIntent intent)
        {
            if (view.Status != PlayerStatus.Normal || !view.KillerVisible ||
                Distance(view.X, view.Z, view.KillerX, view.KillerZ) > ChasedDistance)
            {
                _plan = PlanNone;
                _pressedLast = false;
                return;
            }
            // A vault that leaves the prop between us and the killer lengthens the gap.
            for (int i = 0; i < WorldChaseProps.Rails.Count; i++)
            {
                var rail = WorldChaseProps.Rails[i];
                if (Distance(view.X, view.Z, rail.CenterX, rail.CenterZ) <= GameConstants.RailVaultRadius - 0.05f &&
                    SameSide(rail, view.X, view.Z, view.KillerX, view.KillerZ))
                { Press(ref intent); return; }
            }
            var shutters = WorldChaseProps.Shutters;
            for (int i = 0; i < shutters.Count; i++)
            {
                if (ShutterOf(view.Shutters, i) != ShutterState.Down) continue;
                if (shutters[i].Distance(view.X, view.Z) <= GameConstants.ShutterVaultRadius - 0.05f &&
                    SameSide(shutters[i], view.X, view.Z, view.KillerX, view.KillerZ))
                { Press(ref intent); return; }
            }
            if (_plan != PlanNone && !PlanStillGood(view)) _plan = PlanNone;
            if (_plan == PlanNone && view.Time - _planCheckedAt >= PlanCheckSeconds)
            {
                _planCheckedAt = view.Time;
                ChoosePlan(view, nav);
            }
            if (_plan == PlanNone) return;
            if (_plan == PlanPull) { Pull(view, nav, ref intent); return; }
            // Rail or Down shutter: run to the approach point; the vault checks above press once it is within reach.
            Route(ref intent, nav, view.X, view.Z, _planX, _planZ);
            intent.Interact = false;
            _pressedLast = false;
        }

        private bool PlanStillGood(SurvivorBotView view)
        {
            if (view.Time > _planUntil) return false;
            var prop = _plan == PlanRail ? WorldChaseProps.Rails[_planId] : WorldChaseProps.Shutters[_planId];
            var state = _plan == PlanRail ? ShutterState.Up : ShutterOf(view.Shutters, _planId);
            if (_plan == PlanPull && state != ShutterState.Up) return false;
            if (_plan == PlanDownShutter && state != ShutterState.Down) return false;
            // A pull plan already across the gap waits for the killer; otherwise the killer must not cut us off.
            if (_plan == PlanPull && Across(prop, view)) return true;
            return Distance(view.KillerX, view.KillerZ, _planX, _planZ) >= Distance(view.X, view.Z, _planX, _planZ) - 0.3f;
        }

        private void ChoosePlan(SurvivorBotView view, NavGrid nav)
        {
            float best = float.MaxValue;
            var shutters = WorldChaseProps.Shutters;
            for (int i = 0; i < shutters.Count; i++)
            {
                var state = ShutterOf(view.Shutters, i);
                if (state == ShutterState.Broken) continue;
                bool up = state == ShutterState.Up;
                float depth = shutters[i].HalfThickness + FixedMap.PlayerRadius + (up ? 0.2f : 0.25f);
                Consider(view, nav, shutters[i], i, up ? PlanPull : PlanDownShutter, depth, ref best);
            }
            var rails = WorldChaseProps.Rails;
            for (int i = 0; i < rails.Count; i++)
                Consider(view, nav, rails[i], i, PlanRail, rails[i].HalfThickness + FixedMap.PlayerRadius + 0.25f, ref best);
        }

        private void Consider(SurvivorBotView view, NavGrid nav, ChaseProp prop, int id, int kind, float depth, ref float best)
        {
            if (!SameSide(prop, view.X, view.Z, view.KillerX, view.KillerZ)) return;
            float side = prop.Side(view.X, view.Z) >= 0 ? 1 : -1;
            float along = 0;
            if (kind != PlanRail)
            {
                along = (view.X - prop.CenterX) * prop.CrossZ + (view.Z - prop.CenterZ) * prop.CrossX;
                along = Math.Max(-(prop.HalfLength - 0.6f), Math.Min(prop.HalfLength - 0.6f, along));
            }
            float ax = prop.CenterX + prop.CrossZ * along + prop.CrossX * side * depth;
            float az = prop.CenterZ + prop.CrossX * along + prop.CrossZ * side * depth;
            float mine = Distance(view.X, view.Z, ax, az);
            if (mine > PlanReach || mine >= best) return;
            // We must get there first: the killer is faster, so it needs the longer way by a margin.
            if (Distance(view.KillerX, view.KillerZ, ax, az) < mine * KillerSpeedRatio + ArriveMarginMeters) return;
            if (nav != null && !nav.LineClear(view.X, view.Z, ax, az))
            {
                if (!nav.FindPath(view.X, view.Z, ax, az, _path) || Length(_path) > mine * PlanDetourFactor + 1f) return;
            }
            best = mine;
            _plan = kind;
            _planId = id;
            _planX = ax;
            _planZ = az;
            _planUntil = view.Time + mine / GameConstants.SurvivorSpeed + PlanSlackSeconds;
            _acrossAt = double.NegativeInfinity;
        }

        private static bool Across(ChaseProp shutter, SurvivorBotView view)
        {
            float killerSide = shutter.Side(view.KillerX, view.KillerZ) >= 0 ? 1 : -1;
            return shutter.Side(view.X, view.Z) * killerSide < 0 &&
                Math.Abs(shutter.Side(view.X, view.Z)) >= shutter.HalfThickness + FixedMap.PlayerRadius;
        }

        // Run through the gap to a spot just past the shutter on the far side, then pull it once the killer is close
        // behind (or after PullWaitSeconds).
        private void Pull(SurvivorBotView view, NavGrid nav, ref BotIntent intent)
        {
            var shutter = WorldChaseProps.Shutters[_planId];
            float killerSide = shutter.Side(view.KillerX, view.KillerZ) >= 0 ? 1 : -1;
            if (Across(shutter, view) && shutter.Distance(view.X, view.Z) <= GameConstants.ShutterPullRadius - 0.1f)
            {
                if (double.IsNegativeInfinity(_acrossAt)) { _acrossAt = view.Time; _planUntil = view.Time + PullWaitSeconds + 1; }
                intent.DirX = intent.DirZ = 0;
                intent.Interact = false;
                if (shutter.Distance(view.KillerX, view.KillerZ) <= PullTriggerDistance ||
                    Distance(view.X, view.Z, view.KillerX, view.KillerZ) <= PullTriggerDistance + 1f ||
                    view.Time - _acrossAt >= PullWaitSeconds)
                    Press(ref intent);
                else _pressedLast = false;
                return;
            }
            float along = (view.X - shutter.CenterX) * shutter.CrossZ + (view.Z - shutter.CenterZ) * shutter.CrossX;
            along = Math.Max(-(shutter.HalfLength - 0.6f), Math.Min(shutter.HalfLength - 0.6f, along));
            float depth = shutter.HalfThickness + 1.0f;
            float tx = shutter.CenterX + shutter.CrossZ * along - shutter.CrossX * killerSide * depth;
            float tz = shutter.CenterZ + shutter.CrossX * along - shutter.CrossZ * killerSide * depth;
            Route(ref intent, nav, view.X, view.Z, tx, tz);
            intent.Interact = false;
            _pressedLast = false;
        }

        // Killer: when its target is across a Down shutter close by and going around is more than BreakDetourMeters
        // longer than going through, walk to the shutter and break it; otherwise the brain's route goes around.
        internal void Killer(KillerBotView view, NavGrid nav, ref BotIntent intent)
        {
            var shutters = WorldChaseProps.Shutters;
            float nearest = BreakSightDistance;
            for (int i = 0; i < view.Blips.Count; i++)
            {
                var blip = view.Blips[i];
                float distance = Distance(view.X, view.Z, blip.X, blip.Z);
                if (blip.Lagging || distance > nearest) continue;
                nearest = distance;
                _targetX = blip.X;
                _targetZ = blip.Z;
                _targetSeenAt = view.Time;
            }
            if (_breakShutter < 0 && LoopVault(view, nav, ref intent)) return;
            if (_breakShutter >= 0)
            {
                var s = shutters[_breakShutter];
                if (ShutterOf(view.Shutters, _breakShutter) == ShutterState.Down && view.Time < _breakUntil)
                {
                    if (s.Distance(view.X, view.Z) <= GameConstants.ShutterBreakRadius - 0.1f)
                    {
                        if (_breakApproach)
                        {
                            _breakApproach = false;
                            _breakUntil = view.Time + GameConstants.ShutterBreakSeconds + 0.5;
                        }
                        intent.DirX = intent.DirZ = 0;
                        intent.Interact = true;
                        return;
                    }
                    if (_breakApproach)
                    {
                        Route(ref intent, nav, view.X, view.Z, _breakX, _breakZ);
                        intent.Interact = false;
                        return;
                    }
                }
                _breakShutter = -1;
                _breakApproach = false;
            }
            if (view.Time - _targetSeenAt > BreakMemorySeconds || view.Time - _breakCheckedAt < 0.5) return;
            _breakCheckedAt = view.Time;
            for (int i = 0; i < shutters.Count; i++)
            {
                if (ShutterOf(view.Shutters, i) != ShutterState.Down) continue;
                var s = shutters[i];
                float reach = s.Distance(view.X, view.Z);
                if (reach > BreakPlanReach) continue;
                if (SameSide(s, view.X, view.Z, _targetX, _targetZ)) continue;
                // The target must lie beyond this shutter, not merely somewhere on the other side of its line.
                if (s.Distance(_targetX, _targetZ) > BreakSightDistance) continue;
                // The near-side spot in front of the shutter, closest to the killer.
                float side = s.Side(view.X, view.Z) >= 0 ? 1 : -1;
                float along = (view.X - s.CenterX) * s.CrossZ + (view.Z - s.CenterZ) * s.CrossX;
                along = Math.Max(-(s.HalfLength - 0.3f), Math.Min(s.HalfLength - 0.3f, along));
                float depth = s.HalfThickness + FixedMap.PlayerRadius + 0.3f;
                float bx = s.CenterX + s.CrossZ * along + s.CrossX * side * depth;
                float bz = s.CenterZ + s.CrossX * along + s.CrossZ * side * depth;
                bool atShutter = reach <= GameConstants.ShutterBreakRadius - 0.1f;
                float through = atShutter ? Distance(view.X, view.Z, _targetX, _targetZ)
                    : Distance(view.X, view.Z, bx, bz) + Distance(bx, bz, _targetX, _targetZ);
                float around = nav.FindPath(view.X, view.Z, _targetX, _targetZ, _path) ? Length(_path) : float.MaxValue;
                if (around - through <= BreakDetourMeters) continue;
                _breakShutter = i;
                _breakApproach = !atShutter;
                _breakX = bx;
                _breakZ = bz;
                _breakUntil = view.Time + (atShutter ? GameConstants.ShutterBreakSeconds + 0.5 : BreakApproachSeconds);
                if (atShutter)
                {
                    intent.DirX = intent.DirZ = 0;
                    intent.Interact = true;
                }
                else
                {
                    Route(ref intent, nav, view.X, view.Z, bx, bz);
                    intent.Interact = false;
                }
                return;
            }
        }

        // Tracks the target's side of the rail nearest to it; once it is looping that rail, vault after it.
        private bool LoopVault(KillerBotView view, NavGrid nav, ref BotIntent intent)
        {
            if (view.Time - _targetSeenAt > 1) { _loopRail = -1; return false; }
            var rails = WorldChaseProps.Rails;
            int nearest = -1;
            float best = LoopRailReach;
            for (int i = 0; i < rails.Count; i++)
            {
                float d = Distance(_targetX, _targetZ, rails[i].CenterX, rails[i].CenterZ);
                if (d < best) { best = d; nearest = i; }
            }
            if (nearest < 0) { _loopRail = -1; return false; }
            var rail = rails[nearest];
            float side = rail.Side(_targetX, _targetZ) >= 0 ? 1 : -1;
            if (nearest != _loopRail || view.Time - _loopSince > LoopSeconds)
            {
                _loopRail = nearest;
                _loopSide = side;
                _loopFlips = 0;
                _loopSince = view.Time;
            }
            else if (side != _loopSide)
            {
                _loopSide = side;
                _loopFlips++;
            }
            if (_loopFlips < LoopFlips || SameSide(rail, view.X, view.Z, _targetX, _targetZ)) { _pressedLast = false; return false; }
            float reach = Distance(view.X, view.Z, rail.CenterX, rail.CenterZ);
            if (reach > LoopApproach) return false;
            if (reach <= GameConstants.RailVaultRadius - 0.05f) { Press(ref intent); return true; }
            float mine = rail.Side(view.X, view.Z) >= 0 ? 1 : -1;
            float depth = rail.HalfThickness + FixedMap.PlayerRadius + 0.25f;
            Route(ref intent, nav, view.X, view.Z, rail.CenterX + rail.CrossX * mine * depth, rail.CenterZ + rail.CrossZ * mine * depth);
            intent.Interact = false;
            _pressedLast = false;
            return true;
        }

        // A press must follow a released tick: alternate so a refused press (no room to land) is retried as a new press.
        private bool _pressedLast;

        private void Press(ref BotIntent intent)
        {
            intent.DirX = intent.DirZ = 0;
            intent.Interact = !_pressedLast;
            _pressedLast = intent.Interact;
        }

        // Straight at the point when the way is clear, else toward the first corner of the A* route (no vault links).
        private void Route(ref BotIntent intent, NavGrid nav, float x, float z, float tx, float tz)
        {
            if (nav != null && !nav.LineClear(x, z, tx, tz) && nav.FindPath(x, z, tx, tz, _path) && _path.Count >= 2)
            {
                for (int i = 1; i < _path.Count; i++)
                {
                    if (Distance(x, z, _path[i].X, _path[i].Z) < 0.1f) continue;
                    tx = _path[i].X;
                    tz = _path[i].Z;
                    break;
                }
            }
            Aim(ref intent, x, z, tx, tz);
        }

        private static void Aim(ref BotIntent intent, float x, float z, float tx, float tz)
        {
            float dx = tx - x, dz = tz - z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.05f) { intent.DirX = intent.DirZ = 0; return; }
            intent.DirX = dx / length;
            intent.DirZ = dz / length;
            float yaw = (float)(Math.Atan2(dx, dz) * 180 / Math.PI);
            intent.Yaw = yaw < 0 ? yaw + 360 : yaw;
        }

        private static bool SameSide(ChaseProp prop, float ax, float az, float bx, float bz) =>
            prop.Side(ax, az) * prop.Side(bx, bz) > 0;

        internal static ShutterState ShutterOf(byte[] states, int id) =>
            states == null || id >= states.Length ? ShutterState.Up : (ShutterState)states[id];

        private static float Length(List<MapPoint> path)
        {
            float total = 0;
            for (int i = 1; i < path.Count; i++) total += Distance(path[i - 1].X, path[i - 1].Z, path[i].X, path[i].Z);
            return total;
        }

        private static float Distance(float x, float z, float tx, float tz)
        {
            float dx = x - tx, dz = z - tz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
