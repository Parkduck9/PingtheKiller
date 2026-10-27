using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        private const double TimeEpsilon = 0.000001;
        // GDD §3.3: nobody can be executed during the first 2 s after a capture, even by a fresh touch.
        public const float ExecutionGraceSeconds = 2f;
        private Player _killer;
        private bool _exitOpen;
        private double _exitLockedUntil = double.NegativeInfinity;
        private double[] _hackSeconds = Array.Empty<double>();
        private int[] _hackers = Array.Empty<int>();
        private S2C_GameResult _result;
        private readonly List<int> _escaped = new List<int>();

        // A timed state set at instant t with duration d is active for the ticks in [t, t + d).
        private bool Active(double until) => _now < until - TimeEpsilon;
        private bool ExitBlocked => !_exitOpen || Active(_exitLockedUntil);

        private void ResetMatchRules(Player killer)
        {
            _killer = killer;
            _exitOpen = false;
            _exitLockedUntil = double.NegativeInfinity;
            _hackSeconds = new double[_terminals.Length];
            _hackers = new int[_terminals.Length];
            _result = null;
            _escaped.Clear();
            _skillRequests.Clear();
            _decoys.Clear();
            _killerView.Clear();
            _killerViewSources.Clear();
        }

        // Order: skills (inputs) -> statuses/timers -> capture -> rescue -> hacking -> exit/escape -> win check.
        private void RunRules(float dt)
        {
            if (_sandbox)
            {
                // Sandbox keeps U2a rules: only the time limit can end the match.
                if (_matchTime >= GameConstants.MatchLengthSeconds - TimeEpsilon)
                    _result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };
                return;
            }
            MovePhantoms(dt);
            ExecuteSkills();
            UpdateStatuses();
            Capture();
            Rescue(dt);
            Hack(dt);
            Escape();
            CheckResult();
        }

        private void UpdateStatuses()
        {
            foreach (var player in _players)
                if (player.Role == Role.Survivor && player.Status == PlayerStatus.Lagging && !Active(player.LaggingUntil))
                    SetStatus(player, PlayerStatus.Eliminated, "timeout");
        }

        private void SetStatus(Player player, PlayerStatus status, string reason)
        {
            player.Status = status;
            player.Interacting = player.Hacking = player.Rescuing = false;
            player.RescueProgress = 0;
            if (status == PlayerStatus.Lagging)
            {
                player.CaptureCount = 1;
                player.LaggingUntil = _now + GameConstants.LaggingTimeoutSeconds;
                // Execution needs a fresh touch after the grace: the capturing contact itself never executes.
                player.ExecutionArmed = false;
                player.ExecutionGraceUntil = _now + ExecutionGraceSeconds;
            }
            else if (status == PlayerStatus.Normal)
            {
                player.LaggingUntil = 0;
                player.InvulnerableUntil = _now + GameConstants.PostRescueInvulnerabilitySeconds;
            }
            else
            {
                player.DirX = player.DirZ = 0;
                player.LastSignal = double.NegativeInfinity;
            }
            WriteLog($"status player={player.Id} {status} reason={reason}");
        }

        private void Capture()
        {
            if (_killer == null) return;
            foreach (var survivor in _players)
            {
                if (survivor.Role != Role.Survivor) continue;
                bool contact = Within(survivor, _killer.X, _killer.Z, GameConstants.CaptureRadius);
                if (survivor.Status == PlayerStatus.Lagging)
                {
                    if (!contact) survivor.ExecutionArmed = true;
                    // A touch during the grace is spent: execution needs a re-entry after the grace ends.
                    else if (Active(survivor.ExecutionGraceUntil)) survivor.ExecutionArmed = false;
                    else if (survivor.ExecutionArmed) SetStatus(survivor, PlayerStatus.Eliminated, "execution");
                    continue;
                }
                if (survivor.Status != PlayerStatus.Normal || !contact || Active(survivor.InvulnerableUntil)) continue;
                if (survivor.CaptureCount == 0) SetStatus(survivor, PlayerStatus.Lagging, "capture");
                else SetStatus(survivor, PlayerStatus.Eliminated, "second capture");
            }
        }

        private void Rescue(float dt)
        {
            foreach (var player in _players) player.Rescuing = false;
            foreach (var target in _players)
            {
                if (target.Role != Role.Survivor || target.Status != PlayerStatus.Lagging) continue;
                bool contact = false;
                foreach (var rescuer in _players)
                {
                    if (rescuer == target || rescuer.Role != Role.Survivor || rescuer.Status != PlayerStatus.Normal) continue;
                    if (!Within(rescuer, target.X, target.Z, GameConstants.RescueRadius)) continue;
                    rescuer.Rescuing = true;
                    contact = true;
                }
                // Contact must be continuous: one tick without a rescuer in range restarts the count.
                if (!contact) { target.RescueProgress = 0; continue; }
                target.RescueProgress += dt;
                if (target.RescueProgress >= GameConstants.RescueTimeSeconds - TimeEpsilon)
                    SetStatus(target, PlayerStatus.Normal, "rescue");
            }
        }

        private void Hack(float dt)
        {
            Array.Clear(_hackers, 0, _hackers.Length);
            foreach (var player in _players)
            {
                player.Hacking = false;
                if (player.Role != Role.Survivor || player.Status != PlayerStatus.Normal || !player.Interacting) continue;
                int nearest = -1;
                float best = float.MaxValue;
                for (int i = 0; i < _terminals.Length; i++)
                {
                    if (_terminals[i].Progress >= 100 || !Within(player, _terminals[i].X, _terminals[i].Z, GameConstants.TerminalRadius)) continue;
                    float distance = DistanceSquared(player.X, player.Z, _terminals[i].X, _terminals[i].Z);
                    if (distance < best) { best = distance; nearest = i; }
                }
                if (nearest < 0) continue;
                player.Hacking = true;
                _hackers[nearest]++;
            }
            int completed = 0;
            for (int i = 0; i < _terminals.Length; i++)
            {
                if (_hackers[i] > 0)
                {
                    _hackSeconds[i] += dt * Math.Min(_hackers[i], GameConstants.MaxCoHackMultiplier);
                    if (_hackSeconds[i] >= GameConstants.TerminalHackTimeSeconds - TimeEpsilon)
                    {
                        _hackSeconds[i] = GameConstants.TerminalHackTimeSeconds;
                        _terminals[i].Progress = 100;
                        WriteLog($"terminal {i} complete");
                    }
                    else
                    {
                        double percent = Math.Floor(_hackSeconds[i] * 100 / GameConstants.TerminalHackTimeSeconds + TimeEpsilon);
                        _terminals[i].Progress = (byte)Math.Min(99, percent);
                    }
                }
                if (_terminals[i].Progress >= 100) ++completed;
            }
            if (!_exitOpen && completed >= _terminalsRequired)
            {
                _exitOpen = true;
                WriteLog("exit open");
            }
        }

        private void Escape()
        {
            _escaped.Clear();
            if (ExitBlocked) return;
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor || player.Status != PlayerStatus.Normal) continue;
                if (!Within(player, Map.Exit.X, Map.Exit.Z, GameConstants.ExitRadius)) continue;
                SetStatus(player, PlayerStatus.Escaped, "exit");
                _escaped.Add(player.Id);
            }
        }

        private void CheckResult()
        {
            if (_escaped.Count > 0)
            {
                _result = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, Reason = (byte)GameEndReason.SurvivorEscaped };
                _result.EscapedIds.AddRange(_escaped);
                return;
            }
            bool anyNormal = false;
            foreach (var player in _players)
                if (player.Role == Role.Survivor && player.Status == PlayerStatus.Normal) anyNormal = true;
            if (!anyNormal)
                _result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            else if (_matchTime >= GameConstants.MatchLengthSeconds - TimeEpsilon)
                _result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };
        }

        // Runs after the final WorldState of the match: one GameResult per connected human, then back to the lobby.
        private void FinishMatch()
        {
            var result = _result;
            _result = null;
            _matchStarted = false;
            foreach (var player in _players)
                if (!player.IsBot && player.ConnectionId != null && _connections.TryGetValue(player.ConnectionId, out var binding))
                    Send(binding.Connection, result);
            foreach (var player in _players)
            {
                player.Interacting = player.Hacking = player.Rescuing = false;
                player.DirX = player.DirZ = 0;
                player.LastSignal = double.NegativeInfinity;
            }
            _killer = null;
            _skillRequests.Clear();
            _decoys.Clear();
            _killerView.Clear();
            _killerViewSources.Clear();
            WriteLog($"match end winner={(Role)result.WinnerRole} reason={(GameEndReason)result.Reason} escaped={result.EscapedIds.Count}");
            BroadcastLobby();
        }

        private void SetInteract(Player player, byte active)
        {
            if (active > 1 || _sandbox) return;
            if (active == 0) { player.Interacting = false; return; }
            if (player.Role != Role.Survivor || player.Status != PlayerStatus.Normal) return;
            player.Interacting = true;
        }
    }
}
