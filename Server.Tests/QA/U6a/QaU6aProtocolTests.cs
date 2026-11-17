using System;
using System.Collections.Generic;
using System.Linq;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA.U6a;

public sealed class QaU6aProtocolTests
{
    private static T RoundTrip<T>(T packet) where T : IPacket
    {
        byte[] frame = PacketCodec.Encode(packet);
        var decoded = (T)PacketCodec.Decode((ushort)packet.Id, frame.AsSpan(6));
        Assert.Equal(frame, PacketCodec.Encode(decoded));
        return decoded;
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void SelfStateV04PayloadLengthIsExactlyTwentyFiveBytes()
    {
        var state = new S2C_SelfState
        {
            SkillCooldown = 12.0f,
            RescueProgress = 0.5f,
            InvulnerableLeft = 1.2f,
            ExecutionGraceLeft = 0.0f,
            CollapseLeft = 110.5f,
            ActionProgress = 0.75f,
            ActionKind = (byte)ActionKind.Switch
        };

        byte[] frame = PacketCodec.Encode(state);
        // Header (4 bytes) + Id (2 bytes) + Payload (25 bytes, plus the 4 route bytes of 11/17 R2) = 35 bytes
        Assert.Equal(35, frame.Length);

        var back = RoundTrip(state);
        Assert.Equal(state.SkillCooldown, back.SkillCooldown);
        Assert.Equal(state.RescueProgress, back.RescueProgress);
        Assert.Equal(state.InvulnerableLeft, back.InvulnerableLeft);
        Assert.Equal(state.ExecutionGraceLeft, back.ExecutionGraceLeft);
        Assert.Equal(state.CollapseLeft, back.CollapseLeft);
        Assert.Equal(state.ActionProgress, back.ActionProgress);
        Assert.Equal(state.ActionKind, back.ActionKind);
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void SelfStateInvalidActionKindIsRejected()
    {
        var state = new S2C_SelfState { ActionKind = 8 }; // v0.7: 4-6 are Pull, Vault, Break; R3: 7 is Rescue
        // Orchestrator: enum ranges are validated on decode only (codec convention), so encode is not asserted.

        byte[] valid = PacketCodec.Encode(new S2C_SelfState { ActionKind = (byte)ActionKind.HatchClose });
        valid[^1] = 8; // Corrupt ActionKind to 8 (v0.7 defines 4-6, R3 adds 7)
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_SelfState, valid.AsSpan(6)));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    // 11/17 (R2): replaces SyncResultInvalidResultIsRejected (the sync check packets are retired).
    public void HackInputInvalidDirectionIsRejected()
    {
        byte[] valid = PacketCodec.Encode(new C2S_HackInput { RouteId = 1, Direction = (byte)RouteDirection.Right });
        valid[^1] = 4; // Corrupt Direction to 4 (0-3 are Up, Down, Left, Right)
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.C2S_HackInput, valid.AsSpan(6)));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void GameResultInvalidReasonIsRejected()
    {
        var result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = 4 };
        // Orchestrator: enum ranges are validated on decode only (codec convention), so encode is not asserted.

        byte[] valid = PacketCodec.Encode(new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.Collapse });
        valid[7] = 4; // Corrupt Reason byte (offset 6 is payload start: 6=WinnerRole, 7=Reason)
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_GameResult, valid.AsSpan(6)));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void WorldStateExitStateFlagsAboveSevenAreRejected()
    {
        var world = new S2C_WorldState
        {
            Exits = new List<ExitState>
            {
                new ExitState { Id = 0, X = 4, Z = 4, Flags = 8, SwitchProgress = 50 }
            }
        };
        // Orchestrator: enum ranges are validated on decode only (codec convention), so encode is not asserted.

        byte[] valid = PacketCodec.Encode(new S2C_WorldState
        {
            Exits = new List<ExitState>
            {
                new ExitState { Id = 0, X = 4, Z = 4, Flags = 7, SwitchProgress = 50 }
            }
        });
        // In valid frame: Header(4) + Id(2) = 6.
        // Payload: Tick(4) + TimeLeft(4) + Threat(1) + TermReq(1) + CollapseLeft(4) = 14 bytes (offsets 6..19).
        // Exit count is at offset 20 (index 14 of payload): value is 1.
        // ExitState 0: Id(1) at 21, X(4) at 22..25, Z(4) at 26..29, Flags(1) at 30.
        Assert.Equal(7, valid[30]);
        valid[30] = 8;
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, valid.AsSpan(6)));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void WorldStateTerminalFlagsAboveThreeAreRejected()
    {
        var world = new S2C_WorldState
        {
            Terminals = new List<TerminalState>
            {
                new TerminalState { Id = 0, X = 10, Z = 25, Progress = 50, Flags = 4 }
            }
        };
        // Orchestrator: enum ranges are validated on decode only (codec convention), so encode is not asserted.

        byte[] valid = PacketCodec.Encode(new S2C_WorldState
        {
            Terminals = new List<TerminalState>
            {
                new TerminalState { Id = 0, X = 10, Z = 25, Progress = 50, Flags = 3 }
            }
        });
        // Find the Flags byte (it is the last byte of TerminalState, right before players count 0)
        // TerminalState is 11 bytes: Id(1), X(4), Z(4), Progress(1), Flags(1).
        // Flags was set to 3.
        int flagsOffset = Array.LastIndexOf(valid, (byte)3);
        Assert.True(flagsOffset > 0);
        valid[flagsOffset] = 4;
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.S2C_WorldState, valid.AsSpan(6)));
    }

    [Fact]
    [Trait("Category", "QaU6a")]
    public void GameResultEscapedCountAboveEightIsRejected()
    {
        var result = new S2C_GameResult
        {
            WinnerRole = (byte)Role.Survivor,
            Reason = (byte)GameEndReason.AllResolved,
            EscapedIds = Enumerable.Range(1, 9).ToList()
        };
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(result));
    }
}
