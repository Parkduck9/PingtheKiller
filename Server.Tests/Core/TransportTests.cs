using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class TransportTests
{
    [Fact]
    public void OfflineFactoryNeverCallsSocketFactoryAndMemoryConnectionHasNoSocketFields()
    {
        var options = new ServerOptions { IsOffline = true };
        using var listener = options.CreateTransport(() => throw new InvalidOperationException("Socket factory called"));
        var memory = Assert.IsType<InMemoryTransport>(listener);
        using var game = new GameServer(options);
        memory.ConnectionAccepted += game.AddConnection;
        listener.Start();
        using var connection = memory.Connect();
        // Structural check complements the throwing online factory: neither object
        // owns a Socket/TcpListener/TcpClient, including inherited private fields.
        foreach (var type in new[] { listener.GetType(), connection.GetType() })
            for (Type? current = type; current != null; current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Assert.False(field.FieldType.Namespace?.StartsWith("System.Net.Sockets", StringComparison.Ordinal) == true);
        connection.Send(PacketCodec.Encode(new C2S_Join { Nickname = "Offline" }));
        game.Tick(0.05f);
        var received = new List<IPacket>();
        while (connection.TryReceive(out var packet)) received.Add(packet);
        Assert.Single(received.OfType<S2C_JoinResult>());
    }

    [Fact]
    public void MemoryUsesRealAssemblerForSplitCoalescedAndMalformedFrames()
    {
        using var f = new CoreFixture();
        var peer = f.Connect();
        byte[] join = PacketCodec.Encode(new C2S_Join { Nickname = "Split" });
        foreach (byte b in join) peer.Connection.Send(new[] { b });
        f.Tick();
        Assert.Equal(0, peer.Last<S2C_JoinResult>().Result);
        byte[] ping1 = PacketCodec.Encode(new C2S_Ping { Timestamp = 123 });
        byte[] ping2 = PacketCodec.Encode(new C2S_Ping { Timestamp = 456 });
        peer.Connection.Send(ping1.Concat(ping2).ToArray());
        peer.Drain();
        Assert.Equal(new long[] { 123, 456 }, peer.Packets.OfType<S2C_Pong>().Select(p => p.Timestamp));
        peer.Connection.Send(new byte[] { 255, 255, 255, 255 });
        Assert.True(peer.Connection.IsClosed);
        Assert.Equal(0, f.Join().Last<S2C_JoinResult>().Result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16385)]
    public async Task TcpOneByteJoinAndInvalidLengthOnlyCloseOffender(int invalidLength)
    {
        using var game = new GameServer();
        using var listener = new TcpTransport(0, IPAddress.Loopback);
        listener.ConnectionAccepted += game.AddConnection;
        listener.Start();
        game.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var good = new TcpClient();
        using var bad = new TcpClient();
        await good.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
        await bad.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
        byte[] frame = PacketCodec.Encode(new C2S_Join { Nickname = "TCP" });
        foreach (byte b in frame) await good.GetStream().WriteAsync(new[] { b }, timeout.Token);
        var join = await Receive<S2C_JoinResult>(good.GetStream(), timeout.Token);
        Assert.Equal(0, join.Result);
        Assert.NotEqual(0UL, join.ReconnectToken);
        byte[] invalid = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(invalid, invalidLength);
        await bad.GetStream().WriteAsync(invalid, timeout.Token);
        Assert.Equal(0, await bad.GetStream().ReadAsync(new byte[1], timeout.Token));
        await good.GetStream().WriteAsync(PacketCodec.Encode(new C2S_Ping { Timestamp = 789 }), timeout.Token);
        Assert.Equal(789, (await Receive<S2C_Pong>(good.GetStream(), timeout.Token)).Timestamp);
        listener.Stop();
        game.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TcpUnknownPacketAndTruncatedPayloadAreIsolated(bool knownId)
    {
        using var game = new GameServer();
        using var listener = new TcpTransport(0, IPAddress.Loopback);
        listener.ConnectionAccepted += game.AddConnection;
        listener.Start();
        game.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var bad = new TcpClient();
        await bad.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
        byte[] frame = new byte[6];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), knownId ? (ushort)PacketId.C2S_Join : ushort.MaxValue);
        await bad.GetStream().WriteAsync(frame, timeout.Token);
        Assert.Equal(0, await bad.GetStream().ReadAsync(new byte[1], timeout.Token));
        using var good = new TcpClient();
        await good.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
        await good.GetStream().WriteAsync(PacketCodec.Encode(new C2S_Join()), timeout.Token);
        Assert.Equal(0, (await Receive<S2C_JoinResult>(good.GetStream(), timeout.Token)).Result);
    }

    [Fact]
    public async Task TcpQueueCapDisconnectsSlowClientWithoutBlocking()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var raw = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await raw.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var connection = new TcpConnection(await accept);
            // Hold the send loop dormant so the cap is deterministic, independent
            // of OS socket-buffer size. Every queued frame is a valid encoded ping.
            byte[] frame = PacketCodec.Encode(new C2S_Ping());
            for (int i = 0; i <= TcpConnection.MaxQueuedBytes / frame.Length; ++i) connection.Send(frame);
            Assert.True(connection.IsClosed);
            Assert.InRange(connection.QueuedBytes, 0, TcpConnection.MaxQueuedBytes);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void ThreadedServerRejectsConcurrentManualTicksAndStopsCleanly()
    {
        using var game = new GameServer();
        game.Start();
        Assert.Throws<InvalidOperationException>(() => game.Tick(0.05f));
        game.Stop();
        game.Stop();
        game.Tick(0.05f);
    }

    private static async Task<T> Receive<T>(NetworkStream stream, CancellationToken cancellationToken) where T : IPacket
    {
        while (true)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, cancellationToken);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            Assert.InRange(length, 2, 16384);
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, cancellationToken);
            var packet = PacketCodec.Decode(BinaryPrimitives.ReadUInt16LittleEndian(body), body.AsSpan(2));
            if (packet is T result) return result;
        }
    }
}
