using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

public sealed record ReceivedFrame(IPacket Packet, byte[] Raw, long ReceivedAt)
{
    public static double SecondsBetween(long earlier, long later) => (later - earlier) / (double)Stopwatch.Frequency;
}

// A well-formed server frame whose PacketId is not a Shared PacketId value (e.g. a newer protocol's packet):
// skipped by the receive loop and never delivered as a packet, but kept raw for byte-level leak scans.
public sealed record UnknownFrame(ushort Id, byte[] Raw, long ReceivedAt);

// Skipped unknown-Id frames per PacketId across every WireClient of one harness run.
public sealed class UnknownPackets
{
    private readonly ConcurrentDictionary<ushort, int> counts = new();
    public void Add(ushort id) => counts.AddOrUpdate(id, 1, (_, n) => n + 1);
    public IReadOnlyDictionary<ushort, int> Snapshot() => new Dictionary<ushort, int>(counts);

    // Evidence for the frames skipped between two snapshots, or null when there were none.
    public static string? Describe(IReadOnlyDictionary<ushort, int> before, IReadOnlyDictionary<ushort, int> after)
    {
        var parts = after.OrderBy(p => p.Key).Select(p => (Id: p.Key, Count: p.Value - before.GetValueOrDefault(p.Key)))
            .Where(p => p.Count > 0).Select(p => $"id=0x{p.Id:X2} ×{p.Count}").ToList();
        return parts.Count == 0 ? null : $"skipped unknown packets: {string.Join(", ", parts)}";
    }
}

// Exactly one consumer per client. The receive loop timestamps and preserves every frame,
// even while a test is sending, delaying, or consuming another connection's packets.
public sealed class WireClient : IAsyncDisposable
{
    public const int MaxKeptUnknownFrames = 32768;
    // U3d a3: the last WorldStates received, whoever consumed them, so a later check can join ticks an earlier one read (3.2 s).
    public const int RetainedWorlds = 64;
    private readonly Queue<ReceivedFrame> recentWorlds = new();
    private long latestWorldTick = -1;
    private readonly TcpClient tcp;
    private readonly NetworkStream stream;
    private readonly TimeSpan timeout;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<ReceivedFrame> inbox = Channel.CreateBounded<ReceivedFrame>(32768);
    private readonly Task receiving;
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly UnknownPackets unknownTally;
    private readonly Queue<UnknownFrame> unknown = new();
    private readonly Dictionary<ushort, int> unknownCounts = new();
    private long unknownDropped;
    private int disposed;
    private int serverDisconnected;
    public bool ServerDisconnected => Volatile.Read(ref serverDisconnected) != 0;

    private WireClient(TcpClient tcp, TimeSpan timeout, UnknownPackets unknownTally)
    {
        this.tcp = tcp;
        stream = tcp.GetStream();
        this.timeout = timeout;
        this.unknownTally = unknownTally;
        receiving = ReceiveAsync();
    }

    public static async Task<WireClient> ConnectAsync(Options options, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient { NoDelay = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        try
        {
            await tcp.ConnectAsync(options.Host, options.Port, deadline.Token);
            return new WireClient(tcp, options.Timeout, options.Unknown);
        }
        catch { tcp.Dispose(); throw; }
    }

    // A known Id: a Shared PacketId value, all of which PacketCodec.Decode handles. Anything else is skipped.
    public static bool IsKnownId(ushort id) => Enum.IsDefined((PacketId)id);

    public Task SendAsync(IPacket packet, CancellationToken ct) => SendRawAsync(PacketCodec.Encode(packet), ct);

    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        deadline.CancelAfter(timeout);
        await sending.WaitAsync(deadline.Token);
        try { await stream.WriteAsync(bytes, deadline.Token); }
        finally { sending.Release(); }
    }

