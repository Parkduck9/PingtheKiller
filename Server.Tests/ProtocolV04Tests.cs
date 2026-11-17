using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

// Protocol v0.4 (GDD v2): exits, hatch and the eliminated list of the result; 11/17 (R2) packet routing replaced the
// v0.4 sync check packets (0x0011-0x0013 are retired and now unknown ids).
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
    public void HackInputAndTheSelfStateRouteRoundTripWithTheirIdsAndExactBytes()
    {
        Assert.Equal((ushort)0x0019, (ushort)PacketId.C2S_HackInput);
        var input = RoundTrip(new C2S_HackInput { RouteId = 7, Direction = (byte)RouteDirection.Left });
        Assert.Equal(((byte)7, (byte)2), (input.RouteId, input.Direction));
        Assert.Equal("0400000019000702", Convert.ToHexString(PacketCodec.Encode(input)));

        byte dirs = 0;
        dirs = HackRoute.WithDirection(dirs, 0, RouteDirection.Right);
        dirs = HackRoute.WithDirection(dirs, 1, RouteDirection.Up);
        dirs = HackRoute.WithDirection(dirs, 2, RouteDirection.Down);
        dirs = HackRoute.WithDirection(dirs, 3, RouteDirection.Left);
        Assert.Equal(0b10_01_00_11, dirs);
        Assert.Equal(new[] { RouteDirection.Right, RouteDirection.Up, RouteDirection.Down, RouteDirection.Left },
            Enumerable.Range(0, 4).Select(i => HackRoute.DirectionAt(dirs, i)));
        var self = RoundTrip(new S2C_SelfState { RouteId = 200, RouteLength = 4, RouteDirs = dirs, RouteIndex = 2 });
        Assert.Equal(((byte)200, (byte)4, dirs, (byte)2), (self.RouteId, self.RouteLength, self.RouteDirs, self.RouteIndex));
        Assert.Equal("1F000000" + "1000" + "00000000" + "00000000" + "00000000" + "00000000" + "000080BF" + "00000000" + "00" +
            "C8" + "04" + "93" + "02", Convert.ToHexString(PacketCodec.Encode(self)));
        // An index past the length, or a length past HackRoute.MaxLength, never goes on the wire.
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_SelfState { RouteLength = 2, RouteIndex = 3 }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_SelfState { RouteLength = 5 }));
    }

    [Fact]
    public void TruncatedOrOverlongRoutePacketsAndTheRetiredSyncIdsAreRejected()
    {
        foreach (IPacket packet in new IPacket[] { new C2S_HackInput(), new S2C_SelfState() })
        {
            byte[] payload = PacketCodec.Encode(packet)[6..];
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, payload.AsSpan(0, payload.Length - 1)));
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, payload.Concat(new byte[1]).ToArray()));
        }
        foreach (ushort retired in new ushort[] { 0x0011, 0x0012, 0x0013 })
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(retired, new byte[5]));
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
