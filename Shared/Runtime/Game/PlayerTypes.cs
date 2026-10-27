using System;
using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Game
{
    public enum Role : byte { Survivor = 0, Killer = 1 }
    public enum RoleChoice : byte { Survivor = 0, Killer = 1, Random = 255 }
    public enum PlayerStatus : byte { Normal = 0, Lagging = 1, Escaped = 2, Eliminated = 3 }
    public enum GameEndReason : byte { SurvivorEscaped = 0, AllSurvivorsDown = 1, TimeUp = 2 }
    [Flags]
    public enum PlayerFlags : byte { None = 0, Signal = 1, Afterimage = 2, Stealthed = 4, Hacking = 8, Rescuing = 16 }

    internal static class GameWireValues
    {
        internal static byte ReadRole(ref PacketReader r) => r.ReadByte((byte)Role.Killer);
        internal static byte ReadStatus(ref PacketReader r) => r.ReadByte((byte)PlayerStatus.Eliminated);
        internal static byte ReadReason(ref PacketReader r) => r.ReadByte((byte)GameEndReason.TimeUp);
        internal static byte ReadFlags(ref PacketReader r) => r.ReadByte(
            (byte)(PlayerFlags.Signal | PlayerFlags.Afterimage | PlayerFlags.Stealthed | PlayerFlags.Hacking | PlayerFlags.Rescuing));

        internal static byte ReadRoleChoice(ref PacketReader r)
        {
            byte value = r.ReadByte();
            if (value != (byte)RoleChoice.Survivor && value != (byte)RoleChoice.Killer && value != (byte)RoleChoice.Random)
                throw new PacketFormatException("Undefined role choice.");
            return value;
        }
    }

    public struct PlayerState
    {
        public int Id;
        public byte Role;
        public byte Status;
        public float X;
        public float Z;
        public float Yaw;
        public byte Flags;
        public float StatusTimer;
        public byte CaptureCount;

        public void Write(PacketWriter w)
        {
            w.WriteInt32(Id);
            w.WriteByte(Role);
            w.WriteByte(Status);
            w.WriteSingle(X);
            w.WriteSingle(Z);
            w.WriteSingle(Yaw);
            w.WriteByte(Flags);
            w.WriteSingle(StatusTimer);
            w.WriteByte(CaptureCount);
        }

        public static PlayerState Read(ref PacketReader r) => new PlayerState
        {
            Id = r.ReadInt32(), Role = GameWireValues.ReadRole(ref r), Status = GameWireValues.ReadStatus(ref r),
            X = r.ReadSingle(), Z = r.ReadSingle(), Yaw = r.ReadSingle(), Flags = GameWireValues.ReadFlags(ref r),
            StatusTimer = r.ReadSingle(), CaptureCount = r.ReadByte()
        };
    }

    public struct TerminalState
    {
        public byte Id;
        public float X;
        public float Z;
        public byte Progress;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Id);
            w.WriteSingle(X);
            w.WriteSingle(Z);
            w.WriteByte(Progress);
        }

        public static TerminalState Read(ref PacketReader r) => new TerminalState
        {
            Id = r.ReadByte(), X = r.ReadSingle(), Z = r.ReadSingle(), Progress = r.ReadByte(100)
        };
    }

    public sealed class Slot
    {
        public int PlayerId;
        public string Nickname = string.Empty;
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
