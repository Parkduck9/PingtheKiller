using System.Globalization;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.UI.RulesV2
{
    // HUD_SPEC 13.2: the sync check ring geometry and the client-side reading of its zones. The server judges every
    // press (U6a R3); Judge only states which drawn region a needle fraction falls in. Great is exactly the great part
    // [ZoneStart, ZoneStart + GreatSize]; the optional tolerance (fractions) widens Good on both zone edges and never
    // creates Great. Edges are inclusive.
    public static class SyncCheckMath
    {
        // Seconds the result word stays on screen after S2C_SyncResult.
        public const float ResultSeconds = .4f;
        // U6a SyncToleranceSeconds, for converting to a fraction of one sweep.
        public const float ToleranceSeconds = .15f;

        public static float NeedleFraction(float elapsed, float duration)
        {
            if (!(duration > 0) || float.IsNaN(elapsed)) return 0;
            return Mathf.Clamp01(elapsed / duration);
        }

        // Clockwise degrees from 12 o'clock.
        public static float NeedleDegrees(float elapsed, float duration) => NeedleFraction(elapsed, duration) * 360f;

        public static float ToleranceFraction(float duration) => duration > 0 ? ToleranceSeconds / duration : 0;

        public static SyncResult Judge(float fraction, float zoneStart, float zoneSize, float greatSize, float tolerance = 0)
        {
            if (float.IsNaN(fraction)) return SyncResult.Fail;
            tolerance = Mathf.Max(0, tolerance);
            float greatEnd = zoneStart + Mathf.Min(greatSize, zoneSize);
            float zoneEnd = zoneStart + zoneSize;
            if (fraction >= zoneStart && fraction <= greatEnd) return SyncResult.Great;
            if (fraction >= zoneStart - tolerance && fraction <= zoneEnd + tolerance) return SyncResult.Good;
            return SyncResult.Fail;
        }

        public static SyncResult Judge(float fraction, in SyncCheckView view, float tolerance = 0)
            => Judge(fraction, view.ZoneStart, view.ZoneSize, view.GreatSize, tolerance);

        public static string ResultWord(SyncResult result)
            => result == SyncResult.Great ? "Great" : result == SyncResult.Good ? "Good" : "Fail";

    }

    // HUD_SPEC 13.3: hold action bar copy and fill.
    public static class HoldCopy
    {
        public static float Fill(float progress01)
        {
            if (float.IsNaN(progress01)) return 0;
            return Mathf.Clamp01(progress01);
        }

        public static int Percent(float progress01) => Mathf.FloorToInt(Fill(progress01) * 100f + 1e-4f);

        public static string Label(in HoldActionView view)
        {
            string percent = Percent(view.Progress01).ToString(CultureInfo.InvariantCulture) + "%";
            switch (view.Kind)
            {
                case HoldKind.ExitSwitch: return "Opening the Exit · " + percent + " — you are Signal";
                case HoldKind.KickTerminal: return "Kicking the Terminal · " + percent;
                case HoldKind.CloseHatch: return "Closing the Hatch · " + percent;
                default: return string.Empty;
            }
        }

        // Killer block second line (S muted): what cancels the hold.
        public static string Hint(HoldKind kind, string interactGlyph)
        {
            switch (kind)
            {
                case HoldKind.KickTerminal: return "Hold " + interactGlyph + " · moving cancels the kick";
                case HoldKind.CloseHatch: return "Hold " + interactGlyph + " · releasing cancels";
                default: return string.Empty;
            }
        }
    }

    public enum ExitPhase : byte { NoPower, Powered, Switching, Locked, Open }

    // HUD_SPEC 13.4: exit status. Precedence: Open > Locked > Switching (progress > 0) > Powered > NoPower.
    public static class ExitCopy
    {
        public static ExitPhase Phase(in ExitView exit)
        {
            if (exit.Open) return ExitPhase.Open;
            if (exit.Locked) return ExitPhase.Locked;
            if (!exit.Powered) return ExitPhase.NoPower;
            return HoldCopy.Fill(exit.SwitchProgress01) > 0 ? ExitPhase.Switching : ExitPhase.Powered;
        }

        public static string Name(byte id) => "Exit " + (id + 1).ToString(CultureInfo.InvariantCulture);

        // The right-hand state word of a Survivor exit cell, and the second half of a Killer exit tag.
        public static string State(in ExitView exit)
        {
            switch (Phase(exit))
            {
                case ExitPhase.Open: return "Open";
                case ExitPhase.Locked:
                    return exit.LockedLeft > 0 ? "Locked " + Mathf.CeilToInt(exit.LockedLeft).ToString(CultureInfo.InvariantCulture) + " s" : "Locked";
                case ExitPhase.Switching: return HoldCopy.Percent(exit.SwitchProgress01).ToString(CultureInfo.InvariantCulture) + "%";
                case ExitPhase.Powered: return "Powered";
                default: return "No power";
            }
        }

        public static string Tag(in ExitView exit) => Name(exit.Id) + " · " + State(exit);

        // Everything the exit texts show (id, phase, percent, Locked seconds) packed into one number for TextMemo.
        public static long Key(in ExitView exit)
        {
            int locked = exit.LockedLeft > 0 ? Mathf.Min(4095, Mathf.CeilToInt(exit.LockedLeft)) : 0;
            return exit.Id | (long)Phase(exit) << 8 | (long)HoldCopy.Percent(exit.SwitchProgress01) << 16 | (long)locked << 32;
        }
    }

    // HUD_SPEC 13.5: the collapse clock.
    public static class CollapseCopy
    {
        public const int UrgentSeconds = 30;

        // m:ss of ceil(seconds); negative (not running) reads as 0:00.
        public static string Clock(float seconds)
        {
            int whole = seconds > 0 ? Mathf.CeilToInt(seconds) : 0;
            return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // The clock that eliminates this viewer: the personal one when it runs, else the global one.
        public static float Deciding(in CollapseView view) => view.PersonalLeft >= 0 ? view.PersonalLeft : view.GlobalLeft;

        // Urgent once the shown number is 0:30 or less (ceil), so the colour changes on the same frame as the digits.
        public static bool Urgent(float seconds) => seconds >= 0 && Mathf.CeilToInt(seconds) <= UrgentSeconds;

        public static bool Urgent(in CollapseView view) => view.Running && Urgent(Deciding(view));

        // True when the personal clock shows a different m:ss from the global one.
        public static bool Differs(in CollapseView view)
            => view.PersonalLeft >= 0 && view.GlobalLeft >= 0 && Clock(view.PersonalLeft) != Clock(view.GlobalLeft);

        public static string Main(in CollapseView view) => "Collapse " + Clock(view.GlobalLeft >= 0 ? view.GlobalLeft : view.PersonalLeft);

        // The shown whole seconds of both clocks and the Lagging flag, packed for TextMemo.
        public static long Key(in CollapseView view)
        {
            long global = view.GlobalLeft > 0 ? Mathf.CeilToInt(view.GlobalLeft) + 1 : view.GlobalLeft >= 0 ? 1 : 0;
            long personal = view.PersonalLeft > 0 ? Mathf.CeilToInt(view.PersonalLeft) + 1 : view.PersonalLeft >= 0 ? 1 : 0;
            return global | personal << 24 | (view.Lagging ? 1L : 0L) << 48;
        }

        // Second line (S), Survivor only: empty unless the personal clock differs.
        public static string Personal(in CollapseView view)
        {
            if (!Differs(view)) return string.Empty;
            return view.Lagging ? "Lagging: 2× faster · you " + Clock(view.PersonalLeft) : "You " + Clock(view.PersonalLeft);
        }
    }

    // HUD_SPEC 13.6: terminal flag tags and the Killer's Overloaded alert.
    public static class TerminalFlagCopy
    {
        public static string Name(byte id) => "Terminal " + (id + 1).ToString(CultureInfo.InvariantCulture);

        // Regressing tag: Survivors see the percentage, the Killer does not (HUD_SPEC 6.2, 11.1 N9).
        public static string Regressing(in TerminalFlagsView terminal, Role viewer)
            => viewer == Role.Survivor
                ? Name(terminal.Id) + " · " + HoldCopy.Percent(terminal.Progress01).ToString(CultureInfo.InvariantCulture) + "% · Regressing"
                : Name(terminal.Id) + " · Regressing";

        public static string Overloaded(in TerminalFlagsView terminal, float metres)
            => Name(terminal.Id) + " overloaded · " + Mathf.RoundToInt(Mathf.Max(0, metres)).ToString(CultureInfo.InvariantCulture) + " m";

        // Regressing shows to both roles; Overloaded is the Killer alert only.
        public static bool ShowOverloaded(in TerminalFlagsView terminal, Role viewer) => viewer == Role.Killer && terminal.Overloaded;
    }

    // HUD_SPEC 13.7: the result screen.
    public static class ResultV2Copy
    {
        public const int NameBudget = 24;

        // GDD v2 1 / U6a R8: Survivors win when escaped x 2 >= starting Survivors.
        public static bool SurvivorsWin(int escaped, int starting) => starting > 0 && escaped * 2 >= starting;

        public static int Needed(int starting) => (starting + 1) / 2;

        public static string Headline(Role winner) => winner == Role.Killer ? "Killer wins" : "Survivors win";

        // One line: the count and the majority rule.
        public static string Rule(int escaped, int starting)
            => escaped.ToString(CultureInfo.InvariantCulture) + " of " + starting.ToString(CultureInfo.InvariantCulture)
               + " escaped · Survivors win when half or more escape (" + Needed(starting).ToString(CultureInfo.InvariantCulture)
               + " of " + starting.ToString(CultureInfo.InvariantCulture) + ")";

        public static string Reason(ResultReasonV2 reason)
        {
            switch (reason)
            {
                case ResultReasonV2.AllSurvivorsDown: return "Every Survivor was eliminated before anyone escaped.";
                case ResultReasonV2.TimeUp: return "Time ran out. Survivors still inside were eliminated.";
                case ResultReasonV2.Collapse: return "The collapse eliminated the Survivors still inside.";
                default: return "Every Survivor escaped or was eliminated.";
            }
        }

        public static string Outcome(ResultV2View view)
        {
            if (view.OwnRole == Role.Killer)
            {
                int eliminated = view.Starting - view.Escaped;
                return "You eliminated " + eliminated.ToString(CultureInfo.InvariantCulture) + " of " + view.Starting.ToString(CultureInfo.InvariantCulture) + " Survivors.";
            }
            foreach (ResultRow row in view.Rows)
                if (row.Self) return row.Escaped ? "You escaped." : "You were eliminated.";
            return string.Empty;
        }

        public static string RowState(in ResultRow row) => row.Escaped ? "Escaped" : "Eliminated";

        public static string RowName(in ResultRow row, int index)
        {
            string name = string.IsNullOrEmpty(row.Name) ? "Survivor " + (index + 1).ToString(CultureInfo.InvariantCulture) : row.Name;
            if (name.Length > NameBudget)
            {
                int cut = NameBudget - 1;
                if (char.IsHighSurrogate(name[cut - 1])) cut--;
                name = name.Substring(0, cut) + "…";
            }
            return row.Self ? name + " (you)" : name;
        }

        // Development check only: the server's WinnerRole should match the rule for the listed rows.
        public static bool Consistent(ResultV2View view)
            => (view.Winner == Role.Survivor) == SurvivorsWin(view.Escaped, view.Starting);
    }

    internal static class Palette
    {
        public static readonly Color Bg = new Color32(16, 17, 19, 255);
        public static readonly Color Panel = new Color32(26, 28, 31, 255);
        public static readonly Color Line = new Color32(44, 47, 51, 255);
        public static readonly Color Text = new Color32(232, 230, 225, 255);
        public static readonly Color Muted = new Color32(154, 152, 144, 255);
        public static readonly Color Signal = new Color32(255, 176, 32, 255);
        public static readonly Color Danger = new Color32(229, 72, 77, 255);
        public static readonly Color Ally = new Color32(63, 184, 175, 255);

        // HUD_SPEC 0.2 stroke snapping: n u renders at max(1, round(n x scale)) px.
        public static float Stroke(float units, float scale) => Mathf.Max(1, Mathf.Round(units * scale)) / Mathf.Max(.01f, scale);
    }
}
