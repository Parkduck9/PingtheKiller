using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Dev
{
    public enum KillerLook : byte { None, Nearest, Away, Fixed, Occluded }

    // -ptk-auto-sync: where the scripted press lands on each sync check (miss never presses).
    public enum AutoSync : byte { None, Great, Good, Miss }

    // Dev capture flags (-ptk-*). Inert when none is present. Invalid or missing values disable that flag and add an error.
    public sealed class DevArgs
    {
        public const string Prefix = "-ptk-";
        public string Join { get; private set; }
        public int Bots { get; private set; } = -1;
        public byte? RoleChoice { get; private set; }
        public bool Start { get; private set; }
        public float HoldW { get; private set; }
        public string Shots { get; private set; }
        public float QuitAfter { get; private set; }
        public bool Offline { get; private set; }
        // Survivor autopilot: hold W and turn slowly for this many seconds of the first match (keeps the survivor Signal).
        public float Wander { get; private set; }
        // After a GameResult: Back to lobby, Start again, then -ptk-hold-w in the second match.
        public bool Rematch { get; private set; }
        // Dev-only synthetic S2C_GameResult reason (GameEndReason), dispatched locally after the play shots; never sent.
        public byte? PreviewResult { get; private set; }
        // Survivor camera yaw offset from the character facing, applied once at match start + 2 s (look-behind shot: 180).
        public float? Orbit { get; private set; }
        // Killer view aim: re-aims every 0.5 s toward the nearest blip, away from it, or to a fixed yaw.
        public KillerLook Look { get; private set; }
        public float LookYaw { get; private set; }
        // Opens the play menu this many seconds into the first match (play-menu shot).
        public float Menu { get; private set; }
        // Uses the own skill once at this match second (retried each second until the server starts a cooldown, at most
        // SkillTries presses): the cooldown shots.
        public float SkillAt { get; private set; }
        // U9 evidence, with -ptk-skill-at: a scripted press waits (uncounted) until the client's derived precondition holds
        // (Firewall block: a switch in progress; Emergency restore: a Lagging Survivor next to you).
        public bool SkillWhenAble { get; private set; }
        public const int SkillTries = 6;
        // Killer chase: for this many seconds from match second 2, hold W and steer (NavGrid route) toward the nearest
        // tracked instance, Lagging or Signal; with none in sight it walks the terminals. Makes captures happen for the grace shot.
        public float Chase { get; private set; }
        // Survivor rescue: whenever an ally is Lagging, walk to it and stay in rescue range (the rescue shot).
        public bool Rescue { get; private set; }
        // Survivor client, screenshots only: inject a fake Killer entry this many metres ahead of the own Survivor into each
        // received WorldState (4 s shown, 2 s hidden, repeating, so the fade is visible too). Shots taken with it are
        // labelled "-injected" in reports. Never sent anywhere.
        public float FakeKiller { get; private set; }
        // HUD_SPEC 4.5 / 11 Q12: the reduced Lagging stutter (blend only, no frame hold). A player option, not a dev capture
        // flag, so it does not make the dev component active.
        public bool ReduceStutter { get; private set; }
        // U4j, screenshots only: -ptk-preview-motion <clip> (MotionPreview.Names). A Survivor client scripts its own
        // character's motion inputs (or the injected Killer model attacks once) and DevCapture shoots motion-<clip>.png;
        // killer-down shoots a Killer-view frame 1.2 s after a real capture. Nothing is sent.
        public MotionPreviewClip PreviewMotion { get; private set; }
        // Rules v2 HUD preview (screenshots only): one of RulesV2Preview.Scenarios, shown with fake view-model data over
        // the running match from match second 2 (5 with -ptk-shots).
        public string PreviewV2Hud { get; private set; }
        // Screenshots of the Settings screen: opened from the main menu after the menu shot, and from the play menu (needs -ptk-menu).
        public bool Settings { get; private set; }
        // R3 (11/17): shots of the How to play screen, both pages, opened from the main menu after the menu shot.
        public bool HowTo { get; private set; }
        // After the play-menu shots (and the Settings shot): press the open play menu's Leave match path and shoot
        // menu-after-leave.png once the main menu shows (needs -ptk-menu).
        public bool MenuLeave { get; private set; }
        // Survivor autopilot (rules v2 screenshots): walk (NavGrid route, normal MoveInput) to the nearest unfinished terminal
        // and hold Interact there for this many seconds in total, re-targeting when a terminal completes. Off by default.
        public float DevHack { get; private set; }
        // Survivor autopilot, after the exits are powered: hold the nearest unopened exit's switch, then walk into an open
        // exit. Sends only MoveInput and Interact, like a player.
        public bool DevSwitch { get; private set; }
        // Presses Space's path (SyncCheckTracker.TryPress) at the great part, inside the rest of the zone, or never.
        public AutoSync AutoSync { get; private set; }
        // HUD_SPEC 14 skill feedback shots: an offline match whose own skill is not this id is left at once and a new offline
        // match is started (the server draws skills at random), at most WantSkillTries times.
        public byte? WantSkill { get; private set; }
        public const int WantSkillTries = 30;
        public List<string> Errors { get; } = new List<string>();
        public bool Active => Join != null || Bots >= 0 || RoleChoice.HasValue || Start || HoldW > 0 || Shots != null || QuitAfter > 0
            || Offline || PreviewResult.HasValue || Wander > 0 || Rematch || Orbit.HasValue || Look != KillerLook.None || Menu > 0
            || SkillAt > 0 || Chase > 0 || Rescue || FakeKiller > 0 || PreviewMotion != MotionPreviewClip.None || PreviewV2Hud != null
            || Settings || HowTo || MenuLeave || DevHack > 0 || DevSwitch || AutoSync != AutoSync.None || WantSkill.HasValue;

        public static DevArgs Parse(IReadOnlyList<string> args)
        {
            var result = new DevArgs();
            if (args == null) return result;
            for (int i = 0; i < args.Count; i++)
            {
                string flag = args[i];
                if (flag == null || !flag.StartsWith(Prefix)) continue;
                if (flag == "-ptk-start") { result.Start = true; continue; }
                if (flag == "-ptk-offline") { result.Offline = true; continue; }
                if (flag == "-ptk-rematch") { result.Rematch = true; continue; }
                if (flag == "-ptk-rescue") { result.Rescue = true; continue; }
                if (flag == "-ptk-skill-when-able") { result.SkillWhenAble = true; continue; }
                if (flag == "-ptk-settings") { result.Settings = true; continue; }
                if (flag == "-ptk-howto") { result.HowTo = true; continue; }
                if (flag == "-ptk-menu-leave") { result.MenuLeave = true; continue; }
                if (flag == "-ptk-dev-switch") { result.DevSwitch = true; continue; }
                if (flag == "-ptk-reduce-stutter") { result.ReduceStutter = true; continue; }
                bool known = flag == "-ptk-join" || flag == "-ptk-bots" || flag == "-ptk-role" || flag == "-ptk-hold-w"
                    || flag == "-ptk-shots" || flag == "-ptk-quit-after" || flag == "-ptk-preview-result" || flag == "-ptk-wander" || flag == "-ptk-orbit" || flag == "-ptk-killer-look"
                    || flag == "-ptk-menu" || flag == "-ptk-skill-at" || flag == "-ptk-chase" || flag == "-ptk-fake-killer" || flag == "-ptk-preview-motion"
                    || flag == "-ptk-preview-v2-hud" || flag == "-ptk-dev-hack" || flag == "-ptk-auto-sync" || flag == "-ptk-want-skill";
                if (!known) { result.Errors.Add("Unknown flag " + flag); continue; }
                string value = i + 1 < args.Count ? args[i + 1] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith(Prefix)) { result.Errors.Add(flag + " needs a value"); continue; }
                i++;
                if (!result.Apply(flag, value.Trim())) result.Errors.Add(flag + ": invalid value '" + value + "'");
            }
            return result;
        }

        private bool Apply(string flag, string value)
        {
            switch (flag)
            {
                case "-ptk-join":
                    if (!EndpointParser.TryParse(value, out _, out _)) return false;
                    Join = value; return true;
                case "-ptk-bots":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int bots) || bots > 7) return false;
                    Bots = bots; return true;
                case "-ptk-role":
                    switch (value.ToLowerInvariant())
                    {
                        case "killer": RoleChoice = (byte)Shared.Game.RoleChoice.Killer; return true;
                        case "survivor": RoleChoice = (byte)Shared.Game.RoleChoice.Survivor; return true;
                        case "random": RoleChoice = (byte)Shared.Game.RoleChoice.Random; return true;
                        default: return false;
                    }
                case "-ptk-hold-w":
                    if (!Seconds(value, out float hold)) return false;
                    HoldW = hold; return true;
                case "-ptk-wander":
                    if (!Seconds(value, out float wander)) return false;
                    Wander = wander; return true;
                case "-ptk-orbit":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float orbit) || float.IsNaN(orbit) || float.IsInfinity(orbit)) return false;
                    Orbit = orbit; return true;
                case "-ptk-killer-look":
                    switch (value.ToLowerInvariant())
                    {
                        case "nearest": Look = KillerLook.Nearest; return true;
                        case "away": Look = KillerLook.Away; return true;
                        case "occluded": Look = KillerLook.Occluded; return true; // keeps LookYaw from an earlier number as its fallback
                    }
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw) || float.IsNaN(yaw) || float.IsInfinity(yaw)) return false;
                    Look = KillerLook.Fixed; LookYaw = yaw; return true;
                case "-ptk-menu":
                    if (!Seconds(value, out float menu)) return false;
                    Menu = menu; return true;
                case "-ptk-want-skill":
                    if (!byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out byte want) || want >= SkillIds.SurvivorSkillCount) return false;
                    WantSkill = want; return true;
                case "-ptk-skill-at":
                    if (!Seconds(value, out float skillAt)) return false;
                    SkillAt = skillAt; return true;
                case "-ptk-chase":
                    if (!Seconds(value, out float chase)) return false;
                    Chase = chase; return true;
                case "-ptk-fake-killer":
                    if (!Seconds(value, out float metres) || metres > 30) return false;
                    FakeKiller = metres; return true;
                case "-ptk-preview-v2-hud":
                    string scenario = value.ToLowerInvariant();
                    if (!UI.RulesV2.RulesV2Preview.Known(scenario)) return false;
                    PreviewV2Hud = scenario; return true;
                case "-ptk-dev-hack":
                    if (!Seconds(value, out float hack)) return false;
                    DevHack = hack; return true;
                case "-ptk-auto-sync":
                    switch (value.ToLowerInvariant())
                    {
                        case "great": AutoSync = AutoSync.Great; return true;
                        case "good": AutoSync = AutoSync.Good; return true;
                        case "miss": AutoSync = AutoSync.Miss; return true;
                        default: return false;
                    }
                case "-ptk-shots":
                    Shots = value; return true;
                case "-ptk-preview-motion":
                    if (!MotionPreview.TryParse(value, out MotionPreviewClip clip)) return false;
                    PreviewMotion = clip; return true;
                case "-ptk-quit-after":
                    if (!Seconds(value, out float quit)) return false;
                    QuitAfter = quit; return true;
                case "-ptk-preview-result":
                    switch (value.ToLowerInvariant())
                    {
                        // v0.4: "survivorescaped" stays as an alias of AllResolved (a survivor win).
                        case "survivorescaped":
                        case "allresolved": PreviewResult = (byte)GameEndReason.AllResolved; return true;
                        case "collapse": PreviewResult = (byte)GameEndReason.Collapse; return true;
                        case "allsurvivorsdown": PreviewResult = (byte)GameEndReason.AllSurvivorsDown; return true;
                        case "timeup": PreviewResult = (byte)GameEndReason.TimeUp; return true;
                        default: return false;
                    }
            }
            return false;
        }

        private static bool Seconds(string value, out float seconds)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
               && seconds > 0 && !float.IsInfinity(seconds) && !float.IsNaN(seconds);
    }
}
