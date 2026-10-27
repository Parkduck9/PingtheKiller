using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for U2a attempt 2, TCP part (fix 3 accept loop, fix 5 keep-alive and token takeover over real sockets).
// Loopback only, ephemeral port 0, every wait <= 5 s, port 7777 is never used.
public sealed class U2aA2NetworkTests
{
    private static (GameServer game, TcpTransport listener, ConcurrentQueue<TcpConnection> accepted, ConcurrentQueue<string> logs)
        StartTcp(ServerOptions? options = null, Action<TcpTransport>? beforeStart = null)
    {
        var game = new GameServer(options ?? new ServerOptions { Seed = 5 });
        var listener = new TcpTransport(0, IPAddress.Loopback);
        var accepted = new ConcurrentQueue<TcpConnection>();
        var logs = new ConcurrentQueue<string>();
        listener.Log += logs.Enqueue;
        beforeStart?.Invoke(listener);
        listener.ConnectionAccepted += c => { if (c is TcpConnection t) accepted.Enqueue(t); };
        listener.ConnectionAccepted += game.AddConnection;
        listener.Start();
        game.Start();
        return (game, listener, accepted, logs);
    }

    private static async Task<(RawClient client, S2C_JoinResult join)> JoinAsync(int port, string name, ulong token = 0)
    {
        var client = await RawClient.ConnectAsync(port);
        await client.SendAsync(new C2S_Join { Nickname = name, ReconnectToken = token });
        var join = await client.ReceiveAsync<S2C_JoinResult>();
        return (client, join);
    }

    [Fact]
    public async Task Accept_ThrowingSubscriberOnFirstClient_ThatClientClosed_NextClientAcceptedNormally_StopDoesNotThrow()
    {
        int calls = 0;
        var (game, listener, accepted, logs) = StartTcp(beforeStart: t =>
            t.ConnectionAccepted += _ => { if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("subscriber-boom"); });
        try
        {
            using var first = await RawClient.ConnectAsync(listener.Port);
            Assert.True(await first.WaitClosedAsync(), "client whose accept subscriber threw was not closed");

            // The accept loop survived: a second client is accepted and served (Ping needs the game to be wired).
            using var second = await RawClient.ConnectAsync(listener.Port);
            await second.SendAsync(new C2S_Join { Nickname = "Second" });
            Assert.Equal(0, (await second.ReceiveAsync<S2C_JoinResult>()).Result);
            await second.SendAsync(new C2S_Ping { Timestamp = 77 });
            Assert.Equal(77, (await second.ReceiveAsync<S2C_Pong>()).Timestamp);
            Assert.True(logs.Any(l => l.Contains("accept error") && l.Contains("subscriber-boom")), "the fault was not reported through TcpTransport.Log");
        }
        finally
        {
            var ex = Record.Exception(() => listener.Stop());
            Assert.Null(ex);
            game.Stop();
        }
    }

    [Fact]
    public async Task Accept_SubscriberThrowsForEveryClient_LoopSurvivesManyFaults_LogSubscriberThrowingIsHarmless_StopDoesNotThrow()
    {
        var game = new GameServer(new ServerOptions { Seed = 6 });
        var listener = new TcpTransport(0, IPAddress.Loopback);
        int logCalls = 0, accepts = 0;
        listener.Log += _ => { Interlocked.Increment(ref logCalls); throw new InvalidOperationException("log-boom"); };
        listener.ConnectionAccepted += _ => { Interlocked.Increment(ref accepts); throw new InvalidOperationException("always-boom"); };
        listener.Start();
        game.Start();
        try
        {
            for (int i = 0; i < 6; i++)
            {
                using var c = await RawClient.ConnectAsync(listener.Port);
                Assert.True(await c.WaitClosedAsync(), "client " + i + " not closed");
            }
            Assert.True(Volatile.Read(ref accepts) >= 6, "accept loop stopped after faults (" + accepts + " accepts)");
            Assert.True(Volatile.Read(ref logCalls) >= 1);
            // Now make the subscriber healthy: the very same running loop serves the next client.
            listener.ConnectionAccepted += game.AddConnection;
        }
        finally
        {
            Assert.Null(Record.Exception(() => listener.Stop()));
            game.Stop();
        }
    }

    [Fact]
    public async Task Accept_ClosedGameDoesNotBreakTransport_ThrowingAddConnectionFollowedByGoodClient()
    {
        // AddConnection(null-equivalent faults are not reachable); instead a subscriber that closes the connection and throws.
        int n = 0;
        var (game, listener, _, _) = StartTcp(beforeStart: t =>
            t.ConnectionAccepted += c => { if (Interlocked.Increment(ref n) <= 2) { c.Close(); throw new ObjectDisposedException("x"); } });
        try
        {
            for (int i = 0; i < 2; i++)
            {
                using var bad = await RawClient.ConnectAsync(listener.Port);
                Assert.True(await bad.WaitClosedAsync());
            }
            using var good = await RawClient.ConnectAsync(listener.Port);
            await good.SendAsync(new C2S_Ping { Timestamp = 5 });
            Assert.Equal(5, (await good.ReceiveAsync<S2C_Pong>()).Timestamp);
        }
        finally { Assert.Null(Record.Exception(() => listener.Stop())); game.Stop(); }
    }

