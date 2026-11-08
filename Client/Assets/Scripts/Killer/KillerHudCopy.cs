using System;
using System.Globalization;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Killer
{
    // Killer HUD copy (HUD_SPEC 3, 6.1, 6.2, 6.6, 8, 13). Pure; KillerHud and KillerWorldLayer only display it.
    public static class KillerHudCopy
    {
        public const string Dot = "·";
        public const string IntroTitle = "You are the Killer";
        public const string IntroRules = "Only Signal Survivors show, even through walls. Eliminate more than half of the Survivors to win.";
        public const string OneMore = "One more powers the Exits.";
        public const string Ready = "Ready";
        public const string Passive = "Passive — always on";
        public const string NeedsSwitch = "Needs an Exit switch in progress";
        public const string Resume = "Resume";
        public const string LeaveMatch = "Leave match";
        public const string LaggingTitle = "Lagging Survivor";
        public const string Lagging = "Lagging";

        // HUD_SPEC 8 names (sentence case) and effect lines.
        public static string SkillName(byte skillId)
        {
            switch (skillId)
            {
                case SkillIds.PingScanner: return "Ping scanner";
                case SkillIds.Overclock: return "Overclock";
                case SkillIds.PacketAfterimage: return "Packet afterimage";
                case SkillIds.FirewallBlock: return "Firewall block";
                default: return "Skill " + skillId;
            }
        }

        public static string SkillEffect(byte skillId)
        {
            switch (skillId)
            {
                case SkillIds.PingScanner: return "Shows the nearest Survivor for 1.5 s.";
                case SkillIds.Overclock: return "1.5× speed for 3 s, then 1 s unable to move.";
                case SkillIds.PacketAfterimage: return "Passive: Survivors stay visible 1 s after their Signal ends.";
                case SkillIds.FirewallBlock: return "Locks the Exit switch in progress for 5 s.";
                default: return string.Empty;
            }
        }

        // m:ss, rounded up so 0:00 shows only when time has run out.
        public static string Time(float seconds)
        {
            int total = float.IsNaN(seconds) || seconds <= 0 ? 0 : (int)Math.Ceiling(Math.Min(seconds, 5999));
            return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool TimePressure(float seconds) => seconds <= 60;

        // The Survivor's format (HudCopy.Terminals), so both roles read "Terminals 1/3".
        public static string Terminals(int done, int needed) => PingTheKiller.Client.UI.HudCopy.Terminals(done, needed);

        // Line 3 only while exactly one terminal is missing.
        public static string OneMoreLine(int done, int needed) => needed > 0 && done == needed - 1 ? OneMore : null;

        public static string Seconds1(float seconds)
            => (float.IsNaN(seconds) || seconds < 0 ? 0 : seconds).ToString("0.0", CultureInfo.InvariantCulture) + " s";

        // HUD_SPEC 14.2: the 6.1c own-skill lines (tick for the effect's duration).
        public static string Overclock(float left) => "Overclock: 1.5× speed " + Dot + " " + Seconds1(left);
        public static string Recovering(float left) => "Recovering: cannot move " + Dot + " " + Seconds1(left);
        public static string Scanned(float left) => "Nearest Survivor shown " + Dot + " " + Seconds1(left);
        public static string Locked(int exit, float left) => (exit >= 0 ? "Exit " + (exit + 1) : "An Exit") + " switch locked " + Dot + " " + Seconds1(left);
        public static string Slowed(float left) => "Slowed by traffic overload " + Dot + " " + Seconds1(left);

        // Skill block state line (6.1d). cooldown is S2C_SelfState.SkillCooldown once received this match, else null.
        // switchInProgress: an unopened Exit with switch progress (Firewall block's only target, GDD v2 3).
        public static string SkillState(byte skillId, bool switchInProgress, float? cooldown)
        {
            if (skillId == SkillIds.PacketAfterimage) return Passive;
            if (skillId == SkillIds.FirewallBlock && !switchInProgress) return NeedsSwitch;
            if (cooldown.HasValue && cooldown.Value > 0) return "Cooldown " + (int)Math.Ceiling(cooldown.Value) + " s";
            return Ready;
        }

        public static string Distance(float metres) => (int)Math.Round(Math.Max(0, metres), MidpointRounding.AwayFromZero) + " m";
        public static string LaggingDistance(float metres) => Lagging + " " + Dot + " " + Distance(metres);
        public static string Terminal(byte id) => "Terminal " + (id + 1);
    }
}
