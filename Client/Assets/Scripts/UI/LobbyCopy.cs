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

        public static string Title(bool offline) => offline ? "Lobby · offline" : "Lobby";

        public static string RoleLine(byte choice, bool offline)
            => (offline ? "Your role: " : "Host role: ") + (choice == (byte)RoleChoice.Killer ? "Killer" : choice == (byte)RoleChoice.Survivor ? "Survivor" : "Random");
    }
}
