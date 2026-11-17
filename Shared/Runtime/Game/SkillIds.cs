using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Game
{
    // Skill ids are role-local and follow the GDD §4 table order. Durations and radii are GDD §4 values.
    public static class SkillIds
    {
        public const byte KillerSkillCount = 4;
        public const byte SurvivorSkillCount = 5;

        // Killer skills.
        public const byte PingScanner = 0;
        public const byte Overclock = 1;
        // R1 (11/17): the former passive Packet afterimage, now the active Packet trace (same id).
        public const byte PacketTrace = 2;
        public const byte FirewallBlock = 3;

        // Survivor skills.
        public const byte DummyPacket = 0;
        public const byte StealthRelay = 1;
        public const byte TrafficOverload = 2;
        public const byte EmergencyRestore = 3;
        public const byte WhiteNoise = 4;

        // R1 (11/17): 1.5 -> 3.0 s.
        public const float PingScannerExposeSeconds = 3f;
        public const float OverclockSeconds = 3f;
        public const float OverclockSpeedMultiplier = 1.5f;
        // R1 (11/17): the 1 s freeze after the boost became 0.3 s at 60 % speed.
        public const float OverclockRecoverySeconds = 0.3f;
        public const float OverclockRecoverySpeedMultiplier = 0.6f;
        // R1 (11/17): Packet trace forces every Survivor within PacketTraceRadius of the Killer Signal.
        public const float PacketTraceRadius = 12f;
        public const float PacketTraceSeconds = 2.5f;
        public const float FirewallBlockSeconds = 5f;
        // R1 (11/17): without an Exit switch in progress, Firewall block locks the unfinished terminal with the most progress.
        public const float FirewallTerminalSeconds = 8f;
        public const float DummyPacketSeconds = 3f;
        public const float StealthRelaySeconds = 2f;
        public const float StealthRelayRadius = 5f;
        public const float TrafficOverloadSeconds = 2f;
        public const float TrafficOverloadKillerSpeedMultiplier = 0.5f;
        public const float EmergencyRestoreRadius = 1.5f;
        public const float WhiteNoiseSeconds = 1.5f;
        public const int WhiteNoiseSignalCount = 5;

        // R1 (11/17) lobby skill choice (C2S_ChooseSkill, Slot.SkillChoice): one role-qualified byte. 0..4 a Survivor
        // skill, KillerChoiceFlag | 0..3 a Killer skill, NoChoice for a random skill.
        public const byte NoChoice = 0xFF;
        public const byte KillerChoiceFlag = 0x80;

        public static byte Choice(Role role, byte skillId) =>
            role == Role.Killer ? (byte)(KillerChoiceFlag | skillId) : skillId;

        // True for a defined choice other than NoChoice; role and id of the chosen skill.
        public static bool TryParseChoice(byte choice, out Role role, out byte skillId)
        {
            role = (choice & KillerChoiceFlag) != 0 ? Role.Killer : Role.Survivor;
            skillId = (byte)(choice & ~KillerChoiceFlag);
            return choice != NoChoice && skillId < (role == Role.Killer ? KillerSkillCount : SurvivorSkillCount);
        }

        public static bool ValidChoice(byte choice) => choice == NoChoice || TryParseChoice(choice, out _, out _);

        internal static byte ReadChoice(ref PacketReader r)
        {
            byte value = r.ReadByte();
            if (!ValidChoice(value)) throw new PacketFormatException("Undefined skill choice.");
            return value;
        }
    }
}
