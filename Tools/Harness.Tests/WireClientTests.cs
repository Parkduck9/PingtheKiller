using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class WireClientTests
{
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
