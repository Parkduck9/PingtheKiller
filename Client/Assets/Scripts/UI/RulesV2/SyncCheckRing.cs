using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    // HUD_SPEC 13.2 sync check state, pure. One press per check (C2S_SyncPress is at most one per CheckId); the needle
    // freezes where it was pressed; the server's result shows for ResultSeconds, then the ring hides until a new CheckId.
    // A check that stops arriving without a result (cancelled: the Survivor stopped hacking) hides at once.
    public sealed class SyncCheckState
    {
        private double resultAt = -1;
        private bool started;
        public SyncCheckView View { get; private set; }
        public bool Visible { get; private set; }
        public bool Pressed { get; private set; }
        public float PressFraction { get; private set; }
        public SyncResult? Result { get; private set; }
        // Raised once per check with its CheckId; the owner sends C2S_SyncPress.
        public event Action<uint> Press;

        public float Needle => Pressed ? PressFraction : SyncCheckMath.NeedleFraction(View.Elapsed, View.Duration);

        // present: a check is current on the wire (S2C_SyncCheck received and not yet resolved or cancelled).
        public void Update(in SyncCheckView view, bool present, double now)
        {
            if (present && (!started || view.CheckId != View.CheckId))
            {
                started = true; Pressed = false; PressFraction = 0; Result = null; resultAt = -1;
            }
            if (present)
            {
                View = view;
                if (view.Result.HasValue && resultAt < 0) { Result = view.Result; resultAt = now; }
            }
            bool flashing = resultAt >= 0 && now - resultAt < SyncCheckMath.ResultSeconds - 1e-4;
            Visible = resultAt >= 0 ? flashing : present;
        }

        public bool TryPress()
        {
            if (!Visible || Pressed || Result.HasValue) return false;
            if (View.Elapsed > View.Duration + SyncCheckMath.ToleranceSeconds) return false;
            Pressed = true;
            PressFraction = SyncCheckMath.NeedleFraction(View.Elapsed, View.Duration);
            Press?.Invoke(View.CheckId);
            return true;
        }
    }

    // HUD_SPEC 13.2: a 144 u ring on a `panel` disc. Track 10 u `line` at radius 56 (dark, so the zone reads at once);
    // zone 10 u `text` over the full band width; great part 22 u `text`, standing 6 u out of the band on both sides (it
    // differs from Good by shape, not by tone or glow); needle 3 u `text` on a 6 u `bg` keyline from radius 40 to 70,
    // clockwise from 12 o'clock. Centre: the key glyph (S `muted`) while waiting, the result word (M 600 `text`) after
    // the server's result.
    public sealed class SyncCheckRing : VisualElement
    {
        public const float Size = 144;
        private const float BandRadius = 56, BandWidth = 10, GreatWidth = 22, NeedleInner = 40, NeedleOuter = 70;
        private readonly Label centre;
        private readonly SyncCheckState state;
        private string glyph = "Space";
        public float Scale { get; set; } = 1;
        public SyncCheckState State => state;

        public SyncCheckRing(SyncCheckState value, Font font)
        {
            state = value;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-sync");
            centre = V2Ui.Text(this, glyph, font, "v2-sync-centre");
            generateVisualContent += Draw;
        }

        // The last-used device's glyph: "Space" or "A".
        public void SetGlyph(string value) { glyph = value; }

        public void Refresh()
        {
            style.display = state.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.Visible) return;
            bool result = state.Result.HasValue;
            string text = result ? SyncCheckMath.ResultWord(state.Result.Value) : glyph;
            if (centre.text != text) centre.text = text;
            centre.EnableInClassList("v2-sync-result", result);
            MarkDirtyRepaint();
        }

        private static float Degrees(float fraction) => -90f + 360f * fraction;

        private void Draw(MeshGenerationContext context)
        {
            if (!state.Visible) return;
            Painter2D p = context.painter2D;
            var c = new Vector2(Size * .5f, Size * .5f);
            SyncCheckView v = state.View;
            p.fillColor = Palette.Panel;
            p.BeginPath(); p.Arc(c, Size * .5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            float hair = Palette.Stroke(1, Scale);
            p.lineWidth = hair; p.strokeColor = Palette.Line;
            p.BeginPath(); p.Arc(c, Size * .5f - hair * .5f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.lineCap = LineCap.Butt;
            p.lineWidth = Palette.Stroke(BandWidth, Scale);
            p.strokeColor = Palette.Line;
            p.BeginPath(); p.Arc(c, BandRadius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            float zoneStart = Mathf.Clamp01(v.ZoneStart), zoneEnd = Mathf.Clamp01(v.ZoneStart + v.ZoneSize);
            float greatEnd = Mathf.Clamp01(v.ZoneStart + Mathf.Min(v.GreatSize, v.ZoneSize));
            if (zoneEnd > zoneStart)
            {
                p.strokeColor = Palette.Text;
                p.BeginPath(); p.Arc(c, BandRadius, Angle.Degrees(Degrees(zoneStart)), Angle.Degrees(Degrees(zoneEnd))); p.Stroke();
            }
            if (greatEnd > zoneStart)
            {
                p.strokeColor = Palette.Text; p.lineWidth = Palette.Stroke(GreatWidth, Scale);
                p.BeginPath(); p.Arc(c, BandRadius, Angle.Degrees(Degrees(zoneStart)), Angle.Degrees(Degrees(greatEnd))); p.Stroke();
            }
            float a = Degrees(state.Needle) * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            // `bg` keyline under the needle so it stays visible where it crosses the `text` zone.
            p.lineWidth = Palette.Stroke(6, Scale); p.strokeColor = Palette.Bg;
            p.BeginPath(); p.MoveTo(c + d * NeedleInner); p.LineTo(c + d * (NeedleOuter - hair)); p.Stroke();
            p.lineWidth = Palette.Stroke(3, Scale); p.strokeColor = Palette.Text;
            p.BeginPath(); p.MoveTo(c + d * NeedleInner); p.LineTo(c + d * (NeedleOuter - hair)); p.Stroke();
        }
    }

    internal static class V2Ui
    {
        public static Label Text(VisualElement parent, string value, Font font, params string[] classes)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            if (font != null) label.style.unityFontDefinition = FontDefinition.FromFont(font);
            label.AddToClassList("v2-text");
            foreach (string c in classes) label.AddToClassList(c);
            parent.Add(label);
            return label;
        }

        public static VisualElement Box(VisualElement parent, params string[] classes)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (string c in classes) box.AddToClassList(c);
            parent.Add(box);
            return box;
        }

        public static void Show(VisualElement element, bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) element.style.display = display;
        }

        public static void SetText(Label label, string value)
        {
            if (label.text != value) label.text = value;
        }

        public static void Fill(VisualElement fill, float fraction)
            => fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
    }
}
