using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.Killer;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.UI
{
    // One skill row of the How to play screen and the intro card: the name and effect come only from the existing copy
    // (SurvivorReadout.SkillName / KillerHudCopy.SkillName and PlayMenuCopy.SurvivorEffect / KillerEffect).
    public readonly struct HelpSkill
    {
        public readonly byte Id;
        public readonly string Name, Effect;
        public HelpSkill(byte id, string name, string effect) { Id = id; Name = name; Effect = effect; }
    }

    // One page of the How to play screen (HUD_SPEC 1.7, 11/17 R3): the goal, the controls, the status ladder, Signal and
    // Silent in one line, and every skill of the role.
    public sealed class HelpPage
    {
        public string Title, Goal, Controls, Visibility;
        public string[] Ladder;
        public HelpSkill[] Skills;
    }

    // Onboarding and rescue copy (HUD_SPEC 3, 4.4, 4.6, 1.7; 11/17 R3). Pure; the HUD classes only display it. Skill names and
    // effects are never written here: they are read from the existing tables so a skill string exists in one place.
    public static class HelpCopy
    {
        public const string Dot = "·", Dash = "—";
        public const float IntroSeconds = 7f;
        public const float HintSeconds = 6f;
        public const string HowToPlay = "How to play";
        public const string SurvivorTab = "Survivor", KillerTab = "Killer", Back = "Back";

        // --- Controls legends (one per role, shared by the intro card and the How to play page) ---
        public const string SurvivorControls = "WASD Move · Hold E Hack / Rescue / Exit switch / Shutter · While hacking: WASD routes packets · Q Skill · Esc Menu";
        public const string KillerControls = "WASD Move · Left click Attack · Hold E Kick terminal / Break shutter / Close hatch · Q Skill · Esc Menu";
        public static string Controls(Role role) => role == Role.Killer ? KillerControls : SurvivorControls;

        // --- Intro card ---
        public const string SurvivorGoalNoCount = "Hack the terminals, then hold an exit switch and escape.";
        public const string KillerGoal = "Capture every Survivor before they escape.";

        // `Survivor · Hack 3 terminals, then hold an exit switch and escape.` (the count comes from the first WorldState).
        public static string IntroGoal(Role role, int terminalsRequired)
        {
            if (role == Role.Killer) return "Killer " + Dot + " " + KillerGoal;
            string goal = terminalsRequired > 0
                ? "Hack " + terminalsRequired.ToString(CultureInfo.InvariantCulture) + " terminals, then hold an exit switch and escape."
                : SurvivorGoalNoCount;
            return "Survivor " + Dot + " " + goal;
        }

        public static string SkillName(Role role, byte skillId) => role == Role.Killer ? KillerHudCopy.SkillName(skillId) : SurvivorReadout.SkillName(skillId);
        public static string SkillEffect(Role role, byte skillId) => role == Role.Killer ? PlayMenuCopy.KillerEffect(skillId) : PlayMenuCopy.SurvivorEffect(skillId);

        public static bool Passive(Role role, byte skillId) => role == Role.Killer && skillId == SkillIds.PacketAfterimage;

        // `Dummy packet [Q] · Cooldown 15 s`; the passive Packet afterimage has no key and no cooldown.
        public static string IntroSkillLine(Role role, byte skillId)
        {
            string name = SkillName(role, skillId);
            if (Passive(role, skillId)) return name + " " + Dot + " Passive";
            float cooldown = role == Role.Killer ? GameConstants.KillerSkillCooldownSeconds : GameConstants.SurvivorSkillCooldownSeconds;
            return name + " [Q] " + Dot + " Cooldown " + Seconds(cooldown);
        }

        private static string Seconds(float value) => ((int)System.Math.Round(value)).ToString(CultureInfo.InvariantCulture) + " s";

        // --- Rescue prompts (HUD_SPEC 4.4) ---
        public static string RescuePrompt(string name) => "Hold [E] to rescue " + name + " " + Dash + " you will be Signal";
        public static string RescuingPrompt(string name) => "Rescuing " + name + " " + Dash + " you are Signal";
        public static string ApproachPrompt(string name, int metres) => name + " is Lagging " + Dot + " " + metres + " m " + Dash + " move next to them, hold [E] to rescue";

        // R3: the rescued player's line for the GetUpSeconds (1.5 s) get-up.
        public const string GettingUp = "Getting up…";
        // The downed player's persistent line: `Lagging · an ally can rescue you with [E] · nearest ally 18 m · 54 s`.
        public const string DownedPrefix = "Lagging " + Dot + " an ally can rescue you with [E]";
        public static string DownedLine(float nearestAllyMetres, float secondsLeft)
        {
            string line = DownedPrefix;
            if (nearestAllyMetres >= 0 && !float.IsNaN(nearestAllyMetres) && !float.IsInfinity(nearestAllyMetres))
                line += " " + Dot + " nearest ally " + (int)System.Math.Round(nearestAllyMetres, System.MidpointRounding.AwayFromZero) + " m";
            return line + " " + Dot + " " + SurvivorReadout.Ceil(secondsLeft) + " s";
        }

        // --- Ally markers (HUD_SPEC 4.6): `Mina · Lagging · 23 m` ---
        public static string AllyTag(string name, float metres)
            => name + " " + Dot + " Lagging " + Dot + " " + (int)System.Math.Round(System.Math.Max(0, metres), System.MidpointRounding.AwayFromZero) + " m";

        // --- First-time hints (once per match each) ---
        public const string HintTerminal = "Hold [E] at a terminal to hack it";
        public const string HintSignal = "You are Signal: the Killer can see you";
        public const string HintKillerBlip = "Get close and left click to attack";
        public static string HintLagging(string name) => name + " is Lagging " + Dot + " hold [E] next to them to rescue";

        // --- How to play pages ---
        public static HelpPage Page(Role role)
        {
            string stage1 = Seconds(GameConstants.LaggingTimeoutSeconds), stage2 = Seconds(GameConstants.SecondLaggingTimeoutSeconds);
            string rescue = Seconds(GameConstants.RescueTimeSeconds), safe = Seconds(GameConstants.PostRescueInvulnerabilitySeconds);
            string getUp = GameConstants.GetUpSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";
            string radius = GameConstants.RescueRadius.ToString("0.0", CultureInfo.InvariantCulture) + " m";
            var skills = new List<HelpSkill>();
            if (role == Role.Killer)
                foreach (byte id in new[] { SkillIds.PingScanner, SkillIds.Overclock, SkillIds.PacketAfterimage, SkillIds.FirewallBlock })
                    skills.Add(new HelpSkill(id, SkillName(role, id), SkillEffect(role, id)));
            else
                foreach (byte id in new[] { SkillIds.DummyPacket, SkillIds.StealthRelay, SkillIds.TrafficOverload, SkillIds.EmergencyRestore, SkillIds.WhiteNoise })
                    skills.Add(new HelpSkill(id, SkillName(role, id), SkillEffect(role, id)));
            if (role == Role.Killer)
                return new HelpPage
                {
                    Title = KillerTab,
                    Goal = KillerGoal + " If at least half of the Survivors escape, the Survivors win.",
                    Controls = KillerControls,
                    Ladder = new[]
                    {
                        "Normal: a hit (left click) makes a Survivor Lagging. The third hit eliminates.",
                        "Lagging: crawling and always Signal, for " + stage1 + " (" + stage2 + " the second time). Then eliminated.",
                        "Rescue: a Survivor who holds E within " + radius + " for " + rescue + " makes a Lagging Survivor Normal. It cannot be hit for " + safe + " and cannot move for the first " + getUp + " while it gets up.",
                    },
                    Visibility = "You see only Signal Survivors, even through walls. Silent Survivors never show.",
                    Skills = skills.ToArray()
                };
            return new HelpPage
            {
                Title = SurvivorTab,
                Goal = "Hack enough terminals to power the exits, then hold an exit switch and escape. At least half of the Survivors must escape.",
                Controls = SurvivorControls,
                Ladder = new[]
                {
                    "Normal: full speed. A hit from the Killer makes you Lagging; the third hit eliminates you.",
                    "Lagging: you crawl, stay Signal and cannot use skills. " + stage1 + " the first time, " + stage2 + " the second. Then you are eliminated.",
                    "Rescue: an ally holds E within " + radius + " of you for " + rescue + ". Nothing can hit you for " + safe + "; for the first " + getUp + " you get up and cannot move.",
                },
                Visibility = "Signal: the Killer can see you. Moving, hacking and rescuing make you Signal; standing still keeps you Silent.",
                Skills = skills.ToArray()
            };
        }

        // Every string a page shows, for the copy rules in tests.
        public static IEnumerable<string> AllText(HelpPage page)
        {
            yield return page.Goal; yield return page.Controls; yield return page.Visibility;
            foreach (string line in page.Ladder) yield return line;
            foreach (HelpSkill skill in page.Skills) { yield return skill.Name; yield return skill.Effect; }
        }
    }
}
