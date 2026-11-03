using System.Numerics;
using PingTheKiller.Harness;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class PlannerTests
{
    private static readonly Planner Planner = new();

    private static void AssertWalkable(IReadOnlyList<Vector2> path, IReadOnlyList<Circle> avoid)
    {
        var map = new FixedMap();
        for (int i = 1; i < path.Count; i++)
            for (int k = 0; k <= 50; k++)
            {
                var p = Vector2.Lerp(path[i - 1], path[i], k / 50f);
                Assert.True(map.CanStand(p.X, p.Y), $"segment {i} point {p} is inside a wall");
                if (i > 1 && i < path.Count - 1)
                    Assert.All(avoid, c => Assert.True(Vector2.Distance(p, c.Center) >= c.Radius - 0.5f, $"{p} enters {c}"));
            }
    }

    [Fact]
    public void PathGoesAroundTheWallBetweenTwoTerminals()
    {
        var from = new Vector2(10, 25);
        var to = new Vector2(26, 28);
        var path = Planner.PathTo(from, to, Array.Empty<Circle>());
        Assert.NotNull(path);
        Assert.Equal(from, path![0]);
        Assert.Equal(to, path[^1]);
        Assert.True(Planner.Length(path) > Vector2.Distance(from, to) + 0.5f, "a straight line would cross wall (18,14)-(20,32)");
        AssertWalkable(path, Array.Empty<Circle>());
    }

    [Fact]
    public void PathKeepsOutOfAvoidCircles()
    {
        var avoid = new[] { new Circle(new Vector2(48, 20), 4) };
        var path = Planner.PathTo(new Vector2(42, 20), new Vector2(54.5f, 20), avoid);
        Assert.NotNull(path);
        Assert.True(path!.Count > 2);
        AssertWalkable(path, avoid);
        Assert.True(Planner.Length(path) > 12.5f + 1, "the straight line crosses the circle");
    }

    [Fact]
    public void PathToNearestFindsTheCheapestPointSatisfyingTheGoal()
    {
        var a = new Vector2(26, 28);
        var path = Planner.PathToNearest(a, p => Vector2.Distance(p, a) >= 9, Array.Empty<Circle>());
        Assert.NotNull(path);
        Assert.InRange(Vector2.Distance(path![^1], a), 9, 9.8f);
        AssertWalkable(path, Array.Empty<Circle>());
    }
}

public sealed class KillerLeakScanTests
{
    private const int A = 2, B = 3, KillerId = 1;
    private static readonly float KillerX = 12.345678f, KillerZ = 40.98765f;
    private static readonly HashSet<int> Killer = new() { KillerLeakScan.Bits(KillerX), KillerLeakScan.Bits(KillerZ) };

    private static ReceivedFrame Frame(IPacket packet) => new(packet, PacketCodec.Encode(packet), 0);

    private static S2C_WorldState World(params PlayerState[] players) => new()
    {
        Tick = 7, TimeLeft = 500, Players = players.ToList(),
        Terminals = new List<TerminalState> { new() { Id = 0, X = 10, Z = 25 } }
    };

    [Fact]
    public void CleanFrameAndExactCoincidencesWithLegitimateFieldsPass()
    {
        Assert.Null(KillerLeakScan.Excess(Frame(World(new PlayerState { Id = A, X = 1, Z = 2 })), Killer, new[] { A, B }));
        int explained = 0;
        // A survivor standing exactly at a killer coordinate, a Yaw/StatusTimer/TimeLeft/terminal equal to one.
        var world = World(new PlayerState { Id = A, X = KillerX, Z = 3 }, new PlayerState { Id = B, Yaw = KillerZ, StatusTimer = KillerX });
        world.TimeLeft = KillerZ;
        world.Terminals.Add(new TerminalState { Id = 1, X = KillerX, Z = KillerZ });
        Assert.Null(KillerLeakScan.Excess(Frame(world), Killer, new[] { A, B }, n => explained += n));
        Assert.Equal(6, explained);
        Assert.Null(KillerLeakScan.Excess(Frame(new S2C_SkillEffect { CasterId = B, X = KillerX, Z = KillerZ }), Killer, new[] { A, B }));
    }

