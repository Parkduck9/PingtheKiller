using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.Net;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Dev
{
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
        // Dev-only synthetic S2C_GameResult reason (GameEndReason), dispatched locally after the play shots; never sent.
        public byte? PreviewResult { get; private set; }
        public List<string> Errors { get; } = new List<string>();
        public bool Active => Join != null || Bots >= 0 || RoleChoice.HasValue || Start || HoldW > 0 || Shots != null || QuitAfter > 0
            || Offline || PreviewResult.HasValue;

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
                bool known = flag == "-ptk-join" || flag == "-ptk-bots" || flag == "-ptk-role" || flag == "-ptk-hold-w"
                    || flag == "-ptk-shots" || flag == "-ptk-quit-after" || flag == "-ptk-preview-result";
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
                case "-ptk-shots":
                    Shots = value; return true;
                case "-ptk-quit-after":
                    if (!Seconds(value, out float quit)) return false;
                    QuitAfter = quit; return true;
                case "-ptk-preview-result":
                    switch (value.ToLowerInvariant())
                    {
                        case "survivorescaped": PreviewResult = (byte)GameEndReason.SurvivorEscaped; return true;
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
