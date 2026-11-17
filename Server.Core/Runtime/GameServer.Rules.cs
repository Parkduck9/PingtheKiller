using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        private const double TimeEpsilon = 0.000001;
        private Player _killer;
        private S2C_GameResult _result;
        private int _startingSurvivors;
        // Set when the collapse eliminates a survivor this tick: the match-end reason is then Collapse (GDD v2 §1).
        private bool _collapseEliminated;
        private bool _timeUp;

        // A timed state set at instant t with duration d is active for the ticks in [t, t + d).
        private bool Active(double until) => _now < until - TimeEpsilon;

        private void ResetMatchRules(Player killer)
        {
            _killer = killer;
            _result = null;
            _collapseEliminated = _timeUp = false;
            _startingSurvivors = 0;
            foreach (var player in _players) if (player.Role == Role.Survivor) ++_startingSurvivors;
            ResetTerminals();
            ResetExits();
            ResetSyncChecks();
            ResetChaseProps();
            _skillRequests.Clear();
            _decoys.Clear();
            _killerView.Clear();
            _killerViewSources.Clear();
        }

        // Order: skills (inputs) -> statuses/timers -> collapse -> shutters and rails -> capture -> rescue -> hacking (regression, sync checks)
        // -> killer holds (kick, hatch close) -> exit switches -> escape -> hatch spawn -> time limit -> end check.
        private void RunRules(float dt)
        {
            if (_sandbox)
            {
                // Sandbox keeps U2a rules: only the time limit can end the match.
                if (_matchTime >= GameConstants.MatchLengthSeconds - TimeEpsilon)
                    _result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };
                return;
            }
            _collapseEliminated = false;
            MovePhantoms(dt);
            ExecuteSkills();
            UpdateStatuses();
            RunCollapse(dt);
            ChaseProps(dt);
            Capture();
            Rescue(dt);
            Hack(dt);
            KillerHolds(dt);
            Switches(dt);
            Escape();
            UpdateHatch();
            if (_matchTime >= GameConstants.MatchLengthSeconds - TimeEpsilon) TimeUp();
            CheckResult();
        }

        // GDD v2 §4: a Lagging timer that runs out advances the capture stage (count 1 -> 2 with 30 s) or eliminates.
        private void UpdateStatuses()
        {
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor || player.Status != PlayerStatus.Lagging || Active(player.LaggingUntil)) continue;
                if (player.CaptureCount >= 2) SetStatus(player, PlayerStatus.Eliminated, "timeout");
                else AdvanceStage(player, "timeout");
            }
        }

        // Count 1 -> 2 while staying Lagging, with the shorter second timer.
        private void AdvanceStage(Player player, string reason)
        {
            player.CaptureCount = 2;
            player.LaggingUntil = _now + GameConstants.SecondLaggingTimeoutSeconds;
            WriteLog($"stage player={player.Id} count=2 reason={reason}");
        }

        private void SetStatus(Player player, PlayerStatus status, string reason)
        {
            player.Status = status;
            if (player.InChaseAction) CancelChase(player);
            player.Interacting = player.Hacking = player.Rescuing = false;
            player.HackTerminal = player.SwitchExit = -1;
            player.RescueProgress = 0;
            if (status == PlayerStatus.Lagging)
            {
                player.LaggingUntil = _now + (player.CaptureCount >= 2
                    ? GameConstants.SecondLaggingTimeoutSeconds : GameConstants.LaggingTimeoutSeconds);
            }
            else if (status == PlayerStatus.Normal)
            {
                player.LaggingUntil = 0;
                player.InvulnerableUntil = _now + GameConstants.PostRescueInvulnerabilitySeconds;
                player.GetUpUntil = _now + GameConstants.GetUpSeconds;
                player.DirX = player.DirZ = 0;
            }
            else
            {
                player.DirX = player.DirZ = 0;
                player.LastSignal = double.NegativeInfinity;
            }
            if (status != PlayerStatus.Normal) CancelSyncCheck(player);
            WriteLog($"status player={player.Id} {status} reason={reason}");
        }

        // GDD v2 §4 capture stages: catching a Normal survivor counts one more capture (1 -> Lagging 60 s, 2 -> Lagging
        // 30 s, 3 -> Eliminated). The killer cannot touch a Lagging survivor into the next stage: only its timer advances it.
        private void Capture()
        {
            // GDD v2 section 8: a stunned killer cannot capture.
            if (_killer == null || Active(_killer.StunnedUntil)) return;
            foreach (var survivor in _players)
            {
                if (survivor.Role != Role.Survivor || survivor.Status != PlayerStatus.Normal) continue;
                if (!Within(survivor, _killer.X, _killer.Z, GameConstants.CaptureRadius) || Active(survivor.InvulnerableUntil)) continue;
                if (survivor.CaptureCount < byte.MaxValue) survivor.CaptureCount++;
                if (survivor.CaptureCount >= GameConstants.EliminatingCaptureCount)
                    SetStatus(survivor, PlayerStatus.Eliminated, "third capture");
                else SetStatus(survivor, PlayerStatus.Lagging, "capture");
            }
        }

        // R3 (11/17): a rescue needs the rescuer to HOLD Interact within RescueRadius of a Lagging ally; each rescuer works on
        // the nearest one. Releasing E, leaving the radius, a vault or a status change restarts the count.
        // E priority (one hold at a time): rescue a Lagging ally > shutter/rail (ChaseProps, a fresh press) > exit switch > terminal.
        private void Rescue(float dt)
        {
            foreach (var player in _players) player.Rescuing = false;
            foreach (var target in _players)
            {
                if (target.Role != Role.Survivor || target.Status != PlayerStatus.Lagging) continue;
                bool contact = false;
                foreach (var rescuer in _players)
                {
                    if (!CanHoldRescue(rescuer) || NearestLaggingAlly(rescuer) != target) continue;
                    rescuer.Rescuing = true;
                    contact = true;
                }
                // Contact must be continuous: one tick without a holding rescuer in range restarts the count.
                if (!contact) { target.RescueProgress = 0; continue; }
                target.RescueProgress += dt;
                if (target.RescueProgress >= GameConstants.RescueTimeSeconds - TimeEpsilon)
                    SetStatus(target, PlayerStatus.Normal, "rescue");
            }
        }

        private static bool CanHoldRescue(Player rescuer) =>
            rescuer.Role == Role.Survivor && rescuer.Status == PlayerStatus.Normal && rescuer.Interacting && !rescuer.InChaseAction;

        // The nearest Lagging ally within RescueRadius, or null.
        private Player NearestLaggingAlly(Player player)
        {
            Player target = null;
            float best = float.MaxValue;
            foreach (var ally in _players)
            {
                if (ally == player || ally.Role != Role.Survivor || ally.Status != PlayerStatus.Lagging ||
                    !Within(player, ally.X, ally.Z, GameConstants.RescueRadius)) continue;
                float distance = DistanceSquared(player.X, player.Z, ally.X, ally.Z);
                if (distance < best) { best = distance; target = ally; }
            }
            return target;
        }

        // GDD v2 §1: the match ends when no survivor is Normal or Lagging (or at the time limit, see TimeUp).
        private void CheckResult()
        {
            if (_result != null) return;
            if (!_timeUp)
                foreach (var player in _players)
                    if (player.IsActiveSurvivor) return;
            int escaped = 0;
            foreach (var player in _players)
                if (player.Role == Role.Survivor && player.Status == PlayerStatus.Escaped) ++escaped;
            GameEndReason reason = _timeUp ? GameEndReason.TimeUp
                : _collapseEliminated ? GameEndReason.Collapse
                : escaped == 0 ? GameEndReason.AllSurvivorsDown : GameEndReason.AllResolved;
            _result = new S2C_GameResult { WinnerRole = (byte)WinnerFor(escaped, _startingSurvivors), Reason = (byte)reason };
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor) continue;
                if (player.Status == PlayerStatus.Escaped) _result.EscapedIds.Add(player.Id);
                else if (player.Status == PlayerStatus.Eliminated) _result.EliminatedIds.Add(player.Id);
            }
        }

        // GDD v2 §1: survivors win when at least half of the starting survivors escaped.
        internal static Role WinnerFor(int escaped, int startingSurvivors) =>
            escaped * 2 >= startingSurvivors ? Role.Survivor : Role.Killer;

        // GDD v2 §1 safety net: at the time limit every survivor still in play is eliminated. When nobody is left in play
        // (the last one escaped in this very tick) the usual end reason applies.
        private void TimeUp()
        {
            foreach (var player in _players)
            {
                if (!player.IsActiveSurvivor) continue;
                SetStatus(player, PlayerStatus.Eliminated, "time up");
                _timeUp = true;
            }
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
                player.SyncPending = false;
            }
            _killer = null;
            _skillRequests.Clear();
            _decoys.Clear();
            _killerView.Clear();
            _killerViewSources.Clear();
            WriteLog($"match end winner={(Role)result.WinnerRole} reason={(GameEndReason)result.Reason} escaped={result.EscapedIds.Count}");
            BroadcastLobby();
        }

        // Survivors hold Interact to hack, hold an exit switch or open the hatch; the killer holds it to kick a terminal
        // or close the hatch (GDD v2 §2-§3).
        // R3: for GetUpSeconds after a rescue the Survivor cannot move, interact or use a skill; the look yaw is still read.
        private bool GettingUp(Player player) => player.Role == Role.Survivor && player.Status == PlayerStatus.Normal && _now < player.GetUpUntil - TimeEpsilon;

        private void SetInteract(Player player, byte active)
        {
            if (active > 1 || _sandbox) return;
            if (active == 0) { player.Interacting = false; return; }
            if (player.Role == Role.Survivor && (player.Status != PlayerStatus.Normal || GettingUp(player))) return;
            // GDD v2 section 8: a pull or vault starts on a fresh press (released before), never on a held Interact.
            if (!player.Interacting) player.InteractPressed = true;
            player.Interacting = true;
        }
    }
}
