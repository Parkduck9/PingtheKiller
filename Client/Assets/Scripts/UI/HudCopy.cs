using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.UI
{
    // Pure play-readout copy (GDD 3.3-3.5, 4). Unit-tested; ClientScreens only displays the results.
    public static class HudCopy
    {
        // Role-local skill IDs come from Shared SkillIds; the names are the GDD section 4 names.
        private static readonly string[] KillerSkills = KillerTable();
        private static readonly string[] SurvivorSkills = SurvivorTable();

        private static string[] KillerTable()
        {
            var table = new string[SkillIds.KillerSkillCount];
            table[SkillIds.PingScanner] = "Ping Scanner";
            table[SkillIds.Overclock] = "Overclock";
            table[SkillIds.PacketAfterimage] = "Packet Afterimage";
            table[SkillIds.FirewallBlock] = "Firewall Block";
            return table;
        }

        private static string[] SurvivorTable()
        {
            var table = new string[SkillIds.SurvivorSkillCount];
            table[SkillIds.DummyPacket] = "Dummy Packet";
            table[SkillIds.StealthRelay] = "Stealth Relay";
            table[SkillIds.TrafficOverload] = "Traffic Overload";
            table[SkillIds.EmergencyRestore] = "Emergency Restore";
            table[SkillIds.WhiteNoise] = "White Noise";
            return table;
        }

        public static string SkillName(Role role, byte skillId)
        {
            string[] table = role == Role.Killer ? KillerSkills : SurvivorSkills;
            return skillId < table.Length ? table[skillId] : "Skill " + skillId;
        }

        public static bool IsPassive(Role role, byte skillId) => role == Role.Killer && skillId == SkillIds.PacketAfterimage;

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

        public static string TerminalsLine(IReadOnlyList<TerminalState> terminals, byte required)
            => Terminals(CompletedTerminals(terminals), required);

        // The one terminal-count format for both roles (HUD_SPEC 4.3, 6.1a): "Terminals 1/3". The Survivor info block
        // (inner 144 u) has no room for "Terminals 4 of 4".
        public static string Terminals(int done, int needed) => "Terminals " + done + "/" + needed;

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
    }
}
