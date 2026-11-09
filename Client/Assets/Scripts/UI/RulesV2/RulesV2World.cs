using System.Collections.Generic;
using PingTheKiller.Client.Killer;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    public enum V2TagPivot : byte { Above, Below, Centre }

    // HUD_SPEC 13.4 / 13.6 world layer: Killer exit markers, Regressing tags (both roles) and the Killer's Overloaded alert
    // (on-screen square or edge chevron). Positions are panel units, y down; projection and edge placement are the
    // caller's (HUD_SPEC 6.2 rule, Killer.EdgePlacement). Call Begin, add the frame's markers, then End.
    public sealed class V2MarkerLayer : VisualElement
    {
        public const float TagGap = 12, Inset = 24;
        private enum Kind : byte { Exit, Overloaded, Chevron }  // Chevron: the off-screen Overloaded square with its pointer
        private struct Shape { public Kind Kind; public Vector2 At, Direction; public ExitView Exit; }
        // Q2: half the Overloaded square's bounds (8 u + the 1 u keyline) and of the Exit marker (the progress arc at
        // radius 11, 2 u wide, with its keyline), for the Killer's shared layout pass.
        public const float OverloadedHalf = 9, ExitHalf = 13;
        // Q2: this frame's tag i: its shape (-1 = none), layout priority, placed rectangle (reserved size), whether it was
        // measured this frame (else it stays hidden) and whether the edge spread dropped it.
        private struct Slot { public int Shape; public LayoutPriority Priority; public Rect Rect; public bool Measured, Dropped; }
        private readonly List<Shape> shapes = new List<Shape>();
        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<Vector2> sizes = new List<Vector2>();  // last measured size per pooled label
        private readonly List<Label> tags = new List<Label>();
        private readonly Font font;
        private readonly TextMemo memo = new TextMemo(2 + 16 + 16);  // exit tags, Regressing tags, Overloaded alerts
        private int used;
        public float Scale { get; set; } = 1;

        public V2MarkerLayer(Font value)
        {
            font = value;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-markers");
            generateVisualContent += Draw;
        }

        public void Begin() { shapes.Clear(); slots.Clear(); used = 0; }

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
            long key = ExitCopy.Key(exit);
            if (!memo.Hit(exit.Id & 1, key, out string text)) text = memo.Put(exit.Id & 1, key, ExitCopy.Tag(exit));
            Tag(at - new Vector2(0, TagGap), text, ExitCopy.Phase(exit) == ExitPhase.NoPower ? "v2-tag-muted" : null, V2TagPivot.Above, shapes.Count - 1, LayoutPriority.Exit);
        }

        // Regressing tag above a terminal point; 2 u left bar `danger` (Survivor: Killer-caused) or `text` (Killer).
        public void Regressing(Vector2 at, in TerminalFlagsView terminal, Role viewer)
        {
            if (!terminal.Regressing) return;
            long key = terminal.Id | (long)HoldCopy.Percent(terminal.Progress01) << 8 | (long)viewer << 16;
            int slot = 2 + (terminal.Id & 15);
            if (!memo.Hit(slot, key, out string text)) text = memo.Put(slot, key, TerminalFlagCopy.Regressing(terminal, viewer));
            Tag(at - new Vector2(0, TagGap), text, viewer == Role.Survivor ? "v2-tag-bar-danger" : "v2-tag-bar-text", V2TagPivot.Above, -1, LayoutPriority.Terminal);
        }

        // Overloaded alert (Killer): a 16 u `signal` square at the terminal on screen; off screen the same square at the
        // edge-indicator position with an 8 u `signal` pointer along `pointing` (y down). A square, never a chevron or a
        // diamond, so it cannot be read as a Survivor. Tag below, kept inside the safe rectangle.
        // The alert tag `Terminal 3 overloaded · 20 m`, memoized per terminal (rebuilt only when the metres change).
        public string OverloadedTag(in TerminalFlagsView terminal, float metres)
        {
            int slot = 18 + (terminal.Id & 15);
            long key = terminal.Id | (long)Mathf.RoundToInt(Mathf.Max(0, metres)) << 8;
            if (!memo.Hit(slot, key, out string text)) text = memo.Put(slot, key, TerminalFlagCopy.Overloaded(terminal, metres));
            return text;
        }

        public void Overloaded(Vector2 at, bool onScreen, Vector2 pointing, string text)
        {
            shapes.Add(new Shape { Kind = onScreen ? Kind.Overloaded : Kind.Chevron, At = at, Direction = pointing });
            Tag(at + new Vector2(0, TagGap), text, null, V2TagPivot.Below, shapes.Count - 1, LayoutPriority.Overloaded);
        }

        // Q2 (Killer): an off-screen Overloaded alert whose place the caller decides later with PlaceEdge (after the edge
        // spread). Returns the tag index; TagSize gives the size to reserve for it.
        public int OverloadedEdge(Vector2 at, Vector2 pointing, string text)
        {
            Overloaded(at, false, pointing, text);
            return used - 1;
        }

        // The size reserved for tag i: its measured size, else the last size its label had, else an estimate from the text.
        public Vector2 TagSize(int index) => slots[index].Rect.size;

        // Moves the off-screen alert of tag i (from OverloadedEdge) to the spread glyph point and its tag to `rect`;
        // show = false when the spread dropped the tag.
        public void PlaceEdge(int index, Vector2 at, Rect rect, bool show)
        {
            Slot slot = slots[index];
            if (slot.Shape >= 0) { Shape shape = shapes[slot.Shape]; shape.At = at; shapes[slot.Shape] = shape; }
            slot.Dropped = !show;
            if (show) slot.Rect = WholeInside(SafeArea, rect.x, rect.y, slot.Rect.width, slot.Rect.height);
            slots[index] = slot;
            Label tag = tags[index];
            tag.style.left = slot.Rect.x; tag.style.top = slot.Rect.y;
            if (!show) tag.style.visibility = Visibility.Hidden;
        }

        // Q2: this frame's tags as layout items for the Killer's single layout pass (MarkerLayer.External). Exit glyphs are
        // never hidden (HUD_SPEC 6.2 "always shown"); the Overloaded square is above the Exit priority, so it never is.
        public int LayoutCount => used;

        public void LayoutInput(int index, out LayoutPriority priority, out Rect glyph, out Rect tag, out bool keepGlyph)
        {
            Slot slot = slots[index];
            priority = slot.Priority;
            glyph = slot.Shape >= 0 ? Bounds(shapes[slot.Shape]) : default;
            tag = slot.Dropped ? default : slot.Rect;
            keepGlyph = slot.Shape >= 0 && shapes[slot.Shape].Kind == Kind.Exit;
        }

        // Applies the layout result to tag i: hidden, or moved down by `shift` (an unmeasured tag stays hidden).
        public void ApplyLayout(int index, bool visible, float shift)
        {
            Slot slot = slots[index];
            if (!slot.Measured || slot.Dropped) return;
            Label tag = tags[index];
            tag.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (visible && shift != 0) tag.style.top = Mathf.Round(slot.Rect.y + shift);
        }

        private static Rect Bounds(Shape s)
        {
            if (s.Kind == Kind.Exit) return TagLayout.Around(s.At, ExitHalf, ExitHalf);
            Rect square = TagLayout.Around(s.At, OverloadedHalf, OverloadedHalf);
            if (s.Kind == Kind.Overloaded) return square;
            Vector2 d = s.Direction.sqrMagnitude > 1e-6f ? s.Direction.normalized : Vector2.down;
            Vector2 tip = s.At + d * (8 / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) + OverloadedHalf);
            return Rect.MinMaxRect(Mathf.Min(square.xMin, tip.x), Mathf.Min(square.yMin, tip.y), Mathf.Max(square.xMax, tip.x), Mathf.Max(square.yMax, tip.y));
        }

        private void Tag(Vector2 at, string text, string style, V2TagPivot pivot, int shape, LayoutPriority priority)
        {
            if (used == tags.Count)
            {
                Label created = V2Ui.Text(this, string.Empty, font, "v2-s", "v2-tag");
                tags.Add(created);
                sizes.Add(Vector2.zero);
            }
            int index = used;
            Label tag = tags[used++];
            V2Ui.Show(tag, true);
            bool changed = tag.text != text;
            V2Ui.SetText(tag, text);
            tag.EnableInClassList("v2-tag-muted", style == "v2-tag-muted");
            tag.EnableInClassList("v2-tag-bar-danger", style == "v2-tag-bar-danger");
            tag.EnableInClassList("v2-tag-bar-text", style == "v2-tag-bar-text");
            // Placed from its measured size (the previous layout) and kept inside the 24 u screen inset; a tag with no
            // measured size yet, or whose text just changed (a pooled label still has the old text's size), stays invisible
            // for that one frame.
            float w = tag.resolvedStyle.width, h = tag.resolvedStyle.height;
            bool measured = !changed && !float.IsNaN(w) && !float.IsNaN(h) && w > 0 && h > 0;
            tag.style.visibility = measured ? Visibility.Visible : Visibility.Hidden;
            // Q2: an unmeasured tag (hidden this frame) still reserves space in the Killer layout: its label's last
            // measured size, else an estimate. A text change (the metres ticking) therefore never shifts its neighbours.
            if (measured) sizes[index] = new Vector2(w, h);
            else if (!float.IsNaN(w) && !float.IsNaN(h) && w > 0 && h > 0 && sizes[index] == Vector2.zero) sizes[index] = new Vector2(w, h);
            Vector2 size = measured ? new Vector2(w, h) : sizes[index] != Vector2.zero ? sizes[index] : MarkerLayer.TagSize(text, TagStyle.Text);
            w = Mathf.Ceil(size.x); h = Mathf.Ceil(size.y);  // whole-unit reservation, so rounding never pushes it past the edge
            Rect area = SafeArea;
            float x = at.x - w * .5f;
            float y = pivot == V2TagPivot.Above ? at.y - h : pivot == V2TagPivot.Below ? at.y : at.y - h * .5f;
            Rect placed = WholeInside(area, x, y, w, h);
            tag.style.left = placed.x; tag.style.top = placed.y;
            slots.Add(new Slot { Shape = shape, Priority = priority, Rect = placed, Measured = measured });
        }

        // A whole-unit rect (w, h whole) whose rounded origin is clamped so the whole rect stays inside `area`
        // (Ceil toward the inside on the min side, Floor on the max side). An axis the area is too small for is left alone.
        private static Rect WholeInside(Rect area, float x, float y, float w, float h)
        {
            x = Mathf.Round(x); y = Mathf.Round(y);
            if (area.width > w) x = Mathf.Clamp(x, Mathf.Ceil(area.xMin), Mathf.Floor(area.xMax - w));
            if (area.height > h) y = Mathf.Clamp(y, Mathf.Ceil(area.yMin), Mathf.Floor(area.yMax - h));
            return new Rect(x, y, w, h);
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

    // HUD_SPEC 13.7: the result screen body. Left column at 48 u: headline (L 600), own outcome (M), the majority rule
    // (S `muted`), the reason (S `muted`), then one 560x32 u row per starting Survivor: name (M) left, `Escaped` (M
    // `text`) or `Eliminated` (M `muted`) right; the own row has a 4 u `text` bar inside its left edge and a 600 name.
    // Actions: the existing Back to lobby / Leave buttons (HUD_SPEC 7.5, 7.6) go into Actions.
    public sealed class ResultV2Panel : VisualElement
    {
        private readonly Label headline, outcome, rule, reason;
        private readonly VisualElement list;
        private readonly Font font;
        public VisualElement Actions { get; }
        // Notes above the actions (the lobby line, the dev preview note), S `muted`.
        public VisualElement Notes { get; }

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
            Notes = V2Ui.Box(column, "v2-result-notes");
            Actions = V2Ui.Box(column, "v2-result-actions");
            Actions.pickingMode = PickingMode.Position;
            pickingMode = PickingMode.Position;
        }

        public string Headline => headline.text;
        public string Outcome => outcome.text;
        public string Rule => rule.text;
        public string Reason => reason.text;
        public int RowCount => list.childCount;

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
