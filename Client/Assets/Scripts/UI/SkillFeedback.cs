using System;
using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.Killer;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 14 skill feedback, pure (no UnityEngine): which viewer an S2C_SkillEffect makes this client, and the record
    // it plays: one result line, one world effect, one sound. World/SkillFx only plays records; HUD classes only show lines.
    public enum FxViewer : byte { None, Caster, Affected, Ally, Killer }
    // Result-line bar colour (14.1a): Reason = no bar, `muted` text; Plain = no bar, `text` (Killer own-skill lines).
    public enum LineTone : byte { Reason, Danger, Signal, Ally, Plain }
    // Hovl = a vendor one-shot; Ring = the procedural ground ring (fixed or growing); Sweep = the Ping scanner ring.
    public enum FxKind : byte { None, Hovl, Ring, Sweep }
    public enum FxPrefab : byte { None, Pointer, Zone, Pillar, Circle, Arrows, DangerZone }
    public enum FxTint : byte { Signal, Ally, Danger, Text }
    public enum FxClip : byte
    {
        None, ScanKiller, ScanTarget, OverclockStart, OverclockRecover, FirewallKiller, FirewallSurvivor,
        Dummy, Relay, Overload, OverloadSlowed, Restore, WhiteNoise
    }
    // Where the world effect stands: the effect's X, Z, the own rendered position, or an exit switch point (static map).
    public enum FxAnchor : byte { None, Effect, Own, Switch }
    // Survivor 2D cues of Killer skills that target the viewer sit above the skill tier (14.1 Sound priority).
    public enum FxTier : byte { Skill, High, World }

    public struct FxLine
    {
        public string Text;
        public LineTone Tone;
        public byte Priority;
        public float Seconds;
        public bool Shown => !string.IsNullOrEmpty(Text);
    }

    public struct FxVisual
    {
        public FxKind Kind;
        public FxPrefab Prefab;
        public FxTint Tint;
        public FxAnchor Anchor;
        public float X, Z;        // world point for Effect / Switch anchors
        public float Diameter;    // outer diameter in metres (final diameter for Ring / Sweep)
        public float StartDiameter; // Ring / Sweep start (equal to Diameter when it does not grow)
        public float GrowSeconds; // 0 = full size at once
        public float Seconds;     // cleared at this time at the latest
        public bool Shown => Kind != FxKind.None;
    }

    public struct FxSound
    {
        public FxClip Clip;
        public float Db;
        public bool Positional;
        public float Range;  // positional only: silent beyond (metres)
        public float X, Z;   // positional only: the effect's world point (never the listener's own position)
        public float Cut;    // stop at this time (seconds); 0 = full length
        public float Fade;   // fade-out before the cut
        public FxTier Tier;
        public bool Shown => Clip != FxClip.None;
    }

    public struct FxRecord
    {
        public FxViewer Viewer;
        public byte SkillId;
        public bool KillerSkill;
        public FxLine Line;
        public FxVisual Visual;
        public FxSound Sound;
    }

    // What a Survivor client knows when an effect arrives or a snapshot follows (HUD_SPEC 0.1: full Survivor state).
    public struct SurvivorFrame
    {
        public int OwnId;
        public OwnEntry Own;
        public IReadOnlyList<Mate> Mates;
        public Func<int, string> Names;
        public byte ActionKind; // own S2C_SelfState.ActionKind (1 = exit switch)
    }

    public static class SkillFeedback
    {
        public const string Dot = "·";
        public const byte AffectedDanger = 5, AffectedPriority = 4, CasterPriority = 3, AllyPriority = 2, ReasonPriority = 1;
        public const float LineSeconds = 2.5f, ReasonSeconds = 2f;
        public const float ExitPickRange = 1f, RestoreMatchRange = .5f;
        public const float SwitchDiameter = 2 * GameConstants.ExitSwitchRadius;
        public const float SweepFrom = 1, SweepTo = 16, SweepSeconds = .6f, RelayGrowSeconds = .15f;
        public const float OverclockFovDegrees = 4, OverclockFovEase = .15f;
        public const float CueReference = .4f; // the 0 dB cue amplitude (capture_confirm)

        // R1 (11/17): Firewall block fails only when no Exit switch and no terminal has progress.
        public const string NoSwitchReason = "Not used " + Dot + " nothing to lock";
        // R1 (11/17): the own swing ended without a capture (KillerSwingWatch). a2: its own brief line in the text tone (not
        // the muted reason), above the own skill line, for MissedSeconds.
        public const string Missed = "Missed";
        public const float MissedSeconds = 1f;
        // (11/18, M1) A hit with no capture confirm (an eliminating hit, or one on a Survivor never seen): the same line slot, `Hit`.
        public const string Hit = "Hit";
        // R1 (11/17): Packet trace's world ring grows to its 12 m radius in this time.
        public const float TraceGrowSeconds = .35f, TraceSeconds = .6f;
        public const string KillerRefused = "Not used " + Dot + " server did not accept";
        public const string SurvivorRefused = "Skill not used — the server did not accept it.";
        // Q3 server reasons (14.3): the server cooldown had not ended though the client derived Ready.
        public const string SurvivorOnCooldown = "Skill still on cooldown.";
        public const string KillerOnCooldown = "Not used " + Dot + " still on cooldown";
        public const string KillerNoScanTarget = "Not used " + Dot + " no Survivor to scan";

        // Vendor clip paths under AUDIO/ (HUD_SPEC 14.4) and their measured peaks (int16 peak / 32768).
        public static readonly string[] ClipPaths =
        {
            null,
            "Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_2.wav",
            "Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_1.wav",
            "Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun1_Set_to_Charge_Part_2.wav",
            "Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Reload_Magazine_Out_1.wav",
            "Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Set_Part_1.wav",
            "Mechanics/SFX_GUNS_MECH_SCIFI_Pistol_or_Rifle_Set_Part_2.wav",
            "Shoot/Energy/SFX_SCIFI_WEAPON_Energy_Shoot_Big_Gun3_1.wav",
            "Skill/SFX_SCIFI_ENERGY_Skill_Whoosh_Electrified_1.wav",
            "Shoot/Electric/SFX_SCIFI_WEAPON_Electric_Shoot_Medium_Gun_1.wav",
            "Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun_Out_of_Ammo.wav",
            "Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_Start_2.wav",
            "Skill/SFX_SCIFI_SKILL_Fire_Rain_1.wav",
        };
        public static readonly float[] ClipPeaks = { 1, .82f, .75f, .78f, .63f, .69f, .85f, .84f, .85f, .84f, .78f, .69f, .85f };

        // Vendor prefab names (Map track markers VFX/Prefabs) and the diameter each draws at scale 1 (its widest system).
        public static readonly string[] PrefabNames = { null, "Marker 2 Pointer", "Marker 3 Zone", "Marker 4 Pillar", "Marker 5 Circle", "Marker 6 Arrows", "Marker 7 Danger zone" };
        public static readonly float[] PrefabDiameters = { 1, 2.5f, 3, 3, 2.04f, 1.8f, 5 };

        // AudioSource.volume for a clip at `db` (14.1 Sound: peak at the cue reference, then the stated dB).
        public static float Volume(FxClip clip, float db)
        {
            float peak = ClipPeaks[(int)clip];
            return Math.Min(1f, CueReference / Math.Max(.01f, peak) * (float)Math.Pow(10, db / 20));
        }

        // Positional attenuation (the 6.5.1 curve): the stated level up to 2 m, linear in dB to -40 dB at `range`, muted beyond.
        public static float Attenuation(float distance, float range)
        {
            if (distance <= 2) return 1;
            if (distance > range) return 0;
            float db = -40 * (distance - 2) / Math.Max(.01f, range - 2);
            return (float)Math.Pow(10, db / 20);
        }

        public static float Clamp(float duration) => float.IsNaN(duration) || duration < 0 ? 0 : Math.Min(duration, 60);

        public static string Num(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
        public static string Seconds1(float value) => (float.IsNaN(value) || value < 0 ? 0 : value).ToString("0.0", CultureInfo.InvariantCulture) + " s";

        private static string Name(Func<int, string> names, int id) => SurvivorReadout.Ellipsize(names?.Invoke(id));
        private static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        // The exit (static map) within 1 m of a Firewall block point, or -1 (14.0).
        public static int ExitAt(float x, float z)
        {
            IReadOnlyList<MapPoint> exits = SharedMapPoints.Exits;
            for (int i = 0; i < exits.Count; i++)
            {
                float dx = exits[i].X - x, dz = exits[i].Z - z;
                if (dx * dx + dz * dz <= ExitPickRange * ExitPickRange) return i;
            }
            return -1;
        }

        public static string ExitSwitch(int exit) => exit >= 0 ? "Exit " + (exit + 1).ToString(CultureInfo.InvariantCulture) + " switch" : "An Exit switch";

        // R1 (11/17): the map terminal (static FixedMap index = TerminalState.Id) within 1 m of a Firewall block point, or -1.
        public static int TerminalAt(float x, float z)
        {
            IReadOnlyList<MapPoint> terminals = SharedMapPoints.Terminals;
            for (int i = 0; i < terminals.Count; i++)
            {
                float dx = terminals[i].X - x, dz = terminals[i].Z - z;
                if (dx * dx + dz * dz <= ExitPickRange * ExitPickRange) return i;
            }
            return -1;
        }

        public static string TerminalName(int terminal) => terminal >= 0 ? "Terminal " + (terminal + 1).ToString(CultureInfo.InvariantCulture) : "A terminal";

        // ---- Killer client (14.0 Killer rule) ----

        // The Killer acts on its own effects and on the Killer-bound Traffic overload (CasterId 0) only; anything else,
        // including any Survivor skill that would reach it, yields Viewer None. x, z: the effect point (own position for
        // the own skills; a static exit point for Firewall block); never a Survivor's.
        public static FxRecord ForKiller(int ownId, int casterId, byte skillId, float x, float z, float duration)
        {
            var r = new FxRecord { SkillId = skillId, KillerSkill = casterId != 0 };
            float d = Clamp(duration);
            if (casterId == 0)
            {
                if (skillId != SkillIds.TrafficOverload) return default;
                r.Viewer = FxViewer.Killer; r.KillerSkill = false;
                r.Line = new FxLine { Tone = LineTone.Danger, Priority = AffectedDanger, Seconds = d }; // text ticks (KillerFxState)
                r.Sound = Cue(FxClip.OverloadSlowed, -6, 1f, FxTier.High);
                return r;
            }
            if (casterId != ownId) return default;
            r.Viewer = FxViewer.Caster;
            r.Line = new FxLine { Tone = LineTone.Plain, Priority = CasterPriority, Seconds = d };
            switch (skillId)
            {
                case SkillIds.PingScanner:
                    r.Visual = new FxVisual { Kind = FxKind.Sweep, Tint = FxTint.Text, Anchor = FxAnchor.Own, StartDiameter = 2 * SweepFrom, Diameter = 2 * SweepTo, GrowSeconds = SweepSeconds, Seconds = SweepSeconds };
                    r.Sound = Cue(FxClip.ScanKiller, -9, 0, FxTier.Skill);
                    break;
                case SkillIds.Overclock:
                    r.Sound = Cue(FxClip.OverclockStart, -9, 0, FxTier.Skill);
                    break;
                case SkillIds.FirewallBlock:
                    int exit = ExitAt(x, z);
                    if (exit >= 0)
                    {
                        MapPoint at = SharedMapPoints.Switches[exit];
                        r.Visual = Hovl(FxPrefab.Zone, FxTint.Text, FxAnchor.Switch, at.X, at.Z, SwitchDiameter, 1f);
                    }
                    else if (TerminalAt(x, z) >= 0) // R1: the locked terminal (a static map point)
                        r.Visual = Hovl(FxPrefab.Zone, FxTint.Text, FxAnchor.Effect, x, z, SwitchDiameter, 1f);
                    r.Sound = Cue(FxClip.FirewallKiller, -9, 0, FxTier.Skill);
                    break;
                case SkillIds.PacketTrace:
                    // R1: a short ground ring bursting out to the 12 m trace radius around the own point.
                    r.Visual = new FxVisual { Kind = FxKind.Ring, Tint = FxTint.Text, Anchor = FxAnchor.Own, StartDiameter = 2, Diameter = 2 * SkillIds.PacketTraceRadius, GrowSeconds = TraceGrowSeconds, Seconds = TraceSeconds };
                    r.Sound = Cue(FxClip.ScanKiller, -9, 0, FxTier.Skill);
                    break;
                default:
                    return default;
            }
            return r;
        }

        // ---- Survivor client ----

        // 14.0 Survivor rule: own id = own skill; a Survivor of the last snapshot = that Survivor's; else the Killer's.
        public static bool CasterIsSurvivor(int casterId, int ownId, IReadOnlyList<Mate> mates)
        {
            if (casterId == ownId) return true;
            if (mates != null) foreach (Mate mate in mates) if (mate.Id == casterId) return true;
            return false;
        }

        // The record for one received effect, or Viewer None. A Stealth relay from another Survivor gives a record with
        // the world ring only; its line and sound wait for the next snapshot (SurvivorFxState.OnWorld).
        public static FxRecord ForSurvivor(in SurvivorFrame f, int casterId, byte skillId, float x, float z, float duration)
        {
            float d = Clamp(duration);
            bool own = casterId == f.OwnId;
            bool survivorSkill = CasterIsSurvivor(casterId, f.OwnId, f.Mates);
            var r = new FxRecord { SkillId = skillId, KillerSkill = !survivorSkill };
            string dur = Num(d) + " s";
            if (!survivorSkill)
            {
                switch (skillId)
                {
                    case SkillIds.PingScanner:
                        // Sent to the scanned Survivor only, with X = Z = 0: the effect point is never read.
                        r.Viewer = FxViewer.Affected;
                        r.Line = Line("Scanned: the Killer sees you " + Dot + " " + dur, LineTone.Danger, AffectedDanger);
                        r.Visual = Hovl(FxPrefab.Circle, FxTint.Signal, FxAnchor.Own, 0, 0, 2, .6f);
                        r.Sound = Cue(FxClip.ScanTarget, -6, 0, FxTier.High);
                        return r;
                    case SkillIds.PacketTrace:
                        // R1: sent to each traced Survivor only, with X = Z = 0 (never the Killer position).
                        r.Viewer = FxViewer.Affected;
                        r.Line = Line("Traced: the Killer sees you " + Dot + " " + dur, LineTone.Danger, AffectedDanger);
                        r.Visual = new FxVisual { Kind = FxKind.Ring, Tint = FxTint.Signal, Anchor = FxAnchor.Own, StartDiameter = 4, Diameter = 2, GrowSeconds = TraceGrowSeconds, Seconds = TraceSeconds };
                        r.Sound = Cue(FxClip.ScanTarget, -6, 0, FxTier.High);
                        return r;
                    case SkillIds.FirewallBlock when ExitAt(x, z) < 0 && TerminalAt(x, z) >= 0:
                    {
                        // R1: a terminal lock (no Exit switch was in progress). Every Survivor is told; a hacker on it most.
                        // A point that is neither an exit nor a terminal keeps the old unplaced Exit-switch reading below.
                        int terminal = TerminalAt(x, z);
                        bool affected = f.Own.Hacking && Within(f.Own.X, f.Own.Z, x, z, GameConstants.TerminalRadius + .5f);
                        r.Viewer = affected ? FxViewer.Affected : FxViewer.Ally;
                        r.Visual = Hovl(FxPrefab.DangerZone, FxTint.Danger, FxAnchor.Effect, x, z, SwitchDiameter, 1f);
                        r.Line = Line(TerminalLocked(terminal, d), LineTone.Danger, affected ? AffectedDanger : AllyPriority);
                        r.Sound = affected ? Cue(FxClip.FirewallSurvivor, -6, 0, FxTier.High) : Positional(FxClip.FirewallSurvivor, -12, 30, 0, x, z);
                        return r;
                    }
                    case SkillIds.FirewallBlock:
                    {
                        int exit = ExitAt(x, z);
                        bool placed = exit >= 0;
                        MapPoint at = placed ? SharedMapPoints.Switches[exit] : default;
                        bool affected = f.ActionKind == (byte)ActionKind.Switch
                            || (placed && Within(f.Own.X, f.Own.Z, at.X, at.Z, GameConstants.ExitSwitchRadius));
                        r.Viewer = affected ? FxViewer.Affected : FxViewer.Ally;
                        if (placed) r.Visual = Hovl(FxPrefab.DangerZone, FxTint.Danger, FxAnchor.Switch, at.X, at.Z, SwitchDiameter, 1f);
                        if (affected)
                        {
                            r.Line = Line(ExitSwitch(exit) + " locked by the Killer " + Dot + " " + dur, LineTone.Danger, AffectedDanger);
                            r.Sound = Cue(FxClip.FirewallSurvivor, -6, 0, FxTier.High);
                        }
                        else if (placed) r.Sound = Positional(FxClip.FirewallSurvivor, -12, 30, 0, at.X, at.Z);
                        return r;
                    }
                    default:
                        return default;
                }
            }
            string caster = Name(f.Names, casterId);
            r.Viewer = own ? FxViewer.Caster : FxViewer.Ally;
            switch (skillId)
            {
                case SkillIds.DummyPacket:
                    r.Visual = Hovl(FxPrefab.Pointer, FxTint.Signal, FxAnchor.Effect, x, z, 1.5f, .8f);
                    if (own) { r.Line = Line("Dummy packet: fake Signal sent from here " + Dot + " " + dur, LineTone.Ally, CasterPriority); r.Sound = Cue(FxClip.Dummy, -9, .6f, FxTier.Skill); }
                    else r.Sound = Positional(FxClip.Dummy, -15, 20, 0, x, z);
                    return r;
                case SkillIds.StealthRelay:
                    r.Visual = new FxVisual { Kind = FxKind.Ring, Tint = FxTint.Ally, Anchor = FxAnchor.Effect, X = x, Z = z, StartDiameter = 0, Diameter = 2 * SkillIds.StealthRelayRadius, GrowSeconds = RelayGrowSeconds, Seconds = Math.Max(d, RelayGrowSeconds) };
                    if (own) { r.Sound = Cue(FxClip.Relay, -12, Math.Min(2f, d), FxTier.Skill); r.Sound.Fade = .3f; }
                    return r; // lines (and the non-caster sounds) come with the next snapshot
                case SkillIds.TrafficOverload:
                    r.Visual = Hovl(FxPrefab.Arrows, FxTint.Signal, FxAnchor.Effect, x, z, 2, .5f);
                    if (own) { r.Line = Line("Traffic overload: you are Signal, the Killer is slowed " + Dot + " " + dur, LineTone.Signal, CasterPriority); r.Sound = Cue(FxClip.Overload, -9, .6f, FxTier.Skill); }
                    else { r.Line = Line(Capital(caster) + " slowed the Killer " + Dot + " " + dur, LineTone.Ally, AllyPriority); r.Sound = Positional(FxClip.Overload, -15, 20, 0, x, z); }
                    return r;
                case SkillIds.EmergencyRestore:
                {
                    r.Visual = Hovl(FxPrefab.Pillar, FxTint.Ally, FxAnchor.Effect, x, z, 1.5f, 1f);
                    bool self = !own && f.Own.Status == PlayerStatus.Lagging && Within(f.Own.X, f.Own.Z, x, z, RestoreMatchRange);
                    string target = Name(f.Names, RestoreTarget(f.Mates, casterId, x, z));
                    if (own) { r.Line = Line("Rescued " + target + " " + Dot + " no longer Lagging", LineTone.Ally, CasterPriority); r.Sound = Cue(FxClip.Restore, -9, 1f, FxTier.Skill); }
                    else if (self)
                    {
                        r.Viewer = FxViewer.Affected;
                        r.Line = Line(Capital(caster) + " rescued you " + Dot + " no longer Lagging", LineTone.Ally, AffectedPriority);
                        r.Sound = Cue(FxClip.Restore, -6, 1f, FxTier.Skill);
                    }
                    else { r.Line = Line(Capital(caster) + " rescued " + target, LineTone.Ally, AllyPriority); r.Sound = Positional(FxClip.Restore, -15, 20, 0, x, z); }
                    return r;
                }
                case SkillIds.WhiteNoise:
                    r.Visual = Hovl(FxPrefab.Zone, FxTint.Signal, FxAnchor.Effect, x, z, 3, 1f);
                    if (own) { r.Line = Line("White noise: fake Signals sent across the map " + Dot + " " + dur, LineTone.Ally, CasterPriority); r.Sound = Cue(FxClip.WhiteNoise, -12, Math.Min(1.5f, Math.Max(d, .2f)), FxTier.Skill); r.Sound.Fade = .2f; }
                    else { r.Line = Line(Capital(caster) + " sent fake Signals across the map " + Dot + " " + dur, LineTone.Ally, AllyPriority); r.Sound = Positional(FxClip.WhiteNoise, -18, 20, 1f, x, z); }
                    return r;
                default:
                    return default;
            }
        }

        // The Stealth relay result for this client once the snapshot after the effect arrived (14.2.6): the caster's count
        // line, the affected line, or (another Survivor out of range) only the positional sound.
        public static FxRecord RelayResolved(in SurvivorFrame f, int casterId, float x, float z, float duration)
        {
            float d = Clamp(duration);
            string dur = Num(d) + " s";
            var r = new FxRecord { SkillId = SkillIds.StealthRelay };
            if (casterId == f.OwnId)
            {
                int covered = 0;
                if (f.Mates != null)
                    foreach (Mate mate in f.Mates)
                        if (mate.Id != f.OwnId && mate.Status == PlayerStatus.Normal && mate.Stealthed && Within(mate.X, mate.Z, x, z, SkillIds.StealthRelayRadius + .05f)) covered++;
                r.Viewer = FxViewer.Caster;
                string who = covered == 0 ? "you are" : "you and " + covered + (covered == 1 ? " Survivor are" : " Survivors are");
                r.Line = Line("Stealth relay: " + who + " Silent " + Dot + " " + dur, LineTone.Ally, CasterPriority);
                return r;
            }
            if (f.Own.Status == PlayerStatus.Normal && f.Own.Stealthed && Within(f.Own.X, f.Own.Z, x, z, SkillIds.StealthRelayRadius + .05f))
            {
                r.Viewer = FxViewer.Affected;
                r.Line = Line("You are Silent " + Dot + " " + dur + " " + Dot + " Stealth relay from " + Name(f.Names, casterId), LineTone.Ally, AffectedPriority);
                r.Sound = Cue(FxClip.Relay, -15, 1f, FxTier.Skill);
                return r;
            }
            r.Viewer = FxViewer.Ally;
            r.Sound = Positional(FxClip.Relay, -18, 20, 1f, x, z);
            return r;
        }

        // 14.2.8: the Survivor (not the caster) that was Lagging in the snapshot before the effect, nearest to x, z within
        // 0.5 m; 0 when none (the line then says "a Survivor").
        public static int RestoreTarget(IReadOnlyList<Mate> mates, int casterId, float x, float z)
        {
            int best = 0; float bestD = RestoreMatchRange * RestoreMatchRange;
            if (mates == null) return 0;
            foreach (Mate mate in mates)
            {
                if (mate.Id == casterId || mate.Status != PlayerStatus.Lagging) continue;
                float dx = mate.X - x, dz = mate.Z - z, dd = dx * dx + dz * dz;
                if (dd <= bestD) { bestD = dd; best = mate.Id; }
            }
            return best;
        }

        // Where the record plays (the one anchoring path ClientApp uses). The world effect: the own rendered position for
        // FxAnchor.Own, otherwise its X, Z. A positional sound: always the effect's world point carried by the sound, whether
        // or not a visual is shown; never the listener (14.1 Sound). 2D sounds have no position (the own point is returned).
        public static void VisualPoint(in FxRecord r, float ownX, float ownZ, out float x, out float z)
        {
            bool own = r.Visual.Anchor == FxAnchor.Own;
            x = own ? ownX : r.Visual.X; z = own ? ownZ : r.Visual.Z;
        }

        public static void SoundPoint(in FxRecord r, float ownX, float ownZ, out float x, out float z)
        {
            if (r.Sound.Positional) { x = r.Sound.X; z = r.Sound.Z; return; }
            x = ownX; z = ownZ;
        }

        // The positional gain a listener at (listenerX, listenerZ) gets for the record's sound (0 = muted); 1 for 2D sounds.
        public static float Heard(in FxRecord r, float listenerX, float listenerZ)
        {
            if (!r.Sound.Shown) return 0;
            if (!r.Sound.Positional) return 1;
            SoundPoint(r, listenerX, listenerZ, out float x, out float z);
            float dx = x - listenerX, dz = z - listenerZ;
            return Attenuation((float)Math.Sqrt(dx * dx + dz * dz), r.Sound.Range);
        }

        // Spectators (Eliminated, Escaped) keep the world effect and positional sound only (14.1 Viewers).
        public static FxRecord ForSpectator(FxRecord r)
        {
            r.Line = default;
            if (r.Sound.Shown && !r.Sound.Positional) r.Sound = default;
            return r;
        }

        // Reduced motion (14.1): a Hovl one-shot becomes its still ring at the final diameter; growing rings start full;
        // the Ping scanner sweep is off.
        public static FxVisual Reduced(FxVisual v)
        {
            if (v.Kind == FxKind.Sweep) return default;
            if (v.Kind == FxKind.Hovl) { v.Kind = FxKind.Ring; v.Prefab = FxPrefab.None; }
            v.StartDiameter = v.Diameter; v.GrowSeconds = 0;
            return v;
        }

        // Diameter of a Ring / Sweep at `elapsed` seconds (linear growth); -1 once it has ended.
        public static float DiameterAt(in FxVisual v, double elapsed)
        {
            if (elapsed < 0 || elapsed >= v.Seconds) return -1;
            if (v.GrowSeconds <= 0 || elapsed >= v.GrowSeconds) return v.Diameter;
            return v.StartDiameter + (v.Diameter - v.StartDiameter) * (float)(elapsed / v.GrowSeconds);
        }

        // ---- Presses (14.3) ----

        // The reason line for a press the client consumes itself, and whether the 0.5 s refused-press watch may arm. A press
        // that is passive, made while not Normal, on cooldown (or before the first SelfState) or failing the derived
        // precondition never arms the watch.
        public static string PressReason(Role role, byte skillId, PlayerStatus status, float? cooldown, bool precondition, out bool watch)
        {
            watch = false;
            if (status == PlayerStatus.Lagging) return SurvivorReadout.SkillsOffLagging;
            if (status != PlayerStatus.Normal) return null;
            if (!cooldown.HasValue || cooldown.Value > 0) return null;
            if (!precondition)
            {
                if (role == Role.Survivor && skillId == SkillIds.EmergencyRestore) return SurvivorReadout.RestoreNeedsTarget;
            }
            watch = true;
            return null;
        }

        // 14.2.2: the Recovering-start cue (Overclock has ended; R1: 0.3 s at 60 % speed).
        public static FxRecord RecoverCue() => new FxRecord
        {
            Viewer = FxViewer.Caster, SkillId = SkillIds.Overclock, KillerSkill = true,
            Sound = new FxSound { Clip = FxClip.OverclockRecover, Db = -12, Fade = .05f, Tier = FxTier.Skill }
        };

        // Q3 (14.3 "Server reason"): the line for an S2C_SkillRejected of the own press, or null for none. Cases the client
        // also pre-checks use the same text as the pre-check (PressReason), so both paths never disagree.
        public static string RejectionLine(Role role, byte skillId, SkillRejectReason reason, PlayerStatus status)
        {
            bool survivor = role == Role.Survivor;
            switch (reason)
            {
                case SkillRejectReason.Cooldown:
                    return survivor ? SurvivorOnCooldown : KillerOnCooldown;
                case SkillRejectReason.NoTarget:
                    if (survivor && skillId == SkillIds.EmergencyRestore) return SurvivorReadout.RestoreNeedsTarget;
                    if (!survivor && skillId == SkillIds.PingScanner) return KillerNoScanTarget;
                    break;
                case SkillRejectReason.NoSwitchInProgress:
                    if (!survivor) return NoSwitchReason;
                    break;
                case SkillRejectReason.NotNormal:
                    // Eliminated and Escaped have their own banners; the Killer is never refused for this.
                    if (survivor) return status == PlayerStatus.Lagging ? SurvivorReadout.SkillsOffLagging : null;
                    break;
            }
            // NotInMatch, NotYourSkill, Sandbox, a reason that does not fit the role, or a value from a newer server.
            return survivor ? SurvivorRefused : KillerRefused;
        }

        public static FxLine Reason(string text) => new FxLine { Text = text, Tone = LineTone.Reason, Priority = ReasonPriority, Seconds = ReasonSeconds };

        // ---- helpers ----

        // R1 (11/17), Survivor view: "Terminal 3 · Locked · 8 s".
        public static string TerminalLocked(int terminal, float seconds) => TerminalName(terminal) + " " + Dot + " Locked " + Dot + " " + Num(seconds) + " s";

        private static FxLine Line(string text, LineTone tone, byte priority) => new FxLine { Text = text, Tone = tone, Priority = priority, Seconds = LineSeconds };

        private static FxVisual Hovl(FxPrefab prefab, FxTint tint, FxAnchor anchor, float x, float z, float diameter, float seconds)
            => new FxVisual { Kind = FxKind.Hovl, Prefab = prefab, Tint = tint, Anchor = anchor, X = x, Z = z, Diameter = diameter, StartDiameter = diameter, Seconds = seconds };

        private static FxSound Cue(FxClip clip, float db, float cut, FxTier tier) => new FxSound { Clip = clip, Db = db, Cut = cut, Fade = .05f, Tier = tier };

        private static FxSound Positional(FxClip clip, float db, float range, float cut, float x, float z)
            => new FxSound { Clip = clip, Db = db, Positional = true, Range = range, Cut = cut, Fade = .05f, Tier = FxTier.World, X = x, Z = z };

        private static bool Within(float ax, float az, float bx, float bz, float range)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz <= range * range + 1e-6f;
        }
    }

    // Static exit and switch points (the same FixedMap the server runs), kept here so the pure rules need no UnityEngine.
    public static class SharedMapPoints
    {
        private static readonly FixedMap Map = new FixedMap();
        public static IReadOnlyList<MapPoint> Exits => Map.Exits;
        public static IReadOnlyList<MapPoint> Switches => Map.ExitSwitches;
        public static IReadOnlyList<MapPoint> Terminals => Map.TerminalPositions;
    }

    // HUD_SPEC 14.1a: one result line at a time, never stacked. A new line replaces the shown one when its priority is
    // equal or higher; a lower one is dropped, not queued.
    public sealed class ResultLine
    {
        private FxLine shown;
        private double until = double.NegativeInfinity;

        public void Reset() { shown = default; until = double.NegativeInfinity; }

        public bool Offer(FxLine line, double now)
        {
            if (!line.Shown) return false;
            if (now < until && line.Priority < shown.Priority) return false;
            shown = line; until = now + line.Seconds;
            return true;
        }

        public FxLine Current(double now) => now < until ? shown : default;
    }

    // HUD_SPEC 14.3 refused-press inference: armed only by a press the client sent while it derived Ready; fires once when,
    // within 0.5 s, neither an own effect nor an S2C_SkillRejected arrived, nor the cooldown became > 0. Since Q3 the
    // server answers every refusal, so the watch is only a fallback (a reply slower than 0.5 s).
    public sealed class PressWatch
    {
        public const double Window = .5;
        private double pressedAt;
        public bool Armed { get; private set; }

        public void Reset() => Armed = false;
        public void OnPress(double now, bool arm) { Armed = arm; pressedAt = now; }
        public void OnOwnEffect() => Armed = false;

        // Q3: a server rejection answers the pending (armed) press and disarms the watch. False when no press is pending:
        // after an own effect, after the fallback fired, or for a press the client already explained itself.
        public bool TakeRejection()
        {
            if (!Armed) return false;
            Armed = false;
            return true;
        }

        public bool Tick(double now, float? cooldown)
        {
            if (!Armed) return false;
            if (cooldown.HasValue && cooldown.Value > 0) { Armed = false; return false; }
            if (now - pressedAt < Window) return false;
            Armed = false;
            return true;
        }
    }

    // Survivor-side state between an effect and the snapshot that follows it (14.2.6 Stealth relay).
    public sealed class SurvivorFxState
    {
        private struct Pending { public int Caster; public float X, Z, Duration; }
        private readonly List<Pending> relays = new List<Pending>();
        public ResultLine Line { get; } = new ResultLine();
        public PressWatch Press { get; } = new PressWatch();

        public void Reset() { relays.Clear(); Line.Reset(); Press.Reset(); }

        public FxRecord OnEffect(in SurvivorFrame f, bool spectating, int casterId, byte skillId, float x, float z, float duration, double now)
        {
            FxRecord r = SkillFeedback.ForSurvivor(f, casterId, skillId, x, z, duration);
            if (r.Viewer == FxViewer.None) return r;
            if (casterId == f.OwnId) Press.OnOwnEffect();
            if (!r.KillerSkill && skillId == SkillIds.StealthRelay) relays.Add(new Pending { Caster = casterId, X = x, Z = z, Duration = duration });
            if (spectating) r = SkillFeedback.ForSpectator(r);
            Line.Offer(r.Line, now);
            return r;
        }

        // Call after each WorldState (frame = that snapshot). Resolved relays are appended to `output`.
        public void OnWorld(in SurvivorFrame f, bool spectating, double now, List<FxRecord> output)
        {
            for (int i = 0; i < relays.Count; i++)
            {
                FxRecord r = SkillFeedback.RelayResolved(f, relays[i].Caster, relays[i].X, relays[i].Z, relays[i].Duration);
                if (spectating) r = SkillFeedback.ForSpectator(r);
                Line.Offer(r.Line, now);
                output.Add(r);
            }
            relays.Clear();
        }

        public void OnReason(string text, double now) { if (!string.IsNullOrEmpty(text)) Line.Offer(SkillFeedback.Reason(text), now); }

        // Q3: an S2C_SkillRejected. Only the own pending press shows its reason, at once, on the 14.1a line; a reason the
        // prompt row already shows steadily (4.8 Disabled) is not repeated. Returns the text shown, or null.
        public string OnRejected(byte ownSkill, byte skillId, SkillRejectReason reason, PlayerStatus status, string promptText, double now)
        {
            if (skillId != ownSkill || !Press.TakeRejection()) return null;
            string text = SkillFeedback.RejectionLine(Role.Survivor, skillId, reason, status);
            if (string.IsNullOrEmpty(text) || text == promptText) return null;
            OnReason(text, now);
            return text;
        }
    }

    // Killer-side state (14.1 Result line, Killer; 14.2.1, 14.2.2, 14.2.4, 14.2.7): the 6.1c line, the Recovering cue and
    // the Overclock FOV offset.
    public sealed class KillerFxState
    {
        private double scanEnd = double.NegativeInfinity, overclockStart = double.NegativeInfinity, overclockEnd = double.NegativeInfinity;
        private double lockEnd = double.NegativeInfinity, slowedEnd = double.NegativeInfinity, reasonEnd = double.NegativeInfinity;
        private double traceEnd = double.NegativeInfinity, missedEnd = double.NegativeInfinity, hitEnd = double.NegativeInfinity;
        private byte ownSkill;
        private int lockExit = -1, lockTerminal = -1;
        private string reason;
        private bool recoverPending;
        public PressWatch Press { get; } = new PressWatch();

        public void Reset()
        {
            scanEnd = overclockStart = overclockEnd = lockEnd = slowedEnd = reasonEnd = traceEnd = missedEnd = hitEnd = double.NegativeInfinity;
            lockExit = lockTerminal = -1; reason = null; recoverPending = false; Press.Reset();
        }

        public FxRecord OnEffect(int ownId, int casterId, byte skillId, float x, float z, float duration, double now)
        {
            FxRecord r = SkillFeedback.ForKiller(ownId, casterId, skillId, x, z, duration);
            if (r.Viewer == FxViewer.None) return r;
            float d = SkillFeedback.Clamp(duration);
            if (r.Viewer == FxViewer.Killer) { slowedEnd = now + d; return r; }
            Press.OnOwnEffect();
            reasonEnd = double.NegativeInfinity;
            ownSkill = skillId;
            if (skillId == SkillIds.PingScanner) scanEnd = now + d;
            else if (skillId == SkillIds.Overclock) { overclockStart = now; overclockEnd = now + d; recoverPending = true; }
            else if (skillId == SkillIds.FirewallBlock)
            {
                lockEnd = now + d; lockExit = SkillFeedback.ExitAt(x, z);
                lockTerminal = lockExit >= 0 ? -1 : SkillFeedback.TerminalAt(x, z);
            }
            else if (skillId == SkillIds.PacketTrace) traceEnd = now + d;
            return r;
        }

        public void OnReason(string text, double now)
        {
            if (string.IsNullOrEmpty(text)) return;
            reason = text; reasonEnd = now + SkillFeedback.ReasonSeconds;
        }

        // R1 (11/17) a2: the own swing missed (KillerSwingWatch).
        public void OnMissed(double now) { hitEnd = double.NegativeInfinity; missedEnd = now + SkillFeedback.MissedSeconds; }

        // (11/18, M1) A hit proven by the own flag's length: replaces a `Missed` still showing.
        public void OnHit(double now) { missedEnd = double.NegativeInfinity; hitEnd = now + SkillFeedback.MissedSeconds; }

        // Q3: an S2C_SkillRejected. Only the own pending press shows its reason, at once, on the 6.1c line (`muted`).
        // Returns the text shown, or null.
        public string OnRejected(byte ownSkill, byte skillId, SkillRejectReason reason, double now)
        {
            if (skillId != ownSkill || !Press.TakeRejection()) return null;
            string text = SkillFeedback.RejectionLine(Role.Killer, skillId, reason, PlayerStatus.Normal);
            OnReason(text, now);
            return text;
        }

        // The Recovering-start cue, once, when Overclock ends (14.2.2).
        public bool TakeRecoverCue(double now)
        {
            if (!recoverPending || now < overclockEnd) return false;
            recoverPending = false;
            return now < overclockEnd + SkillIds.OverclockRecoverySeconds;
        }

        // The 6.1c line: Slowed > the most recent own skill > reason. Text null = hidden.
        public FxLine Line(double now)
        {
            if (now < slowedEnd) return new FxLine { Text = KillerHudCopy.Slowed((float)(slowedEnd - now)), Tone = LineTone.Danger };
            if (now >= missedEnd - SkillFeedback.MissedSeconds && now < missedEnd) return new FxLine { Text = SkillFeedback.Missed, Tone = LineTone.Plain };
            if (now >= hitEnd - SkillFeedback.MissedSeconds && now < hitEnd) return new FxLine { Text = SkillFeedback.Hit, Tone = LineTone.Plain };
            string own = OwnLine(now);
            if (own != null) return new FxLine { Text = own, Tone = LineTone.Plain };
            if (now < reasonEnd) return new FxLine { Text = reason, Tone = LineTone.Reason };
            return default;
        }

        private static readonly byte[] Order = { SkillIds.Overclock, SkillIds.PingScanner, SkillIds.FirewallBlock, SkillIds.PacketTrace };

        // R1 (11/17): the terminal (static map index) the own Firewall block holds locked now, or -1 (the terminal marker tag).
        public int LockedTerminal(double now) => now < lockEnd ? lockTerminal : -1;

        // The latest own skill's line while it runs; otherwise any other own line still running.
        private string OwnLine(double now)
        {
            string latest = OwnLine(ownSkill, now);
            if (latest != null) return latest;
            foreach (byte skill in Order) { string line = OwnLine(skill, now); if (line != null) return line; }
            return null;
        }

        private string OwnLine(byte skill, double now)
        {
            switch (skill)
            {
                case SkillIds.Overclock:
                    if (now < overclockEnd) return KillerHudCopy.Overclock((float)(overclockEnd - now));
                    double recovered = overclockEnd + SkillIds.OverclockRecoverySeconds;
                    return now < recovered ? KillerHudCopy.Recovering((float)(recovered - now)) : null;
                case SkillIds.PingScanner: return now < scanEnd ? KillerHudCopy.Scanned((float)(scanEnd - now)) : null;
                case SkillIds.FirewallBlock:
                    if (now >= lockEnd) return null;
                    return lockExit < 0 && lockTerminal >= 0 ? KillerHudCopy.TerminalLocked(lockTerminal, (float)(lockEnd - now))
                        : KillerHudCopy.Locked(lockExit, (float)(lockEnd - now));
                case SkillIds.PacketTrace: return now < traceEnd ? KillerHudCopy.Traced((float)(traceEnd - now)) : null;
                default: return null;
            }
        }

        // Vertical FOV offset (degrees) for Overclock: +4 eased in over 0.15 s, held, eased out over 0.15 s from the
        // effect's end (when Recovering starts). 0 with reduced motion.
        public float FovOffset(double now, bool reduced)
        {
            if (reduced || now < overclockStart) return 0;
            double ease = SkillFeedback.OverclockFovEase;
            if (now < overclockEnd) return SkillFeedback.OverclockFovDegrees * (float)Math.Min(1, (now - overclockStart) / ease);
            double after = now - overclockEnd;
            return after >= ease ? 0 : SkillFeedback.OverclockFovDegrees * (float)(1 - after / ease);
        }
    }
}
