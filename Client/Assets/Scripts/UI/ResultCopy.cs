using System.Collections.Generic;
using System.Text;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.UI
{
    // Pure S2C_GameResult copy: winner side + reason in plain words, the local player's own outcome. No banner words.
    public static class ResultCopy
    {
        public const string Waiting = "Waiting for the host to reopen the lobby.";
        public const string WaitingOffline = "Waiting for the lobby to reopen.";
        public const string LobbyOpen = "The lobby is open again.";

        public static string Headline(S2C_GameResult result, S2C_LobbyState lobby)
        {
            switch ((GameEndReason)result.Reason)
            {
                case GameEndReason.SurvivorEscaped:
                    string names = Names(result.EscapedIds, lobby);
                    return names.Length == 0 ? "Survivors win. A survivor reached the Exit." : "Survivors win. " + names + " reached the Exit.";
                case GameEndReason.AllSurvivorsDown: return "Killer wins. No survivor is left in play.";
                default: return "Killer wins. Time ran out.";
            }
        }

        public static string Outcome(S2C_GameResult result, int localId, Role localRole, PlayerStatus localStatus)
        {
            bool survivorsWon = result.Reason == (byte)GameEndReason.SurvivorEscaped;
            if (localRole == Role.Killer) return survivorsWon ? "You lost as the Killer." : "You won as the Killer.";
            if (result.EscapedIds != null && result.EscapedIds.Contains(localId) || localStatus == PlayerStatus.Escaped) return "You escaped.";
            if (localStatus == PlayerStatus.Eliminated) return "You were eliminated.";
            if (localStatus == PlayerStatus.Lagging) return "You were still Lagging when the match ended.";
            return survivorsWon ? "Your team won. You did not reach the Exit." : "You did not escape.";
        }

        public static string NameOf(int id, S2C_LobbyState lobby)
        {
            if (lobby?.Slots != null)
                foreach (Slot slot in lobby.Slots)
                    if (slot != null && slot.PlayerId == id && !string.IsNullOrEmpty(slot.Nickname)) return slot.Nickname;
            return "Player " + id;
        }

        // "A", "A and B", "A, B and C".
        public static string Names(IReadOnlyList<int> ids, S2C_LobbyState lobby)
        {
            var text = new StringBuilder();
            int count = ids?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) text.Append(i == count - 1 ? " and " : ", ");
                text.Append(NameOf(ids[i], lobby));
            }
            return text.ToString();
        }

        public static string LobbyLine(bool lobbyOpen, bool offline) => lobbyOpen ? LobbyOpen : offline ? WaitingOffline : Waiting;
    }
}
