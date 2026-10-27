using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests.QA.U3c;

internal enum BotScript
{
    None,           // no bot is ever shown (the killer sees only the controlled survivor)
    Walkers,        // six bots circling far away, visible (Signal) in the killer view
    PassBy,         // bot 0 walks a +-2 m triangle past the stopped survivor at BotDistance (closest pass ~BotDistance)
    StopNear,       // bot 0 stands still at about BotDistance from the stopped survivor (no shared coordinate), visible (e.g. Lagging)
    StopNearSameZ,  // like StopNear but the bot shares the stopped survivor's exact Z float
    Cluster,        // six bots stand in a ring of radius BotDistance around the stopped survivor
    ClusterWalking, // six bots walk past together, 0.04/0.03 m apart, at BotDistance
    StandExact      // bot 0 stands at the stopped survivor's exact float position
}

internal enum StoppedLeak { None, Exact, Rounded, Offset, OffsetEveryNth, EveryNth, UnderBotId, NearerToBot, KillerOwnEntryDuplicate }

internal sealed record ViewKnobs
{
    public bool Anonymous { get; init; }
    // Id mode only: every killer-view survivor entry gets a random nonzero Id (including 1 = the killer, 2 = the controlled survivor, bot Ids).
    public bool RandomFakeIds { get; init; }
    public BotScript Bots { get; init; } = BotScript.Walkers;
    public float BotDistance { get; init; } = 0.3f;
    public StoppedLeak Leak { get; init; }
    public float OffsetX { get; init; } = 0.02f;
    public float OffsetZ { get; init; } = -0.03f;
    public int EveryN { get; init; } = 5;
    public bool ExtraEntry { get; init; }
    public bool NeverShowMoving { get; init; }
    public int KillerDelayMs { get; init; }
    public int SurvivorDelayMs { get; init; }
    public int KillerJitterMs { get; init; }
    public int SurvivorJitterMs { get; init; }
    // Survivor-bound worlds with tick % DropMod < DropCount are never sent.
    public int DropMod { get; init; }
    public int DropCount { get; init; }
    // Killer view shows the bots at their previous-tick position (a server that snapshots at another point of the tick).
    public bool BotsStaleInKillerView { get; init; }
    public bool MovingSurvivorStaleInKillerView { get; init; }
    public float BotOffsetInKillerView { get; init; }
    public bool ShuffleInIdMode { get; init; }
}

// QA fake for H3 with scripted survivor bots (ids 10..15), an exact true-position survivor stream (every survivor, bots
// included, killer never), and a killer view in today's Id mode or the U2c anonymous mode (Id 0, Yaw/CaptureCount/
// StatusTimer 0, Signal-only flags, shuffled per snapshot). Positions are computed once per tick and written to both
// views, so a correct server's killer-view positions are bit-identical to the survivor-view ones.
internal sealed class QaViewServer : IAsyncDisposable
{
    private sealed class Player(int id)
    {
        public int Id { get; } = id;
        public Peer? Peer;
        public float X = 7.13579f + id * 3, Z = -8.24681f - id * 2;
        public float Dx, Dz;
        public long LastMoving;
    }

    private readonly record struct Item(byte[] Bytes, long Due);

    private sealed class Peer(TcpClient tcp, ViewKnobs knobs)
    {
        public TcpClient Tcp { get; } = tcp;
        public Channel<Item> Output { get; } = Channel.CreateUnbounded<Item>();
        public Player? Player;
        private long lastDue;
        private readonly Random jitter = new(Environment.TickCount ^ tcp.GetHashCode());

        public void Send(IPacket packet)
        {
            long now = Stopwatch.GetTimestamp();
            long due = now;
            if (packet is S2C_WorldState && Player != null)
            {
                bool killer = Player.Id == 1;
                int delay = killer ? knobs.KillerDelayMs : knobs.SurvivorDelayMs;
                int spread = killer ? knobs.KillerJitterMs : knobs.SurvivorJitterMs;
                due = now + (long)((delay + (spread > 0 ? jitter.Next(spread + 1) : 0)) / 1000.0 * Stopwatch.Frequency);
            }
            due = Math.Max(due, lastDue); // order-preserving
            lastDue = due;
            Output.Writer.TryWrite(new Item(PacketCodec.Encode(packet), due));
        }
    }

