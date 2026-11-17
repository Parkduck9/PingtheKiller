using System;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    // HUD_SPEC 13.2 sync check state, pure. One press per check (C2S_SyncPress is at most one per CheckId); the pulse
    // (Needle: the sweep fraction) freezes where it was pressed; the server's result shows for ResultSeconds, then the bar hides until a new CheckId.
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

        // Hides at once and forgets the check (a new match, the own status left Normal, the link went).
        public void Reset()
        {
            started = false; Pressed = false; PressFraction = 0; Result = null; resultAt = -1; Visible = false; View = default;
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

    // HUD_SPEC 13.2 (11/17, IP): the sync lock, a 320x80 u horizontal bar on a `panel` fill with a 1 u `line` edge.
    // Track: a waveform across x 16..304 u, y 8..44 u. A pulse (a bump in the wave with a 4 u `text` bar at its crest, on an
    // 8 u `bg` keyline) travels left to right; its x is exactly SyncCheckMath.NeedleFraction mapped onto the track, so the
    // timing is the one the old ring used. The lock window is drawn from ZoneStart to ZoneStart + ZoneSize: the Good part a
    // `muted` block the height of the track, the Great part (the first GreatSize of the window, as the judge reads it) a `text`
    // block 6 u taller on both sides, with a 2 u `panel` cut between them. Label row y 52..80 u: "Sync lock · Space" while
    // waiting, the result word (M 600 `text`) after the server result.
    public sealed class SyncLockBar : VisualElement
    {
        public const float Width = 320, Height = 80, LabelTop = 52;
        public const float TrackLeft = 16, TrackWidth = 288, TrackTop = 8, TrackHeight = 36, GreatExtra = 6, GreatGap = 2, PulseWidth = 4, PulseKeyline = 8, PulseReach = 4;
        public const float WaveAmplitude = 2, BumpAmplitude = 13, BumpWidth = 9;
        private readonly Label label;
        private readonly SyncCheckState state;
        private string glyph = "Space";
        public float Scale { get; set; } = 1;
        public SyncCheckState State => state;

        public SyncLockBar(SyncCheckState value, Font font)
        {
            state = value;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-lock");
            label = V2Ui.Text(this, WaitingText(glyph), font, "v2-lock-label");
            generateVisualContent += Draw;
        }

        // The last-used device glyph: "Space" or "A".
        public void SetGlyph(string value) { glyph = value; }

        public static string WaitingText(string glyph) => "Sync lock · " + glyph;

        // Track x (u, from the left edge of the bar) of a sweep fraction, and back. Fractions are clamped to 0..1.
        public static float PulseX(float fraction) => TrackLeft + Mathf.Clamp01(fraction) * TrackWidth;
        public static float FractionAtX(float x) => Mathf.Clamp01((x - TrackLeft) / TrackWidth);

        // x of the middle of the lock window: the pulse is here when the old needle stood in the middle of the zone.
        public static float WindowCentreX(in SyncCheckView view) => PulseX(view.ZoneStart + view.ZoneSize * .5f);

        // Waveform offset (u, positive = down) at track x, for a pulse at pulseX: a small steady wave plus a bump that
        // follows the pulse.
        public static float WaveOffset(float x, float pulseX)
        {
            float d = (x - pulseX) / BumpWidth;
            float amplitude = WaveAmplitude + BumpAmplitude * Mathf.Exp(-d * d);
            return amplitude * Mathf.Sin((x - TrackLeft) * .62f);
        }

        public void Refresh()
        {
            style.display = state.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.Visible) return;
            bool result = state.Result.HasValue;
            string text = result ? SyncCheckMath.ResultWord(state.Result.Value) : WaitingText(glyph);
            if (label.text != text) label.text = text;
            label.EnableInClassList("v2-lock-result", result);
            MarkDirtyRepaint();
        }

        private static void Block(Painter2D p, float x0, float x1, float y0, float y1, Color color)
        {
            if (x1 <= x0) return;
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1)); p.ClosePath(); p.Fill();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!state.Visible) return;
            Painter2D p = context.painter2D;
            SyncCheckView v = state.View;
            float hair = Palette.Stroke(1, Scale);
            Block(p, 0, Width, 0, Height, Palette.Panel);
            p.lineWidth = hair; p.strokeColor = Palette.Line; p.lineCap = LineCap.Butt;
            p.BeginPath(); p.MoveTo(new Vector2(hair * .5f, hair * .5f)); p.LineTo(new Vector2(Width - hair * .5f, hair * .5f));
            p.LineTo(new Vector2(Width - hair * .5f, Height - hair * .5f)); p.LineTo(new Vector2(hair * .5f, Height - hair * .5f)); p.ClosePath(); p.Stroke();
            float mid = TrackTop + TrackHeight * .5f, top = TrackTop, bottom = TrackTop + TrackHeight;
            // The lock window, behind the wave.
            float zoneStart = Mathf.Clamp01(v.ZoneStart), zoneEnd = Mathf.Clamp01(v.ZoneStart + v.ZoneSize);
            float greatEnd = Mathf.Clamp01(v.ZoneStart + Mathf.Min(v.GreatSize, v.ZoneSize));
            float gap = Palette.Stroke(GreatGap, Scale);
            if (zoneEnd > zoneStart) Block(p, PulseX(zoneStart), PulseX(zoneEnd), top, bottom, Palette.Muted);
            if (greatEnd > zoneStart)
            {
                Block(p, PulseX(zoneStart), PulseX(greatEnd), top - GreatExtra, bottom + GreatExtra, Palette.Text);
                if (zoneEnd > greatEnd) Block(p, PulseX(greatEnd), PulseX(greatEnd) + gap, top - GreatExtra, bottom + GreatExtra, Palette.Panel);
            }
            // The waveform, steady in `line`, and the bump around the pulse in `text`.
            float px = PulseX(state.Needle);
            const int steps = 144;
            p.lineJoin = LineJoin.Round;
            p.lineWidth = Palette.Stroke(2, Scale); p.strokeColor = Palette.Line;
            p.BeginPath();
            for (int i = 0; i <= steps; i++)
            {
                float x = TrackLeft + TrackWidth * i / steps;
                var pt = new Vector2(x, mid + WaveOffset(x, px));
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
            p.lineWidth = Palette.Stroke(3, Scale); p.strokeColor = Palette.Text;
            p.BeginPath();
            bool started = false;
            for (int i = 0; i <= steps; i++)
            {
                float x = TrackLeft + TrackWidth * i / steps;
                if (Mathf.Abs(x - px) > BumpWidth * 1.6f) { started = false; continue; }
                var pt = new Vector2(x, mid + WaveOffset(x, px));
                if (!started) { p.MoveTo(pt); started = true; } else p.LineTo(pt);
            }
            p.Stroke();
            // The crest: a short bar through the wave, on a keyline so it stays visible over the `text` Great block.
            float reach = TrackHeight * .5f + PulseReach;
            p.lineWidth = Palette.Stroke(PulseKeyline, Scale); p.strokeColor = Palette.Bg;
            p.BeginPath(); p.MoveTo(new Vector2(px, mid - reach)); p.LineTo(new Vector2(px, mid + reach)); p.Stroke();
            p.lineWidth = Palette.Stroke(PulseWidth, Scale); p.strokeColor = Palette.Text;
            p.BeginPath(); p.MoveTo(new Vector2(px, mid - reach)); p.LineTo(new Vector2(px, mid + reach)); p.Stroke();
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
