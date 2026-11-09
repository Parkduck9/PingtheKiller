using System.Collections.Generic;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // HUD_SPEC 6.2 edge-indicator placement, steps 1-6 (also used by 4.6). Pure. Screen positions are in panel units with
    // the origin top-left and y pointing down; camera space is x right, y up, z forward.
    public static class EdgePlacement
    {
        public const float Inset = 24, BandInset = 56, KeepOutMargin = 8;

        // Step 2: screen direction from the camera-space point. Works behind the camera too (x and y keep their sign);
        // straight behind (x = y = 0) points at the bottom edge. Returned y-up.
        public static Vector2 Direction(Vector3 cameraSpace)
        {
            var d = new Vector2(cameraSpace.x, cameraSpace.y);
            return d.sqrMagnitude < 1e-12f ? new Vector2(0, -1) : d.normalized;
        }

        // Step 3: the screen inset 24 u, with the top at 56 u while the connection band shows.
        public static Rect SafeRect(Vector2 screen, bool connectionBand)
        {
            float top = connectionBand ? BandInset : Inset;
            return Rect.MinMaxRect(Inset, top, Mathf.Max(Inset, screen.x - Inset), Mathf.Max(top, screen.y - Inset));
        }

        // Step 5: the HUD cluster expanded by 8 u.
        public static Rect KeepOut(Rect cluster)
            => Rect.MinMaxRect(cluster.xMin - KeepOutMargin, cluster.yMin - KeepOutMargin, cluster.xMax + KeepOutMargin, cluster.yMax + KeepOutMargin);

        // Steps 4-5. directionUp: step 2's direction (y up). nearestSlide: on the bottom edge slide to whichever side of the
        // keep-out is nearer (Survivor); otherwise slide away from the screen corner (Killer: right on the bottom edge).
        // pointing: the direction the chevron points, y down.
        public static Vector2 Place(Vector2 screen, Vector2 directionUp, Rect safe, Rect keepOut, bool nearestSlide, out Vector2 pointing)
        {
            Vector2 center = screen * .5f;
            pointing = new Vector2(directionUp.x, -directionUp.y);
            if (pointing.sqrMagnitude < 1e-12f) pointing = new Vector2(0, 1);
            pointing.Normalize();
            float t = float.PositiveInfinity;
            if (pointing.x > 1e-6f) t = Mathf.Min(t, (safe.xMax - center.x) / pointing.x);
            else if (pointing.x < -1e-6f) t = Mathf.Min(t, (safe.xMin - center.x) / pointing.x);
            if (pointing.y > 1e-6f) t = Mathf.Min(t, (safe.yMax - center.y) / pointing.y);
            else if (pointing.y < -1e-6f) t = Mathf.Min(t, (safe.yMin - center.y) / pointing.y);
            if (float.IsInfinity(t) || t < 0) t = 0;
            Vector2 p = center + pointing * t;
            p.x = Mathf.Clamp(p.x, safe.xMin, safe.xMax);
            p.y = Mathf.Clamp(p.y, safe.yMin, safe.yMax);
            if (!Inside(keepOut, p)) return p;
            const float edge = .01f;
            bool vertical = Mathf.Abs(p.x - safe.xMin) < edge || Mathf.Abs(p.x - safe.xMax) < edge;
            bool horizontal = Mathf.Abs(p.y - safe.yMin) < edge || Mathf.Abs(p.y - safe.yMax) < edge;
            if (horizontal && (!vertical || Mathf.Abs(p.y - safe.yMax) < edge))
            {
                // On the bottom (or top) edge: slide along x.
                bool right = nearestSlide ? keepOut.xMax - p.x <= p.x - keepOut.xMin : keepOut.center.x < center.x;
                p.x = Mathf.Clamp(right ? keepOut.xMax : keepOut.xMin, safe.xMin, safe.xMax);
                if (Inside(keepOut, p)) p.y = Mathf.Clamp(keepOut.yMin, safe.yMin, safe.yMax);
            }
            else
            {
                // On the left or right edge: slide along y, away from the nearer screen corner.
                bool up = keepOut.center.y > center.y;
                p.y = Mathf.Clamp(up ? keepOut.yMin : keepOut.yMax, safe.yMin, safe.yMax);
                if (Inside(keepOut, p)) p.x = Mathf.Clamp(keepOut.xMax, safe.xMin, safe.xMax);
            }
            return p;
        }

        // Step 6: an on-screen marker inside the keep-out moves to the nearest keep-out edge whose outside stays in the
        // safe rectangle.
        public static Vector2 ClampOutside(Vector2 point, Rect keepOut, Rect safe)
        {
            if (!Inside(keepOut, point)) return point;
            Vector2 best = point; float bestDistance = float.PositiveInfinity;
            Consider(new Vector2(point.x, keepOut.yMin), point, safe, ref best, ref bestDistance);
            Consider(new Vector2(point.x, keepOut.yMax), point, safe, ref best, ref bestDistance);
            Consider(new Vector2(keepOut.xMin, point.y), point, safe, ref best, ref bestDistance);
            Consider(new Vector2(keepOut.xMax, point.y), point, safe, ref best, ref bestDistance);
            return best;
        }

        // Where a tag sits: on the screen-centre side of its chevron.
        public static Vector2 TagAnchor(Vector2 chevron, Vector2 screen, float offset)
        {
            Vector2 toCenter = screen * .5f - chevron;
            return toCenter.sqrMagnitude < 1e-6f ? chevron : chevron + toCenter.normalized * offset;
        }

        private static void Consider(Vector2 candidate, Vector2 point, Rect safe, ref Vector2 best, ref float bestDistance)
        {
            if (candidate.x < safe.xMin || candidate.x > safe.xMax || candidate.y < safe.yMin || candidate.y > safe.yMax) return;
            float distance = (candidate - point).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = candidate; }
        }

        // Strictly inside: a point on the keep-out edge is already clear.
        private static bool Inside(Rect r, Vector2 p) => p.x > r.xMin && p.x < r.xMax && p.y > r.yMin && p.y < r.yMax;
    }

    public enum EdgeSide : byte { Left = 0, Right = 1, Top = 2, Bottom = 3 }

    // Where an edge marker's tag sits. Beside: on the screen-centre side of the glyph, 4 u away (Survivor chevrons).
    // Below: 12 u under the glyph and kept inside the safe rectangle (the Overloaded alert, HUD_SPEC 13.6); on the
    // bottom edge, where below would leave the screen, 12 u above it.
    public enum EdgeTagMode : byte { Beside, Below }

    // Q2: spreads the frame's off-screen edge markers (Survivor chevrons and off-screen Overloaded squares together) along
    // their edge so that no two marker+tag footprints on one edge overlap, with a 4 u gap. Pure and allocation-free per
    // frame (buffers are reused). Per edge:
    // 1. Order by the ideal coordinate along the edge (tie: the caller's stable key, never the input order). Two
    //    neighbours keep last frame's order while their ideal coordinates differ by less than Hysteresis.
    // 2. Displacement-minimising 1D packing: in gap-removed coordinates the problem is isotonic regression (pool adjacent
    //    violators, each cluster at the mean of its ideal positions), then clamped to the usable segment (the safe
    //    rectangle's side minus the keep-out). Continuous in the inputs while the order holds; outputs are whole units.
    // 3. If the footprints do not fit: shrink the gap 4 -> 3 -> 2 u, then drop tags one at a time (glyphs always stay),
    //    the lowest priority first (an Overloaded tag before a Survivor edge tag), then the farthest, then the higher key.
    public sealed class EdgeSpread
    {
        public const float Gap = 4, MinGap = 2, Hysteresis = 6, BesideGap = 4, BelowGap = 12, EdgeEpsilon = .5f;

        private struct Item
        {
            public Vector2 Ideal, GlyphHalf, TagSize, Glyph;
            public EdgeTagMode Mode;
            public LayoutPriority Priority;
            public int Key;
            public float Distance;
            public EdgeSide Side;
            public bool TagShown;
            public float Lo, Hi, Target;   // footprint along the edge relative to the glyph; solver scratch
        }

        private readonly List<Item> items = new List<Item>();
        private readonly List<int>[] sides = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        private readonly List<int> fresh = new List<int>();
        private readonly List<float> blockSum = new List<float>();
        private readonly List<int> blockCount = new List<int>();
        private readonly Dictionary<int, int> previous = new Dictionary<int, int>();   // key -> rank * 4 + side
        private Rect safe, keepOut;

        public int Count => items.Count;

        public void Begin(Rect safeRect, Rect keepOutRect)
        {
            items.Clear();
            safe = safeRect; keepOut = keepOutRect;
        }

        // ideal: the EdgePlacement.Place point. glyphHalf: half the glyph's bounds (keyline included). tagSize: 0 = no tag.
        // key: unique per frame and stable across frames (a blip handle; terminals use negative keys).
        public int Add(Vector2 ideal, Vector2 glyphHalf, Vector2 tagSize, EdgeTagMode mode, LayoutPriority priority, int key, float distance)
        {
            items.Add(new Item
            {
                Ideal = ideal, GlyphHalf = glyphHalf, TagSize = tagSize.x > 0 && tagSize.y > 0 ? tagSize : Vector2.zero,
                Mode = mode, Priority = priority, Key = key, Distance = distance, Side = SideOf(ideal, safe), Glyph = ideal,
            });
            return items.Count - 1;
        }

        public EdgeSide Side(int index) => items[index].Side;
        // The spread glyph centre.
        public Vector2 Glyph(int index) => items[index].Glyph;
        public bool HasTag(int index) => items[index].TagSize.x > 0;
        // False when the tag was dropped because the edge was full.
        public bool TagShown(int index) => items[index].TagShown;
        public Rect GlyphRect(int index) => TagLayout.Around(items[index].Glyph, items[index].GlyphHalf.x, items[index].GlyphHalf.y);
        // The tag rectangle at the spread position (width 0 when the item has no tag).
        public Rect Tag(int index)
        {
            Item item = items[index];
            return item.TagSize.x > 0 ? TagRect(item.Glyph, item.GlyphHalf, item.TagSize, item.Mode, item.Side, safe) : default;
        }

        // Glyph plus shown tag: what may not overlap another footprint on the same edge.
        public Rect Footprint(int index)
        {
            Rect glyph = GlyphRect(index);
            if (!items[index].TagShown) return glyph;
            Rect tag = Tag(index);
            return Rect.MinMaxRect(Mathf.Min(glyph.xMin, tag.xMin), Mathf.Min(glyph.yMin, tag.yMin), Mathf.Max(glyph.xMax, tag.xMax), Mathf.Max(glyph.yMax, tag.yMax));
        }

        // The edge a placed point belongs to. Corners go to the left or right edge.
        public static EdgeSide SideOf(Vector2 p, Rect safe)
        {
            if (Mathf.Abs(p.x - safe.xMax) <= EdgeEpsilon) return EdgeSide.Right;
            if (Mathf.Abs(p.x - safe.xMin) <= EdgeEpsilon) return EdgeSide.Left;
            if (Mathf.Abs(p.y - safe.yMin) <= EdgeEpsilon) return EdgeSide.Top;
            if (Mathf.Abs(p.y - safe.yMax) <= EdgeEpsilon) return EdgeSide.Bottom;
            // Not on an edge (a keep-out fallback): the nearest one.
            float left = p.x - safe.xMin, right = safe.xMax - p.x, top = p.y - safe.yMin, bottom = safe.yMax - p.y;
            float best = Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));
            return best == right ? EdgeSide.Right : best == left ? EdgeSide.Left : best == top ? EdgeSide.Top : EdgeSide.Bottom;
        }

        // The tag rectangle of a glyph at an edge (panel units, y down).
        public static Rect TagRect(Vector2 glyph, Vector2 glyphHalf, Vector2 size, EdgeTagMode mode, EdgeSide side, Rect safe)
        {
            float x, y;
            if (mode == EdgeTagMode.Beside)
            {
                switch (side)
                {
                    case EdgeSide.Right: x = glyph.x - glyphHalf.x - BesideGap - size.x; y = glyph.y - size.y * .5f; break;
                    case EdgeSide.Left: x = glyph.x + glyphHalf.x + BesideGap; y = glyph.y - size.y * .5f; break;
                    case EdgeSide.Top: x = glyph.x - size.x * .5f; y = glyph.y + glyphHalf.y + BesideGap; break;
                    default: x = glyph.x - size.x * .5f; y = glyph.y - glyphHalf.y - BesideGap - size.y; break;
                }
                return new Rect(x, y, size.x, size.y);
            }
            x = glyph.x - size.x * .5f;
            if (safe.width > size.x) x = Mathf.Clamp(x, safe.xMin, safe.xMax - size.x);
            y = side == EdgeSide.Bottom ? glyph.y - BelowGap - size.y : glyph.y + BelowGap;
            return new Rect(x, y, size.x, size.y);
        }

        public void Solve()
        {
            for (int s = 0; s < sides.Length; s++) sides[s].Clear();
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                item.TagShown = item.TagSize.x > 0;
                item.Glyph = item.Ideal;
                items[i] = item;
            }
            for (int s = 0; s < sides.Length; s++)
            {
                Order((EdgeSide)s);
                Pack((EdgeSide)s);
            }
            previous.Clear();
            for (int s = 0; s < sides.Length; s++)
                for (int r = 0; r < sides[s].Count; r++) previous[items[sides[s][r]].Key] = r * 4 + s;
        }

        private static bool Vertical(EdgeSide side) => side == EdgeSide.Left || side == EdgeSide.Right;
        private static float Along(Vector2 p, EdgeSide side) => Vertical(side) ? p.y : p.x;

        // 1. Last frame's order for the known items, new items inserted by (ideal, key); then adjacent swaps only where two
        //    neighbours are out of order by Hysteresis or more. Deterministic for a given set; never the input order.
        private void Order(EdgeSide side)
        {
            List<int> list = sides[(int)side];
            fresh.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Side != side) continue;
                if (previous.TryGetValue(items[i].Key, out int code) && code % 4 == (int)side) list.Add(i);
                else fresh.Add(i);
            }
            // Known items by last rank (insertion sort; a handful of items, no allocation).
            for (int a = 1; a < list.Count; a++)
                for (int b = a; b > 0 && previous[items[list[b - 1]].Key] > previous[items[list[b]].Key]; b--) Swap(list, b - 1, b);
            for (int a = 1; a < fresh.Count; a++)
                for (int b = a; b > 0 && Before(fresh[b], fresh[b - 1], side); b--) Swap(fresh, b - 1, b);
            for (int f = 0; f < fresh.Count; f++)
            {
                int at = 0;
                while (at < list.Count && !Before(fresh[f], list[at], side)) at++;
                list.Insert(at, fresh[f]);
            }
            // Each swap removes one inversion of the ideal order, so this ends.
            for (bool swapped = true; swapped;)
            {
                swapped = false;
                for (int k = 0; k + 1 < list.Count; k++)
                    if (Along(items[list[k]].Ideal, side) - Along(items[list[k + 1]].Ideal, side) >= Hysteresis) { Swap(list, k, k + 1); swapped = true; }
            }
        }

        private bool Before(int a, int b, EdgeSide side)
        {
            float ia = Along(items[a].Ideal, side), ib = Along(items[b].Ideal, side);
            return ia < ib || (ia == ib && items[a].Key < items[b].Key);
        }

        private static void Swap(List<int> list, int a, int b) { int t = list[a]; list[a] = list[b]; list[b] = t; }

        // The usable stretch of an edge: the safe rectangle's side minus the part the keep-out covers (the longer piece
        // when the keep-out splits it).
        public static void Segment(EdgeSide side, Rect safe, Rect keepOut, out float lo, out float hi)
        {
            bool vertical = Vertical(side);
            lo = vertical ? safe.yMin : safe.xMin;
            hi = vertical ? safe.yMax : safe.xMax;
            float line = side == EdgeSide.Left ? safe.xMin : side == EdgeSide.Right ? safe.xMax : side == EdgeSide.Top ? safe.yMin : safe.yMax;
            float kMin = vertical ? keepOut.xMin : keepOut.yMin, kMax = vertical ? keepOut.xMax : keepOut.yMax;
            if (keepOut.width <= 0 || keepOut.height <= 0 || line < kMin || line > kMax) return;
            float cutLo = vertical ? keepOut.yMin : keepOut.xMin, cutHi = vertical ? keepOut.yMax : keepOut.xMax;
            if (cutHi <= lo || cutLo >= hi) return;
            if (cutLo - lo >= hi - cutHi) hi = Mathf.Min(hi, cutLo); else lo = Mathf.Max(lo, cutHi);
        }

        private static void Footprint(ref Item item, EdgeSide side)
        {
            bool vertical = Vertical(side);
            float half = vertical ? item.GlyphHalf.y : item.GlyphHalf.x;
            item.Lo = -half; item.Hi = half;
            if (!item.TagShown) return;
            if (item.Mode == EdgeTagMode.Below && vertical)
            {
                item.Hi = Mathf.Max(half, BelowGap + item.TagSize.y);
                return;
            }
            float tagHalf = (vertical ? item.TagSize.y : item.TagSize.x) * .5f;
            item.Lo = Mathf.Min(item.Lo, -tagHalf); item.Hi = Mathf.Max(item.Hi, tagHalf);
        }

        private void Pack(EdgeSide side)
        {
            List<int> list = sides[(int)side];
            if (list.Count == 0) return;
            Segment(side, safe, keepOut, out float lo, out float hi);
            while (true)
            {
                for (int k = 0; k < list.Count; k++) { Item item = items[list[k]]; Footprint(ref item, side); items[list[k]] = item; }
                for (float gap = Gap; gap >= MinGap; gap -= 1)
                    if (TryPack(list, side, lo, hi, gap)) return;
                if (!DropOneTag(list)) break;
            }
            // Even the bare glyphs do not fit: squeeze them evenly over the segment (they may overlap; none is dropped).
            float total = 0;
            for (int k = 0; k < list.Count; k++) total += items[list[k]].Hi - items[list[k]].Lo;
            float squeeze = list.Count > 1 ? (hi - lo - total) / (list.Count - 1) : 0;
            float at = lo;
            for (int k = 0; k < list.Count; k++)
            {
                Item item = items[list[k]];
                Set(ref item, side, Mathf.Floor(at - item.Lo + .5f));
                at += item.Hi - item.Lo + squeeze;
                items[list[k]] = item;
            }
        }

        // Isotonic regression in gap-removed coordinates (y = x - D, D = the summed minimum spacings in whole units),
        // clamped to the segment and rounded with floor(v + .5), so a 1 u input change moves an output by at most 1 u.
        private bool TryPack(List<int> list, EdgeSide side, float lo, float hi, float gap)
        {
            float offset = 0;
            for (int k = 0; k < list.Count; k++)
            {
                Item item = items[list[k]];
                if (k > 0) offset += Spacing(items[list[k - 1]], item, gap);
                item.Target = Along(item.Ideal, side) - offset;
                items[list[k]] = item;
            }
            Item first = items[list[0]], last = items[list[list.Count - 1]];
            float yMin = Mathf.Ceil(lo - first.Lo - 1e-4f), yMax = Mathf.Floor(hi - last.Hi - offset + 1e-4f);
            if (yMin > yMax) return false;
            // Pool adjacent violators: each block sits at the mean of its targets.
            blockSum.Clear(); blockCount.Clear();
            for (int k = 0; k < list.Count; k++)
            {
                blockSum.Add(items[list[k]].Target); blockCount.Add(1);
                while (blockSum.Count > 1)
                {
                    int n = blockSum.Count;
                    if (blockSum[n - 2] / blockCount[n - 2] <= blockSum[n - 1] / blockCount[n - 1]) break;
                    blockSum[n - 2] += blockSum[n - 1]; blockCount[n - 2] += blockCount[n - 1];
                    blockSum.RemoveAt(n - 1); blockCount.RemoveAt(n - 1);
                }
            }
            offset = 0;
            int index = 0;
            for (int b = 0; b < blockSum.Count; b++)
            {
                float y = Mathf.Clamp(Mathf.Floor(blockSum[b] / blockCount[b] + .5f), yMin, yMax);
                for (int c = 0; c < blockCount[b]; c++, index++)
                {
                    Item item = items[list[index]];
                    if (index > 0) offset += Spacing(items[list[index - 1]], item, gap);
                    Set(ref item, side, y + offset);
                    items[list[index]] = item;
                }
            }
            return true;
        }

        // The minimum centre-to-centre distance of two neighbours, in whole units.
        private static float Spacing(Item before, Item after, float gap) => Mathf.Ceil(before.Hi - after.Lo + gap - 1e-4f);

        private static void Set(ref Item item, EdgeSide side, float along)
            => item.Glyph = Vertical(side) ? new Vector2(item.Ideal.x, along) : new Vector2(along, item.Ideal.y);

        // Lowest priority first (the highest LayoutPriority value), then the farthest, then the higher key.
        private bool DropOneTag(List<int> list)
        {
            int pick = -1;
            for (int k = 0; k < list.Count; k++)
            {
                Item item = items[list[k]];
                if (!item.TagShown) continue;
                if (pick < 0) { pick = list[k]; continue; }
                Item best = items[pick];
                if (item.Priority > best.Priority || (item.Priority == best.Priority &&
                    (item.Distance > best.Distance || (item.Distance == best.Distance && item.Key > best.Key)))) pick = list[k];
            }
            if (pick < 0) return false;
            Item dropped = items[pick]; dropped.TagShown = false; items[pick] = dropped;
            return true;
        }
    }
}
