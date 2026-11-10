using System.Collections.Generic;
using PingTheKiller.Client.Killer;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    public enum ExitMark : byte { Locked, Open, LockedByKiller }
    public enum TagStyle : byte { Text, Muted, Lagging }
    public enum TagPivot : byte { Below, Above, Center }

    // Screen-space world markers drawn over the 3D view (HUD_SPEC 6.2): through-wall Signal diamonds, edge chevrons,
    // Terminal squares and the Exit ring, each with a 1 u bg keyline, plus their tags. Two channels keep the draw order:
    // channel 0 (Terminal and Exit) under channel 1 (Signal markers and edge indicators); the cluster is drawn after
    // this layer. Positions are panel units (origin top-left, y down). No glow, no animation. Layout runs TagLayout over
    // everything added this frame, so no two tags overlap and no lower-priority mark covers a Survivor marker.
    public sealed class MarkerLayer : VisualElement
    {
        public const int Static = 0, Signals = 1;
        private static readonly Color Bg = new Color32(16, 17, 19, 255);
        private static readonly Color Text = new Color32(232, 230, 225, 255);
        private static readonly Color Muted = new Color32(154, 152, 144, 255);
        private static readonly Color Signal = new Color32(255, 176, 32, 255);
        public const float TagHeight = 23, TagCharWidth = 7.4f, TagPadding = 8;
        private enum Kind : byte { Diamond, Chevron, Square, Exit, Body }
        private struct Shape { public Kind Kind; public Vector2 Position, Direction; public bool Ring, Hidden; public ExitMark Exit; public Rect Bounds; }
        private struct TagRecord { public int Shape; public LayoutPriority Priority; public Rect Rect; }
        private readonly List<TagRecord>[] records = { new List<TagRecord>(), new List<TagRecord>() };
        private readonly TagLayout layout = new TagLayout();
        private readonly List<int> itemShape = new List<int>(), itemTag = new List<int>(), itemChannel = new List<int>();
        private readonly List<LayoutItem> external = new List<LayoutItem>();
        private readonly List<Shape>[] shapes = { new List<Shape>(), new List<Shape>() };
        private readonly List<Label>[] tags = { new List<Label>(), new List<Label>() };
        private readonly int[] used = new int[2];
        private readonly Font font;
        public float Scale { get; set; } = 1;

        public MarkerLayer(Font font)
        {
            this.font = font;
            pickingMode = PickingMode.Ignore;
            AddToClassList("markers");
            generateVisualContent += Draw;
        }

        public void Clear(int channel)
        {
            shapes[channel].Clear();
            records[channel].Clear();
            used[channel] = 0;
            if (channel == Static) external.Clear();
        }

        // Q2: a layout item drawn by another layer (the rules v2 world layer), solved in the same pass as this layer's own
        // markers so no tag of either layer overlaps another. Cleared with the Static channel; read the result with
        // ExternalResult after Layout.
        public int External(LayoutPriority priority, Rect glyph, Rect tag, bool keepGlyph)
        {
            external.Add(new LayoutItem { Priority = priority, Glyph = glyph, Tag = tag, KeepGlyph = keepGlyph });
            return external.Count - 1;
        }

        public LayoutItem ExternalResult(int index) => external[index];

        // Hides the unused pooled tags of a channel and repaints. Call after a channel's markers are added.
        public void Commit(int channel)
        {
            List<Label> pool = tags[channel];
            for (int i = used[channel]; i < pool.Count; i++) if (pool[i].style.display != DisplayStyle.None) pool[i].style.display = DisplayStyle.None;
            MarkDirtyRepaint();
        }

        // Glyph bounds include the 1 u bg keyline (and the lagging ring).
        public void Diamond(int channel, Vector2 position, bool laggingRing)
            => shapes[channel].Add(new Shape { Kind = Kind.Diamond, Position = position, Ring = laggingRing, Bounds = TagLayout.Around(position, laggingRing ? 14 : 10, laggingRing ? 14 : 10) });
        public void Chevron(int channel, Vector2 position, Vector2 pointing, bool laggingRing)
            => shapes[channel].Add(new Shape { Kind = Kind.Chevron, Position = position, Direction = pointing, Ring = laggingRing, Bounds = TagLayout.Around(position, ChevronHalf(laggingRing), ChevronHalf(laggingRing)) });
        // Half the chevron's bounds (keyline and lagging ring included), for the edge spread (Q2).
        public static float ChevronHalf(bool laggingRing) => laggingRing ? 14 : 10;
        public void Square(int channel, Vector2 position)
            => shapes[channel].Add(new Shape { Kind = Kind.Square, Position = position, Bounds = TagLayout.Around(position, 9, 9) });
        public void Exit(int channel, Vector2 position, ExitMark state)
            => shapes[channel].Add(new Shape { Kind = Kind.Exit, Position = position, Exit = state, Bounds = TagLayout.Around(position, state == ExitMark.LockedByKiller ? 13 : 10, 10) });
        // An in-view Survivor's screen rectangle: never drawn here, but no tag or lower-priority mark may cover it.
        public void Body(int channel, Rect bounds)
            => shapes[channel].Add(new Shape { Kind = Kind.Body, Bounds = bounds });

        // Tag size in panel units (estimated from the text, so the layout never depends on last frame's UI layout).
        public static Vector2 TagSize(string text, TagStyle style)
            => new Vector2(text.Length * TagCharWidth + TagPadding + (style == TagStyle.Lagging ? 2 : 0), TagHeight);

        // ownGlyph: the tag belongs to the glyph added just before it on this channel (it moves and hides with it).
        public void Tag(int channel, Vector2 anchor, string text, TagStyle style, TagPivot pivot, LayoutPriority priority, bool ownGlyph)
        {
            Vector2 size = TagSize(text, style);
            float top = pivot == TagPivot.Below ? anchor.y : pivot == TagPivot.Above ? anchor.y - size.y : anchor.y - size.y * .5f;
            records[channel].Add(new TagRecord
            {
                Shape = ownGlyph ? shapes[channel].Count - 1 : -1, Priority = priority,
                Rect = new Rect(anchor.x - size.x * .5f, top, size.x, size.y)
            });
            Tag(channel, anchor, text, style, pivot);
        }

        // Runs the layout over both channels: hides covered low-priority glyphs and their tags, stacks or hides colliding
        // tags. Call after every marker of the frame is added and before Commit.
        public void Layout(Rect bounds, Rect keepOut)
        {
            layout.Clear(); itemShape.Clear(); itemTag.Clear(); itemChannel.Clear();
            layout.Block(keepOut);
            for (int channel = 0; channel < shapes.Length; channel++)
            {
                int first = layout.Count;
                List<Shape> list = shapes[channel];
                for (int i = 0; i < list.Count; i++)
                {
                    layout.Add(Priority(list[i].Kind), list[i].Bounds, default);
                    itemShape.Add(i); itemTag.Add(-1); itemChannel.Add(channel);
                }
                List<TagRecord> tagList = records[channel];
                for (int t = 0; t < tagList.Count; t++)
                {
                    TagRecord record = tagList[t];
                    int owner = record.Shape >= 0 ? first + record.Shape : -1;
                    if (owner >= 0 && itemTag[owner] < 0)
                    {
                        layout.Set(owner, record.Rect);
                        itemTag[owner] = t;
                        continue;
                    }
                    // A second tag of the same glyph (the Lagging tag above a body) or a tag without glyph.
                    layout.Add(record.Priority, default, record.Rect);
                    itemShape.Add(-1); itemTag.Add(t); itemChannel.Add(channel);
                }
            }
            int firstExternal = layout.Count;
            for (int e = 0; e < external.Count; e++)
            {
                layout.Add(external[e].Priority, external[e].Glyph, external[e].Tag, external[e].KeepGlyph);
                itemShape.Add(-1); itemTag.Add(-1); itemChannel.Add(-1);
            }
            layout.Solve(bounds);
            for (int e = 0; e < external.Count; e++) external[e] = layout[firstExternal + e];
            for (int k = 0; k < firstExternal; k++)
            {
                LayoutItem item = layout[k];
                int channel = itemChannel[k];
                if (itemShape[k] >= 0 && item.HasGlyph && !item.GlyphVisible)
                {
                    Shape shape = shapes[channel][itemShape[k]]; shape.Hidden = true; shapes[channel][itemShape[k]] = shape;
                }
                if (itemTag[k] < 0) continue;
                Label label = tags[channel][itemTag[k]];
                if (!item.TagVisible) { if (label.style.display != DisplayStyle.None) label.style.display = DisplayStyle.None; continue; }
                if (item.TagShift != 0) label.style.top = label.style.top.value.value + item.TagShift;
            }
        }

        private static LayoutPriority Priority(Kind kind)
            => kind == Kind.Chevron ? LayoutPriority.Edge : kind == Kind.Exit ? LayoutPriority.Exit : kind == Kind.Square ? LayoutPriority.Terminal : LayoutPriority.Survivor;

        private void Tag(int channel, Vector2 anchor, string text, TagStyle style, TagPivot pivot)
        {
            List<Label> pool = tags[channel];
            if (used[channel] == pool.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("tag");
                label.AddToClassList("ks");
                label.style.unityFontDefinition = FontDefinition.FromFont(font);
                Add(label);
                pool.Add(label);
            }
            Label tag = pool[used[channel]++];
            if (tag.text != text) tag.text = text;
            tag.EnableInClassList("tag-muted", style == TagStyle.Muted);
            tag.EnableInClassList("tag-lagging", style == TagStyle.Lagging);
            tag.style.left = anchor.x;
            tag.style.top = anchor.y;
            float y = pivot == TagPivot.Below ? 0 : pivot == TagPivot.Above ? -100 : -50;
            tag.style.translate = new Translate(Length.Percent(-50), Length.Percent(y));
            if (tag.style.display != DisplayStyle.Flex) tag.style.display = DisplayStyle.Flex;
        }

        // A line of n u renders at max(1, round(n x scale)) physical px (HUD_SPEC 0.2); returned in panel units.
        public float Stroke(float units) => Mathf.Max(1, Mathf.Round(units * Scale)) / Scale;
        // Centre of the nearest physical pixel, so 1 px lines stay crisp.
        private Vector2 Snap(Vector2 p) => new Vector2((Mathf.Floor(p.x * Scale) + .5f) / Scale, (Mathf.Floor(p.y * Scale) + .5f) / Scale);

        private void Draw(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            painter.lineJoin = LineJoin.Miter;
            for (int channel = 0; channel < shapes.Length; channel++)
                foreach (Shape shape in shapes[channel])
                {
                    if (shape.Hidden) continue;
                    Vector2 p = Snap(shape.Position);
                    switch (shape.Kind)
                    {
                        case Kind.Diamond:
                            if (shape.Ring) Ring(painter, p, 12, Text, false);
                            FilledDiamond(painter, p, 8);
                            break;
                        case Kind.Chevron:
                            if (shape.Ring) Ring(painter, p, 12, Text, false);
                            FilledChevron(painter, p, shape.Direction);
                            break;
                        case Kind.Square:
                            StrokedSquare(painter, p, 7);
                            break;
                        case Kind.Exit:
                            DrawExit(painter, p, shape.Exit);
                            break;
                    }
                }
        }

        private void FilledDiamond(Painter2D painter, Vector2 p, float half)
        {
            float key = Stroke(1);
            Polygon(painter, Bg, p + new Vector2(0, -half - key * 1.42f), p + new Vector2(half + key * 1.42f, 0), p + new Vector2(0, half + key * 1.42f), p + new Vector2(-half - key * 1.42f, 0));
            Polygon(painter, Signal, p + new Vector2(0, -half), p + new Vector2(half, 0), p + new Vector2(0, half), p + new Vector2(-half, 0));
        }

        // A 16 u arrowhead pointing along the direction (y down).
        private void FilledChevron(Painter2D painter, Vector2 p, Vector2 direction)
        {
            Vector2 d = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.down, n = new Vector2(-d.y, d.x);
            Vector2 tip = p + d * 8, left = p - d * 7 + n * 8, notch = p - d * 2, right = p - d * 7 - n * 8;
            painter.strokeColor = Bg; painter.lineWidth = Stroke(1) * 2;
            painter.BeginPath(); painter.MoveTo(tip); painter.LineTo(left); painter.LineTo(notch); painter.LineTo(right); painter.ClosePath();
            painter.Stroke();
            Polygon(painter, Signal, tip, left, notch, right);
        }

        private void StrokedSquare(Painter2D painter, Vector2 p, float half)
        {
            float line = Stroke(1);
            for (int pass = 0; pass < 2; pass++)
            {
                painter.strokeColor = pass == 0 ? Bg : Text;
                painter.lineWidth = pass == 0 ? line * 3 : line;
                painter.BeginPath();
                painter.MoveTo(p + new Vector2(-half, -half)); painter.LineTo(p + new Vector2(half, -half));
                painter.LineTo(p + new Vector2(half, half)); painter.LineTo(p + new Vector2(-half, half));
                painter.ClosePath(); painter.Stroke();
            }
        }

        private void DrawExit(Painter2D painter, Vector2 p, ExitMark state)
        {
            Ring(painter, p, 8, state == ExitMark.Locked ? Muted : Text, state == ExitMark.Locked);
            if (state != ExitMark.LockedByKiller) return;
            float line = Stroke(1);
            for (int pass = 0; pass < 2; pass++)
            {
                painter.strokeColor = pass == 0 ? Bg : Text;
                painter.lineWidth = pass == 0 ? line * 3 : line;
                painter.BeginPath(); painter.MoveTo(p + new Vector2(-11, 0)); painter.LineTo(p + new Vector2(11, 0)); painter.Stroke();
            }
        }

        // 1 u ring with a 1 u bg keyline on both sides; dashed = 12 dashes.
        private void Ring(Painter2D painter, Vector2 p, float radius, Color color, bool dashed)
        {
            float line = Stroke(1);
            for (int pass = 0; pass < 2; pass++)
            {
                painter.strokeColor = pass == 0 ? Bg : color;
                painter.lineWidth = pass == 0 ? line * 3 : line;
                if (!dashed)
                {
                    painter.BeginPath(); painter.Arc(p, radius, Angle.Degrees(0), Angle.Degrees(360)); painter.Stroke();
                    continue;
                }
                for (int i = 0; i < 12; i++)
                {
                    painter.BeginPath();
                    painter.Arc(p, radius, Angle.Degrees(i * 30), Angle.Degrees(i * 30 + 16));
                    painter.Stroke();
                }
            }
        }

        private static void Polygon(Painter2D painter, Color color, params Vector2[] points)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
