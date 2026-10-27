using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class GoldenFrameTests
{
    public static IEnumerable<object[]> Frames()
    {
        yield return new object[]
        {
            new S2C_JoinResult { PlayerId = -2, ReconnectToken = 0x0102030405060708UL, Result = 255 },
            new byte[] { 15, 0, 0, 0, 2, 0, 0xFE, 0xFF, 0xFF, 0xFF, 8, 7, 6, 5, 4, 3, 2, 1, 255 }
        };
        yield return new object[]
        {
            new C2S_MoveInput { DirX = 1f, DirZ = -0f, Yaw = -2f, Seq = 0x89ABCDEFU },
            new byte[] { 18, 0, 0, 0, 3, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x80, 0, 0, 0, 0xC0, 0xEF, 0xCD, 0xAB, 0x89 }
        };
        yield return new object[]
        {
            new C2S_UseSkill { SkillId = 9 },
            new byte[] { 3, 0, 0, 0, 4, 0, 9 }
        };
        yield return new object[]
        {
            new S2C_WorldState
            {
                Tick = 0x12345678, TimeLeft = 1f, ExitLocked = 1,
                Players = new() { new() { Id = -2, Role = 1, Status = 2, X = 1f, Z = -0f, Yaw = -2f, Flags = 7 } }
            },
            new byte[]
            {
                31, 0, 0, 0, 5, 0, 0x78, 0x56, 0x34, 0x12, 0, 0, 0x80, 0x3F, 1, 1,
                0xFE, 0xFF, 0xFF, 0xFF, 1, 2, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x80, 0, 0, 0, 0xC0, 7
            }
        };
        yield return new object[]
        {
            new S2C_SkillEffect { CasterId = -2, SkillId = 9, X = 1f, Z = -2f, Duration = 0.5f },
            new byte[] { 19, 0, 0, 0, 6, 0, 0xFE, 0xFF, 0xFF, 0xFF, 9, 0, 0, 0x80, 0x3F, 0, 0, 0, 0xC0, 0, 0, 0, 0x3F }
        };
        yield return new object[]
        {
            new S2C_GameResult { WinnerRole = 1, EscapedIds = new() { -1, 2147483647 } },
            new byte[] { 12, 0, 0, 0, 7, 0, 1, 2, 255, 255, 255, 255, 255, 255, 255, 127 }
        };
        yield return new object[]
        {
            new S2C_Pong { Timestamp = long.MinValue },
            new byte[] { 10, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0, 0, 128 }
        };
        yield return new object[]
        {
            new S2C_LobbyState
            {
                Slots = new Slot[]
                {
                    new() { PlayerId = 1, Nickname = "", IsBot = 0, IsHost = 1 },
                    new() { PlayerId = 2, Nickname = "", IsBot = 1, IsHost = 0 },
                    new() { PlayerId = 3, Nickname = "", IsBot = 1, IsHost = 0 },
                    new() { PlayerId = 4, Nickname = "", IsBot = 1, IsHost = 0 },
                    new() { PlayerId = 5, Nickname = "", IsBot = 1, IsHost = 0 },
                    new() { PlayerId = 6, Nickname = "", IsBot = 1, IsHost = 0 },
                    new() { PlayerId = 7, Nickname = "", IsBot = 1, IsHost = 0 },
                    new() { PlayerId = 8, Nickname = "", IsBot = 1, IsHost = 0 }
                }
            },
            new byte[]
            {
                67, 0, 0, 0, 10, 0, 8,
                1, 0, 0, 0, 0, 0, 0, 1,
                2, 0, 0, 0, 0, 0, 1, 0,
                3, 0, 0, 0, 0, 0, 1, 0,
                4, 0, 0, 0, 0, 0, 1, 0,
                5, 0, 0, 0, 0, 0, 1, 0,
                6, 0, 0, 0, 0, 0, 1, 0,
                7, 0, 0, 0, 0, 0, 1, 0,
                8, 0, 0, 0, 0, 0, 1, 0
            }
        };
        yield return new object[] { new C2S_StartMatch(), new byte[] { 2, 0, 0, 0, 11, 0 } };
        yield return new object[]
        {
            new S2C_MatchStart { Role = 1, SkillId = 4, Seed = -2147483648 },
            new byte[] { 8, 0, 0, 0, 12, 0, 1, 4, 0, 0, 0, 128 }
        };
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void FieldOrderAndIdMatchIndependentGoldenFrame(IPacket packet, byte[] expected)
    {
        Assert.Equal(expected, PacketCodec.Encode(packet));
        Assert.Equivalent(packet, PacketCodec.Decode(expected[4], expected.AsSpan(6)), strict: true);
    }
}
