using System;
using System.Collections.Generic;
using System.Linq;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.Core;
using Xunit;

namespace PingTheKiller.Tests.QA.U6g;

public sealed class U6gRttProbeQaTests
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

    [Fact]
    [Trait("Category", "QaU6g")]
    public void PlayerACannotEchoPlayerBNonceCrossConnection()
    {
        using var f = new CoreFixture(offline: false);
        var playerA = f.Join("Alice");
        var playerB = f.Join("Bob");

        var probeA = Probe(playerA);
        var probeB = Probe(playerB);

        Assert.NotEqual(0U, probeA.Nonce);
        Assert.NotEqual(0U, probeB.Nonce);
        Assert.NotEqual(probeA.Nonce, probeB.Nonce);

        // Player B attempts to echo Player A's nonce over Player B's connection.
        f.Clock.Now = 0.050;
        playerB.Send(new C2S_RttEcho { Nonce = probeA.Nonce });
        f.Tick();

        // Player B should NOT produce a sample, and probe should still be outstanding.
        var stateB = f.Server.RttStateForTests(playerB.Id);
        Assert.Equal(0, stateB.Samples);
        Assert.Equal(0.0, stateB.Seconds);
        Assert.True(stateB.Outstanding);

        // Player A's probe must remain outstanding and unaffected.
        var stateA = f.Server.RttStateForTests(playerA.Id);
        Assert.Equal(0, stateA.Samples);
        Assert.Equal(0.0, stateA.Seconds);
        Assert.True(stateA.Outstanding);

        // Player A sends its own valid echo at 80 ms.
        f.Clock.Now = 0.080;
        playerA.Send(new C2S_RttEcho { Nonce = probeA.Nonce });
        f.Tick();

        stateA = f.Server.RttStateForTests(playerA.Id);
        Assert.Equal(1, stateA.Samples);
        Assert.Equal(0.080, stateA.Seconds, 9);
        Assert.False(stateA.Outstanding);

        // Player B sends its own valid echo at 120 ms.
        f.Clock.Now = 0.120;
        playerB.Send(new C2S_RttEcho { Nonce = probeB.Nonce });
        f.Tick();

        stateB = f.Server.RttStateForTests(playerB.Id);
        Assert.Equal(1, stateB.Samples);
        Assert.Equal(0.120, stateB.Seconds, 9);
        Assert.False(stateB.Outstanding);
    }

    [Fact]
    [Trait("Category", "QaU6g")]
    public void ReplayedOldNonceAfterReconnectIsIgnoredAndPendingProbeIsReset()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join("Reconnector");
        var joinResult = peer.Last<S2C_JoinResult>();
        var firstProbe = Probe(peer);

        // Answer first probe to establish a valid estimate.
        f.Clock.Now = 0.050;
        peer.Send(new C2S_RttEcho { Nonce = firstProbe.Nonce });
        f.Tick();
        Assert.Equal(1, f.Server.RttStateForTests(peer.Id).Samples);
        Assert.Equal(0.050, f.Server.RttStateForTests(peer.Id).Seconds, 9);

        // Advance clock to trigger second probe.
        f.Clock.Now = 1.0;
        f.Tick();
        var secondProbe = Probe(peer);
        Assert.True(f.Server.RttStateForTests(peer.Id).Outstanding);

        // Disconnect connection.
        peer.Dispose();
        f.Tick();
        Assert.Equal((0.0, 0, false), f.Server.RttStateForTests(joinResult.PlayerId));

        // Reconnect via token at clock 1.2.
        f.Clock.Now = 1.2;
        var restored = f.Join("Reconnector", token: joinResult.ReconnectToken);
        Assert.Equal(joinResult.PlayerId, restored.Id);

        // Reconnect must start fresh with a new probe on the next tick.
        var freshProbe = Probe(restored);
        Assert.NotEqual(firstProbe.Nonce, freshProbe.Nonce);
        Assert.NotEqual(secondProbe.Nonce, freshProbe.Nonce);
        Assert.Equal((0.0, 0, true), f.Server.RttStateForTests(restored.Id));

        // Adversarial replay: send firstProbe and secondProbe.
        f.Clock.Now = 1.22;
        restored.Send(new C2S_RttEcho { Nonce = firstProbe.Nonce });
        restored.Send(new C2S_RttEcho { Nonce = secondProbe.Nonce });
        f.Tick();

        // Replayed nonces must be silently ignored.
        var state = f.Server.RttStateForTests(restored.Id);
        Assert.Equal(0, state.Samples);
        Assert.Equal(0.0, state.Seconds);
        Assert.True(state.Outstanding);

        // Legitimate fresh probe echo accepted.
        f.Clock.Now = 1.26;
        restored.Send(new C2S_RttEcho { Nonce = freshProbe.Nonce });
        f.Tick();

        state = f.Server.RttStateForTests(restored.Id);
        Assert.Equal(1, state.Samples);
        Assert.Equal(0.060, state.Seconds, 9);
        Assert.False(state.Outstanding);
    }

    [Fact]
    [Trait("Category", "QaU6g")]
    public void EchoExactlyAtTimeoutEdgeIsRejectedWhereasJustBeforeTimeoutIsAccepted()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join("TimeoutEdge");
        var probe1 = Probe(peer);

        // Echo sent exactly at timeout (elapsed == 1.000000 s).
        f.Clock.Now = GameServer.RttProbeTimeoutSeconds; // 1.0 s
        peer.Send(new C2S_RttEcho { Nonce = probe1.Nonce });
        f.Tick();

        // At exactly 1.0 s, the echo is timed out (no sample produced).
        var state = f.Server.RttStateForTests(peer.Id);
        Assert.Equal(0, state.Samples);
        Assert.Equal(0.0, state.Seconds);

        // On the next scheduled tick, a fresh probe is issued because the prior probe timed out.
        var probe2 = Probe(peer);
        Assert.NotEqual(probe1.Nonce, probe2.Nonce);
        Assert.True(f.Server.RttStateForTests(peer.Id).Outstanding);

        // Echo probe2 just inside the timeout (elapsed == 0.999 s, within 1.0 s).
        // probe2 was sent at clock 1.0 s, so arrival is 1.999 s.
        f.Clock.Now = 1.999;
        peer.Send(new C2S_RttEcho { Nonce = probe2.Nonce });
        f.Tick();

        state = f.Server.RttStateForTests(peer.Id);
        Assert.Equal(1, state.Samples);
        // 0.999 s elapsed is clamped to MaxRttSeconds (0.25 s).
        Assert.Equal(GameServer.MaxRttSeconds, state.Seconds, 9);
        Assert.False(state.Outstanding);
    }

    [Fact]
    [Trait("Category", "QaU6g")]
    public void BurstOfInvalidEchoesPrecedingLegitimateEchoDoesNotCorruptSampleOrDropConnection()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join("BurstSender");
        var probe = Probe(peer);

        // Flood 100 invalid nonces.
        for (uint i = 1; i <= 100; i++)
        {
            uint wrongNonce = probe.Nonce + i;
            if (wrongNonce == probe.Nonce) wrongNonce = probe.Nonce ^ 0xFFFFFFFFU;
            peer.Send(new C2S_RttEcho { Nonce = wrongNonce });
        }

        // Send the legitimate echo at 45 ms.
        f.Clock.Now = 0.045;
        peer.Send(new C2S_RttEcho { Nonce = probe.Nonce });

        // Trailing duplicate / invalid echoes in the same burst.
        peer.Send(new C2S_RttEcho { Nonce = probe.Nonce });
        peer.Send(new C2S_RttEcho { Nonce = probe.Nonce + 999 });

        f.Tick();

        Assert.False(peer.Connection.IsClosed);
        var state = f.Server.RttStateForTests(peer.Id);
        Assert.Equal(1, state.Samples);
        Assert.Equal(0.045, state.Seconds, 9);
        Assert.False(state.Outstanding);
    }

    // Retired 11/17 (R2): LateTickWithNonzeroRttJudgesPressAccurately judged a sync-check press across a late tick with
    // the RTT subtracted. The sync check, its judge and the tick-lag clock mapping are gone (packet routing is judged by key
    // order); route input order and rate limits are covered in Server.Tests/Core/PacketRoutingTests.cs.

    [Fact]
    [Trait("Category", "QaU6g")]
    public void BackwardsClockArrivalClampsSampleToZeroWithoutUnderflow()
    {
        using var f = new CoreFixture(offline: false);
        var peer = f.Join("BackwardsClock");
        var probe = Probe(peer);

        // Probe was sent at clock 0.0.
        // Simulate jittered receive-thread stamp slightly before sent stamp (-10 ms).
        f.Clock.Now = -0.010;
        peer.Send(new C2S_RttEcho { Nonce = probe.Nonce });
        f.Clock.Now = 0.050; // tick handled at 50 ms
        f.Tick();

        var state = f.Server.RttStateForTests(peer.Id);
        Assert.Equal(1, state.Samples);
        Assert.Equal(0.0, state.Seconds);
        Assert.False(state.Outstanding);
    }
}
