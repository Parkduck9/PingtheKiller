using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // U9 own-cast timing (pure): the flash shows for Seconds from the own effect, then is removed in one frame. With motion
    // on it rises RiseUnits u into place over RiseSeconds (ease-out, SLOP 5: <= 150 ms); with reduced motion it is still.
    public sealed class CastFlashTimer
    {
        public const double Seconds = .6;
        public const float RiseSeconds = .1f, RiseUnits = 4;
        private double start = double.NegativeInfinity;
        public bool Reduced { get; private set; }
        public bool KillerSkill { get; private set; }
        public byte SkillId { get; private set; }

        public void Show(bool killerSkill, byte skillId, bool reduced, double now)
        {
            KillerSkill = killerSkill; SkillId = skillId; Reduced = reduced; start = now;
        }

        public void Reset() => start = double.NegativeInfinity;
        public bool Visible(double now) => now >= start && now < start + Seconds;

        // Vertical offset in u (positive = below its rest place); 0 at rest and always 0 with reduced motion.
        public float Offset(double now)
        {
            if (Reduced || !Visible(now)) return 0;
            float t = Mathf.Clamp01((float)((now - start) / RiseSeconds));
            float eased = 1 - (1 - t) * (1 - t);
            return RiseUnits * (1 - eased);
        }
    }

    // U9 own-cast flash (HUD_SPEC 14.1b): the skill's glyph for 0.6 s beside the own skill block, outside the
    // bottom-centre stack, so it never covers the character's feet (4.10). `panel` fill, 1 u `line` border, padding 8,
    // a 32 u glyph in `text` (48 x 48 u; attempt 2, F7: no name, the skill block already names it). It sits left of the Survivor cluster (8 u gap) and right
    // of the Killer's bottom-left cluster (8 u gap); bottom-aligned with the skill block.
    public sealed class SkillCastFlash : VisualElement
    {
        private static readonly Color Panel = new Color32(26, 28, 31, 255), Line = new Color32(44, 47, 51, 255), Ink = new Color32(232, 230, 225, 255);
        private readonly SkillGlyphElement glyph;
        private float scale = 1, shownOffset = -1;
        private bool shown;
        public CastFlashTimer Timer { get; } = new CastFlashTimer();

        // leftOfAnchor: the Survivor block (flash to the left of the anchor); else to the right (Killer).
        public SkillCastFlash(bool leftOfAnchor, float gap)
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.bottom = 0;
            if (leftOfAnchor) { style.right = Length.Percent(100); style.marginRight = gap; }
            else { style.left = Length.Percent(100); style.marginLeft = gap; }
            style.flexDirection = FlexDirection.Row; style.alignItems = Align.Center;
            style.height = 48; style.paddingLeft = 8; style.paddingRight = 8; style.paddingTop = 8; style.paddingBottom = 8;
            style.backgroundColor = Panel;
            style.borderTopColor = Line; style.borderBottomColor = Line; style.borderLeftColor = Line; style.borderRightColor = Line;
            SetBorder(1);
            glyph = new SkillGlyphElement();
            glyph.style.width = 32; glyph.style.height = 32; glyph.style.flexShrink = 0;
            Add(glyph);
            style.display = DisplayStyle.None;
            schedule.Execute(() => Tick(Time.unscaledTimeAsDouble)).Every(16);
        }

        // The skill whose glyph shows, or null (attempt 2, F7: glyph only; the skill block already names the skill).
        public string ShownSkill => shown ? (Timer.KillerSkill ? "K" : "S") + Timer.SkillId : null;

        public void Show(bool killerSkill, byte skillId, bool reduced, double now)
        {
            Timer.Show(killerSkill, skillId, reduced, now);
            glyph.Set(killerSkill, skillId, Ink);
            Tick(now);
        }

        public void Hide() { Timer.Reset(); Tick(double.PositiveInfinity); }

        public void SetScale(float value)
        {
            scale = value; glyph.Scale = value; SetBorder(1);
            glyph.MarkDirtyRepaint();
        }

        private void SetBorder(float units)
        {
            float border = Mathf.Max(1, Mathf.Round(units * scale)) / scale; // HUD_SPEC 0.2
            style.borderTopWidth = border; style.borderBottomWidth = border; style.borderLeftWidth = border; style.borderRightWidth = border;
        }

        public void Tick(double now)
        {
            bool visible = Timer.Visible(now);
            if (visible != shown)
            {
                shown = visible;
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            float offset = visible ? Timer.Offset(now) : 0;
            if (offset != shownOffset) { shownOffset = offset; style.translate = new Translate(0, offset); }
        }
    }
}
