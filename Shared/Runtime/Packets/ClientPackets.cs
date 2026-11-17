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

    public sealed class C2S_RttEcho : IPacket
    {
        public uint Nonce;
        public PacketId Id => PacketId.C2S_RttEcho;
        public void Write(PacketWriter w) => w.WriteUInt32(Nonce);
        public static C2S_RttEcho Read(ref PacketReader r) => new C2S_RttEcho { Nonce = r.ReadUInt32() };
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

    // 11/17 (R2): one packet-routing key press while hacking. RouteId is the S2C_SelfState.RouteId the press answers
    // (a stale or unknown id is dropped by the server); Direction is a RouteDirection (Up 0, Down 1, Left 2, Right 3).
    public sealed class C2S_HackInput : IPacket
    {
        public byte RouteId;
        public byte Direction;
        public PacketId Id => PacketId.C2S_HackInput;

        public void Write(PacketWriter w)
        {
            w.WriteByte(RouteId);
            w.WriteByte(Direction);
        }

        public static C2S_HackInput Read(ref PacketReader r) => new C2S_HackInput
        {
            RouteId = r.ReadByte(), Direction = GameWireValues.ReadRouteDirection(ref r)
        };
    }

    // R1 (11/17): starts a Killer swing. Seq is this connection's attack sequence (separate from MoveInput.Seq); the
    // server aims with the latest MoveInput.Yaw and drops a Seq that is not newer than the last one it accepted.
    public sealed class C2S_Attack : IPacket
    {
        public uint Seq;
        public PacketId Id => PacketId.C2S_Attack;
        public void Write(PacketWriter w) => w.WriteUInt32(Seq);
        public static C2S_Attack Read(ref PacketReader r) => new C2S_Attack { Seq = r.ReadUInt32() };
    }

    // R1 (11/17): the lobby skill choice. SkillId is a role-qualified choice (SkillIds.Choice): 0..4 a Survivor skill,
    // 0x80..0x83 a Killer skill, 0xFF no choice (random). Any other value is a wire-format violation.
    public sealed class C2S_ChooseSkill : IPacket
    {
        public byte SkillId = SkillIds.NoChoice;
        public PacketId Id => PacketId.C2S_ChooseSkill;
        public void Write(PacketWriter w) => w.WriteByte(SkillId);
        public static C2S_ChooseSkill Read(ref PacketReader r) => new C2S_ChooseSkill { SkillId = SkillIds.ReadChoice(ref r) };
    }
}
