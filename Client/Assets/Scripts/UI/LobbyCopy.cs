using System.Collections.Generic;
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

        public static string Title(bool offline) => offline ? "Lobby · offline" : "Lobby";

        public static string RoleLine(byte choice, bool offline)
            => (offline ? "Your role: " : "Host role: ") + (choice == (byte)RoleChoice.Killer ? "Killer" : choice == (byte)RoleChoice.Survivor ? "Survivor" : "Random");
    }
}
