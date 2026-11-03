using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for unit U1b (protocol v0.2). Every expected value is a hand-written literal derived
// from docs/briefs/U1b-a1.md and docs/GDD.md; frames and payloads are built with the raw byte helpers below,
// never with the code under test (except where the code under test is the thing being exercised).
public sealed class U1bAdversarialTests
{
    // ---------------------------------------------------------------- raw helpers (test-local)

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Le32(int v) => new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };

    private static byte[] Concat(params byte[][] parts)
    {
        var l = new List<byte>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }

    // v0.2 S2C_WorldState payload with one terminal and one player, every enum-ish byte overridable.
    //   v0.4: uint Tick, float TimeLeft, byte ThreatLevel, byte TerminalsRequired, float CollapseLeft,
    //   byte exitCount, { byte Id, float X, float Z, byte Flags, byte SwitchProgress }, byte HatchVisible,
    //   byte terminalCount, { byte Id, float X, float Z, byte Progress, byte Flags }, byte playerCount,
    //   { int Id, byte Role, byte Status, float X, float Z, float Yaw, byte Flags, float StatusTimer, byte CaptureCount }
    private static byte[] World(
        byte threat = 0, byte exitFlags = 0, byte switchProgress = 0, byte terminalsRequired = 2,
        byte progress = 0, byte role = 0, byte status = 0, byte flags = 0, byte terminalFlags = 0)
    {
        return Concat(
            Hex("01000000"),                // Tick = 1
            Hex("0000803F"),                // TimeLeft = 1f
            new[] { threat, terminalsRequired },
            Hex("000080BF"),                // CollapseLeft = -1f
            new byte[] { 1, 0 }, Hex("00008040"), Hex("00008040"), new[] { exitFlags, switchProgress }, // exit 0 at (4,4)
            new byte[] { 0 },               // HatchVisible = 0
            new byte[] { 1 },               // one terminal
            new byte[] { 5 }, Hex("0000803F"), Hex("00000000"), new[] { progress, terminalFlags },
            new byte[] { 1 },               // one player
            Le32(7), new[] { role, status }, Hex("0000803F"), Hex("00000040"), Hex("00000000"),
            new[] { flags }, Hex("00000000"), new byte[] { 0 });
    }

    // v0.2 S2C_GameResult payload with no escaped ids: byte WinnerRole, byte Reason, byte count.
    private static byte[] Result(byte winnerRole, byte reason) => new byte[] { winnerRole, reason, 0, 0 };

    // v0.2 S2C_LobbyState payload with 8 trivial slots: byte IsOffline, byte HostRoleChoice, byte 8, slots.
    private static byte[] Lobby(byte isOffline, byte hostRoleChoice, byte isBot = 0, byte isHost = 0)
    {
        var parts = new List<byte[]> { new[] { isOffline, hostRoleChoice, (byte)8 } };
        for (int i = 0; i < 8; i++)
        {
            parts.Add(Le32(i + 1));                   // PlayerId
            parts.Add(new byte[] { 1, 0, (byte)'a' }); // Nickname "a"
            parts.Add(new[] { isBot, isHost });
        }
        return Concat(parts.ToArray());
    }

    private static void AssertFormatError(ushort id, byte[] payload) =>
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, payload));

    // ---------------------------------------------------------------- 1. WorldState v0.2 golden frame

    // Hand-computed. Payload (48 bytes):
    //   Tick = 0x01020304                -> 04 03 02 01
    //   TimeLeft = 1f (0x3F800000)       -> 00 00 80 3F
    //   ThreatLevel 3, ExitOpen 1, ExitLocked 0, TerminalsRequired 2 -> 03 01 00 02
    //   terminal count 1                 -> 01
    //     Id 5, X 1f, Z 0f, Progress 100 -> 05 | 00 00 80 3F | 00 00 00 00 | 64
    //   player count 1                   -> 01
    //     Id 7                           -> 07 00 00 00
    //     Role 0 (Survivor), Status 1 (Lagging) -> 00 01
    //     X 1f, Z 2f (0x40000000), Yaw -1f (0xBF800000) -> 00 00 80 3F | 00 00 00 40 | 00 00 80 BF
    //     Flags Signal|Hacking = 1|8 = 9 -> 09
    //     StatusTimer 0.5f (0x3F000000)  -> 00 00 00 3F
    //     CaptureCount 1                 -> 01
    // v0.4: TerminalsRequired is followed by CollapseLeft -1f (0xBF800000), no exits, HatchVisible 0; TerminalState
    // gains Flags 0. Body = 2 (id) + 53 = 55 = 0x37. Frame = 4 + 55 = 59 bytes.
    private const string WorldGoldenHex =
        "37000000 0500" +
        "04030201 0000803F 03 02 000080BF 00 00" +
        "01 05 0000803F 00000000 64 00" +
        "01 07000000 00 01 0000803F 00000040 000080BF 09 0000003F 01";

    [Fact]
    public void WorldState_V02_GoldenFrame_Encodes()
    {
        var w = new S2C_WorldState
        {
            Tick = 0x01020304u, TimeLeft = 1f, ThreatLevel = 3, TerminalsRequired = 2
        };
        w.Terminals.Add(new TerminalState { Id = 5, X = 1f, Z = 0f, Progress = 100 });
        w.Players.Add(new PlayerState
        {
            Id = 7, Role = 0, Status = 1, X = 1f, Z = 2f, Yaw = -1f, Flags = 9, StatusTimer = 0.5f, CaptureCount = 1
        });

        byte[] expected = Hex(WorldGoldenHex);
        Assert.Equal(59, expected.Length);
        Assert.Equal(expected, PacketCodec.Encode(w));
    }

    [Fact]
    public void WorldState_V02_GoldenFrame_DecodesThroughAssembler()
    {
        var a = new PacketAssembler();
        a.Append(Hex(WorldGoldenHex));
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> payload));
        Assert.Equal((ushort)5, id);
        Assert.Equal(53, payload.Length);

        var w = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(id, payload.Span));
        Assert.Equal(0x01020304u, w.Tick);
        Assert.Equal(1f, w.TimeLeft);
        Assert.Equal((byte)3, w.ThreatLevel);
        Assert.Equal((byte)2, w.TerminalsRequired);
        Assert.Equal(-1f, w.CollapseLeft);
        Assert.Empty(w.Exits);
        Assert.Equal((byte)0, w.HatchVisible);
        var t = Assert.Single(w.Terminals);
        Assert.Equal((byte)5, t.Id);
        Assert.Equal(1f, t.X);
        Assert.Equal(0f, t.Z);
        Assert.Equal((byte)100, t.Progress);
        var p = Assert.Single(w.Players);
        Assert.Equal(7, p.Id);
        Assert.Equal((byte)0, p.Role);
        Assert.Equal((byte)1, p.Status);
        Assert.Equal(1f, p.X);
        Assert.Equal(2f, p.Z);
        Assert.Equal(-1f, p.Yaw);
        Assert.Equal((byte)9, p.Flags);
        Assert.Equal(0.5f, p.StatusTimer);
        Assert.Equal((byte)1, p.CaptureCount);
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
    }

    // ---------------------------------------------------------------- 2. GameResult golden frame

    // WinnerRole 1 (Killer), Reason 2 (TimeUp), count 2, ids 3 and 258 (0x00000102).
    // The wire layer does not enforce winner/reason/escaped consistency, so this combination is legal bytes.
    // Payload = 01 02 02 | 03 00 00 00 | 02 01 00 00 = 11 bytes; body = 13 = 0x0D.
    // v0.4 appends the EliminatedIds count (0 here).
    private const string ResultGoldenHex = "0E000000 0700 01 02 02 03000000 02010000 00";

    [Fact]
    public void GameResult_TimeUpWithTwoEscapedIds_GoldenFrame_Encodes()
    {
        var g = new S2C_GameResult { WinnerRole = 1, Reason = 2 };
        g.EscapedIds.Add(3);
        g.EscapedIds.Add(258);
        Assert.Equal(Hex(ResultGoldenHex), PacketCodec.Encode(g));
    }

    [Fact]
    public void GameResult_TimeUpWithTwoEscapedIds_GoldenFrame_Decodes()
    {
        byte[] frame = Hex(ResultGoldenHex);
        var a = new PacketAssembler();
        a.Append(frame);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> payload));
        Assert.Equal((ushort)7, id);
        var g = Assert.IsType<S2C_GameResult>(PacketCodec.Decode(id, payload.Span));
        Assert.Equal((byte)1, g.WinnerRole);
        Assert.Equal((byte)2, g.Reason);
        Assert.Equal(new[] { 3, 258 }, g.EscapedIds);
    }

    // ---------------------------------------------------------------- 3. boundary acceptance / rejection

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ThreatLevel_0To3_Accepted(byte level)
    {
        var w = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(0x0005, World(threat: level)));
        Assert.Equal(level, w.ThreatLevel);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(128)]
    [InlineData(255)]
    public void ThreatLevel_Above3_Rejected(byte level) => AssertFormatError(0x0005, World(threat: level));

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void TerminalProgress_0To100_Accepted(byte progress)
    {
        var w = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(0x0005, World(progress: progress)));
        Assert.Equal(progress, Assert.Single(w.Terminals).Progress);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void TerminalProgress_Above100_Rejected(byte progress) => AssertFormatError(0x0005, World(progress: progress));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(7)]
    public void SetBots_0To7_Accepted(byte count)
    {
        var p = Assert.IsType<C2S_SetBots>(PacketCodec.Decode(0x000E, new[] { count }));
        Assert.Equal(count, p.Count);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(128)]
    [InlineData(255)]
    public void SetBots_Above7_Rejected(byte count) => AssertFormatError(0x000E, new[] { count });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void SetRole_0_1_255_Accepted(byte choice)
    {
        var p = Assert.IsType<C2S_SetRole>(PacketCodec.Decode(0x000F, new[] { choice }));
        Assert.Equal(choice, p.RoleChoice);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(127)]
    [InlineData(254)]     // one below 255; 256 is not representable in a byte
    public void SetRole_UndefinedValues_Rejected(byte choice) => AssertFormatError(0x000F, new[] { choice });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Interact_0_1_Accepted(byte active)
    {
        var p = Assert.IsType<C2S_Interact>(PacketCodec.Decode(0x000D, new[] { active }));
        Assert.Equal(active, p.Active);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(128)]
    [InlineData(255)]
    public void Interact_Above1_Rejected(byte active) => AssertFormatError(0x000D, new[] { active });

    [Fact]
    public void Interact_GoldenFrame_FromSpec()
    {
        // Spec acceptance test 2: C2S_Interact { Active = 1 } -> 03 00 00 00 0D 00 01
        Assert.Equal(Hex("03000000 0D00 01"), PacketCodec.Encode(new C2S_Interact { Active = 1 }));
    }

    [Fact]
    public void NewClientPackets_EmptyOrTrailingPayload_Rejected()
    {
        foreach (ushort id in new ushort[] { 0x000D, 0x000E, 0x000F })
        {
            AssertFormatError(id, Array.Empty<byte>());
            AssertFormatError(id, new byte[] { 0, 0 });
        }
    }

    // ---------------------------------------------------------------- 4. every enum-typed byte: undefined value rejected

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(255, false)]
    public void PlayerRole_OnlyDefinedValuesAccepted(byte role, bool accepted)
    {
        if (accepted) Assert.Equal(role, ((S2C_WorldState)PacketCodec.Decode(0x0005, World(role: role))).Players[0].Role);
        else AssertFormatError(0x0005, World(role: role));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(255, false)]
    public void PlayerStatus_OnlyDefinedValuesAccepted(byte status, bool accepted)
    {
        if (accepted) Assert.Equal(status, ((S2C_WorldState)PacketCodec.Decode(0x0005, World(status: status))).Players[0].Status);
        else AssertFormatError(0x0005, World(status: status));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(255, false)]
    public void GameResult_WinnerRole_OnlyDefinedValuesAccepted(byte role, bool accepted)
    {
        if (accepted) Assert.Equal(role, ((S2C_GameResult)PacketCodec.Decode(0x0007, Result(role, 0))).WinnerRole);
        else AssertFormatError(0x0007, Result(role, 0));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)] // v0.4: Collapse
    [InlineData(4, false)]
    [InlineData(255, false)]
    public void GameResult_Reason_OnlyDefinedValuesAccepted(byte reason, bool accepted)
    {
        if (accepted) Assert.Equal(reason, ((S2C_GameResult)PacketCodec.Decode(0x0007, Result(0, reason))).Reason);
        else AssertFormatError(0x0007, Result(0, reason));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(255, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(254, false)]
    public void Lobby_HostRoleChoice_OnlyDefinedValuesAccepted(byte choice, bool accepted)
    {
        if (accepted) Assert.Equal(choice, ((S2C_LobbyState)PacketCodec.Decode(0x000A, Lobby(0, choice))).HostRoleChoice);
        else AssertFormatError(0x000A, Lobby(0, choice));
    }

    [Theory]
    [InlineData(0, 0, 3, true)]
    [InlineData(1, 0, 7, true)]
    [InlineData(2, 0, 3, false)]
    [InlineData(255, 0, 3, false)]
    public void MatchStart_Role_OnlyDefinedValuesAccepted(byte role, byte skill, int seed, bool accepted)
    {
        // payload: byte Role, byte SkillId, int Seed
        byte[] payload = Concat(new[] { role, skill }, Le32(seed));
        if (accepted) Assert.Equal(role, ((S2C_MatchStart)PacketCodec.Decode(0x000C, payload)).Role);
        else AssertFormatError(0x000C, payload);
    }

    // Byte fields the brief does NOT declare as bool/enum on S2C packets (IsOffline, ExitOpen, ExitLocked,
    // Slot.IsBot, Slot.IsHost). The brief only mandates 0/1 validation for C2S_Interact.Active, so these tests
    // assert nothing about acceptance; they only pin that decode never throws anything but PacketFormatException.
    // Observed behaviour is recorded as a finding in docs/qa/U1b-qa-a1.md.
    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void UnspecifiedBoolBytes_NeverThrowAnythingButFormatException(byte value)
    {
        var cases = new (ushort Id, byte[] Payload)[]
        {
            (0x0005, World(exitFlags: value)),
            (0x0005, World(switchProgress: value)),
            (0x0005, World(terminalFlags: value)),
            (0x000A, Lobby(value, 0)),
            (0x000A, Lobby(0, 0, isBot: value)),
            (0x000A, Lobby(0, 0, isHost: value)),
        };
        foreach (var (id, payload) in cases)
        {
            try { PacketCodec.Decode(id, payload); }
            catch (PacketFormatException) { }
        }
    }

    // ---------------------------------------------------------------- 5. PlayerFlags unknown bits

    [Fact]
    public void PlayerFlags_AllCombinationsOfDefinedBits_Accepted()
    {
        // Defined bits: Signal 1, Afterimage 2, Stealthed 4, Hacking 8, Rescuing 16 -> every value 0..31.
        for (byte f = 0; f <= 31; f++)
            Assert.Equal(f, ((S2C_WorldState)PacketCodec.Decode(0x0005, World(flags: f))).Players[0].Flags);
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    public void PlayerFlags_UnknownHighBits_NeverThrowAnythingButFormatException(byte flags)
    {
        // The brief says "every enum-typed byte is validated" but does not say whether [Flags] masks count,
        // so acceptance vs rejection is not asserted; it is reported as a finding.
        try { PacketCodec.Decode(0x0005, World(flags: flags)); }
        catch (PacketFormatException) { }
    }

    // ---------------------------------------------------------------- 6. default-constructed packets round-trip

    private static IPacket[] AllDefaultPackets() => new IPacket[]
    {
        new C2S_Join(), new S2C_JoinResult(), new C2S_MoveInput(), new C2S_UseSkill(), new S2C_WorldState(),
        new S2C_SkillEffect(), new S2C_GameResult(), new C2S_Ping(), new S2C_Pong(), new S2C_LobbyState(),
        new C2S_StartMatch(), new S2C_MatchStart(), new C2S_Interact(), new C2S_SetBots(), new C2S_SetRole(),
    };

    [Fact]
    public void DefaultConstructed_AllFifteenPacketTypes_EncodeDecodeRoundTrip()
    {
        var packets = AllDefaultPackets();
        Assert.Equal(15, packets.Length);
        for (int i = 0; i < packets.Length; i++)
        {
            ushort expectedId = (ushort)(i + 1);   // ids 0x0001..0x000F in table order
            Assert.Equal(expectedId, (ushort)packets[i].Id);

            byte[] frame = null!;
            var enc = Record.Exception(() => frame = PacketCodec.Encode(packets[i]));
            Assert.True(enc == null, $"{packets[i].GetType().Name} encode threw {enc}");

            // Header: Length (LE int32) == frame.Length - 4, then PacketId LE.
            int body = frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24);
            Assert.Equal(frame.Length - 4, body);
            Assert.Equal(expectedId, (ushort)(frame[4] | (frame[5] << 8)));

            IPacket decoded = null!;
            var dec = Record.Exception(() => decoded = PacketCodec.Decode(expectedId, frame.AsSpan(6)));
            Assert.True(dec == null, $"{packets[i].GetType().Name} decode threw {dec}");
            Assert.Equal(packets[i].GetType(), decoded.GetType());
            Assert.Equal(frame, PacketCodec.Encode(decoded));
        }
    }

    [Fact]
    public void DefaultLobbyState_HasEightNonNullSlotsWithEmptyNickname_AndKnownBytes()
    {
        var l = new S2C_LobbyState();
        Assert.Equal(8, l.Slots.Length);
        Assert.All(l.Slots, s => { Assert.NotNull(s); Assert.Equal(string.Empty, s.Nickname); });

        // Hand-built: header, IsOffline 0, HostRoleChoice 0, count 8, then 8 x (int 0, ushort 0, 0, 0) = 8 bytes.
        // Payload = 3 + 8*8 = 67; body = 69 = 0x45.
        byte[] expected = Concat(Hex("45000000 0A00 00 00 08"),
            Concat(Enumerable.Repeat(Hex("00000000 0000 00 00"), 8).ToArray()));
        Assert.Equal(expected, PacketCodec.Encode(l));
    }

    [Fact]
    public void DefaultWorldStateAndGameResult_ListsAreEmptyNotNull_AndEncodeToKnownBytes()
    {
        var w = new S2C_WorldState();
        Assert.NotNull(w.Terminals);
        Assert.NotNull(w.Players);
        // Tick 0, TimeLeft 0f, 4 flag bytes 0, terminal count 0, player count 0 = 4+4+4+1+1 = 14; body 16 = 0x10.
        Assert.Equal(Hex("14000000 0500 00000000 00000000 00 00 000080BF 00 00 00 00"), PacketCodec.Encode(w));

        var g = new S2C_GameResult();
        Assert.NotNull(g.EscapedIds);
        // WinnerRole 0, Reason 0, count 0 -> payload 3; body 5.
        Assert.Equal(Hex("06000000 0700 00 00 00 00"), PacketCodec.Encode(g));
    }

    [Fact]
    public void ExplicitNullLists_EncodeThrowsOnlyPacketFormatException()
    {
        // The brief says this "may still throw PacketFormatException"; no other exception type is acceptable.
        var ex1 = Record.Exception(() => PacketCodec.Encode(new S2C_WorldState { Players = null! }));
        var ex2 = Record.Exception(() => PacketCodec.Encode(new S2C_WorldState { Terminals = null! }));
        var ex3 = Record.Exception(() => PacketCodec.Encode(new S2C_GameResult { EscapedIds = null! }));
        var ex4 = Record.Exception(() => PacketCodec.Encode(new S2C_LobbyState { Slots = null! }));
        foreach (var ex in new[] { ex1, ex2, ex3, ex4 })
            Assert.True(ex == null || ex is PacketFormatException, $"unexpected {ex?.GetType().Name}");
    }

    // ---------------------------------------------------------------- 7. assembler: 10 MB in one call

    [Fact]
    public void Assembler_TenMegabytesInOneAppend_DoesNotThrowAndReadsFirstFrame()
    {
        // Orchestrator ruling (U1b review): U1 spec allows growth up to one frame plus ONE incoming chunk, so a single
        // huge Append is accepted; only undrained growth across calls faults (covered elsewhere). Real transports
        // append socket-sized chunks, so a 10 MB chunk cannot arrive from the network.
        var a = new PacketAssembler();
        byte[] big = new byte[10 * 1024 * 1024];
        // First header: Length = 16384 (0x00004000 LE).
        big[0] = 0x00; big[1] = 0x40; big[2] = 0x00; big[3] = 0x00;

        Assert.Null(Record.Exception(() => a.Append(big)));
        Assert.False(a.IsFaulted);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> p));
        Assert.Equal(0, id);
        Assert.Equal(16384 - 2, p.Length);
    }

    [Fact]
    public void Assembler_TenMegabytesInOneAppend_ZeroFilled_FaultsWithoutThrowing()
    {
        // Header Length = 0 is itself malformed, so TryRead must be Malformed either way; the point of this case
        // is that no exception escapes Append for a huge zero buffer.
        var a = new PacketAssembler();
        var ex = Record.Exception(() => a.Append(new byte[10 * 1024 * 1024]));
        Assert.Null(ex);
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
        Assert.True(a.IsFaulted);
    }

    [Fact]
    public void Assembler_TenMegabytesInOneMegabyteChunksWithoutDraining_FaultsWithoutThrowing()
    {
        var a = new PacketAssembler();
        // An incomplete frame header (Length = 16384, body never completes) keeps the data undrainable.
        byte[] chunk = new byte[1024 * 1024];
        chunk[1] = 0x40;
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 10; i++) a.Append(chunk);
        });
        Assert.Null(ex);
        Assert.True(a.IsFaulted);
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
    }

    // ---------------------------------------------------------------- 8. GameConstants vs GDD

    // docs/GDD.md section 3.4: survivors 1-2 -> 2 required / 4 placed; 3-5 -> 3 / 5; 6-7 -> 4 / 6.
    [Theory]
    [InlineData(1, 3, 5)]
    [InlineData(2, 3, 5)]
    [InlineData(3, 4, 7)]
    [InlineData(4, 4, 7)]
    [InlineData(5, 4, 7)]
    [InlineData(6, 5, 8)]
    [InlineData(7, 5, 8)]
    public void TerminalsFor_MatchesGddTable(int survivors, int required, int placed)
    {
        GameConstants.TerminalsFor(survivors, out int r, out int p);
        Assert.Equal(required, r);
        Assert.Equal(placed, p);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void TerminalsFor_OutOfRange_ThrowsArgumentOutOfRange(int survivors)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameConstants.TerminalsFor(survivors, out _, out _));
    }

    [Fact]
    public void TerminalsFor_PlacedNeverExceedsWireTerminalCap_AndRequiredNeverExceedsPlaced()
    {
        for (int n = 1; n <= 7; n++)
        {
            GameConstants.TerminalsFor(n, out int required, out int placed);
            Assert.InRange(placed, required, Protocol.MaxTerminals);   // wire list cap is 8
        }
    }

    [Fact]
    public void GameConstants_MatchGddNumbers()
    {
        // GDD section 5 / 3.x literals, one assertion per value.
        Assert.Equal(20, GameConstants.TickRate);                         // 5: server tick 20 Hz
        Assert.Equal(4.0f, GameConstants.SurvivorSpeed);                  // 5: 4.0 m/s
        Assert.Equal(4.6f, GameConstants.KillerSpeed);                    // 5: 4.6 m/s
        Assert.Equal(0.25f, GameConstants.LaggingSpeedMultiplier);        // 3.3: 25 %
        Assert.Equal(1.0f, GameConstants.SurvivorSpeed * GameConstants.LaggingSpeedMultiplier); // 3.3: "(1.0 m/s)"
        Assert.Equal(0.15f, GameConstants.SignalHoldSeconds);             // 3.1: 0.15 s
        Assert.Equal(1.2f, GameConstants.CaptureRadius);                  // 3.3: 1.2 m
        Assert.Equal(1.5f, GameConstants.RescueRadius);                   // 3.3: 1.5 m
        Assert.Equal(3.0f, GameConstants.RescueTimeSeconds);              // 3.3: 3 s
        Assert.Equal(60f, GameConstants.LaggingTimeoutSeconds);           // 3.3: 60 s
        Assert.Equal(2f, GameConstants.PostRescueInvulnerabilitySeconds); // 3.3: 2 s
        Assert.Equal(1.5f, GameConstants.TerminalRadius);                 // 3.4: 1.5 m
        Assert.Equal(45f, GameConstants.TerminalHackTimeSeconds);         // GDD v2 2: 45 s
        Assert.Equal(2.4f, GameConstants.CoHackRate(3));                  // GDD v2 2: 3 or more 2.4x
        Assert.Equal(2.0f, GameConstants.ExitRadius);                     // 3.5: 2 m
        Assert.Equal(900f, GameConstants.MatchLengthSeconds);             // GDD v2 1: 900 s
        Assert.Equal(18f, GameConstants.ThreatLevel1Distance);            // 3.2: 18 m
        Assert.Equal(12f, GameConstants.ThreatLevel2Distance);            // 3.2: 12 m
        Assert.Equal(6f, GameConstants.ThreatLevel3Distance);             // 3.2: 6 m
        Assert.Equal(12f, GameConstants.KillerSkillCooldownSeconds);      // 4: 12 s
        Assert.Equal(15f, GameConstants.SurvivorSkillCooldownSeconds);    // 4: 15 s
    }

    [Fact]
    public void ProtocolLimits_MatchSpec()
    {
        Assert.Equal(4, Protocol.HeaderSize);
        Assert.Equal(2, Protocol.IdSize);
        Assert.Equal(16384, Protocol.MaxBodyLength);
        Assert.Equal(48, Protocol.MaxNicknameBytes);
        Assert.Equal(8, Protocol.MaxPlayers);        // GDD 6.1: max 8 players
        Assert.Equal(8, Protocol.MaxTerminals);      // brief section 4: Terminals <= 8
        Assert.Equal(8, Protocol.LobbySlotCount);
        Assert.Equal(8, Protocol.MaxEscapedIds);
    }

    [Fact]
    public void EnumNumericValues_MatchSpec()
    {
        Assert.Equal(new byte[] { 0, 1 }, new[] { (byte)Role.Survivor, (byte)Role.Killer });
        Assert.Equal(new byte[] { 0, 1, 255 }, new[] { (byte)RoleChoice.Survivor, (byte)RoleChoice.Killer, (byte)RoleChoice.Random });
        Assert.Equal(new byte[] { 0, 1, 2, 3 },
            new[] { (byte)PlayerStatus.Normal, (byte)PlayerStatus.Lagging, (byte)PlayerStatus.Escaped, (byte)PlayerStatus.Eliminated });
        Assert.Equal(4, Enum.GetValues<PlayerStatus>().Length);   // "Frozen" removed
        Assert.Equal(new byte[] { 0, 1, 2 },
            new[] { (byte)GameEndReason.AllResolved, (byte)GameEndReason.AllSurvivorsDown, (byte)GameEndReason.TimeUp });
        Assert.Equal(new byte[] { 1, 2, 4, 8, 16 },
            new[] { (byte)PlayerFlags.Signal, (byte)PlayerFlags.Afterimage, (byte)PlayerFlags.Stealthed, (byte)PlayerFlags.Hacking, (byte)PlayerFlags.Rescuing });
    }
}
