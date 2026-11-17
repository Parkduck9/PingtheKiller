using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

// v0.8 (INTEG-R, 11/17): the one version bump for R1 (attack, skill choice, Swinging, Locked), R2 (packet routing, the
// retired sync check) and R3 (Rescue action, GettingUp flag). Golden frames are written out by hand from docs/PROTOCOL.md.
public sealed class ProtocolV08Tests
{
    public static IEnumerable<object[]> Frames()
    {
        yield return new object[] { new C2S_Attack { Seq = 0x01020304U }, new byte[] { 6, 0, 0, 0, 0x17, 0, 4, 3, 2, 1 } };
        yield return new object[] { new C2S_ChooseSkill { SkillId = 0x83 }, new byte[] { 3, 0, 0, 0, 0x18, 0, 0x83 } };
        yield return new object[] { new C2S_HackInput { RouteId = 9, Direction = 3 }, new byte[] { 4, 0, 0, 0, 0x19, 0, 9, 3 } };
        yield return new object[]
        {
            new S2C_SelfState
            {
                SkillCooldown = 1f, RescueProgress = 0.5f, InvulnerableLeft = 0f, ExecutionGraceLeft = 0f, CollapseLeft = -1f,
                ActionProgress = 0.5f, ActionKind = (byte)ActionKind.Rescue,
                RouteId = 9, RouteLength = 4, RouteDirs = 0xE4, RouteIndex = 2
            },
            new byte[]
            {
                31, 0, 0, 0, 0x10, 0,
                0, 0, 0x80, 0x3F, 0, 0, 0, 0x3F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0xBF, 0, 0, 0, 0x3F,
                7, 9, 4, 0xE4, 2
            }
        };
        yield return new object[]
        {
            new S2C_WorldState
            {
                Tick = 0x12345678, TimeLeft = 1f, ThreatLevel = 3, TerminalsRequired = 2,
                Terminals = new() { new() { Id = 7, X = 2f, Z = -1f, Progress = 55, Flags = (byte)TerminalFlags.Locked } },
                Players = new() { new() { Id = -2, Role = 0, Status = 0, X = 1f, Z = -0f, Yaw = -2f, Flags = 63, StatusTimer = 0.5f, CaptureCount = 2, Chase = (byte)ChaseFlags.Swinging } },
                Shutters = new() { 0, 1, 2 }
            },
            new byte[]
            {
                60, 0, 0, 0, 5, 0, 0x78, 0x56, 0x34, 0x12, 0, 0, 0x80, 0x3F, 3, 2, 0, 0, 0x80, 0xBF, 0, 0, 1,
                7, 0, 0, 0, 0x40, 0, 0, 0x80, 0xBF, 55, 4, 1,
                0xFE, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x80, 0, 0, 0, 0xC0, 63,
                0, 0, 0, 0x3F, 2, 16,
                3, 0, 1, 2
            }
        };
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void V08FramesMatchTheirGoldenBytes(IPacket packet, byte[] expected)
    {
        Assert.Equal(expected, PacketCodec.Encode(packet));
        Assert.Equivalent(packet, PacketCodec.Decode(expected[4], expected.AsSpan(6)), strict: true);
    }

    [Fact]
    public void TheVersionIsV08AndTheDocSaysSo()
    {
        Assert.Equal("0.8", Protocol.Version);
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "docs", "PROTOCOL.md"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string first = File.ReadLines(Path.Combine(dir!, "docs", "PROTOCOL.md")).First();
        Assert.StartsWith("# Ping the Killer: wire protocol v" + Protocol.Version, first);
    }

    [Theory]
    [InlineData((ushort)0x0011)]
    [InlineData((ushort)0x0012)]
    [InlineData((ushort)0x0013)]
    [InlineData((ushort)0x001A)]
    public void RetiredAndUnallocatedIdsAreUnknown(ushort id) =>
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, ReadOnlySpan<byte>.Empty));

    [Fact]
    public void TheNewValuesAreTheClampEdges()
    {
        // One past each new maximum is rejected (ActionKind 7, PlayerFlags 63, ChaseFlags 31, TerminalFlags 7, Direction 3).
        byte[] self = PacketCodec.Encode(new S2C_SelfState { ActionKind = 7 });
        self[6 + 24] = 8;
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(self[4], self.AsSpan(6)));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.C2S_HackInput, new byte[] { 1, 4 }));
        var world = new S2C_WorldState
        {
            Terminals = new() { new() { Flags = 7 } },
            Players = new() { new() { Flags = 63, Chase = 31 } }
        };
        byte[] frame = PacketCodec.Encode(world);
        Assert.Equivalent(world, PacketCodec.Decode(frame[4], frame.AsSpan(6)), strict: true);
    }
}
