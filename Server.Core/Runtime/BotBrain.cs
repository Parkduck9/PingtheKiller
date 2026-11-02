using System;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // Shared movement for both bot brains: the NavGrid route with a short random sidestep when progress stalls (the policy
    // phantoms copy in GameServer.Phantoms). Uses only the bot's own position and speed. All randomness comes from the
    // private bot stream passed in by the server.
    internal abstract class BotBrain
    {
        internal const float StopDistance = 1.0f;
        internal const float StuckFraction = 0.3f;

        // The server tick of the last update; a gap means the slot was a human meanwhile, so the brain starts over.
        internal uint LastTick;
        internal bool Started;

        private float _lastX, _lastZ;
        private bool _movedLastTick;
        private double _detourUntil;
        private float _detourX, _detourZ;
        private float _yaw;

        protected static double Uniform(Random random, double min, double max) => min + random.NextDouble() * (max - min);

        protected static float Distance(float x, float z, float tx, float tz)
        {
            float dx = x - tx, dz = z - tz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        protected static float YawOf(float dx, float dz)
        {
            float yaw = (float)(Math.Atan2(dx, dz) * 180 / Math.PI);
            return yaw < 0 ? yaw + 360 : yaw;
        }

        protected float Facing => _yaw;

        protected void Stop(ref BotIntent intent, float x, float z)
        {
            _detourUntil = 0;
            intent.DirX = intent.DirZ = 0;
            intent.Yaw = _yaw;
            _movedLastTick = false;
            _lastX = x;
            _lastZ = z;
        }

        protected void Face(ref BotIntent intent, float yaw)
        {
            _yaw = ((yaw % 360) + 360) % 360;
            intent.Yaw = _yaw;
        }

        // Steps toward (tx, tz) along the NavGrid route. Returns true (and stops) when within stop distance.
        protected bool MoveTo(ref BotIntent intent, NavGrid nav, float x, float z, float tx, float tz, float stop,
            float speed, double time, Random random)
        {
            float distance = Distance(x, z, tx, tz);
            if (distance <= Math.Max(stop, 0.05f) || !nav.Steer(x, z, tx, tz, out float dx, out float dz))
            {
                Stop(ref intent, x, z);
                return true;
            }
            float expected = speed / GameConstants.TickRate;
            float moved = Distance(x, z, _lastX, _lastZ);
            if (_movedLastTick && expected > 0 && moved < expected * StuckFraction && time >= _detourUntil)
            {
                float side = random.Next(2) == 0 ? 1 : -1;
                _detourX = -dz * side;
                _detourZ = dx * side;
                _detourUntil = time + 0.5 + random.NextDouble() * 0.5;
            }
            if (time < _detourUntil) { dx = _detourX; dz = _detourZ; }
            intent.DirX = dx;
            intent.DirZ = dz;
            _yaw = YawOf(dx, dz);
            intent.Yaw = _yaw;
            _movedLastTick = true;
            _lastX = x;
            _lastZ = z;
            return false;
        }
    }
}
