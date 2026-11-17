using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for unit U1. Every expected value is a hand-written literal derived from
// docs/briefs/U1-a1.md; frames and payloads are built here with raw byte helpers, never with the
// code under test (except where the code under test is the thing being exercised).
public sealed class U1AdversarialTests
{
    // ---------------------------------------------------------------- raw helpers (test-local)

    private static byte[] Frame(ushort id, byte[] payload)
    {
        int body = 2 + payload.Length;
        var f = new byte[4 + body];
        f[0] = (byte)body;
        f[1] = (byte)(body >> 8);
        f[2] = (byte)(body >> 16);
        f[3] = (byte)(body >> 24);
        f[4] = (byte)id;
        f[5] = (byte)(id >> 8);
        payload.CopyTo(f, 6);
        return f;
    }

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Le32(int v) => new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };

    private static byte[] Concat(params byte[][] parts)
    {
        var l = new List<byte>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }

    private static byte[] Pattern(int seed, int length)
    {
        var b = new byte[length];
        for (int i = 0; i < length; i++) b[i] = (byte)((seed * 31 + i * 7 + (i >> 8)) & 0xFF);
        return b;
    }

    // C2S_Ping { Timestamp = 0x0102030405060708 } -- spec golden frame.
    private static readonly byte[] PingFrame = Hex("0A000000 0800 0807060504030201");

    // S2C_Pong { Timestamp = 0x1111111111111111 }.
    private static readonly byte[] PongFrame = Hex("0A000000 0900 1111111111111111");

    private static AssembleResult Drain(PacketAssembler a, List<(ushort Id, byte[] Payload)> into)
    {
        while (true)
        {
            var r = a.TryRead(out ushort id, out ReadOnlyMemory<byte> p);
            if (r != AssembleResult.Packet) return r;
            into.Add((id, p.ToArray()));
        }
    }

    // C2S_Join payload = ushort byteLen, utf8 bytes, ulong token 0x1122334455667788 (LE: 88 77 66 55 44 33 22 11).
    private static byte[] JoinPayload(byte[] nickBytes, int? declaredLength = null)
    {
        int len = declaredLength ?? nickBytes.Length;
        return Concat(new[] { (byte)len, (byte)(len >> 8) }, nickBytes, Hex("8877665544332211"));
    }

    private const ulong JoinToken = 0x1122334455667788UL;

    private static C2S_Join DecodeJoin(byte[] payload) => (C2S_Join)PacketCodec.Decode(0x0001, payload);

    // ---------------------------------------------------------------- 1. header split across chunks

    [Fact]
    public void Header_FedOneByteAtATime_ReturnsNeedMoreDataUntilWholeFrameArrives()
    {
        var a = new PacketAssembler();
        // Whole frame: 0A 00 00 00 | 08 00 | 08 07 06 05 04 03 02 01  (14 bytes)
        for (int i = 0; i < 13; i++)
        {
            a.Append(new[] { PingFrame[i] });
            Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
            Assert.False(a.IsFaulted);
        }
        a.Append(new[] { PingFrame[13] });
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> payload));
        Assert.Equal((ushort)8, id);
        Assert.Equal(Hex("0807060504030201"), payload.ToArray());
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
    }

    [Fact]
    public void Header_ThreeOfFourLengthBytes_ThenFourth_StillNeedsBody()
    {
        var a = new PacketAssembler();
        a.Append(Hex("0A"));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(Hex("00"));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(Hex("00"));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(Hex("00"));            // header complete, no body yet
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        Assert.False(a.IsFaulted);
    }

    [Fact]
    public void MinimumLengthTwo_EmptyPayloadPacket_IsAccepted()
    {
        var a = new PacketAssembler();
        a.Append(Hex("02000000 0B00")); // C2S_StartMatch, Length = 2
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> payload));
        Assert.Equal((ushort)0x0B, id);
        Assert.Equal(0, payload.Length);
        Assert.IsType<C2S_StartMatch>(PacketCodec.Decode(id, payload.Span));
    }

    // ---------------------------------------------------------------- 2. exact buffer boundaries (4096 / 8192)

    public static IEnumerable<object[]> BoundaryPlans()
    {
        // Frame sizes in bytes (4 length + 2 id + payload). Every plan sums to 8192.
        yield return new object[] { new[] { 4096, 4096 } };
        yield return new object[] { new[] { 1000, 3000, 96, 4096 } };
        yield return new object[] { new[] { 4095, 4097 } };          // boundary 1 byte into next header
        yield return new object[] { new[] { 4097, 4095 } };          // frame straddles the 4096 boundary
        yield return new object[] { new[] { 4092, 6, 4094 } };       // 6-byte empty-payload frame sits on boundary
        yield return new object[] { new[] { 6, 4090, 100, 3996 } };
    }

    [Theory]
    [MemberData(nameof(BoundaryPlans))]
    public void StreamEndingExactlyOn4096And8192_DeliversEveryFrameIntact(int[] sizes)
    {
        var frames = new List<byte[]>();
        for (int i = 0; i < sizes.Length; i++)
            frames.Add(Frame((ushort)(0x7000 + i), Pattern(i + 1, sizes[i] - 6)));
        byte[] stream = Concat(frames.ToArray());
        Assert.Equal(8192, stream.Length);

        // Mode 1: chunks cut exactly at 4096 and 8192, draining after each.
        {
            var a = new PacketAssembler();
            var got = new List<(ushort Id, byte[] Payload)>();
            a.Append(stream.AsSpan(0, 4096));
            Assert.Equal(AssembleResult.NeedMoreData, Drain(a, got));
            int completeAfterFirst = 0, cum = 0;
            foreach (int s in sizes) { cum += s; if (cum <= 4096) completeAfterFirst++; }
            Assert.Equal(completeAfterFirst, got.Count);
            a.Append(stream.AsSpan(4096, 4096));
            Assert.Equal(AssembleResult.NeedMoreData, Drain(a, got));
            Assert.Equal(sizes.Length, got.Count);
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.Equal((ushort)(0x7000 + i), got[i].Id);
                Assert.Equal(Pattern(i + 1, sizes[i] - 6), got[i].Payload);
            }
            Assert.False(a.IsFaulted);
        }

        // Mode 2: the whole 8192 bytes in a single Append (forces buffer growth past 4096).
        {
            var a = new PacketAssembler();
            var got = new List<(ushort Id, byte[] Payload)>();
            a.Append(stream);
            Assert.Equal(AssembleResult.NeedMoreData, Drain(a, got));
            Assert.Equal(sizes.Length, got.Count);
            for (int i = 0; i < sizes.Length; i++)
                Assert.Equal(Pattern(i + 1, sizes[i] - 6), got[i].Payload);
        }

        // Mode 3: 4095/4097 byte chunks (cuts one byte off the boundary on each side).
        {
            var a = new PacketAssembler();
            var got = new List<(ushort Id, byte[] Payload)>();
            a.Append(stream.AsSpan(0, 4095));
            Drain(a, got);
            a.Append(stream.AsSpan(4095, 4097));
            Assert.Equal(AssembleResult.NeedMoreData, Drain(a, got));
            Assert.Equal(sizes.Length, got.Count);
            for (int i = 0; i < sizes.Length; i++)
                Assert.Equal(Pattern(i + 1, sizes[i] - 6), got[i].Payload);
        }
    }

    // ---------------------------------------------------------------- 3. max body in 3 chunks + trailing small packet

    [Fact]
    public void MaxBodyFrame_InThreeChunks_ThenSmallPacketInSameChunkAsTail()
    {
        byte[] payload = Pattern(99, 16382);               // body = 2 + 16382 = 16384 = MaxBodyLength
        byte[] big = Frame(0x7777, payload);
        Assert.Equal(16388, big.Length);
        Assert.Equal(Hex("00400000"), big[..4]);            // 16384 LE = 00 40 00 00

        var a = new PacketAssembler();
        a.Append(big.AsSpan(0, 6000));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(big.AsSpan(6000, 6000));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(Concat(big[12000..], PingFrame));

        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id1, out ReadOnlyMemory<byte> p1));
        Assert.Equal((ushort)0x7777, id1);
        Assert.Equal(payload, p1.ToArray());

        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id2, out ReadOnlyMemory<byte> p2));
        Assert.Equal((ushort)8, id2);
        var ping = Assert.IsType<C2S_Ping>(PacketCodec.Decode(id2, p2.Span));
        Assert.Equal(0x0102030405060708L, ping.Timestamp);

        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        Assert.False(a.IsFaulted);
    }

    [Fact]
    public void MaxBodyFrame_DeliveredByteByByteDripTail_IsAccepted()
    {
        byte[] payload = Pattern(5, 16382);
        byte[] big = Frame(0x7778, payload);
        var a = new PacketAssembler();
        a.Append(big.AsSpan(0, 16380));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        for (int i = 16380; i < 16387; i++)
        {
            a.Append(big.AsSpan(i, 1));
            Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        }
        a.Append(big.AsSpan(16387, 1));
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> p));
        Assert.Equal((ushort)0x7778, id);
        Assert.Equal(payload, p.ToArray());
    }

    // ---------------------------------------------------------------- 4. faulted assembler stays faulted

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(16385)]
    public void Faulted_AfterMalformedHeader_ValidFrameStillYieldsMalformed(int badLength)
    {
        var a = new PacketAssembler();
        a.Append(Le32(badLength));
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
        Assert.True(a.IsFaulted);

        a.Append(PingFrame);
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out ushort id, out ReadOnlyMemory<byte> p));
        Assert.Equal(0, id);
        Assert.Equal(0, p.Length);
        Assert.True(a.IsFaulted);
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
    }

    [Fact]
    public void Faulted_AfterValidPacketThenBadHeader_DoesNotResurrect()
    {
        var a = new PacketAssembler();
        a.Append(Concat(PingFrame, Le32(0), PongFrame));
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out _));
        Assert.Equal((ushort)8, id);
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
        Assert.True(a.IsFaulted);
        // The pong that follows in the same buffer must never be released.
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
        a.Append(PingFrame);
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
    }

    [Fact]
    public void MalformedDetected_EvenWhenOnlyHeaderBytesArrivedOneByOne()
    {
        var a = new PacketAssembler();
        byte[] h = Le32(1);
        a.Append(h.AsSpan(0, 1));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(h.AsSpan(1, 1));
        a.Append(h.AsSpan(2, 1));
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(h.AsSpan(3, 1));
        Assert.Equal(AssembleResult.Malformed, a.TryRead(out _, out _));
        Assert.True(a.IsFaulted);
    }

    // ---------------------------------------------------------------- 5. nickname edge cases

    [Fact]
    public void Nickname_Exactly48Utf8Bytes_IsAccepted()
    {
        var p = DecodeJoin(JoinPayload(Enumerable.Repeat((byte)'a', 48).ToArray()));
        Assert.Equal(new string('a', 48), p.Nickname);
        Assert.Equal(JoinToken, p.ReconnectToken);
    }

    [Fact]
    public void Nickname_49Utf8Bytes_IsRejectedOnDecode()
    {
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Decode(0x0001, JoinPayload(Enumerable.Repeat((byte)'a', 49).ToArray())));
    }

    [Fact]
    public void Nickname_DeclaredLength0xFFFF_IsRejected()
    {
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Decode(0x0001, JoinPayload(new byte[10], declaredLength: 0xFFFF)));
    }

    [Fact]
    public void Nickname_DeclaredWithinLimitButBytesMissing_IsRejected()
    {
        // declares 48 bytes, only 10 + token(8) bytes follow -> 48 > 18 remaining
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Decode(0x0001, JoinPayload(new byte[10], declaredLength: 48)));
    }

    [Fact]
    public void Nickname_Korean16Chars_Is48Bytes_AcceptedAndEncodesToHandBuiltFrame()
    {
        string nick = new string('가', 16);          // each U+AC00 = EA B0 80 (3 bytes) -> 48 bytes
        byte[] nickBytes = Enumerable.Repeat(Hex("EAB080"), 16).SelectMany(x => x).ToArray();
        Assert.Equal(48, nickBytes.Length);

        Assert.Equal(nick, DecodeJoin(JoinPayload(nickBytes)).Nickname);

        // Length = 2 (id) + 2 (strlen) + 48 + 8 (token) = 60 = 0x3C
        byte[] expected = Concat(Hex("3C000000 0100 3000"), nickBytes, Hex("8877665544332211"));
        byte[] actual = PacketCodec.Encode(new C2S_Join { Nickname = nick, ReconnectToken = JoinToken });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Nickname_Korean17Chars_Is51Bytes_RejectedOnDecodeAndEncode()
    {
        byte[] nickBytes = Enumerable.Repeat(Hex("EAB080"), 17).SelectMany(x => x).ToArray();
        Assert.Equal(51, nickBytes.Length);
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0001, JoinPayload(nickBytes)));
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Encode(new C2S_Join { Nickname = new string('가', 17), ReconnectToken = 1 }));
    }

    [Theory]
    [InlineData(15, 3, true)]    // 15*3 + 3  = 48 bytes
    [InlineData(15, 4, false)]   // 15*3 + 4  = 49 bytes
    [InlineData(16, 0, true)]    // 48
    [InlineData(16, 1, false)]   // 49
    [InlineData(1, 45, true)]    // 3 + 45 = 48
    [InlineData(1, 46, false)]   // 3 + 46 = 49
    [InlineData(1, 47, false)]   // 3 + 47 = 50 (char count 48 would wrongly pass a char-based limit)
    public void Nickname_MixedKoreanAndAscii_CrossesByteLimit(int koreanChars, int asciiChars, bool accepted)
    {
        string nick = new string('가', koreanChars) + new string('z', asciiChars);
        byte[] bytes = Concat(
            Enumerable.Repeat(Hex("EAB080"), koreanChars).SelectMany(x => x).ToArray(),
            Enumerable.Repeat((byte)'z', asciiChars).ToArray());
        if (accepted)
        {
            Assert.Equal(nick, DecodeJoin(JoinPayload(bytes)).Nickname);
            Assert.Equal(
                Concat(Le32(2 + 2 + bytes.Length + 8), Hex("0100"), new[] { (byte)bytes.Length, (byte)0 }, bytes,
                    Hex("8877665544332211")),
                PacketCodec.Encode(new C2S_Join { Nickname = nick, ReconnectToken = JoinToken }));
        }
        else
        {
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0001, JoinPayload(bytes)));
            Assert.Throws<PacketFormatException>(() =>
                PacketCodec.Encode(new C2S_Join { Nickname = nick, ReconnectToken = JoinToken }));
        }
    }

    [Fact]
    public void Nickname_Empty_DecodesToEmptyAndEncodesToHandBuiltFrame()
    {
        Assert.Equal(string.Empty, DecodeJoin(JoinPayload(Array.Empty<byte>())).Nickname);
        // Length = 2 + 2 + 0 + 8 = 12 = 0x0C
        byte[] expected = Hex("0C000000 0100 0000 8877665544332211");
        Assert.Equal(expected, PacketCodec.Encode(new C2S_Join { Nickname = "", ReconnectToken = JoinToken }));
    }

    [Fact]
    public void Nickname_Supplementary4ByteUtf8_DecodesToSurrogatePair()
    {
        // F0 90 80 80 = U+10000 -> "𐀀"
        Assert.Equal("𐀀", DecodeJoin(JoinPayload(Hex("F0908080"))).Nickname);
    }

    // ---------------------------------------------------------------- 6. invalid UTF-8

    [Theory]
    [InlineData("C328")]       // 2-byte lead followed by non-continuation
    [InlineData("80")]         // lone continuation byte
    [InlineData("FF")]         // never valid in UTF-8
    [InlineData("C080")]       // overlong NUL
    [InlineData("C0AF")]       // overlong '/'
    [InlineData("EDA080")]     // UTF-16 surrogate encoded in UTF-8
    [InlineData("E282")]       // truncated 3-byte sequence
    [InlineData("F4908080")]   // > U+10FFFF
    [InlineData("6162FE63")]   // valid ASCII around an invalid byte
    public void InvalidUtf8_InNicknameField_ThrowsPacketFormatException(string hex)
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0001, JoinPayload(Hex(hex))));
    }

    [Fact]
    public void InvalidUtf8_InsideLobbySlotNickname_ThrowsPacketFormatException()
    {
        // 8 slots; slot 3 has nickname bytes "C3 28". Slot layout: int id, ushort len, bytes, byte bot, byte host.
        // v0.2 layout: byte IsOffline, byte HostRoleChoice, byte slotCount, then slots.
        var parts = new List<byte[]> { new byte[] { 0, 0, 8 } };
        for (int i = 0; i < 8; i++)
        {
            byte[] nick = i == 3 ? Hex("C328") : new[] { (byte)('a' + i) };
            parts.Add(Le32(i + 1));
            parts.Add(new[] { (byte)nick.Length, (byte)0 });
            parts.Add(nick);
            parts.Add(new byte[] { 0, 0 });
        }
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x000A, Concat(parts.ToArray())));
    }

    // ---------------------------------------------------------------- 7. S2C_LobbyState slot count

    private static byte[] LobbyPayload(byte declaredCount, int slotsPresent)
    {
        // v0.2 (docs/briefs/U1b-a1.md section 4): byte IsOffline, byte HostRoleChoice, Slot[8].
        // IsOffline = 1, HostRoleChoice = 255 (Random) so the control test can assert both header bytes.
        var parts = new List<byte[]> { new byte[] { 1, 255 }, new[] { declaredCount } };
        for (int i = 0; i < slotsPresent; i++)
        {
            parts.Add(Le32(i + 1));                              // PlayerId = i + 1
            parts.Add(new byte[] { 1, 0, (byte)('a' + i % 26) }); // Nickname length 1, one ASCII char
            parts.Add(new[] { (byte)(i == 7 ? 1 : 0), (byte)(i == 0 ? 1 : 0) }); // IsBot, IsHost
            parts.Add(new byte[] { 0xFF });                      // R1 SkillChoice: none
        }
        return Concat(parts.ToArray());
    }

    [Fact]
    public void LobbyState_ExactlyEightSlots_IsAccepted_Control()
    {
        var l = Assert.IsType<S2C_LobbyState>(PacketCodec.Decode(0x000A, LobbyPayload(8, 8)));
        Assert.Equal((byte)1, l.IsOffline);
        Assert.Equal((byte)255, l.HostRoleChoice);
        Assert.Equal(8, l.Slots.Length);
        Assert.Equal(1, l.Slots[0].PlayerId);
        Assert.Equal("a", l.Slots[0].Nickname);
        Assert.Equal((byte)0, l.Slots[0].IsBot);
        Assert.Equal((byte)1, l.Slots[0].IsHost);
        Assert.Equal(8, l.Slots[7].PlayerId);
        Assert.Equal("h", l.Slots[7].Nickname);
        Assert.Equal((byte)1, l.Slots[7].IsBot);
        Assert.Equal((byte)0, l.Slots[7].IsHost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(255)]
    public void LobbyState_SlotCountOtherThanEight_IsRejected(int count)
    {
        // Wire data is internally consistent (declared count == slots actually present).
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Decode(0x000A, LobbyPayload((byte)count, count)));
    }

    [Fact]
    public void LobbyState_DeclaresEightButOnlySevenPresent_IsRejected()
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x000A, LobbyPayload(8, 7)));
    }

    [Fact]
    public void LobbyState_EightSlotsPlusTrailingByte_IsRejected()
    {
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Decode(0x000A, Concat(LobbyPayload(8, 8), new byte[] { 0 })));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void LobbyState_EncodeWithWrongSlotCount_Throws(int count)
    {
        var slots = new Slot[count];
        for (int i = 0; i < count; i++) slots[i] = new Slot { PlayerId = i, Nickname = "x" };
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_LobbyState { Slots = slots }));
    }

    // ---------------------------------------------------------------- 8. S2C_WorldState player count

    private static byte[] WorldPayload(byte declaredCount, int playersPresent)
    {
        // v0.2 (docs/briefs/U1b-a1.md sections 2-4):
        // uint Tick, float TimeLeft, byte ThreatLevel, byte ExitOpen, byte ExitLocked, byte TerminalsRequired,
        // byte terminalCount + terminals, byte playerCount + players (24 bytes each).
        var parts = new List<byte[]>
        {
            Hex("0D0C0B0A"),   // Tick = 0x0A0B0C0D
            Hex("0000C03F"),   // TimeLeft = 1.5f  (0x3FC00000)
            new byte[] { 0, 2 },  // ThreatLevel 0, TerminalsRequired 2
            Hex("000080BF"),   // CollapseLeft = -1f (v0.4)
            new byte[] { 0, 0 },  // no exits, HatchVisible 0 (v0.4)
            new byte[] { 0 },  // zero terminals
            new[] { declaredCount },
        };
        for (int i = 0; i < playersPresent; i++)
        {
            parts.Add(Le32(i + 1));                  // Id
            parts.Add(new byte[] { (byte)(i % 2), 0 }); // Role, Status
            parts.Add(Hex("0000803F"));              // X = 1f
            parts.Add(Hex("00000040"));              // Z = 2f
            parts.Add(Hex("00000000"));              // Yaw = 0f
            parts.Add(new byte[] { 0 });             // Flags
            parts.Add(Hex("00000000"));              // StatusTimer = 0f
            parts.Add(new byte[] { 0 });             // CaptureCount
            parts.Add(new byte[] { 0 });             // Chase (v0.7)
        }
        parts.Add(new byte[] { 0 });                 // no shutters (v0.7)
        return Concat(parts.ToArray());
    }

    [Fact]
    public void WorldState_EightPlayers_IsAccepted_Control()
    {
        var w = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(0x0005, WorldPayload(8, 8)));
        Assert.Equal(0x0A0B0C0Du, w.Tick);
        Assert.Equal(1.5f, w.TimeLeft);
        Assert.Equal((byte)0, w.ThreatLevel);
        Assert.False(w.Powered());
        Assert.False(w.AnyOpen());
        Assert.Equal((byte)2, w.TerminalsRequired);
        Assert.Empty(w.Terminals);
        Assert.Equal(8, w.Players.Count);
        Assert.Equal(8, w.Players[7].Id);
        Assert.Equal((byte)1, w.Players[7].Role);
        Assert.Equal(1f, w.Players[7].X);
        Assert.Equal(2f, w.Players[7].Z);
    }

    [Fact]
    public void WorldState_ZeroPlayers_IsAccepted()
    {
        var w = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(0x0005, WorldPayload(0, 0)));
        Assert.Empty(w.Players);
    }

    // U2c migration: the entry cap rose from 8 to 16 (reserved slots per hidden player plus phantoms); the byte layout is
    // unchanged. The invariant kept: more than the cap is a wire-format violation, up to the cap is accepted.
    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(32)]
    [InlineData(255)]
    public void WorldState_MoreThanSixteenEntries_IsRejected(int count)
    {
        Assert.Throws<PacketFormatException>(() =>
            PacketCodec.Decode(0x0005, WorldPayload((byte)count, count)));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(16)]
    public void WorldState_UpToSixteenEntries_AreAccepted_ForTheNineToSixteenRangeThatWasRejectedBefore(int count)
    {
        var w = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(0x0005, WorldPayload((byte)count, count)));
        Assert.Equal(count, w.Players.Count);
        Assert.Equal(count, w.Players[count - 1].Id);
    }

    [Fact]
    public void WorldState_DeclaresEightButSevenPresent_IsRejected()
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0005, WorldPayload(8, 7)));
    }

    [Fact]
    public void WorldState_EncodeSeventeenEntries_Throws_SixteenRoundTrips()
    {
        var w = new S2C_WorldState();
        for (int i = 0; i < 16; i++) w.Players.Add(new PlayerState { Id = i + 1, X = i, Z = 2 * i });
        var back = Assert.IsType<S2C_WorldState>(PacketCodec.Decode(0x0005, PacketCodec.Encode(w).AsSpan(6)));
        Assert.Equal(16, back.Players.Count);
        Assert.Equal(16, back.Players[15].Id);
        w.Players.Add(new PlayerState { Id = 17 });
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(w));
    }

    [Fact]
    public void GameResult_NineEscapedIds_IsRejected()
    {
        // v0.2: byte WinnerRole (1 = Killer), byte Reason (2 = TimeUp), byte count (9 -> too many), ids.
        var parts = new List<byte[]> { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 9 } };
        for (int i = 0; i < 9; i++) parts.Add(Le32(i));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0007, Concat(parts.ToArray())));
    }

    // ---------------------------------------------------------------- 9. golden bytes for C2S_MoveInput

    [Fact]
    public void MoveInput_GoldenBytes_EncodeAndDecode()
    {
        // DirX = 1f   = 0x3F800000 -> 00 00 80 3F
        // DirZ = -1f  = 0xBF800000 -> 00 00 80 BF
        // Yaw  = 0f   = 0x00000000 -> 00 00 00 00
        // Seq  = 1                 -> 01 00 00 00
        // payload = 16 bytes, Length = 2 + 16 = 18 = 0x12, PacketId = 0x0003
        byte[] expected = Hex("12000000 0300 0000803F 000080BF 00000000 01000000");
        Assert.Equal(22, expected.Length);

        byte[] actual = PacketCodec.Encode(new C2S_MoveInput { DirX = 1f, DirZ = -1f, Yaw = 0f, Seq = 1 });
        Assert.Equal(expected, actual);

        var a = new PacketAssembler();
        a.Append(expected);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out ReadOnlyMemory<byte> payload));
        Assert.Equal((ushort)3, id);
        var m = Assert.IsType<C2S_MoveInput>(PacketCodec.Decode(id, payload.Span));
        Assert.Equal(1f, m.DirX);
        Assert.Equal(-1f, m.DirZ);
        Assert.Equal(0f, m.Yaw);
        Assert.Equal(1u, m.Seq);
    }

    [Fact]
    public void MoveInput_NegativeZeroAndNaNBitsSurviveWire()
    {
        // Yaw = -0f must keep its sign bit (0x80000000), DirX = quiet NaN 0x7FC00000.
        byte[] payload = Hex("0000C07F 00000000 00000080 07000000");
        var m = Assert.IsType<C2S_MoveInput>(PacketCodec.Decode(3, payload));
        Assert.True(float.IsNaN(m.DirX));
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(m.Yaw));
        Assert.Equal(7u, m.Seq);
        // Re-encode: bytes identical (Length 18, id 3).
        Assert.Equal(Hex("12000000 0300").Concat(payload).ToArray(), PacketCodec.Encode(m));
    }

    // ---------------------------------------------------------------- 10. payload lifetime vs Append

    [Fact]
    public void Payload_DecodedBeforeNextAppend_IsUnaffectedByLaterAppendCompactionAndGrowth()
    {
        // Tiny initial capacity (24) so the second Append must compact, the third must grow.
        var a = new PacketAssembler(initialCapacity: 24);

        a.Append(PingFrame);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort idA, out ReadOnlyMemory<byte> pA));
        byte[] copyA = pA.ToArray();
        var decodedA = Assert.IsType<C2S_Ping>(PacketCodec.Decode(idA, pA.Span));
        Assert.Equal(0x0102030405060708L, decodedA.Timestamp);

        a.Append(PongFrame);   // readOffset 14 > 12 -> compaction
        Assert.Equal(0x0102030405060708L, decodedA.Timestamp);
        Assert.Equal(Hex("0807060504030201"), copyA);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort idB, out ReadOnlyMemory<byte> pB));
        Assert.Equal((ushort)9, idB);
        var decodedB = Assert.IsType<S2C_Pong>(PacketCodec.Decode(idB, pB.Span));
        Assert.Equal(0x1111111111111111L, decodedB.Timestamp);

        byte[] filler = Pattern(3, 10000);
        a.Append(Frame(0x7001, filler));   // forces buffer growth
        Assert.Equal(0x0102030405060708L, decodedA.Timestamp);
        Assert.Equal(0x1111111111111111L, decodedB.Timestamp);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort idC, out ReadOnlyMemory<byte> pC));
        Assert.Equal((ushort)0x7001, idC);
        Assert.Equal(filler, pC.ToArray());

        a.Append(PingFrame);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort idD, out ReadOnlyMemory<byte> pD));
        Assert.Equal((ushort)8, idD);
        Assert.Equal(Hex("0807060504030201"), pD.ToArray());
    }

    [Fact]
    public void PartialFrameLeftover_SurvivesCompactionAndCompletesCorrectly()
    {
        var a = new PacketAssembler(initialCapacity: 24);
        // A whole, then the first 9 bytes of B, then the remaining 5 bytes of B.
        a.Append(Concat(PingFrame, PongFrame[..9]));
        Assert.Equal(AssembleResult.Packet, a.TryRead(out ushort id, out _));
        Assert.Equal((ushort)8, id);
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
        a.Append(PongFrame[9..]);
        Assert.Equal(AssembleResult.Packet, a.TryRead(out id, out ReadOnlyMemory<byte> p));
        Assert.Equal((ushort)9, id);
        Assert.Equal(Hex("1111111111111111"), p.ToArray());
        Assert.Equal(AssembleResult.NeedMoreData, a.TryRead(out _, out _));
    }

    // ---------------------------------------------------------------- extra adversarial stress

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void RandomChunking_WithTinyOrDefaultInitialCapacity_ReassemblesAllFrames(int initialCapacity)
    {
        var rng = new Random(777);
        var frames = new List<(ushort Id, byte[] Payload)>();
        for (int i = 0; i < 300; i++)
        {
            int len = rng.Next(10) == 0 ? rng.Next(8000, 16383) : rng.Next(0, 300);
            if (i == 150) len = 16382;
            var payload = new byte[len];
            rng.NextBytes(payload);
            frames.Add(((ushort)rng.Next(1, 65536), payload));
        }
        byte[] stream = Concat(frames.Select(f => Frame(f.Id, f.Payload)).ToArray());

        var a = new PacketAssembler(initialCapacity: initialCapacity);
        var got = new List<(ushort Id, byte[] Payload)>();
        int pos = 0;
        while (pos < stream.Length)
        {
            int n = Math.Min(rng.Next(1, 5001), stream.Length - pos);
            a.Append(stream.AsSpan(pos, n));
            pos += n;
            Assert.NotEqual(AssembleResult.Malformed, Drain(a, got));
        }
        Assert.Equal(frames.Count, got.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.Equal(frames[i].Id, got[i].Id);
            Assert.Equal(frames[i].Payload, got[i].Payload);
        }
        Assert.False(a.IsFaulted);
    }

    [Fact]
    public void ManyLargeChunks_DrainedBetweenAppends_NeverThrowAndNeverGrowWithoutBound()
    {
        // 200 chunks of ~16 KB, each holding many whole frames plus a partial tail.
        var a = new PacketAssembler();
        byte[] f = Frame(0x7123, Pattern(1, 94));      // 100-byte frame
        var got = new List<(ushort Id, byte[] Payload)>();
        int received = 0;
        for (int c = 0; c < 200; c++)
        {
            var chunk = new List<byte>();
            for (int i = 0; i < 163; i++) chunk.AddRange(f);   // 16300 bytes
            chunk.AddRange(f.AsSpan(0, 37).ToArray());          // partial tail
            a.Append(chunk.ToArray());
            Assert.Equal(AssembleResult.NeedMoreData, Drain(a, got));
            received += got.Count;
            got.Clear();
            a.Append(f.AsSpan(37, 63));                         // complete the tail
            Assert.Equal(AssembleResult.NeedMoreData, Drain(a, got));
            received += got.Count;
            got.Clear();
        }
        Assert.False(a.IsFaulted);
        Assert.Equal(200 * 164, received);
    }

    // ---------------------------------------------------------------- decode-side hardening

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0010)]   // first id past v0.2's 0x000F (0x000D is now C2S_Interact)
    [InlineData(0x0100)]
    [InlineData(0xFFFF)]
    public void UnknownPacketIds_AreRejectedWithPacketFormatException(int id)
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)id, Array.Empty<byte>()));
    }

    [Fact]
    public void Decode_EmptyPayloadForNonEmptyPacket_IsFormatErrorNotOtherException()
    {
        for (ushort id = 1; id <= 0x0F; id++)
        {
            if (id == 0x0B) continue; // StartMatch is legitimately empty
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, Array.Empty<byte>()));
        }
    }

    [Fact]
    public void Decode_StartMatchWithTrailingByte_IsRejected()
    {
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x000B, new byte[] { 0 }));
    }

    [Fact]
    public void Decode_RandomGarbagePayloads_OnlyEverThrowPacketFormatException()
    {
        var rng = new Random(4242);
        for (int i = 0; i < 20000; i++)
        {
            ushort id = (ushort)rng.Next(0, 0x10);
            var payload = new byte[rng.Next(0, 80)];
            rng.NextBytes(payload);
            try
            {
                PacketCodec.Decode(id, payload);
            }
            catch (PacketFormatException)
            {
            }
        }
    }
}
