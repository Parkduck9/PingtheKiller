namespace PingTheKiller.Shared
{
    public static class Protocol
    {
        // The wire protocol version (docs/PROTOCOL.md). v0.8 (INTEG-R, 11/17): C2S_Attack 0x0017, C2S_ChooseSkill 0x0018 and
        // C2S_HackInput 0x0019; the sync check (0x0011-0x0013) retired; ActionKind.Rescue, PlayerFlags.GettingUp,
        // ChaseFlags.Swinging, TerminalFlags.Locked; S2C_SelfState route fields; the lobby slot SkillChoice byte.
        // Documentation only: it is not sent on the wire, both ends compile this same file.
        public const string Version = "0.8";
        public const int HeaderSize = 4;
        public const int IdSize = 2;
        public const int MaxBodyLength = 16384;
        public const int MaxNicknameBytes = 48;
        public const int MaxPlayers = 8;
        // S2C_WorldState entry limit: the 8 players plus the killer view's phantoms and afterimages (v0.3).
        // Only the accepted count changes; the count is still one byte and the layout is unchanged.
        public const int MaxWorldEntries = 16;
        public const int MaxTerminals = 8;
        public const int LobbySlotCount = 8;
        public const int MaxEscapedIds = 8;
        // v0.4: S2C_GameResult.EliminatedIds and S2C_WorldState exits.
        public const int MaxEliminatedIds = 8;
        public const int MaxExits = 2;
        // v0.7: S2C_WorldState shutter states (WorldChaseProps.ShutterCount).
        public const int MaxShutters = 10;
    }
}
