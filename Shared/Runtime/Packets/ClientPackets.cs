using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Game;

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

    public sealed class C2S_Interact : IPacket
    {
        public byte Active;
        public PacketId Id => PacketId.C2S_Interact;
        public void Write(PacketWriter w) => w.WriteByte(Active);
        public static C2S_Interact Read(ref PacketReader r) => new C2S_Interact { Active = r.ReadByte(1) };
    }

    public sealed class C2S_SetBots : IPacket
    {
        public byte Count;
        public PacketId Id => PacketId.C2S_SetBots;
        public void Write(PacketWriter w) => w.WriteByte(Count);
        public static C2S_SetBots Read(ref PacketReader r) => new C2S_SetBots { Count = r.ReadByte(7) };
    }

    public sealed class C2S_SetRole : IPacket
    {
        public byte RoleChoice;
        public PacketId Id => PacketId.C2S_SetRole;
        public void Write(PacketWriter w) => w.WriteByte(RoleChoice);
        public static C2S_SetRole Read(ref PacketReader r) => new C2S_SetRole { RoleChoice = GameWireValues.ReadRoleChoice(ref r) };
    }

    // v0.4: the answer to one S2C_SyncCheck, sent at most once per check at the moment the player pressed.
    public sealed class C2S_SyncPress : IPacket
    {
        public uint CheckId;
        public PacketId Id => PacketId.C2S_SyncPress;
        public void Write(PacketWriter w) => w.WriteUInt32(CheckId);
        public static C2S_SyncPress Read(ref PacketReader r) => new C2S_SyncPress { CheckId = r.ReadUInt32() };
    }
}
