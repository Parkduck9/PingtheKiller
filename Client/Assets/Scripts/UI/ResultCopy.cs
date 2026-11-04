namespace PingTheKiller.Client.UI
{
    // Result screen lines outside the rules v2 body (HUD_SPEC 7.5, 13.7): whether the lobby is open again, and the dev
    // preview note. The headline, outcome, rule, reason and rows are ResultV2Copy.
    public static class ResultCopy
    {
        public const string Waiting = "Waiting for the host to reopen the lobby.";
        public const string WaitingOffline = "Waiting for the lobby to reopen.";
        public const string LobbyOpen = "The lobby is open again.";
        public const string PreviewNote = "Preview: synthetic result from a dev flag, not sent by a server.";

        public static string LobbyLine(bool lobbyOpen, bool offline) => lobbyOpen ? LobbyOpen : offline ? WaitingOffline : Waiting;
    }
}
