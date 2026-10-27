using System;
using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Game
{
    public enum Role : byte { Survivor = 0, Killer = 1 }
    public enum PlayerStatus : byte { Normal, Frozen, Escaped }
    [Flags]
    public enum PlayerFlags : byte { None = 0, Signal = 1, Afterimage = 2, Stealthed = 4 }

    public struct PlayerState
    {
        public int Id;
        public byte Role;
        public byte Status;
        public float X;
        public float Z;
        public float Yaw;
        public byte Flags;

        public void Write(PacketWriter w)
        {
            w.WriteInt32(Id);
            w.WriteByte(Role);
            w.WriteByte(Status);
            w.WriteSingle(X);
            w.WriteSingle(Z);
            w.WriteSingle(Yaw);
            w.WriteByte(Flags);
        }

        public static PlayerState Read(ref PacketReader r) => new PlayerState
        {
            Id = r.ReadInt32(), Role = r.ReadByte(), Status = r.ReadByte(),
            X = r.ReadSingle(), Z = r.ReadSingle(), Yaw = r.ReadSingle(), Flags = r.ReadByte()
        };
    }

    public struct Slot
    {
        public int PlayerId;
        public string Nickname;
        public byte IsBot;
        public byte IsHost;

        public void Write(PacketWriter w)
        {
            w.WriteInt32(PlayerId);
            w.WriteString(Nickname, Protocol.MaxNicknameBytes);
            w.WriteByte(IsBot);
            w.WriteByte(IsHost);
        }

        public static Slot Read(ref PacketReader r) => new Slot
        {
            PlayerId = r.ReadInt32(), Nickname = r.ReadString(Protocol.MaxNicknameBytes),
            IsBot = r.ReadByte(), IsHost = r.ReadByte()
        };
    }
}
