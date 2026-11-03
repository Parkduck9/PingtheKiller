using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Shared.Game
{
    // Protocol v0.4 readers of the per-exit states in a WorldState (GDD v2: two exits, each Powered, Open or switch-Locked).
    public static class WorldExits
    {
        public static bool Has(ExitState exit, ExitFlags flag) => (exit.Flags & (byte)flag) != 0;

        public static bool AnyOpen(S2C_WorldState world) => Any(world, ExitFlags.Open);

        // A switch locked by the Killer's Firewall Block.
        public static bool AnyLocked(S2C_WorldState world) => Any(world, ExitFlags.Locked);

        // Both exits have power (the required terminals are done or the Killer closed the hatch).
        public static bool Powered(S2C_WorldState world)
        {
            if (world?.Exits == null || world.Exits.Count == 0) return false;
            for (int i = 0; i < world.Exits.Count; i++)
                if (!Has(world.Exits[i], ExitFlags.Powered)) return false;
            return true;
        }

        private static bool Any(S2C_WorldState world, ExitFlags flag)
        {
            if (world?.Exits == null) return false;
            for (int i = 0; i < world.Exits.Count; i++)
                if (Has(world.Exits[i], flag)) return true;
            return false;
        }
    }
}
