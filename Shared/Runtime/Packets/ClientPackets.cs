using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Packets
{
    public sealed class C2S_Join : IPacket
    {
        public string Nickname = string.Empty;
        public ulong ReconnectToken;
        public PacketId Id => PacketId.C2S_Join;

        public void Write(PacketWriter w)
        {
            w.WriteString(Nickname, Protocol.MaxNicknameBytes);
            w.WriteUInt64(ReconnectToken);
        }

        public static C2S_Join Read(ref PacketReader r) => new C2S_Join
        {
            Nickname = r.ReadString(Protocol.MaxNicknameBytes), ReconnectToken = r.ReadUInt64()
        };
    }

    public sealed class C2S_MoveInput : IPacket
    {
        public float DirX;
        public float DirZ;
        public float Yaw;
        public uint Seq;
        public PacketId Id => PacketId.C2S_MoveInput;

        public void Write(PacketWriter w)
        {
            w.WriteSingle(DirX);
            w.WriteSingle(DirZ);
            w.WriteSingle(Yaw);
            w.WriteUInt32(Seq);
        }

        public static C2S_MoveInput Read(ref PacketReader r) => new C2S_MoveInput
        {
            DirX = r.ReadSingle(), DirZ = r.ReadSingle(), Yaw = r.ReadSingle(), Seq = r.ReadUInt32()
        };
    }

    public sealed class C2S_UseSkill : IPacket
    {
        public byte SkillId;
        public PacketId Id => PacketId.C2S_UseSkill;
        public void Write(PacketWriter w) => w.WriteByte(SkillId);
        public static C2S_UseSkill Read(ref PacketReader r) => new C2S_UseSkill { SkillId = r.ReadByte() };
    }

    public sealed class C2S_Ping : IPacket
    {
        public long Timestamp;
        public PacketId Id => PacketId.C2S_Ping;
        public void Write(PacketWriter w) => w.WriteInt64(Timestamp);
        public static C2S_Ping Read(ref PacketReader r) => new C2S_Ping { Timestamp = r.ReadInt64() };
    }

    public sealed class C2S_StartMatch : IPacket
    {
        public PacketId Id => PacketId.C2S_StartMatch;
        public void Write(PacketWriter w) { }
        public static C2S_StartMatch Read(ref PacketReader r) => new C2S_StartMatch();
    }
}
