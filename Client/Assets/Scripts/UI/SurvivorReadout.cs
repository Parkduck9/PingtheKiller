using System;
using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.UI
{
    public enum BarFill : byte { Empty, Ally, Danger, Signal, Text }

    // The own entry of a Survivor client's WorldState, as the HUD reads it.
    public struct OwnEntry
    {
        public PlayerStatus Status;
        public bool Signal, Hacking, Rescuing, Stealthed;
        public float StatusTimer, X, Z;
        public byte CaptureCount;
    }

    // Another Survivor as the Survivor HUD sees it (positions are allowed on a Survivor client, HUD_SPEC 0.1).
    public struct Mate
    {
        public int Id;
        public float X, Z;
        public PlayerStatus Status;
        public bool Hacking;
        public bool Stealthed; // HUD_SPEC 14.2.6: the Stealth relay count
    }

    // HUD_SPEC 4.1 transmit meter: row A word + capture tag, row B reason, row C bar.
    public struct MeterState
    {
        public string Word, Reason, Tag;
        public bool WordSignal, ReasonMuted, TagMuted, AllyOutline;
        // (10/30) Eliminated with an inferred reason: the reason may take two lines and the bar row is not drawn.
        public bool Wrap;
        public BarFill Fill;
        public float Width;
    }

    // HUD_SPEC 4.3 / 4.4 / 4.8 prompt row (672 x 40 above the cluster). Text null = hidden.
    public struct PromptState
    {
        public string Text;
        public bool Muted;
        public bool Danger;   // (10/30) capture banner: a 4x18 u `danger` bar before the text
        public BarFill Bar;   // Empty = no bar
        public float Width;
    }

    // Survivor-bound S2C_SkillEffect timing (HUD_SPEC 4.1 rows 5-6, 13.4 lock seconds, 4.8 Active). Skill ids are role-local, so an
    // effect is the Killer's only when its caster is neither this player nor a Survivor of the last WorldState.
    public sealed class SurvivorEffects
    {
        public double OwnStart = double.NegativeInfinity, OwnEnd = double.NegativeInfinity;
        public byte OwnSkill;
        public double ScannedEnd = double.NegativeInfinity, OverloadEnd = double.NegativeInfinity, LockEnd = double.NegativeInfinity;

        public void Reset()
        {
            OwnStart = OwnEnd = ScannedEnd = OverloadEnd = LockEnd = double.NegativeInfinity;
        }

        public void Accept(int casterId, byte skillId, float duration, double now, int ownId, bool casterIsSurvivor)
        {
            float d = float.IsNaN(duration) || duration < 0 ? 0 : Math.Min(duration, 60);
            if (casterId == ownId)
            {
                OwnStart = now; OwnEnd = now + d; OwnSkill = skillId;
                if (skillId == SkillIds.TrafficOverload) OverloadEnd = now + d;
                return;
            }
            if (casterIsSurvivor) return;
            if (skillId == SkillIds.PingScanner) ScannedEnd = now + d;
            else if (skillId == SkillIds.FirewallBlock) LockEnd = now + d;
        }

        // Remaining 0..1 of the own effect for the active arc, or -1 when none runs.
        public float OwnActive(double now) => now < OwnEnd && OwnEnd > OwnStart ? (float)((OwnEnd - now) / (OwnEnd - OwnStart)) : -1;
        public float Scanned(double now) => now < ScannedEnd ? (float)(ScannedEnd - now) : 0;
        public float Overload(double now) => now < OverloadEnd ? (float)(OverloadEnd - now) : 0;
        public float? Locked(double now) => now < LockEnd ? (float)(LockEnd - now) : (float?)null;
    }

    // Pure Survivor HUD readouts and copy (HUD_SPEC 4.1, 4.3, 4.4, 4.7, 4.8, 8). ClientScreens / SurvivorHud only show them.
    public static class SurvivorReadout
    {
        public const string Dot = "·", Dash = "—";
        public const float ApproachRange = 6f;
        public const int NameBudget = 12;
        public const string CursorHint = "Click to resume mouse look";
        public const string SkillsOffLagging = "Skills are off while Lagging.";
        public const string RestoreNeedsTarget = "Emergency restore needs a Lagging Survivor next to you.";
        public const string LaggingCannotHack = "Lagging Survivors cannot hack.";

        // HUD_SPEC 8 survivor names (sentence case).
        public static string SkillName(byte skillId)
        {
            switch (skillId)
            {
                case SkillIds.DummyPacket: return "Dummy packet";
                case SkillIds.StealthRelay: return "Stealth relay";
                case SkillIds.TrafficOverload: return "Traffic overload";
                case SkillIds.EmergencyRestore: return "Emergency restore";
                case SkillIds.WhiteNoise: return "White noise";
                default: return "Skill " + skillId;
            }
        }

        // One decimal, tabular seconds ("1.4 s"); garbage reads 0.
        public static string Seconds1(float seconds)
            => (float.IsNaN(seconds) || seconds < 0 ? 0 : Math.Min(seconds, 3600)).ToString("0.0", CultureInfo.InvariantCulture) + " s";

        public static int Ceil(float seconds) => float.IsNaN(seconds) || seconds <= 0 ? 0 : (int)Math.Ceiling(Math.Min(seconds, 3600));

        // HUD_SPEC 4.1: the first matching row wins. self = the SelfState seam (null or unavailable: rows that need it
        // are skipped). scannedLeft / overloadLeft = seconds left of a PingScanner effect on you / your own Traffic overload.
        public static MeterState Meter(OwnEntry own, ISelfState self, float scannedLeft, float overloadLeft)
            => Meter(own, self, scannedLeft, overloadLeft, null);

        // eliminatedReason (10/30): CaptureWatch's inferred reason, shown in `text` on up to two lines while Eliminated.
        public static MeterState Meter(OwnEntry own, ISelfState self, float scannedLeft, float overloadLeft, string eliminatedReason)
        {
            bool known = self != null && self.Available;
            var m = new MeterState { Fill = BarFill.Empty };
            switch (own.Status)
            {
                case PlayerStatus.Eliminated:
                    m.Word = "Eliminated";
                    if (string.IsNullOrEmpty(eliminatedReason)) { m.Reason = "Watching until the match ends."; m.ReasonMuted = true; }
                    else { m.Reason = eliminatedReason; m.Wrap = true; }
                    break;
                case PlayerStatus.Escaped:
                    m.Word = "Escaped"; m.Reason = "Waiting for the result."; m.ReasonMuted = true;
                    break;
                case PlayerStatus.Lagging:
                    m.Word = "Lagging";
                    if (known && self.RescueProgress > 0)
                    {
                        m.Reason = "Rescue in progress " + Dash + " stay close"; m.Fill = BarFill.Ally; m.Width = Clamp01(self.RescueProgress);
                    }
                    else
                    {
                        float timer = float.IsNaN(own.StatusTimer) ? 0 : own.StatusTimer;
                        m.Reason = "Always visible " + Dot + " " + Ceil(timer) + " s left"; m.Fill = BarFill.Danger;
                        m.Width = Clamp01(timer / LaggingTotal(own.CaptureCount));
                    }
                    break;
                default:
                    if (own.Signal)
                    {
                        m.Word = "Signal"; m.WordSignal = true; m.Fill = BarFill.Signal; m.Width = 1;
                        m.Reason = scannedLeft > 0 ? "Scanned by the Killer " + Dot + " " + Seconds1(scannedLeft)
                            : overloadLeft > 0 ? "Traffic overload " + Dot + " " + Seconds1(overloadLeft)
                            : own.Hacking ? "Visible while you hack."
                            : own.Rescuing ? "Visible while you rescue."
                            : "Visible while you move.";
                    }
                    else if (own.Stealthed) { m.Word = "Silent"; m.Reason = "Relay: hidden while moving"; m.AllyOutline = true; }
                    else { m.Word = "Silent"; m.Reason = "Not visible to the Killer."; m.ReasonMuted = true; }
                    break;
            }
            // Capture tag (row A right): Protected while the post-rescue immunity runs, else the capture tag (CaptureTag).
            if (known && self.InvulnerableLeft > 0) m.Tag = "Protected " + Seconds1(self.InvulnerableLeft);
            else if (own.CaptureCount >= 1 && own.Status == PlayerStatus.Normal) { m.Tag = CaptureTag(own.CaptureCount); m.TagMuted = true; }
            return m;
        }

        // CaptureCount counts Lagging stages, not captures (a first stage that runs out raises it to 2), so the tag never
        // claims two captures: count 1 is exactly one capture; from 2 no Lagging stage is left and the next capture eliminates.
        public const string CapturedOnce = "Captured once", NextCaptureEliminates = "Next capture eliminates";
        public static string CaptureTag(byte captureCount)
            => captureCount == 0 ? null : SecondStage(captureCount) ? NextCaptureEliminates : CapturedOnce;

        // GDD v2 4: the first Lagging stage lasts 60 s, the second 30 s.
        public static float LaggingTotal(byte captureCount)
            => SecondStage(captureCount) ? GameConstants.SecondLaggingTimeoutSeconds : GameConstants.LaggingTimeoutSeconds;

        // The second Lagging stage: the second capture, or the first stage running out (the server sets CaptureCount = 2).
        public static bool SecondStage(byte captureCount) => captureCount >= 2;

        // HUD_SPEC 4.8 ring state. cooldown = SelfState.SkillCooldown when available, else null (no cooldown ring).
        public static SkillRing.State SkillState(byte skillId, PlayerStatus status, float? cooldown, bool restoreHasTarget)
        {
            if (status != PlayerStatus.Normal) return SkillRing.State.Disabled;
            if (cooldown.HasValue && cooldown.Value > 0) return SkillRing.State.Cooldown;
            if (skillId == SkillIds.EmergencyRestore && !restoreHasTarget) return SkillRing.State.Disabled;
            return SkillRing.State.Ready;
        }

        // HUD_SPEC 4.9 / 4.3 info lines (the v1 Exit line 3 is replaced by the objective row, 13.1).
        public static string TerminalsLine(int done, int needed) => "Terminals " + done + "/" + needed;

        public static string Ellipsize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a Survivor";
            if (name.Length <= NameBudget) return name;
            int cut = NameBudget - 1;
            if (char.IsHighSurrogate(name[cut - 1])) cut--; // never split a surrogate pair
            return name.Substring(0, cut) + "…";
        }

        // HUD_SPEC 4.3 / 4.4 / 4.8 prompt row, first match wins: cursor released, rescuer, a Lagging ally within 6 m,
        // hacking, can hack, Lagging at a terminal, skill reasons. names: lobby nickname by player id (may be null).
        public static PromptState Prompt(OwnEntry own, IReadOnlyList<Mate> mates, IReadOnlyList<TerminalState> terminals, ISelfState self,
            Func<int, string> names, bool cursorReleased, byte skillId, bool restoreHasTarget)
        {
            var p = new PromptState();
            if (cursorReleased) { p.Text = CursorHint; p.Muted = true; return p; }
            if (own.Status == PlayerStatus.Eliminated || own.Status == PlayerStatus.Escaped) return p;
            bool normal = own.Status == PlayerStatus.Normal;
            if (normal && own.Rescuing)
            {
                int target = NearestLagging(own, mates, GameConstants.RescueRadius, out _);
                if (target < 0) target = NearestLagging(own, mates, float.MaxValue, out _);
                p.Text = "Rescuing " + Name(names, target >= 0 ? mates[target].Id : 0) + " " + Dash + " you are Signal";
                p.Bar = BarFill.Ally; p.Width = self != null && self.Available ? Clamp01(self.RescueProgress) : 0;
                return p;
            }
            if (normal)
            {
                int near = NearestLagging(own, mates, ApproachRange, out float distance);
                if (near >= 0)
                {
                    p.Text = Name(names, mates[near].Id) + " is Lagging " + Dot + " " + (int)Math.Round(distance, MidpointRounding.AwayFromZero)
                        + " m " + Dash + " stay within 1.5 m to rescue them";
                    return p;
                }
            }
            int terminal = HudCopy.NearestTerminal(terminals, own.X, own.Z);
            bool atTerminal = terminal >= 0 && Distance(own.X, own.Z, terminals[terminal].X, terminals[terminal].Z) <= GameConstants.TerminalRadius;
            if (own.Hacking && terminal >= 0)
            {
                TerminalState t = terminals[terminal];
                int hackers = 1;
                if (mates != null)
                    foreach (Mate mate in mates)
                        if (mate.Hacking && mate.Status == PlayerStatus.Normal && HudCopy.NearestTerminal(terminals, mate.X, mate.Z) == terminal
                            && Distance(mate.X, mate.Z, t.X, t.Z) <= GameConstants.TerminalRadius) hackers++;
                p.Text = "Hacking " + KillerHudCopyTerminal(t.Id) + " " + Dot + " " + Percent(t.Progress) + (hackers >= 2 ? " " + Dot + " " + hackers + " hacking" : string.Empty);
                p.Bar = BarFill.Text; p.Width = Math.Min((int)t.Progress, 100) / 100f;
                return p;
            }
            if (normal && atTerminal && terminals[terminal].Progress < 100)
            {
                TerminalState t = terminals[terminal];
                // 11/17 (R2): the keys of the route panel are named up front (61 characters at most, SLOP length).
                p.Text = "Hold [E] + WASD to hack " + KillerHudCopyTerminal(t.Id) + " " + Dot + " " + Percent(t.Progress) + " " + Dash + " you will be Signal";
                return p;
            }
            if (own.Status == PlayerStatus.Lagging && atTerminal) { p.Text = LaggingCannotHack; p.Muted = true; return p; }
            if (own.Status == PlayerStatus.Lagging) { p.Text = SkillsOffLagging; p.Muted = true; return p; }
            float? cooldown = self != null && self.Available ? self.SkillCooldown : (float?)null;
            if (skillId == SkillIds.EmergencyRestore && !restoreHasTarget && !(cooldown > 0)) { p.Text = RestoreNeedsTarget; p.Muted = true; }
            return p;
        }

        // Emergency restore's precondition (HUD_SPEC 4.8, derived): a Lagging Survivor within 1.5 m.
        public static bool RestoreHasTarget(OwnEntry own, IReadOnlyList<Mate> mates)
            => NearestLagging(own, mates, SkillIds.EmergencyRestoreRadius, out _) >= 0;

        // Index into mates of the nearest Lagging Survivor within range, or -1.
        public static int NearestLagging(OwnEntry own, IReadOnlyList<Mate> mates, float range, out float distance)
        {
            int best = -1; distance = float.PositiveInfinity;
            if (mates == null) return best;
            for (int i = 0; i < mates.Count; i++)
            {
                if (mates[i].Status != PlayerStatus.Lagging) continue;
                float d = Distance(own.X, own.Z, mates[i].X, mates[i].Z);
                if (d <= range && d < distance) { distance = d; best = i; }
            }
            return best;
        }

        private static string Name(Func<int, string> names, int id) => Ellipsize(names?.Invoke(id));
        private static string KillerHudCopyTerminal(byte id) => "Terminal " + (id + 1);
        private static string Percent(byte progress) => Math.Min((int)progress, 100) + "%";
        private static float Distance(float ax, float az, float bx, float bz) { float dx = ax - bx, dz = az - bz; return (float)Math.Sqrt(dx * dx + dz * dz); }
        private static float Clamp01(float value) => float.IsNaN(value) || value < 0 ? 0 : value > 1 ? 1 : value;
    }
}
