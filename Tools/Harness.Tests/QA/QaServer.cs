using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests.QA;

internal enum DeadMode { None, Hang, Crash }
internal enum ClampMode { Normalize, None, PerAxis, Scale150 }

// Independent QA fake. Default settings behave like a correct U2a-shaped server
// (framing, lobby/match flow, masking, clamping, reconnect). Each knob breaks exactly one behaviour.
internal sealed class QaFaults
{
    // ---- H1 (Ping/Pong) ----
    public int? DropPongAt { get; init; }
    public int? ReorderPongAt { get; init; }          // pong i is delivered after pong i+1
    public int? AlterTimestampAt { get; init; }
    public int? DuplicatePongAt { get; init; }
    public int? ClosePeerAfterPongs { get; init; }
    public bool NaiveFraming { get; init; }           // no reassembly: partial frames at read boundaries are dropped
    public bool OneFramePerRead { get; init; }        // only the first frame of every TCP read is processed

    // ---- lifecycle / liveness ----
    public bool StopWorldAfterStart { get; init; }    // lobby + MatchStart OK, then total silence
    public bool SpamPongsInsteadOfWorld { get; init; }

    // ---- H2 (sync) ----
    public int WorldIntervalMs { get; init; } = 50;
    public int StallEveryN { get; init; }             // every Nth iteration is delayed by StallMs (both clients)
    public int StallMs { get; init; }
    public int? KillerStallAtIteration { get; init; } // only the killer pipeline stalls once
    public int? SurvivorTickJumpAt { get; init; }
    public int TickJump { get; init; } = 3;
    public int? DuplicateTickAt { get; init; }
    public int? TickBackwardAt { get; init; }

    // ---- H3 (masking) ----
    public bool NeverShowSurvivorToKiller { get; init; }
    public bool LeakFlagOnly { get; init; }           // silent survivor entry present in killer worlds
    public double LeakEntryForSeconds { get; init; }  // entry present until N s after the last movement
    public bool LeakInTerminal { get; init; }         // X and Z bytes in a TerminalState
    public bool LeakXInTimeLeft { get; init; }
    public bool LeakZInKillerYaw { get; init; }
    public bool LeakInSkillEffect { get; init; }
    public bool LeakQuantizedInTerminal { get; init; }
    public bool LeakKillerToSurvivors { get; init; }
    // U3d: the killer entry in survivor worlds is legal when sight is clear and Flags/StatusTimer/CaptureCount are 0 (off-map
    // positions here have no walls). KillerEntryFlags != 0 makes it an illegal copy of the killer's state.
    public byte KillerEntryFlags { get; init; }

    // ---- H4 (abuse) ----
    public DeadMode DeadOnNegativeLength { get; init; }
    public DeadMode DeadOnMaxLength { get; init; }
    public DeadMode DeadOnUnknownId { get; init; }
    public DeadMode DeadOnNaN { get; init; }
    public DeadMode DeadOnFlood { get; init; }
    public int? TolerateLength { get; init; }         // keep the abusive socket open and silent
    public bool TolerateUnknownId { get; init; }
    public ClampMode Clamp { get; init; } = ClampMode.Normalize;
    public bool MovePerInput { get; init; }           // each input moves speed*dt immediately
    public bool NaNPoisonsPosition { get; init; }
    public bool NaNAsMove { get; init; }
    public bool KickOnAnyMovement { get; init; }      // defensive: disconnect any client sending a non-zero direction

    // ---- H5 (reconnect) ----
    public bool BrokenReconnect { get; init; }        // rejoin with token allocates a new PlayerId
    public bool ResetPositionOnRejoin { get; init; }
    public bool ChangeRoleOnRejoin { get; init; }
    public bool ChangeStatusOnRejoin { get; init; }
    public bool ResetCapturesOnRejoin { get; init; }
    public byte RejoinResult { get; init; }
    public bool RejoinZeroToken { get; init; }
    public int RejoinDelayMs { get; init; }
}

