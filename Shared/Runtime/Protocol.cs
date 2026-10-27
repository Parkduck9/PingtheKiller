namespace PingTheKiller.Shared
{
    public static class Protocol
    {
        public const int HeaderSize = 4;
        public const int IdSize = 2;
        public const int MaxBodyLength = 16384;
        public const int MaxNicknameBytes = 48;
        public const int MaxPlayers = 8;
        public const int MaxTerminals = 8;
        public const int LobbySlotCount = 8;
        public const int MaxEscapedIds = 8;
    }
}
