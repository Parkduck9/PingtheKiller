using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 4.8 / 6.1d: the 64 u skill area. A 56 u ring (3 u track `line`) around a 32 u icon drawn with 1 u lines
    // (HUD_SPEC 8), and an outer 2 u arc at 64 u while the own effect is active. Ready = full ring `text`; Cooldown =
    // remaining fraction in `muted`; Disabled = track only and a `muted` icon; Passive = no ring. The Survivor block
    // (4.8) shows ceil(SkillCooldown) as M text in the centre while cooling down; the Killer block (6.1d) says it in its
    // state line instead.
    public sealed class SkillRing : VisualElement
    {
        public enum State : byte { Ready, Cooldown, Disabled, Passive }
        private static readonly Color Line = new Color32(44, 47, 51, 255);
        private static readonly Color Text = new Color32(232, 230, 225, 255);
        private static readonly Color Muted = new Color32(154, 152, 144, 255);
        private static readonly Color Signal = new Color32(255, 176, 32, 255);
        private readonly Label count;
        private State state;
        private byte skill;
        private float cooldown = -1, active = -1;
        private bool activeSignal;
        public float Scale { get; set; } = 1;
        // Survivor skill ids share the numbers 0..4 with the Killer's; this picks the icon table.
        public bool SurvivorSkills { get; set; }

        public SkillRing()
        {
            pickingMode = PickingMode.Ignore;
            style.width = 64; style.height = 64; style.flexShrink = 0;
            count = new Label { pickingMode = PickingMode.Ignore };
            count.AddToClassList("ring-count");
            count.style.display = DisplayStyle.None;
            Add(count);
            generateVisualContent += Draw;
        }

        // cooldownFraction: 0..1 of the ring scale (Cooldown only). activeFraction: remaining 0..1 of the own effect, or
        // negative when none. signalArc: the active arc is `signal` (a skill that makes its user Signal).
        public void Set(byte skillId, State value, float cooldownFraction, float activeFraction, bool signalArc)
        {
            cooldownFraction = Quantize(cooldownFraction); activeFraction = Quantize(activeFraction);
            if (skill == skillId && state == value && cooldown == cooldownFraction && active == activeFraction && activeSignal == signalArc) return;
            skill = skillId; state = value; cooldown = cooldownFraction; active = activeFraction; activeSignal = signalArc;
            MarkDirtyRepaint();
        }

        // The centre number (Survivor Cooldown). Null or empty: the icon is drawn instead.
        public void SetCount(string value)
        {
            bool visible = !string.IsNullOrEmpty(value);
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (count.style.display != display) { count.style.display = display; MarkDirtyRepaint(); }
            if (visible && count.text != value) count.text = value;
        }

        public string Count => count.style.display == DisplayStyle.None ? null : count.text;

        private static float Quantize(float value) => value < 0 ? -1 : Mathf.Round(Mathf.Clamp01(value) * 360) / 360;
        private float Stroke(float units) => Mathf.Max(1, Mathf.Round(units * Scale)) / Scale;

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            var c = new Vector2(32, 32);
            p.lineCap = LineCap.Butt;
            if (state != State.Passive)
            {
                float width = Stroke(3);
                p.lineWidth = width; p.strokeColor = Line;
                p.BeginPath(); p.Arc(c, 28 - width * .5f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                if (state == State.Ready)
                {
                    p.strokeColor = Text;
                    p.BeginPath(); p.Arc(c, 28 - width * .5f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                }
                else if (state == State.Cooldown && cooldown > 0)
                {
                    p.strokeColor = Muted;
                    p.BeginPath(); p.Arc(c, 28 - width * .5f, Angle.Degrees(-90), Angle.Degrees(-90 + 360 * cooldown)); p.Stroke();
                }
            }
            if (active > 0)
            {
                float width = Stroke(2);
                p.lineWidth = width; p.strokeColor = activeSignal ? Signal : Text;
                // Shrinks clockwise: the arc that is left ends at 12 o'clock.
                p.BeginPath(); p.Arc(c, 32 - width * .5f, Angle.Degrees(-90 - 360 * active), Angle.Degrees(-90)); p.Stroke();
            }
            if (count.style.display == DisplayStyle.None) Icon(p, new Vector2(16, 16), state == State.Disabled ? Muted : Text);
        }

        // HUD_SPEC 8 icons in a 32 u box (origin = its top-left), 1 u lines; U9: drawn from SkillGlyph, the same glyph the
        // world effect and the own-cast flash show.
        private void Icon(Painter2D p, Vector2 o, Color color)
            => SkillGlyph.Paint(p, SkillGlyph.For(!SurvivorSkills, skill), o, 32, color, Stroke(1));
    }
}
