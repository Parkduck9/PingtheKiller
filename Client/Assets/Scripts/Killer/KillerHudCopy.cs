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
        // R1 (11/17): the attack prompt (6.1b), shown while a visible blip is within AttackPromptRange.
        public const string AttackPrompt = "Click  Attack";
        public const string AttackPromptPad = "RT  Attack";
        public const float AttackPromptRange = 3f;
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
                case SkillIds.PacketTrace: return "Packet trace";
                case SkillIds.FirewallBlock: return "Firewall block";
                default: return "Skill " + skillId;
            }
        }

        // R1 (11/17): one copy source, PlayMenuCopy (the lobby picker and the R3 intro card read the same strings).
        public static string SkillEffect(byte skillId) => PingTheKiller.Client.UI.PlayMenuCopy.KillerEffect(skillId);

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
        // R1 (11/17): the Overclock tail is 0.3 s at reduced speed (no longer a 1 s freeze). No percent sign in the Killer view.
        public static string Recovering(float left) => "Recovering: slowed " + Dot + " " + Seconds1(left);
        public static string Scanned(float left) => "Nearest Survivor shown " + Dot + " " + Seconds1(left);
        public static string Locked(int exit, float left) => (exit >= 0 ? "Exit " + (exit + 1) : "An Exit") + " switch locked " + Dot + " " + Seconds1(left);
        // R1 (11/17): Firewall block on a terminal; Packet trace.
        public static string TerminalLocked(int terminal, float left) => "Terminal " + (terminal + 1) + " locked " + Dot + " " + Seconds1(left);
        public static string Traced(float left) => "Survivors within 12 m shown " + Dot + " " + Seconds1(left);
        public static string Slowed(float left) => "Slowed by traffic overload " + Dot + " " + Seconds1(left);

        // Skill block state line (6.1d). cooldown is S2C_SelfState.SkillCooldown once received this match, else null.
        // R1 (11/17): no Killer skill is passive any more, and Firewall block also locks a terminal, so it is never shown
        // unavailable for a missing switch (the Killer cannot see terminal progress; a refusal says why).
        public static string SkillState(byte skillId, float? cooldown, bool stunned = false)
        {
            if (stunned) return "Stunned"; // M4: the server refuses every press while the Killer is Stunned
            if (cooldown.HasValue && cooldown.Value > 0) return "Cooldown " + (int)Math.Ceiling(cooldown.Value) + " s";
            return Ready;
        }

        // R1 (11/17) 6.1b: the attack prompt while a visible blip is within AttackPromptRange metres, else null.
        public static string Attack(float nearestBlip, bool pad) => nearestBlip <= AttackPromptRange ? (pad ? AttackPromptPad : AttackPrompt) : null;

        public static string Distance(float metres) => (int)Math.Round(Math.Max(0, metres), MidpointRounding.AwayFromZero) + " m";
        public static string LaggingDistance(float metres) => Lagging + " " + Dot + " " + Distance(metres);
        public static string Terminal(byte id) => "Terminal " + (id + 1);
        // R1 (11/17): the terminal marker tag; a locked terminal (TerminalFlags.Locked) says so.
        public static string TerminalTag(byte id, bool locked) => locked ? Terminal(id) + " " + Dot + " Locked" : Terminal(id);
    }
}
