using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests;

// A test double only: product Harness has no listener, simulated server, or Server.Core reference.
internal sealed class PingServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(15));
    private readonly Task serving;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int Pings { get; private set; }

    public PingServer(int? dropIndex = null, bool silent = false, bool malformed = false, bool duplicateLast = false,
        bool unknownFrames = false, bool trailingByte = false)
    {
        listener.Start();
        serving = ServeAsync(dropIndex, silent, malformed, duplicateLast, unknownFrames, trailingByte);
    }

    private async Task ServeAsync(int? dropIndex, bool silent, bool malformed, bool duplicateLast, bool unknownFrames, bool trailingByte)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
            client.NoDelay = true;
            using var stream = client.GetStream();
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            while (true)
            {
                int count = await stream.ReadAsync(buffer, lifetime.Token);
                if (count == 0) return;
                assembler.Append(buffer.AsSpan(0, count));
                while (assembler.TryRead(out ushort id, out var payload) == AssembleResult.Packet)
                {
                    var packet = PacketCodec.Decode(id, payload.Span);
                    if (packet is not C2S_Ping ping) continue;
                    int index = Pings++;
                    if (silent || index == dropIndex) continue;
                    if (malformed)
                    {
                        var invalid = new byte[4];
                        BinaryPrimitives.WriteInt32LittleEndian(invalid, -1);
                        await stream.WriteAsync(invalid, lifetime.Token);
                        return;
                    }
                    byte[] pong = PacketCodec.Encode(new S2C_Pong { Timestamp = ping.Timestamp });
                    if (trailingByte)
                    {
                        // A KNOWN Id (S2C_Pong) whose payload has one trailing byte: a decode failure, never an unknown packet.
                        Array.Resize(ref pong, pong.Length + 1);
                        BinaryPrimitives.WriteInt32LittleEndian(pong, pong.Length - 4);
                    }
                    // Unknown-Id frames (0x7E, and 0x10 = a future v0.3 Id) interleaved before every pong, fragmented the same way.
                    if (unknownFrames) pong = LobbyServer.UnknownFrame(0x7E, 12, 0x7E).Concat(LobbyServer.UnknownFrame(0x10, 16, 0x10)).Concat(pong).ToArray();
                    // Deliberately fragment responses too, exercising the harness assembler.
                    await stream.WriteAsync(pong.AsMemory(0, 3), lifetime.Token);
                    await stream.WriteAsync(pong.AsMemory(3), lifetime.Token);
                    if (duplicateLast && index == 9999) await stream.WriteAsync(pong, lifetime.Token);
                }
            }
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
