using System.Buffers.Binary;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class PacketCodecTests
{
    public static IEnumerable<object[]> LiteralPackets()
    {
        yield return new object[] { new C2S_Join { Nickname = "살인마", ReconnectToken = 0xFEDCBA9876543210UL } };
        yield return new object[] { new C2S_Join { Nickname = "", ReconnectToken = 0UL } };
        yield return new object[] { new C2S_Join { Nickname = "가나다라마바사아자차카타파하가나", ReconnectToken = ulong.MaxValue } };
        yield return new object[] { new S2C_JoinResult { PlayerId = -2147483648, ReconnectToken = ulong.MaxValue, Result = 255 } };
        yield return new object[] { new C2S_MoveInput { DirX = float.MaxValue, DirZ = -0f, Yaw = -123.5f, Seq = 4294967295U } };
        yield return new object[] { new C2S_UseSkill { SkillId = 255 } };
        yield return new object[] { new S2C_WorldState { Tick = 1234567890U, TimeLeft = 599.5f, ExitLocked = 1, TerminalsRequired = 4, Players = new(), Terminals = new() } };
        yield return new object[] { new S2C_WorldState
        {
            Tick = uint.MaxValue, TimeLeft = -0f, ThreatLevel = 3, ExitOpen = 1, ExitLocked = 0, TerminalsRequired = 3,
            Terminals = new()
            {
                new() { Id = 0, X = float.MaxValue, Z = -0f, Progress = 0 },
                new() { Id = 1, X = -1.5f, Z = 2f, Progress = 1 },
                new() { Id = 2, X = 3f, Z = 4f, Progress = 25 },
                new() { Id = 3, X = 5f, Z = 6f, Progress = 50 },
                new() { Id = 4, X = 7f, Z = 8f, Progress = 75 },
                new() { Id = 5, X = 9f, Z = 10f, Progress = 98 },
                new() { Id = 6, X = 11f, Z = 12f, Progress = 99 },
                new() { Id = 255, X = 13f, Z = 14f, Progress = 100 }
            },
            Players = new()
            {
                new() { Id = -2147483648, Role = 1, Status = 3, X = float.MaxValue, Z = -0f, Yaw = -180.5f, Flags = 31, StatusTimer = float.MaxValue, CaptureCount = 255 },
                new() { Id = 2147483647, Role = 0, Status = 1, X = -17.25f, Z = 5.5f, Yaw = 359.75f, Flags = 9, StatusTimer = 59.5f, CaptureCount = 1 },
                new() { Id = -3, Role = 0, Status = 0, X = 0f, Z = -4.5f, Yaw = 90f, Flags = 17, StatusTimer = -0f, CaptureCount = 2 },
                new() { Id = 4, Role = 0, Status = 0, X = 1f, Z = 2f, Yaw = 3f, Flags = 4 },
                new() { Id = 5, Role = 0, Status = 1, X = 4f, Z = 5f, Yaw = 6f, Flags = 0 },
                new() { Id = 6, Role = 0, Status = 2, X = 7f, Z = 8f, Yaw = 9f, Flags = 3 },
                new() { Id = 7, Role = 0, Status = 0, X = 10f, Z = 11f, Yaw = 12f, Flags = 5 },
                new() { Id = 8, Role = 0, Status = 0, X = 13f, Z = 14f, Yaw = 15f, Flags = 6 }
            }
        } };
        yield return new object[] { new S2C_SkillEffect { CasterId = -123456789, SkillId = 9, X = -0f, Z = float.MaxValue, Duration = 12.5f } };
        yield return new object[] { new S2C_GameResult { WinnerRole = 1, Reason = 1, EscapedIds = new() } };
        yield return new object[] { new S2C_GameResult { WinnerRole = 1, Reason = 2, EscapedIds = new() } };
        yield return new object[] { new S2C_GameResult { WinnerRole = 0, Reason = 0, EscapedIds = new() { -2147483648, 2147483647, -3, 4, 5, 6, 7, 8 } } };
        yield return new object[] { new C2S_Ping { Timestamp = long.MinValue } };
        yield return new object[] { new S2C_Pong { Timestamp = long.MaxValue } };
        yield return new object[] { new S2C_LobbyState
        {
            IsOffline = 1, HostRoleChoice = 255,
            Slots = new Slot[]
            {
                new() { PlayerId = -1, Nickname = "살인마", IsBot = 0, IsHost = 1 },
                new() { PlayerId = 2147483647, Nickname = "A", IsBot = 1, IsHost = 0 },
                new() { PlayerId = -2147483648, Nickname = "", IsBot = 0, IsHost = 0 },
                new() { PlayerId = 4, Nickname = "B", IsBot = 1, IsHost = 0 },
                new() { PlayerId = 5, Nickname = "C", IsBot = 1, IsHost = 0 },
                new() { PlayerId = 6, Nickname = "D", IsBot = 1, IsHost = 0 },
                new() { PlayerId = 7, Nickname = "E", IsBot = 1, IsHost = 0 },
                new() { PlayerId = 8, Nickname = "가나다라마바사아자차카타파하가나", IsBot = 1, IsHost = 0 }
            }
        } };
        yield return new object[] { new C2S_StartMatch() };
        yield return new object[] { new S2C_MatchStart { Role = 1, SkillId = 4, Seed = -2147483648 } };
        yield return new object[] { new C2S_Interact { Active = 1 } };
        yield return new object[] { new C2S_SetBots { Count = 7 } };
        yield return new object[] { new C2S_SetRole { RoleChoice = 0 } };
        yield return new object[] { new C2S_SetRole { RoleChoice = 1 } };
        yield return new object[] { new C2S_SetRole { RoleChoice = 255 } };
    }

    [Theory]
    [MemberData(nameof(LiteralPackets))]
    public void LiteralFieldsRoundTrip(IPacket expected)
    {
        byte[] frame = PacketCodec.Encode(expected);
        Assert.Equal(frame.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(frame));
        Assert.Equal((ushort)expected.Id, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)));
        IPacket actual = PacketCodec.Decode((ushort)expected.Id, frame.AsSpan(6));
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equivalent(expected, actual, strict: true);
        Assert.Equal(frame, PacketCodec.Encode(actual));
    }

    [Fact]
    public void PingHasSpecifiedGoldenFrame()
    {
        byte[] expected = { 0x0A, 0, 0, 0, 0x08, 0, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 };
        Assert.Equal(expected, PacketCodec.Encode(new C2S_Ping { Timestamp = 0x0102030405060708 }));
        Assert.Equal(0x0102030405060708, Assert.IsType<C2S_Ping>(PacketCodec.Decode(8, expected.AsSpan(6))).Timestamp);
    }

    [Fact]
    public void JoinHasLiteralUtf8LengthAndTokenOrder()
    {
        byte[] expected =
        {
            0x15, 0, 0, 0, 1, 0, 9, 0,
            0xEC, 0x82, 0xB4, 0xEC, 0x9D, 0xB8, 0xEB, 0xA7, 0x88,
            8, 7, 6, 5, 4, 3, 2, 1
        };
        Assert.Equal(expected, PacketCodec.Encode(new C2S_Join { Nickname = "살인마", ReconnectToken = 0x0102030405060708UL }));
    }

    [Fact]
    public void FloatingPointBitsSurviveIncludingNegativeZeroAndNan()
    {
        int[] bits = { unchecked((int)0x80000000), 0x7F7FFFFF, 0x7F800000, unchecked((int)0xFF800000), 0x7FC12345, 1 };
        foreach (int value in bits)
        {
            byte[] frame = PacketCodec.Encode(new C2S_MoveInput { DirX = BitConverter.Int32BitsToSingle(value) });
            var result = Assert.IsType<C2S_MoveInput>(PacketCodec.Decode(3, frame.AsSpan(6)));
            Assert.Equal(value, BitConverter.SingleToInt32Bits(result.DirX));
        }
    }

    [Theory]
    [MemberData(nameof(LiteralPackets))]
    public void EveryPacketRejectsEveryTruncationAndTrailingBytes(IPacket packet)
    {
        byte[] payload = PacketCodec.Encode(packet)[6..];
        for (int length = 0; length < payload.Length; length++)
        {
            int truncatedLength = length;
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, payload.AsSpan(0, truncatedLength)));
        }
        byte[] trailing = new byte[payload.Length + 1];
        payload.CopyTo(trailing, 0);
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)packet.Id, trailing));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(65535)]
    public void UnknownIdIsFormatError(ushort id) =>
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, Array.Empty<byte>()));

    [Theory]
    [InlineData(1, new byte[] { 49, 0 })]
    [InlineData(1, new byte[] { 255, 255 })]
    [InlineData(1, new byte[] { 1, 0, 0xFF })]
    [InlineData(1, new byte[] { 2, 0, 0xC0, 0xAF })]
    [InlineData(1, new byte[] { 3, 0, 0xED, 0xA0, 0x80 })]
    [InlineData(1, new byte[] { 1, 0, 0xE2 })]
    [InlineData(5, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9 })] // Terminals
    [InlineData(5, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9 })] // Players
    [InlineData(7, new byte[] { 0, 0, 9 })]
    [InlineData(10, new byte[] { 0, 0, 0 })]
    [InlineData(10, new byte[] { 0, 0, 7 })]
    [InlineData(10, new byte[] { 0, 0, 9 })]
    [InlineData(10, new byte[] { 0, 0, 8, 0, 0, 0, 0, 49, 0 })]
    [InlineData(10, new byte[] { 0, 0, 8, 0, 0, 0, 0, 1, 0, 0xFF })]
    public void InvalidStringOrCountIsFormatError(ushort id, byte[] payload) =>
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, payload));

    [Fact]
    public void EncodingRejectsOverLimitNullAndInvalidUnicodeFields()
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new C2S_Join { Nickname = new string('a', 49) }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new C2S_Join { Nickname = new string('한', 17) }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new C2S_Join { Nickname = "\uD800" }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new C2S_Join { Nickname = null! }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { Players = new(new PlayerState[9]) }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { Terminals = new(new TerminalState[9]) }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_GameResult { EscapedIds = new(new int[9]) }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_LobbyState { Slots = new Slot[7] }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_LobbyState { Slots = new Slot[9] }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { Players = null! }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_WorldState { Terminals = null! }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_GameResult { EscapedIds = null! }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_LobbyState { Slots = null! }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_LobbyState { Slots = new Slot[8] }));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(11, new byte[Protocol.MaxBodyLength]));
    }

    [Fact]
    public void GameEnumsHaveSpecifiedWireValues()
    {
        Assert.Equal(0, (byte)Role.Survivor);
        Assert.Equal(1, (byte)Role.Killer);
        Assert.Equal(0, (byte)PlayerStatus.Normal);
        Assert.Equal(1, (byte)PlayerStatus.Lagging);
        Assert.Equal(2, (byte)PlayerStatus.Escaped);
        Assert.Equal(3, (byte)PlayerStatus.Eliminated);
        Assert.Equal(0, (byte)PlayerFlags.None);
        Assert.Equal(1, (byte)PlayerFlags.Signal);
        Assert.Equal(2, (byte)PlayerFlags.Afterimage);
        Assert.Equal(4, (byte)PlayerFlags.Stealthed);
        Assert.Equal(8, (byte)PlayerFlags.Hacking);
        Assert.Equal(16, (byte)PlayerFlags.Rescuing);
        Assert.Equal(0, (byte)RoleChoice.Survivor);
        Assert.Equal(1, (byte)RoleChoice.Killer);
        Assert.Equal(255, (byte)RoleChoice.Random);
        Assert.Equal(0, (byte)GameEndReason.SurvivorEscaped);
        Assert.Equal(1, (byte)GameEndReason.AllSurvivorsDown);
        Assert.Equal(2, (byte)GameEndReason.TimeUp);
    }
}
