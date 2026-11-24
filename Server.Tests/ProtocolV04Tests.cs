using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

// Protocol v0.4 (GDD v2): sync check packets, exits, hatch and the eliminated list of the result.
public sealed class ProtocolV04Tests
{
    private static T RoundTrip<T>(T packet) where T : IPacket
    {
        byte[] frame = PacketCodec.Encode(packet);
        var decoded = (T)PacketCodec.Decode((ushort)packet.Id, frame.AsSpan(6));
        Assert.Equal(frame, PacketCodec.Encode(decoded));
        return decoded;
    }

    [Fact]
    public void SyncPacketsRoundTripWithTheirIdsAndExactBytes()
    {
        var check = RoundTrip(new S2C_SyncCheck
        {
            CheckId = 0x01020304, TerminalId = 6, Duration = 1.1f, ZoneStart = 0.5f, ZoneSize = 0.12f, GreatSize = 0.035f
        });
        Assert.Equal((0x01020304u, (byte)6, 1.1f, 0.5f, 0.12f, 0.035f),
            (check.CheckId, check.TerminalId, check.Duration, check.ZoneStart, check.ZoneSize, check.GreatSize));
        Assert.Equal("17000000110004030201" + "06CDCC8C3F0000003F8FC2F53D295C0F3D",
            Convert.ToHexString(PacketCodec.Encode(check)));

        Assert.Equal(77u, RoundTrip(new C2S_SyncPress { CheckId = 77 }).CheckId);
        Assert.Equal("0600000012004D000000", Convert.ToHexString(PacketCodec.Encode(new C2S_SyncPress { CheckId = 77 })));

        var result = RoundTrip(new S2C_SyncResult { CheckId = 9, Result = (byte)SyncResult.Great });
        Assert.Equal((9u, (byte)2), (result.CheckId, result.Result));
        Assert.Equal("0700000013000900000002", Convert.ToHexString(PacketCodec.Encode(result)));
    }

    [Fact]
    public void TruncatedOrOverlongSyncPacketsAreRejected()
    {
        foreach (IPacket packet in new IPacket[] { new S2C_SyncCheck(), new C2S_SyncPress(), new S2C_SyncResult() })
        {
            byte[] payload = PacketCodec.Encode(packet)[6..];
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, payload.AsSpan(0, payload.Length - 1)));
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, payload.Concat(new byte[1]).ToArray()));
        }
    }

    [Fact]
    public void ExitsAndHatchRoundTripAndTheHatchCoordinatesFollowOnlyAVisibleHatch()
    {
        var world = new S2C_WorldState
        {
            CollapseLeft = 87.5f,
            Exits = new()
            {
                new ExitState { Id = 0, X = 4, Z = 4, Flags = (byte)(ExitFlags.Powered | ExitFlags.Open), SwitchProgress = 100 },
                new ExitState { Id = 1, X = 76, Z = 76, Flags = (byte)(ExitFlags.Powered | ExitFlags.Locked), SwitchProgress = 40 }
            },
            HatchVisible = 1, HatchX = 26, HatchZ = 16
        };
        var back = RoundTrip(world);
        Assert.Equivalent(world, back, strict: true);
        int hidden = PacketCodec.Encode(new S2C_WorldState { HatchVisible = 0, HatchX = 26, HatchZ = 16 }).Length;
        int shown = PacketCodec.Encode(new S2C_WorldState { HatchVisible = 1, HatchX = 26, HatchZ = 16 }).Length;
        Assert.Equal(8, shown - hidden);
        var decodedHidden = RoundTrip(new S2C_WorldState { HatchVisible = 0, HatchX = 26, HatchZ = 16 });
        Assert.Equal((0f, 0f), (decodedHidden.HatchX, decodedHidden.HatchZ));
    }

    [Fact]
    public void ExitCountAboveTwoAndHatchVisibleAboveOneAreRejected()
    {
        var three = new S2C_WorldState { Exits = new() { new ExitState(), new ExitState(), new ExitState() } };
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(three));
        // Patch the exit count of a valid two-exit frame to 3 and append a third entry of 11 bytes.
        byte[] payload = PacketCodec.Encode(new S2C_WorldState { Exits = new() { new ExitState(), new ExitState() } })[6..];
        Assert.Equal(2, payload[14]);
        payload[14] = 3;
        byte[] withThird = payload.Take(15 + 22).Concat(new byte[11]).Concat(payload.Skip(15 + 22)).ToArray();
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, withThird));

        byte[] hatch = PacketCodec.Encode(new S2C_WorldState())[6..];
        Assert.Equal(0, hatch[15]);
        hatch[15] = 2;
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, hatch));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { HatchVisible = 2 }));
    }

    [Fact]
    public void EliminatedIdsRoundTripAndMoreThanEightAreRejected()
    {
        var result = RoundTrip(new S2C_GameResult
        {
            WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.Collapse,
            EscapedIds = new() { 3 }, EliminatedIds = new() { 2, 5, 7 }
        });
        Assert.Equal(new[] { 2, 5, 7 }, result.EliminatedIds);
        Assert.Equal(Protocol.MaxEliminatedIds, 8);
        RoundTrip(new S2C_GameResult { EliminatedIds = Enumerable.Range(1, 8).ToList() });
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_GameResult { EliminatedIds = Enumerable.Range(1, 9).ToList() }));
        // WinnerRole, Reason, escaped count 0, eliminated count 9 and nine ids.
        byte[] payload = new byte[] { 0, 0, 0, 9 }.Concat(new byte[9 * 4]).ToArray();
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_GameResult, payload));
        payload[3] = 8;
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_GameResult, payload)); // 4 bytes too many
        Assert.Equal(8, ((S2C_GameResult)PacketCodec.Decode((ushort)PacketId.S2C_GameResult, payload.AsSpan(0, 4 + 32))).EliminatedIds.Count);
    }
}
