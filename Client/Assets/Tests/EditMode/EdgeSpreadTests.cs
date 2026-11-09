using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // Q2-edge-labels: the Killer edge spread (EdgePlacement.EdgeSpread) and the single layout pass for every Killer tag.
    // Survivor chevrons and off-screen Overloaded alerts on one edge never overlap (marker + tag, 4 u gap), the result does
    // not depend on the input order, a slowly drifting input never makes the output jump, and the busiest Killer edge
    // (3 Overloaded alerts, an Exit, a Survivor chevron and a diamond) stays readable at 720p and 1080p.
    public sealed class EdgeSpreadTests
    {
        private static readonly Vector2 Screen720 = new Vector2(1600, 900), Screen1080 = new Vector2(1920, 1080);
        private const string Near = "42 m", LaggingNear = "Lagging · 42 m", Alert = "Terminal 3 overloaded · 41 m", ExitTag = "Exit 1 · Powered";
        private static readonly EdgeSide[] Sides = { EdgeSide.Right, EdgeSide.Left, EdgeSide.Top, EdgeSide.Bottom };

        // The Killer cluster in normal play (info 72 + 8 + skill 72), bottom-left at 24 u, expanded by the 8 u keep-out.
        private static Rect KillerKeepOut(Vector2 screen) => EdgePlacement.KeepOut(new Rect(24, screen.y - 24 - 152, 320, 152));

        // kind 0: chevron, 1: lagging chevron, 2: Overloaded alert. Sizes exactly as KillerWorldLayer feeds them.
        private static int AddMarker(EdgeSpread spread, Vector2 ideal, int kind, int key, float distance)
        {
            if (kind == 2)
                return spread.Add(ideal, new Vector2(V2MarkerLayer.OverloadedHalf, V2MarkerLayer.OverloadedHalf), MarkerLayer.TagSize(Alert, TagStyle.Text),
                    EdgeTagMode.Below, LayoutPriority.Overloaded, key, distance);
            bool lagging = kind == 1;
            float half = MarkerLayer.ChevronHalf(lagging);
            return spread.Add(ideal, new Vector2(half, half), MarkerLayer.TagSize(lagging ? LaggingNear : Near, TagStyle.Text), EdgeTagMode.Beside, LayoutPriority.Edge, key, distance);
        }

        // A point on an edge: the middle of its usable segment plus an offset along it.
        private static Vector2 OnEdge(EdgeSide side, Rect safe, Rect keepOut, float offset)
        {
            EdgeSpread.Segment(side, safe, keepOut, out float lo, out float hi);
            float along = (lo + hi) * .5f + offset;
            switch (side)
            {
                case EdgeSide.Right: return new Vector2(safe.xMax, along);
                case EdgeSide.Left: return new Vector2(safe.xMin, along);
                case EdgeSide.Top: return new Vector2(along, safe.yMin);
                default: return new Vector2(along, safe.yMax);
            }
        }

        private static bool Vertical(EdgeSide side) => side == EdgeSide.Left || side == EdgeSide.Right;

        private static bool Inside(Rect inner, Rect outer, float eps = 1e-3f)
            => inner.xMin >= outer.xMin - eps && inner.yMin >= outer.yMin - eps && inner.xMax <= outer.xMax + eps && inner.yMax <= outer.yMax + eps;

        // Distance between two footprints along their edge (negative = overlap).
        private static float GapAlong(Rect a, Rect b, EdgeSide side)
            => Vertical(side) ? Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax) : Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax);

        private static void AssertClean(EdgeSpread spread, Vector2 screen, Rect safe, Rect keepOut, string label)
        {
            var screenRect = new Rect(0, 0, screen.x, screen.y);
            for (int i = 0; i < spread.Count; i++)
            {
                Vector2 g = spread.Glyph(i);
                Assert.That(g.x >= safe.xMin - 1e-3f && g.x <= safe.xMax + 1e-3f && g.y >= safe.yMin - 1e-3f && g.y <= safe.yMax + 1e-3f, Is.True, label + ": glyph " + i + " centre in the safe rect " + g);
                Assert.That(Inside(spread.GlyphRect(i), screenRect), Is.True, label + ": glyph " + i + " on screen");
                Assert.That(g.y == Mathf.Round(g.y) && g.x == Mathf.Round(g.x), Is.True, label + ": whole units " + g);
                Assert.That(spread.Footprint(i).Overlaps(keepOut), Is.False, label + ": footprint " + i + " out of the keep-out");
                if (!spread.TagShown(i)) continue;
                Assert.That(Inside(spread.Tag(i), safe), Is.True, label + ": tag " + i + " inside the safe rect " + spread.Tag(i));
            }
            for (int i = 0; i < spread.Count; i++)
                for (int j = i + 1; j < spread.Count; j++)
                {
                    if (spread.TagShown(i) && spread.TagShown(j))
                        Assert.That(spread.Tag(i).Overlaps(spread.Tag(j)), Is.False, label + ": tags " + i + " / " + j);
                    if (spread.Side(i) != spread.Side(j)) continue;
                    Assert.That(GapAlong(spread.Footprint(i), spread.Footprint(j), spread.Side(i)), Is.GreaterThanOrEqualTo(EdgeSpread.Gap - 1e-3f),
                        label + ": footprints " + i + " / " + j + " keep the 4 u gap");
                }
        }

        [Test]
        public void FiveSevenAndTenClusteredMarkersOnEachEdgeNeverOverlap([Values(5, 7, 10)] int count)
        {
            Rect safe = EdgePlacement.SafeRect(Screen720, false), keepOut = KillerKeepOut(Screen720);
            foreach (EdgeSide side in Sides)
            {
                var spread = new EdgeSpread();
                spread.Begin(safe, keepOut);
                // Ideal points clustered within 20 u; every third one an Overloaded alert, every third a lagging chevron.
                for (int i = 0; i < count; i++) AddMarker(spread, OnEdge(side, safe, keepOut, (i * 7919 % 21) - 10), i % 3, 100 + i, 10 + i);
                spread.Solve();
                string label = side + " x" + count;
                AssertClean(spread, Screen720, safe, keepOut, label);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(spread.Side(i), Is.EqualTo(side), label);
                    Assert.That(spread.TagShown(i), Is.True, label + ": everything fits, no tag dropped");
                }

                // The layout pass keeps what the spread cleared: every tag visible and not moved (shift 0).
                var layout = new TagLayout();
                layout.Block(keepOut);
                for (int i = 0; i < count; i++)
                    layout.Add(i % 3 == 2 ? LayoutPriority.Overloaded : LayoutPriority.Edge, spread.GlyphRect(i), spread.Tag(i));
                layout.Solve(safe);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(layout[i].GlyphVisible && layout[i].TagVisible, Is.True, label + ": layout item " + i);
                    Assert.That(layout[i].TagShift, Is.EqualTo(0f), label + ": the layout does not undo the spread, item " + i);
                }
            }
        }

        [Test]
        public void TheResultDoesNotDependOnTheInputOrder()
        {
            Rect safe = EdgePlacement.SafeRect(Screen720, false), keepOut = KillerKeepOut(Screen720);
            var inputs = new List<(Vector2 ideal, int kind, int key, float distance)>();
            // Exact ties on purpose: several markers on the same ideal point (the key breaks the tie).
            float[] offsets = { 0, 0, 0, 3, 3, -5, 8, -8, 12, 0 };
            for (int i = 0; i < offsets.Length; i++) inputs.Add((OnEdge(EdgeSide.Right, safe, keepOut, offsets[i]), i % 3, 50 - i * 3, 20 + (i % 4)));
            inputs.Add((OnEdge(EdgeSide.Bottom, safe, keepOut, 4), 0, 7, 30));
            inputs.Add((OnEdge(EdgeSide.Bottom, safe, keepOut, 4), 2, -3, 30));
            Dictionary<int, string> reference = Run(inputs, safe, keepOut);
            var rng = new System.Random(11);
            for (int round = 0; round < 25; round++)
            {
                var shuffled = new List<(Vector2, int, int, float)>(inputs);
                for (int i = shuffled.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t; }
                Dictionary<int, string> result = Run(shuffled, safe, keepOut);
                foreach (var pair in reference) Assert.That(result[pair.Key], Is.EqualTo(pair.Value), "round " + round + ", key " + pair.Key);
            }
        }

        private static Dictionary<int, string> Run(List<(Vector2 ideal, int kind, int key, float distance)> inputs, Rect safe, Rect keepOut)
        {
            var spread = new EdgeSpread();
            spread.Begin(safe, keepOut);
            foreach (var input in inputs) AddMarker(spread, input.ideal, input.kind, input.key, input.distance);
            spread.Solve();
            var result = new Dictionary<int, string>();
            for (int i = 0; i < inputs.Count; i++) result[inputs[i].key] = spread.Glyph(i) + " " + spread.Tag(i) + " " + spread.TagShown(i);
            return result;
        }

        [Test]
        public void ADriftingInputNeverMakesTheOutputJumpOrFlipOrder()
        {
            Rect safe = EdgePlacement.SafeRect(Screen720, false), keepOut = KillerKeepOut(Screen720);
            const int count = 7, frames = 120;
            var rng = new System.Random(20261108);
            var bases = new float[count];
            var ideal = new float[count];
            for (int i = 0; i < count; i++) { bases[i] = 440 + i * 3; ideal[i] = bases[i]; }   // clustered within 20 u
            var spread = new EdgeSpread();
            var lastGlyph = new Vector2[count];
            var lastIdeal = new float[count];
            for (int frame = 0; frame < frames; frame++)
            {
                if (frame > 0)
                    for (int i = 0; i < count; i++)
                    {
                        // A random step of at most 0.5 u, kept within 2.5 u of its base.
                        ideal[i] = Mathf.Clamp(ideal[i] + (float)(rng.NextDouble() - .5), bases[i] - 2.5f, bases[i] + 2.5f);
                    }
                spread.Begin(safe, keepOut);
                for (int i = 0; i < count; i++) AddMarker(spread, new Vector2(safe.xMax, ideal[i]), i % 3, 900 + i, 15 + i);
                spread.Solve();
                AssertClean(spread, Screen720, safe, keepOut, "frame " + frame);
                for (int i = 0; i < count; i++)
                {
                    Vector2 g = spread.Glyph(i);
                    if (frame > 0)
                    {
                        Assert.That(Mathf.Abs(g.y - lastGlyph[i].y), Is.LessThanOrEqualTo(1f), "frame " + frame + " item " + i + " moved more than 1 u");
                        Assert.That(g.x, Is.EqualTo(lastGlyph[i].x));
                        for (int j = 0; j < count; j++)
                        {
                            if (j == i) continue;
                            bool near = Mathf.Abs(ideal[i] - ideal[j]) < EdgeSpread.Hysteresis && Mathf.Abs(lastIdeal[i] - lastIdeal[j]) < EdgeSpread.Hysteresis;
                            if (near)
                                Assert.That(g.y < spread.Glyph(j).y, Is.EqualTo(lastGlyph[i].y < lastGlyph[j].y), "frame " + frame + ": order of " + i + " / " + j + " flipped inside the hysteresis");
                        }
                    }
                }
                for (int i = 0; i < count; i++) { lastGlyph[i] = spread.Glyph(i); lastIdeal[i] = ideal[i]; }
            }
        }

        [Test]
        public void AnOverfullEdgeShrinksTheGapThenDropsOverloadedTagsFarthestFirstAndKeepsEveryGlyph()
        {
            Rect safe = EdgePlacement.SafeRect(Screen720, false), keepOut = KillerKeepOut(Screen720);
            var spread = new EdgeSpread();
            spread.Begin(safe, keepOut);
            // 16 alerts (44 u footprints) + 10 chevrons (23 u) need about 1000 u on an 852 u edge.
            for (int i = 0; i < 16; i++) AddMarker(spread, OnEdge(EdgeSide.Right, safe, keepOut, i - 8), 2, -1 - i, 10 + i);
            for (int i = 0; i < 10; i++) AddMarker(spread, OnEdge(EdgeSide.Right, safe, keepOut, i - 5), 0, 1 + i, 50 + i);
            spread.Solve();
            int dropped = 0;
            for (int i = 0; i < 16; i++) if (!spread.TagShown(i)) dropped++;
            Assert.That(dropped, Is.GreaterThan(0), "it does not fit with all tags");
            Assert.That(dropped, Is.LessThan(16), "only as many as needed");
            for (int i = 0; i < 16; i++)
                Assert.That(spread.TagShown(i), Is.EqualTo(i < 16 - dropped), "the farthest alerts lose their tag first (alert " + i + ")");
            for (int i = 16; i < 26; i++) Assert.That(spread.TagShown(i), Is.True, "Survivor edge tags win over Overloaded tags");
            var screenRect = new Rect(0, 0, Screen720.x, Screen720.y);
            for (int i = 0; i < spread.Count; i++)
            {
                Assert.That(Inside(spread.GlyphRect(i), screenRect), Is.True, "every glyph stays, on screen");
                if (spread.TagShown(i)) Assert.That(Inside(spread.Tag(i), safe), Is.True);
            }
            for (int i = 0; i < spread.Count; i++)
                for (int j = i + 1; j < spread.Count; j++)
                    Assert.That(GapAlong(spread.Footprint(i), spread.Footprint(j), EdgeSide.Right), Is.GreaterThanOrEqualTo(EdgeSpread.MinGap - 1e-3f), i + " / " + j);
        }

        [Test]
        public void TheLeftEdgeSegmentStopsAboveTheKillerClusterAndTheBottomOneRightOfIt()
        {
            Rect safe = EdgePlacement.SafeRect(Screen720, false), keepOut = KillerKeepOut(Screen720);
            EdgeSpread.Segment(EdgeSide.Left, safe, keepOut, out float lo, out float hi);
            Assert.That(lo, Is.EqualTo(24f)); Assert.That(hi, Is.EqualTo(keepOut.yMin));
            EdgeSpread.Segment(EdgeSide.Bottom, safe, keepOut, out lo, out hi);
            Assert.That(lo, Is.EqualTo(keepOut.xMax)); Assert.That(hi, Is.EqualTo(1576f));
            EdgeSpread.Segment(EdgeSide.Right, safe, keepOut, out lo, out hi);
            Assert.That(lo, Is.EqualTo(24f)); Assert.That(hi, Is.EqualTo(876f));
            // A chevron pushed onto the keep-out corner of the left edge is moved up until its tag clears the cluster.
            var spread = new EdgeSpread();
            spread.Begin(safe, keepOut);
            AddMarker(spread, new Vector2(safe.xMin, keepOut.yMin), 0, 1, 30);
            AddMarker(spread, new Vector2(safe.xMin, keepOut.yMin - 2), 2, -1, 30);
            spread.Solve();
            AssertClean(spread, Screen720, safe, keepOut, "left corner");
            Assert.That(spread.Tag(0).Overlaps(keepOut) || spread.Tag(1).Overlaps(keepOut), Is.False);
        }

        [Test]
        public void TheTagSitsBesideTheChevronOnTheScreenCentreSideAndTheAlertTagBelowItsSquare()
        {
            Rect safe = EdgePlacement.SafeRect(Screen720, false);
            Vector2 size = new Vector2(40, 23), half = new Vector2(10, 10);
            Rect right = EdgeSpread.TagRect(new Vector2(1576, 450), half, size, EdgeTagMode.Beside, EdgeSide.Right, safe);
            Assert.That(right, Is.EqualTo(new Rect(1576 - 10 - 4 - 40, 450 - 11.5f, 40, 23)));
            Rect left = EdgeSpread.TagRect(new Vector2(24, 450), half, size, EdgeTagMode.Beside, EdgeSide.Left, safe);
            Assert.That(left.xMin, Is.EqualTo(24 + 10 + 4f));
            Rect top = EdgeSpread.TagRect(new Vector2(800, 24), half, size, EdgeTagMode.Beside, EdgeSide.Top, safe);
            Assert.That(top.yMin, Is.EqualTo(24 + 10 + 4f));
            Rect bottom = EdgeSpread.TagRect(new Vector2(800, 876), half, size, EdgeTagMode.Beside, EdgeSide.Bottom, safe);
            Assert.That(bottom.yMax, Is.EqualTo(876 - 10 - 4f));
            Rect alert = EdgeSpread.TagRect(new Vector2(1576, 450), new Vector2(9, 9), new Vector2(200, 23), EdgeTagMode.Below, EdgeSide.Right, safe);
            Assert.That(alert, Is.EqualTo(new Rect(1576 - 200, 450 + 12, 200, 23)), "12 u below, kept inside the safe rect");
            Rect alertBottom = EdgeSpread.TagRect(new Vector2(800, 876), new Vector2(9, 9), new Vector2(200, 23), EdgeTagMode.Below, EdgeSide.Bottom, safe);
            Assert.That(alertBottom.yMax, Is.EqualTo(876 - 12f), "on the bottom edge the alert tag goes above its square");
        }

        // ---- the crowded Killer edge -------------------------------------------------------------------------------------

        [Test]
        public void TheCrowdedRightEdgeStaysReadableAt720pAnd1080p()
        {
#if !Q2_SCRATCH
            // The real Killer keep-out: the cluster rectangle in normal play, expanded by 8 u.
            var parent = new UnityEngine.UIElements.VisualElement();
            var hud = new KillerHud(parent, Resources.Load<Font>("Fonts/Inter-Regular"));
            hud.Begin(PingTheKiller.Shared.Game.SkillIds.PingScanner);
            hud.SetIntro(false);
            Rect keepOut720 = EdgePlacement.KeepOut(hud.ClusterRect(Screen720.y)), keepOut1080 = EdgePlacement.KeepOut(hud.ClusterRect(Screen1080.y));
            Assert.That(keepOut720, Is.EqualTo(KillerKeepOut(Screen720)));
#else
            Rect keepOut720 = KillerKeepOut(Screen720), keepOut1080 = KillerKeepOut(Screen1080);
#endif
            // The on-screen diamond and Exit swept through the edge cluster's rows (every 10 u, 150 u either way).
            for (int dy = -150; dy <= 150; dy += 10)
            {
                Crowded(Screen720, keepOut720, dy, -dy / 2);
                Crowded(Screen1080, keepOut1080, dy, -dy / 2);
            }
        }

        // 3 off-screen Overloaded terminals + a Survivor edge chevron, all placed on the right edge by EdgePlacement.Place,
        // an Exit marker and a through-wall Survivor diamond on screen near the right edge; then the spread and the single
        // layout pass exactly as KillerWorldLayer runs them.
        public static void Crowded(Vector2 screen, Rect keepOut, float diamondOffset, float exitOffset)
        {
            string label = screen.x + "x" + screen.y + " diamond " + diamondOffset + " exit " + exitOffset;
            Rect safe = EdgePlacement.SafeRect(screen, false);
            var spread = new EdgeSpread();
            spread.Begin(safe, keepOut);
            Vector2[] alertDirections = { new Vector2(1, 0), new Vector2(1, -.02f), new Vector2(1, -.03f) };
            var pointing = new Vector2[4];
            for (int i = 0; i < 3; i++)
            {
                Vector2 edge = EdgePlacement.Place(screen, alertDirections[i].normalized, safe, keepOut, false, out pointing[i]);
                AddMarker(spread, edge, 2, -1 - i, 29 + i);
            }
            Vector2 chevron = EdgePlacement.Place(screen, new Vector2(1, -.01f).normalized, safe, keepOut, false, out pointing[3]);
            AddMarker(spread, chevron, 0, 5, 42);
            spread.Solve();
            AssertClean(spread, screen, safe, keepOut, label);

            var layout = new TagLayout();
            layout.Block(keepOut);
            // On-screen Survivor diamond (Survivor priority) with its distance tag 12 u below.
            Vector2 diamond = new Vector2(screen.x - 184, screen.y * .5f + diamondOffset);
            Vector2 diamondTagSize = MarkerLayer.TagSize("30 m", TagStyle.Text);
            int diamondItem = layout.Add(LayoutPriority.Survivor, TagLayout.Around(diamond, 10, 10), new Rect(diamond.x - diamondTagSize.x * .5f, diamond.y + 12, diamondTagSize.x, diamondTagSize.y));
            // Edge chevron (Edge priority) and the alerts (Overloaded priority, glyph = square + pointer as V2MarkerLayer).
            var spreadItem = new int[4];
            for (int i = 0; i < 4; i++)
            {
                bool alert = i < 3;
                Rect glyph = spread.GlyphRect(i);
                if (alert)
                {
                    Vector2 tip = spread.Glyph(i) + pointing[i].normalized * (8 + V2MarkerLayer.OverloadedHalf);
                    glyph = Rect.MinMaxRect(Mathf.Min(glyph.xMin, tip.x), Mathf.Min(glyph.yMin, tip.y), Mathf.Max(glyph.xMax, tip.x), Mathf.Max(glyph.yMax, tip.y));
                }
                spreadItem[i] = layout.Add(alert ? LayoutPriority.Overloaded : LayoutPriority.Edge, glyph, spread.TagShown(i) ? spread.Tag(i) : default);
            }
            // Exit marker near the right edge (Exit priority, glyph kept), tag 12 u above.
            Vector2 exit = new Vector2(screen.x - 124, screen.y * .5f + exitOffset);
            Vector2 exitTagSize = MarkerLayer.TagSize(ExitTag, TagStyle.Text);
            int exitItem = layout.Add(LayoutPriority.Exit, TagLayout.Around(exit, V2MarkerLayer.ExitHalf, V2MarkerLayer.ExitHalf),
                new Rect(exit.x - exitTagSize.x * .5f, exit.y - 12 - exitTagSize.y, exitTagSize.x, exitTagSize.y), true);
            layout.Solve(safe);

            var screenRect = new Rect(0, 0, screen.x, screen.y);
            var survivorGlyphs = new List<Rect> { layout[diamondItem].Glyph, layout[spreadItem[3]].Glyph };
            for (int i = 0; i < layout.Count; i++) Assert.That(layout[i].TagVisible, Is.True, label + ": tag " + i + " shown (everything must fit)");
            for (int i = 0; i < layout.Count; i++)
            {
                LayoutItem item = layout[i];
                Assert.That(item.GlyphVisible, Is.True, label + ": glyph " + i + " shown");
                Assert.That(Inside(item.Glyph, screenRect), Is.True, label + ": glyph " + i + " fully on screen " + item.Glyph);
                Rect placed = Placed(item);
                Assert.That(Inside(placed, safe), Is.True, label + ": tag " + i + " inside the safe rect " + placed);
                Assert.That(placed.Overlaps(keepOut), Is.False, label + ": tag " + i + " out of the cluster keep-out");
                foreach (Rect survivor in survivorGlyphs) Assert.That(placed.Overlaps(survivor), Is.False, label + ": tag " + i + " over a Survivor glyph");
                for (int j = i + 1; j < layout.Count; j++) Assert.That(placed.Overlaps(Placed(layout[j])), Is.False, label + ": tags " + i + " / " + j);
            }
            Assert.That(layout[exitItem].TagVisible, Is.True);
        }

        private static Rect Placed(LayoutItem item) => new Rect(item.Tag.x, item.Tag.y + item.TagShift, item.Tag.width, item.Tag.height);

