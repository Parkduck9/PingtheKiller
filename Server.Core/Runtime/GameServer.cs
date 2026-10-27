using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer : IDisposable
    {
        private sealed class Binding
        {
            internal IConnection Connection;
            internal Action Available;
            internal Action Closed;
            internal int Pending;
            internal double AddedAt;
        }
        private readonly struct Input
        {
            internal Input(Binding binding, IPacket packet, byte kind)
            { Binding = binding; Packet = packet; Kind = kind; }
            internal readonly Binding Binding;
            internal readonly IPacket Packet;
            internal readonly byte Kind; // 0 = packet, 1 = add, 2 = close
        }

        private readonly ConcurrentQueue<Input> _inputs = new ConcurrentQueue<Input>();
        private readonly Dictionary<string, Binding> _connections = new Dictionary<string, Binding>();
        private readonly List<Player> _players = new List<Player>();
        private readonly object _tickGate = new object();
        private readonly object _lifecycleGate = new object();
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private readonly bool _offline, _autoStart;
        private readonly int _initialBots, _seed;
        private readonly Random _random;
        private Thread _thread;
        private int _running, _nextId = 1;
        private bool _initialized, _matchStarted;
        private RoleChoice _hostChoice = RoleChoice.Random;
        private double _now, _matchTime;
        private uint _tick;
        private TerminalState[] _terminals = Array.Empty<TerminalState>();
        private byte _terminalsRequired;
        private readonly Dictionary<string, double> _faultLogs = new Dictionary<string, double>();

        public GameServer(ServerOptions options = null)
        {
            options = options ?? new ServerOptions();
            _offline = options.IsOffline;
            _autoStart = options.AutoStart;
            _initialBots = Math.Max(0, Math.Min(Protocol.MaxPlayers, options.BotCount));
            _seed = options.Seed ?? RandomNumberGeneratorSeed();
            _random = new Random(_seed);
        }
        public FixedMap Map { get; } = new FixedMap();
        public event Action<string> Log;

        public void AddConnection(IConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            var binding = new Binding { Connection = connection };
            binding.Available = () => DrainConnection(binding);
            binding.Closed = () => _inputs.Enqueue(new Input(binding, null, 2));
            _inputs.Enqueue(new Input(binding, null, 1));
            connection.PacketAvailable += binding.Available;
            connection.Closed += binding.Closed;
            DrainConnection(binding);
            if (connection.IsClosed) binding.Closed();
        }

        private void DrainConnection(Binding binding)
        {
            while (binding.Connection.TryReceive(out var packet))
            {
                // Ping has no game-state dependency and can be answered directly by I/O.
                if (packet is C2S_Ping ping)
                {
                    Send(binding.Connection, new S2C_Pong { Timestamp = ping.Timestamp });
                    continue;
                }
                if (Interlocked.Increment(ref binding.Pending) > 4096)
                {
                    Interlocked.Decrement(ref binding.Pending);
                    binding.Connection.Close();
                    break;
                }
                _inputs.Enqueue(new Input(binding, packet, 0));
            }
        }

        public void Start()
        {
            lock (_lifecycleGate)
            {
                if (_running != 0) return;
                _stop.Reset();
                _thread = new Thread(Run) { IsBackground = true, Name = "PingTheKiller.GameLoop" };
                Volatile.Write(ref _running, 1);
                _thread.Start();
            }
        }
        public void Stop()
        {
            lock (_lifecycleGate)
            {
                _stop.Set();
                if (_thread != null && Thread.CurrentThread != _thread) _thread.Join();
                Volatile.Write(ref _running, 0);
                lock (_tickGate)
                {
                    foreach (var binding in _connections.Values) binding.Connection.Close();
                    while (_inputs.TryDequeue(out var input))
                    {
                        try
                        {
                            if (input.Kind == 1)
                            {
                                Register(input.Binding);
                                input.Binding.Connection.Close();
                                RemoveConnection(input.Binding);
                            }
                            else if (input.Kind == 2) RemoveConnection(input.Binding);
                            else Interlocked.Decrement(ref input.Binding.Pending);
                        }
                        catch (Exception ex) { WriteLog("stop error " + ex.GetType().Name + ": " + ex.Message); }
                    }
                }
            }
        }
        public void Dispose() => Stop();
        private void Run()
        {
            var clock = Stopwatch.StartNew();
            double next = 0;
            double step = 1.0 / GameConstants.TickRate;
            try
            {
                while (!_stop.IsSet)
                {
                    double wait = next - clock.Elapsed.TotalSeconds;
                    if (wait > 0)
                    {
                        _stop.Wait(TimeSpan.FromSeconds(wait));
                        continue;
                    }
                    try { Tick(1f / GameConstants.TickRate); }
                    catch (Exception ex)
                    {
                        string message = ex.GetType().Name + ": " + ex.Message;
                        double now = clock.Elapsed.TotalSeconds;
                        // Retain only this second's distinct faults, including across restarts.
                        var expired = new List<string>();
                        foreach (var entry in _faultLogs)
                            if (now - entry.Value >= 1 || now < entry.Value) expired.Add(entry.Key);
                        foreach (string key in expired) _faultLogs.Remove(key);
                        if (!_faultLogs.ContainsKey(message))
                        {
                            _faultLogs[message] = now;
                            WriteLog("tick error " + message);
                        }
                    }
                    next += step;
                    // Do not accumulate an unbounded catch-up backlog after suspension.
                    if (clock.Elapsed.TotalSeconds - next > 1) next = clock.Elapsed.TotalSeconds;
                }
            }
            finally { Volatile.Write(ref _running, 0); }
        }

        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0 || dt > 60)
                throw new ArgumentOutOfRangeException(nameof(dt));
            lock (_tickGate)
            {
                if (Volatile.Read(ref _running) != 0 && Thread.CurrentThread != _thread)
                    throw new InvalidOperationException("Manual Tick cannot run alongside Start().");
                if (!_initialized)
                {
                    _initialized = true;
                    SetBotCount(_initialBots, false);
                    if (_autoStart) StartMatch();
                }
                // Expire before Join: tokens at/after the deadline cannot reclaim a slot.
                ExpireDisconnected();
                ExpireUnjoined();
                while (_inputs.TryDequeue(out var input))
                {
                    if (input.Kind == 1) Register(input.Binding);
                    else if (input.Kind == 2) RemoveConnection(input.Binding);
                    else
                    {
                        Interlocked.Decrement(ref input.Binding.Pending);
                        if (_connections.TryGetValue(input.Binding.Connection.RemoteId, out var active) && active == input.Binding)
                            Handle(input.Binding.Connection, input.Packet);
                    }
                }
                if (_matchStarted)
                {
                    UpdateBots();
                }
                // Movement and recipient masking must evaluate the same instant.
                _now += dt;
                if (_matchStarted)
                {
                    foreach (var player in _players)
                    {
                        // A held direction lives only as long as the Signal hold of the last non-zero input, evaluated
                        // with the same _now as BroadcastWorld: a player that moves this tick is always Signal (visible).
                        // Clients must therefore resend MoveInput (>= 10 Hz) while a key is held.
                        if (!player.IsSignal(_now)) { player.DirX = player.DirZ = 0; continue; }
                        float speed = player.Role == Role.Killer ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed;
                        var position = Map.Move(player.X, player.Z, player.DirX * speed * dt, player.DirZ * speed * dt);
                        player.X = position.X;
                        player.Z = position.Z;
                    }
                    _matchTime += dt;
                }
                ++_tick;
                ExpireDisconnected();
                ExpireUnjoined();
                if (_matchStarted) BroadcastWorld();
            }
        }

        private void Register(Binding binding)
        {
            string id = binding.Connection.RemoteId;
            if (_connections.ContainsKey(id))
            {
                binding.Connection.PacketAvailable -= binding.Available;
                binding.Connection.Closed -= binding.Closed;
                return;
            }
            _connections.Add(id, binding);
            binding.AddedAt = _now;
        }
        // A connection without a successful C2S_Join is closed this long after it was added. Ping is allowed meanwhile.
        public const double JoinTimeoutSeconds = 10;
        private readonly List<Binding> _expiredScratch = new List<Binding>();
        private void ExpireUnjoined()
        {
            foreach (var binding in _connections.Values)
                if (_now - binding.AddedAt >= JoinTimeoutSeconds - 0.000001 && FindByConnection(binding.Connection.RemoteId) == null)
                    _expiredScratch.Add(binding);
            if (_expiredScratch.Count == 0) return;
            try
            {
                foreach (var binding in _expiredScratch)
                {
                    RemoveConnection(binding);
                    binding.Connection.Close();
                }
            }
            finally { _expiredScratch.Clear(); }
        }
        private void WriteLog(string message)
        {
            var subscribers = Log;
            if (subscribers == null) return;
            foreach (Action<string> subscriber in subscribers.GetInvocationList())
                try { subscriber(message); } catch (Exception) { /* Logging cannot stop simulation. */ }
        }
        private void RemoveConnection(Binding binding)
        {
            string id = binding.Connection.RemoteId;
            if (!_connections.TryGetValue(id, out var active) || active != binding) return;
            _connections.Remove(id);
            binding.Connection.PacketAvailable -= binding.Available;
            binding.Connection.Closed -= binding.Closed;
            var player = FindByConnection(id);
            if (player == null) return;
            player.ConnectionId = null;
            player.DisconnectedAt = _now;
            player.DirX = player.DirZ = 0;
            player.LastSignal = double.NegativeInfinity;
            WriteLog($"leave player={player.Id}");
            BroadcastLobby();
        }
        private Player FindByConnection(string id)
        {
            foreach (var player in _players) if (player.ConnectionId == id) return player;
            return null;
        }
        private void Handle(IConnection connection, IPacket packet)
        {
            if (connection.IsClosed) return;
            if (packet is C2S_Join join) { Join(connection, join); return; }
            var player = FindByConnection(connection.RemoteId);
            if (player == null) return;
            if (packet is C2S_MoveInput move && _matchStarted) { ApplyMove(player, move); return; }
            if (!_matchStarted && player.IsHost)
            {
                if (packet is C2S_SetBots bots) { SetBotCount(bots.Count, true); BroadcastLobby(); }
                else if (packet is C2S_SetRole role) { _hostChoice = (RoleChoice)role.RoleChoice; BroadcastLobby(); }
                else if (packet is C2S_StartMatch) StartMatch();
            }
            // TODO U2b: C2S_Interact / C2S_UseSkill, capture/rescue, objectives and results.
        }
        private void Send(IConnection connection, IPacket packet)
        {
            // A faulty connection is closed on its own; it must not abort the tick for other recipients.
            try { connection.Send(PacketCodec.Encode(packet)); }
            catch (Exception ex)
            {
                WriteLog("send error " + ex.GetType().Name + ": " + ex.Message);
                try { connection.Close(); } catch (Exception) { /* Already faulty; nothing more to do. */ }
            }
        }
        private static int RandomNumberGeneratorSeed()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
