using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class StreamFuzzTests
{
    [Fact]
    public void TenThousandRandomPacketsSurviveRandomChunkingByteIdentically()
    {
        var random = new Random(12345);
        var frames = new List<byte[]>(10000);
        var types = new HashSet<PacketId>();
        var terminalCounts = new HashSet<int>();
        using var stream = new MemoryStream();
        for (int i = 0; i < 10000; i++)
        {
            IPacket packet = RandomPacket(random);
            types.Add(packet.Id);
            if (packet is S2C_WorldState world)
                terminalCounts.Add(world.Terminals.Count);
            byte[] frame = PacketCodec.Encode(packet);
            frames.Add(frame);
            stream.Write(frame);
        }
        Assert.Equal(15, types.Count);
        Assert.Equal(Enumerable.Range(0, 9), terminalCounts.Order());

        byte[] bytes = stream.ToArray();
        var assembler = new PacketAssembler(initialCapacity: 1);
        int offset = 0;
        int received = 0;
        while (offset < bytes.Length)
        {
            int size = Math.Min(random.Next(1, 1501), bytes.Length - offset);
            assembler.Append(bytes.AsSpan(offset, size));
            offset += size;
            while (true)
            {
                AssembleResult result = assembler.TryRead(out ushort id, out var payload);
                if (result == AssembleResult.NeedMoreData)
                    break;
                Assert.Equal(AssembleResult.Packet, result);
                Assert.True(received < frames.Count, "Assembler produced an extra packet.");
                IPacket decoded = PacketCodec.Decode(id, payload.Span);
                Assert.Equal(frames[received], PacketCodec.Encode(decoded));
                received++;
            }
        }
        Assert.Equal(10000, received);
        Assert.False(assembler.IsFaulted);
        Assert.Equal(AssembleResult.NeedMoreData, assembler.TryRead(out _, out _));
    }

    private static IPacket RandomPacket(Random r)
    {
        switch (r.Next(15))
        {
            case 0: return new C2S_Join { Nickname = Nickname(r), ReconnectToken = UInt64(r) };
            case 1: return new S2C_JoinResult { PlayerId = Int32(r), ReconnectToken = UInt64(r), Result = Byte(r) };
            case 2: return new C2S_MoveInput { DirX = Float(r), DirZ = Float(r), Yaw = Float(r), Seq = (uint)Int32(r) };
            case 3: return new C2S_UseSkill { SkillId = Byte(r) };
            case 4:
                var world = new S2C_WorldState
                {
                    Tick = (uint)Int32(r), TimeLeft = Float(r), ThreatLevel = (byte)r.Next(4),
                    ExitOpen = Byte(r), ExitLocked = Byte(r), TerminalsRequired = Byte(r)
                };
                int terminals = r.Next(9);
                for (int i = 0; i < terminals; i++)
                    world.Terminals.Add(new TerminalState { Id = Byte(r), X = Float(r), Z = Float(r), Progress = (byte)r.Next(101) });
                int players = r.Next(9);
                for (int i = 0; i < players; i++)
                    world.Players.Add(new PlayerState
                    {
                        Id = Int32(r), Role = (byte)r.Next(2), Status = (byte)r.Next(4),
                        X = Float(r), Z = Float(r), Yaw = Float(r), Flags = (byte)r.Next(32),
                        StatusTimer = Float(r), CaptureCount = Byte(r)
                    });
                return world;
            case 5: return new S2C_SkillEffect { CasterId = Int32(r), SkillId = Byte(r), X = Float(r), Z = Float(r), Duration = Float(r) };
            case 6:
                var result = new S2C_GameResult { WinnerRole = (byte)r.Next(2), Reason = (byte)r.Next(3) };
                int escaped = r.Next(9);
                for (int i = 0; i < escaped; i++)
                    result.EscapedIds.Add(Int32(r));
                return result;
            case 7: return new C2S_Ping { Timestamp = (long)UInt64(r) };
            case 8: return new S2C_Pong { Timestamp = (long)UInt64(r) };
            case 9:
                var lobby = new S2C_LobbyState { IsOffline = Byte(r), HostRoleChoice = RoleChoiceByte(r) };
                for (int i = 0; i < 8; i++)
                    lobby.Slots[i] = new Slot { PlayerId = Int32(r), Nickname = Nickname(r), IsBot = Byte(r), IsHost = Byte(r) };
                return lobby;
            case 10: return new C2S_StartMatch();
            case 11: return new S2C_MatchStart { Role = (byte)r.Next(2), SkillId = Byte(r), Seed = Int32(r) };
            case 12: return new C2S_Interact { Active = (byte)r.Next(2) };
            case 13: return new C2S_SetBots { Count = (byte)r.Next(8) };
            default: return new C2S_SetRole { RoleChoice = RoleChoiceByte(r) };
        }
    }

    private static byte Byte(Random r) => (byte)r.Next(256);
    private static byte RoleChoiceByte(Random r) => r.Next(3) switch { 0 => 0, 1 => 1, _ => 255 };
    private static int Int32(Random r) => (int)r.NextInt64(int.MinValue, (long)int.MaxValue + 1);
    private static ulong UInt64(Random r) => ((ulong)(uint)Int32(r) << 32) | (uint)Int32(r);
    private static float Float(Random r) => BitConverter.Int32BitsToSingle(Int32(r));

    private static string Nickname(Random r)
    {
        // Every candidate is valid Unicode and at most three UTF-8 bytes per character.
        const string characters = "abcXYZ09살인마생존자\0é한";
        int count = r.Next(17);
        var value = new char[count];
        for (int i = 0; i < count; i++)
            value[i] = characters[r.Next(characters.Length)];
        return new string(value);
    }
}