#if !Q2_SCRATCH
        // ---- the wiring: rules v2 tags join the MarkerLayer solve ----------------------------------------------------------

        [Test]
        public void AnExternalV2TagAndAMarkerLayerEdgeTagOnTheSameRectEndUpApart()
        {
            var markers = new MarkerLayer(Resources.Load<Font>("Fonts/Inter-Regular"));
            Rect safe = EdgePlacement.SafeRect(Screen720, false), keepOut = KillerKeepOut(Screen720);
            markers.Clear(MarkerLayer.Static); markers.Clear(MarkerLayer.Signals);
            markers.Chevron(MarkerLayer.Signals, new Vector2(1576, 450), Vector2.right, false);
            Vector2 size = MarkerLayer.TagSize(Near, TagStyle.Text);
            Rect edgeTag = new Rect(1576 - 14 - size.x, 450 - size.y * .5f, size.x, size.y);
            markers.Tag(MarkerLayer.Signals, edgeTag.center, Near, TagStyle.Text, TagPivot.Center, LayoutPriority.Edge, true);
            int alert = markers.External(LayoutPriority.Overloaded, default, edgeTag, false);
            markers.Layout(safe, keepOut);
            LayoutItem result = markers.ExternalResult(alert);
            Assert.That(result.TagVisible, Is.True);
            Assert.That(result.TagShift, Is.Not.EqualTo(0f), "the Overloaded tag gives way to the Survivor edge tag");
            Assert.That(Placed(result).Overlaps(edgeTag), Is.False);
            markers.Clear(MarkerLayer.Static);
            Assert.That(() => markers.ExternalResult(0), Throws.Exception, "external items are cleared with the Static channel");
        }

        [Test]
        public void TheOverloadedPriorityIsBetweenEdgeAndExitAndItsGlyphIsNeverHidden()
        {
            Assert.That(LayoutPriority.Edge < LayoutPriority.Overloaded && LayoutPriority.Overloaded < LayoutPriority.Exit, Is.True);
            var layout = new TagLayout();
            int body = layout.Add(LayoutPriority.Survivor, new Rect(590, 280, 40, 90), default);
            int square = layout.Add(LayoutPriority.Overloaded, TagLayout.Around(new Vector2(610, 300), 9, 9), new Rect(560, 312, 100, 23));
            int exit = layout.Add(LayoutPriority.Exit, TagLayout.Around(new Vector2(612, 330), 13, 13), default, true);
            int terminal = layout.Add(LayoutPriority.Terminal, TagLayout.Around(new Vector2(610, 300), 9, 9), default);
            layout.Solve(new Rect(0, 0, 1600, 900));
            Assert.That(layout[body].GlyphVisible && layout[square].GlyphVisible, Is.True, "a Survivor body never hides the Overloaded square");
            Assert.That(layout[exit].GlyphVisible, Is.True, "the Exit marker is always shown (KeepGlyph)");
            Assert.That(layout[terminal].GlyphVisible, Is.False, "the Terminal square under the alert hides, as under a Survivor");
        }
#endif
    }
}
