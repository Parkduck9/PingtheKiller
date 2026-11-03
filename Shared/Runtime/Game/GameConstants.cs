using System;

namespace PingTheKiller.Shared.Game
{
    public static class GameConstants
    {
        public const int TickRate = 20;
        public const float SurvivorSpeed = 4.0f;
        public const float KillerSpeed = 4.6f;
        public const float LaggingSpeedMultiplier = 0.25f;
        public const float SignalHoldSeconds = 0.15f;
        public const float CaptureRadius = 1.2f;
        public const float RescueRadius = 1.5f;
        public const float RescueTimeSeconds = 3.0f;
        public const float LaggingTimeoutSeconds = 60f;
        public const float PostRescueInvulnerabilitySeconds = 2f;
        public const float TerminalRadius = 1.5f;
        public const float TerminalHackTimeSeconds = 10f;
        public const int MaxCoHackMultiplier = 3;
        public const float ExitRadius = 2.0f;
        public const float MatchLengthSeconds = 600f;
        public const float ThreatLevel1Distance = 18f;
        public const float ThreatLevel2Distance = 12f;
        public const float ThreatLevel3Distance = 6f;
        public const float KillerSkillCooldownSeconds = 12f;
        public const float SurvivorSkillCooldownSeconds = 15f;

        public static void TerminalsFor(int survivorCount, out int required, out int placed)
        {
            if (survivorCount < 1 || survivorCount > 7)
                throw new ArgumentOutOfRangeException(nameof(survivorCount));
            required = survivorCount <= 2 ? 3 : survivorCount <= 5 ? 4 : 5;
            placed = survivorCount <= 2 ? 5 : survivorCount <= 5 ? 7 : 8;
        }
    }
}
