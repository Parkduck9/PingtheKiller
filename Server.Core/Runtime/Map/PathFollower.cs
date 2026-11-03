using System;
using System.Collections.Generic;

namespace PingTheKiller.ServerCore.Map
{
    // Per-bot route state, based exclusively on its own motion and the static map.
    internal sealed class PathFollower
    {
        private readonly List<MapPoint> _path = new List<MapPoint>(128);
        private int _next;
        private float _goalX, _goalZ, _anchorX, _anchorZ;
        private double _plannedAt = double.NegativeInfinity, _progressAt;
        private bool _active;
        internal int Replans { get; private set; }
        internal void Pause() { _active = false; }

        internal void Direction(NavGrid nav, float x, float z, float tx, float tz, float speed, double time,
            out float dx, out float dz)
        {
            dx = dz = 0;
            if (!_active || DistanceSquared(x, z, _anchorX, _anchorZ) >= .04f)
            { _anchorX = x; _anchorZ = z; _progressAt = time; }
            bool stuck = _active && time - _progressAt >= 1.5;
            if (!_active || DistanceSquared(tx, tz, _goalX, _goalZ) > 4 || time - _plannedAt >= 1 || stuck)
            {
                nav.FindPath(x, z, tx, tz, _path);
                _next = 1; _goalX = tx; _goalZ = tz; _plannedAt = time; Replans++;
                if (stuck) { _anchorX = x; _anchorZ = z; _progressAt = time; }
            }
            _active = true;
            // Always aim at the current exact goal once the remaining route can be traversed directly.
            if (nav.LineClear(x, z, tx, tz)) { Aim(x, z, tx, tz, speed, out dx, out dz); return; }
            while (_next < _path.Count && DistanceSquared(x, z, _path[_next].X, _path[_next].Z) < .01f) _next++;
            while (_next + 1 < _path.Count && nav.LineClear(x, z, _path[_next + 1].X, _path[_next + 1].Z)) _next++;
            if (_next < _path.Count) Aim(x, z, _path[_next].X, _path[_next].Z, speed, out dx, out dz);
        }
        private static float DistanceSquared(float x, float z, float tx, float tz) => (tx - x) * (tx - x) + (tz - z) * (tz - z);
        private static void Aim(float x, float z, float tx, float tz, float speed, out float dx, out float dz)
        {
            float distance = (float)Math.Sqrt(DistanceSquared(x, z, tx, tz));
            float divisor = Math.Max(distance, speed / Shared.Game.GameConstants.TickRate);
            if (divisor < .0001f) { dx = dz = 0; return; }
            dx = (tx - x) / divisor; dz = (tz - z) / divisor;
        }
    }
}
