using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        private void Join(IConnection connection, C2S_Join join)
        {
            var existing = FindByConnection(connection.RemoteId);
            if (existing != null) { SendJoin(connection, existing); return; }
            if (join.ReconnectToken != 0)
            {
                foreach (var old in _players)
                {
                    if (old.Token != join.ReconnectToken || old.IsBot ||
                        (old.ConnectionId == null && _now - old.DisconnectedAt >= 10 - 0.000001))
                        continue;
                    if (old.ConnectionId != null && _connections.TryGetValue(old.ConnectionId, out var stale))
                    {
                        RemoveConnection(stale);
                        stale.Connection.Close();
                    }
                    old.ConnectionId = connection.RemoteId;
                    old.SelfSent = null;
                    old.DirX = old.DirZ = 0;
                    old.LastSignal = double.NegativeInfinity;
                    old.HasSeq = false;
                    old.InputTimes.Clear();
                    SendJoin(connection, old);
                    if (_matchStarted) SendMatchStart(old);
                    WriteLog($"join player={old.Id} reconnect=true");
                    BroadcastLobby();
                    return;
                }
                Send(connection, new S2C_JoinResult { Result = 2 });
                return;
            }
            Player player = null;
            // Live joins replace a bot so autostart is useful to socket QA clients.
            if (_matchStarted || _players.Count == Protocol.MaxPlayers)
            {
                for (int i = _players.Count - 1; i >= 0; --i)
                    if (_players[i].IsBot) { player = _players[i]; break; }
                if (player == null) { Send(connection, new S2C_JoinResult { Result = 1 }); return; }
            }
            if (player == null)
            {
                player = new Player { Id = _nextId++ };
                _players.Add(player);
            }
            bool hasHost = false;
            foreach (var other in _players) if (other.IsHost) hasHost = true;
            player.IsBot = false;
            player.IsHost = !hasHost;
            player.Nickname = SanitizeNickname(join.Nickname, player.Id);
            player.Token = NewToken();
            player.ConnectionId = connection.RemoteId;
            player.SelfSent = null;
            player.HasSeq = false;
            player.InputTimes.Clear();
            player.DirX = player.DirZ = 0;
            player.LastSignal = double.NegativeInfinity;
            player.Interacting = false;
            SendJoin(connection, player);
            if (_matchStarted) SendMatchStart(player);
            WriteLog($"join player={player.Id} reconnect=false");
            BroadcastLobby();
        }
        // Trims, strips control/format characters and lone surrogates, caps at Protocol.MaxNicknameBytes UTF-8 bytes
        // without splitting a surrogate pair; empty becomes "Player {id}". Public so clients can preview the same result.
        public static string SanitizeNickname(string nickname, int id)
        {
            var builder = new StringBuilder();
            nickname = nickname ?? string.Empty;
            for (int i = 0; i < nickname.Length; ++i)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(nickname, i);
                int length = char.IsHighSurrogate(nickname[i]) && i + 1 < nickname.Length && char.IsLowSurrogate(nickname[i + 1]) ? 2 : 1;
                if (category != UnicodeCategory.Control && category != UnicodeCategory.Format && category != UnicodeCategory.Surrogate)
                    builder.Append(nickname, i, length);
                i += length - 1;
            }
            string clean = builder.ToString().Trim();
            int bytes = 0, end = 0;
            while (end < clean.Length)
            {
                int length = char.IsHighSurrogate(clean[end]) ? 2 : 1;
                int count = Encoding.UTF8.GetByteCount(clean.Substring(end, length));
                if (bytes + count > Protocol.MaxNicknameBytes) break;
                bytes += count;
                end += length;
            }
            clean = clean.Substring(0, end).Trim();
            return clean.Length == 0 ? "Player " + id : clean;
        }
        private ulong NewToken()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    rng.GetBytes(bytes);
                    ulong token = BitConverter.ToUInt64(bytes, 0);
                    if (token == 0) continue;
                    bool duplicate = false;
                    foreach (var player in _players) if (player.Token == token) duplicate = true;
                    if (!duplicate) return token;
                }
            }
        }
        private void SendJoin(IConnection connection, Player player) => Send(connection,
            new S2C_JoinResult { PlayerId = player.Id, ReconnectToken = player.Token });
        private void SetBotCount(int count, bool enforceMinimum)
        {
            int humans = 0, bots = 0;
            foreach (var player in _players) { if (player.IsBot) ++bots; else ++humans; }
            count = Math.Max(enforceMinimum ? Math.Max(0, 2 - humans) : 0, Math.Min(Protocol.MaxPlayers - humans, count));
            for (int i = _players.Count - 1; i >= 0 && bots > count; i--)
            {
                if (!_players[i].IsBot) continue;
                _players.RemoveAt(i);
                --bots;
            }
            while (bots++ < count)
            {
                int id = _nextId++;
                _players.Add(new Player { Id = id, Nickname = "Bot " + id, IsBot = true });
            }
        }
        private void BroadcastLobby()
        {
            var lobby = new S2C_LobbyState { IsOffline = (byte)(_offline ? 1 : 0), HostRoleChoice = (byte)_hostChoice };
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                lobby.Slots[i] = new Slot
                {
                    PlayerId = player.Id, Nickname = player.Nickname,
                    IsBot = (byte)(player.IsBot ? 1 : 0), IsHost = (byte)(player.IsHost ? 1 : 0)
                };
            }
            foreach (var player in _players)
                if (player.ConnectionId != null && _connections.TryGetValue(player.ConnectionId, out var binding))
                    Send(binding.Connection, lobby);
        }
        private void StartMatch()
        {
            if (_matchStarted || _players.Count < 2) return;
            ReseedForMatch();
            int killer = _random.Next(_players.Count);
            int host = _players.FindIndex(p => p.IsHost);
            if (host >= 0 && _hostChoice == RoleChoice.Killer) killer = host;
            else if (host >= 0 && _hostChoice == RoleChoice.Survivor)
            {
                killer = _random.Next(_players.Count - 1);
                if (killer >= host) ++killer;
            }
            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                player.Role = i == killer ? Role.Killer : Role.Survivor;
                // Role-local IDs follow GDD table order (SkillIds): killer 0..3, survivor 0..4.
                player.Skill = (byte)_random.Next(SkillCount(player.Role));
                // Slot zero is reserved for the killer; survivor slots remain unique.
                int spawn = i == killer ? 0 : (i < killer ? i + 1 : i);
                player.X = Map.SpawnPoints[spawn].X;
                player.Z = Map.SpawnPoints[spawn].Z;
                player.ResetForMatch(_now);
                SendMatchStart(player);
            }
            _terminals = Map.CreateTerminals(_players.Count - 1, out _terminalsRequired);
            ResetMatchRules(_players[killer]);
            _matchTime = 0;
            _matchStarted = true;
            WriteLog($"match start players={_players.Count} killer={_players[killer].Id} matchSeed={_matchSeed}");
        }
        private void SendMatchStart(Player player)
        {
            if (player.ConnectionId != null && _connections.TryGetValue(player.ConnectionId, out var binding))
                Send(binding.Connection, new S2C_MatchStart { Role = (byte)player.Role, SkillId = player.Skill, Seed = _matchSeed });
        }
        private void ExpireDisconnected()
        {
            bool changed = false;
            foreach (var player in _players)
            {
                if (player.IsBot || player.ConnectionId != null || _now - player.DisconnectedAt < 10 - 0.000001) continue;
                player.IsBot = true;
                player.IsHost = false;
                player.Token = 0;
                player.HasSeq = false;
                player.InputTimes.Clear();
                player.ResetBotPlan();
                changed = true;
            }
            if (!changed) return;
            bool hasHost = _players.Exists(p => p.IsHost);
            if (!hasHost)
                foreach (var player in _players)
                    if (!player.IsBot && player.ConnectionId != null) { player.IsHost = true; break; }
            BroadcastLobby();
        }
    }
}
