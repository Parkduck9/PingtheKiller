namespace PingTheKiller.Shared
{
    public static class Protocol
    {
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
    }
}
