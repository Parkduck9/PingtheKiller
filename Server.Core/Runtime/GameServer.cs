using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

// Ground-truth inspection (killer-view labels, phantoms) for the server tests; never reachable through the protocol.
[assembly: InternalsVisibleTo("Server.Tests")]

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
            internal Input(Binding binding, IPacket packet, byte kind, double receivedAt)
            { Binding = binding; Packet = packet; Kind = kind; ReceivedAt = receivedAt; }
            internal readonly Binding Binding;
            internal readonly IPacket Packet;
            internal readonly byte Kind; // 0 = packet, 1 = add, 2 = close
            // Server clock seconds when the receive thread queued it (sync presses and RTT echoes).
            internal readonly double ReceivedAt;
        }

        private readonly ConcurrentQueue<Input> _inputs = new ConcurrentQueue<Input>();
        private readonly Dictionary<string, Binding> _connections = new Dictionary<string, Binding>();
        private readonly List<Player> _players = new List<Player>();
        private readonly object _tickGate = new object();
        private readonly object _lifecycleGate = new object();
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private readonly bool _offline, _autoStart, _sandbox;
        private readonly int _initialBots;
        private readonly int? _privateSeedHook, _matchSeedHook;
        // Private gameplay randomness (256-bit state each); nothing about it leaves the server (see ReseedForMatch).
        // _random: killer/skill assignment and bots; _decoyRandom: phantom placement and motion; _shuffleRandom:
        // killer-view entry order. Separate streams keep each one deterministic regardless of the others' draw counts.
        private Random _random, _decoyRandom, _shuffleRandom;
        // _syncRandom: seeds of the per-survivor sync-check streams (GameServer.Terminals).
        private Random _syncRandom;
        private int _matchSeed;
        private Thread _thread;
        private int _running, _nextId = 1;
        private bool _initialized, _matchStarted;
        private RoleChoice _hostChoice = RoleChoice.Random;
        private double _now, _matchTime;
        private uint _tick;
        private readonly Dictionary<string, double> _faultLogs = new Dictionary<string, double>();
        // One monotonic wall clock in seconds (a Stopwatch unless a test injects one), read on any thread.
        private readonly Func<double> _clock;
        // The current tick: its simulated start (_now before the increment), its length, the clock when it started, and
        // how late Run started it after its schedule (0 for a manual Tick). Simulated time t inside the tick maps to the
        // clock as _tickWallStart - _tickLag + (t - _tickSimStart), so a check and a press in different ticks are timed
        // on the same scale even when the loop wakes up late.
        private double _tickSimStart, _tickDt, _tickWallStart, _tickLag;
        // Run drops its backlog after 1 s, so a real tick is never later than this.
        private const double MaxTickLagSeconds = 2;

        public GameServer(ServerOptions options = null)
        {
            options = options ?? new ServerOptions();
            _offline = options.IsOffline;
            _autoStart = options.AutoStart;
            _sandbox = options.Sandbox;
            _initialBots = Math.Max(0, Math.Min(Protocol.MaxPlayers, options.BotCount));
            _privateSeedHook = options.Seed;
            _matchSeedHook = options.MatchSeedForTests;
            if (options.ClockForTests != null) _clock = options.ClockForTests;
            else
            {
                var stopwatch = Stopwatch.StartNew();
                _clock = () => stopwatch.Elapsed.TotalSeconds;
            }
            Nav = new NavGrid(Map);
            SeedPrivateStreams();
        }

        private void SeedPrivateStreams()
        {
            if (_privateSeedHook is int seed)
            {
                _random = PrivateRandom.FromSeed(seed, 0);
                _decoyRandom = PrivateRandom.FromSeed(seed, 1);
                _shuffleRandom = PrivateRandom.FromSeed(seed, 2);
                _syncRandom = PrivateRandom.FromSeed(seed, 3);
                _rttRandom = PrivateRandom.FromSeed(seed, 4);
            }
            else
            {
                _random = PrivateRandom.FromCsprng();
                _decoyRandom = PrivateRandom.FromCsprng();
                _shuffleRandom = PrivateRandom.FromCsprng();
                _syncRandom = PrivateRandom.FromCsprng();
                _rttRandom = PrivateRandom.FromCsprng();
            }
        }

        // Without the test hook every match gets fresh CSPRNG-seeded streams; the cosmetic MatchStart.Seed is drawn
        // independently, so no client can replay killer/skill assignment, bot decisions, phantoms or view order.
        private void ReseedForMatch()
        {
            if (_privateSeedHook == null) SeedPrivateStreams();
            _matchSeed = _matchSeedHook ?? RandomNumberGeneratorSeed();
        }
        public FixedMap Map { get; } = new FixedMap();
        internal NavGrid Nav { get; }
        public event Action<string> Log;

        public void AddConnection(IConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            var binding = new Binding { Connection = connection };
            binding.Available = () => DrainConnection(binding);
            binding.Closed = () => _inputs.Enqueue(new Input(binding, null, 2, _clock()));
            _inputs.Enqueue(new Input(binding, null, 1, _clock()));
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
                _inputs.Enqueue(new Input(binding, packet, 0, _clock()));
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
                    // -wait: how far behind its schedule this tick starts (the sync judge maps the clock through it).
                    try { Tick(1f / GameConstants.TickRate, -wait); }
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

        public void Tick(float dt) => Tick(dt, 0);

        // lateBy: seconds between the tick's scheduled start and now (Run, and the sync timing tests).
        internal void Tick(float dt, double lateBy)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0 || dt > 60)
                throw new ArgumentOutOfRangeException(nameof(dt));
            lock (_tickGate)
            {
                if (Volatile.Read(ref _running) != 0 && Thread.CurrentThread != _thread)
                    throw new InvalidOperationException("Manual Tick cannot run alongside Start().");
                _tickWallStart = _clock();
                _tickLag = double.IsNaN(lateBy) || lateBy < 0 ? 0 : Math.Min(lateBy, MaxTickLagSeconds);
                _tickSimStart = _now;
                _tickDt = dt;
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
                            Handle(input.Binding.Connection, input.Packet, input.ReceivedAt);
                    }
                }
                UpdateRttProbes();
                if (_matchStarted)
                {
                    UpdateBots();
                }
                // Movement, rules and recipient masking evaluate the same instant.
                double start = _now;
                _now += dt;
                if (_matchStarted)
                {
                    MovePlayers(start, dt);
                    _matchTime += dt;
                    RunRules(dt);
                }
                ++_tick;
                ExpireDisconnected();
                ExpireUnjoined();
                if (_matchStarted)
                {
                    BroadcastWorld();
                    if (_result != null) FinishMatch();
                }
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
            player.Interacting = false;
            ResetRtt(player);
            WriteLog($"leave player={player.Id}");
            BroadcastLobby();
        }
        private Player FindByConnection(string id)
        {
            foreach (var player in _players) if (player.ConnectionId == id) return player;
            return null;
        }
        // receivedAt: server clock when the packet was queued; sync presses and RTT echoes read it.
        private void Handle(IConnection connection, IPacket packet, double receivedAt)
        {
            if (connection.IsClosed) return;
            if (packet is C2S_Join join) { Join(connection, join); return; }
            var player = FindByConnection(connection.RemoteId);
            if (player == null) return;
            if (packet is C2S_RttEcho echo) { ReceiveRttEcho(player, echo.Nonce, receivedAt); return; }
            // Semantically invalid values are ignored; only wire-format violations close a connection.
            if (packet is C2S_MoveInput move) { if (_matchStarted) ApplyMove(player, move); return; }
            if (packet is C2S_Interact interact) { if (_matchStarted) SetInteract(player, interact.Active); return; }
            if (packet is C2S_UseSkill skill) { if (_matchStarted) RequestSkill(player, skill.SkillId); return; }
            if (packet is C2S_SyncPress press) { if (_matchStarted) SyncPress(player, press.CheckId, receivedAt); return; }
            if (!_matchStarted && player.IsHost)
            {
                if (packet is C2S_SetBots bots)
                {
                    if (bots.Count > Protocol.MaxPlayers - 1) return;
                    SetBotCount(bots.Count, true);
                    BroadcastLobby();
                }
                else if (packet is C2S_SetRole role)
                {
                    var choice = (RoleChoice)role.RoleChoice;
                    if (choice != RoleChoice.Survivor && choice != RoleChoice.Killer && choice != RoleChoice.Random) return;
                    _hostChoice = choice;
                    BroadcastLobby();
                }
                else if (packet is C2S_StartMatch) StartMatch();
            }
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
