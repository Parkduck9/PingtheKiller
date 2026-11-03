using System;
using System.Buffers.Binary;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Shared.Net
{
    public static class PacketCodec
    {
        public static byte[] Encode(IPacket packet)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));
            var writer = new PacketWriter();
            packet.Write(writer);
            int bodyLength = Protocol.IdSize + writer.WrittenSpan.Length;
            var frame = new byte[Protocol.HeaderSize + bodyLength];
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(), bodyLength);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(Protocol.HeaderSize), (ushort)packet.Id);
            writer.WrittenSpan.CopyTo(frame.AsSpan(Protocol.HeaderSize + Protocol.IdSize));
            return frame;
        }

        public static IPacket Decode(ushort packetId, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > Protocol.MaxBodyLength - Protocol.IdSize)
                throw new PacketFormatException("Payload exceeds the protocol limit.");
            var reader = new PacketReader(payload);
            IPacket packet;
            switch ((PacketId)packetId)
            {
                case PacketId.C2S_Join: packet = C2S_Join.Read(ref reader); break;
                case PacketId.S2C_JoinResult: packet = S2C_JoinResult.Read(ref reader); break;
                case PacketId.C2S_MoveInput: packet = C2S_MoveInput.Read(ref reader); break;
                case PacketId.C2S_UseSkill: packet = C2S_UseSkill.Read(ref reader); break;
                case PacketId.S2C_WorldState: packet = S2C_WorldState.Read(ref reader); break;
                case PacketId.S2C_SkillEffect: packet = S2C_SkillEffect.Read(ref reader); break;
                case PacketId.S2C_GameResult: packet = S2C_GameResult.Read(ref reader); break;
                case PacketId.C2S_Ping: packet = C2S_Ping.Read(ref reader); break;
                case PacketId.S2C_Pong: packet = S2C_Pong.Read(ref reader); break;
                case PacketId.S2C_LobbyState: packet = S2C_LobbyState.Read(ref reader); break;
                case PacketId.C2S_StartMatch: packet = C2S_StartMatch.Read(ref reader); break;
                case PacketId.S2C_MatchStart: packet = S2C_MatchStart.Read(ref reader); break;
                case PacketId.C2S_Interact: packet = C2S_Interact.Read(ref reader); break;
                case PacketId.C2S_SetBots: packet = C2S_SetBots.Read(ref reader); break;
                case PacketId.C2S_SetRole: packet = C2S_SetRole.Read(ref reader); break;
                case PacketId.S2C_SelfState: packet = S2C_SelfState.Read(ref reader); break;
                case PacketId.S2C_SyncCheck: packet = S2C_SyncCheck.Read(ref reader); break;
                case PacketId.C2S_SyncPress: packet = C2S_SyncPress.Read(ref reader); break;
                case PacketId.S2C_SyncResult: packet = S2C_SyncResult.Read(ref reader); break;
                default: throw new PacketFormatException("Unknown packet ID.");
            }
            reader.EnsureEnd();
            return packet;
        }
    }
}
