using System.Net;
using System.Net.Sockets;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class WireClientTests
{
    [Fact]
    public async Task TypedReadsSkipKnownProbesAndPreserveExpectedPongValues()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var options = new Options { Port = ((IPEndPoint)listener.LocalEndpoint).Port };
            await using var client = await WireClient.ConnectAsync(options, deadline.Token);
            using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
            foreach (long timestamp in new long[] { 42, 43 })
            {
                byte[] bytes = PacketCodec.Encode(new S2C_RttProbe { Nonce = (uint)timestamp })
                    .Concat(PacketCodec.Encode(new S2C_Pong { Timestamp = timestamp })).ToArray();
                await peer.GetStream().WriteAsync(bytes, deadline.Token);
                var pong = (S2C_Pong)(await client.ReadAsync<S2C_Pong>(deadline.Token)).Packet;
                Assert.Equal(timestamp, pong.Timestamp);
            }
            Assert.Empty(client.UnknownCounts());
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task ADecoderFailureIsNotAcceptedAsAnAbuserDisconnect()
    {
        await using var server = new PingServer(malformed: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var client = await WireClient.ConnectAsync(new Options { Port = server.Port }, deadline.Token);
        await client.SendAsync(new C2S_Ping { Timestamp = 1 }, deadline.Token);
        await Assert.ThrowsAsync<PacketFormatException>(() => client.ExpectDisconnectAsync(deadline.Token));
    }

    [Fact]
    public async Task AnOpenSilentSocketCannotPassTheDisconnectCheck()
    {
        await using var server = new PingServer(silent: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var client = await WireClient.ConnectAsync(new Options { Port = server.Port,
            Timeout = TimeSpan.FromMilliseconds(250) }, deadline.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExpectDisconnectAsync(deadline.Token));
    }
}
