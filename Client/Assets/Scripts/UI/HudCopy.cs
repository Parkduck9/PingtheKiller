using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.UI
{
    // Pure play-readout copy (GDD 3.3-3.5, 4). Unit-tested; ClientScreens only displays the results.
    public static class HudCopy
    {
        // Role-local skill IDs in GDD section 4 table order.
        // TODO: switch to Shared SkillIds when U2b merges
        private static readonly string[] KillerSkills = { "Ping Scanner", "Overclock", "Packet Afterimage", "Firewall Block" };
        private static readonly string[] SurvivorSkills = { "Dummy Packet", "Stealth Relay", "Traffic Overload", "Emergency Restore", "White Noise" };
        public const byte KillerPassiveSkill = 2;

        public static string SkillName(Role role, byte skillId)
        {
            string[] table = role == Role.Killer ? KillerSkills : SurvivorSkills;
            return skillId < table.Length ? table[skillId] : "Skill " + skillId;
        }

        public static bool IsPassive(Role role, byte skillId) => role == Role.Killer && skillId == KillerPassiveSkill;

        public static string SkillLine(Role role, byte skillId)
            => (IsPassive(role, skillId) ? "Passive · " : "Q · ") + SkillName(role, skillId);

        // Empty while Normal: the HUD adds nothing for the default state.
        public static string StatusLine(PlayerStatus status, float timer)
        {
            switch (status)
            {
                case PlayerStatus.Lagging:
                    float seconds = float.IsNaN(timer) || timer < 0 ? 0 : Math.Min(timer, 3600);
                    return "Lagging · " + (int)Math.Ceiling(seconds) + " s";
                case PlayerStatus.Escaped: return "Escaped. Waiting for the match to end.";
                case PlayerStatus.Eliminated: return "Eliminated. Watching until the match ends.";
                default: return string.Empty;
            }
        }

        public static int CompletedTerminals(IReadOnlyList<TerminalState> terminals)
        {
            int done = 0;
            if (terminals != null) foreach (TerminalState terminal in terminals) if (terminal.Progress >= 100) done++;
            return done;
        }

        public static string TerminalsLine(IReadOnlyList<TerminalState> terminals, byte required, bool exitOpen)
            => "Terminals " + CompletedTerminals(terminals) + "/" + required + " · Exit " + (exitOpen ? "open" : "locked");

        // Index of the terminal closest to (x, z), or -1 when there is none.
        public static int NearestTerminal(IReadOnlyList<TerminalState> terminals, float x, float z)
        {
            int best = -1; float bestDistance = float.MaxValue;
            if (terminals == null) return best;
            for (int i = 0; i < terminals.Count; i++)
            {
                float dx = terminals[i].X - x, dz = terminals[i].Z - z, distance = dx * dx + dz * dz;
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        public static string HackingLine(byte progress) => "Hacking · " + Math.Min((int)progress, 100) + "%";

        public static string ExitLabel(bool open) => open ? "Exit · Open" : "Exit · Locked";
    }
}
