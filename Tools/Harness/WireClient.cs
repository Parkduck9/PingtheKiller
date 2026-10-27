using System.Buffers.Binary;
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

// Exactly one consumer per client. The receive loop timestamps and preserves every frame,
// even while a test is sending, delaying, or consuming another connection's packets.
public sealed class WireClient : IAsyncDisposable
{
    private readonly TcpClient tcp;
    private readonly NetworkStream stream;
    private readonly TimeSpan timeout;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<ReceivedFrame> inbox = Channel.CreateBounded<ReceivedFrame>(32768);
    private readonly Task receiving;
    private readonly SemaphoreSlim sending = new(1, 1);
    private int disposed;
    private int serverDisconnected;
    public bool ServerDisconnected => Volatile.Read(ref serverDisconnected) != 0;

    private WireClient(TcpClient tcp, TimeSpan timeout)
    {
        this.tcp = tcp;
        stream = tcp.GetStream();
        this.timeout = timeout;
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
            return new WireClient(tcp, options.Timeout);
        }
        catch { tcp.Dispose(); throw; }
    }

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
        catch (ChannelClosedException ex) { throw new IOException("Server disconnected or sent an invalid frame.", ex.InnerException ?? ex); }
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
                    var frame = new ReceivedFrame(PacketCodec.Decode(id, payload.Span), raw, receivedAt);
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
