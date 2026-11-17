using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class RttProbeTests
{
    private const float Dt = 1f / GameConstants.TickRate;

    private static List<T> Take<T>(Peer peer) where T : IPacket
    {
        peer.Drain();
        var packets = peer.Packets.OfType<T>().ToList();
        peer.Packets.RemoveAll(p => p is T);
        return packets;
    }

    private static S2C_RttProbe Probe(Peer peer) => Assert.Single(Take<S2C_RttProbe>(peer));

    private static void Echo(CoreFixture f, Peer peer, uint nonce, double receivedAt, double? handledAt = null)
    {
        f.Clock.Now = receivedAt;
        peer.Send(new C2S_RttEcho { Nonce = nonce });
        f.Clock.Now = handledAt ?? receivedAt;
        f.Tick();
        Assert.False(peer.Connection.IsClosed);
    }

    [Fact]
    public void FirstTickProbesAJoinedHumanAndReceiveStampExcludesTheInputQueueWait()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join();
        var first = Probe(peer);
        Assert.NotEqual(0U, first.Nonce);
        Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(peer.Id));
        Echo(f, peer, first.Nonce, .080, 1.10);
        // A valid echo queued before timeout still counts even when the game loop stalls beyond it.
        Assert.Equal(.080, f.Server.RttStateForTests(peer.Id).Seconds, 9);
        var second = Probe(peer); // Original 1.0 s schedule, not one second after handling the echo.
        Echo(f, peer, second.Nonce, 1.260, 1.4);
        var state = f.Server.RttStateForTests(peer.Id);
        Assert.Equal(.090, state.Seconds, 9);
        Assert.Equal(2, state.Samples);
        Assert.False(state.Outstanding);
    }

    [Fact]
    public void ProbeStampIsReadWhenSendingRatherThanAtTheTickStart()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Connect();
        peer.Send(new C2S_Join { Nickname = "Clock" });
        f.Clock.Then = .04;
        f.Tick();
        Echo(f, peer, Probe(peer).Nonce, .12);
        Assert.Equal(.08, f.Server.RttStateForTests(peer.Id).Seconds, 9);
    }

    [Fact]
    public void SamplesAndTheSmoothedEstimateAreClamped()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join();
        Echo(f, peer, Probe(peer).Nonce, .6);
        Assert.Equal(GameServer.MaxRttSeconds, f.Server.RttStateForTests(peer.Id).Seconds);
        f.Clock.Now = 1;
        f.Tick();
        Echo(f, peer, Probe(peer).Nonce, 1.6);
        Assert.Equal(GameServer.MaxRttSeconds, f.Server.RttStateForTests(peer.Id).Seconds);
        f.Clock.Now = 2;
        f.Tick();
        // A backwards injected clock clamps to zero, never a negative compensation.
        Echo(f, peer, Probe(peer).Nonce, 1.9);
        Assert.Equal(.25 - .25 / 8, f.Server.RttStateForTests(peer.Id).Seconds, 9);
    }

    [Fact]
    public void UnsolicitedGuessedWrongDuplicateAndOlderEchoesCannotCreateOrShrinkASample()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Connect();
        peer.Send(new C2S_Join { Nickname = "Guesser" });
        peer.Send(new C2S_RttEcho { Nonce = 123 }); // Joined, but no probe has left yet.
        f.Tick();
        var first = Probe(peer);
        Assert.Equal(0, f.Server.RttStateForTests(peer.Id).Samples);
        Echo(f, peer, first.Nonce ^ 0x80000000U, .01);
        Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(peer.Id));
        Echo(f, peer, first.Nonce, .08);
        var accepted = f.Server.RttStateForTests(peer.Id);
        Echo(f, peer, first.Nonce, .09); // Duplicate, with no outstanding probe.
        Assert.Equal(accepted, f.Server.RttStateForTests(peer.Id));
        f.Clock.Now = 1;
        f.Tick();
        var second = Probe(peer);
        Assert.NotEqual(0U, second.Nonce);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(unchecked(first.Nonce + 1), second.Nonce);
        Assert.NotEqual(unchecked(second.Nonce + 1), first.Nonce);
        Echo(f, peer, unchecked(first.Nonce + 1), 1.01);
        Echo(f, peer, first.Nonce, 1.02);
        Assert.Equal((.08, 1, true), f.Server.RttStateForTests(peer.Id));
        Echo(f, peer, second.Nonce, 1.16);
        Assert.Equal(.09, f.Server.RttStateForTests(peer.Id).Seconds, 9);
        Assert.Equal(2, f.Server.RttStateForTests(peer.Id).Samples);
    }

    [Fact]
    public void NeverEchoingTimesOutWithoutFloodingAndExpiredEchoesAreIgnored()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join();
        var previous = Probe(peer);
        for (int round = 0; round < 6; round++)
        {
            for (int step = 1; step < 20; step++)
            {
                f.Clock.Now = round + step / 20.0;
                f.Tick();
                Assert.Empty(Take<S2C_RttProbe>(peer));
                Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(peer.Id));
            }
            f.Clock.Now = round + 1;
            f.Tick();
            var next = Probe(peer); // Exactly one replacement at timeout.
            Assert.NotEqual(previous.Nonce, next.Nonce);
            Echo(f, peer, previous.Nonce, round + 1.001);
            Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(peer.Id));
            previous = next;
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1)]
    public void AnEchoReceivedAtOrAfterTimeoutCannotProduceASample(double arrival)
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join();
        Echo(f, peer, Probe(peer).Nonce, arrival);
        Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(peer.Id));
        Probe(peer);
    }

    [Fact]
    public void AnsweringThenStoppingKeepsTheLastEstimate()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join();
        Echo(f, peer, Probe(peer).Nonce, .04);
        for (int i = 1; i <= 5; i++)
        {
            f.Clock.Now = i;
            f.Tick();
            Probe(peer);
            Assert.Equal((.04, 1, true), f.Server.RttStateForTests(peer.Id));
        }
    }

    [Fact]
    public void UnjoinedConnectionsBotsAndOfflineHumansAreNeverProbed()
    {
        using (var f = new CoreFixture(offline: false, bots: 2))
        {
            var stranger = f.Connect();
            stranger.Send(new C2S_RttEcho { Nonce = 1 });
            f.Tick();
            Assert.Empty(Take<S2C_RttProbe>(stranger));
            var human = f.Join();
            Probe(human);
            var bots = human.Last<S2C_LobbyState>().Slots.Where(s => s.IsBot != 0).ToArray();
            Assert.Equal(2, bots.Length);
            foreach (var bot in bots)
            {
                var state = f.Server.RttStateForTests(bot.PlayerId);
                Assert.False(state.Outstanding);
                Assert.Equal(0, state.Samples);
            }
        }
        using (var f = new CoreFixture())
        {
            var peer = f.Join();
            for (int i = 0; i < 4; i++)
            {
                Echo(f, peer, (uint)i, i);
                Assert.Empty(Take<S2C_RttProbe>(peer));
                Assert.Equal((0.0, 0, false), f.Server.RttStateForTests(peer.Id));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisconnectOrLiveReconnectResetsEstimateOutstandingProbeAndSchedule(bool liveReconnect)
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join();
        var join = peer.Last<S2C_JoinResult>();
        Echo(f, peer, Probe(peer).Nonce, .08);
        f.Clock.Now = 1;
        f.Tick();
        var old = Probe(peer);
        if (!liveReconnect)
        {
            peer.Dispose();
            f.Tick();
            Assert.Equal((0.0, 0, false), f.Server.RttStateForTests(join.PlayerId));
        }
        f.Clock.Now = 1.1;
        var restored = f.Join(token: join.ReconnectToken);
        Assert.Equal(join.PlayerId, restored.Id);
        var fresh = Probe(restored); // Does not wait for the old schedule at 2.0.
        Assert.NotEqual(old.Nonce, fresh.Nonce);
        Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(restored.Id));
        Echo(f, restored, old.Nonce, 1.11);
        Assert.Equal(0, f.Server.RttStateForTests(restored.Id).Samples);
        Echo(f, restored, fresh.Nonce, 1.14);
        Assert.Equal(.04, f.Server.RttStateForTests(restored.Id).Seconds, 9);
        Assert.Equal(1, f.Server.RttStateForTests(restored.Id).Samples);
    }

    [Fact]
    public void LobbyEstimateAndOutstandingProbeSurviveStartingAMatchAndPingNeverSetsRtt()
    {
        using var f = new CoreFixture(offline: false);
        var host = f.Join();
        var first = Probe(host);
        host.Send(new C2S_Ping { Timestamp = long.MinValue });
        Assert.Equal(long.MinValue, host.Last<S2C_Pong>().Timestamp);
        Assert.Equal(0, f.Server.RttStateForTests(host.Id).Samples);
        Echo(f, host, first.Nonce, .06);
        f.Join("Guest");
        host.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal(.06, f.Server.RttStateForTests(host.Id).Seconds, 9);
        f.Clock.Now = 1;
        f.Tick();
        var inMatch = Probe(host);
        Assert.True(f.Server.RttStateForTests(host.Id).Outstanding);
        Echo(f, host, inMatch.Nonce, 1.06);
        Assert.Equal(2, f.Server.RttStateForTests(host.Id).Samples);
    }

    [Fact]
    public void NonceStreamUsesSeedHookFourAndIsIndependentOfGameplayDraws()
    {
        using var idle = new CoreFixture(offline: false);
        using var busy = new CoreFixture(offline: false);
        var a = idle.Join();
        var b = busy.Join();
        var expected = PrivateRandom.FromSeed(1234, 4);
        b.Send(new C2S_SetBots { Count = 7 });
        b.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        b.Send(new C2S_StartMatch());
        busy.Tick(); // Consumes gameplay streams, preserving the lobby's pending RTT probe.
        uint previous = 0;
        for (int i = 0; i < 5; i++)
        {
            if (i > 0)
            {
                idle.Clock.Now = busy.Clock.Now = i;
                idle.Tick();
                busy.Tick();
            }
            uint nonce = Probe(a).Nonce;
            Assert.Equal(nonce, Probe(b).Nonce);
            uint draw;
            do draw = (uint)expected.NextUInt64();
            while (draw == 0 || draw == previous || draw == unchecked(previous + 1) || draw == unchecked(previous - 1));
            Assert.Equal(draw, nonce);
            previous = nonce;
        }
    }

    // Uses real online packets and ticks; only the wall clock and positions are arranged by test hooks.
    // Retired 11/17 (R2): SymmetricSixtyMillisecondPathGetsGreatWithProbesAndGoodWithout,
    // DelayedEchoesShiftTheGreatWindowByAConstantWithoutWideningIt and their SyncScenario judged sync-check presses against
    // the probe's RTT. Packet routing replaced the sync check and is judged by key order, not by timing, so no rule reads the
    // RTT any more; the probe's own measurement, nonce and timeout rules stay covered above and below.

    [Fact]
    public async Task RealTcpLoopbackEchoesProduceASmallPositiveServerTimedRtt()
    {
        using var server = new GameServer();
        using var transport = new TcpTransport(0, IPAddress.Loopback);
        transport.ConnectionAccepted += server.AddConnection;
        transport.Start();
        server.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, transport.Port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(PacketCodec.Encode(new C2S_Join { Nickname = "RTT loopback" }), timeout.Token);
        int playerId = 0, echoes = 0;
        while (echoes < 3)
        {
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, timeout.Token);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            Assert.InRange(length, 2, 16384);
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, timeout.Token);
            var packet = PacketCodec.Decode(BinaryPrimitives.ReadUInt16LittleEndian(body), body.AsSpan(2));
            if (packet is S2C_JoinResult join) playerId = join.PlayerId;
            if (packet is not S2C_RttProbe probe) continue;
            await stream.WriteAsync(PacketCodec.Encode(new C2S_RttEcho { Nonce = probe.Nonce }), timeout.Token);
            ++echoes;
        }
        while (server.RttStateForTests(playerId).Samples < echoes) await Task.Delay(5, timeout.Token);
        var measured = server.RttStateForTests(playerId).Seconds;
        // Positive, server-timed and small. The bound is wall-clock on a loaded machine (a parallel build or Unity run once gave 0.057 s
        // against .05), so it is 0.25 s: still far below a stale or client-supplied timestamp, which would be seconds.
        Assert.True(measured > 0 && measured < .25, $"loopback RTT {measured:R} s");
    }
}