    [Fact]
    public void ExtraCopiesFail()
    {
        // Killer-cast effect carrying killer X/Z (survivor-bound SkillEffect must use exit or 0,0).
        Assert.NotNull(KillerLeakScan.Excess(Frame(new S2C_SkillEffect { CasterId = KillerId, X = KillerX, Z = KillerZ }), Killer, new[] { A, B }));
        // An entry with an unknown id is not a legitimate holder, even if shaped like a survivor.
        Assert.NotNull(KillerLeakScan.Excess(Frame(World(new PlayerState { Id = 99, X = KillerX })), Killer, new[] { A, B }));
        // Two copies but only one legitimate field: A stands at killer X, and an Id field carries the same bits.
        var world = World(new PlayerState { Id = A, X = KillerX }, new PlayerState { Id = KillerLeakScan.Bits(KillerX), X = 1 });
        var error = KillerLeakScan.Excess(Frame(world), Killer, new[] { A, B });
        Assert.NotNull(error);
        Assert.Contains("2 occurrence(s)", error);
        Assert.Contains("only 1 legitimate", error);
        // Integer, 8-byte and string fields of other packets.
        Assert.NotNull(KillerLeakScan.Excess(Frame(new S2C_GameResult { EscapedIds = new List<int> { KillerLeakScan.Bits(KillerZ) } }), Killer, new[] { A, B }));
        long packed = ((long)KillerLeakScan.Bits(KillerZ) << 32) | (uint)KillerLeakScan.Bits(KillerX);
        Assert.Contains("S2C_Pong", KillerLeakScan.Excess(Frame(new S2C_Pong { Timestamp = packed }), Killer, new[] { A, B }));
        var lobby = new S2C_LobbyState();
        float ascii = BitConverter.Int32BitsToSingle(0x41424344); // 12.14..., little-endian bytes "DCBA"
        lobby.Slots[3] = new Slot { PlayerId = 5, Nickname = "xDCBA" };
        Assert.Contains("S2C_LobbyState", KillerLeakScan.Excess(Frame(lobby), new HashSet<int> { KillerLeakScan.Bits(ascii) }, new[] { A, B }));
    }

    [Fact]
    public void TamperedOrPaddedRawBytesFailAsUnexplained()
    {
        var world = World(new PlayerState { Id = A, X = 3 });
        var raw = PacketCodec.Encode(world);
        Assert.Null(KillerLeakScan.Excess(new ReceivedFrame(world, raw, 0), Killer, new[] { A, B }));
        Measurements.FloatBytes(KillerX).CopyTo(raw, raw.Length - 5); // bytes no longer match the decoded packet
        Assert.Contains("unexplained bytes", KillerLeakScan.Excess(new ReceivedFrame(world, raw, 0), Killer, new[] { A, B }));
    }

    // F3 regression: zero Role/Status bytes plus the low bytes of a survivor X form 00 00 A0 40 (= 5.0f), a
    // killer spawn coordinate. No field holds 5.0, so a structural scan must not flag it.
    [Fact]
    public void WindowsAcrossFieldBoundariesAreNotLeaks()
    {
        var world = World(new PlayerState { Id = A, X = BitConverter.Int32BitsToSingle(0x412040A0), Z = 30.684925f });
        Assert.Contains(new byte[] { 0, 0, 0xA0, 0x40 }, Enumerable.Range(0, PacketCodec.Encode(world).Length - 3)
            .Select(i => PacketCodec.Encode(world).Skip(i).Take(4).ToArray()), new ByteArrayComparer());
        Assert.Null(KillerLeakScan.Excess(Frame(world), new HashSet<int> { KillerLeakScan.Bits(5f) }, new[] { A, B }));
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x!.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Length;
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    public void ExtraRawBytesInAnyPacketFail(int offset)
    {
        var raw = new byte[20];
        Measurements.FloatBytes(KillerZ).CopyTo(raw, offset);
        var error = KillerLeakScan.Excess(new ReceivedFrame(new S2C_Pong(), raw, 0), Killer, new[] { A, B });
        Assert.NotNull(error);
        Assert.Contains("S2C_Pong", error);
    }
}

public sealed class QaFakeRegressionTests
{
    // Flaky-test root cause (U3b): QaServer's naive-framing fault decoded a garbage "frame" from a read that
    // began mid-frame; the PacketFormatException faulted the connection task and DisposeAsync rethrew it.
    // This sends such a frame deterministically and requires the fake to keep serving (and dispose cleanly).
    [Fact]
    public async Task NaiveFramingFakeSurvivesAGarbageFrameThatFailsToDecode()
    {
        await using var server = new QA.QaServer(new QA.QaFaults { NaiveFraming = true });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await WireClient.ConnectAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(5) }, deadline.Token);
        var garbage = new byte[10];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(garbage, 6);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(garbage.AsSpan(4), (ushort)PacketId.S2C_LobbyState);
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_LobbyState, garbage.AsSpan(6)));
        await client.SendRawAsync(garbage, deadline.Token);
        // A ping coalesced into the garbage read is discarded by the naive server; a later one is answered.
        for (long attempt = 0; ; attempt++)
        {
            await client.SendAsync(new C2S_Ping { Timestamp = attempt }, deadline.Token);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            wait.CancelAfter(TimeSpan.FromMilliseconds(200));
            try
            {
                await client.ReadAsync<S2C_Pong>(wait.Token);
                break;
            }
            catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
        }
        Assert.False(client.ServerDisconnected);
    }
}
