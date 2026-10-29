using System;
using System.Collections.Generic;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // Marker priority for the screen-space layout, highest first (U4c fix list F-01/F-02/F-04).
    public enum LayoutPriority : byte { Survivor = 0, Edge = 1, Exit = 2, Terminal = 3 }

    // One marker for the layout: an optional glyph rectangle and an optional tag rectangle at its preferred place
    // (panel units, y down). Solve fills the results.
    public struct LayoutItem
    {
        public LayoutPriority Priority;
        public Rect Glyph;          // width 0 = no glyph
        public Rect Tag;            // width 0 = no tag
        public bool GlyphVisible;   // result
        public bool TagVisible;     // result
        public float TagShift;      // result: vertical offset added to the tag (and its anchor)
        public bool HasGlyph => Glyph.width > 0;
        public bool HasTag => Tag.width > 0;
    }

    // Pure screen-space layout for the Killer world markers, run every frame after projection.
    // 1. Glyphs: Survivor markers (in-view bodies, diamonds, edge chevrons) are never hidden. An Exit or Terminal glyph that
    //    would cover a higher-priority glyph is hidden together with its tag.
    // 2. Tags, highest priority first: a tag may not overlap another placed tag, a visible glyph of the same or higher
    //    priority (its own included), a blocked rectangle (the HUD cluster) or leave the bounds. A colliding tag is stacked by
    //    tag height + 2 u: below, above, below again (3 steps); then it is hidden.
    // The result depends only on the set of items: they are sorted by priority and geometry, never by input order.
    public sealed class TagLayout
    {
        public const float StepGap = 2;
        public const int MaxSteps = 3;
        private static readonly float[] Steps = { 0, 1, -1, 2 };
        private readonly List<LayoutItem> items = new List<LayoutItem>();
        private readonly List<int> order = new List<int>();
        private readonly List<Rect> tags = new List<Rect>();
        private readonly List<Rect> blocked = new List<Rect>();
        private readonly Comparison<int> compare;

        public TagLayout() { compare = Compare; }

        public int Count => items.Count;
        public LayoutItem this[int index] => items[index];

        public void Clear() { items.Clear(); blocked.Clear(); }

        public int Add(LayoutPriority priority, Rect glyph, Rect tag)
        {
            items.Add(new LayoutItem { Priority = priority, Glyph = glyph, Tag = tag });
            return items.Count - 1;
        }

        // Sets the tag of an item added earlier.
        public void Set(int index, Rect tag) { LayoutItem item = items[index]; item.Tag = tag; items[index] = item; }

        // A rectangle no tag may enter (the bottom-left cluster).
        public void Block(Rect rect) { if (rect.width > 0 && rect.height > 0) blocked.Add(rect); }

        // A rectangle centred on a point.
        public static Rect Around(Vector2 centre, float halfWidth, float halfHeight)
            => new Rect(centre.x - halfWidth, centre.y - halfHeight, halfWidth * 2, halfHeight * 2);

        public void Solve(Rect bounds)
        {
            order.Clear();
            for (int i = 0; i < items.Count; i++) order.Add(i);
            order.Sort(compare);

            // 1. Glyphs.
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                LayoutItem item = items[i];
                item.GlyphVisible = item.HasGlyph;
                if (item.HasGlyph && item.Priority >= LayoutPriority.Exit)
                    for (int m = 0; m < k; m++)
                    {
                        LayoutItem other = items[order[m]];
                        if (other.GlyphVisible && other.Priority < item.Priority && other.Glyph.Overlaps(item.Glyph)) { item.GlyphVisible = false; break; }
                    }
                item.TagVisible = false; item.TagShift = 0;
                items[i] = item;
            }

            // 2. Tags.
            tags.Clear();
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                LayoutItem item = items[i];
                if (!item.HasTag || (item.HasGlyph && !item.GlyphVisible)) continue;
                float step = item.Tag.height + StepGap;
                for (int s = 0; s <= MaxSteps; s++)
                {
                    Rect candidate = item.Tag;
                    candidate.y += Steps[s] * step;
                    if (!Free(candidate, item.Priority, bounds)) continue;
                    item.TagVisible = true; item.TagShift = Steps[s] * step;
                    tags.Add(candidate);
                    break;
                }
                items[i] = item;
            }
        }

        private bool Free(Rect candidate, LayoutPriority priority, Rect bounds)
        {
            if (candidate.xMin < bounds.xMin || candidate.yMin < bounds.yMin || candidate.xMax > bounds.xMax || candidate.yMax > bounds.yMax) return false;
            foreach (Rect rect in blocked) if (rect.Overlaps(candidate)) return false;
            foreach (Rect rect in tags) if (rect.Overlaps(candidate)) return false;
            foreach (LayoutItem other in items)
                if (other.GlyphVisible && other.Priority <= priority && other.Glyph.Overlaps(candidate)) return false;
            return true;
        }

        // Priority first, then geometry (top, left, size of the glyph, then of the tag): a total order on the content.
        private int Compare(int a, int b)
        {
            LayoutItem x = items[a], y = items[b];
            int c = x.Priority.CompareTo(y.Priority);
            if (c != 0) return c;
            Rect rx = x.HasGlyph ? x.Glyph : x.Tag, ry = y.HasGlyph ? y.Glyph : y.Tag;
            if ((c = rx.y.CompareTo(ry.y)) != 0) return c;
            if ((c = rx.x.CompareTo(ry.x)) != 0) return c;
            if ((c = x.HasGlyph.CompareTo(y.HasGlyph)) != 0) return -c;
            if ((c = Key(x.Glyph).CompareTo(Key(y.Glyph))) != 0) return c;
            if ((c = x.Tag.y.CompareTo(y.Tag.y)) != 0) return c;
            if ((c = x.Tag.x.CompareTo(y.Tag.x)) != 0) return c;
            if ((c = x.Tag.width.CompareTo(y.Tag.width)) != 0) return c;
            return x.Tag.height.CompareTo(y.Tag.height);
        }

        private static float Key(Rect rect) => rect.width * 4096 + rect.height;
    }
}
