using System;
using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.UI
{
    public static class LobbyCopy
    {
        // Nickname plus markers only: "Bot 3 · bot", "Player · host · you". Humans get no kind word.
        public static string SlotText(Slot slot, int localId)
        {
            if (slot == null || slot.PlayerId == 0) return "Empty";
            return slot.Nickname + (slot.IsBot != 0 ? " · bot" : string.Empty) + (slot.IsHost != 0 ? " · host" : string.Empty)
                + (slot.PlayerId == localId ? " · you" : string.Empty);
        }

        // Display order only (the server's slot order is unchanged): the host first, then the other humans, then bots,
        // then empty slots; server order is kept inside each group. The result always has slots.Count entries.
        public static Slot[] DisplayOrder(IReadOnlyList<Slot> slots)
        {
            int count = slots?.Count ?? 0, next = 0;
            var ordered = new Slot[count];
            for (int rank = 0; rank < 4; rank++)
                for (int i = 0; i < count; i++)
                    if (Rank(slots[i]) == rank) ordered[next++] = slots[i];
            return ordered;
        }

        private static int Rank(Slot slot)
        {
            if (slot == null || slot.PlayerId == 0) return 3;
            if (slot.IsBot != 0) return 2;
            return slot.IsHost != 0 ? 0 : 1;
        }

        // The server names a bot "Bot " + its PlayerId, so ids taken by humans leave gaps (player in slot 4: Bot 1, 2, 3, 5).
        // Returns a copy of the state (new Slot objects; the received one is left as it was) in which every bot that still has
        // that default name is renamed Bot 1..N by ascending PlayerId among those bots. Any other name (a human who left and
        // became a bot keeps theirs) stays, and the numbering skips every name it keeps. Every client computes the same names
        // from the same state. Null stays null.
        public static S2C_LobbyState ContiguousBotNames(S2C_LobbyState state)
        {
            if (state == null) return null;
            var copy = new S2C_LobbyState { IsOffline = state.IsOffline, HostRoleChoice = state.HostRoleChoice };
            if (state.Slots == null) { copy.Slots = null; return copy; }
            copy.Slots = new Slot[state.Slots.Length];
            var kept = new HashSet<string>(StringComparer.Ordinal);
            var renamed = new List<Slot>();
            for (int i = 0; i < state.Slots.Length; i++)
            {
                Slot slot = state.Slots[i];
                if (slot == null) continue;
                var clone = new Slot { PlayerId = slot.PlayerId, Nickname = slot.Nickname, IsBot = slot.IsBot, IsHost = slot.IsHost };
                copy.Slots[i] = clone;
                if (slot.PlayerId == 0) continue;
                if (DefaultBotName(slot)) renamed.Add(clone);
                else if (slot.Nickname != null) kept.Add(slot.Nickname);
            }
            // Numbering order only (ascending PlayerId); the slot array keeps the server order.
            for (int i = 1; i < renamed.Count; i++)
                for (int j = i; j > 0 && renamed[j].PlayerId < renamed[j - 1].PlayerId; j--)
                    (renamed[j], renamed[j - 1]) = (renamed[j - 1], renamed[j]);
            int number = 0;
            foreach (Slot bot in renamed)
            {
                string name;
                do name = BotName(++number); while (kept.Contains(name));
                bot.Nickname = name;
            }
            return copy;
        }

        private static bool DefaultBotName(Slot slot) => slot.IsBot != 0 && slot.Nickname == BotName(slot.PlayerId);

        private static string BotName(int number) => "Bot " + number.ToString(CultureInfo.InvariantCulture);

        public static string Title(bool offline) => offline ? "Lobby · offline" : "Lobby";

        public static string RoleLine(byte choice, bool offline)
            => (offline ? "Your role: " : "Host role: ") + (choice == (byte)RoleChoice.Killer ? "Killer" : choice == (byte)RoleChoice.Survivor ? "Survivor" : "Random");
    }
}