    [Fact]
    public async Task Accept_StopThenRestartOnSameInstanceStillAccepts_StopIsIdempotent()
    {
        var game = new GameServer(new ServerOptions { Seed = 8 });
        var listener = new TcpTransport(0, IPAddress.Loopback);
        listener.ConnectionAccepted += game.AddConnection;
        listener.Start();
        game.Start();
        try
        {
            using (var c = await RawClient.ConnectAsync(listener.Port)) { await c.SendAsync(new C2S_Ping { Timestamp = 1 }); await c.ReceiveAsync<S2C_Pong>(); }
            Assert.Null(Record.Exception(() => listener.Stop()));
            Assert.Null(Record.Exception(() => listener.Stop()));
        }
        finally { game.Stop(); }
    }

    [Fact]
    public async Task Tcp_AcceptedConnectionHasKeepAliveEnabled()
    {
        var (game, listener, accepted, _) = StartTcp();
        try
        {
            using var c = await RawClient.ConnectAsync(listener.Port);
            await c.SendAsync(new C2S_Ping { Timestamp = 1 });
            await c.ReceiveAsync<S2C_Pong>();
            Assert.True(accepted.TryPeek(out var conn));
            var field = typeof(TcpConnection).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var tcp = (TcpClient)field!.GetValue(conn)!;
            Assert.NotEqual(0, Convert.ToInt32(tcp.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)));
        }
        finally { listener.Stop(); game.Stop(); }
    }

    [Fact]
    public async Task Tcp_TokenTakeover_OldSocketStillOpen_OldClosed_NewBoundToSamePlayerAndPosition()
    {
        var (game, listener, _, _) = StartTcp(new ServerOptions { Seed = 9, BotCount = 3, AutoStart = true });
        try
        {
            var (old, join) = await JoinAsync(listener.Port, "Old");
            using var _old = old;
            Assert.Equal(0, join.Result);
            await old.ReceiveAsync<S2C_MatchStart>();
            // wait until the player has frames (stationary human, no inputs)
            var w0 = await old.ReceiveAsync<S2C_WorldState>();
            var w1 = await old.ReceiveAsync<S2C_WorldState>();
            var me0 = w1.Players.Single(p => p.Id == join.PlayerId);

            // The old socket is NOT closed by the client (half-open peer). A new socket presents the token.
            var (fresh, join2) = await JoinAsync(listener.Port, "New", join.ReconnectToken);
            using var _fresh = fresh;
            Assert.Equal(0, join2.Result);
            Assert.Equal(join.PlayerId, join2.PlayerId);
            Assert.Equal(join.ReconnectToken, join2.ReconnectToken);
            Assert.True(await old.WaitClosedAsync(), "old connection stayed open after takeover");
            await fresh.ReceiveAsync<S2C_MatchStart>();
            var wn = await fresh.ReceiveAsync<S2C_WorldState>();
            var me1 = wn.Players.Single(p => p.Id == join.PlayerId);
            Assert.Equal(me0.X, me1.X);
            Assert.Equal(me0.Z, me1.Z);
            Assert.Equal(me0.Role, me1.Role);
            var lobby = fresh.Received.OfType<S2C_LobbyState>().Last();
            Assert.Equal(1, lobby.Slots.Count(s => s.PlayerId != 0 && s.IsBot == 0));
            Assert.Equal(3, lobby.Slots.Count(s => s.PlayerId != 0)); // a live join replaces a bot: 1 human + 2 bots
        }
        finally { listener.Stop(); game.Stop(); }
    }

    [Fact]
    public async Task Tcp_TokenOfAnotherPlayer_OnAFreshSocket_TakesOverThatPlayerOnly_OthersUndisturbed()
    {
        var (game, listener, _, _) = StartTcp(new ServerOptions { Seed = 10 });
        try
        {
            var (a, ja) = await JoinAsync(listener.Port, "A");
            var (b, jb) = await JoinAsync(listener.Port, "B");
            using var _a = a; using var _b = b;
            Assert.NotEqual(ja.ReconnectToken, jb.ReconnectToken);
            // A fresh socket with a bogus token: rejected, nobody is closed.
            var (x, jx) = await JoinAsync(listener.Port, "X", ja.ReconnectToken ^ 0xFFUL);
            using var _x = x;
            Assert.NotEqual(0, jx.Result);
            await a.SendAsync(new C2S_Ping { Timestamp = 3 });
            Assert.Equal(3, (await a.ReceiveAsync<S2C_Pong>()).Timestamp);
            await b.SendAsync(new C2S_Ping { Timestamp = 4 });
            Assert.Equal(4, (await b.ReceiveAsync<S2C_Pong>()).Timestamp);
            // Taking over B leaves A untouched.
            var (b2, jb2) = await JoinAsync(listener.Port, "B2", jb.ReconnectToken);
            using var _b2 = b2;
            Assert.Equal(jb.PlayerId, jb2.PlayerId);
            Assert.True(await b.WaitClosedAsync());
            await a.SendAsync(new C2S_Ping { Timestamp = 5 });
            Assert.Equal(5, (await a.ReceiveAsync<S2C_Pong>()).Timestamp);
        }
        finally { listener.Stop(); game.Stop(); }
    }
}
