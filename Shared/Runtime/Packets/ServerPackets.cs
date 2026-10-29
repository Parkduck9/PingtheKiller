using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Packets
{
    public sealed class S2C_JoinResult : IPacket
    {
        public int PlayerId;
        public ulong ReconnectToken;
        public byte Result;
        public PacketId Id => PacketId.S2C_JoinResult;

        public void Write(PacketWriter w)
        {
            w.WriteInt32(PlayerId);
            w.WriteUInt64(ReconnectToken);
            w.WriteByte(Result);
        }

        public static S2C_JoinResult Read(ref PacketReader r) => new S2C_JoinResult
        {
            PlayerId = r.ReadInt32(), ReconnectToken = r.ReadUInt64(), Result = r.ReadByte()
        };
    }

    public sealed class S2C_WorldState : IPacket
    {
        public uint Tick;
        public float TimeLeft;
        public byte ThreatLevel;
        public byte ExitOpen;
        public byte ExitLocked;
        public byte TerminalsRequired;
        public List<TerminalState> Terminals = new List<TerminalState>();
        public List<PlayerState> Players = new List<PlayerState>();
        public PacketId Id => PacketId.S2C_WorldState;

        public void Write(PacketWriter w)
        {
            if (Players == null)
                throw new PacketFormatException("Players cannot be null.");
            if (Terminals == null)
                throw new PacketFormatException("Terminals cannot be null.");
            w.WriteUInt32(Tick);
            w.WriteSingle(TimeLeft);
            w.WriteByte(ThreatLevel);
            w.WriteByte(ExitOpen);
            w.WriteByte(ExitLocked);
            w.WriteByte(TerminalsRequired);
            w.WriteCount(Terminals.Count, Protocol.MaxTerminals);
            for (int i = 0; i < Terminals.Count; i++)
                Terminals[i].Write(w);
            w.WriteCount(Players.Count, Protocol.MaxWorldEntries);
            for (int i = 0; i < Players.Count; i++)
                Players[i].Write(w);
        }

        public static S2C_WorldState Read(ref PacketReader r)
        {
            var packet = new S2C_WorldState
            {
                Tick = r.ReadUInt32(), TimeLeft = r.ReadSingle(), ThreatLevel = r.ReadByte(3),
                ExitOpen = r.ReadByte(), ExitLocked = r.ReadByte(), TerminalsRequired = r.ReadByte()
            };
            int terminals = r.ReadCount(Protocol.MaxTerminals);
            packet.Terminals = new List<TerminalState>(terminals);
            for (int i = 0; i < terminals; i++)
                packet.Terminals.Add(TerminalState.Read(ref r));
            int count = r.ReadCount(Protocol.MaxWorldEntries);
            packet.Players = new List<PlayerState>(count);
            for (int i = 0; i < count; i++)
                packet.Players.Add(PlayerState.Read(ref r));
            return packet;
        }
    }

    public sealed class S2C_SkillEffect : IPacket
    {
        public int CasterId;
        public byte SkillId;
        public float X;
        public float Z;
        public float Duration;
        public PacketId Id => PacketId.S2C_SkillEffect;

        public void Write(PacketWriter w)
        {
            w.WriteInt32(CasterId);
            w.WriteByte(SkillId);
            w.WriteSingle(X);
            w.WriteSingle(Z);
            w.WriteSingle(Duration);
        }

        public static S2C_SkillEffect Read(ref PacketReader r) => new S2C_SkillEffect
        {
            CasterId = r.ReadInt32(), SkillId = r.ReadByte(),
            X = r.ReadSingle(), Z = r.ReadSingle(), Duration = r.ReadSingle()
        };
    }

    public sealed class S2C_GameResult : IPacket
    {
        public byte WinnerRole;
        public byte Reason;
        public List<int> EscapedIds = new List<int>();
        public PacketId Id => PacketId.S2C_GameResult;

        public void Write(PacketWriter w)
        {
            if (EscapedIds == null)
                throw new PacketFormatException("Escaped IDs cannot be null.");
            w.WriteByte(WinnerRole);
            w.WriteByte(Reason);
            w.WriteCount(EscapedIds.Count, Protocol.MaxEscapedIds);
            for (int i = 0; i < EscapedIds.Count; i++)
                w.WriteInt32(EscapedIds[i]);
        }

        public static S2C_GameResult Read(ref PacketReader r)
        {
            var packet = new S2C_GameResult { WinnerRole = GameWireValues.ReadRole(ref r), Reason = GameWireValues.ReadReason(ref r) };
            int count = r.ReadCount(Protocol.MaxEscapedIds);
            packet.EscapedIds = new List<int>(count);
            for (int i = 0; i < count; i++)
                packet.EscapedIds.Add(r.ReadInt32());
            return packet;
        }
    }

    public sealed class S2C_Pong : IPacket
    {
        public long Timestamp;
        public PacketId Id => PacketId.S2C_Pong;
        public void Write(PacketWriter w) => w.WriteInt64(Timestamp);
        public static S2C_Pong Read(ref PacketReader r) => new S2C_Pong { Timestamp = r.ReadInt64() };
    }

    public sealed class S2C_LobbyState : IPacket
    {
        public byte IsOffline;
        public byte HostRoleChoice;
        public Slot[] Slots = CreateSlots();
        public PacketId Id => PacketId.S2C_LobbyState;

        private static Slot[] CreateSlots()
        {
            var slots = new Slot[Protocol.LobbySlotCount];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new Slot();
            return slots;
        }

        public void Write(PacketWriter w)
        {
            if (Slots == null)
                throw new PacketFormatException("Slots cannot be null.");
            w.WriteByte(IsOffline);
            w.WriteByte(HostRoleChoice);
            w.WriteCount(Slots.Length, Protocol.LobbySlotCount, exact: true);
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] == null)
                    throw new PacketFormatException("Slots cannot contain null entries.");
                Slots[i].Write(w);
            }
        }

        public static S2C_LobbyState Read(ref PacketReader r)
        {
            var packet = new S2C_LobbyState
            {
                IsOffline = r.ReadByte(), HostRoleChoice = GameWireValues.ReadRoleChoice(ref r)
            };
            int count = r.ReadCount(Protocol.LobbySlotCount, exact: true);
            for (int i = 0; i < count; i++)
                packet.Slots[i] = Slot.Read(ref r);
            return packet;
        }
    }

    public sealed class S2C_MatchStart : IPacket
    {
        public byte Role;
        public byte SkillId;
        public int Seed;
        public PacketId Id => PacketId.S2C_MatchStart;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Role);
            w.WriteByte(SkillId);
            w.WriteInt32(Seed);
        }

        public static S2C_MatchStart Read(ref PacketReader r) => new S2C_MatchStart
        {
            Role = GameWireValues.ReadRole(ref r), SkillId = r.ReadByte(), Seed = r.ReadInt32()
        };
    }

    // Protocol v0.3: sent every tick only to the player it describes, never about another player.
    public sealed class S2C_SelfState : IPacket
    {
        // Seconds until the own skill is ready; 0 when ready.
        public float SkillCooldown;
        // 0..1: the own rescue while Lagging, or the rescue of the ally this survivor is rescuing.
        public float RescueProgress;
        // Seconds of post-rescue capture immunity left.
        public float InvulnerableLeft;
        // Killer only: grace left for the nearest Lagging survivor within 3 m, else 0. Always 0 for survivors.
        public float ExecutionGraceLeft;
        public PacketId Id => PacketId.S2C_SelfState;

        public void Write(PacketWriter w)
        {
            w.WriteSingle(SkillCooldown);
            w.WriteSingle(RescueProgress);
            w.WriteSingle(InvulnerableLeft);
            w.WriteSingle(ExecutionGraceLeft);
        }

        public static S2C_SelfState Read(ref PacketReader r) => new S2C_SelfState
        {
            SkillCooldown = r.ReadSingle(), RescueProgress = r.ReadSingle(),
            InvulnerableLeft = r.ReadSingle(), ExecutionGraceLeft = r.ReadSingle()
        };
    }
}
