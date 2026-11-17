using System;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    // GDD v2 §2: terminal hacking with co-hack rates, the killer kick (regression) and sync checks.
    public sealed partial class GameServer
    {
        // GDD v2 §2 / brief U6a R3: the chance that a survivor bot hits a sync check (U6d tunes it per bot).
        internal const float DefaultBotSyncSuccessRate = 0.85f;
        // Share of the successful bot presses that land in the great part.
        internal const double BotSyncGreatShare = 0.2;

        private TerminalState[] _terminals = Array.Empty<TerminalState>();
        private byte _terminalsRequired;
        // Server-side progress in percentage points (0..100); the wire carries the byte TerminalState.Progress.
        private double[] _progress = Array.Empty<double>();
        private bool[] _regressing = Array.Empty<bool>();
        private double[] _overloadedUntil = Array.Empty<double>();
        private int[] _hackers = Array.Empty<int>();
        // R1 (11/17): Firewall block on a terminal (no hacking until then).
        private double[] _terminalLockedUntil = Array.Empty<double>();
        private uint _nextCheckId;
        // Float progress is summed per tick; this absorbs the rounding of float dt times the co-hack rate.
        private const double ProgressEpsilon = 0.0001;
        // Test hook (Server.Tests only): false stops new sync checks so long hacks are not interrupted.
        internal bool SyncChecksEnabled { get; set; } = true;

        private void ResetTerminals()
        {
            _progress = new double[_terminals.Length];
            _regressing = new bool[_terminals.Length];
            _overloadedUntil = new double[_terminals.Length];
            for (int i = 0; i < _overloadedUntil.Length; i++) _overloadedUntil[i] = double.NegativeInfinity;
            _hackers = new int[_terminals.Length];
            _terminalLockedUntil = new double[_terminals.Length];
            for (int i = 0; i < _terminalLockedUntil.Length; i++) _terminalLockedUntil[i] = double.NegativeInfinity;
        }

        private bool TerminalLocked(int i) => Active(_terminalLockedUntil[i]);

        // R1 (11/17) a2: a terminal cannot be locked again until FirewallTerminalRelockSeconds after its lock ended (another
        // terminal can), so an 8 s lock on a 12 s cooldown cannot hold the last terminal until the clock runs out.
        public const double FirewallTerminalRelockSeconds = 20;

        private bool TerminalRelockable(int i) => _now >= _terminalLockedUntil[i] + FirewallTerminalRelockSeconds - TimeEpsilon;

        // R1 (11/17) Firewall block without an Exit switch in progress: the unfinished, relockable terminal with the most
        // progress (above 0; ties: lower index), or -1.
        private int FirewallTerminalTarget()
        {
            int target = -1;
            for (int i = 0; i < _terminals.Length; i++)
                if (!TerminalDone(i) && TerminalRelockable(i) && _progress[i] > TimeEpsilon &&
                    (target < 0 || _progress[i] > _progress[target] + TimeEpsilon))
                    target = i;
            return target;
        }

        // Hackers on it stop at once (the hack pass skips a locked terminal; a pending sync check is cancelled there).
        private void LockTerminal(int i)
        {
            _terminalLockedUntil[i] = _now + SkillIds.FirewallTerminalSeconds;
            foreach (var player in _players)
                if (player.HackTerminal == i)
                {
                    player.Hacking = false;
                    player.HackTerminal = -1;
                    CancelSyncCheck(player);
                }
            WriteLog($"terminal {i} locked seconds={SkillIds.FirewallTerminalSeconds}");
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
                // R1 (11/17): a locked terminal takes no hack (the nearest terminal decides, so no other one is picked).
                if (nearest < 0 || TerminalLocked(nearest)) continue;
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
                    _progress[i] += dt * GameConstants.CoHackRate(_hackers[i]) * 100.0 / GameConstants.TerminalHackTimeSeconds;
                }
                else if (_regressing[i])
                {
                    _progress[i] -= dt * GameConstants.RegressionPointsPerSecond;
                    if (_progress[i] <= TimeEpsilon) { _progress[i] = 0; _regressing[i] = false; }
                }
                SyncTerminal(i);
            }
            UpdateSyncChecks(dt);
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
            // R1 (11/17): both roles see a Firewall lock (the Killer on its terminal marker).
            if (TerminalLocked(i)) flags |= TerminalFlags.Locked;
            return (byte)flags;
        }

        // GDD v2 §2 kick and §3 hatch close: the killer holds Interact without moving. Moving or releasing cancels.
        private void KillerHolds(float dt)
        {
            var killer = _killer;
            if (killer == null) return;
            // GDD v2 section 8: a stun, a vault or a shutter break takes Interact before the hatch and the kick.
            if (!killer.Interacting || killer.InputHeld(_now) || ChaseFrozen(killer) || Swinging(killer))
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

        private void ResetSyncChecks()
        {
            foreach (var player in _players)
            {
                player.SyncPending = false;
                player.SyncBotPressAt = double.NaN;
                if (player.Role != Role.Survivor) continue;
                // One private stream per survivor, drawn from the match's private sync stream (never the public seed).
                player.SyncRandom = PrivateRandom.FromSeed(_syncRandom.Next(), player.Id);
                player.SyncTimer = NextSyncInterval(player);
            }
        }

        private static double NextSyncInterval(Player player) =>
            GameConstants.SyncCheckIntervalMinSeconds +
            player.SyncRandom.NextDouble() * (GameConstants.SyncCheckIntervalMaxSeconds - GameConstants.SyncCheckIntervalMinSeconds);

        // Bots press inside the server (no latency). Online humans use the server-timed probe estimate; offline
        // sessions have no transport delay and stay at 0. Server tests can override it through SetRttForTests.
        // The full round trip applies: the needle starts when the check reaches the client (half a trip after it left)
        // and the press needs the other half to come back.
        private static double RttOf(Player player) => player.IsBot ? 0 : player.RttSeconds;

        // A press is never dated earlier than this before the start of the tick that handles it.
        internal const double MaxPressAgeSeconds = 0.25;

        // Simulated time of the current clock reading inside this tick (when a packet sent now leaves the server).
        private double SimulatedNow()
        {
            double offset = _clock() - (_tickWallStart - _tickLag);
            return _tickSimStart + Math.Max(0, Math.Min(_tickLag + _tickDt, offset));
        }

        // Simulated time of a press that the receive thread queued at the clock reading receivedAt (called while the
        // tick drains its inputs, so _now is still the tick's start).
        private double SimulatedArrival(double receivedAt)
        {
            double age = (_tickWallStart - _tickLag) - receivedAt;
            if (double.IsNaN(age)) age = 0;
            return _now - Math.Max(-_tickLag, Math.Min(MaxPressAgeSeconds, age));
        }

        // GDD v2 §2 sync checks: the interval runs only while the survivor hacks; stopping cancels a pending check.
        private void UpdateSyncChecks(float dt)
        {
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor || player.SyncRandom == null) continue;
                if (player.SyncPending)
                {
                    if (!player.Hacking || player.HackTerminal != player.SyncTerminal) { CancelSyncCheck(player); continue; }
                    // A bot press is handled by the first tick that starts at or after it, like a human press arriving then;
                    // it is judged at its own moment.
                    if (!double.IsNaN(player.SyncBotPressAt) && _tickSimStart + _tickLag >= player.SyncBotPressAt - TimeEpsilon)
                    {
                        Judge(player, player.SyncBotPressAt);
                        continue;
                    }
                    // A press made within Duration + tolerance arrives at most one rtt after that limit. Every press that
                    // arrived before this tick started has been handled already, so fail only once that start is past it.
                    double limit = player.SyncSentAt + GameConstants.SyncCheckDurationSeconds + GameConstants.SyncToleranceSeconds +
                        RttOf(player);
                    if (_tickSimStart + _tickLag >= limit - TimeEpsilon) Resolve(player, SyncResult.Fail);
                    continue;
                }
                if (!player.Hacking || !SyncChecksEnabled) continue;
                player.SyncTimer -= dt;
                if (player.SyncTimer <= TimeEpsilon) IssueSyncCheck(player);
            }
        }

        private void IssueSyncCheck(Player player)
        {
            var random = player.SyncRandom;
            player.SyncPending = true;
            player.SyncCheckId = ++_nextCheckId;
            player.SyncTerminal = player.HackTerminal;
            // The moment the check leaves (this tick's start plus the time spent in it), not the end of the tick.
            player.SyncSentAt = SimulatedNow();
            player.SyncZoneStart = (float)(GameConstants.SyncZoneStartMin +
                random.NextDouble() * (GameConstants.SyncZoneStartMax - GameConstants.SyncZoneStartMin));
            player.SyncBotPressAt = double.NaN;
            if (player.IsBot && random.NextDouble() < player.BotSyncSuccessRate)
            {
                // A bot hit lands in the middle of the great part or of the rest of the zone; a miss never presses.
                double fraction = random.NextDouble() < BotSyncGreatShare
                    ? player.SyncZoneStart + GameConstants.SyncGreatSize / 2.0
                    : player.SyncZoneStart + (GameConstants.SyncGreatSize + GameConstants.SyncZoneSize) / 2.0;
                player.SyncBotPressAt = player.SyncSentAt + fraction * GameConstants.SyncCheckDurationSeconds;
            }
            SendTo(player, new S2C_SyncCheck
            {
                CheckId = player.SyncCheckId, TerminalId = _terminals[player.SyncTerminal].Id,
                Duration = GameConstants.SyncCheckDurationSeconds, ZoneStart = player.SyncZoneStart,
                ZoneSize = GameConstants.SyncZoneSize, GreatSize = GameConstants.SyncGreatSize
            });
        }

        // C2S_SyncPress: unknown, expired, repeated or non-survivor presses are ignored without a reply.
        // receivedAt: server clock when the press was queued, so the wait for the next tick does not count.
        private void SyncPress(Player player, uint checkId, double receivedAt)
        {
            if (player.Role != Role.Survivor || !player.SyncPending || player.SyncCheckId != checkId || player.IsBot) return;
            // The needle the player saw had run (arrival - sent - rtt) seconds.
            Judge(player, SimulatedArrival(receivedAt) - RttOf(player));
        }

        // Classifies a press moment against the pending check of this survivor.
        private void Judge(Player player, double pressAt) =>
            Resolve(player, Classify((pressAt - player.SyncSentAt) / GameConstants.SyncCheckDurationSeconds, player.SyncZoneStart));

        // Fraction of the sweep at the press. The tolerance widens both zone edges; a press inside the leading tolerance
        // band counts as Good, only the great part itself counts as Great.
        internal static SyncResult Classify(double fraction, float zoneStart)
        {
            const double edge = 0.000001;
            double tolerance = GameConstants.SyncToleranceSeconds / GameConstants.SyncCheckDurationSeconds;
            if (fraction >= zoneStart - edge && fraction <= zoneStart + GameConstants.SyncGreatSize + edge) return SyncResult.Great;
            if (fraction >= zoneStart - tolerance - edge && fraction <= zoneStart + GameConstants.SyncZoneSize + tolerance + edge)
                return SyncResult.Good;
            return SyncResult.Fail;
        }

        private void Resolve(Player player, SyncResult result)
        {
            int terminal = player.SyncTerminal;
            uint id = player.SyncCheckId;
            player.SyncPending = false;
            player.SyncBotPressAt = double.NaN;
            player.SyncTimer = NextSyncInterval(player);
            if (terminal >= 0 && terminal < _terminals.Length && !TerminalDone(terminal))
            {
                if (result == SyncResult.Great) _progress[terminal] += GameConstants.SyncGreatBonusPoints;
                else if (result == SyncResult.Fail)
                {
                    _progress[terminal] = Math.Max(0, _progress[terminal] - GameConstants.SyncFailPenaltyPoints);
                    _overloadedUntil[terminal] = _now + GameConstants.OverloadedSeconds;
                }
                SyncTerminal(terminal);
            }
            WriteLog($"sync player={player.Id} check={id} result={result} rtt={RttOf(player) * 1000:0}");
            SendTo(player, new S2C_SyncResult { CheckId = id, Result = (byte)result });
        }

        // Stopping the hack before the press: no penalty and no result.
        private void CancelSyncCheck(Player player)
        {
            if (!player.SyncPending) return;
            player.SyncPending = false;
            player.SyncBotPressAt = double.NaN;
            if (player.SyncRandom != null) player.SyncTimer = NextSyncInterval(player);
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

        // Server tests only: overrides the probe's estimate of the full RTT subtracted by the sync judge.
        internal void SetRttForTests(int playerId, double seconds)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) player.RttSeconds = seconds;
        }

        // Server tests only: seconds of hacking until this survivor's next sync check.
        internal void SetSyncTimerForTests(int playerId, double seconds)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) player.SyncTimer = seconds;
        }
    }
}