    private readonly ViewKnobs knobs;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(60));
    private readonly Random shuffle = new(1234), fakeIds = new(99);
    private readonly object sync = new();
    private readonly List<Player> players = new();
    private readonly List<Peer> peers = new();
    private readonly List<Task> connections = new();
    private readonly Task accepting, ticking;
    private readonly (float X, float Z)[] previous = new (float, float)[6];
    private (float X, float Z) previousSurvivor;
    private bool started;
    private byte bots, choice = (byte)RoleChoice.Random;
    private uint tick;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int MatchStarts { get; private set; }
    public float MinBotDistanceWhileStill { get; private set; } = float.PositiveInfinity;
    public int StillTicks { get; private set; }
    public int LeakEntriesSent { get; private set; }
    public int KillerWorldsSent { get; private set; }

    public QaViewServer(ViewKnobs? knobs = null)
    {
        this.knobs = knobs ?? new ViewKnobs();
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
                var peer = new Peer(tcp, knobs);
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
                if (peer.Player is { } player && ReferenceEquals(player.Peer, peer)) { player.Peer = null; player.Dx = player.Dz = 0; }
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
                long wait = item.Due - Stopwatch.GetTimestamp();
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait / (double)Stopwatch.Frequency), lifetime.Token);
                await peer.Tcp.GetStream().WriteAsync(item.Bytes, lifetime.Token);
            }
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
                var player = new Player(players.Count + 1);
                players.Add(player);
                player.Peer = peer;
                peer.Player = player;
                peer.Send(new S2C_JoinResult { PlayerId = player.Id, ReconnectToken = (ulong)(1000 + player.Id) });
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
                if (!float.IsFinite(x) || !float.IsFinite(z)) x = z = 0;
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

    private (float X, float Z)[] BotPositions(uint t, bool still, float sx, float sz)
    {
        var result = new (float, float)[6];
        for (int k = 0; k < 6; k++)
        {
            float angle = t * 0.04f + k;
            result[k] = (-20f + 3f * k + 5f * MathF.Cos(angle), 15f + 5f * MathF.Sin(angle));
        }
        if (knobs.Bots == BotScript.None || !still) return result;
        float d = knobs.BotDistance;
        int phase = (int)(t % 40);
        float offset = (phase <= 20 ? phase : 40 - phase) * 0.2f - 2f + 0.0137f;
        switch (knobs.Bots)
        {
            case BotScript.PassBy: result[0] = (sx + offset, sz + d); break;
            case BotScript.StopNear: result[0] = (sx + d * 0.9f, sz + d * 0.4f); break;
            case BotScript.StopNearSameZ: result[0] = (sx + d, sz); break;
            case BotScript.StandExact: result[0] = (sx, sz); break;
            case BotScript.Cluster:
                for (int k = 0; k < 6; k++)
                    result[k] = (sx + d * MathF.Cos(k * MathF.PI / 3 + 0.3f), sz + d * MathF.Sin(k * MathF.PI / 3 + 0.3f));
                break;
            case BotScript.ClusterWalking:
                for (int k = 0; k < 6; k++) result[k] = (sx + offset + 0.04f * k, sz + d + 0.03f * k);
                break;
        }
        return result;
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
                lock (sync)
                    if (started) Step();
        }
        catch (OperationCanceledException) { }
    }

    private void Step()
    {
        tick++;
        foreach (var p in players)
        {
            float speed = p.Id == 1 ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed;
            p.X += p.Dx * speed / 20;
            p.Z += p.Dz * speed / 20;
        }
        long now = Stopwatch.GetTimestamp();
        var killer = players[0];
        var mover = players[1];
        bool still = mover.LastMoving != 0 && ReceivedFrame.SecondsBetween(mover.LastMoving, now) > 0.3;
        bool signal = ReceivedFrame.SecondsBetween(mover.LastMoving, now) < 0.15;
        var bot = BotPositions(tick, still, mover.X, mover.Z);
        if (still)
        {
            StillTicks++;
            MinBotDistanceWhileStill = Math.Min(MinBotDistanceWhileStill, Math.Min(
                MathF.Sqrt((bot[0].X - mover.X) * (bot[0].X - mover.X) + (bot[0].Z - mover.Z) * (bot[0].Z - mover.Z)),
                knobs.Bots == BotScript.Cluster ? knobs.BotDistance : float.PositiveInfinity));
        }
        if (knobs.Leak == StoppedLeak.NearerToBot && still) bot[0] = (mover.X + 0.025f, mover.Z + 0.02f);
        if (tick == 1) { for (int k = 0; k < 6; k++) previous[k] = bot[k]; previousSurvivor = (mover.X, mover.Z); }

        foreach (var recipient in players)
        {
            var world = new S2C_WorldState { Tick = tick, TimeLeft = 600 };
            if (recipient.Id == 1)
            {
                world.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = killer.X, Z = killer.Z, Yaw = 0.3f, Flags = 0 });
                if (signal && !knobs.NeverShowMoving)
                {
                    var at = knobs.MovingSurvivorStaleInKillerView ? previousSurvivor : (mover.X, mover.Z);
                    world.Players.Add(new PlayerState { Id = 2, X = at.X, Z = at.Z, Yaw = 0.5f, Flags = 1, StatusTimer = 1.5f, CaptureCount = 1 });
                }
                if (still) AddLeak(world, mover);
                if (knobs.Bots != BotScript.None)
                    for (int k = 0; k < 6; k++)
                    {
                        var at = knobs.BotsStaleInKillerView ? previous[k] : bot[k];
                        world.Players.Add(new PlayerState { Id = 10 + k, X = at.X + knobs.BotOffsetInKillerView, Z = at.Z, Yaw = 1.25f, Flags = 1 | 8, StatusTimer = 2.5f, CaptureCount = 1 });
                    }
                if (knobs.ExtraEntry && still) world.Players.Add(new PlayerState { X = 3.21f, Z = -4.56f, Flags = 1 });
                ApplyKillerView(world);
                KillerWorldsSent++;
            }
            else
            {
                if (knobs.DropMod > 0 && tick % knobs.DropMod < knobs.DropCount) continue;
                world.Players.Add(new PlayerState { Id = 2, X = mover.X, Z = mover.Z, Yaw = 0.5f, Flags = (byte)(signal ? 1 : 0) });
                if (knobs.Bots != BotScript.None)
                    for (int k = 0; k < 6; k++)
                        world.Players.Add(new PlayerState { Id = 10 + k, X = bot[k].X, Z = bot[k].Z, Yaw = 1.25f, Flags = 1 | 8 });
            }
            recipient.Peer?.Send(world);
        }
        previousSurvivor = (mover.X, mover.Z);
        for (int k = 0; k < 6; k++) previous[k] = bot[k];
    }

    private void AddLeak(S2C_WorldState world, Player mover)
    {
        float x = mover.X, z = mover.Z;
        var leak = new PlayerState { Id = 2, X = x, Z = z, Flags = 0, Yaw = 0.5f };
        switch (knobs.Leak)
        {
            case StoppedLeak.Exact: break;
            case StoppedLeak.Rounded: leak.X = (float)Math.Round(x, 2); leak.Z = (float)Math.Round(z, 2); break;
            case StoppedLeak.Offset: leak.X = x + knobs.OffsetX; leak.Z = z + knobs.OffsetZ; break;
            case StoppedLeak.EveryNth: if (tick % knobs.EveryN != 0) return; break;
            case StoppedLeak.OffsetEveryNth: if (tick % knobs.EveryN != 0) return; leak.X = x + knobs.OffsetX; leak.Z = z + knobs.OffsetZ; break;
            case StoppedLeak.UnderBotId: leak.Id = 10; break;
            case StoppedLeak.NearerToBot: leak.X = x + 0.015f; leak.Z = z + 0.012f; break;
            case StoppedLeak.KillerOwnEntryDuplicate:
                leak = new PlayerState { Id = 1, Role = (byte)Role.Killer, X = x + 0.02f, Z = z - 0.03f }; break;
            default: return;
        }
        LeakEntriesSent++;
        world.Players.Add(leak);
    }

    private void ApplyKillerView(S2C_WorldState world)
    {
        for (int i = 0; i < world.Players.Count; i++)
        {
            var p = world.Players[i];
            if (p.Role == (byte)Role.Killer && p.Id == 1) continue;
            if (knobs.RandomFakeIds) p.Id = new[] { 1, 2, 3, 10, 11, 12, 15, 777, 100000 }[fakeIds.Next(9)];
            if (knobs.Anonymous)
            {
                p.Id = 0; p.Yaw = 0; p.CaptureCount = 0; p.StatusTimer = 0;
                p.Flags &= (byte)PlayerFlags.Signal;
            }
            world.Players[i] = p;
        }
        if (!knobs.Anonymous && !knobs.RandomFakeIds && !knobs.ShuffleInIdMode) return;
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
