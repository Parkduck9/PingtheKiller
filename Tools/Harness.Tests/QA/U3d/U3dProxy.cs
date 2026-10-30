using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests.QA.U3d;

// QA-owned man-in-the-middle for U3d. Client-to-server bytes are copied untouched. Every server-to-client frame goes through
// Rewrite(connection, decoded packet or null for an unknown Id, raw frame) -> frames to forward, and is released after a
// per-connection delay (frame order is preserved, so a delay is a pure stream skew). Connection 0 is the first client to
// connect: the killer in both H3 (MatchSession) and H6 (ScenarioRun).
internal sealed class U3dProxy : IAsyncDisposable
{
    public delegate IEnumerable<byte[]> Rewrite(int connection, IPacket? packet, byte[] raw);

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(190));
    private readonly int upstreamPort;
    private readonly Rewrite rewrite;
    private readonly Func<int, TimeSpan> delay;
    private readonly List<Task> tasks = new();
    private readonly List<TcpClient> sockets = new();
    private readonly Task accepting;
    private int connections;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public U3dProxy(int upstreamPort, Rewrite rewrite, Func<int, TimeSpan>? delay = null)
    {
        this.upstreamPort = upstreamPort;
        this.rewrite = rewrite;
        this.delay = delay ?? (_ => TimeSpan.Zero);
        listener.Start();
        accepting = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                client.NoDelay = true;
                var upstream = new TcpClient { NoDelay = true };
                await upstream.ConnectAsync(IPAddress.Loopback, upstreamPort, lifetime.Token);
                int index = connections++;
                var queue = Channel.CreateUnbounded<(long Due, byte[] Frame)>();
                lock (tasks)
                {
                    sockets.Add(client);
                    sockets.Add(upstream);
                    tasks.Add(CopyAsync(client, upstream));
                    tasks.Add(ForwardAsync(index, upstream, queue.Writer));
                    tasks.Add(WriteAsync(client, queue.Reader));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task CopyAsync(TcpClient from, TcpClient to)
    {
        try { await from.GetStream().CopyToAsync(to.GetStream(), lifetime.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        finally { try { to.Client.Shutdown(SocketShutdown.Send); } catch (Exception) { } }
    }

    private async Task ForwardAsync(int index, TcpClient upstream, ChannelWriter<(long, byte[])> queue)
    {
        try
        {
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            while (true)
            {
                int count = await upstream.GetStream().ReadAsync(buffer, lifetime.Token);
                if (count == 0) return;
                assembler.Append(buffer.AsSpan(0, count));
                while (true)
                {
                    var result = assembler.TryRead(out ushort id, out var payload);
                    if (result != AssembleResult.Packet) break;
                    var raw = new byte[6 + payload.Length];
                    BinaryPrimitives.WriteInt32LittleEndian(raw, 2 + payload.Length);
                    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4), id);
                    payload.Span.CopyTo(raw.AsSpan(6));
                    IPacket? packet = Enum.IsDefined((PacketId)id) ? PacketCodec.Decode(id, payload.Span) : null;
                    long due = Stopwatch.GetTimestamp() + (long)(delay(index).TotalSeconds * Stopwatch.Frequency);
                    foreach (var frame in rewrite(index, packet, raw)) queue.TryWrite((due, frame));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        finally { queue.TryComplete(); }
    }

    private async Task WriteAsync(TcpClient client, ChannelReader<(long Due, byte[] Frame)> queue)
    {
        try
        {
            var stream = client.GetStream();
            await foreach (var (due, frame) in queue.ReadAllAsync(lifetime.Token))
            {
                long wait = due - Stopwatch.GetTimestamp();
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency), lifetime.Token);
                await stream.WriteAsync(frame, lifetime.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        await accepting.WaitAsync(TimeSpan.FromSeconds(3));
        lock (tasks) foreach (var socket in sockets) socket.Dispose();
        Task[] pending;
        lock (tasks) pending = tasks.ToArray();
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        lifetime.Dispose();
    }
}

// One survivor-bound WorldState, with everything a mutation needs to be "contract aware" from the QA side.
internal sealed class SurvCtx
{
    public int Conn, RecipientId;
    public S2C_WorldState World = null!;
    public uint Tick;
    public PlayerState Truth;            // the killer's own entry at this tick (from the killer stream)
    public PlayerState? Prev;            // ... at Tick - 1
    public PlayerState Self;
    public int SelfIndex = -1;
    public int EntryIndex = -1;          // index of the killer entry already in the frame (the fake's), or -1
    public RefClass Ref;                 // QA reference line of sight from Self to Truth
    public double Margin;
    public uint SentTick;                // the Tick the frame is sent with (mutations may change it)
    public readonly List<byte[]> After = new();
    public bool Eligible => Self.Status is (byte)PlayerStatus.Normal or (byte)PlayerStatus.Lagging;

    public PlayerState LegalEntry => new() { Id = 1, Role = (byte)Role.Killer, Status = Truth.Status, X = Truth.X, Z = Truth.Z, Yaw = Truth.Yaw };
}

// Tracks the killer's own stream (connection 0) and which survivor ticks carried the entry, so a rewrite of a survivor frame
// can be decided against the killer's true position at the same tick.
internal sealed class LosTap
{
    private readonly ConcurrentDictionary<uint, PlayerState> killer = new();
    private readonly ConcurrentDictionary<int, uint> maxTick = new();
    private readonly ConcurrentDictionary<(int Conn, uint Tick), bool> entryTicks = new();
    private readonly ConcurrentDictionary<int, int> ids = new();

    public int KillerId { get; init; } = 1;

    public void Observe(int conn, IPacket? packet)
    {
        if (packet is S2C_JoinResult join) ids[conn] = join.PlayerId;
        if (packet is not S2C_WorldState world) return;
        if (conn == 0)
        {
            foreach (var p in world.Players)
                if (p.Id == KillerId && p.Role == (byte)Role.Killer) killer[world.Tick] = p;
        }
        maxTick.AddOrUpdate(conn, world.Tick, (_, old) => Math.Max(old, world.Tick));
    }

    public void NoteEntry(int conn, uint tick, bool had) => entryTicks[(conn, tick)] = had;

    public int RecipientId(int conn) => ids.TryGetValue(conn, out int id) ? id : -1;

    public bool WaitKiller(uint tick, out PlayerState truth, int ms = 500)
    {
        var clock = Stopwatch.StartNew();
        while (!killer.TryGetValue(tick, out truth))
        {
            if (clock.ElapsedMilliseconds > ms) return false;
            Thread.Sleep(1);
        }
        return true;
    }

    public PlayerState? KillerAt(uint tick) => killer.TryGetValue(tick, out var p) ? p : null;

    // Did the survivor on `conn` get the entry at `tick`? Waits briefly for that connection's frame to pass the proxy.
    public bool? EntryAt(int conn, uint tick, int ms = 150)
    {
        var clock = Stopwatch.StartNew();
        while (!entryTicks.ContainsKey((conn, tick)))
        {
            if (clock.ElapsedMilliseconds > ms) return null;
            Thread.Sleep(1);
        }
        return entryTicks[(conn, tick)];
    }
}

// Builds the rewrite delegate for a mutation over survivor-bound WorldStates (connection > 0).
internal sealed class SurvivorTap
{
    private readonly LosTap tap = new();
    private readonly Func<SurvCtx, bool> mutate;
    private int applied, seen;
    public readonly ConcurrentBag<string> Log = new();

    // Evidence tally with the QA reference, for the count cross-check: key = outcome name.
    public readonly ConcurrentDictionary<string, int> Tally = new();
    public int Applied => Volatile.Read(ref applied);
    public int Seen => Volatile.Read(ref seen);
    public LosTap Tap => tap;

    public SurvivorTap(Func<SurvCtx, bool>? mutate = null) => this.mutate = mutate ?? (_ => false);

    public IEnumerable<byte[]> Rewrite(int conn, IPacket? packet, byte[] raw)
    {
        tap.Observe(conn, packet);
        if (conn == 0 || packet is not S2C_WorldState world) return new[] { raw };
        Interlocked.Increment(ref seen);
        int recipient = tap.RecipientId(conn);
        if (!tap.WaitKiller(world.Tick, out var truth))
        {
            tap.NoteEntry(conn, world.Tick, world.Players.Exists(p => p.Id == tap.KillerId || p.Role == (byte)Role.Killer));
            return new[] { raw };
        }
        var ctx = new SurvCtx { Conn = conn, RecipientId = recipient, World = world, Tick = world.Tick, SentTick = world.Tick, Truth = truth, Prev = tap.KillerAt(world.Tick - 1) };
        for (int i = 0; i < world.Players.Count; i++)
        {
            var p = world.Players[i];
            if (p.Id == recipient) { ctx.Self = p; ctx.SelfIndex = i; }
            else if (p.Id == tap.KillerId || p.Role == (byte)Role.Killer) ctx.EntryIndex = i;
        }
        if (ctx.SelfIndex >= 0)
        {
            ctx.Margin = RefLos.Margin(ctx.Self.X, ctx.Self.Z, truth.X, truth.Z, exactGrid: false);
            ctx.Ref = ctx.Margin > RefLos.Band ? RefClass.Clear : ctx.Margin < -RefLos.Band ? RefClass.Blocked : RefClass.Ambiguous;
            // Tally from the fake's own frame (before mutation) with the QA reference.
            string key = !ctx.Eligible ? "spectator" : (ctx.EntryIndex >= 0 ? (ctx.Ref == RefClass.Ambiguous ? "ambiguous" : "visible") :
                ctx.Ref == RefClass.Blocked ? "blocked-absent" : ctx.Ref == RefClass.Clear ? "clear-absent" : "ambiguous");
            Tally.AddOrUpdate(key, 1, (_, n) => n + 1);
        }
        bool changed = ctx.SelfIndex >= 0 && mutate(ctx);
        tap.NoteEntry(conn, world.Tick, world.Players.Exists(p => p.Id == tap.KillerId || p.Role == (byte)Role.Killer));
        if (!changed) return new[] { raw };
        Interlocked.Increment(ref applied);
        Log.Add($"conn {conn} tick {world.Tick} ref={ctx.Ref} margin={ctx.Margin:F2}");
        world.Tick = ctx.SentTick;
        var frames = new List<byte[]> { PacketCodec.Encode(world) };
        frames.AddRange(ctx.After);
        return frames;
    }

    public static byte[] Unknown(ushort id, int payloadLength, byte fill, Action<byte[]>? fillPayload = null)
    {
        var payload = new byte[payloadLength];
        Array.Fill(payload, fill);
        fillPayload?.Invoke(payload);
        var frame = new byte[6 + payloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 2 + payloadLength);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), id);
        payload.CopyTo(frame, 6);
        return frame;
    }
}
