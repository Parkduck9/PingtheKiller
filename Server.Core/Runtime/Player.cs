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
        internal int BotTarget = -1;
        internal double BotPauseUntil;
        internal double BotWalkUntil;
        internal bool HasLastSeen;
        internal float LastSeenX, LastSeenZ;

        internal bool IsSignal(double now) => now - LastSignal < GameConstants.SignalHoldSeconds - 0.0000001;
        internal PlayerState State(double now) => new PlayerState
        {
            Id = Id, Role = (byte)Role, Status = (byte)PlayerStatus.Normal,
            X = X, Z = Z, Yaw = Yaw,
            Flags = IsSignal(now) ? (byte)PlayerFlags.Signal : (byte)PlayerFlags.None
        };
    }
}
