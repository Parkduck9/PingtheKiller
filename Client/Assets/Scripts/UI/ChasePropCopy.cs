namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 15 (Q5): Shutter and Rail copy. New fixed terms: Shutter, Rail, vault (verb), pull (verb), force (verb,
    // Killer), Stunned. Same pattern as the HUD_SPEC 13.3 prompts ("Press [E] to ...", "Hold [E] to ..."): one action, one
    // fact, no exclamation marks. A Survivor line names the Signal cost, because every Shutter and Rail action makes the
    // Survivor Signal (the server forces it during the action and for 1 s after). Killer lines are at most 38 characters.
    public static class ChasePropCopy
    {
        // Survivor prompt row (672x40, M).
        public const string Pull = "Press [E] to pull the Shutter down — you will be Signal";
        public const string VaultShutter = "Press [E] to vault the Shutter — you will be Signal";
        public const string VaultRail = "Press [E] to vault the Rail — you will be Signal";
        // Survivor action rows while the server runs the action (no bar: the actions are 0.3-1.0 s).
        public const string Pulling = "Pulling the Shutter — you are Signal";
        public const string Vaulting = "Vaulting — you are Signal";

        // Killer prompt block (6.1p): line 1 M 600, line 2 S `muted`.
        public const string KillerVaultRail = "Press [E] to vault the Rail";
        public const string KillerVaultHint = "Takes 1.6 s · you cannot move";
        public const string Force = "Hold [E] to force the Shutter";
        public const string ForceHint = "Stand still · 2 s breaks it";
        // Killer hold block while forcing: "Forcing the Shutter · 55%" + this hint.
        public const string Forcing = "Forcing the Shutter";
        public const string ForcingHint = "Hold [E] · moving cancels";
        // Killer hold block while its own rail vault runs.
        public const string KillerVaulting = "Vaulting the Rail";
        public const string KillerVaultingHint = "You cannot move until you land";
        // Killer effect line (6.1c) while the server reports Stunned. Kept to one word (brief Q5b S2.2).
        public const string Stunned = "Stunned";

        public static string ForcingLabel(int percent) => Forcing + " · " + percent + "%";
    }
}
