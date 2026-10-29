using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests.QA.U3c;

// Test-only TCP man-in-the-middle in front of a fake server. Client-to-server bytes are copied untouched; every
// server-to-client frame goes through Rewrite(connectionIndex, packet or null for an unknown Id, raw frame) which returns
// the frames to forward. Connection 0 is the first client to connect (H6: the killer joins first).
internal sealed class QaProxy : IAsyncDisposable
{
    public delegate IEnumerable<byte[]> Rewrite(int connection, IPacket? packet, byte[] raw);

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(170));
    private readonly int upstreamPort;
    private readonly Rewrite rewrite;
    private readonly List<Task> tasks = new();
    private readonly List<TcpClient> sockets = new();
    private readonly Task accepting;
    private int connections;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public QaProxy(int upstreamPort, Rewrite rewrite)
    {
        this.upstreamPort = upstreamPort;
        this.rewrite = rewrite;
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
                lock (tasks)
                {
                    sockets.Add(client);
                    sockets.Add(upstream);
                    tasks.Add(CopyAsync(client, upstream));
                    tasks.Add(ForwardAsync(index, upstream, client));
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

    private async Task ForwardAsync(int index, TcpClient upstream, TcpClient client)
    {
        try
        {
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            var stream = client.GetStream();
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
                    foreach (var frame in rewrite(index, packet, raw))
                        await stream.WriteAsync(frame, lifetime.Token);
                }
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

// Test-only raw byte server for WireClient-level framing tests: accepts one client and runs the script.
internal sealed class QaRawServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(20));
    private readonly Task serving;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public QaRawServer(Func<NetworkStream, CancellationToken, Task> script)
    {
        listener.Start();
        serving = ServeAsync(script);
    }

    private async Task ServeAsync(Func<NetworkStream, CancellationToken, Task> script)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
            client.NoDelay = true;
            await script(client.GetStream(), lifetime.Token);
            await Task.Delay(300, lifetime.Token); // let the client drain before the socket closes
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        await serving.WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Dispose();
    }
}