internal sealed class QaServer : IAsyncDisposable
{
    private sealed class Player(int id)
    {
        public int Id { get; } = id;
        public ulong Token { get; } = (ulong)(5000 + id);
        public Peer? Peer;
        public float X = 7.13579f + id * 3, Z = -8.24681f - id * 2;
        public float Dx, Dz;
        public long LastMoving;
        public byte Captures = (byte)(id == 1 ? 0 : 2);
        public bool KillerOverride;
        public byte Status;
    }

    private readonly record struct Out(byte[] Bytes, int DelayMs, bool Close);

    private sealed class Peer(TcpClient tcp)
    {
        public TcpClient Tcp { get; } = tcp;
        public Channel<Out> Output { get; } = Channel.CreateUnbounded<Out>();
        public Player? Player;
        public int Pings, MoveInputs;
        public byte[]? Held;
        public void Send(IPacket packet, int delayMs = 0) => Output.Writer.TryWrite(new Out(PacketCodec.Encode(packet), delayMs, false));
        public void SendBytes(byte[] bytes) => Output.Writer.TryWrite(new Out(bytes, 0, false));
        public void CloseAfterFlush() => Output.Writer.TryWrite(new Out([], 0, true));
    }

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(40));
    private readonly object sync = new();
    private readonly List<Player> players = new();
    private readonly List<Peer> peers = new();
    private readonly List<Task> connections = new();
    private readonly Task accepting, ticking;
    private readonly QaFaults f;
    private DeadMode dead;
    private bool started;
    private long startedAt;
    private byte bots, choice = (byte)RoleChoice.Random;
    private uint tick;
    private int iteration;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int MatchStarts { get; private set; }
    public int ReconnectRequests { get; private set; }
    public bool UnexpectedFlow { get; private set; }
    public bool IsDead => dead != DeadMode.None;

    public QaServer(QaFaults? faults = null)
    {
        f = faults ?? new QaFaults();
        listener.Start();
        accepting = AcceptAsync();
        ticking = TickAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var tcp = await listener.AcceptTcpClientAsync(lifetime.Token);
                tcp.NoDelay = true;
                var peer = new Peer(tcp);
                lock (sync)
                {
                    peers.Add(peer);
                    connections.Add(ConnectionAsync(peer));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void Kill(DeadMode mode)
    {
        // Called with the lock held.
        if (mode == DeadMode.None) return;
        dead = mode;
        if (mode == DeadMode.Crash)
        {
            try { listener.Stop(); } catch { }
            foreach (var p in peers) { try { p.Tcp.Dispose(); } catch { } }
        }
    }

    private async Task ConnectionAsync(Peer peer)
    {
        Task sender = SendAsync(peer);
        try
        {
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            bool ignoring = false;
            while (true)
            {
                int count = await peer.Tcp.GetStream().ReadAsync(buffer, lifetime.Token);
                if (count == 0) return;
                if (ignoring) continue;
                if (f.NaiveFraming || f.OneFramePerRead)
                {
                    // Classic bug: assumes one TCP read == whole frames. Partial trailing frames are discarded.
                    int off = 0;
                    while (off + 6 <= count)
                    {
                        int len = BitConverter.ToInt32(buffer, off);
                        if (len < 2 || off + 4 + len > count) break;
                        ushort nid = BitConverter.ToUInt16(buffer, off + 4);
                        // A read that starts mid-frame yields garbage "frames"; whether one of them passes the
                        // length test and then fails to decode depends on TCP segmentation (scheduler timing).
                        // The naive server discards the rest of such a read like any other partial data; letting
                        // the exception escape faulted this connection task and failed the test in DisposeAsync.
                        IPacket naive;
                        try { naive = PacketCodec.Decode(nid, buffer.AsSpan(off + 6, len - 2)); }
                        catch (PacketFormatException) { break; }
                        lock (sync) Handle(peer, naive);
                        off += 4 + len;
                        if (f.OneFramePerRead) break;
                    }
                    continue;
                }
                assembler.Append(buffer.AsSpan(0, count));
                while (true)
                {
                    var result = assembler.TryRead(out ushort id, out var payload);
                    if (result == AssembleResult.NeedMoreData) break;
                    if (result == AssembleResult.Malformed)
                    {
                        int length = count >= 4 ? BitConverter.ToInt32(buffer, 0) : 0;
                        lock (sync)
                        {
                            if (length == -1) Kill(f.DeadOnNegativeLength);
                            if (length == int.MaxValue) Kill(f.DeadOnMaxLength);
                        }
                        if (f.TolerateLength == length) { ignoring = true; break; }
                        return;
                    }
                    IPacket packet;
                    try { packet = PacketCodec.Decode(id, payload.Span); }
                    catch (PacketFormatException)
                    {
                        lock (sync) Kill(f.DeadOnUnknownId);
                        if (f.TolerateUnknownId) continue;
                        return;
                    }
                    lock (sync) Handle(peer, packet);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            lock (sync)
            {
                if (peer.Player is { } player && ReferenceEquals(player.Peer, peer))
                {
                    player.Peer = null;
                    player.Dx = player.Dz = 0;
                }
            }
            peer.Output.Writer.TryComplete();
            peer.Tcp.Dispose();
            await sender;
        }
    }

    private async Task SendAsync(Peer peer)
    {
        try
        {
            await foreach (var item in peer.Output.Reader.ReadAllAsync(lifetime.Token))
            {
                if (item.DelayMs > 0) await Task.Delay(item.DelayMs, lifetime.Token);
                if (item.Close) { peer.Tcp.Client.Shutdown(SocketShutdown.Send); peer.Tcp.Dispose(); return; }
                await peer.Tcp.GetStream().WriteAsync(item.Bytes, lifetime.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void Handle(Peer peer, IPacket packet)
    {
        if (dead == DeadMode.Hang && packet is C2S_Ping) return;
        switch (packet)
        {
            case C2S_Ping ping:
                HandlePing(peer, ping);
                break;
            case C2S_Join join:
                HandleJoin(peer, join);
                break;
            case C2S_SetRole setRole when peer.Player?.Id == 1 && !started:
                choice = setRole.RoleChoice;
                Lobby();
                break;
            case C2S_SetBots setBots when peer.Player?.Id == 1 && !started:
                bots = (byte)Math.Clamp(setBots.Count, Math.Max(0, 2 - players.Count), 8 - players.Count);
                Lobby();
                break;
            case C2S_StartMatch when peer.Player?.Id == 1 && !started:
                if (players.Count != 2 || bots != 6 || choice != (byte)RoleChoice.Killer)
                {
                    UnexpectedFlow = true;
                    break;
                }
                started = true;
                startedAt = Stopwatch.GetTimestamp();
                MatchStarts++;
                foreach (var p in players)
                    p.Peer?.Send(new S2C_MatchStart { Role = p.Id == 1 ? (byte)Role.Killer : (byte)Role.Survivor, Seed = 99 });
                break;
            case C2S_MoveInput move when peer.Player is { } mover:
                HandleMove(peer, mover, move);
                break;
        }
    }

    private void HandlePing(Peer peer, C2S_Ping ping)
    {
        int index = peer.Pings++;
        long timestamp = ping.Timestamp + (f.AlterTimestampAt == index ? 1 : 0);
        if (f.DropPongAt != index)
        {
            byte[] bytes = PacketCodec.Encode(new S2C_Pong { Timestamp = timestamp });
            if (f.ReorderPongAt == index) peer.Held = bytes;
            else
            {
                peer.SendBytes(bytes);
                if (peer.Held != null) { peer.SendBytes(peer.Held); peer.Held = null; }
            }
            if (f.DuplicatePongAt == index) peer.SendBytes(bytes);
        }
        if (f.ClosePeerAfterPongs is int n && index + 1 == n) peer.CloseAfterFlush();
    }

    private void HandleJoin(Peer peer, C2S_Join join)
    {
        if (join.ReconnectToken != 0) ReconnectRequests++;
        var player = f.BrokenReconnect || join.ReconnectToken == 0 ? null : players.Find(p => p.Token == join.ReconnectToken);
        bool rebind = player != null;
        if (player == null)
        {
            player = new Player(players.Count + 1);
            players.Add(player);
        }
        else
        {
            if (f.ResetPositionOnRejoin) { player.X = 0.5f; player.Z = 0.25f; }
            if (f.ChangeRoleOnRejoin) player.KillerOverride = true;
            if (f.ChangeStatusOnRejoin) player.Status = (byte)PlayerStatus.Lagging;
            if (f.ResetCapturesOnRejoin) player.Captures = 0;
        }
        player.Peer = peer;
        peer.Player = player;
        peer.Send(new S2C_JoinResult
        {
            PlayerId = player.Id,
            ReconnectToken = rebind && f.RejoinZeroToken ? 0UL : player.Token,
            Result = rebind ? f.RejoinResult : (byte)0
        }, rebind ? f.RejoinDelayMs : 0);
        if (!started) Lobby();
    }

    private void HandleMove(Peer peer, Player mover, C2S_MoveInput move)
    {
        peer.MoveInputs++;
        float x = move.DirX, z = move.DirZ;
        if (f.KickOnAnyMovement && (x != 0 || z != 0)) { peer.Tcp.Dispose(); return; }
        if (f.DeadOnFlood != DeadMode.None && peer.MoveInputs > 300) { Kill(f.DeadOnFlood); return; }
        if (!float.IsFinite(x) || !float.IsFinite(z))
        {
            if (f.DeadOnNaN != DeadMode.None) { Kill(f.DeadOnNaN); return; }
            if (f.NaNPoisonsPosition) { mover.X = float.NaN; return; }
            x = f.NaNAsMove ? 1 : 0;
            z = 0;
        }
        switch (f.Clamp)
        {
            case ClampMode.Normalize:
                Scale(ref x, ref z, 1);
                break;
            case ClampMode.PerAxis:
                x = Math.Clamp(x, -1f, 1f);
                z = Math.Clamp(z, -1f, 1f);
                break;
            case ClampMode.Scale150:
                Scale(ref x, ref z, 1.5);
                break;
        }
        if (f.MovePerInput)
        {
            float speed = (mover.Id == 1 ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) / GameConstants.TickRate;
            mover.X += x * speed;
            mover.Z += z * speed;
        }
        else { mover.Dx = x; mover.Dz = z; }
        if (x != 0 || z != 0) mover.LastMoving = Stopwatch.GetTimestamp();
    }

    private static void Scale(ref float x, ref float z, double limit)
    {
        double length = Math.Sqrt((double)x * x + (double)z * z);
        if (length > limit) { x = (float)(x / length * limit); z = (float)(z / length * limit); }
    }

    private void Lobby()
    {
        var lobby = new S2C_LobbyState { HostRoleChoice = choice };
        int index = 0;
        foreach (var player in players)
            lobby.Slots[index++] = new Slot { PlayerId = player.Id, Nickname = "Human", IsHost = (byte)(player.Id == 1 ? 1 : 0) };
        for (int i = 0; i < bots; i++)
            lobby.Slots[index++] = new Slot { PlayerId = 10 + i, Nickname = "Bot", IsBot = 1 };
        foreach (var player in players) player.Peer?.Send(lobby);
    }

    private static bool IsKiller(Player p) => p.Id == 1 || p.KillerOverride;

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(f.WorldIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                bool stall;
                lock (sync) stall = started && f.StallEveryN > 0 && (iteration + 1) % f.StallEveryN == 0;
                if (stall) await Task.Delay(f.StallMs, lifetime.Token);
                lock (sync)
                {
                    if (!started || dead != DeadMode.None || f.StopWorldAfterStart) continue;
                    iteration++;
                    tick++;
                    if (f.SpamPongsInsteadOfWorld)
                    {
                        foreach (var p in players)
                            for (int i = 0; i < 20; i++) p.Peer?.Send(new S2C_Pong { Timestamp = i });
                        continue;
                    }
                    foreach (var player in players)
                    {
                        float speed = IsKiller(player) ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed;
                        player.X += player.Dx * speed / GameConstants.TickRate;
                        player.Z += player.Dz * speed / GameConstants.TickRate;
                    }
                    foreach (var recipient in players) Broadcast(recipient);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Broadcast(Player recipient)
    {
        if (recipient.Peer == null) return;
        uint t = tick;
        if (f.SurvivorTickJumpAt is int j && recipient.Id != 1 && iteration >= j) t += (uint)f.TickJump;
        if (iteration == f.DuplicateTickAt) t = tick - 1;
        if (iteration == f.TickBackwardAt) t = tick - 5;
        var world = new S2C_WorldState { Tick = t, TimeLeft = 600 };
        int killerEntry = -1;
        foreach (var p in players)
        {
            double sinceMoving = ReceivedFrame.SecondsBetween(p.LastMoving, Stopwatch.GetTimestamp());
            bool signal = p.LastMoving != 0 && sinceMoving < GameConstants.SignalHoldSeconds;
            if (recipient.Id != 1 && IsKiller(p) && p.Id != recipient.Id && !f.LeakKillerToSurvivors) continue;
            if (recipient.Id == 1 && !IsKiller(p))
            {
                bool include = signal && !f.NeverShowSurvivorToKiller;
                if (!signal && p.LastMoving != 0 && (f.LeakFlagOnly || sinceMoving < f.LeakEntryForSeconds)) include = true;
                if (!include)
                {
                    if (p.LastMoving != 0 && !signal) AddByteLeaks(recipient, world, p);
                    continue;
                }
            }
            if (IsKiller(p) && p.Id == recipient.Id) killerEntry = world.Players.Count;
            world.Players.Add(new PlayerState
            {
                Id = p.Id, Role = (byte)(IsKiller(p) ? Role.Killer : Role.Survivor), Status = p.Status,
                X = p.X, Z = p.Z, Flags = (byte)((signal ? PlayerFlags.Signal : PlayerFlags.None) |
                    (recipient.Id != 1 && IsKiller(p) ? (PlayerFlags)f.KillerEntryFlags : PlayerFlags.None)), CaptureCount = p.Captures
            });
        }
        if (f.LeakZInKillerYaw && recipient.Id == 1 && killerEntry >= 0)
        {
            var survivor = players.Find(p => !IsKiller(p));
            if (survivor != null && survivor.LastMoving != 0 &&
                ReceivedFrame.SecondsBetween(survivor.LastMoving, Stopwatch.GetTimestamp()) >= GameConstants.SignalHoldSeconds)
            {
                var entry = world.Players[killerEntry];
                entry.Yaw = survivor.Z;
                world.Players[killerEntry] = entry;
            }
        }
        recipient.Peer.Send(world, f.KillerStallAtIteration == iteration && recipient.Id == 1 ? f.StallMs : 0);
    }

    private void AddByteLeaks(Player recipient, S2C_WorldState world, Player silent)
    {
        if (f.LeakInTerminal) world.Terminals.Add(new TerminalState { Id = 1, X = silent.X, Z = silent.Z, Progress = 0 });
        if (f.LeakQuantizedInTerminal)
            world.Terminals.Add(new TerminalState { Id = 2, X = MathF.Round(silent.X * 10) / 10, Z = MathF.Round(silent.Z * 10) / 10 });
        if (f.LeakXInTimeLeft) world.TimeLeft = silent.X;
        if (f.LeakInSkillEffect)
            recipient.Peer?.Send(new S2C_SkillEffect { CasterId = 99, SkillId = 1, X = silent.X, Z = silent.Z, Duration = 1 });
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        try { listener.Stop(); } catch { }
        await accepting.WaitAsync(TimeSpan.FromSeconds(3));
        lock (sync) foreach (var peer in peers) { try { peer.Tcp.Dispose(); } catch { } }
        await Task.WhenAll(connections.Append(ticking)).WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Dispose();
    }
}

// Accepts TCP connections and then never reads or writes.
internal sealed class SilentServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> held = new();
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(60));
    private readonly Task accepting;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public SilentServer()
    {
        listener.Start();
        accepting = Task.Run(async () =>
        {
            try { while (true) { var c = await listener.AcceptTcpClientAsync(lifetime.Token); lock (held) held.Add(c); } }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        });
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        await accepting.WaitAsync(TimeSpan.FromSeconds(3));
        lock (held) foreach (var c in held) c.Dispose();
    }

    // A port with nothing listening (connection refused).
    public static int ClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
