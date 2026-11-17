using System;
using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Game
{
    public enum Role : byte { Survivor = 0, Killer = 1 }
    public enum RoleChoice : byte { Survivor = 0, Killer = 1, Random = 255 }
    public enum PlayerStatus : byte { Normal = 0, Lagging = 1, Escaped = 2, Eliminated = 3 }
    // v0.4 (GDD v2 ¡×1): the match ends once every survivor escaped or was eliminated, or at the time limit.
    public enum GameEndReason : byte { AllResolved = 0, AllSurvivorsDown = 1, TimeUp = 2, Collapse = 3 }
    [Flags]
    // R1 (11/17): Locked = 4 while a Firewall block holds the terminal (no hacking).
    public enum TerminalFlags : byte { None = 0, Regressing = 1, Overloaded = 2, Locked = 4 }
    [Flags]
    public enum ExitFlags : byte { None = 0, Powered = 1, Open = 2, Locked = 4 }
    public enum SyncResult : byte { Fail = 0, Good = 1, Great = 2 }
    // S2C_SelfState.ActionKind: the own hold action in progress.
    // v0.7 (GDD v2 section 8): Pull, Vault (shutter or rail) and Break (Killer on a shutter).
    public enum ActionKind : byte { None = 0, Switch = 1, Kick = 2, HatchClose = 3, Pull = 4, Vault = 5, Break = 6 }
    [Flags]
    public enum PlayerFlags : byte { None = 0, Signal = 1, Afterimage = 2, Stealthed = 4, Hacking = 8, Rescuing = 16 }

    internal static class GameWireValues
    {
        internal static byte ReadRole(ref PacketReader r) => r.ReadByte((byte)Role.Killer);
        internal static byte ReadStatus(ref PacketReader r) => r.ReadByte((byte)PlayerStatus.Eliminated);
        internal static byte ReadReason(ref PacketReader r) => r.ReadByte((byte)GameEndReason.Collapse);
        internal static byte ReadTerminalFlags(ref PacketReader r) => r.ReadByte((byte)(TerminalFlags.Regressing | TerminalFlags.Overloaded | TerminalFlags.Locked));
        internal static byte ReadExitFlags(ref PacketReader r) => r.ReadByte((byte)(ExitFlags.Powered | ExitFlags.Open | ExitFlags.Locked));
        internal static byte ReadSyncResult(ref PacketReader r) => r.ReadByte((byte)SyncResult.Great);
        internal static byte ReadActionKind(ref PacketReader r) => r.ReadByte((byte)ActionKind.Break);
        internal static byte ReadChaseFlags(ref PacketReader r) => r.ReadByte(
            (byte)(ChaseFlags.Pulling | ChaseFlags.Vaulting | ChaseFlags.Breaking | ChaseFlags.Stunned | ChaseFlags.Swinging));
        internal static byte ReadShutterState(ref PacketReader r) => r.ReadByte((byte)ShutterState.Broken);
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
        // v0.7: ChaseFlags (Pulling = 1, Vaulting = 2, Breaking = 4, Stunned = 8; R1 Swinging = 16); values above 31 are rejected.
        public byte Chase;

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
            w.WriteByte(Chase);
        }

        public static PlayerState Read(ref PacketReader r) => new PlayerState
        {
            Id = r.ReadInt32(), Role = GameWireValues.ReadRole(ref r), Status = GameWireValues.ReadStatus(ref r),
            X = r.ReadSingle(), Z = r.ReadSingle(), Yaw = r.ReadSingle(), Flags = GameWireValues.ReadFlags(ref r),
            StatusTimer = r.ReadSingle(), CaptureCount = r.ReadByte(), Chase = GameWireValues.ReadChaseFlags(ref r)
        };
    }

    public struct TerminalState
    {
        public byte Id;
        public float X;
        public float Z;
        public byte Progress;
        // v0.4: TerminalFlags (Regressing = 1, Overloaded = 2).
        public byte Flags;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Id);
            w.WriteSingle(X);
            w.WriteSingle(Z);
            w.WriteByte(Progress);
            w.WriteByte(Flags);
        }

        public static TerminalState Read(ref PacketReader r) => new TerminalState
        {
            Id = r.ReadByte(), X = r.ReadSingle(), Z = r.ReadSingle(), Progress = r.ReadByte(100),
            Flags = GameWireValues.ReadTerminalFlags(ref r)
        };
    }

    // v0.4: one exit as every recipient sees it. X and Z are the exit point; SwitchProgress is 0..100.
    public struct ExitState
    {
        public byte Id;
        public float X;
        public float Z;
        public byte Flags;
        public byte SwitchProgress;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Id);
            w.WriteSingle(X);
            w.WriteSingle(Z);
            w.WriteByte(Flags);
            w.WriteByte(SwitchProgress);
        }

        public static ExitState Read(ref PacketReader r) => new ExitState
        {
            Id = r.ReadByte(), X = r.ReadSingle(), Z = r.ReadSingle(), Flags = GameWireValues.ReadExitFlags(ref r),
            SwitchProgress = r.ReadByte(100)
        };
    }

    public sealed class Slot
    {
        public int PlayerId;
        public string Nickname = string.Empty;
        public byte IsBot;
        public byte IsHost;
        // R1 (11/17): this player's lobby skill choice (SkillIds.Choice encoding; NoChoice = random).
        public byte SkillChoice = SkillIds.NoChoice;

        public void Write(PacketWriter w)
        {
            w.WriteInt32(PlayerId);
            w.WriteString(Nickname, Protocol.MaxNicknameBytes);
            w.WriteByte(IsBot);
            w.WriteByte(IsHost);
            if (!SkillIds.ValidChoice(SkillChoice)) throw new PacketFormatException("Undefined skill choice.");
            w.WriteByte(SkillChoice);
        }

        public static Slot Read(ref PacketReader r) => new Slot
        {
            PlayerId = r.ReadInt32(), Nickname = r.ReadString(Protocol.MaxNicknameBytes),
            IsBot = r.ReadByte(), IsHost = r.ReadByte(), SkillChoice = SkillIds.ReadChoice(ref r)
        };
    }
}
