using System;
using System.Collections.Generic;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // Marker priority for the screen-space layout, highest first (U4c fix list F-01/F-02/F-04). Q2: the rules v2 Overloaded
    // alert (HUD_SPEC 13.6) sits between the Survivor edge indicators and the Exit markers: Survivor information always
    // wins, but a short-lived alert is more urgent than the static Exit and Terminal labels. Its glyph is never hidden
    // (only Exit and lower glyphs can be).
    public enum LayoutPriority : byte { Survivor = 0, Edge = 1, Overloaded = 2, Exit = 3, Terminal = 4 }

    // One marker for the layout: an optional glyph rectangle and an optional tag rectangle at its preferred place
    // (panel units, y down). Solve fills the results.
    public struct LayoutItem
    {
        public LayoutPriority Priority;
        public Rect Glyph;          // width 0 = no glyph
        public Rect Tag;            // width 0 = no tag
        public bool KeepGlyph;      // the glyph is never hidden (the rules v2 Exit marker, HUD_SPEC 6.2 "always shown")
        public bool GlyphVisible;   // result
        public bool TagVisible;     // result
        public float TagShift;      // result: vertical offset added to the tag (and its anchor)
        public bool HasGlyph => Glyph.width > 0;
        public bool HasTag => Tag.width > 0;
    }

    // Pure screen-space layout for the Killer world markers, run every frame after projection.
    // 1. Glyphs: Survivor markers (in-view bodies, diamonds, edge chevrons) and Overloaded alerts are never hidden. An Exit or
    //    Terminal glyph that would cover a higher-priority glyph is hidden together with its tag, unless it is KeepGlyph.
    // 2. Tags, highest priority first: a tag may not overlap another placed tag, a visible glyph of the same or higher
    //    priority (its own included), a blocked rectangle (the HUD cluster) or leave the bounds. A colliding tag is stacked by
    //    tag height + 2 u: below, above, below again (3 steps); then it is hidden. The rules v2 Overloaded and Exit tags
    //    (wide, often sandwiched at a crowded edge) instead take the nearest free vertical place within the same 4-step
    //    reach (Q2), then they are hidden.
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

        public int Add(LayoutPriority priority, Rect glyph, Rect tag) => Add(priority, glyph, tag, false);

        // keepGlyph: the glyph is never hidden, even over a higher-priority glyph (its tag still gives way).
        public int Add(LayoutPriority priority, Rect glyph, Rect tag, bool keepGlyph)
        {
            items.Add(new LayoutItem { Priority = priority, Glyph = glyph, Tag = tag, KeepGlyph = keepGlyph });
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
                if (item.HasGlyph && !item.KeepGlyph && item.Priority >= LayoutPriority.Exit)
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
                if (Slotted(item.Priority))
                {
                    if (Slot(item.Tag, item.Priority, bounds, out float shift))
                    {
                        item.TagVisible = true; item.TagShift = shift;
                        Rect placed = item.Tag; placed.y += shift;
                        tags.Add(placed);
                    }
                    items[i] = item;
                    continue;
                }
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

        private static bool Slotted(LayoutPriority priority) => priority == LayoutPriority.Overloaded || priority == LayoutPriority.Exit;

        // Q2: the nearest free vertical place for an Overloaded or Exit tag, within MaxSteps + 1 stacking steps of its own
        // place. Candidates: its own place, just below or above every obstacle (2 u apart) and the bounds; the smallest
        // shift wins, below before above on a tie. No allocation.
        private bool Slot(Rect tag, LayoutPriority priority, Rect bounds, out float shift)
        {
            float limit = (MaxSteps + 1) * (tag.height + StepGap) + 1e-3f;
            shift = 0;
            bool found = false;
            Consider(tag, 0, priority, bounds, limit, ref shift, ref found);
            for (int b = 0; b < blocked.Count; b++) NextTo(tag, blocked[b], priority, bounds, limit, ref shift, ref found);
            for (int t = 0; t < tags.Count; t++) NextTo(tag, tags[t], priority, bounds, limit, ref shift, ref found);
            for (int g = 0; g < items.Count; g++)
                if (items[g].GlyphVisible && items[g].Priority <= priority) NextTo(tag, items[g].Glyph, priority, bounds, limit, ref shift, ref found);
            Consider(tag, bounds.yMin - tag.y, priority, bounds, limit, ref shift, ref found);
            Consider(tag, bounds.yMax - tag.height - tag.y, priority, bounds, limit, ref shift, ref found);
            return found;
        }

        private void NextTo(Rect tag, Rect obstacle, LayoutPriority priority, Rect bounds, float limit, ref float shift, ref bool found)
        {
            if (obstacle.xMax <= tag.xMin || obstacle.xMin >= tag.xMax) return;
            Consider(tag, obstacle.yMax + StepGap - tag.y, priority, bounds, limit, ref shift, ref found);
            Consider(tag, obstacle.yMin - StepGap - tag.height - tag.y, priority, bounds, limit, ref shift, ref found);
        }

        private void Consider(Rect tag, float candidate, LayoutPriority priority, Rect bounds, float limit, ref float shift, ref bool found)
        {
            float size = Mathf.Abs(candidate);
            if (size > limit) return;
            if (found && (size > Mathf.Abs(shift) || (size == Mathf.Abs(shift) && candidate <= shift))) return;
            Rect rect = tag; rect.y += candidate;
            if (!Free(rect, priority, bounds)) return;
            shift = candidate; found = true;
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
            int c = ((byte)x.Priority).CompareTo((byte)y.Priority);
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
