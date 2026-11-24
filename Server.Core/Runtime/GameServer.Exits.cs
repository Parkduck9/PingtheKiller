using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // GDD v2 §3: two exits with switches, escape, the collapse countdown and the hatch.
    public sealed partial class GameServer
    {
        private bool _powered;
        private double[] _switchSeconds = Array.Empty<double>();
        private bool[] _exitOpen = Array.Empty<bool>();
        private bool[] _switchHeld = Array.Empty<bool>();
        private double[] _exitLockedUntil = Array.Empty<double>();
        private bool _collapseRunning;
        private double _collapseLeft;
        private bool _hatchSpawned, _hatchOpen;
        private float _hatchX, _hatchZ;

        private void ResetExits()
        {
            int count = Map.Exits.Count;
            _powered = false;
            _switchSeconds = new double[count];
            _exitOpen = new bool[count];
            _switchHeld = new bool[count];
            _exitLockedUntil = new double[count];
            for (int i = 0; i < count; i++) _exitLockedUntil[i] = double.NegativeInfinity;
            _collapseRunning = false;
            _collapseLeft = 0;
            _hatchSpawned = _hatchOpen = false;
            foreach (var player in _players) player.CollapseLeft = GameConstants.CollapseSeconds;
        }

        private bool ExitLocked(int exit) => Active(_exitLockedUntil[exit]);

        private bool AnyExitOpen
        {
            get
            {
                foreach (bool open in _exitOpen) if (open) return true;
                return false;
            }
        }

        private void PowerExits(string reason)
        {
            if (_powered) return;
            _powered = true;
            WriteLog($"exits powered reason={reason}");
        }

        // The nearest switch of a powered, unopened exit within ExitSwitchRadius, or -1.
        private int SwitchInReach(Player player)
        {
            if (!_powered) return -1;
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _exitOpen.Length; i++)
            {
                var point = Map.ExitSwitches[i];
                if (_exitOpen[i] || !Within(player, point.X, point.Z, GameConstants.ExitSwitchRadius)) continue;
                float distance = DistanceSquared(player.X, player.Z, point.X, point.Z);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        // Several holders do not stack; released progress is kept; a locked switch does not progress.
        private void Switches(float dt)
        {
            Array.Clear(_switchHeld, 0, _switchHeld.Length);
            foreach (var player in _players)
                if (player.Role == Role.Survivor && player.Status == PlayerStatus.Normal && player.SwitchExit >= 0)
                    _switchHeld[player.SwitchExit] = true;
            for (int i = 0; i < _exitOpen.Length; i++)
            {
                if (!_switchHeld[i] || _exitOpen[i] || ExitLocked(i)) continue;
                _switchSeconds[i] += dt;
                if (_switchSeconds[i] < GameConstants.ExitSwitchSeconds - TimeEpsilon) continue;
                _switchSeconds[i] = GameConstants.ExitSwitchSeconds;
                _exitOpen[i] = true;
                WriteLog($"exit {i} open");
                StartCollapse("exit");
            }
        }

        private void Escape()
        {
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor || player.Status != PlayerStatus.Normal) continue;
                bool escaped = false;
                for (int i = 0; i < _exitOpen.Length && !escaped; i++)
                    escaped = _exitOpen[i] && Within(player, Map.Exits[i].X, Map.Exits[i].Z, GameConstants.ExitRadius);
                if (escaped) { SetStatus(player, PlayerStatus.Escaped, "exit"); continue; }
                if (_hatchOpen && player.Interacting && Within(player, _hatchX, _hatchZ, GameConstants.HatchRadius))
                    SetStatus(player, PlayerStatus.Escaped, "hatch");
            }
        }

        private void StartCollapse(string reason)
        {
            if (_collapseRunning) return;
            _collapseRunning = true;
            _collapseLeft = GameConstants.CollapseSeconds;
            foreach (var player in _players) player.CollapseLeft = GameConstants.CollapseSeconds;
            WriteLog($"collapse start reason={reason}");
        }

        // The global clock runs at 1x; each survivor's own clock runs at 1x while Normal and 2x while Lagging.
        private void RunCollapse(float dt)
        {
            if (!_collapseRunning) return;
            _collapseLeft = Math.Max(0, _collapseLeft - dt);
            foreach (var player in _players)
            {
                if (!player.IsActiveSurvivor) continue;
                double rate = player.Status == PlayerStatus.Lagging ? GameConstants.CollapseLaggingRate : 1;
                player.CollapseLeft = Math.Max(0, player.CollapseLeft - dt * rate);
                if (player.CollapseLeft > TimeEpsilon) continue;
                SetStatus(player, PlayerStatus.Eliminated, "collapse");
                _collapseEliminated = true;
            }
        }

        // GDD v2 §3: the hatch opens once, when exactly one of at least two starting survivors is still in play.
        private void UpdateHatch()
        {
            if (_hatchSpawned || _startingSurvivors < 2) return;
            int active = 0;
            foreach (var player in _players) if (player.IsActiveSurvivor) ++active;
            if (active != 1) return;
            var point = Map.HatchPoints[_random.Next(Map.HatchPoints.Count)];
            _hatchSpawned = _hatchOpen = true;
            _hatchX = point.X;
            _hatchZ = point.Z;
            WriteLog($"hatch open x={_hatchX} z={_hatchZ}");
        }

        // The killer closed the hatch: both exits get power and the collapse starts unless it already runs.
        private void CloseHatch()
        {
            _hatchOpen = false;
            WriteLog("hatch closed");
            PowerExits("hatch");
            StartCollapse("hatch");
        }

        // Only within HatchSightDistance and with a wall-free line, so neither side learns the hatch for free.
        private bool HatchVisibleTo(Player player) =>
            _hatchOpen && DistanceSquared(player.X, player.Z, _hatchX, _hatchZ) <=
                GameConstants.HatchSightDistance * GameConstants.HatchSightDistance + 0.000001f &&
            !Map.SegmentBlocked(player.X, player.Z, _hatchX, _hatchZ);

        // Firewall Block target: the unopened exit with the highest switch progress above 0, or -1.
        private int FirewallTarget()
        {
            int target = -1;
            double best = TimeEpsilon;
            for (int i = 0; i < _exitOpen.Length; i++)
                if (!_exitOpen[i] && _switchSeconds[i] > best) { best = _switchSeconds[i]; target = i; }
            return target;
        }

        private byte SwitchPercent(int exit) => _exitOpen[exit] ? (byte)100
            : (byte)Math.Min(99, Math.Floor(_switchSeconds[exit] * 100 / GameConstants.ExitSwitchSeconds + TimeEpsilon));

        private void AddExitStates(List<ExitState> exits)
        {
            for (int i = 0; i < _exitOpen.Length; i++)
            {
                var flags = ExitFlags.None;
                if (_powered) flags |= ExitFlags.Powered;
                if (_exitOpen[i]) flags |= ExitFlags.Open;
                else if (ExitLocked(i)) flags |= ExitFlags.Locked;
                exits.Add(new ExitState
                {
                    Id = (byte)i, X = Map.Exits[i].X, Z = Map.Exits[i].Z, Flags = (byte)flags, SwitchProgress = SwitchPercent(i)
                });
            }
        }

        // Server tests only: forces a status between ticks (hatch and collapse scenarios).
        internal void SetStatusForTests(int playerId, PlayerStatus status)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) SetStatus(player, status, "test");
        }

        internal (bool Open, float X, float Z) HatchForTests() { lock (_tickGate) return (_hatchOpen, _hatchX, _hatchZ); }
    }
}
