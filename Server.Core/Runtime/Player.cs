using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    internal sealed class Player
    {
        internal int Id;
        internal string Nickname;
        internal bool IsBot;
        internal bool IsHost;
        internal ulong Token;
        internal string ConnectionId;
        internal double DisconnectedAt;
        internal Role Role;
        internal byte Skill;
        internal float X, Z, Yaw, DirX, DirZ;
        internal uint Seq;
        internal bool HasSeq;
        internal double LastSignal = double.NegativeInfinity;
        internal readonly System.Collections.Generic.Queue<double> InputTimes = new System.Collections.Generic.Queue<double>();

        // Match state, owned by the game-loop thread and reset by ResetForMatch.
        internal PlayerStatus Status;
        internal byte CaptureCount;
        internal double LaggingUntil;
        internal double InvulnerableUntil;
        internal double RescueProgress;
        internal bool ExecutionArmed;
        internal double ExecutionGraceUntil;
        internal bool Interacting, Hacking, Rescuing;
        internal double SkillReadyAt;
        internal double ExposedUntil, StealthUntil;
        internal double OverclockUntil, RecoveryUntil, SlowedUntil;
        // Effective Signal for the current tick and the last position the killer saw (Packet Afterimage).
        internal bool Visible;
        internal double LastVisibleAt = double.NegativeInfinity;
        internal float VisibleX, VisibleZ, VisibleYaw;

        internal int BotTarget = -1;
        internal double BotPauseUntil;
        internal double BotWalkUntil;
        internal bool HasLastSeen;
        internal float LastSeenX, LastSeenZ;
        internal float BotLastX, BotLastZ;
        internal double BotDetourUntil;
        internal float BotDetourX, BotDetourZ;
        // Survivor bot plan (GameServer.Bots): current mode, reaction timer, burst pacing, threat hold, terminal claim.
        internal byte BotMode;
        internal double BotThinkAt, BotBurstUntil, BotBraveUntil;
        internal double BotThreatSince = double.NegativeInfinity;
        internal int BotClaim = -1, BotAvoid = -1;
        internal float BotGoalX, BotGoalZ, BotGoalStop;
        internal bool BotGoalHack, BotRelocating;
        // The last S2C_SelfState sent to this player's connection; null forces the next one.
        internal PingTheKiller.Shared.Packets.S2C_SelfState SelfSent;
        internal double SelfSentAt = double.NegativeInfinity;
        internal float BotHideX, BotHideZ;

        internal void ResetBotPlan()
        {
            BotTarget = -1;
            BotPauseUntil = BotWalkUntil = BotDetourUntil = 0;
            BotMode = 0;
            BotThinkAt = BotBurstUntil = BotBraveUntil = 0;
            BotThreatSince = double.NegativeInfinity;
            BotClaim = BotAvoid = -1;
            BotGoalHack = BotRelocating = false;
        }

        // The latest non-zero MoveInput keeps the direction alive for SignalHoldSeconds.
        internal bool InputHeld(double now) => now - LastSignal < GameConstants.SignalHoldSeconds - 0.0000001;

        internal void ResetForMatch(double now)
        {
            Status = PlayerStatus.Normal;
            CaptureCount = 0;
            LaggingUntil = InvulnerableUntil = RescueProgress = 0;
            ExecutionArmed = false;
            Interacting = Hacking = Rescuing = false;
            SkillReadyAt = now;
            ExposedUntil = StealthUntil = OverclockUntil = RecoveryUntil = SlowedUntil = double.NegativeInfinity;
            Visible = false;
            LastVisibleAt = double.NegativeInfinity;
            DirX = DirZ = 0;
            LastSignal = double.NegativeInfinity;
            // A client may restart Seq at 1 for a new match.
            HasSeq = false;
            Seq = 0;
            InputTimes.Clear();
            ExecutionGraceUntil = 0;
            SelfSent = null;
            ResetBotPlan();
            HasLastSeen = false;
            BotLastX = X;
            BotLastZ = Z;
        }

        internal bool IsActiveSurvivor => Role == Role.Survivor && (Status == PlayerStatus.Normal || Status == PlayerStatus.Lagging);
    }
}
