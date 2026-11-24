using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    public enum V2TagPivot : byte { Above, Below, Centre }

    // HUD_SPEC 12.4 / 12.6 world layer: Killer exit markers, Regressing tags (both roles) and the Killer's Overloaded alert
    // (on-screen square or edge chevron). Positions are panel units, y down; projection and edge placement are the
    // caller's (HUD_SPEC 6.2 rule, Killer.EdgePlacement). Call Begin, add the frame's markers, then End.
    public sealed class V2MarkerLayer : VisualElement
    {
        public const float TagGap = 12, Inset = 24;
        private enum Kind : byte { Exit, Overloaded, Chevron }  // Chevron: the off-screen Overloaded square with its pointer
        private struct Shape { public Kind Kind; public Vector2 At, Direction; public ExitView Exit; }
        private readonly List<Shape> shapes = new List<Shape>();
        private readonly List<Label> tags = new List<Label>();
        private readonly Font font;
        private int used;
        public float Scale { get; set; } = 1;

        public V2MarkerLayer(Font value)
        {
            font = value;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-markers");
            generateVisualContent += Draw;
        }

        public void Begin() { shapes.Clear(); used = 0; }

        public void End()
        {
            for (int i = used; i < tags.Count; i++) V2Ui.Show(tags[i], false);
            MarkDirtyRepaint();
        }

        public IReadOnlyList<string> Tags
        {
            get { var list = new List<string>(); for (int i = 0; i < used; i++) list.Add(tags[i].text); return list; }
        }

        // Killer exit marker at 2.5 m (HUD_SPEC 6.2), tag above: `Exit 1 · 62%`.
        public void Exit(Vector2 at, in ExitView exit)
        {
            shapes.Add(new Shape { Kind = Kind.Exit, At = at, Exit = exit });
            Tag(at - new Vector2(0, TagGap), ExitCopy.Tag(exit), ExitCopy.Phase(exit) == ExitPhase.NoPower ? "v2-tag-muted" : null, V2TagPivot.Above);
        }

        // Regressing tag above a terminal point; 2 u left bar `danger` (Survivor: Killer-caused) or `text` (Killer).
        public void Regressing(Vector2 at, in TerminalFlagsView terminal, Role viewer)
        {
            if (!terminal.Regressing) return;
            Tag(at - new Vector2(0, TagGap), TerminalFlagCopy.Regressing(terminal, viewer), viewer == Role.Survivor ? "v2-tag-bar-danger" : "v2-tag-bar-text", V2TagPivot.Above);
        }

        // Overloaded alert (Killer): a 16 u `signal` square at the terminal on screen; off screen the same square at the
        // edge-indicator position with an 8 u `signal` pointer along `pointing` (y down). A square, never a chevron or a
        // diamond, so it cannot be read as a Survivor. Tag below, kept inside the safe rectangle.
        public void Overloaded(Vector2 at, bool onScreen, Vector2 pointing, string text)
        {
            shapes.Add(new Shape { Kind = onScreen ? Kind.Overloaded : Kind.Chevron, At = at, Direction = pointing });
            Tag(at + new Vector2(0, TagGap), text, null, V2TagPivot.Below);
        }

        private void Tag(Vector2 at, string text, string style, V2TagPivot pivot)
        {
            if (used == tags.Count)
            {
                Label created = V2Ui.Text(this, string.Empty, font, "v2-s", "v2-tag");
                tags.Add(created);
            }
            Label tag = tags[used++];
            V2Ui.Show(tag, true);
            V2Ui.SetText(tag, text);
            tag.EnableInClassList("v2-tag-muted", style == "v2-tag-muted");
            tag.EnableInClassList("v2-tag-bar-danger", style == "v2-tag-bar-danger");
            tag.EnableInClassList("v2-tag-bar-text", style == "v2-tag-bar-text");
            // Placed from its measured size (the previous layout) and kept inside the 24 u screen inset; a tag with no
            // measured size yet stays invisible for that one frame.
            float w = tag.resolvedStyle.width, h = tag.resolvedStyle.height;
            bool measured = !float.IsNaN(w) && !float.IsNaN(h) && w > 0 && h > 0;
            tag.style.visibility = measured ? Visibility.Visible : Visibility.Hidden;
            if (!measured) { w = 0; h = 0; }
            Rect area = SafeArea;
            float x = at.x - w * .5f;
            float y = pivot == V2TagPivot.Above ? at.y - h : pivot == V2TagPivot.Below ? at.y : at.y - h * .5f;
            if (area.width > w) x = Mathf.Clamp(x, area.xMin, area.xMax - w);
            if (area.height > h) y = Mathf.Clamp(y, area.yMin, area.yMax - h);
            tag.style.left = Mathf.Round(x); tag.style.top = Mathf.Round(y);
        }

        private Rect SafeArea
        {
            get
            {
                float w = layout.width, h = layout.height;
                if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return Rect.MinMaxRect(0, 0, float.MaxValue, float.MaxValue);
                return Rect.MinMaxRect(Inset, Inset, Mathf.Max(Inset, w - Inset), Mathf.Max(Inset, h - Inset));
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            float hair = Palette.Stroke(1, Scale), key = Palette.Stroke(3, Scale);
            p.lineCap = LineCap.Butt; p.lineJoin = LineJoin.Miter;
            foreach (Shape s in shapes)
            {
                switch (s.Kind)
                {
                    case Kind.Exit: DrawExit(p, s, hair, key, Palette.Stroke(2, Scale)); break;
                    case Kind.Overloaded:
                        p.fillColor = Palette.Bg;
                        Square(p, s.At, 8 + hair); p.Fill();
                        p.fillColor = Palette.Signal;
                        Square(p, s.At, 8); p.Fill();
                        break;
                    case Kind.Chevron:
                        Vector2 d = s.Direction.sqrMagnitude > 1e-6f ? s.Direction.normalized : Vector2.down;
                        var n = new Vector2(-d.y, d.x);
                        // The pointer: a triangle from the square's edge, 8 u long, 8 u wide at its base.
                        float reach = 8 / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
                        Vector2 tip = s.At + d * (reach + 8), left = s.At + d * reach + n * 4, right = s.At + d * reach - n * 4;
                        p.fillColor = Palette.Bg;
                        Square(p, s.At, 8 + hair); p.Fill();
                        Pointer(p, tip + d * hair, left + n * hair, right - n * hair); p.Fill();
                        p.fillColor = Palette.Signal;
                        Square(p, s.At, 8); p.Fill();
                        Pointer(p, tip, left, right); p.Fill();
                        break;
                }
            }
        }

        private static void Square(Painter2D p, Vector2 c, float half)
        {
            p.BeginPath();
            p.MoveTo(c + new Vector2(-half, -half)); p.LineTo(c + new Vector2(half, -half));
            p.LineTo(c + new Vector2(half, half)); p.LineTo(c + new Vector2(-half, half)); p.ClosePath();
        }

        private static void Pointer(Painter2D p, Vector2 tip, Vector2 left, Vector2 right)
        {
            p.BeginPath(); p.MoveTo(tip); p.LineTo(left); p.LineTo(right); p.ClosePath();
        }

        // 16 u ring (radius 8). No power: dashed `muted`. Powered: solid `text`. Switching: solid `text` + a 2 u progress
        // arc at radius 11. Locked: solid ring + a bar across + the progress arc. Open: a filled `text` disc. Each shape has
        // a `bg` keyline.
        private static void DrawExit(Painter2D p, Shape s, float hair, float key, float width)
        {
            ExitPhase phase = ExitCopy.Phase(s.Exit);
            Vector2 c = s.At;
            if (phase == ExitPhase.Open)
            {
                p.fillColor = Palette.Bg; p.BeginPath(); p.Arc(c, 8 + hair, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                p.fillColor = Palette.Text; p.BeginPath(); p.Arc(c, 8, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                return;
            }
            p.lineWidth = key; p.strokeColor = Palette.Bg;
            p.BeginPath(); p.Arc(c, 8, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.lineWidth = hair;
            if (phase == ExitPhase.NoPower)
            {
                p.strokeColor = Palette.Muted;
                for (int i = 0; i < 12; i++) { p.BeginPath(); p.Arc(c, 8, Angle.Degrees(i * 30), Angle.Degrees(i * 30 + 15)); p.Stroke(); }
                return;
            }
            p.strokeColor = Palette.Text;
            p.BeginPath(); p.Arc(c, 8, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (phase == ExitPhase.Locked)
            {
                p.BeginPath(); p.MoveTo(c + new Vector2(-11, 0)); p.LineTo(c + new Vector2(11, 0)); p.Stroke();
            }
            float progress = HoldCopy.Fill(s.Exit.SwitchProgress01);
            if (progress > 0)
            {
                p.lineWidth = width + 2 * hair; p.strokeColor = Palette.Bg;
                p.BeginPath(); p.Arc(c, 11, Angle.Degrees(-90), Angle.Degrees(-90 + 360 * progress)); p.Stroke();
                p.lineWidth = width; p.strokeColor = Palette.Text;
                p.BeginPath(); p.Arc(c, 11, Angle.Degrees(-90), Angle.Degrees(-90 + 360 * progress)); p.Stroke();
            }
        }
    }

    // HUD_SPEC 12.7: the result screen body. Left column at 48 u: headline (L 600), own outcome (M), the majority rule
    // (S `muted`), the reason (S `muted`), then one 560x32 u row per starting Survivor: name (M) left, `Escaped` (M
    // `text`) or `Eliminated` (M `muted`) right; the own row has a 4 u `text` bar inside its left edge and a 600 name.
    // Actions: the existing Back to lobby / Leave buttons (HUD_SPEC 7.5, 7.6) go into Actions.
    public sealed class ResultV2Panel : VisualElement
    {
        private readonly Label headline, outcome, rule, reason;
        private readonly VisualElement list;
        private readonly Font font;
        public VisualElement Actions { get; }

        public ResultV2Panel(Font value)
        {
            font = value;
            AddToClassList("v2-result");
            VisualElement column = V2Ui.Box(this, "v2-result-column");
            headline = V2Ui.Text(column, string.Empty, font, "v2-l", "v2-bold", "v2-result-headline");
            outcome = V2Ui.Text(column, string.Empty, font, "v2-m", "v2-result-outcome");
            rule = V2Ui.Text(column, string.Empty, font, "v2-s", "v2-muted", "v2-result-rule");
            reason = V2Ui.Text(column, string.Empty, font, "v2-s", "v2-muted", "v2-result-reason");
            list = V2Ui.Box(column, "v2-result-list");
            Actions = V2Ui.Box(column, "v2-result-actions");
            Actions.pickingMode = PickingMode.Position;
            pickingMode = PickingMode.Position;
        }

        public string Headline => headline.text;
        public string Rule => rule.text;

        public void Set(ResultV2View view)
        {
            V2Ui.SetText(headline, ResultV2Copy.Headline(view.Winner));
            string own = ResultV2Copy.Outcome(view);
            V2Ui.SetText(outcome, own);
            V2Ui.Show(outcome, own.Length > 0);
            V2Ui.SetText(rule, ResultV2Copy.Rule(view.Escaped, view.Starting));
            V2Ui.SetText(reason, ResultV2Copy.Reason(view.Reason));
            list.Clear();
            for (int i = 0; i < view.Rows.Count; i++)
            {
                ResultRow row = view.Rows[i];
                VisualElement line = V2Ui.Box(list, "v2-result-row");
                line.EnableInClassList("v2-self", row.Self);
                Label name = V2Ui.Text(line, ResultV2Copy.RowName(row, i), font, "v2-m", "v2-result-name");
                name.EnableInClassList("v2-bold", row.Self);
                Label state = V2Ui.Text(line, ResultV2Copy.RowState(row), font, "v2-m", "v2-right");
                state.EnableInClassList("v2-muted", !row.Escaped);
            }
        }
    }
}
