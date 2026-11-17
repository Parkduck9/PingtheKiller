using System;
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
        internal bool Interacting, Hacking, Rescuing;
        internal double SkillReadyAt;
        // Q3 skill rejections: the last tick a press was answered (used or rejected) and the per-second log throttle.
        // Kept across match resets; the tick counter never repeats.
        internal long SkillAnsweredTick = -1;
        internal double SkillRejectLoggedAt = double.NegativeInfinity;
        internal int SkillRejectsUnlogged;
        // GDD v2: the terminal hacked and the exit switch held this tick (-1 when none).
        internal int HackTerminal = -1, SwitchExit = -1;
        // Killer holds: seconds of the current kick and hatch close; the kicked terminal (-1 when none).
        internal double KickHeld, HatchHeld;
        internal int KickTerminal = -1;
        // Personal collapse clock (GDD v2 §3).
        internal double CollapseLeft;
        // Packet routing (GDD v2 §2, 11/17 R2): the private direction stream, a survivor bot's key-timing stream, the own
        // route (RouteId 0 = none), its terminal, the next index, the hacking seconds before the next route after a completed
        // one, the route-input rate window and the per-player log throttle.
        internal Random RouteRandom, RouteBotRandom;
        internal byte RouteId, RouteSerial, RouteDirs, RouteIndex;
        internal int RouteTerminal = -1;
        internal double RouteGapLeft;
        // The server time the current route was dealt (RouteMinCycleSeconds).
        internal double RouteIssuedAt;
        internal readonly System.Collections.Generic.Queue<double> RouteInputTimes = new System.Collections.Generic.Queue<double>();
        internal double RouteLoggedAt = double.NegativeInfinity;
        internal int RouteLogsUnlogged;
        // Smoothed round-trip time from the server-timed probe (diagnostics); 0 until a measurement exists.
        internal double RttSeconds;
        // Connection state, deliberately preserved across match resets to keep the lobby estimate warm.
        internal uint RttProbeNonce;
        internal bool RttProbePending;
        internal int RttSampleCount;
        internal double RttProbeSentAt, RttNextProbeAt;
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
        // GDD v2 section 8 (GameServer.ChaseProps): a fresh Interact press, the running pull/vault, the killer's break
        // hold, its stun and a survivor's forced-Signal tail.
        internal bool InteractPressed;
        internal byte ChaseKind;          // ActionKind.None, Pull or Vault while one runs
        internal int ChaseProp = -1;      // shutter id for a pull or a shutter vault, rail id for a rail vault
        internal bool ChaseOnRail;
        internal double ChaseStart, ChaseEnd;
        internal float ChaseFromX, ChaseFromZ, ChaseToX, ChaseToZ, ChaseSide;
        internal int BreakShutter = -1;
        internal double BreakHeld;
        internal double StunnedUntil = double.NegativeInfinity, ChaseSignalUntil = double.NegativeInfinity;

        internal bool InChaseAction => ChaseKind != 0;

        internal void ResetChase()
        {
            InteractPressed = false;
            ChaseKind = 0;
            ChaseProp = BreakShutter = -1;
            ChaseOnRail = false;
            BreakHeld = 0;
            StunnedUntil = ChaseSignalUntil = double.NegativeInfinity;
        }

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
            Interacting = Hacking = Rescuing = false;
            HackTerminal = SwitchExit = KickTerminal = RouteTerminal = -1;
            KickHeld = HatchHeld = 0;
            RouteId = RouteIndex = RouteDirs = 0;
            RouteGapLeft = 0;
            RouteInputTimes.Clear();
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
            ResetChase();
            SelfSent = null;
            ResetBotPlan();
            HasLastSeen = false;
            BotLastX = X;
            BotLastZ = Z;
        }

        internal bool IsActiveSurvivor => Role == Role.Survivor && (Status == PlayerStatus.Normal || Status == PlayerStatus.Lagging);
    }
}
