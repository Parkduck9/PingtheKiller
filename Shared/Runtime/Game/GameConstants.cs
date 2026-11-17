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
        // Pre-R1 automatic capture radius. R1 (11/17): captures come only from a Killer swing (Swing* below); this value
        // still sizes the bots' safety margins and the Killer client's capture-confirm fallback.
        public const float CaptureRadius = 1.2f;
        // R1 (11/17) Killer swing (server-authoritative, C2S_Attack): a 0.2 s wind-up at half speed, then one hit check
        // (Normal, non-invulnerable Survivors within SwingRange in 2D, within SwingHalfAngleDegrees of the look yaw, in
        // line of sight; the nearest is captured), then a recovery: SwingHitRecoverySeconds at 0 % after a hit,
        // SwingMissRecoverySeconds at SwingMissSpeedMultiplier after a miss. No new swing until the recovery ends.
        // R1 balance (BalanceSweep): brief 2.2 m / 35 deg / 1.0 s at 40 % moved to 2.6 m / 40 deg / 0.8 s at 48 % (all within +-20 %).
        // INTEG-R balance (11/17): range 2.6 -> 2.75 m (+6 %), with the bot changes, for a stronger Killer after the R1-R3 merge.
        // B1 balance (11/18): range 2.75 -> 2.95 m (+7 %) for a stronger Killer at 1v7 (the gain grows with the team size:
        // 1v7 -7 points, 1v3 -1 over 1000 seeds). The Killer bot's swing trigger stays 2.75 m (KillerBrain.SwingTrigger).
        public const float SwingWindUpSeconds = 0.2f;
        public const float SwingWindUpSpeedMultiplier = 0.5f;
        public const float SwingRange = 2.95f;
        public const float SwingHalfAngleDegrees = 40f;
        public const float SwingHitRecoverySeconds = 0.6f;
        public const float SwingMissRecoverySeconds = 0.8f;
        public const float SwingMissSpeedMultiplier = 0.48f;
        // Accepted C2S_Attack packets per player per second; more are dropped.
        public const int MaxAttacksPerSecond = 5;
        public const float RescueRadius = 1.5f;
        public const float RescueTimeSeconds = 3.0f;
        // GDD v2 §4: Lagging after the first capture lasts 60 s, after the second 30 s; the third capture eliminates.
        public const float LaggingTimeoutSeconds = 60f;
        public const float SecondLaggingTimeoutSeconds = 30f;
        public const int EliminatingCaptureCount = 3;
        public const float PostRescueInvulnerabilitySeconds = 2f;
        // R3: a rescued Survivor cannot move, interact or use a skill while getting up (inside the invulnerability).
        public const float GetUpSeconds = 1.5f;
        public const float TerminalRadius = 1.5f;
        // GDD v2 §2 (11/17, R2): the passive part of hacking. One idle holder needs 90 s; co-hackers speed the passive part up
        // with diminishing returns (see CoHackRate). Packet routing bursts come on top (Route* below).
        public const float TerminalPassiveSeconds = 90f;
        public const float TwoHackerRate = 1.8f;
        public const float ThreeHackerRate = 2.4f;
        // GDD v2 §2 (killer kick): hold 2 s within TerminalRadius, then -5 points at once and -0.5 points/s until re-hacked.
        public const float KickHoldSeconds = 2f;
        public const float KickPenaltyPoints = 5f;
        public const float RegressionPointsPerSecond = 0.5f;
        // GDD v2 §2 (packet routing, 11/17 R2): each hacker gets its own route of RouteLength directions. The full route adds
        // RouteBurstPoints times RouteBurstShare(n) (a co-hack falloff, see there); a wrong direction costs
        // RouteErrorPenaltyPoints, deals a new route at once and flags the terminal Overloaded for OverloadedSeconds. After a
        // completed route the next one arrives after RouteGapSeconds of hacking, and never sooner than RouteMinCycleSeconds
        // after the completed one was dealt (so typing faster than about 0.15 s per key gains nothing). Route inputs above
        // RouteInputsPerSecond (any 1 s window) are dropped. Attempt 2: the gap is short so the key pace is most of the cycle.
        public const int RouteLength = 4;
        public const float RouteBurstPoints = 3.4f;
        public const float RouteErrorPenaltyPoints = 2f;
        public const float RouteGapSeconds = 0.7f;
        public const float RouteMinCycleSeconds = 1.3f;
        public const int RouteInputsPerSecond = 12;
        public const float OverloadedSeconds = 1.5f;
        // GDD v2 §3: both exits are powered once the required terminals are done; a switch is held 17 s within 1.5 m.
        // B1 balance (11/18): 15 -> 17 s, so the Killer has time to reach a switch before the exits open.
        public const float ExitRadius = 2.0f;
        public const float ExitSwitchRadius = 1.5f;
        public const float ExitSwitchSeconds = 17f;
        // GDD v2 §3: the collapse runs 120 s from the first open exit (or the hatch closing); Lagging clocks run 2x.
        public const float CollapseSeconds = 120f;
        public const float CollapseLaggingRate = 2f;
        // GDD v2 §3 (hatch): the last survivor escapes within 1.5 m; the killer closes it with a 1 s hold.
        public const float HatchRadius = 1.5f;
        public const float HatchCloseSeconds = 1f;
        public const float HatchSightDistance = 15f;
        // GDD v2 §1: 15 minutes as a safety net; matches normally end through the collapse.
        public const float MatchLengthSeconds = 900f;
        public const float ThreatLevel1Distance = 18f;
        public const float ThreatLevel2Distance = 12f;
        public const float ThreatLevel3Distance = 6f;
        // GDD v2 §8 (Q5a): shutters and vault rails. Ranges are measured to the prop box (shutters) or the vault point
        // (rails). A Survivor is forced Signal during a pull or vault and for ChaseSignalTailSeconds after.
        public const float ShutterPullRadius = 1.5f;
        public const float ShutterPullSeconds = 0.3f;
        public const float ShutterStunSeconds = 2.0f;
        public const float ShutterStunInflate = 0.6f;
        public const float ShutterVaultRadius = 1.0f;
        public const float ShutterVaultSeconds = 1.0f;
        public const float ShutterBreakRadius = 1.5f;
        public const float ShutterBreakSeconds = 2.0f;
        public const float RailVaultRadius = 1.0f;
        public const float RailVaultSurvivorSeconds = 0.8f;
        public const float RailVaultKillerSeconds = 1.6f;
        public const float ChaseSignalTailSeconds = 1.0f;
        public const float ShutterHeight = 1.2f;
        public const float RailHeight = 1.0f;
        public const float KillerSkillCooldownSeconds = 12f;
        public const float SurvivorSkillCooldownSeconds = 15f;

        public static void TerminalsFor(int survivorCount, out int required, out int placed)
        {
            if (survivorCount < 1 || survivorCount > 7)
                throw new ArgumentOutOfRangeException(nameof(survivorCount));
            required = survivorCount <= 2 ? 3 : survivorCount <= 5 ? 4 : 5;
            placed = survivorCount <= 2 ? 5 : survivorCount <= 5 ? 7 : 8;
        }

        // GDD v2 §2: effective hack rate of n hackers on one terminal (1 -> 1.0, 2 -> 1.8, 3 or more -> 2.4).
        public static float CoHackRate(int hackers) =>
            hackers <= 0 ? 0f : hackers == 1 ? 1f : hackers == 2 ? TwoHackerRate : ThreeHackerRate;

        // 11/17 (R2, attempt 2): the share of a route burst each of n co-hackers adds: the square of the passive per-head
        // falloff (CoHackRate(n) / n), so two fast co-hackers need about 16 s, not 2x a solo hacker.
        public static float RouteBurstShare(int hackers)
        {
            if (hackers <= 1) return 1f;
            float perHead = CoHackRate(hackers) / hackers;
            return perHead * perHead;
        }
    }
}
