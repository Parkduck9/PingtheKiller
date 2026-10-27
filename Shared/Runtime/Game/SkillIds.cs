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
        public const byte PacketAfterimage = 2; // Passive: never activated through C2S_UseSkill.
        public const byte FirewallBlock = 3;

        // Survivor skills.
        public const byte DummyPacket = 0;
        public const byte StealthRelay = 1;
        public const byte TrafficOverload = 2;
        public const byte EmergencyRestore = 3;
        public const byte WhiteNoise = 4;

        public const float PingScannerExposeSeconds = 1.5f;
        public const float OverclockSeconds = 3f;
        public const float OverclockSpeedMultiplier = 1.5f;
        public const float OverclockRecoverySeconds = 1f;
        public const float AfterimageSeconds = 1f;
        public const float FirewallBlockSeconds = 5f;
        public const float DummyPacketSeconds = 3f;
        public const float StealthRelaySeconds = 2f;
        public const float StealthRelayRadius = 5f;
        public const float TrafficOverloadSeconds = 2f;
        public const float TrafficOverloadKillerSpeedMultiplier = 0.5f;
        public const float EmergencyRestoreRadius = 1.5f;
        public const float WhiteNoiseSeconds = 1.5f;
        public const int WhiteNoiseSignalCount = 5;
    }
}
