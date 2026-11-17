using System.Collections.Generic;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.World
{
    // U9 (HUD_SPEC 14.1 Signatures): one visual signature per skill, so a glance tells which skill fired. Every signature has
    // its own shape and its own motion; colour stays a palette meaning (SLOP 8: four colours, one meaning each), so two
    // skills always differ in at least shape and motion. Pure: SkillFeedback still decides WHO sees an effect and WHERE
    // (14.0 / 14.2 viewer rules); this only decides HOW it looks.
    // R1 (11/17): TraceRing / Flash (Packet trace) replaced the passive afterimage's Afterimage / Linger.
    public enum FxShape : byte { None, PulseRings, Streaks, HexWall, WireDouble, Dome, Spikes, Beam, Static, TraceRing }
    public enum FxMotion : byte { None, Pulse, RushPast, RiseBlink, PopIn, Envelop, Burst, Ascend, Flicker, Flash }

    public struct Signature
    {
        public FxShape Shape;
        public FxMotion Motion;
        public FxTint Tint;      // the signature's colour in its main view (records keep their own per-viewer tint)
        public bool TintFixed;   // true: this tint replaces the record's tint (White noise: no Signal at the caster)
        public float Seconds;    // world time; 0 = the record's time
        public bool Played;      // false: no world effect exists
    }

    // What SkillFx draws for one record. The point is always SkillFeedback.VisualPoint (or, for FollowCamera, the own
    // camera): a plan never carries a position of its own.
    public struct FxPlan
    {
        public FxShape Shape;
        public FxTint Tint;
        public float Diameter, Seconds;
        public bool Still, Inward, FollowCamera, Glyph;
        public float StartDiameter; // R1: TraceRing grows from here
        public bool KillerSkill;
        public byte SkillId;
        public bool Shown => Shape != FxShape.None;
    }

    public static class SkillSignature
    {
        public const float GlyphSeconds = 1f, GlyphHeight = 1.9f, GlyphRise = .15f, GlyphSize = .6f;

        private static readonly Dictionary<int, Signature> Table = new Dictionary<int, Signature>
        {
            // Killer: expanding pulse bands (0.5-0.85 m high, attempt 2 F1), the radar ping (its icon's three arcs). `text` in the Killer view.
            [SkillGlyph.Key(true, SkillIds.PingScanner)] = new Signature { Shape = FxShape.PulseRings, Motion = FxMotion.Pulse, Tint = FxTint.Text, Seconds = 1f, Played = true },
            // Speed streaks rushing past the first-person camera for the whole effect.
            [SkillGlyph.Key(true, SkillIds.Overclock)] = new Signature { Shape = FxShape.Streaks, Motion = FxMotion.RushPast, Tint = FxTint.Text, Played = true },
            // R1 (11/17) Packet trace: one flat ground ring that flashes out to the 12 m trace radius in 0.35 s (a short local
            // burst, not Ping's three staggered bands); on a traced Survivor it snaps in around the feet.
            [SkillGlyph.Key(true, SkillIds.PacketTrace)] = new Signature { Shape = FxShape.TraceRing, Motion = FxMotion.Flash, Tint = FxTint.Text, Seconds = SkillFeedback.TraceSeconds, Played = true },
            // A hex-grid barrier that rises around the locked switch and blinks once before it drops.
            [SkillGlyph.Key(true, SkillIds.FirewallBlock)] = new Signature { Shape = FxShape.HexWall, Motion = FxMotion.RiseBlink, Tint = FxTint.Danger, Seconds = 1f, Played = true },
            // Survivor: a dashed wireframe double of the caster that pops in (overshoot) where the fake Signal starts.
            [SkillGlyph.Key(false, SkillIds.DummyPacket)] = new Signature { Shape = FxShape.WireDouble, Motion = FxMotion.PopIn, Tint = FxTint.Signal, Seconds = 1.2f, Played = true },
            // A wire dome over the 5 m relay area that closes over the covered Survivors and turns slowly.
            [SkillGlyph.Key(false, SkillIds.StealthRelay)] = new Signature { Shape = FxShape.Dome, Motion = FxMotion.Envelop, Tint = FxTint.Ally, Played = true },
            // Jagged data spikes bursting out of the caster and jittering.
            [SkillGlyph.Key(false, SkillIds.TrafficOverload)] = new Signature { Shape = FxShape.Spikes, Motion = FxMotion.Burst, Tint = FxTint.Signal, Seconds = .8f, Played = true },
            // An upward beam with rings climbing it, on the restored Survivor.
            [SkillGlyph.Key(false, SkillIds.EmergencyRestore)] = new Signature { Shape = FxShape.Beam, Motion = FxMotion.Ascend, Tint = FxTint.Ally, Seconds = 1f, Played = true },
            // Scattered static squares blinking at random. `text`: the fake Signals are elsewhere, nothing transmits here.
            [SkillGlyph.Key(false, SkillIds.WhiteNoise)] = new Signature { Shape = FxShape.Static, Motion = FxMotion.Flicker, Tint = FxTint.Text, TintFixed = true, Seconds = 1f, Played = true },
        };

        // Every skill (Killer then Survivor), for tests and warm-up.
        public static IEnumerable<(bool killerSkill, byte skillId)> All
        {
            get
            {
                for (byte i = 0; i < 4; i++) yield return (true, i);
                for (byte i = 0; i < 5; i++) yield return (false, i);
            }
        }

        public static Signature For(bool killerSkill, byte skillId) => Table.TryGetValue(SkillGlyph.Key(killerSkill, skillId), out Signature s) ? s : default;

        // The own skill was just used: the caster's record of its own effect (not the Overclock Recovering cue, not the
        // Stealth relay count line that follows a snapshot later; both carry no start cue).
        public static bool IsOwnCast(in FxRecord r)
            => r.Viewer == FxViewer.Caster && r.Sound.Shown && !r.Sound.Positional && r.Sound.Clip != FxClip.OverclockRecover;

        public static FxPlan Plan(in FxRecord r, bool reduced)
        {
            if (r.Viewer == FxViewer.None) return default;
            Signature s = For(r.KillerSkill, r.SkillId);
            if (!s.Played) return default;
            var plan = new FxPlan { Shape = s.Shape, KillerSkill = r.KillerSkill, SkillId = r.SkillId, Still = reduced };
            if (r.KillerSkill && r.SkillId == SkillIds.Overclock)
            {
                // 14.2.2: the Killer's own Overclock start only (the record's line lasts exactly the effect); first person, so
                // no glyph; reduced motion: none.
                if (!IsOwnCast(r) || reduced || r.Line.Seconds <= 0) return default;
                plan.Tint = FxTint.Text; plan.Seconds = r.Line.Seconds; plan.FollowCamera = true;
                return plan;
            }
            if (!r.Visual.Shown) return default; // no world effect for this viewer (14.2): no signature, no glyph
            bool killerOwnPoint = r.KillerSkill && r.Viewer == FxViewer.Caster && r.Visual.Anchor == FxAnchor.Own;
            if (r.Visual.Kind == FxKind.Sweep && reduced) return default; // 14.1: the Ping scanner sweep is off
            plan.Tint = s.TintFixed ? s.Tint : r.Visual.Tint;
            plan.Diameter = r.Visual.Diameter;
            plan.StartDiameter = r.Visual.StartDiameter;
            plan.Seconds = s.Seconds > 0 ? s.Seconds : r.Visual.Seconds;
            // The scanned Survivor sees the rings close in on itself; the Killer sees them sweep out.
            plan.Inward = r.SkillId == SkillIds.PingScanner && r.KillerSkill && r.Viewer == FxViewer.Affected;
            if (plan.Inward) plan.Diameter = 6;
            // First person has no body above the own point: the Killer's own-point effects get the HUD flash only.
            plan.Glyph = !killerOwnPoint;
            return plan;
        }
    }
}
