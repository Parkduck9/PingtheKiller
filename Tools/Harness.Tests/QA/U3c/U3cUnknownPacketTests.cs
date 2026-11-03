using System.Buffers.Binary;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3c;

internal static class Frames
{
    public static byte[] Raw(int length, ushort id, byte[] payload)
    {
        var frame = new byte[6 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), id);
        payload.CopyTo(frame, 6);
        return frame;
    }

    public static byte[] Unknown(ushort id, int payloadLength = 12) => LobbyServer.UnknownFrame(id, payloadLength, 0x7E);
    public static byte[] Pong(long timestamp) => PacketCodec.Encode(new S2C_Pong { Timestamp = timestamp });
    public static byte[] Header(int length) { var h = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(h, length); return h; }
}

// WireClient receive loop against a raw byte server: unknown Ids are skipped (also fragmented and at the extreme legal
// sizes); a known Id that does not decode and every bad length still fault the stream.
public sealed class U3cWireClientUnknownTests
{
    private static async Task<WireClient> Connect(QaRawServer server, CancellationToken ct) =>
        await WireClient.ConnectAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(3) }, ct);

    [Fact]
    public async Task AnUnknownFrameSentOneByteAtATimeIsSkippedAndTheNextPongIsDelivered()
    {
        await using var server = new QaRawServer(async (s, ct) =>
        {
            foreach (byte b in Frames.Unknown(0x7E).Concat(Frames.Pong(42)))
            {
                await s.WriteAsync(new[] { b }, ct);
                await s.FlushAsync(ct);
            }
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await Connect(server, deadline.Token);
        var frame = await client.ReadAsync<S2C_Pong>(deadline.Token);
        Assert.Equal(42, ((S2C_Pong)frame.Packet).Timestamp);
        Assert.Equal(Frames.Pong(42), frame.Raw);
        Assert.Equal(new Dictionary<ushort, int> { [0x7E] = 1 }, client.UnknownCounts());
    }

    public static TheoryData<ushort, int> UnknownShapes => new()
    {
        { 0x0000, 0 }, { 0x0021, 0 }, { 0x0021, 16 }, { 0x0021, 4 }, { 0x00FF, 1 }, { 0x7E7E, 100 }, { 0xFFFF, 0 },
        { 0x0021, 16384 - 2 }, // the largest legal frame (Length = 16384)
    };

    [Theory]
    [MemberData(nameof(UnknownShapes))]
    public async Task EveryUnknownIdAtEveryLegalSizeIsSkipped(ushort id, int payloadLength)
    {
        await using var server = new QaRawServer(async (s, ct) =>
        {
            await s.WriteAsync(Frames.Unknown(id, payloadLength), ct);
            await s.WriteAsync(Frames.Pong(7), ct);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await Connect(server, deadline.Token);
        var frame = await client.ReadAsync<S2C_Pong>(deadline.Token);
        Assert.Equal(7, ((S2C_Pong)frame.Packet).Timestamp);
        Assert.Equal(new Dictionary<ushort, int> { [id] = 1 }, client.UnknownCounts());
        var kept = Assert.Single(client.UnknownFramesSince(long.MinValue));
        Assert.Equal(id, kept.Id);
        Assert.Equal(6 + payloadLength, kept.Raw.Length);
    }

    public static TheoryData<string> KnownIdDecodeFailures => new() { "pong-truncated", "pong-trailing", "pong-empty", "worldstate-trailing", "lobby-truncated", "joinresult-short" };

    private static byte[] BadKnown(string kind) => kind switch
    {
        "pong-truncated" => Frames.Raw(2 + 3, (ushort)PacketId.S2C_Pong, new byte[3]),
        "pong-trailing" => Frames.Raw(2 + 9, (ushort)PacketId.S2C_Pong, new byte[9]),
        "pong-empty" => Frames.Raw(2, (ushort)PacketId.S2C_Pong, Array.Empty<byte>()),
        "worldstate-trailing" => PacketCodec.Encode(new S2C_WorldState { Tick = 1 }).Concat(new byte[] { 0 }).ToArray(),
        "lobby-truncated" => Frames.Raw(2 + 5, (ushort)PacketId.S2C_LobbyState, new byte[5]),
        _ => Frames.Raw(2 + 2, (ushort)PacketId.S2C_JoinResult, new byte[2])
    };

    [Theory]
    [MemberData(nameof(KnownIdDecodeFailures))]
    public async Task AKnownIdThatDoesNotDecodeStillFaultsTheStreamAfterGoodFramesAreDelivered(string kind)
    {
        await using var server = new QaRawServer(async (s, ct) =>
        {
            var bad = BadKnown(kind);
            if (kind == "worldstate-trailing") BinaryPrimitives.WriteInt32LittleEndian(bad, bad.Length - 4); // keep the frame length valid
            await s.WriteAsync(Frames.Unknown(0x7E).Concat(Frames.Pong(1)).Concat(Frames.Unknown(0x21)).Concat(bad).Concat(Frames.Pong(2)).ToArray(), ct);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await Connect(server, deadline.Token);
        Assert.Equal(1, ((S2C_Pong)(await client.ReadAsync<S2C_Pong>(deadline.Token)).Packet).Timestamp);
        var error = await Assert.ThrowsAsync<IOException>(() => client.ReadAsync(deadline.Token));
        Assert.IsType<PacketFormatException>(error.InnerException);
        Assert.Equal(new Dictionary<ushort, int> { [0x7E] = 1, [0x21] = 1 }, client.UnknownCounts()); // the pong after the fault is never read
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-5)]
    [InlineData(16385)]
    [InlineData(int.MaxValue)]
    public async Task ABadLengthStillFaultsTheStreamEvenAfterUnknownFrames(int length)
    {
        await using var server = new QaRawServer(async (s, ct) =>
        {
            await s.WriteAsync(Frames.Unknown(0x21).Concat(Frames.Header(length)).Concat(Frames.Pong(2)).ToArray(), ct);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await Connect(server, deadline.Token);
        var error = await Assert.ThrowsAsync<IOException>(() => client.ReadAsync(deadline.Token));
        Assert.IsType<PacketFormatException>(error.InnerException);
        Assert.Contains("Invalid frame length", error.InnerException!.Message);
        Assert.Equal(new Dictionary<ushort, int> { [0x21] = 1 }, client.UnknownCounts());
    }

    [Fact]
    public async Task UnknownFrameBodiesAreNeverInterpretedEvenWhenTheyLookLikeAWorldStatePayload()
    {
        // Same payload bytes as a valid WorldState, under the future Id 0x21: skipped, no S2C_WorldState is ever delivered.
        var world = PacketCodec.Encode(new S2C_WorldState { Tick = 9, Players = { new() { Id = 5, X = 1, Z = 2 } } });
        var disguised = world.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(disguised.AsSpan(4), 0x21);
        await using var server = new QaRawServer(async (s, ct) => await s.WriteAsync(disguised.Concat(Frames.Pong(3)).ToArray(), ct));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var client = await Connect(server, deadline.Token);
        Assert.IsType<S2C_Pong>((await client.ReadAsync(deadline.Token)).Packet);
        Assert.Equal(new Dictionary<ushort, int> { [0x21] = 1 }, client.UnknownCounts());
    }

    // LIMIT (informational): the raw store keeps at most MaxKeptUnknownFrames per connection. Reading the frames afterwards
    // fails loudly (never silently) once more were skipped. 20 Hz x 2 packets for a 150 s H6 is ~6000, far below the cap.
    [Fact]
    public async Task Limit_MoreThanTheKeptCapOfUnknownFramesSkipsThemButRefusesTheRawScan()
    {
        const int count = WireClient.MaxKeptUnknownFrames + 500;
        await using var server = new QaRawServer(async (s, ct) =>
        {
            var one = Frames.Unknown(0x21, 4);
            var batch = new byte[one.Length * 1000];
            for (int i = 0; i < 1000; i++) one.CopyTo(batch, i * one.Length);
            for (int i = 0; i < count / 1000 + 1; i++) await s.WriteAsync(batch, ct);
            await s.WriteAsync(Frames.Pong(11), ct);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await Connect(server, deadline.Token);
        Assert.Equal(11, ((S2C_Pong)(await client.ReadAsync<S2C_Pong>(deadline.Token)).Packet).Timestamp);
        Assert.True(client.UnknownCounts()[0x21] >= count);
        Assert.Throws<CheckFailure>(() => client.UnknownFramesSince(long.MinValue));
    }
}

// Whole-harness behaviour with unknown-Id frames interleaved in every stream, in today's Id mode and the U2c anonymous mode.
public abstract class U3cFullRunWithUnknownFrames(bool anonymous)
{
    [Fact]
    public async Task H1ToH6AllPassWithSkipCountsInEveryCheck()
    {
        await using var lobby = new LobbyServer { UnknownFrames = true, AnonymousKillerView = anonymous };
        await using var scenario = new ScenarioServer { UnknownFrames = true, AnonymousKillerView = anonymous, ExecutionGrace = true };
        using var deadline = new CancellationTokenSource(ScenarioCheck.Budget + TimeSpan.FromSeconds(60)); // GDD v2: H6 takes longer
        var report = await HarnessRunner.RunAsync(new Options { Port = lobby.Port, H6Port = scenario.Port, Timeout = TimeSpan.FromSeconds(3),
            Duration = TimeSpan.FromSeconds(2) }, deadline.Token);
        foreach (var item in report.Items) U3cRun.Dump($"{(anonymous ? "anon" : "id")}/full-run-unknown-frames", item);
        Assert.Equal(6, report.Items.Count);
        Assert.All(report.Items, item => Assert.True(item.Passed, $"{item.Id}: {item.Evidence}"));
        Assert.Equal(100, report.Total);
        Assert.True(report.GatesPassed);
        Assert.Contains("id=0x7E ×", report.Items.Single(i => i.Id == "H1").Evidence);
        foreach (var item in report.Items.Where(i => i.Id != "H1"))
        {
            Assert.Contains("skipped unknown packets: ", item.Evidence);
            Assert.Contains("id=0x21 ×", item.Evidence);
            Assert.Contains("id=0x7E ×", item.Evidence);
        }
        Assert.Equal(anonymous, scenario.AnonymousWorldsSent > 0);
    }
}

public sealed class U3cFullRunUnknownFramesIdModeTests() : U3cFullRunWithUnknownFrames(false);
public sealed class U3cFullRunUnknownFramesAnonymousTests() : U3cFullRunWithUnknownFrames(true);

public sealed class U3cUnknownEvidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailingCheckStillReportsTheSkippedUnknownPackets(bool anonymous)
    {
        await using var server = new LobbyServer { UnknownFrames = true, AnonymousKillerView = anonymous, LeakSilentOffset = true };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3" } }, deadline.Token);
        var h3 = Assert.Single(report.Items);
        U3cRun.Dump($"{(anonymous ? "anon" : "id")}/unknown-frames+offset-leak", h3);
        Assert.False(h3.Passed);
        Assert.Contains("leaked in the killer view", h3.Evidence);
        Assert.Contains("skipped unknown packets: id=0x21 ×", h3.Evidence);
        Assert.Contains("id=0x7E ×", h3.Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnknownFrameWithAHeaderLikeBodyDoesNotCountAsAKillerWorld(bool anonymous)
    {
        // The unknown frames of LobbyServer carry 0x7E/0x21 fill bytes only; H3 must report them as raw-scanned unknown frames
        // and still join exactly the real WorldStates.
        await using var server = new LobbyServer { UnknownFrames = true, AnonymousKillerView = anonymous };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3" } }, deadline.Token);
        var h3 = Assert.Single(report.Items);
        U3cRun.Dump($"{(anonymous ? "anon" : "id")}/unknown-frames-H3", h3);
        Assert.True(h3.Passed, h3.Evidence);
        Assert.Matches(@"\(\+\d+ unknown-Id frames\)", h3.Evidence);
        Assert.DoesNotContain("(+0 unknown-Id frames)", h3.Evidence);
    }
}
