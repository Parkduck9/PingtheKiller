using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Tests;

// v0.4 exit readouts of a WorldState: both exits share the power flag; each exit is open or locked on its own.
internal static class ExitView
{
    private static bool Has(ExitState exit, ExitFlags flag) => (exit.Flags & (byte)flag) != 0;
    internal static bool Powered(this S2C_WorldState w) => w.Exits.Count > 0 && w.Exits.TrueForAll(e => Has(e, ExitFlags.Powered));
    internal static bool AnyOpen(this S2C_WorldState w) => w.Exits.Exists(e => Has(e, ExitFlags.Open));
    internal static bool Open(this S2C_WorldState w, int exit) => Has(w.Exits[exit], ExitFlags.Open);
    internal static bool Locked(this S2C_WorldState w, int exit) => Has(w.Exits[exit], ExitFlags.Locked);
    internal static bool AnyLocked(this S2C_WorldState w) => w.Exits.Exists(e => Has(e, ExitFlags.Locked));
    internal static byte SwitchProgress(this S2C_WorldState w, int exit) => w.Exits[exit].SwitchProgress;
}
