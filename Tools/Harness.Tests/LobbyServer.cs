using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests;

// U3d: where the killer (1) and the controlled survivor (2) stand. OffMap keeps the historic coordinates (outside FixedMap,
// line of sight clear); Clear and Blocked put both on the map with no wall / the wall x 18..20 between them.
internal enum SightLayout { OffMap, Clear, Blocked }

// U3d killer entry in survivor worlds: never (today), per contract (only with a clear line of sight), always (a leak
// whenever blocked), per contract but with Flags 1 (a leak whenever shown), or (U3d a2, QA F1) per contract plus the
// entry on every 8th tick, sight or not, on which the killer itself gets no WorldState.
internal enum KillerLos { Off, Contract, Always, NonZeroFields, UnjoinableTicks }

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

    private sealed class Peer(TcpClient tcp, bool unknownFrames)
    {
        public TcpClient Tcp { get; } = tcp;
        public Channel<byte[]> Output { get; } = Channel.CreateUnbounded<byte[]>();
        public Player? Player;
        public int MoveInputs;
        public void Send(IPacket packet)
        {
            // UnknownFrames: a well-formed unknown-Id frame before every packet, plus a 0x11 (future Id) one per world.
            if (unknownFrames) Output.Writer.TryWrite(UnknownFrame(0x7E, 12, 0x7E));
            Output.Writer.TryWrite(PacketCodec.Encode(packet));
            if (unknownFrames && packet is S2C_WorldState) Output.Writer.TryWrite(UnknownFrame(0x11, 16, 0x11));
        }
    }

    public static byte[] UnknownFrame(ushort id, int payloadLength, byte fill, byte[]? payload = null)
    {
        var frame = new byte[6 + payloadLength];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(frame, 2 + payloadLength);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), id);
        frame.AsSpan(6).Fill(fill);
        payload?.CopyTo(frame, 6);
        return frame;
    }

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(45));
    private readonly Random shuffle = new(77);
    private float botX = -20.123f, botZ = 15.456f;
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
    public SightLayout Layout { get; init; }
    public KillerLos KillerEntry { get; init; }
    public int KillerEntriesSent { get; private set; }
    public bool BrokenReconnect { get; init; }
    public float MovementMultiplier { get; init; } = 1;
    public bool NeverMove { get; init; }
    public bool BlockForward { get; init; }
    public bool BlockFloodMovement { get; init; }
    public bool DisconnectNaN { get; init; }
    public bool DisconnectFlood { get; init; }
    public int OppositeInputs { get; private set; }

    // U2c killer view: survivor entries with Id 0, Yaw/CaptureCount/StatusTimer 0, Signal-only flags, shuffled per
    // snapshot. This fake has no terminals, so progress quantisation is exercised by ScenarioServer only.
    public bool AnonymousKillerView { get; init; }
    // Id mode with borrowed Ids: each killer-view survivor entry carries ANOTHER survivor's real Id (needs PassingBot).
    public bool RotateKillerViewIds { get; init; }
    // A scripted, always-moving survivor bot (Id 10). Once survivor 2 has stood still for 0.3 s it walks back and forth
    // right past it (closest pass about 0.2 m), so it is visible to the killer during the whole Silent window.
    public bool PassingBot { get; init; }
    public float MinBotDistanceWhileStill { get; private set; } = float.PositiveInfinity;
    // Broken variants of the stopped survivor's killer-view entry: rounded to 0.01 m, or offset by (0.02, -0.03) m.
    public bool LeakSilentRounded { get; init; }
    public bool LeakSilentOffset { get; init; }
    // Broken: an extra killer-view entry that matches no survivor while survivor 2 is still.
    public bool ExtraEntry { get; init; }
    public bool UnknownFrames { get; init; }
    // Broken: the stopped survivor's X/Z inside an unknown-Id frame to the killer.
    public bool LeakSilentInUnknown { get; init; }

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
                var peer = new Peer(tcp, UnknownFrames);
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
                    (player.X, player.Z) = (Layout, player.Id) switch
                    {
                        (SightLayout.Clear, 1) => (12.31579f, 9.75319f),
                        (SightLayout.Clear, 2) => (8.13579f, 20.24681f),
                        (SightLayout.Blocked, 1) => (25.13579f, 22.75319f),
                        (SightLayout.Blocked, 2) => (14.13579f, 20.24681f),
                        _ => (player.X, player.Z)
                    };
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
                    long now = Stopwatch.GetTimestamp();
                    var mover = players.Count > 1 ? players[1] : null;
                    bool still = mover is { LastMoving: not 0 } && ReceivedFrame.SecondsBetween(mover.LastMoving, now) > 0.3;
                    if (PassingBot)
                    {
                        // Triangle walk of +-2 m at 0.2 m/tick; the closest pass is (0.0137, 0.2) m from survivor 2, never exact.
                        int k = (int)(tick % 40);
                        float offset = (k <= 20 ? k : 40 - k) * 0.2f - 2f + 0.0137f;
                        (botX, botZ) = still ? (mover!.X + offset, mover.Z + 0.2f) : (-20.123f + offset, 15.456f);
                        if (still) MinBotDistanceWhileStill = Math.Min(MinBotDistanceWhileStill,
                            MathF.Sqrt((botX - mover!.X) * (botX - mover.X) + (botZ - mover.Z) * (botZ - mover.Z)));
                    }
                    bool leak = LeakSilent || LeakSilentRounded || LeakSilentOffset;
                    foreach (var recipient in players)
                    {
                        var world = new S2C_WorldState { Tick = tick, TimeLeft = 600 };
                        foreach (var player in players)
                        {
                            bool signal = ReceivedFrame.SecondsBetween(player.LastMoving, now) < .15;
                            if (recipient.Id != 1 && player.Id == 1) continue;
                            if (recipient.Id == 1 && player.Id != 1 && !signal && !leak) continue;
                            var state = new PlayerState { Id = player.Id, Role = (byte)(player.Id == 1 ? 1 : 0),
                                X = player.X, Z = player.Z, Flags = (byte)(signal ? 1 : 0) };
                            if (recipient.Id == 1 && player.Id != 1 && !signal)
                            {
                                if (LeakSilentRounded) (state.X, state.Z) = ((float)Math.Round(state.X, 2), (float)Math.Round(state.Z, 2));
                                if (LeakSilentOffset) (state.X, state.Z) = (state.X + 0.02f, state.Z - 0.03f); // 0.036 m, no exact X or Z bytes
                            }
                            world.Players.Add(state);
                        }
                        if (PassingBot) world.Players.Add(new PlayerState { Id = 10, X = botX, Z = botZ, Flags = 1, Yaw = 1.25f });
                        if (recipient.Id != 1 && KillerEntry != KillerLos.Off && players[0] is var killer &&
                            (KillerEntry == KillerLos.Always || (KillerEntry == KillerLos.UnjoinableTicks && tick % 8 == 0) ||
                             !FakeSight.Blocked(recipient.X, recipient.Z, killer.X, killer.Z)))
                        {
                            world.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = killer.X, Z = killer.Z,
                                Flags = (byte)(KillerEntry == KillerLos.NonZeroFields ? 1 : 0) });
                            KillerEntriesSent++;
                        }
                        if (recipient.Id == 1)
                        {
                            if (ExtraEntry && still) world.Players.Add(new PlayerState { X = 3.21f, Z = -4.56f, Flags = 1 });
                            KillerView(world);
                            if (LeakSilentInUnknown && still)
                            {
                                var payload = new byte[8];
                                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(payload, mover!.X);
                                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4), mover.Z);
                                recipient.Peer?.Output.Writer.TryWrite(UnknownFrame(0x7E, 8, 0, payload));
                            }
                        }
                        if (recipient.Id == 1 && KillerEntry == KillerLos.UnjoinableTicks && tick % 8 == 0) continue;
                        recipient.Peer?.Send(world);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void KillerView(S2C_WorldState world)
    {
        var survivorIds = players.Where(p => p.Id != 1).Select(p => p.Id).Concat(PassingBot ? new[] { 10 } : Array.Empty<int>()).ToList();
        for (int i = 0; i < world.Players.Count; i++)
        {
            var p = world.Players[i];
            if (p.Role == (byte)Role.Killer) continue;
            if (RotateKillerViewIds && survivorIds.IndexOf(p.Id) is int at and >= 0) p.Id = survivorIds[(at + 1) % survivorIds.Count];
            if (AnonymousKillerView)
            {
                p.Id = 0;
                p.Yaw = 0;
                p.CaptureCount = 0;
                p.StatusTimer = 0;
                p.Flags &= (byte)PlayerFlags.Signal;
            }
            world.Players[i] = p;
        }
        if (!AnonymousKillerView) return;
        for (int i = world.Players.Count - 1; i > 0; i--)
        {
            int j = shuffle.Next(i + 1);
            (world.Players[i], world.Players[j]) = (world.Players[j], world.Players[i]);
        }
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
