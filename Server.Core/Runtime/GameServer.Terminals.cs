using System;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    // GDD v2 §2: terminal hacking (a passive part with co-hack rates plus packet-routing bursts, 11/17 R2) and the killer
    // kick (regression).
    public sealed partial class GameServer
    {
        private TerminalState[] _terminals = Array.Empty<TerminalState>();
        private byte _terminalsRequired;
        // Server-side progress in percentage points (0..100); the wire carries the byte TerminalState.Progress.
        private double[] _progress = Array.Empty<double>();
        private bool[] _regressing = Array.Empty<bool>();
        private double[] _overloadedUntil = Array.Empty<double>();
        private int[] _hackers = Array.Empty<int>();
        // Float progress is summed per tick; this absorbs the rounding of float dt times the co-hack rate.
        private const double ProgressEpsilon = 0.0001;
        private void ResetTerminals()
        {
            _progress = new double[_terminals.Length];
            _regressing = new bool[_terminals.Length];
            _overloadedUntil = new double[_terminals.Length];
            for (int i = 0; i < _overloadedUntil.Length; i++) _overloadedUntil[i] = double.NegativeInfinity;
            _hackers = new int[_terminals.Length];
        }

        private bool TerminalDone(int i) => _terminals[i].Progress >= 100;

        private void Hack(float dt)
        {
            Array.Clear(_hackers, 0, _hackers.Length);
            foreach (var player in _players)
            {
                player.Hacking = false;
                player.HackTerminal = -1;
                player.SwitchExit = -1;
                if (player.Role != Role.Survivor || player.Status != PlayerStatus.Normal || !player.Interacting ||
                    player.InChaseAction) continue;
                // An exit switch in reach takes the hold before any terminal.
                player.SwitchExit = SwitchInReach(player);
                if (player.SwitchExit >= 0) continue;
                int nearest = -1;
                float best = float.MaxValue;
                for (int i = 0; i < _terminals.Length; i++)
                {
                    if (TerminalDone(i) || !Within(player, _terminals[i].X, _terminals[i].Z, GameConstants.TerminalRadius)) continue;
                    float distance = DistanceSquared(player.X, player.Z, _terminals[i].X, _terminals[i].Z);
                    if (distance < best) { best = distance; nearest = i; }
                }
                if (nearest < 0) continue;
                player.Hacking = true;
                player.HackTerminal = nearest;
                _hackers[nearest]++;
            }
            for (int i = 0; i < _terminals.Length; i++)
            {
                if (TerminalDone(i)) continue;
                if (_hackers[i] > 0)
                {
                    // Any survivor hacking the terminal stops a kick regression.
                    _regressing[i] = false;
                    _progress[i] += dt * GameConstants.CoHackRate(_hackers[i]) * 100.0 / GameConstants.TerminalPassiveSeconds;
                }
                else if (_regressing[i])
                {
                    _progress[i] -= dt * GameConstants.RegressionPointsPerSecond;
                    if (_progress[i] <= TimeEpsilon) { _progress[i] = 0; _regressing[i] = false; }
                }
                SyncTerminal(i);
            }
            UpdateRoutes(dt);
            int completed = 0;
            for (int i = 0; i < _terminals.Length; i++) if (TerminalDone(i)) ++completed;
            if (!_powered && completed >= _terminalsRequired) PowerExits("terminals");
        }

        // Copies the float progress to the wire byte; reaching 100 completes the terminal for good.
        private void SyncTerminal(int i)
        {
            if (TerminalDone(i)) return;
            if (_progress[i] < 0) _progress[i] = 0;
            if (_progress[i] >= 100 - ProgressEpsilon)
            {
                _progress[i] = 100;
                _regressing[i] = false;
                _terminals[i].Progress = 100;
                WriteLog($"terminal {i} complete");
                return;
            }
            _terminals[i].Progress = (byte)Math.Min(99, Math.Floor(_progress[i] + ProgressEpsilon));
        }

        private byte TerminalFlagsFor(int i, bool killer)
        {
            var flags = TerminalFlags.None;
            // Regression stops when a survivor starts hacking, so the killer would learn of a hidden hacker: survivors only.
            if (!killer && _regressing[i]) flags |= TerminalFlags.Regressing;
            if (Active(_overloadedUntil[i])) flags |= TerminalFlags.Overloaded;
            return (byte)flags;
        }

        // GDD v2 §2 kick and §3 hatch close: the killer holds Interact without moving. Moving or releasing cancels.
        private void KillerHolds(float dt)
        {
            var killer = _killer;
            if (killer == null) return;
            // GDD v2 section 8: a stun, a vault or a shutter break takes Interact before the hatch and the kick.
            if (!killer.Interacting || killer.InputHeld(_now) || ChaseFrozen(killer))
            {
                killer.KickHeld = killer.HatchHeld = 0;
                killer.KickTerminal = -1;
                return;
            }
            if (_hatchOpen && Within(killer, _hatchX, _hatchZ, GameConstants.HatchRadius))
            {
                killer.KickHeld = 0;
                killer.KickTerminal = -1;
                killer.HatchHeld += dt;
                if (killer.HatchHeld >= GameConstants.HatchCloseSeconds - TimeEpsilon)
                {
                    killer.HatchHeld = 0;
                    CloseHatch();
                }
                return;
            }
            killer.HatchHeld = 0;
            int target = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _terminals.Length; i++)
            {
                if (TerminalDone(i) || _regressing[i] || _progress[i] <= TimeEpsilon ||
                    !Within(killer, _terminals[i].X, _terminals[i].Z, GameConstants.TerminalRadius)) continue;
                float distance = DistanceSquared(killer.X, killer.Z, _terminals[i].X, _terminals[i].Z);
                if (distance < best) { best = distance; target = i; }
            }
            if (target != killer.KickTerminal) killer.KickHeld = 0;
            killer.KickTerminal = target;
            if (target < 0) return;
            killer.KickHeld += dt;
            if (killer.KickHeld < GameConstants.KickHoldSeconds - TimeEpsilon) return;
            killer.KickHeld = 0;
            killer.KickTerminal = -1;
            _progress[target] = Math.Max(0, _progress[target] - GameConstants.KickPenaltyPoints);
            _regressing[target] = _progress[target] > TimeEpsilon;
            SyncTerminal(target);
            WriteLog($"kick terminal {target}");
        }

        private void ResetRoutes()
        {
            foreach (var player in _players)
            {
                ClearRoute(player);
                player.RouteGapLeft = 0;
                player.RouteSerial = 0;
                player.RouteInputTimes.Clear();
                player.RouteRandom = player.RouteBotRandom = null;
                if (player.Role != Role.Survivor) continue;
                // Private streams per survivor, drawn from the match's route stream (never the public seed). Every survivor
                // draws both, so a human taking a bot slot (or the reverse) does not shift anyone else's routes.
                player.RouteRandom = PrivateRandom.FromSeed(_routeRandom.Next(), player.Id);
                player.RouteBotRandom = PrivateRandom.FromSeed(_routeRandom.Next(), player.Id + Protocol.MaxPlayers);
            }
        }

        private static void ClearRoute(Player player)
        {
            player.RouteId = player.RouteIndex = player.RouteDirs = 0;
            player.RouteTerminal = -1;
        }

        // GDD v2 §2 packet routing: a hacker without a route gets one at once (the first one, or after an error) or after
        // the gap (RouteGapSeconds of hacking, at least RouteMinCycleSeconds after the last route was dealt). A route belongs
        // to one terminal. It is kept (not re-rolled) while its owner stops hacking, but it is only sent in SelfState and only
        // answerable while the owner hacks that terminal (attempt 2, F1); hacking another terminal deals a new one. Leaving
        // Normal or a done terminal drops it.
        private void UpdateRoutes(float dt)
        {
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor || player.RouteRandom == null) continue;
                if (player.Status != PlayerStatus.Normal ||
                    (player.RouteTerminal >= 0 && TerminalDone(player.RouteTerminal))) ClearRoute(player);
                if (!player.Hacking) continue;
                if (player.RouteId != 0)
                {
                    if (player.RouteTerminal != player.HackTerminal) IssueRoute(player, player.HackTerminal);
                    continue;
                }
                player.RouteGapLeft -= dt;
                if (player.RouteGapLeft <= TimeEpsilon) IssueRoute(player, player.HackTerminal);
            }
        }

        private void IssueRoute(Player player, int terminal)
        {
            player.RouteSerial = (byte)(player.RouteSerial >= byte.MaxValue ? 1 : player.RouteSerial + 1);
            player.RouteId = player.RouteSerial;
            player.RouteTerminal = terminal;
            player.RouteIndex = 0;
            player.RouteGapLeft = 0;
            player.RouteIssuedAt = _now;
            byte dirs = 0;
            for (int i = 0; i < GameConstants.RouteLength; i++)
                dirs = HackRoute.WithDirection(dirs, i, (RouteDirection)player.RouteRandom.Next(4));
            player.RouteDirs = dirs;
        }

        // C2S_HackInput (and the survivor bots' route solver): unknown or stale route ids, inputs while not hacking the
        // route's terminal and inputs beyond RouteInputsPerSecond are dropped without a reply. A right direction advances;
        // the full route adds RouteBurstPoints. A wrong one costs RouteErrorPenaltyPoints, flags the terminal Overloaded (the
        // only trace the killer gets) and deals a new route at once.
        private void HackInput(Player player, byte routeId, byte direction)
        {
            if (player.Role != Role.Survivor || routeId == 0 || routeId != player.RouteId || direction > (byte)RouteDirection.Right) return;
            int terminal = player.RouteTerminal;
            if (!player.Hacking || player.HackTerminal != terminal || terminal < 0 || TerminalDone(terminal)) return;
            var times = player.RouteInputTimes;
            while (times.Count > 0 && _now - times.Peek() >= 1 - TimeEpsilon) times.Dequeue();
            if (times.Count >= GameConstants.RouteInputsPerSecond) return;
            times.Enqueue(_now);
            if ((RouteDirection)direction == HackRoute.DirectionAt(player.RouteDirs, player.RouteIndex))
            {
                if (++player.RouteIndex < GameConstants.RouteLength) return;
                // Co-hack falloff on bursts too (attempt 2, F4): each of n hackers adds RouteBurstShare(n) of a burst.
                _progress[terminal] += GameConstants.RouteBurstPoints * GameConstants.RouteBurstShare(_hackers[terminal]);
                double age = _now - player.RouteIssuedAt;
                ClearRoute(player);
                player.RouteGapLeft = Math.Max(GameConstants.RouteGapSeconds, GameConstants.RouteMinCycleSeconds - age);
                LogRoute(player, terminal, "complete");
            }
            else
            {
                _progress[terminal] = Math.Max(0, _progress[terminal] - GameConstants.RouteErrorPenaltyPoints);
                _overloadedUntil[terminal] = _now + GameConstants.OverloadedSeconds;
                IssueRoute(player, terminal);
                LogRoute(player, terminal, "error");
            }
            SyncTerminal(terminal);
        }

        // One line per completed route and per error, at most RouteLogsPerSecond lines per player; skipped lines are counted
        // into the next one.
        private const double RouteLogsPerSecond = 2;
        private void LogRoute(Player player, int terminal, string outcome)
        {
            if (_now - player.RouteLoggedAt < 1 / RouteLogsPerSecond - TimeEpsilon)
            {
                if (player.RouteLogsUnlogged < int.MaxValue) ++player.RouteLogsUnlogged;
                return;
            }
            string skipped = player.RouteLogsUnlogged > 0 ? $" unlogged={player.RouteLogsUnlogged}" : string.Empty;
            player.RouteLoggedAt = _now;
            player.RouteLogsUnlogged = 0;
            WriteLog($"route player={player.Id} terminal={terminal} {outcome} progress={_progress[terminal]:0.0}{skipped}");
        }

        private void SendTo(Player player, IPacket packet)
        {
            if (player.ConnectionId != null && _connections.TryGetValue(player.ConnectionId, out var binding))
                Send(binding.Connection, packet);
        }

        // Server tests only: sets the float progress of one terminal between ticks.
        internal void SetTerminalProgressForTests(int index, double progress)
        {
            lock (_tickGate)
            {
                _progress[index] = progress;
                if (_terminals[index].Progress >= 100 && progress < 100) _terminals[index].Progress = 0;
                SyncTerminal(index);
            }
        }

        internal double TerminalProgressForTests(int index) { lock (_tickGate) return _progress[index]; }

        // Server tests only: the own route of one player as (RouteId, packed directions, next index, terminal).
        internal (byte Id, byte Dirs, byte Index, int Terminal) RouteForTests(int playerId)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) return (player.RouteId, player.RouteDirs, player.RouteIndex, player.RouteTerminal);
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }

        // Server tests only: one route key press through the C2S_HackInput path, between ticks.
        internal void HackInputForTests(int playerId, byte routeId, byte direction)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) HackInput(player, routeId, direction);
        }
    }
}
