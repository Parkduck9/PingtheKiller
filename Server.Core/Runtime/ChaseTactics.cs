using System;
using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // GDD v2 section 8 bot layer, applied after the role brain decided. It reads only the role's view (fair-information
    // rule: shutter states are public, the killer is used only while visible to the survivor, the killer reads blips)
    // and may override the brain's intent for this tick. Rail vaults on a route come from BotBrain.MoveTo (A* links).
    internal sealed class ChaseTactics
    {
        internal const float ChasedDistance = 6f;
        internal const float ShutterLookahead = 3f;
        internal const float PullTriggerDistance = 2.0f;
        internal const double PullWaitSeconds = 3.0;
        internal const float BreakDetourMeters = 8f;
        internal const float BreakMemorySeconds = 3f;
        internal const float BreakSightDistance = 10f;
        // Bot use rate (Q5a balance): a survivor runs to a rail only with the killer this close (1v3 sweep was 77 %).
        internal const float RailChasedDistance = 2.5f;

        private int _pullShutter = -1;
        private double _pullPlannedAt;
        private int _breakShutter = -1;
        private double _breakUntil;
        private float _targetX, _targetZ;
        private double _targetSeenAt = double.NegativeInfinity;
        private double _breakCheckedAt = double.NegativeInfinity;
        private readonly List<MapPoint> _path = new List<MapPoint>(128);

        // Survivor: when chased, run past an Up shutter ahead and pull it once the killer is close behind; vault a rail
        // or a Down shutter that puts the prop between itself and the killer.
        internal void Survivor(SurvivorBotView view, ref BotIntent intent)
        {
            if (view.Status != PlayerStatus.Normal || !view.KillerVisible ||
                Distance(view.X, view.Z, view.KillerX, view.KillerZ) > ChasedDistance)
            {
                _pullShutter = -1;
                _pressedLast = false;
                return;
            }
            var shutters = WorldChaseProps.Shutters;
            // A vault that leaves the prop between us and the killer lengthens the gap.
            for (int i = 0; i < WorldChaseProps.Rails.Count; i++)
            {
                var rail = WorldChaseProps.Rails[i];
                if (Distance(view.X, view.Z, rail.CenterX, rail.CenterZ) <= GameConstants.RailVaultRadius - 0.05f &&
                    SameSide(rail, view.X, view.Z, view.KillerX, view.KillerZ))
                { Press(ref intent); return; }
            }
            for (int i = 0; i < shutters.Count; i++)
            {
                if (ShutterOf(view.Shutters, i) != ShutterState.Down) continue;
                if (shutters[i].Distance(view.X, view.Z) <= GameConstants.ShutterVaultRadius - 0.05f &&
                    SameSide(shutters[i], view.X, view.Z, view.KillerX, view.KillerZ))
                { Press(ref intent); return; }
            }
            if (_pullShutter >= 0 && ShutterOf(view.Shutters, _pullShutter) != ShutterState.Up) _pullShutter = -1;
            if (_pullShutter < 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < shutters.Count; i++)
                {
                    if (ShutterOf(view.Shutters, i) != ShutterState.Up) continue;
                    var s = shutters[i];
                    float distance = s.Distance(view.X, view.Z);
                    if (distance > ShutterLookahead || !SameSide(s, view.X, view.Z, view.KillerX, view.KillerZ)) continue;
                    // On the flight path: the shutter lies ahead, away from the killer.
                    float ax = s.CenterX - view.X, az = s.CenterZ - view.Z;
                    float fx = view.X - view.KillerX, fz = view.Z - view.KillerZ;
                    if (ax * fx + az * fz <= 0) continue;
                    if (distance < best) { best = distance; _pullShutter = i; }
                }
                if (_pullShutter < 0) { RunToRail(view, ref intent); return; }
                _pullPlannedAt = view.Time;
            }
            var shutter = shutters[_pullShutter];
            float killerSide = shutter.Side(view.KillerX, view.KillerZ) >= 0 ? 1 : -1;
            bool across = shutter.Side(view.X, view.Z) * killerSide < 0 &&
                Math.Abs(shutter.Side(view.X, view.Z)) >= shutter.HalfThickness + FixedMap.PlayerRadius;
            if (across && shutter.Distance(view.X, view.Z) <= GameConstants.ShutterPullRadius - 0.1f)
            {
                intent.DirX = intent.DirZ = 0;
                intent.Interact = false;
                if (shutter.Distance(view.KillerX, view.KillerZ) <= PullTriggerDistance || view.Time - _pullPlannedAt >= PullWaitSeconds)
                    Press(ref intent);
                else _pressedLast = false;
                return;
            }
            // Run through the gap to a spot 1 m past the shutter on the far side.
            float along = (view.X - shutter.CenterX) * shutter.CrossZ + (view.Z - shutter.CenterZ) * shutter.CrossX;
            along = Math.Max(-(shutter.HalfLength - 0.6f), Math.Min(shutter.HalfLength - 0.6f, along));
            float depth = shutter.HalfThickness + 1.0f;
            float tx = shutter.CenterX + shutter.CrossZ * along - shutter.CrossX * killerSide * depth;
            float tz = shutter.CenterZ + shutter.CrossX * along - shutter.CrossZ * killerSide * depth;
            Aim(ref intent, view.X, view.Z, tx, tz);
            intent.Interact = false;
            _pressedLast = false;
        }

        // No shutter to pull: a rail vault point within ShutterLookahead ahead, with the killer behind on the same side,
        // is run to (the vault check above presses once it is within reach). The killer needs 1.6 s to follow.
        private void RunToRail(SurvivorBotView view, ref BotIntent intent)
        {
            var rails = WorldChaseProps.Rails;
            int pick = -1;
            float best = float.MaxValue;
            for (int i = 0; i < rails.Count; i++)
            {
                var r = rails[i];
                float distance = Distance(view.X, view.Z, r.CenterX, r.CenterZ);
                if (distance > ShutterLookahead || !SameSide(r, view.X, view.Z, view.KillerX, view.KillerZ)) continue;
                float ax = r.CenterX - view.X, az = r.CenterZ - view.Z;
                float fx = view.X - view.KillerX, fz = view.Z - view.KillerZ;
                if (ax * fx + az * fz <= 0) continue;
                if (distance < best) { best = distance; pick = i; }
            }
            if (pick < 0 || Distance(view.X, view.Z, view.KillerX, view.KillerZ) > RailChasedDistance) return;
            var rail = rails[pick];
            float side = rail.Side(view.X, view.Z) >= 0 ? 1 : -1;
            float depth = rail.HalfThickness + FixedMap.PlayerRadius + 0.1f;
            Aim(ref intent, view.X, view.Z, rail.CenterX + rail.CrossX * side * depth, rail.CenterZ + rail.CrossZ * side * depth);
            intent.Interact = false;
            _pressedLast = false;
        }

        // Killer: after losing sight of a survivor across a Down shutter, break it when going around is more than
        // BreakDetourMeters longer than going through; otherwise the brain's route goes around.
        internal void Killer(KillerBotView view, NavGrid nav, ref BotIntent intent)
        {
            var shutters = WorldChaseProps.Shutters;
            for (int i = 0; i < view.Blips.Count; i++)
            {
                var blip = view.Blips[i];
                if (blip.Lagging || Distance(view.X, view.Z, blip.X, blip.Z) > BreakSightDistance) continue;
                _targetX = blip.X;
                _targetZ = blip.Z;
                _targetSeenAt = view.Time;
            }
            if (_breakShutter >= 0)
            {
                if (ShutterOf(view.Shutters, _breakShutter) == ShutterState.Down && view.Time < _breakUntil &&
                    shutters[_breakShutter].Distance(view.X, view.Z) <= GameConstants.ShutterBreakRadius - 0.05f)
                {
                    intent.DirX = intent.DirZ = 0;
                    intent.Interact = true;
                    return;
                }
                _breakShutter = -1;
            }
            if (view.Time - _targetSeenAt > BreakMemorySeconds || view.Time - _breakCheckedAt < 1) return;
            for (int i = 0; i < shutters.Count; i++)
            {
                if (ShutterOf(view.Shutters, i) != ShutterState.Down) continue;
                var s = shutters[i];
                if (s.Distance(view.X, view.Z) > GameConstants.ShutterBreakRadius - 0.05f) continue;
                if (SameSide(s, view.X, view.Z, _targetX, _targetZ)) continue;
                _breakCheckedAt = view.Time;
                float direct = Distance(view.X, view.Z, _targetX, _targetZ);
                float around = nav.FindPath(view.X, view.Z, _targetX, _targetZ, _path) ? Length(_path) : float.MaxValue;
                if (around - direct <= BreakDetourMeters) return;
                _breakShutter = i;
                _breakUntil = view.Time + GameConstants.ShutterBreakSeconds + 0.5;
                intent.DirX = intent.DirZ = 0;
                intent.Interact = true;
                return;
            }
        }

        // A press must follow a released tick: alternate so a refused press (no room to land) is retried as a new press.
        private bool _pressedLast;

        private void Press(ref BotIntent intent)
        {
            intent.DirX = intent.DirZ = 0;
            intent.Interact = !_pressedLast;
            _pressedLast = intent.Interact;
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
