using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // U2d fair-information rule: a bot brain sees the match only through one of these query objects, filled by the server
    // with exactly what a human client of that role receives (WorldState, SelfState and its own inputs). Brains are separate
    // classes without any reference to GameServer or Player, so they cannot read anything else.

    // What a survivor client knows: its own state, every survivor entry of its WorldState (teammates included), exact
    // terminal progress, the exit, ThreatLevel (distance band only) and the killer position only while its own line of
    // sight is clear (GDD §2, FixedMap.SegmentBlocked between the two server positions).
    internal sealed class SurvivorBotView
    {
        internal double Time;
        internal int SelfId;
        internal float X, Z, Speed;
        internal PlayerStatus Status;
        internal byte Skill;
        internal bool SkillReady;
        // The survivor entries of the latest WorldState, the bot's own entry included (empty before the first frame).
        internal readonly List<PlayerState> Team = new List<PlayerState>();
        internal TerminalState[] Terminals = new TerminalState[0];
        internal int TerminalCount;
        internal byte TerminalsRequired;
        internal bool ExitOpen, ExitLocked;
        internal MapPoint Exit;
        internal byte Threat;
        internal bool KillerVisible;
        internal float KillerX, KillerZ, KillerYaw;
    }

    // What the killer client knows: its own state and the anonymous killer view (Signal survivors, phantoms, afterimages
    // and Lagging survivors by position and status only; no Id, no Silent survivor), terminals as done or not done.
    internal sealed class KillerBotView
    {
        internal struct Blip
        {
            internal float X, Z;
            internal bool Lagging;
        }

        internal struct Terminal
        {
            internal float X, Z;
            internal bool Done;
        }

        internal double Time;
        internal float X, Z, Yaw, Speed;
        internal byte Skill;
        internal bool SkillReady;
        internal readonly List<Blip> Blips = new List<Blip>();
        // SelfState.ExecutionGraceLeft: the grace of the nearest Lagging survivor within 3 m (0 if none).
        internal float ExecutionGraceLeft;
        internal Terminal[] Terminals = new Terminal[0];
        internal int TerminalCount;
        internal bool ExitOpen, ExitLocked;
        internal MapPoint Exit;
    }

    // A brain's output for one tick, applied through the same paths as client packets (MoveInput, Interact, UseSkill).
    internal struct BotIntent
    {
        internal float DirX, DirZ, Yaw;
        internal bool Interact, UseSkill;
    }
}
