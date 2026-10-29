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
            internal KillerViewSource(int label, bool phantom, bool afterimage)
            { Label = label; Phantom = phantom; Afterimage = afterimage; }
            // Real entries: the player Id. Phantoms: the phantom serial.
            internal readonly int Label;
            internal readonly bool Phantom;
            internal readonly bool Afterimage;
        }

        // The killer's masked view of the latest tick; the killer bot reads only this.
        private readonly List<PlayerState> _killerView = new List<PlayerState>();
        private readonly List<KillerViewSource> _killerViewSources = new List<KillerViewSource>();
        private readonly List<PlayerState> _survivorView = new List<PlayerState>();
        internal IReadOnlyList<KillerViewSource> KillerViewSources => _killerViewSources;

        private bool KillerHasAfterimage => !_sandbox && _killer != null && _killer.Skill == SkillIds.PacketAfterimage;

        // Effective Signal: Lagging and forced exposure always show; Stealth hides moving, hacking and rescuing.
        private bool IsVisibleToKiller(Player survivor)
        {
            if (survivor.Status == PlayerStatus.Lagging) return true;
            if (survivor.Status != PlayerStatus.Normal) return false;
            if (Active(survivor.ExposedUntil)) return true;
            if (Active(survivor.StealthUntil)) return false;
            return survivor.InputHeld(_now) || survivor.Hacking || survivor.Rescuing;
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

        // One afterimage rule for real survivors and phantoms: the entry stays frozen at the last position the killer
        // saw for AfterimageSeconds, so movement while hidden is never revealed.
        private bool AfterimageWindow(bool visible, double lastVisibleAt) =>
            KillerHasAfterimage && !visible && _now - lastVisibleAt <= SkillIds.AfterimageSeconds + TimeEpsilon;

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
                        Flags = player.InputHeld(_now) ? (byte)PlayerFlags.Signal : (byte)PlayerFlags.None
                    });
                    _killerViewSources.Add(new KillerViewSource(player.Id, false, false));
                    continue;
                }
                bool afterimage = player.Status == PlayerStatus.Normal && AfterimageWindow(player.Visible, player.LastVisibleAt);
                if (player.Visible || afterimage)
                    AddKillerEntry(player.Visible ? player.Status : PlayerStatus.Normal, player.VisibleX, player.VisibleZ,
                        new KillerViewSource(player.Id, false, afterimage));
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
                    bool afterimage = AfterimageWindow(phantom.Visible, phantom.LastVisibleAt);
                    phantom.InView = (phantom.Visible || afterimage) && _killerView.Count < phantomLimit;
                    if (phantom.InView)
                        AddKillerEntry(PlayerStatus.Normal, phantom.VisibleX, phantom.VisibleZ,
                            new KillerViewSource(phantom.Serial, true, afterimage));
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
                _survivorView.Add(new PlayerState
                {
                    Id = player.Id, Role = (byte)Role.Survivor, Status = (byte)player.Status,
                    X = player.X, Z = player.Z, Yaw = player.Yaw, Flags = (byte)flags,
                    StatusTimer = StatusTimer(player), CaptureCount = player.CaptureCount
                });
            }
        }

        private float StatusTimer(Player player) =>
            player.Status == PlayerStatus.Lagging ? (float)Math.Max(0, player.LaggingUntil - _now) : 0;

        private byte ThreatFor(Player survivor)
        {
            if (_killer == null) return 0;
            float dx = survivor.X - _killer.X, dz = survivor.Z - _killer.Z;
            return ThreatForDistance((float)Math.Sqrt(dx * dx + dz * dz));
        }

        private const float ExecutionGuidanceRange = 3f;

        private S2C_SelfState SelfStateFor(Player player)
        {
            var state = new S2C_SelfState { SkillCooldown = (float)Math.Max(0, player.SkillReadyAt - _now) };
            if (player.Role == Role.Killer)
            {
                Player target = null;
                float best = ExecutionGuidanceRange * ExecutionGuidanceRange + 0.000001f;
                foreach (var survivor in _players)
                {
                    if (survivor.Role != Role.Survivor || survivor.Status != PlayerStatus.Lagging) continue;
                    float distance = DistanceSquared(player.X, player.Z, survivor.X, survivor.Z);
                    if (distance <= best) { best = distance; target = survivor; }
                }
                if (target != null) state.ExecutionGraceLeft = (float)Math.Max(0, target.ExecutionGraceUntil - _now);
                return state;
            }
            if (player.Status == PlayerStatus.Lagging) state.RescueProgress = RescueFraction(player);
            else if (player.Rescuing)
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
                if (target != null) state.RescueProgress = RescueFraction(target);
            }
            if (player.Status == PlayerStatus.Normal) state.InvulnerableLeft = (float)Math.Max(0, player.InvulnerableUntil - _now);
            return state;
        }

        // Clients count the timers down (and a running rescue up) locally, so SelfState is sent only when a value
        // departs from that prediction (cooldown started, rescue started or stopped, immunity or grace granted) and
        // as a 1 Hz refresh.
        private const double SelfStateRefreshSeconds = 1;
        private const double SelfStateTolerance = 0.01;

        private bool SelfStateDue(Player player, S2C_SelfState now)
        {
            var last = player.SelfSent;
            double elapsed = _now - player.SelfSentAt;
            if (last == null || elapsed >= SelfStateRefreshSeconds - TimeEpsilon) return true;
            double rescue = last.RescueProgress > 0 ? Math.Min(1, last.RescueProgress + elapsed / GameConstants.RescueTimeSeconds) : 0;
            return Departs(now.SkillCooldown, Math.Max(0, last.SkillCooldown - elapsed)) ||
                Departs(now.InvulnerableLeft, Math.Max(0, last.InvulnerableLeft - elapsed)) ||
                Departs(now.ExecutionGraceLeft, Math.Max(0, last.ExecutionGraceLeft - elapsed)) ||
                Departs(now.RescueProgress, rescue);
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
                    ExitOpen = (byte)(_exitOpen ? 1 : 0), ExitLocked = (byte)(ExitBlocked ? 1 : 0),
                    TerminalsRequired = _terminalsRequired
                };
                if (recipient.Role == Role.Killer)
                {
                    // Exact progress would locate a hidden hacker: the killer only learns done or not done.
                    foreach (var terminal in _terminals)
                        world.Terminals.Add(new TerminalState
                        {
                            Id = terminal.Id, X = terminal.X, Z = terminal.Z, Progress = (byte)(terminal.Progress >= 100 ? 100 : 0)
                        });
                    world.Players.AddRange(_killerView);
                }
                else
                {
                    world.Terminals.AddRange(_terminals);
                    world.ThreatLevel = ThreatFor(recipient);
                    world.Players.AddRange(_survivorView);
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
