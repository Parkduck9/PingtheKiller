using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class ProtocolV02Tests
{
    public static IEnumerable<object[]> ByteFields()
    {
        var world = new S2C_WorldState
        {
            Players = new() { new PlayerState() }
        };
        // v0.4 offsets: ThreatLevel 8, TerminalsRequired 9, CollapseLeft 10-13, exit count 14, HatchVisible 15,
        // terminal count 16, player count 17, then the player (Role 22, Status 23, Flags 36).
        yield return new object[] { world, 8, new byte[] { 0, 1, 2, 3 } }; // ThreatLevel
        yield return new object[] { world, 22, new byte[] { 0, 1 } }; // Player.Role
        yield return new object[] { world, 23, new byte[] { 0, 1, 2, 3 } }; // Player.Status
        yield return new object[] { world, 36, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray() }; // Flags combinations
        var terminals = new S2C_WorldState { Terminals = new() { new TerminalState() } };
        yield return new object[] { terminals, 26, Enumerable.Range(0, 101).Select(i => (byte)i).ToArray() }; // Progress
        yield return new object[] { terminals, 27, new byte[] { 0, 1, 2, 3 } }; // Terminal flags
        var exits = new S2C_WorldState { Exits = new() { new ExitState() } };
        yield return new object[] { exits, 24, Enumerable.Range(0, 8).Select(i => (byte)i).ToArray() }; // Exit flags
        yield return new object[] { exits, 25, Enumerable.Range(0, 101).Select(i => (byte)i).ToArray() }; // SwitchProgress
        yield return new object[] { new S2C_GameResult(), 0, new byte[] { 0, 1 } }; // WinnerRole
        yield return new object[] { new S2C_GameResult(), 1, new byte[] { 0, 1, 2, 3 } }; // Reason
        yield return new object[] { new S2C_SelfState(), 24, new byte[] { 0, 1, 2, 3 } }; // ActionKind
        yield return new object[] { new S2C_SyncResult(), 4, new byte[] { 0, 1, 2 } }; // Result
        yield return new object[] { new S2C_LobbyState(), 1, new byte[] { 0, 1, 255 } }; // HostRoleChoice
        yield return new object[] { new S2C_MatchStart(), 0, new byte[] { 0, 1 } }; // Role
        yield return new object[] { new C2S_Interact(), 0, new byte[] { 0, 1 } };
        yield return new object[] { new C2S_SetBots(), 0, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 } };
        yield return new object[] { new C2S_SetRole(), 0, new byte[] { 0, 1, 255 } };
    }

    [Theory]
    [MemberData(nameof(ByteFields))]
    public void DecodeAcceptsOnlyDefinedEnumAndBoundedByteValues(IPacket packet, int offset, byte[] allowed)
    {
        byte[] payload = PacketCodec.Encode(packet)[6..];
        for (int value = 0; value <= 255; value++)
        {
            payload[offset] = (byte)value;
            if (allowed.Contains((byte)value))
                Assert.Equal(payload, PacketCodec.Encode(PacketCodec.Decode((ushort)packet.Id, payload))[6..]);
            else
                Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, payload));
        }
    }

    public static IEnumerable<object[]> DefaultPackets() => typeof(IPacket).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(IPacket).IsAssignableFrom(t))
        .Select(t => new object[] { (IPacket)Activator.CreateInstance(t)! });

    [Theory]
    [MemberData(nameof(DefaultPackets))]
    public void EveryDefaultPacketEncodesAndDecodes(IPacket packet)
    {
        byte[] frame = PacketCodec.Encode(packet);
        IPacket decoded = PacketCodec.Decode((ushort)packet.Id, frame.AsSpan(6));
        Assert.Equivalent(packet, decoded, strict: true);
        Assert.Equal(frame, PacketCodec.Encode(decoded));
    }

    [Fact]
    public void DefaultPacketCoverageIncludesEveryPacketId()
    {
        var packets = DefaultPackets().Select(row => (IPacket)row[0]).ToArray();
        int count = Enum.GetValues<PacketId>().Length;
        Assert.Equal(19, count); // v0.3 adds S2C_SelfState (0x0010); v0.4 adds the sync check packets (0x0011-0x0013)
        Assert.Equal(count, packets.Length);
        Assert.Equal(Enumerable.Range(1, count).Select(i => (PacketId)i), packets.Select(p => p.Id).Order());
    }

    [Fact]
    public void DefaultLobbySlotsAreInitializedAndIndependentlyOwned()
    {
        var lobby = new S2C_LobbyState();
        var other = new S2C_LobbyState();
        Assert.Equal(8, lobby.Slots.Length);
        for (int i = 0; i < 8; i++)
        {
            Assert.NotNull(lobby.Slots[i]);
            Assert.Equal(string.Empty, lobby.Slots[i].Nickname);
            Assert.NotSame(lobby.Slots[i], other.Slots[i]);
            for (int j = 0; j < i; j++)
                Assert.NotSame(lobby.Slots[i], lobby.Slots[j]);
        }
        lobby.Slots[0].Nickname = "changed";
        Assert.All(lobby.Slots.Skip(1), slot => Assert.Equal(string.Empty, slot.Nickname));
        Assert.All(other.Slots, slot => Assert.Equal(string.Empty, slot.Nickname));
    }
}
