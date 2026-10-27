using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests;

// Minimal U2a-shaped test fixture. It verifies the harness's lobby/rebind/capture
// orchestration over TCP; it is not an implementation or validation of Server.Core.
internal sealed class LobbyServer : IAsyncDisposable
{
    private sealed class Player(int id)
    {
        public int Id { get; } = id;
        public ulong Token { get; } = (ulong)(1000 + id);
        public Peer? Peer;
        public float X = 7.13579f + id * 3, Z = -8.24681f - id * 2;
        public float Dx, Dz;
        public long LastMoving;
    }

    private sealed class Peer(TcpClient tcp)
    {
        public TcpClient Tcp { get; } = tcp;
        public Channel<byte[]> Output { get; } = Channel.CreateUnbounded<byte[]>();
        public Player? Player;
        public int MoveInputs;
        public void Send(IPacket packet) => Output.Writer.TryWrite(PacketCodec.Encode(packet));
    }

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(25));
    private readonly object sync = new();
    private readonly List<Player> players = new();
    private readonly List<Peer> peers = new();
    private readonly List<Task> connections = new();
    private readonly Task accepting, ticking;
    private bool started;
    private byte bots, choice = (byte)RoleChoice.Random;
    private uint tick;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int MatchStarts { get; private set; }
    public int Rebinds { get; private set; }
    public int ReconnectRequests { get; private set; }
    public bool LeakSilent { get; init; }
    public bool LeakKiller { get; init; }
    public bool BrokenReconnect { get; init; }
    public float MovementMultiplier { get; init; } = 1;
    public bool NeverMove { get; init; }
    public bool BlockForward { get; init; }
    public bool BlockFloodMovement { get; init; }
    public bool DisconnectNaN { get; init; }
    public bool DisconnectFlood { get; init; }
    public int OppositeInputs { get; private set; }

    public LobbyServer()
    {
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
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task ConnectionAsync(Peer peer)
    {
        Task sender = SendAsync(peer);
        try
        {
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            while (true)
            {
                int count = await peer.Tcp.GetStream().ReadAsync(buffer, lifetime.Token);
                if (count == 0) return;
                assembler.Append(buffer.AsSpan(0, count));
                while (true)
                {
                    var result = assembler.TryRead(out ushort id, out var payload);
                    if (result == AssembleResult.NeedMoreData) break;
                    if (result == AssembleResult.Malformed) return;
                    IPacket packet = PacketCodec.Decode(id, payload.Span);
                    lock (sync) Handle(peer, packet);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or PacketFormatException or ObjectDisposedException) { }
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
            await foreach (byte[] bytes in peer.Output.Reader.ReadAllAsync(lifetime.Token))
                await peer.Tcp.GetStream().WriteAsync(bytes, lifetime.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
    }

    private void Handle(Peer peer, IPacket packet)
    {
        switch (packet)
        {
            case C2S_Ping ping:
                peer.Send(new S2C_Pong { Timestamp = ping.Timestamp });
                break;
            case C2S_Join join:
                if (join.ReconnectToken != 0) ReconnectRequests++;
                var player = BrokenReconnect ? null : players.Find(p => p.Token == join.ReconnectToken);
                if (player == null)
                {
                    player = new Player(players.Count + 1);
                    players.Add(player);
                }
                else Rebinds++;
                player.Peer = peer;
                peer.Player = player;
                peer.Send(new S2C_JoinResult { PlayerId = player.Id, ReconnectToken = player.Token });
                if (!started) Lobby();
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
                    throw new InvalidOperationException("Harness did not establish the expected host/role/bots flow.");
                started = true;
                MatchStarts++;
                foreach (var p in players)
                    p.Peer?.Send(new S2C_MatchStart { Role = p.Id == 1 ? (byte)Role.Killer : (byte)Role.Survivor, Seed = 456 });
                break;
            case C2S_MoveInput move when peer.Player is { } mover:
                float x = move.DirX, z = move.DirZ;
                peer.MoveInputs++;
                if ((DisconnectNaN && (!float.IsFinite(x) || !float.IsFinite(z))) ||
                    (DisconnectFlood && peer.MoveInputs > 100))
                {
                    peer.Tcp.Dispose();
                    break;
                }
                if (x < 0) OppositeInputs++;
                if (!float.IsFinite(x) || !float.IsFinite(z)) x = z = 0;
                if (NeverMove || (BlockForward && x > 0) || (BlockFloodMovement && Math.Abs(x) == 1 && z == 0))
                    x = z = 0;
                double length = Math.Sqrt((double)x * x + (double)z * z);
                if (length > 1) { x = (float)(x / length); z = (float)(z / length); }
                mover.Dx = x;
                mover.Dz = z;
                if (x != 0 || z != 0) mover.LastMoving = Stopwatch.GetTimestamp();
                break;
        }
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

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                lock (sync)
                {
                    if (!started) continue;
                    tick++;
                    foreach (var player in players)
                    {
                        float speed = (player.Id == 1 ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) * MovementMultiplier;
                        player.X += player.Dx * speed / 20;
                        player.Z += player.Dz * speed / 20;
                    }
                    foreach (var recipient in players)
                    {
                        var world = new S2C_WorldState { Tick = tick, TimeLeft = 600 };
                        foreach (var player in players)
                        {
                            bool signal = ReceivedFrame.SecondsBetween(player.LastMoving, Stopwatch.GetTimestamp()) < .15;
                            if (recipient.Id != 1 && player.Id == 1 && !LeakKiller) continue;
                            if (recipient.Id == 1 && player.Id != 1 && !signal && !LeakSilent) continue;
                            world.Players.Add(new PlayerState { Id = player.Id, Role = (byte)(player.Id == 1 ? 1 : 0),
                                X = player.X, Z = player.Z, Flags = (byte)(signal ? 1 : 0) });
                        }
                        recipient.Peer?.Send(world);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        await accepting.WaitAsync(TimeSpan.FromSeconds(3));
        lock (sync) foreach (var peer in peers) peer.Tcp.Dispose();
        await Task.WhenAll(connections.Append(ticking)).WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Dispose();
    }
}
