using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// Tests for the U2a attempt-2 fix list (docs/briefs/U2a-a2.md), one region per fix.
public sealed class AttemptTwoFixTests
{
    private static bool Visible(CoreFixture f, S2C_WorldState world, int id) => KillerTruth.Has(f.Server, world, id);
    private static float Step(PlayerState a, PlayerState b) => MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));

    // ------------------------------------------------------------ fix 1: held direction expires with the Signal hold

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleInputMovesOnlyWhileSignalHoldIsActiveThenStops(bool moveKiller)
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        var mover = moveKiller ? killer : survivor;
        mover.Move(1, 0);
        var previous = mover.Self();
        int movingTicks = 0;
        for (int i = 0; i < 40; i++)
        {
            f.Tick();
            var current = mover.Self();
            if (Step(previous, current) > 1e-4f)
            {
                movingTicks++;
                if (!moveKiller) Assert.True(Visible(f, killer.Last<S2C_WorldState>(), survivor.Id), "moved while hidden at tick " + i);
            }
            previous = current;
        }
        // The hold is 0.15 s: the input moves the player for at most three 0.05 s ticks, then it stops.
        Assert.InRange(movingTicks, 1, (int)Math.Round(GameConstants.SignalHoldSeconds / 0.05f));
        Assert.False(Visible(f, killer.Last<S2C_WorldState>(), survivor.Id));
    }

    [Fact]
    public void TwentyHertzClientMovesContinuouslyAtFullSpeedAndStaysVisible()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        var previous = survivor.Self();
        for (int i = 0; i < 40; i++)
        {
            f.Walk(survivor, 0, 1, 1);
            var current = survivor.Self();
            Assert.InRange(Step(previous, current), 0.199f, 0.201f);
            Assert.Equal((byte)PlayerFlags.Signal, KillerTruth.Entry(f.Server, killer.Last<S2C_WorldState>(), survivor.Id)?.Flags);
            previous = current;
        }
    }

    [Fact]
    public void ThirtyHertzClientContractMovesContinuouslyThenStopsAndHidesWithinHold()
    {
        // Cross-unit contract: the Unity client sends MoveInput at a fixed 30 Hz while a key is held (never above 30/s).
        // Here the client stops sending entirely after 1 s (no zero input), the worst case for the server.
        var (fixture, killer, survivor) = CoreFixture.PairWithoutAfterimage();
        using var f = fixture;
        var previous = survivor.Self();
        int nextInput = 0; // input k is sent at simulated time k / 30 s
        int lastInputTick = -1, lastMovingTick = -1;
        var visibleByTick = new List<bool>();
        for (int tick = 0; tick < 40; tick++)
        {
            // Tick j processes the inputs sent during [j, j + 1) * 0.05 s, i.e. k / 30 < (j + 1) / 20.
            int sentThisTick = 0;
            while (nextInput < 30 && 2 * nextInput < 3 * (tick + 1))
            {
                survivor.Move(0, 1);
                nextInput++;
                sentThisTick++;
                lastInputTick = tick;
            }
            if (tick < 20) Assert.InRange(sentThisTick, 1, 2); // 30 Hz spread over 20 Hz ticks
            f.Tick();
            var current = survivor.Self();
            bool moved = Step(previous, current) > 1e-4f;
            bool visible = Visible(f, killer.Last<S2C_WorldState>(), survivor.Id);
            visibleByTick.Add(visible);
            if (moved) lastMovingTick = tick;
            // (4) never changes position in a tick where it is absent from the killer's WorldState.
            Assert.False(moved && !visible, "survivor moved while omitted from the killer frame at tick " + tick);
            if (tick < 20)
            {
                // (1) continuous full speed and visible for the whole second of input.
                Assert.InRange(Step(previous, current), 0.199f, 0.201f);
                Assert.True(visible);
            }
            previous = current;
        }
        Assert.Equal(19, lastInputTick);
        // (2) position stops changing within 0.15 s +- one tick after the last input.
        Assert.InRange((lastMovingTick - lastInputTick) * 0.05f, 0f, GameConstants.SignalHoldSeconds + 0.05f + 1e-4f);
        // (3) after that it drops out of the killer's WorldState and stays out.
        for (int tick = lastMovingTick + 2; tick < visibleByTick.Count; tick++)
            Assert.False(visibleByTick[tick], "stationary survivor still in killer frame at tick " + tick);
    }

    [Fact]
    public void RandomInputPatternsNeverMoveAnySurvivorWhileHiddenFromKiller()
    {
        // Invariant over human and bot survivors: a position change implies presence in the same tick's killer frame.
        using var f = new CoreFixture(seed: 77);
        var killer = f.Join("Killer");
        var survivor = f.Join("Survivor");
        killer.Send(new C2S_SetBots { Count = 3 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        var random = new Random(5);
        // Survivor frames may also carry the killer in line of sight (U2e); only survivor entries are ground truth here.
        var previous = survivor.Last<S2C_WorldState>().Players.Where(p => p.Role == (byte)Role.Survivor).ToDictionary(p => p.Id);
        Assert.Equal(4, previous.Count);
        int movedChecks = 0, hiddenTicks = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            int roll = random.Next(10);
            if (roll < 3) survivor.Move((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);
            else if (roll == 3) survivor.Move(0, 0);
            if (roll == 4) killer.Move(1, 0);
            f.Tick();
            // Survivors receive every survivor: that is the ground truth for positions.
            var truth = survivor.Last<S2C_WorldState>().Players.Where(p => p.Role == (byte)Role.Survivor);
            var killerView = killer.Last<S2C_WorldState>();
            foreach (var state in truth)
            {
                var before = previous[state.Id];
                if (Step(before, state) > 1e-5f)
                {
                    movedChecks++;
                    Assert.True(Visible(f, killerView, state.Id), $"survivor {state.Id} moved while hidden at tick {tick}");
                }
                else if (!Visible(f, killerView, state.Id)) hiddenTicks++;
                previous[state.Id] = state;
            }
        }
        Assert.True(movedChecks > 100);
        Assert.True(hiddenTicks > 50);
    }

    // ------------------------------------------------------------ fix 2: game loop survives faults

    [Fact]
    public void ConnectionWhoseSendThrowsIsClosedAloneAndOthersKeepReceiving()
    {
        using var f = new CoreFixture();
        var log = new List<string>();
        f.Server.Log += log.Add;
        var (killer, survivor) = f.StartPair();
        var faulty = new FaultyConnection(throwOnSend: true, packetToDeliver: new C2S_Join { Nickname = "Faulty" });
        f.Server.AddConnection(faulty);
        uint before = survivor.Last<S2C_WorldState>().Tick;
        f.Tick();
        f.Tick();
        Assert.True(faulty.IsClosed);
        Assert.Contains(log, l => l.StartsWith("send error InvalidOperationException", StringComparison.Ordinal));
        Assert.True(survivor.Last<S2C_WorldState>().Tick >= before + 2);
        Assert.True(killer.Last<S2C_WorldState>().Tick >= before + 2);
    }

    [Fact]
    public void ThreadedLoopLogsTickFaultsOncePerSecondAndKeepsTicking()
    {
        using var game = new GameServer(new ServerOptions { IsOffline = true, Seed = 3, BotCount = 2, AutoStart = true });
        using var transport = new InMemoryTransport();
        transport.ConnectionAccepted += game.AddConnection;
        transport.Start();
        var log = new ConcurrentQueue<string>();
        game.Log += _ => throw new InvalidOperationException("subscriber failure"); // must not escape either
        game.Log += log.Enqueue;
        var peer = new Peer(transport.Connect());
        peer.Send(new C2S_Join { Nickname = "Healthy" });
        game.Start();
        WaitUntil(() => { peer.Drain(); return peer.Packets.OfType<S2C_WorldState>().Any(); });
        // Three connections whose RemoteId throws: each faults one tick inside Tick (connection registration).
        for (int i = 0; i < 3; i++) game.AddConnection(new FaultyConnection(throwOnRemoteId: true));
        WaitUntil(() => log.Any(l => l.Contains("tick error InvalidOperationException: remote id failure")));
        uint afterFault = peer.Last<S2C_WorldState>().Tick;
        WaitUntil(() => peer.Last<S2C_WorldState>().Tick >= afterFault + 6);
        game.Stop();
        // The three faults happen within ~0.15 s, so the distinct message is logged once.
        Assert.Single(log, l => l.StartsWith("tick error", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ fix 3: accept loop survives faults

    [Fact]
    public async Task ThrowingAcceptSubscriberClosesOnlyThatClientAndAcceptLoopContinues()
    {
        using var game = new GameServer();
        using var transport = new TcpTransport(0, IPAddress.Loopback);
        var log = new ConcurrentQueue<string>();
        transport.Log += log.Enqueue;
        int accepted = 0;
        transport.ConnectionAccepted += connection =>
        {
            if (Interlocked.Increment(ref accepted) == 1) throw new InvalidOperationException("subscriber failure");
            game.AddConnection(connection);
        };
        transport.Start();
        game.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = new TcpClient();
        await first.ConnectAsync(IPAddress.Loopback, transport.Port, timeout.Token);
        int read;
        try { read = await first.GetStream().ReadAsync(new byte[1], timeout.Token); }
        catch (IOException) { read = 0; } // reset counts as closed
        Assert.Equal(0, read);
        using var second = new TcpClient();
        await second.ConnectAsync(IPAddress.Loopback, transport.Port, timeout.Token);
        await second.GetStream().WriteAsync(PacketCodec.Encode(new C2S_Join { Nickname = "Second" }), timeout.Token);
        Assert.Equal(0, (await TcpReceive<S2C_JoinResult>(second.GetStream(), timeout.Token)).Result);
        Assert.Contains(log, l => l.Contains("accept error InvalidOperationException: subscriber failure"));
        transport.Stop(); // never rethrows
        transport.Stop();
        game.Stop();
    }

    // ------------------------------------------------------------ fix 4: Seq serial-number comparison

    [Fact]
    public void SeqWrapAroundIsAcceptedInOrderAndOlderSeqAfterWrapIsDropped()
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        (uint seq, float x, float z)[] steps =
        {
            (uint.MaxValue - 1, 1, 0), (uint.MaxValue, 0, 1), (0, -1, 0), (1, 0, -1),
        };
        foreach (var (seq, x, z) in steps)
        {
            var before = survivor.Self();
            survivor.Send(new C2S_MoveInput { DirX = x, DirZ = z, Seq = seq });
            f.Tick();
            var after = survivor.Self();
            Assert.InRange((after.X - before.X) * x + (after.Z - before.Z) * z, 0.199f, 0.201f);
        }
        // uint.MaxValue is older than 1 under serial-number arithmetic: dropped, the player keeps going -Z.
        var last = survivor.Self();
        survivor.Send(new C2S_MoveInput { DirX = 1, Seq = uint.MaxValue });
        f.Tick();
        var now = survivor.Self();
        Assert.Equal(last.X, now.X);
        Assert.InRange(last.Z - now.Z, 0.199f, 0.201f);
    }

    // ------------------------------------------------------------ fix 5: half-open connections

    [Fact]
    public async Task AcceptedTcpConnectionEnablesKeepAlive()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var raw = new TcpClient();
            var accept = listener.AcceptTcpClientAsync();
            await raw.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var client = await accept;
            using var connection = new TcpConnection(client);
            Assert.NotEqual(0, (int)client.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void ValidTokenFromNewConnectionTakesOverBoundSlot()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        f.Walk(survivor, 1, 0, 3);
        var identity = survivor.Last<S2C_JoinResult>();
        var position = survivor.Self();
        var match = survivor.Last<S2C_MatchStart>();
        var replacement = f.Connect();
        replacement.Send(new C2S_Join { Nickname = "Again", ReconnectToken = identity.ReconnectToken });
        f.Tick();
        var result = replacement.Last<S2C_JoinResult>();
        Assert.Equal(0, result.Result);
        Assert.Equal(identity.PlayerId, result.PlayerId);
        Assert.Equal(identity.ReconnectToken, result.ReconnectToken);
        Assert.Equal(match.Role, replacement.Last<S2C_MatchStart>().Role);
        Assert.True(survivor.Connection.IsClosed);
        Assert.False(killer.Connection.IsClosed);
        var restored = replacement.Last<S2C_WorldState>().Players.Single(p => p.Id == identity.PlayerId);
        Assert.Equal(position.X, restored.X); // unbinding the old connection stops the held direction
        Assert.Equal(position.Z, restored.Z);
        // The new connection controls the slot.
        replacement.Send(new C2S_MoveInput { DirX = 1, Seq = 1 });
        f.Tick();
        Assert.True(replacement.Last<S2C_WorldState>().Players.Single(p => p.Id == identity.PlayerId).X > restored.X);
    }

    [Fact]
    public void ConnectionWithoutJoinIsClosedAfterTenSecondsButPingWorksMeanwhile()
    {
        using var f = new CoreFixture();
        var member = f.Join("Member");
        var idle = f.Connect();
        idle.Send(new C2S_Ping { Timestamp = 42 });
        Assert.Equal(42, idle.Last<S2C_Pong>().Timestamp);
        for (int i = 0; i < 195; i++) f.Tick(); // 9.75 s
        Assert.False(idle.Connection.IsClosed);
        for (int i = 0; i < 10; i++) f.Tick(); // 10.25 s
        Assert.True(idle.Connection.IsClosed);
        Assert.False(member.Connection.IsClosed); // joined connections are never subject to the join timeout
        for (int i = 0; i < 400; i++) f.Tick();
        Assert.False(member.Connection.IsClosed);
    }

    [Fact]
    public void RejectedJoinDoesNotCountAsJoinedForTimeout()
    {
        using var f = new CoreFixture();
        f.Join("Host");
        var rejected = f.Connect();
        rejected.Send(new C2S_Join { ReconnectToken = 12345 });
        f.Tick();
        Assert.NotEqual(0, rejected.Last<S2C_JoinResult>().Result);
        for (int i = 0; i < 205; i++) f.Tick();
        Assert.True(rejected.Connection.IsClosed);
    }

    // ------------------------------------------------------------ fix 6: nickname sanitize

    [Theory]
    [InlineData("‮abc​", "abc")]
    [InlineData("​‍⁦", null)]
    [InlineData("a﻿b­c", "abc")]
    public void NicknameStripsFormatCharacters(string input, string? expected)
    {
        using var f = new CoreFixture();
        var peer = f.Join(input);
        var slot = peer.Last<S2C_LobbyState>().Slots.Single(s => s.PlayerId == peer.Id);
        Assert.Equal(expected ?? "Player " + peer.Id, slot.Nickname);
    }

    [Fact]
    public void NicknameIsCappedInUtf8BytesWithoutSplittingSurrogatePairs()
    {
        // The wire codec already rejects > 48-byte strings, so the cap is exercised on the public sanitizer directly.
        string emoji = string.Concat(Enumerable.Repeat("\U0001F600", 20)); // 80 UTF-8 bytes
        string name = GameServer.SanitizeNickname(emoji, 3);
        Assert.Equal(Protocol.MaxNicknameBytes, Encoding.UTF8.GetByteCount(name));
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", Protocol.MaxNicknameBytes / 4)), name);
        // 1 + 4n bytes: the pair that would cross 48 bytes is dropped whole, never split.
        string mixedName = GameServer.SanitizeNickname("a" + emoji, 3);
        Assert.True(Encoding.UTF8.GetByteCount(mixedName) <= Protocol.MaxNicknameBytes);
        Assert.Equal("a" + string.Concat(Enumerable.Repeat("\U0001F600", 11)), mixedName);
        // No lone surrogate: a lossless UTF-8 round trip proves every pair is intact.
        Assert.Equal(mixedName, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(mixedName)));
        // A lone surrogate is dropped; an over-long name that is only format characters falls back to the default.
        Assert.Equal("ab", GameServer.SanitizeNickname("a\uD800b", 3));
        Assert.Equal("Player 3", GameServer.SanitizeNickname(new string('​', 40), 3));
        // A name that fits the wire limit is accepted end to end.
        using var f = new CoreFixture();
        string fits = "a" + string.Concat(Enumerable.Repeat("\U0001F600", 11)); // 45 bytes
        var peer = f.Join(fits);
        Assert.Equal(fits, peer.Last<S2C_LobbyState>().Slots.Single(s => s.PlayerId == peer.Id).Nickname);
    }

    // ------------------------------------------------------------ fix 8: killer spawn outside every threat band

    [Fact]
    public void KillerSpawnIsAtLeastThreatLevel1DistanceFromEverySurvivorSpawn()
    {
        var map = new FixedMap();
        Assert.Equal(Protocol.MaxPlayers, map.SpawnPoints.Count);
        var killerSpawn = map.SpawnPoints[0];
        for (int i = 1; i < map.SpawnPoints.Count; i++)
        {
            float dx = map.SpawnPoints[i].X - killerSpawn.X, dz = map.SpawnPoints[i].Z - killerSpawn.Z;
            Assert.True(MathF.Sqrt(dx * dx + dz * dz) > GameConstants.ThreatLevel1Distance, "survivor spawn " + i);
        }
    }

    [Fact]
    public void EveryKillerIndexStartsAllSurvivorsAtThreatZero()
    {
        var covered = new HashSet<int>();
        for (int seed = 1; seed < 400 && covered.Count < Protocol.MaxPlayers; seed++)
        {
            using var f = new CoreFixture(seed: seed);
            var peers = new List<Peer>();
            for (int i = 0; i < Protocol.MaxPlayers; i++) peers.Add(f.Join("P" + i));
            peers[0].Send(new C2S_StartMatch()); // host RoleChoice stays Random: any index can be the killer
            f.Tick();
            int killerIndex = peers.FindIndex(p => p.Last<S2C_MatchStart>().Role == (byte)Role.Killer);
            Assert.True(killerIndex >= 0);
            Assert.Single(peers, p => p.Last<S2C_MatchStart>().Role == (byte)Role.Killer);
            if (!covered.Add(killerIndex)) continue;
            foreach (var peer in peers)
            {
                if (peer == peers[killerIndex]) continue;
                Assert.Equal(0, peer.Last<S2C_WorldState>().ThreatLevel);
            }
        }
        Assert.Equal(Protocol.MaxPlayers, covered.Count);
    }

    // ------------------------------------------------------------ helpers

    private static void WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "condition not reached within 5 s");
            Thread.Sleep(10);
        }
    }

    private static async Task<T> TcpReceive<T>(NetworkStream stream, CancellationToken cancellationToken) where T : IPacket
    {
        while (true)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, cancellationToken);
            int length = BitConverter.ToInt32(header, 0);
            Assert.InRange(length, 2, 16384);
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, cancellationToken);
            var packet = PacketCodec.Decode(BitConverter.ToUInt16(body, 0), body.AsSpan(2));
            if (packet is T result) return result;
        }
    }

    // A connection with injectable faults, reachable through the public GameServer.AddConnection API.
    private sealed class FaultyConnection : IConnection
    {
        private readonly bool _throwOnSend, _throwOnRemoteId;
        private IPacket? _pending;
        private int _closed;
        internal FaultyConnection(bool throwOnSend = false, bool throwOnRemoteId = false, IPacket? packetToDeliver = null)
        {
            _throwOnSend = throwOnSend;
            _throwOnRemoteId = throwOnRemoteId;
            _pending = packetToDeliver;
        }
        private readonly string _id = "faulty:" + Guid.NewGuid().ToString("N");
        public string RemoteId => _throwOnRemoteId ? throw new InvalidOperationException("remote id failure") : _id;
        public bool IsClosed => Volatile.Read(ref _closed) != 0;
        public event Action? PacketAvailable { add { } remove { } }
        public event Action? Closed;
        public bool TryReceive(out IPacket packet)
        {
            packet = Interlocked.Exchange(ref _pending, null)!;
            return packet != null;
        }
        public void Send(byte[] frame)
        {
            if (_throwOnSend) throw new InvalidOperationException("send failure");
        }
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) Closed?.Invoke();
        }
        public void Dispose() => Close();
    }
}
