using System;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // GDD v2 §2 packet routing (11/17, R2): a survivor bot solves its own route with human-like timing. It reads only the
    // route of its own SurvivorBotView (exactly what that survivor's S2C_SelfState carries) and answers through the
    // C2S_HackInput path. Each key comes KeyMinSeconds-KeyMaxSeconds after the route (or the previous key) was seen while
    // hacking, and is wrong with ErrorRate. The randomness is the bot's private route-solving stream, so solving never
    // shifts the bots' movement stream.
    internal sealed class RouteSolver
    {
        internal const double KeyMinSeconds = 0.15, KeyMaxSeconds = 0.35, ErrorRate = 0.045;
        private byte _routeId, _index;
        private double _dueAt = double.NaN;

        // holding: the brain keeps Interact held this tick. True with the key to send when one is due.
        internal bool Update(SurvivorBotView view, bool holding, Random random, out byte routeId, out byte direction)
        {
            routeId = 0;
            direction = 0;
            if (random == null || !holding || !view.Hacking || view.RouteId == 0 || view.RouteIndex >= view.RouteLength)
            {
                _dueAt = double.NaN;
                return false;
            }
            if (double.IsNaN(_dueAt) || view.RouteId != _routeId || view.RouteIndex != _index)
            {
                _routeId = view.RouteId;
                _index = view.RouteIndex;
                _dueAt = view.Time + KeyMinSeconds + random.NextDouble() * (KeyMaxSeconds - KeyMinSeconds);
            }
            if (view.Time < _dueAt - 1e-9) return false;
            var right = HackRoute.DirectionAt(view.RouteDirs, view.RouteIndex);
            direction = random.NextDouble() < ErrorRate ? (byte)(((int)right + 1 + random.Next(3)) % 4) : (byte)right;
            routeId = view.RouteId;
            // Re-armed when the next view shows the answer (a new index or route); a dropped key retries after a new wait.
            _dueAt = double.NaN;
            return true;
        }
    }
}
