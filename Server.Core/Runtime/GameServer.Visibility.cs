using System;
using System.Collections.Generic;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        // Ground truth of one killer-view entry, index-aligned with the killer-bound WorldState. Server tests only.
        internal readonly struct KillerViewSource
        {
            internal KillerViewSource(int label, bool phantom) { Label = label; Phantom = phantom; }
            // Real entries: the player Id. Phantoms: the phantom serial.
            internal readonly int Label;
            internal readonly bool Phantom;
        }

        // The killer's masked view of the latest tick; the killer bot reads only this.
        private readonly List<PlayerState> _killerView = new List<PlayerState>();
        private readonly List<KillerViewSource> _killerViewSources = new List<KillerViewSource>();
        private readonly List<PlayerState> _survivorView = new List<PlayerState>();
        internal IReadOnlyList<KillerViewSource> KillerViewSources => _killerViewSources;

        // Effective Signal: Lagging and forced exposure always show; Stealth hides moving, hacking and rescuing.
        private bool IsVisibleToKiller(Player survivor)
        {
            if (survivor.Status == PlayerStatus.Lagging) return true;
            if (survivor.Status != PlayerStatus.Normal) return false;
            if (Active(survivor.ExposedUntil) || ChaseForcesSignal(survivor)) return true;
            if (Active(survivor.StealthUntil)) return false;
            return survivor.InputHeld(_now) || survivor.Hacking || survivor.Rescuing || survivor.SwitchExit >= 0;
        }

        private void UpdateVisibility()
        {
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor) continue;
                player.Visible = IsVisibleToKiller(player);
                if (!player.Visible) continue;
                player.LastVisibleAt = _now;
                player.VisibleX = player.X;
                player.VisibleZ = player.Z;
                player.VisibleYaw = player.Yaw;
            }
            UpdatePhantomVisibility();
        }

        // Every survivor-shaped entry the killer receives is anonymous: no Id, facing, timers, capture count or detail
        // flags. Only Status (Lagging is always visible, GDD §3.3) and the position remain.
        private void AddKillerEntry(PlayerStatus status, float x, float z, KillerViewSource source)
        {
            _killerView.Add(new PlayerState
            {
                Id = 0, Role = (byte)Role.Survivor, Status = (byte)status, X = x, Z = z, Yaw = 0,
                Flags = (byte)PlayerFlags.Signal, StatusTimer = 0, CaptureCount = 0
            });
            _killerViewSources.Add(source);
        }

        private void BuildKillerView()
        {
            _killerView.Clear();
            _killerViewSources.Clear();
            int hiddenReal = 0;
            foreach (var player in _players)
            {
                if (player.Role == Role.Killer)
                {
                    // The killer's own entry keeps its Id; clients identify it by Role.
                    _killerView.Add(new PlayerState
                    {
                        Id = player.Id, Role = (byte)Role.Killer, Status = (byte)PlayerStatus.Normal,
                        X = player.X, Z = player.Z, Yaw = player.Yaw,
                        Flags = player.InputHeld(_now) ? (byte)PlayerFlags.Signal : (byte)PlayerFlags.None,
                        Chase = ChaseFlagsOf(player)
                    });
                    _killerViewSources.Add(new KillerViewSource(player.Id, false));
                    continue;
                }
                // R1 (11/17): the passive Packet afterimage became the active Packet trace, so no entry outlives its Signal.
                if (player.Visible)
                    AddKillerEntry(player.Status, player.VisibleX, player.VisibleZ, new KillerViewSource(player.Id, false));
                else
                    ++hiddenReal;
            }
            // The WorldState carries at most Protocol.MaxWorldEntries entries. Every hidden player keeps a reserved slot,
            // so a player who turns Signal never pushes out a shown phantom; phantoms already shown come first (no shown
            // track or tail is cut), then new phantoms by age.
            int phantomLimit = Protocol.MaxWorldEntries - hiddenReal;
            for (int pass = 0; pass < 2; pass++)
                foreach (var phantom in _decoys)
                {
                    if (phantom.InView != (pass == 0)) continue;
                    phantom.InView = phantom.Visible && _killerView.Count < phantomLimit;
                    if (phantom.InView)
                        AddKillerEntry(PlayerStatus.Normal, phantom.VisibleX, phantom.VisibleZ, new KillerViewSource(phantom.Serial, true));
                }
            Shuffle();
        }

        // Fisher-Yates over the private shuffle stream: the wire order says nothing about who is real.
        private void Shuffle()
        {
            for (int i = _killerView.Count - 1; i > 0; i--)
            {
                int j = _shuffleRandom.Next(i + 1);
                var entry = _killerView[i];
                _killerView[i] = _killerView[j];
                _killerView[j] = entry;
                var source = _killerViewSources[i];
                _killerViewSources[i] = _killerViewSources[j];
                _killerViewSources[j] = source;
            }
        }

        private void BuildSurvivorView()
        {
            _survivorView.Clear();
            foreach (var player in _players)
            {
                if (player.Role != Role.Survivor) continue;
                var flags = PlayerFlags.None;
                if (player.Visible) flags |= PlayerFlags.Signal;
                if (player.Status == PlayerStatus.Normal && Active(player.StealthUntil)) flags |= PlayerFlags.Stealthed;
                if (player.Hacking) flags |= PlayerFlags.Hacking;
                if (player.Rescuing) flags |= PlayerFlags.Rescuing;
                if (GettingUp(player)) flags |= PlayerFlags.GettingUp;
                _survivorView.Add(new PlayerState
                {
                    Id = player.Id, Role = (byte)Role.Survivor, Status = (byte)player.Status,
                    X = player.X, Z = player.Z, Yaw = player.Yaw, Flags = (byte)flags,
                    StatusTimer = StatusTimer(player), CaptureCount = player.CaptureCount, Chase = ChaseFlagsOf(player)
                });
            }
        }

        // GDD §2 (10/30): an active survivor sees the killer while the 2D segment between their server positions of
        // this tick touches no wall. Spectators (Escaped, Eliminated) never do. Per recipient, no linger or prediction.
        private bool KillerInSightOf(Player survivor) =>
            _killer != null && survivor.IsActiveSurvivor &&
            !Map.SegmentBlocked(survivor.X, survivor.Z, _killer.X, _killer.Z);

        // Server tests only: puts a player at an exact standable position between ticks (line-of-sight edge cases).
        internal void PlaceForTests(int playerId, float x, float z)
        {
            if (!Map.CanStand(x, z)) throw new ArgumentOutOfRangeException(nameof(x));
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId) { player.X = x; player.Z = z; return; }
            throw new ArgumentException("unknown player", nameof(playerId));
        }

        // The survivor-bound killer entry carries position, facing and status only: no input, timer or count detail.
        private PlayerState SurvivorKillerEntry() => new PlayerState
        {
            Id = _killer.Id, Role = (byte)Role.Killer, Status = (byte)_killer.Status,
            X = _killer.X, Z = _killer.Z, Yaw = _killer.Yaw,
            Flags = (byte)PlayerFlags.None, StatusTimer = 0, CaptureCount = 0, Chase = ChaseFlagsOf(_killer)
        };

        private float StatusTimer(Player player) =>
            player.Status == PlayerStatus.Lagging ? (float)Math.Max(0, player.LaggingUntil - _now) : 0;

        private byte ThreatFor(Player survivor)
        {
            if (_killer == null) return 0;
            float dx = survivor.X - _killer.X, dz = survivor.Z - _killer.Z;
            return ThreatForDistance((float)Math.Sqrt(dx * dx + dz * dz));
        }

        private S2C_SelfState SelfStateFor(Player player)
        {
            var state = new S2C_SelfState { SkillCooldown = (float)Math.Max(0, player.SkillReadyAt - _now) };
            SetActionState(player, state);
            SetRouteState(player, state);
            // Deprecated (GDD v2 §4, 2026-11-03): no execution, so ExecutionGraceLeft stays 0 on the wire.
            if (player.Role == Role.Killer) return state;
            if (player.Status == PlayerStatus.Lagging) state.RescueProgress = RescueFraction(player);
            else if (player.Rescuing)
            {
                Player target = NearestLaggingAlly(player);
                if (target != null) state.RescueProgress = RescueFraction(target);
            }
            if (player.Status == PlayerStatus.Normal) state.InvulnerableLeft = (float)Math.Max(0, player.InvulnerableUntil - _now);
            return state;
        }

        // Clients count the timers down (and a running rescue up) locally, so SelfState is sent only when a value
        // departs from that prediction (cooldown started, rescue started or stopped, immunity granted) and
        // as a 1 Hz refresh.
        private const double SelfStateRefreshSeconds = 1;
        private const double SelfStateTolerance = 0.01;

        private bool SelfStateDue(Player player, S2C_SelfState now)
        {
            var last = player.SelfSent;
            double elapsed = _now - player.SelfSentAt;
            if (last == null || elapsed >= SelfStateRefreshSeconds - TimeEpsilon || last.ActionKind != now.ActionKind) return true;
            // 11/17 (R2): every route change (a new route, a step, the end of one) goes out at once.
            if (last.RouteId != now.RouteId || last.RouteIndex != now.RouteIndex || last.RouteDirs != now.RouteDirs) return true;
            double rescue = last.RescueProgress > 0 ? Math.Min(1, last.RescueProgress + elapsed / GameConstants.RescueTimeSeconds) : 0;
            return Departs(now.SkillCooldown, Math.Max(0, last.SkillCooldown - elapsed)) ||
                Departs(now.InvulnerableLeft, Math.Max(0, last.InvulnerableLeft - elapsed)) ||
                Departs(now.RescueProgress, rescue) ||
                Departs(now.CollapseLeft, last.CollapseLeft < 0 ? -1 : Math.Max(0, last.CollapseLeft - elapsed * CollapseRate(player))) ||
                Departs(now.ActionProgress, last.ActionProgress <= 0 ? 0 : Math.Min(1, last.ActionProgress + elapsed * ActionRate(player, last.ActionKind)));
        }

        private static double CollapseRate(Player player) =>
            player.Role == Role.Survivor && player.Status == PlayerStatus.Lagging ? GameConstants.CollapseLaggingRate : 1;

        // A vault's length depends on the prop and the role, so its rate comes from the running action.
        private static double ActionRate(Player player, byte kind) =>
            kind == (byte)ActionKind.Vault && player.InChaseAction ? 1 / Math.Max(TimeEpsilon, player.ChaseEnd - player.ChaseStart)
            : ActionRate(kind);

        private static double ActionRate(byte kind) =>
            kind == (byte)ActionKind.Switch ? 1 / GameConstants.ExitSwitchSeconds
            : kind == (byte)ActionKind.Kick ? 1 / GameConstants.KickHoldSeconds
            : kind == (byte)ActionKind.HatchClose ? 1 / GameConstants.HatchCloseSeconds
            : kind == (byte)ActionKind.Pull ? 1 / GameConstants.ShutterPullSeconds
            : kind == (byte)ActionKind.Rescue ? 1 / GameConstants.RescueTimeSeconds
            : kind == (byte)ActionKind.Break ? 1 / GameConstants.ShutterBreakSeconds : 0;

        // v0.4 SelfState: the own collapse clock and the own hold action (exit switch, kick or hatch close).
        private void SetActionState(Player player, S2C_SelfState state)
        {
            if (_collapseRunning)
            {
                if (player.Role == Role.Killer) state.CollapseLeft = (float)_collapseLeft;
                else if (player.IsActiveSurvivor) state.CollapseLeft = (float)player.CollapseLeft;
            }
            if (player.InChaseAction)
            {
                state.ActionKind = player.ChaseKind;
                state.ActionProgress = (float)Math.Max(0, Math.Min(1,
                    (_now - player.ChaseStart) / Math.Max(TimeEpsilon, player.ChaseEnd - player.ChaseStart)));
                return;
            }
            if (player.BreakShutter >= 0)
            {
                state.ActionKind = (byte)ActionKind.Break;
                state.ActionProgress = (float)Math.Min(1, player.BreakHeld / GameConstants.ShutterBreakSeconds);
                return;
            }
            if (player.Role == Role.Survivor)
            {
                if (player.Rescuing)
                {
                    // R3: the rescuer's own hold action; the progress is the ally's restore fraction.
                    var ally = NearestLaggingAlly(player);
                    state.ActionKind = (byte)ActionKind.Rescue;
                    state.ActionProgress = ally == null ? 0 : RescueFraction(ally);
                    return;
                }
                if (player.SwitchExit < 0) return;
                state.ActionKind = (byte)ActionKind.Switch;
                state.ActionProgress = (float)Math.Min(1, _switchSeconds[player.SwitchExit] / GameConstants.ExitSwitchSeconds);
            }
            else if (player.HatchHeld > 0)
            {
                state.ActionKind = (byte)ActionKind.HatchClose;
                state.ActionProgress = (float)Math.Min(1, player.HatchHeld / GameConstants.HatchCloseSeconds);
            }
            else if (player.KickHeld > 0)
            {
                state.ActionKind = (byte)ActionKind.Kick;
                state.ActionProgress = (float)Math.Min(1, player.KickHeld / GameConstants.KickHoldSeconds);
            }
        }

        // 11/17 (R2) packet routing: only the own route, so only the hacker (or that bot's view) ever learns it, and only
        // while it hacks that route's terminal (attempt 2, F1): released E, walked away, kicked out of the hack or a gated
        // terminal sends RouteId 0, so the client gives W A S D back to movement.
        internal static bool RouteAdvertised(Player player) =>
            player.Role == Role.Survivor && player.RouteId != 0 && player.Hacking && player.HackTerminal == player.RouteTerminal;

        private static void SetRouteState(Player player, S2C_SelfState state)
        {
            if (!RouteAdvertised(player)) return;
            state.RouteId = player.RouteId;
            state.RouteLength = GameConstants.RouteLength;
            state.RouteDirs = player.RouteDirs;
            state.RouteIndex = player.RouteIndex;
        }

        private static bool Departs(float value, double predicted) => Math.Abs(value - predicted) > SelfStateTolerance;

        private static float RescueFraction(Player lagging) =>
            (float)Math.Max(0, Math.Min(1, lagging.RescueProgress / GameConstants.RescueTimeSeconds));

        private void BroadcastWorld()
        {
            UpdateVisibility();
            BuildKillerView();
            BuildSurvivorView();
            foreach (var recipient in _players)
            {
                if (recipient.ConnectionId == null || !_connections.TryGetValue(recipient.ConnectionId, out var binding)) continue;
                var world = new S2C_WorldState
                {
                    Tick = _tick, TimeLeft = Math.Max(0, GameConstants.MatchLengthSeconds - (float)_matchTime),
                    TerminalsRequired = _terminalsRequired,
                    CollapseLeft = _collapseRunning ? (float)_collapseLeft : -1
                };
                AddExitStates(world.Exits);
                // GDD v2 section 8: shutter states are public, the same for every recipient.
                for (int i = 0; i < WorldChaseProps.ShutterCount; i++) world.Shutters.Add((byte)Map.ShutterAt(i));
                if (HatchVisibleTo(recipient))
                {
                    world.HatchVisible = 1;
                    world.HatchX = _hatchX;
                    world.HatchZ = _hatchZ;
                }
                bool killer = recipient.Role == Role.Killer;
                for (int i = 0; i < _terminals.Length; i++)
                {
                    var terminal = _terminals[i];
                    // Exact progress would locate a hidden hacker: the killer only learns done or not done.
                    if (killer) terminal.Progress = (byte)(terminal.Progress >= 100 ? 100 : 0);
                    terminal.Flags = TerminalFlagsFor(i, killer);
                    world.Terminals.Add(terminal);
                }
                if (killer) world.Players.AddRange(_killerView);
                else
                {
                    world.ThreatLevel = ThreatFor(recipient);
                    world.Players.AddRange(_survivorView);
                    // Same tick, same positions as the snapshot: the line-of-sight decision and the entry agree.
                    if (KillerInSightOf(recipient)) world.Players.Add(SurvivorKillerEntry());
                }
                Send(binding.Connection, world);
                var self = SelfStateFor(recipient);
                if (SelfStateDue(recipient, self))
                {
                    Send(binding.Connection, self);
                    recipient.SelfSent = self;
                    recipient.SelfSentAt = _now;
                }
            }
        }
    }
}