    public async Task<ReceivedFrame> ReadAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try { return await inbox.Reader.ReadAsync(deadline.Token); }
        catch (ChannelClosedException ex)
        {
            var cause = ex.InnerException ?? ex;
            throw new IOException(cause is PacketFormatException
                ? $"Server disconnected or sent an invalid frame: {cause.Message}" : "Server disconnected or sent an invalid frame.", cause);
        }
    }

    public async Task<ReceivedFrame> ReadAsync<T>(CancellationToken ct, Func<T, bool>? predicate = null) where T : IPacket
    {
        // A stream of unrelated packets must not postpone the expected packet's deadline.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (true)
        {
            var frame = await ReadAsync(deadline.Token);
            if (frame.Packet is T packet && (predicate == null || predicate(packet))) return frame;
        }
    }

    public async Task ExpectDisconnectAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            while (await inbox.Reader.WaitToReadAsync(deadline.Token))
                while (inbox.Reader.TryRead(out _)) deadline.Token.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is IOException or SocketException) { }
    }

    // Tick of the newest WorldState the receive loop has seen (consumed or not), or -1.
    public long LatestWorldTick => Interlocked.Read(ref latestWorldTick);

    // The last RetainedWorlds WorldStates received, oldest first, whether or not a reader consumed them.
    public List<ReceivedFrame> RecentWorlds()
    {
        lock (recentWorlds) return recentWorlds.ToList();
    }

    // Skipped unknown-Id frames per PacketId on this connection.
    public IReadOnlyDictionary<ushort, int> UnknownCounts()
    {
        lock (unknown) return new Dictionary<ushort, int>(unknownCounts);
    }

    // Every skipped unknown-Id frame received at or after `since`. Fails if the bounded store dropped any of them.
    public List<UnknownFrame> UnknownFramesSince(long since)
    {
        lock (unknown)
        {
            Check.Require(unknownDropped == 0 || (unknown.Count > 0 && unknown.Peek().ReceivedAt < since),
                $"More than {MaxKeptUnknownFrames} unknown-Id frames on one connection; dropped frames cannot be scanned.");
            return unknown.Where(f => f.ReceivedAt >= since).ToList();
        }
    }

    private void KeepUnknown(UnknownFrame frame)
    {
        unknownTally.Add(frame.Id);
        lock (unknown)
        {
            unknownCounts[frame.Id] = unknownCounts.GetValueOrDefault(frame.Id) + 1;
            unknown.Enqueue(frame);
            if (unknown.Count > MaxKeptUnknownFrames)
            {
                unknown.Dequeue();
                unknownDropped++;
            }
        }
    }

    private async Task ReceiveAsync()
    {
        Exception? failure = null;
        try
        {
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            while (true)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(timeout);
                int count = await stream.ReadAsync(buffer, deadline.Token);
                if (count == 0)
                {
                    Volatile.Write(ref serverDisconnected, 1);
                    break;
                }
                long receivedAt = Stopwatch.GetTimestamp();
                assembler.Append(buffer.AsSpan(0, count));
                while (true)
                {
                    var result = assembler.TryRead(out ushort id, out var payload);
                    if (result == AssembleResult.NeedMoreData) break;
                    if (result == AssembleResult.Malformed) throw new PacketFormatException("Invalid frame length from server.");
                    var raw = new byte[Protocol.HeaderSize + Protocol.IdSize + payload.Length];
                    BinaryPrimitives.WriteInt32LittleEndian(raw, Protocol.IdSize + payload.Length);
                    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(Protocol.HeaderSize), id);
                    payload.Span.CopyTo(raw.AsSpan(Protocol.HeaderSize + Protocol.IdSize));
                    // The length was valid (checked above). An unknown Id is skipped, counted and kept raw; a known Id
                    // that fails to decode (bad payload, trailing bytes) still faults the stream, as does a bad length.
                    if (!IsKnownId(id))
                    {
                        KeepUnknown(new UnknownFrame(id, raw, receivedAt));
                        continue;
                    }
                    var frame = new ReceivedFrame(PacketCodec.Decode(id, payload.Span), raw, receivedAt);
                    if (frame.Packet is S2C_WorldState world)
                    {
                        lock (recentWorlds)
                        {
                            recentWorlds.Enqueue(frame);
                            if (recentWorlds.Count > RetainedWorlds) recentWorlds.Dequeue();
                        }
                        Interlocked.Exchange(ref latestWorldTick, world.Tick);
                    }
                    if (!inbox.Writer.TryWrite(frame)) throw new CheckFailure("Harness receive queue exceeded 32768 frames.");
                }
            }
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested && ex is IOException or SocketException)
                Volatile.Write(ref serverDisconnected, 1);
            failure = ex;
        }
        finally { inbox.Writer.TryComplete(failure); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        tcp.Dispose();
        await receiving.WaitAsync(timeout);
        lifetime.Dispose();
        sending.Dispose();
    }
}
