using System.Text;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3b;

// Round 2: probes of the structural leak scan (decode/re-encode equality + field-level comparison incl. string bytes).
public sealed class U3bStructuralScanTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int A = 2, B = 3;
    private static readonly int[] Survivors = { A, B };

    // As the harness receives it: decode the encoded bytes.
    private static ReceivedFrame Wire(IPacket p)
    {
        byte[] raw = PacketCodec.Encode(p);
        var packet = PacketCodec.Decode(BitConverter.ToUInt16(raw, 4), raw.AsSpan(6));
        return new ReceivedFrame(packet, raw, 0);
    }
    private static HashSet<int> Bits(params float[] v) => v.Select(KillerLeakScan.Bits).ToHashSet();
    private static string? Scan(IPacket p, HashSet<int> killer) => KillerLeakScan.Excess(Wire(p), killer, Survivors);
    private static float Float(int bits) => BitConverter.Int32BitsToSingle(bits);
    private static int AsciiBits(string four) => BitConverter.ToInt32(Encoding.ASCII.GetBytes(four));

    // Killer values: spawn plus every 0.5 m planner grid coordinate (all round floats).
    private static HashSet<int> RoundKiller()
    {
        var set = new HashSet<float> { 5f };
        for (float x = 0.5f; x <= 60; x += 0.5f) set.Add(x);
        return Bits(set.ToArray());
    }

    private static Slot NamedSlot(int id, string name, bool bot = false) => new() { PlayerId = id, Nickname = name, IsBot = (byte)(bot ? 1 : 0) };

    [Fact]
    public void CleanFramesOfEveryServerToClientPacketTypeAreNeverFlagged_RoundKillerValues()
    {
        var rng = new Random(7);
        var killer = RoundKiller();
        int flagged = 0, total = 0;
        var errors = new List<string>();
        void Check(IPacket p)
        {
            total++;
            string? e = Scan(p, killer);
            if (e != null) { flagged++; if (errors.Count < 5) errors.Add(e); }
        }
        for (int i = 0; i < 20000; i++)
        {
            Check(new S2C_WorldState
            {
                Tick = (uint)rng.Next(0, 20000), TimeLeft = 600 - rng.Next(0, 12000) * 0.05f, ThreatLevel = (byte)rng.Next(4),
                ExitOpen = (byte)rng.Next(2), ExitLocked = (byte)rng.Next(2), TerminalsRequired = 2,
                Terminals = Enumerable.Range(0, 4).Select(t => new TerminalState { Id = (byte)t, X = rng.Next(0, 120) * 0.5f, Z = rng.Next(0, 120) * 0.5f, Progress = (byte)rng.Next(101) }).ToList(),
                Players = new List<PlayerState>
                {
                    new() { Id = A, Role = 0, Status = (byte)rng.Next(4), X = (float)(rng.NextDouble() * 60), Z = (float)(rng.NextDouble() * 60), Yaw = (float)rng.NextDouble() * 6, Flags = (byte)rng.Next(32), StatusTimer = (float)(rng.NextDouble() * 60), CaptureCount = (byte)rng.Next(2) },
                    new() { Id = B, Role = 0, Status = (byte)rng.Next(4), X = rng.Next(0, 120) * 0.5f, Z = rng.Next(0, 120) * 0.5f, StatusTimer = rng.Next(0, 60) }
                }
            });
            Check(new S2C_Pong { Timestamp = rng.NextInt64() });
            Check(new S2C_Pong { Timestamp = rng.Next(0, 100000) });
        }
        var lobby = new S2C_LobbyState { HostRoleChoice = 1 };
        lobby.Slots[0] = NamedSlot(1, "Harness Host"); lobby.Slots[1] = NamedSlot(2, "Harness H6 A"); lobby.Slots[2] = NamedSlot(3, "Harness H6 B");
        lobby.Slots[3] = NamedSlot(100, "Bot", true);
        Check(lobby);
        Check(new S2C_JoinResult { PlayerId = 2, ReconnectToken = 9502 });
        Check(new S2C_JoinResult { PlayerId = 3, ReconnectToken = ulong.MaxValue });
        Check(new S2C_MatchStart { Role = 0, SkillId = 1, Seed = 4242 });
        Check(new S2C_GameResult { WinnerRole = 0, Reason = 0, EscapedIds = new List<int> { 3 } });
        Check(new S2C_SkillEffect { CasterId = A, SkillId = 0, X = 10, Z = 12, Duration = 0.123f });
        Assert.True(flagged == 0, $"{flagged}/{total} clean frames flagged: {string.Join(" | ", errors)}");
    }

    [Fact]
    public void RoundTripEdgeCasesAreNotFlaggedAsUnexplainedBytes()
    {
        var killer = Bits(5f);
        var world = new S2C_WorldState
        {
            Tick = uint.MaxValue, TimeLeft = float.NaN, Terminals = new List<TerminalState> { new() { Id = 5, X = -0f, Z = float.PositiveInfinity, Progress = 100 } },
            Players = new List<PlayerState> { new() { Id = int.MinValue, X = Float(0x7FC12345), Z = float.NegativeInfinity, Yaw = float.Epsilon, StatusTimer = float.MaxValue, Flags = 31, CaptureCount = 255 } }
        };
        Assert.Null(Scan(world, killer));
        var lobby = new S2C_LobbyState();
        lobby.Slots[0] = NamedSlot(1, "");
        lobby.Slots[1] = NamedSlot(2, new string('é', 24));              // 48 bytes, the limit
        lobby.Slots[2] = NamedSlot(3, "\U0001F600\U0001F600 Spieler");        // surrogate pairs
        lobby.Slots[3] = NamedSlot(4, "한글 닉네임");     // Korean
        Assert.Null(Scan(lobby, killer));
        Assert.Null(Scan(new S2C_Pong { Timestamp = long.MinValue }, killer));
        Assert.Null(Scan(new S2C_GameResult { EscapedIds = new List<int>() }, killer));
    }

    [Theory]
    [InlineData("ABCD", "ABCD")]
    [InlineData("ABCD", "xABCDx")]
    [InlineData("ABCD", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxABCD")] // 48 bytes, bytes at the very end
    [InlineData("ABCD", "é ABCD")]
    [InlineData("ABCD", "ABCDABCD")]
    public void KillerBitsInsideALobbyNicknameAreLeaks(string asciiFloat, string nickname)
    {
        var killer = new HashSet<int> { AsciiBits(asciiFloat) };
        var lobby = new S2C_LobbyState();
        lobby.Slots[5] = NamedSlot(6, nickname);
        string? e = Scan(lobby, killer);
        Assert.NotNull(e);
        Assert.Contains("killer coordinate", e);
    }

    [Fact]
    public void NicknameBytesWithoutTheKillerValueAreNotFlagged()
    {
        var killer = new HashSet<int> { AsciiBits("ABCD") };
        var lobby = new S2C_LobbyState();
        lobby.Slots[0] = NamedSlot(1, "ABC");
        lobby.Slots[1] = NamedSlot(2, "ABCE ABC D");
        lobby.Slots[2] = NamedSlot(3, "ABcd");
        Assert.Null(Scan(lobby, killer));
    }

    [Fact]
    public void KillerBitsInEveryIntFieldAreLeaks()
    {
        float k = 12.345678f;
        int bits = KillerLeakScan.Bits(k);
        var killer = new HashSet<int> { bits };
        IPacket[] leaks =
        {
            new S2C_Pong { Timestamp = bits },
            new S2C_Pong { Timestamp = (long)bits << 32 },
            new S2C_JoinResult { PlayerId = bits, ReconnectToken = 1 },
            new S2C_JoinResult { PlayerId = 1, ReconnectToken = (ulong)(uint)bits },
            new S2C_JoinResult { PlayerId = 1, ReconnectToken = (ulong)(uint)bits << 32 },
            new S2C_MatchStart { Seed = bits },
            new S2C_GameResult { EscapedIds = new List<int> { 3, bits } },
            new S2C_WorldState { Tick = (uint)bits },
            new S2C_WorldState { Players = new List<PlayerState> { new() { Id = bits, Role = 0 } } },
            new S2C_SkillEffect { CasterId = bits },
            new S2C_SkillEffect { CasterId = 1, Duration = k },
            new S2C_SkillEffect { CasterId = 1, X = k },
        };
        for (int i = 0; i < leaks.Length; i++)
            Assert.True(Scan(leaks[i], killer) != null, $"leak variant {i} ({leaks[i].GetType().Name}) not detected");
    }

    [Fact]
    public void KillerBitsInFloatFieldsOfNonSurvivorEntriesAreLeaks()
    {
        float k = 33.25f;
        var killer = Bits(k);
        foreach (var field in new[] { "X", "Z", "Yaw", "StatusTimer" })
        {
            object boxed = new PlayerState { Id = 98, Role = 0 };
            typeof(PlayerState).GetField(field)!.SetValue(boxed, k);
            var world = new S2C_WorldState { Players = new List<PlayerState> { (PlayerState)boxed } };
            Assert.True(Scan(world, killer) != null, $"unknown-id entry {field} not detected");
        }
    }

    [Fact]
    public void ReEncodeMismatchIsReportedAsUnexplainedBytes()
    {
        var world = new S2C_WorldState { Tick = 1, TimeLeft = 500 };
        byte[] raw = PacketCodec.Encode(world);
        byte[] tampered = (byte[])raw.Clone();
        tampered[^1] ^= 1; // same decoded packet is impossible here; the scan must still not trust a raw frame that differs
        string? e = KillerLeakScan.Excess(new ReceivedFrame(world, tampered, 0), Bits(5f), Survivors);
        Assert.NotNull(e);
        Assert.Contains("unexplained bytes", e);
        e = KillerLeakScan.Excess(new ReceivedFrame(world, raw.Concat(new byte[] { 0, 0, 0xA0, 0x40 }).ToArray(), 0), Bits(5f), Survivors);
        Assert.NotNull(e);
    }

    // The structural scan only inspects decoded fields. Four consecutive BYTE fields (ThreatLevel, ExitOpen, ExitLocked,
    // TerminalsRequired) can carry a float. KNOWN-LIMIT (info): not detected, unless the decoder rejects the bytes.
    [Fact]
    public void KnownLimit_KillerFloatSmuggledAcrossFourConsecutiveByteFields_IsNotDetected()
    {
        float k = 5f; // 00 00 A0 40
        byte[] b = BitConverter.GetBytes(KillerLeakScan.Bits(k));
        var world = new S2C_WorldState { Tick = 1, TimeLeft = 500, ThreatLevel = b[0], ExitOpen = b[1], ExitLocked = b[2], TerminalsRequired = b[3] };
        string? e;
        try { e = Scan(world, Bits(k)); }
        catch (PacketFormatException ex) { output.WriteLine("decoder rejected: " + ex.Message); return; }
        output.WriteLine("smuggle accepted by decoder and not detected");
        Assert.Null(e);
    }

    // KNOWN-LIMIT (info, unreachable in H6: no skills, no bots): a legitimate survivor-cast SkillEffect whose Duration
    // equals a round killer coordinate (a 5 s duration vs spawn X=5) is flagged: Duration is not a legitimate holder.
    [Fact]
    public void KnownLimit_SkillEffectDurationEqualToRoundKillerCoordinate_IsFlagged()
    {
        Assert.NotNull(Scan(new S2C_SkillEffect { CasterId = A, SkillId = 0, X = 10, Z = 12, Duration = 5f }, Bits(5f)));
    }
}
