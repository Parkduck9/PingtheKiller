using System;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // Shared static-map movement for both brains; phantoms use the same PathFollower.
    internal abstract class BotBrain
    {
        internal const float StopDistance = 1.0f;
        internal const float StuckFraction = 0.3f;

        // The server tick of the last update; a gap means the slot was a human meanwhile, so the brain starts over.
        internal uint LastTick;
        internal bool Started;

        private readonly PathFollower _route = new PathFollower();
        // GDD v2 section 8: the bot's own chase-prop layer, and whether this tick's route wants a rail vault press.
        internal readonly ChaseTactics Tactics = new ChaseTactics();
        internal bool VaultWanted;
        // Extra metres a rail vault costs this brain's routes (A* vault links); negative never vaults.
        internal float VaultCostMeters { set => _route.VaultCostMeters = value; }
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
            _route.Pause();
            intent.DirX = intent.DirZ = 0;
            intent.Yaw = _yaw;
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
            if (Distance(x, z, tx, tz) <= Math.Max(stop, 0.05f))
            {
                Stop(ref intent, x, z);
                return true;
            }
            _route.Direction(nav, x, z, tx, tz, speed, time, out float dx, out float dz);
            intent.DirX = dx;
            intent.DirZ = dz;
            // The route crosses a rail here: the next waypoint is on the other side of a vault point within reach.
            if (_route.TryNextWaypoint(out float wx, out float wz))
            {
                var rails = WorldChaseProps.Rails;
                for (int i = 0; i < rails.Count; i++)
                    if (Distance(x, z, rails[i].CenterX, rails[i].CenterZ) <= GameConstants.RailVaultRadius - 0.05f &&
                        rails[i].Side(x, z) * rails[i].Side(wx, wz) < 0)
                        VaultWanted = true;
            }
            if (dx != 0 || dz != 0) _yaw = YawOf(dx, dz);
            intent.Yaw = _yaw;
            return false;
        }
    }
}