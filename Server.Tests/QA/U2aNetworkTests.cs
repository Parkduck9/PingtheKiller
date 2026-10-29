using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for unit U2a (Server.Core): real TCP (loopback, ephemeral port, every wait <= 5 s), the
// offline no-socket property and game-loop thread ownership.
public sealed class U2aNetworkTests
{
    private static (GameServer game, TcpTransport listener, ConcurrentQueue<TcpConnection> accepted) StartTcp(ServerOptions? options = null)
    {
        var game = new GameServer(options ?? new ServerOptions { Seed = 5 });
        var listener = new TcpTransport(0, IPAddress.Loopback);
        var accepted = new ConcurrentQueue<TcpConnection>();
        listener.ConnectionAccepted += c => { if (c is TcpConnection t) accepted.Enqueue(t); };
        listener.ConnectionAccepted += game.AddConnection;
        listener.Start();
        game.Start();
        return (game, listener, accepted);
    }

    private static byte[] RawFrame(int length, ushort id, byte[] payload)
    {
        var frame = new byte[4 + 2 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), id);
        payload.CopyTo(frame, 6);
        return frame;
    }

    // ------------------------------------------------------------------ 9. TCP isolation

    [Fact]
    public async Task Tcp_EachMalformedClientIsClosedIndividually_WellBehavedClientKeepsWorking()
    {
        var (game, listener, _) = StartTcp();
        try
        {
            using var good = await RawClient.ConnectAsync(listener.Port);
            // Join split into 1-byte writes.
            await good.SendByteByByteAsync(PacketCodec.Encode(new C2S_Join { Nickname = "Good" }));
            var join = await good.ReceiveAsync<S2C_JoinResult>();
            Assert.Equal(0, join.Result);
            Assert.NotEqual(0UL, join.ReconnectToken);

            var bad = new List<(string name, byte[] bytes)>
            {
                ("Length=-1", RawFrame(-1, 0, Array.Empty<byte>()).AsSpan(0, 4).ToArray()),
                ("Length=0", new byte[4]),
                ("Length=1", BitConverter.GetBytes(1)),
                ("Length=16385", BitConverter.GetBytes(16385)),
                ("Length=int.MaxValue", BitConverter.GetBytes(int.MaxValue)),
                ("Length=int.MinValue", BitConverter.GetBytes(int.MinValue)),
                ("MoveInput with garbage 3-byte payload", RawFrame(2 + 3, (ushort)PacketId.C2S_MoveInput, new byte[] { 1, 2, 3 })),
                ("MoveInput with oversized payload", RawFrame(2 + 20, (ushort)PacketId.C2S_MoveInput, new byte[20])),
                ("unknown packet id", RawFrame(2, 0xFFFF, Array.Empty<byte>())),
                ("SetRole with undefined role byte", RawFrame(3, (ushort)PacketId.C2S_SetRole, new byte[] { 77 })),
                ("Join with bogus string length", RawFrame(2 + 12, (ushort)PacketId.C2S_Join, new byte[12].Select((_, i) => i == 0 ? (byte)0xFF : (byte)0x7F).ToArray())),
            };
            foreach (var (name, bytes) in bad)
            {
                using var offender = await RawClient.ConnectAsync(listener.Port);
                await offender.SendAsync(bytes);
                Assert.True(await offender.WaitClosedAsync(), "offender not closed: " + name);
                // The well-behaved client is unaffected after every abuse.
                await good.SendAsync(new C2S_Ping { Timestamp = name.Length });
                Assert.Equal(name.Length, (await good.ReceiveAsync<S2C_Pong>()).Timestamp);
            }

            // A second well-behaved client can still join and sees the first one in the lobby.
            using var late = await RawClient.ConnectAsync(listener.Port);
            await late.SendAsync(new C2S_Join { Nickname = "Late" });
            Assert.Equal(0, (await late.ReceiveAsync<S2C_JoinResult>()).Result);
            var lobby = await late.ReceiveAsync<S2C_LobbyState>();
            Assert.Contains(lobby.Slots, s => s.Nickname == "Good");
        }
        finally { listener.Stop(); game.Stop(); }
    }

    [Fact]
    public async Task Tcp_ValidFrameFollowedByGarbageInSameWrite_ClosesOnlyThatConnection_AfterProcessingTheValidFrame()
    {
        var (game, listener, _) = StartTcp();
        try
        {
            using var good = await RawClient.ConnectAsync(listener.Port);
            await good.SendAsync(new C2S_Join { Nickname = "Good" });
            await good.ReceiveAsync<S2C_JoinResult>();

            using var mixed = await RawClient.ConnectAsync(listener.Port);
            var bytes = PacketCodec.Encode(new C2S_Ping { Timestamp = 4242 }).Concat(BitConverter.GetBytes(-5)).ToArray();
            await mixed.SendAsync(bytes);
            Assert.True(await mixed.WaitClosedAsync());

            await good.SendAsync(new C2S_Ping { Timestamp = 1 });
            Assert.Equal(1, (await good.ReceiveAsync<S2C_Pong>()).Timestamp);
        }
        finally { listener.Stop(); game.Stop(); }
    }

    [Fact]
    public async Task Tcp_ServerToClientPacketIdsSentByAClient_DoNotCrashOrDisturbOthers()
    {
        var (game, listener, _) = StartTcp();
        try
        {
            using var good = await RawClient.ConnectAsync(listener.Port);
            await good.SendAsync(new C2S_Join { Nickname = "Good" });
            await good.ReceiveAsync<S2C_JoinResult>();
            using var odd = await RawClient.ConnectAsync(listener.Port);
            await odd.SendAsync(new C2S_Join { Nickname = "Odd" });
            await odd.ReceiveAsync<S2C_JoinResult>();
            foreach (IPacket p in new IPacket[] { new S2C_Pong { Timestamp = 1 }, new S2C_MatchStart(), new S2C_WorldState(), new S2C_LobbyState(), new S2C_GameResult(), new S2C_JoinResult { PlayerId = 1 } })
                await odd.SendAsync(p);
            await odd.SendAsync(new C2S_Ping { Timestamp = 5 });
            // whether the offender is kept or dropped is unspecified; it must not affect anyone else
            await good.SendAsync(new C2S_Ping { Timestamp = 6 });
            Assert.Equal(6, (await good.ReceiveAsync<S2C_Pong>()).Timestamp);
        }
        finally { listener.Stop(); game.Stop(); }
    }

    // ------------------------------------------------------------------ 10. slow consumer

    [Fact]
    public async Task Tcp_SlowConsumerIsDisconnectedAtQueueCap_GameLoopKeepsServingOthers()
    {
        var (game, listener, accepted) = StartTcp(new ServerOptions { BotCount = 3, AutoStart = true, Seed = 7 });
        try
        {
            using var good = await RawClient.ConnectAsync(listener.Port);
            await good.SendAsync(new C2S_Join { Nickname = "Good" });
            Assert.Equal(0, (await good.ReceiveAsync<S2C_JoinResult>()).Result);
            await good.ReceiveAsync<S2C_MatchStart>();
            uint tick0 = (await good.ReceiveAsync<S2C_WorldState>()).Tick;

            using var slow = await RawClient.ConnectAsync(listener.Port, receiveBuffer: 1024);
            await slow.SendAsync(new C2S_Join { Nickname = "Slow" });
            var sw = Stopwatch.StartNew();
            while (accepted.Count < 2 && sw.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(5);
            Assert.Equal(2, accepted.Count);
            var slowConn = accepted.ToArray()[1];

            // The slow client never reads. It floods Pings; each is answered with a Pong that piles up server-side
            // (plus the 20 Hz WorldState stream it also receives).
            byte[] chunk = Enumerable.Repeat(PacketCodec.Encode(new C2S_Ping { Timestamp = 1 }), 2000).SelectMany(b => b).ToArray();
            long sent = 0;
            int maxQueued = 0;
            sw.Restart();
            while (!slowConn.IsClosed && sw.Elapsed < TimeSpan.FromSeconds(5) && sent < 400L * 1024 * 1024)
            {
                try { await slow.SendAsync(chunk); }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException) { break; }
                sent += chunk.Length;
                maxQueued = Math.Max(maxQueued, slowConn.QueuedBytes);
            }
            sw.Restart();
            while (!slowConn.IsClosed && sw.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            Assert.True(slowConn.IsClosed, $"slow consumer never disconnected (sent {sent} bytes of pings, max queued {maxQueued})");
            Assert.True(maxQueued <= TcpConnection.MaxQueuedBytes, $"queue grew to {maxQueued} > cap");

            // The game loop kept ticking and still serves the other client.
            uint last = tick0;
            sw.Restart();
            while (last < tick0 + 20 && sw.Elapsed < TimeSpan.FromSeconds(5))
                last = (await good.ReceiveAsync<S2C_WorldState>()).Tick;
            Assert.True(last >= tick0 + 20, "game loop stalled for the healthy client");
            await good.SendAsync(new C2S_Ping { Timestamp = 31337 });
            Assert.Equal(31337, (await good.ReceiveAsync<S2C_Pong>()).Timestamp);
        }
        finally { listener.Stop(); game.Stop(); }
    }

    // ------------------------------------------------------------------ 11. InMemoryTransport opens no socket

    private sealed class SocketEvents : EventListener
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal readonly ConcurrentQueue<string> Events = new();
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name.Contains("System.Net.Sockets") || source.Name.Contains("System.Net.NameResolution"))
                EnableEvents(source, EventLevel.Verbose, EventKeywords.All);
        }
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == _thread) Events.Enqueue(e.EventSource.Name + "/" + e.EventName);
        }
    }

    private static bool IsForbiddenNetworkType(Type t) =>
        t.Namespace != null && t.Namespace.StartsWith("System.Net.Sockets", StringComparison.Ordinal) ||
        typeof(Stream).IsAssignableFrom(t);

    // Walks the object graph (own types, collections, delegates, arrays) and returns the first socket-ish object type.
    private static string? FindNetworkObject(params object[] roots)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>(roots);
        var ownAssemblies = new[] { typeof(GameServer).Assembly, typeof(PacketCodec).Assembly };
        while (queue.Count > 0 && seen.Count < 200_000)
        {
            var o = queue.Dequeue();
            if (!seen.Add(o)) continue;
            var t = o.GetType();
            if (IsForbiddenNetworkType(t)) return t.FullName;
            if (o is Array arr)
            {
                if (!t.GetElementType()!.IsPrimitive) foreach (var e in arr) if (e != null) queue.Enqueue(e);
                continue;
            }
            bool descend = ownAssemblies.Contains(t.Assembly) || typeof(Delegate).IsAssignableFrom(t) ||
                           t.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true;
            if (!descend) continue;
            for (Type? c = t; c != null && c != typeof(object); c = c.BaseType)
                foreach (var field in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.IsPrimitive || field.FieldType.IsPointer) continue;
                    if (IsForbiddenNetworkType(field.FieldType)) return t.FullName + "." + field.Name + ": " + field.FieldType.FullName;
                    var v = field.GetValue(o);
                    if (v != null) queue.Enqueue(v);
                }
        }
        return null;
    }

    [Fact]
    public void InMemoryTransport_OfflineMatchOpensNoSocket()
    {
        // Method (documented): (a) the offline factory path must never invoke the online (TCP) factory;
        // (b) a process-level EventListener on System.Net.Sockets / NameResolution, filtered to this test's thread
        //     (the whole offline run executes on it), records nothing during the run, with a positive control proving
        //     the listener does capture socket telemetry from this thread;
        // (c) after the run, no System.Net.Sockets object or Stream is reachable from the server, transport or either
        //     connection object graph, nor from any static field of the Server.Core assembly.
        var options = new ServerOptions { IsOffline = true, Seed = 11, BotCount = 4, AutoStart = false };
        using var events = new SocketEvents();

        // positive control: a synchronous loopback connect on this thread must be observed (or the method is blind).
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            using var s = new Socket(SocketType.Stream, ProtocolType.Tcp);
            s.Connect(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)probe.LocalEndpoint).Port));
        }
        finally { probe.Stop(); }
        bool controlSeen = !events.Events.IsEmpty;
        while (events.Events.TryDequeue(out _)) { }

        using var transport = options.CreateTransport(() => throw new InvalidOperationException("online factory must not be called offline"));
        Assert.IsType<InMemoryTransport>(transport);
        using var game = new GameServer(options);
        transport.ConnectionAccepted += game.AddConnection;
        transport.Start();
        var mem = (InMemoryTransport)transport;
        var clients = new List<IConnection> { mem.Connect(), mem.Connect() };
        foreach (var c in clients) c.Send(PacketCodec.Encode(new C2S_Join { Nickname = "Off" }));
        game.Tick(0.05f);
        clients[0].Send(PacketCodec.Encode(new C2S_StartMatch()));
        for (int i = 0; i < 400; i++)
        {
            clients[1].Send(PacketCodec.Encode(new C2S_MoveInput { DirX = 0.6f, DirZ = 0.8f, Seq = (uint)i + 1 }));
            game.Tick(0.05f);
            foreach (var c in clients) while (c.TryReceive(out _)) { }
        }
        var during = events.Events.ToArray();
        Assert.Empty(during);
        var found = FindNetworkObject(game, transport, clients[0], clients[1]);
        Assert.Null(found);
        foreach (var type in typeof(GameServer).Assembly.GetTypes())
            foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Assert.False(IsForbiddenNetworkType(f.FieldType), "static network field " + type.FullName + "." + f.Name);
        Assert.True(controlSeen, "method sanity: the event listener did not observe a control socket connect from this thread");
    }

    // ------------------------------------------------------------------ 12. game-loop thread ownership

    [Fact]
    public async Task GameLoop_ConcurrentSendersFromManyThreads_NoExceptionsConsistentState_StopIsPrompt()
    {
        using var transport = new InMemoryTransport();
        var game = new GameServer(new ServerOptions { IsOffline = true, BotCount = 2, AutoStart = true, Seed = 21 });
        transport.ConnectionAccepted += game.AddConnection;
        transport.Start();
        game.Start();
        var errors = new ConcurrentQueue<string>();
        var worlds = new ConcurrentBag<(int thread, uint tick)>();
        var violations = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();

        // Manual ticking from a foreign thread while the loop runs must be refused (single owner of game state).
        Assert.Throws<InvalidOperationException>(() => game.Tick(0.05f));

        var threads = new List<Thread>();
        for (int t = 0; t < 6; t++)
        {
            int id = t;
            var th = new Thread(() =>
            {
                try
                {
                    var rnd = new Random(1000 + id);
                    var conn = transport.Connect();
                    conn.Send(PacketCodec.Encode(new C2S_Join { Nickname = "T" + id }));
                    uint seq = 0, lastTick = 0;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 1200 && !cts.IsCancellationRequested)
                    {
                        conn.Send(PacketCodec.Encode(new C2S_MoveInput { DirX = (float)rnd.NextDouble() * 2 - 1, DirZ = (float)rnd.NextDouble() * 2 - 1, Seq = ++seq }));
                        int pick = rnd.Next(40);
                        if (pick == 0) conn.Send(PacketCodec.Encode(new C2S_StartMatch()));
                        else if (pick == 1) conn.Send(PacketCodec.Encode(new C2S_SetBots { Count = (byte)rnd.Next(8) }));
                        else if (pick == 2) conn.Send(PacketCodec.Encode(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Random }));
                        else if (pick == 3) conn.Send(PacketCodec.Encode(new C2S_Ping { Timestamp = id }));
                        else if (pick == 4) conn.Send(PacketCodec.Encode(new C2S_Interact { Active = 1 }));
                        else if (pick == 5) conn.Send(PacketCodec.Encode(new C2S_UseSkill { SkillId = 0 }));
                        while (conn.TryReceive(out var p))
                        {
                            switch (p)
                            {
                                case S2C_WorldState w:
                                    if (w.Tick < lastTick) violations.Enqueue("tick went backwards");
                                    lastTick = w.Tick;
                                    worlds.Add((id, w.Tick));
                                    // U2c migration: killer-bound survivor entries are anonymous (Id 0 by design); every entry that
                                    // carries an Id (the killer's own, all survivor-bound entries) must still be unique, and an
                                    // Id-less entry may only be a survivor blip.
                                    var identified = w.Players.Where(x => x.Id != 0).Select(x => x.Id).ToList();
                                    if (identified.Distinct().Count() != identified.Count) violations.Enqueue("duplicate player in world");
                                    if (w.Players.Any(x => x.Id == 0 && x.Role != (byte)Role.Survivor)) violations.Enqueue("anonymous non-survivor entry");
                                    foreach (var pl in w.Players)
                                        if (float.IsNaN(pl.X) || pl.X < 0 || pl.X > 60 || pl.Z < 0 || pl.Z > 60) violations.Enqueue("position out of bounds");
                                    break;
                                case S2C_LobbyState l:
                                    var ids = l.Slots.Where(s => s.PlayerId != 0).Select(s => s.PlayerId).ToList();
                                    if (ids.Distinct().Count() != ids.Count) violations.Enqueue("duplicate slot ids");
                                    if (l.Slots.Count(s => s.IsHost == 1) > 1) violations.Enqueue("two hosts");
                                    break;
                            }
                        }
                        Thread.Sleep(rnd.Next(1, 6));
                    }
                    conn.Close();
                }
                catch (Exception ex) { errors.Enqueue(ex.ToString()); }
            }) { IsBackground = true };
            threads.Add(th);
            th.Start();
        }
        // Extra churn: connections that connect/send junk/close as fast as possible.
        var churn = new Thread(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 1200)
                {
                    var c = transport.Connect();
                    c.Send(PacketCodec.Encode(new C2S_Join { Nickname = "churn" }));
                    c.Send(new byte[] { 1, 2, 3 });
                    c.Close();
                }
            }
            catch (Exception ex) { errors.Enqueue(ex.ToString()); }
        }) { IsBackground = true };
        churn.Start();
        foreach (var th in threads) Assert.True(th.Join(TimeSpan.FromSeconds(5)), "client thread hung");
        Assert.True(churn.Join(TimeSpan.FromSeconds(5)), "churn thread hung");

        var stop = Stopwatch.StartNew();
        game.Stop();
        stop.Stop();
        await Task.CompletedTask;
        Assert.True(stop.ElapsedMilliseconds < 1000, "Stop() took " + stop.ElapsedMilliseconds + " ms");
        Assert.Empty(errors);
        Assert.Empty(violations);
        Assert.True(worlds.Count > 20, "clients received almost no WorldState (game loop dead?)");
        // Loop thread joined: manual Tick is allowed again, i.e. Start()'s thread no longer owns the state.
        game.Tick(0.05f);
        game.Stop(); // idempotent
    }
}
