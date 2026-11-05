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
        public byte TerminalsRequired;
        // v0.4: seconds of the global collapse left, -1 while it is not running.
        public float CollapseLeft = -1;
        public List<ExitState> Exits = new List<ExitState>();
        // v0.4: 1 only when the hatch is open and this recipient can see it; then HatchX and HatchZ follow on the wire.
        public byte HatchVisible;
        public float HatchX;
        public float HatchZ;
        public List<TerminalState> Terminals = new List<TerminalState>();
        public List<PlayerState> Players = new List<PlayerState>();
        public PacketId Id => PacketId.S2C_WorldState;

        public void Write(PacketWriter w)
        {
            if (Players == null)
                throw new PacketFormatException("Players cannot be null.");
            if (Terminals == null)
                throw new PacketFormatException("Terminals cannot be null.");
            if (Exits == null)
                throw new PacketFormatException("Exits cannot be null.");
            if (HatchVisible > 1)
                throw new PacketFormatException("HatchVisible must be 0 or 1.");
            w.WriteUInt32(Tick);
            w.WriteSingle(TimeLeft);
            w.WriteByte(ThreatLevel);
            w.WriteByte(TerminalsRequired);
            w.WriteSingle(CollapseLeft);
            w.WriteCount(Exits.Count, Protocol.MaxExits);
            for (int i = 0; i < Exits.Count; i++)
                Exits[i].Write(w);
            w.WriteByte(HatchVisible);
            if (HatchVisible == 1)
            {
                w.WriteSingle(HatchX);
                w.WriteSingle(HatchZ);
            }
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
                TerminalsRequired = r.ReadByte(), CollapseLeft = r.ReadSingle()
            };
            int exits = r.ReadCount(Protocol.MaxExits);
            packet.Exits = new List<ExitState>(exits);
            for (int i = 0; i < exits; i++)
                packet.Exits.Add(ExitState.Read(ref r));
            packet.HatchVisible = r.ReadByte(1);
            if (packet.HatchVisible == 1)
            {
                packet.HatchX = r.ReadSingle();
                packet.HatchZ = r.ReadSingle();
            }
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
        // v0.4: the survivors who were eliminated, so every survivor learns its own outcome.
        public List<int> EliminatedIds = new List<int>();
        public PacketId Id => PacketId.S2C_GameResult;

        public void Write(PacketWriter w)
        {
            if (EscapedIds == null)
                throw new PacketFormatException("Escaped IDs cannot be null.");
            if (EliminatedIds == null)
                throw new PacketFormatException("Eliminated IDs cannot be null.");
            w.WriteByte(WinnerRole);
            w.WriteByte(Reason);
            w.WriteCount(EscapedIds.Count, Protocol.MaxEscapedIds);
            for (int i = 0; i < EscapedIds.Count; i++)
                w.WriteInt32(EscapedIds[i]);
            w.WriteCount(EliminatedIds.Count, Protocol.MaxEliminatedIds);
            for (int i = 0; i < EliminatedIds.Count; i++)
                w.WriteInt32(EliminatedIds[i]);
        }

        public static S2C_GameResult Read(ref PacketReader r)
        {
            var packet = new S2C_GameResult { WinnerRole = GameWireValues.ReadRole(ref r), Reason = GameWireValues.ReadReason(ref r) };
            int count = r.ReadCount(Protocol.MaxEscapedIds);
            packet.EscapedIds = new List<int>(count);
            for (int i = 0; i < count; i++)
                packet.EscapedIds.Add(r.ReadInt32());
            count = r.ReadCount(Protocol.MaxEliminatedIds);
            packet.EliminatedIds = new List<int>(count);
            for (int i = 0; i < count; i++)
                packet.EliminatedIds.Add(r.ReadInt32());
            return packet;
        }
    }

    // v0.5: server-timed RTT; the client echoes this nonce on its receive thread.
    public sealed class S2C_RttProbe : IPacket
    {
        public uint Nonce;
        public PacketId Id => PacketId.S2C_RttProbe;
        public void Write(PacketWriter w) => w.WriteUInt32(Nonce);
        public static S2C_RttProbe Read(ref PacketReader r) => new S2C_RttProbe { Nonce = r.ReadUInt32() };
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

    // Protocol v0.3 (extended in v0.4): sent only to the player it describes, never about another player.
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
        // v0.4: the own collapse clock (a survivor's personal one, the global one for the killer); -1 when not running.
        public float CollapseLeft = -1;
        // v0.4: 0..1 progress of the own hold action named by ActionKind (exit switch, kick or hatch close).
        public float ActionProgress;
        public byte ActionKind;
        public PacketId Id => PacketId.S2C_SelfState;

        public void Write(PacketWriter w)
        {
            w.WriteSingle(SkillCooldown);
            w.WriteSingle(RescueProgress);
            w.WriteSingle(InvulnerableLeft);
            w.WriteSingle(ExecutionGraceLeft);
            w.WriteSingle(CollapseLeft);
            w.WriteSingle(ActionProgress);
            w.WriteByte(ActionKind);
        }

        public static S2C_SelfState Read(ref PacketReader r) => new S2C_SelfState
        {
            SkillCooldown = r.ReadSingle(), RescueProgress = r.ReadSingle(),
            InvulnerableLeft = r.ReadSingle(), ExecutionGraceLeft = r.ReadSingle(),
            CollapseLeft = r.ReadSingle(), ActionProgress = r.ReadSingle(), ActionKind = GameWireValues.ReadActionKind(ref r)
        };
    }

    // v0.4: a sync check for one hacking survivor, sent only to that survivor. The needle sweeps 0 -> 1 in Duration
    // seconds; the zone is [ZoneStart, ZoneStart + ZoneSize] and its first GreatSize is the great part.
    public sealed class S2C_SyncCheck : IPacket
    {
        public uint CheckId;
        public byte TerminalId;
        public float Duration;
        public float ZoneStart;
        public float ZoneSize;
        public float GreatSize;
        public PacketId Id => PacketId.S2C_SyncCheck;

        public void Write(PacketWriter w)
        {
            w.WriteUInt32(CheckId);
            w.WriteByte(TerminalId);
            w.WriteSingle(Duration);
            w.WriteSingle(ZoneStart);
            w.WriteSingle(ZoneSize);
            w.WriteSingle(GreatSize);
        }

        public static S2C_SyncCheck Read(ref PacketReader r) => new S2C_SyncCheck
        {
            CheckId = r.ReadUInt32(), TerminalId = r.ReadByte(), Duration = r.ReadSingle(),
            ZoneStart = r.ReadSingle(), ZoneSize = r.ReadSingle(), GreatSize = r.ReadSingle()
        };
    }

    // v0.4: the server verdict on one sync check (SyncResult: Fail = 0, Good = 1, Great = 2).
    public sealed class S2C_SyncResult : IPacket
    {
        public uint CheckId;
        public byte Result;
        public PacketId Id => PacketId.S2C_SyncResult;

        public void Write(PacketWriter w)
        {
            w.WriteUInt32(CheckId);
            w.WriteByte(Result);
        }

        public static S2C_SyncResult Read(ref PacketReader r) => new S2C_SyncResult
        {
            CheckId = r.ReadUInt32(), Result = GameWireValues.ReadSyncResult(ref r)
        };
    }
}
