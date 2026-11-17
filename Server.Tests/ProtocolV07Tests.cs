using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

// Protocol v0.7 (GDD v2 section 8, unit Q5a): shutter states in S2C_WorldState, PlayerState.Chase flags,
// ActionKind Pull/Vault/Break and the shared WorldChaseProps geometry.
public sealed class ProtocolV07Tests
{
    private static T RoundTrip<T>(T packet) where T : IPacket
    {
        byte[] frame = PacketCodec.Encode(packet);
        var decoded = (T)PacketCodec.Decode((ushort)packet.Id, frame.AsSpan(6));
        Assert.Equal(frame, PacketCodec.Encode(decoded));
        return decoded;
    }

    [Fact]
    public void ShutterStatesAndChaseFlagsRoundTrip()
    {
        var world = new S2C_WorldState
        {
            Players = new()
            {
                new PlayerState { Id = 1, Chase = (byte)ChaseFlags.Pulling },
                new PlayerState { Id = 2, Role = 1, Chase = (byte)(ChaseFlags.Stunned | ChaseFlags.Breaking) },
                new PlayerState { Id = 3, Chase = (byte)ChaseFlags.Vaulting }
            },
            Shutters = new() { 0, 1, 2, 0, 0, 1, 2, 2, 1, 0 }
        };
        var back = RoundTrip(world);
        Assert.Equal(world.Shutters, back.Shutters);
        Assert.Equal(new byte[] { 1, 12, 2 }, back.Players.Select(p => p.Chase));
        Assert.True(WorldChaseProps.Has(back.Players[1], ChaseFlags.Stunned));
        Assert.False(WorldChaseProps.Has(back.Players[0], ChaseFlags.Vaulting));
        Assert.Equal(ShutterState.Broken, WorldChaseProps.ShutterOf(back, 2));
        Assert.Equal(ShutterState.Up, WorldChaseProps.ShutterOf(back, 99));
        Assert.Equal(ShutterState.Up, WorldChaseProps.ShutterOf(new S2C_WorldState(), 0));
    }

    [Fact]
    public void ShutterListIsTheLastFieldAndCostsOneBytePerShutter()
    {
        int empty = PacketCodec.Encode(new S2C_WorldState()).Length;
        int full = PacketCodec.Encode(new S2C_WorldState { Shutters = new(new byte[Protocol.MaxShutters]) }).Length;
        Assert.Equal(Protocol.MaxShutters, full - empty);
        byte[] payload = PacketCodec.Encode(new S2C_WorldState { Shutters = new() { 1, 2 } })[6..];
        Assert.Equal(new byte[] { 2, 1, 2 }, payload[^3..]);
    }

    [Fact]
    public void TooManyShuttersUndefinedStatesAndTruncationAreRejected()
    {
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Encode(new S2C_WorldState { Shutters = new(new byte[Protocol.MaxShutters + 1]) }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { Shutters = new() { 3 } }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { Shutters = null! }));

        byte[] payload = PacketCodec.Encode(new S2C_WorldState { Shutters = new(new byte[Protocol.MaxShutters]) })[6..];
        int countAt = payload.Length - Protocol.MaxShutters - 1;
        payload[countAt] = (byte)(Protocol.MaxShutters + 1);
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, payload));

        byte[] two = PacketCodec.Encode(new S2C_WorldState { Shutters = new() { 1, 1 } })[6..];
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, two.AsSpan(0, two.Length - 1)));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, two.Concat(new byte[1]).ToArray()));
    }

    [Fact]
    public void SelfStateCarriesTheNewActionKinds()
    {
        foreach (ActionKind kind in new[] { ActionKind.Pull, ActionKind.Vault, ActionKind.Break })
        {
            var back = RoundTrip(new S2C_SelfState { ActionKind = (byte)kind, ActionProgress = 0.5f });
            Assert.Equal((byte)kind, back.ActionKind);
        }
    }

    [Fact]
    public void ChasePropGeometryIsThinIndexAlignedAndWithinTheWireLimit()
    {
        Assert.Equal(WorldChaseProps.ShutterCount, WorldChaseProps.Shutters.Count);
        Assert.Equal(WorldChaseProps.RailCount, WorldChaseProps.Rails.Count);
        Assert.True(WorldChaseProps.ShutterCount <= Protocol.MaxShutters);
        for (int i = 0; i < WorldChaseProps.Shutters.Count; i++)
        {
            ChaseProp s = WorldChaseProps.Shutters[i];
            Assert.Equal(i, s.Id);
            Assert.InRange(s.HalfThickness * 2, 0.29f, 0.31f);
            Assert.InRange(s.HalfLength * 2, 2.9f, 4.1f); // alleys are 3-4 m wide
        }
        for (int i = 0; i < WorldChaseProps.Rails.Count; i++)
        {
            ChaseProp r = WorldChaseProps.Rails[i];
            Assert.Equal(i, r.Id);
            Assert.InRange(r.HalfThickness * 2, 0.29f, 0.31f);
            Assert.InRange(r.HalfLength * 2, 2.99f, 3.01f);
            Assert.Equal(1f, Math.Abs(r.CrossX) + Math.Abs(r.CrossZ));
        }
    }

    [Fact]
    public void ChasePropDistanceSideAndCircleTests()
    {
        var prop = new ChaseProp(0, 10f, 4.85f, 13f, 5.15f); // along X, crossed along Z
        Assert.True(prop.AlongX);
        Assert.Equal((0f, 1f), (prop.CrossX, prop.CrossZ));
        Assert.Equal(0f, prop.Distance(11f, 5f));
        Assert.Equal(1f, prop.Distance(11f, 6.15f), 4);
        Assert.True(prop.Side(11f, 6f) > 0 && prop.Side(11f, 4f) < 0);
        Assert.True(prop.IntersectsCircle(11f, 5.5f, 0.4f));
        Assert.False(prop.IntersectsCircle(11f, 5.55f, 0.4f)); // touching does not intersect
    }
}
