using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        private sealed class Decoy
        {
            internal bool IsDummy;
            internal int CasterId;
            internal float X, Z, Yaw;
            internal double Until;
            internal int BorrowedId;
        }

        private Random _decoyRandom;
        private readonly List<Decoy> _decoys = new List<Decoy>();
        // The killer's masked view of the latest tick; the killer bot reads only this.
        private readonly List<PlayerState> _killerView = new List<PlayerState>();
        private readonly List<PlayerState> _survivorView = new List<PlayerState>();
        private readonly List<Player> _absent = new List<Player>();
        private readonly List<Player> _candidates = new List<Player>();

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
        }

        // An afterimage stays at the last position the killer saw, so movement while hidden is never revealed.
        private bool ShowsAfterimage(Player survivor) => KillerHasAfterimage && survivor.Status == PlayerStatus.Normal &&
            !survivor.Visible && _now - survivor.LastVisibleAt <= SkillIds.AfterimageSeconds + TimeEpsilon;

        private float StatusTimer(Player player) =>
            player.Status == PlayerStatus.Lagging ? (float)Math.Max(0, player.LaggingUntil - _now) : 0;

        private void BuildKillerView()
        {
            _killerView.Clear();
            _absent.Clear();
            foreach (var player in _players)
            {
                if (player.Role == Role.Killer)
                {
                    _killerView.Add(new PlayerState
                    {
                        Id = player.Id, Role = (byte)Role.Killer, Status = (byte)PlayerStatus.Normal,
                        X = player.X, Z = player.Z, Yaw = player.Yaw,
                        Flags = player.InputHeld(_now) ? (byte)PlayerFlags.Signal : (byte)PlayerFlags.None
                    });
                    continue;
                }
                // Hacking/Rescuing/Stealthed are survivor-only details: the killer sees Signal, so a decoy matches it.
                if (player.Visible)
                    _killerView.Add(new PlayerState
                    {
                        Id = player.Id, Role = (byte)Role.Survivor, Status = (byte)player.Status,
                        X = player.X, Z = player.Z, Yaw = player.Yaw, Flags = (byte)PlayerFlags.Signal,
                        StatusTimer = StatusTimer(player), CaptureCount = player.CaptureCount
                    });
                else if (ShowsAfterimage(player))
                    _killerView.Add(new PlayerState
                    {
                        Id = player.Id, Role = (byte)Role.Survivor, Status = (byte)PlayerStatus.Normal,
                        X = player.VisibleX, Z = player.VisibleZ, Yaw = player.VisibleYaw,
                        Flags = (byte)(PlayerFlags.Signal | PlayerFlags.Afterimage),
                        CaptureCount = player.CaptureCount
                    });
                else if (player.Status == PlayerStatus.Normal)
                    _absent.Add(player);
            }
            AddDecoys();
            SortById(_killerView);
        }

        // Fake Signals borrow the Id of a survivor absent from this view, so each entry looks like a real Signal.
        private void AddDecoys()
        {
            foreach (var decoy in _decoys)
            {
                if (!decoy.IsDummy) continue;
                int index = AbsentIndex(decoy.CasterId);
                if (index >= 0) AddDecoy(decoy, index);
            }
            foreach (var decoy in _decoys)
            {
                if (decoy.IsDummy) continue;
                int index = decoy.BorrowedId != 0 ? AbsentIndex(decoy.BorrowedId) : -1;
                if (index < 0)
                {
                    // Prefer Ids that no active Dummy Packet may need, so a point keeps its Id for its lifetime.
                    _candidates.Clear();
                    foreach (var player in _absent) if (!IsDummyCaster(player.Id)) _candidates.Add(player);
                    if (_candidates.Count == 0) _candidates.AddRange(_absent);
                    if (_candidates.Count == 0) continue;
                    var pick = _candidates[_decoyRandom.Next(_candidates.Count)];
                    decoy.BorrowedId = pick.Id;
                    index = _absent.IndexOf(pick);
                }
                AddDecoy(decoy, index);
            }
        }

        private int AbsentIndex(int id)
        {
            for (int i = 0; i < _absent.Count; i++) if (_absent[i].Id == id) return i;
            return -1;
        }

        private bool IsDummyCaster(int id)
        {
            foreach (var decoy in _decoys) if (decoy.IsDummy && decoy.CasterId == id) return true;
            return false;
        }

        private void AddDecoy(Decoy decoy, int absentIndex)
        {
            var owner = _absent[absentIndex];
            // An Id is used once per view: the borrowed survivor is no longer absent for later decoys.
            _absent.RemoveAt(absentIndex);
            _killerView.Add(new PlayerState
            {
                Id = owner.Id, Role = (byte)Role.Survivor, Status = (byte)PlayerStatus.Normal,
                X = decoy.X, Z = decoy.Z, Yaw = decoy.Yaw, Flags = (byte)PlayerFlags.Signal,
                StatusTimer = 0, CaptureCount = owner.CaptureCount
            });
        }

        private static void SortById(List<PlayerState> states)
        {
            for (int i = 1; i < states.Count; i++)
            {
                var item = states[i];
                int j = i - 1;
                while (j >= 0 && states[j].Id > item.Id) { states[j + 1] = states[j]; --j; }
                states[j + 1] = item;
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

        private byte ThreatFor(Player survivor)
        {
            if (_killer == null) return 0;
            float dx = survivor.X - _killer.X, dz = survivor.Z - _killer.Z;
            return ThreatForDistance((float)Math.Sqrt(dx * dx + dz * dz));
        }

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
                world.Terminals.AddRange(_terminals);
                if (recipient.Role == Role.Killer) world.Players.AddRange(_killerView);
                else
                {
                    world.ThreatLevel = ThreatFor(recipient);
                    world.Players.AddRange(_survivorView);
                }
                Send(binding.Connection, world);
            }
        }
    }
}
