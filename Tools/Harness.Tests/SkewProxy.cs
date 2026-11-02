using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace PingTheKiller.Harness.Tests;

// U3d a3: a TCP relay that delays server-to-client bytes per connection, order preserved, so a delay is a pure stream skew
// (a lagging client). Client-to-server bytes pass at once. Connection 0 is the first client to connect: the killer in H3 and H6.
internal sealed class SkewProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly int upstreamPort;
    private readonly Func<int, TimeSpan> delay;
    private readonly List<Task> tasks = new();
    private readonly List<TcpClient> sockets = new();
    private readonly Task accepting;
    private int connections;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public SkewProxy(int upstreamPort, Func<int, TimeSpan> delay)
    {
        this.upstreamPort = upstreamPort;
        this.delay = delay;
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
                var lag = delay(connections++);
                var queue = Channel.CreateUnbounded<(long Due, byte[] Bytes)>();
                lock (tasks)
                {
                    sockets.Add(client);
                    sockets.Add(upstream);
                    tasks.Add(CopyAsync(client, upstream));
                    tasks.Add(ReadUpstreamAsync(upstream, lag, queue.Writer));
                    tasks.Add(WriteDelayedAsync(client, queue.Reader));
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

    private async Task ReadUpstreamAsync(TcpClient upstream, TimeSpan lag, ChannelWriter<(long, byte[])> queue)
    {
        try
        {
            var buffer = new byte[8192];
            while (true)
            {
                int count = await upstream.GetStream().ReadAsync(buffer, lifetime.Token);
                if (count == 0) return;
                long due = Stopwatch.GetTimestamp() + (long)(lag.TotalSeconds * Stopwatch.Frequency);
                queue.TryWrite((due, buffer.AsSpan(0, count).ToArray()));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        finally { queue.TryComplete(); }
    }

    private async Task WriteDelayedAsync(TcpClient client, ChannelReader<(long Due, byte[] Bytes)> queue)
    {
        try
        {
            var stream = client.GetStream();
            await foreach (var (due, bytes) in queue.ReadAllAsync(lifetime.Token))
            {
                long wait = due - Stopwatch.GetTimestamp();
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency), lifetime.Token);
                await stream.WriteAsync(bytes, lifetime.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        finally { try { client.Client.Shutdown(SocketShutdown.Send); } catch (Exception) { } }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        try { await accepting.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        Task[] pending;
        lock (tasks)
        {
            foreach (var socket in sockets) socket.Dispose();
            pending = tasks.ToArray();
        }
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        lifetime.Dispose();
    }
}
