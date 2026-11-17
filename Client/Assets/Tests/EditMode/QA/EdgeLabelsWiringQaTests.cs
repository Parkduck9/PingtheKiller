using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace PingTheKiller.Client.QA.Tests
{
    // Q2-edge-labels QA (attempt 1). The original bug (U6c-wire screens 5-killer-overloaded-exitmarker-720 /
    // a2-5-killer-overloaded-1080) was the off-screen Overloaded alert tag drawn over Survivor edge tags, and no exe run of
    // the implementer showed an Overloaded alert. These tests drive the REAL wiring objects (V2MarkerLayer + MarkerLayer +
    // EdgeSpread + TagLayout) through exactly the call sequence KillerWorldLayer.Render uses for one frame, with the only
    // replacements being: the 3D projection (screen directions are given directly to EdgePlacement.Place), the wire
    // TerminalFlagsView (built by hand), and the UI Toolkit text measurement (labels are not attached to a panel, so every
    // v2 tag is unmeasured and reserves the estimated size, the one-frame hidden rule). The Killer keep-out is the real one
    // (KillerHud.ClusterRect in normal play, expanded by EdgePlacement.KeepOut).
    public sealed class EdgeLabelsWiringQaTests
    {
        private static readonly Vector2 Panel720 = new Vector2(1600, 900), Panel1080 = new Vector2(1920, 1080);

        private struct Mark { public int Spread, V2Tag; public Vector2 Pointing; public string Text; }

        private sealed class Frame
        {
            public Vector2 Screen;
            public Rect Safe, KeepOut;
            public MarkerLayer Markers;
            public V2MarkerLayer V2;
            public EdgeSpread Spread = new EdgeSpread();
            public List<Mark> Marks = new List<Mark>();
            public List<Rect> V2Tags = new List<Rect>(), V2Reserved = new List<Rect>(), MarkerTags = new List<Rect>(), Glyphs = new List<Rect>();
            public int Dropped, V2Hidden;
        }

        private static Font TestFont => Resources.Load<Font>("Fonts/Inter-Regular");

        private static Rect KillerKeepOut(Vector2 screen)
        {
            var hud = new KillerHud(new VisualElement(), TestFont);
            hud.Begin(SkillIds.PingScanner);
            hud.SetIntro(false);
            return EdgePlacement.KeepOut(hud.ClusterRect(screen.y));
        }

        // One KillerWorldLayer.Render frame, from DrawStatic's off-screen Overloaded branch and DrawBlips' edge branch through
        // DrawEdges, the single layout pass and v2.ApplyLayout. alerts / chevrons: screen directions (y up) of the targets.
        private static Frame RunFrame(Vector2 screen, Vector2[] alerts, Vector2[] chevrons, Frame reuse = null)
        {
            Frame f = reuse ?? new Frame { Screen = screen, Markers = new MarkerLayer(TestFont), V2 = new V2MarkerLayer(TestFont) };
            f.Safe = EdgePlacement.SafeRect(screen, false);
            f.KeepOut = KillerKeepOut(screen);
            f.Markers.Clear(MarkerLayer.Static);
            f.Markers.Clear(MarkerLayer.Signals);
            f.V2.Begin();
            f.Spread.Begin(f.Safe, f.KeepOut);
            f.Marks.Clear();
            // DrawStatic: every alert terminal is off screen.
            for (int i = 0; i < alerts.Length; i++)
            {
                var flags = new TerminalFlagsView { Id = (byte)(3 + i), Overloaded = true };
                Assert.That(TerminalFlagCopy.ShowOverloaded(flags, Role.Killer), Is.True);
                float distance = 41 + i;
                string alert = f.V2.OverloadedTag(flags, distance);
                Vector2 edge = EdgePlacement.Place(screen, alerts[i].normalized, f.Safe, f.KeepOut, false, out Vector2 pointing);
                int tag = f.V2.OverloadedEdge(edge, pointing, alert);
                int item = f.Spread.Add(edge, new Vector2(V2MarkerLayer.OverloadedHalf, V2MarkerLayer.OverloadedHalf), f.V2.TagSize(tag),
                    EdgeTagMode.Below, LayoutPriority.Overloaded, -1 - flags.Id, distance);
                f.Marks.Add(new Mark { Spread = item, V2Tag = tag });
            }
            // DrawBlips: every chevron is an off-screen Survivor (handles start at 1, Normal status).
            for (int j = 0; j < chevrons.Length; j++)
            {
                float distance = 30 + j * 12;
                Vector2 c = EdgePlacement.Place(screen, chevrons[j].normalized, f.Safe, f.KeepOut, false, out Vector2 pointing);
                string text = KillerHudCopy.Distance(distance);
                float half = MarkerLayer.ChevronHalf(false);
                int item = f.Spread.Add(c, new Vector2(half, half), MarkerLayer.TagSize(text, TagStyle.Text), EdgeTagMode.Beside, LayoutPriority.Edge, 1 + j, distance);
                f.Marks.Add(new Mark { Spread = item, V2Tag = -1, Pointing = pointing, Text = text });
            }
            // DrawEdges.
            f.Spread.Solve();
            foreach (Mark mark in f.Marks)
            {
                Vector2 at = f.Spread.Glyph(mark.Spread);
                if (mark.V2Tag >= 0) { f.V2.PlaceEdge(mark.V2Tag, at, f.Spread.Tag(mark.Spread), f.Spread.TagShown(mark.Spread)); continue; }
                f.Markers.Chevron(MarkerLayer.Signals, at, mark.Pointing, false);
                if (f.Spread.TagShown(mark.Spread))
                    f.Markers.Tag(MarkerLayer.Signals, f.Spread.Tag(mark.Spread).center, mark.Text, TagStyle.Text, TagPivot.Center, LayoutPriority.Edge, true);
            }
            // The single layout pass.
            for (int i = 0; i < f.V2.LayoutCount; i++)
            {
                f.V2.LayoutInput(i, out LayoutPriority priority, out Rect glyph, out Rect tag, out bool keepGlyph);
                f.Markers.External(priority, glyph, tag, keepGlyph);
            }
            f.Markers.Layout(f.Safe, f.KeepOut);
            for (int i = 0; i < f.V2.LayoutCount; i++)
            {
                LayoutItem result = f.Markers.ExternalResult(i);
                f.V2.ApplyLayout(i, result.TagVisible, result.TagShift);
            }
            f.Markers.Commit(MarkerLayer.Static);
            f.Markers.Commit(MarkerLayer.Signals);
            f.V2.End();
            Collect(f);
            return f;
        }

        // The final rectangles: v2 tags = LayoutInput rect + ExternalResult shift; MarkerLayer tags rebuilt from the pooled
        // labels (left/top anchor, translate pivot, estimated size: exactly what MarkerLayer.Tag and Layout wrote).
        private static void Collect(Frame f)
        {
            f.V2Tags.Clear(); f.V2Reserved.Clear(); f.MarkerTags.Clear(); f.Glyphs.Clear(); f.Dropped = 0; f.V2Hidden = 0;
            for (int i = 0; i < f.Spread.Count; i++)
            {
                if (!f.Spread.TagShown(i)) f.Dropped++;
                f.Glyphs.Add(f.Spread.GlyphRect(i));
            }
            for (int i = 0; i < f.V2.LayoutCount; i++)
            {
                f.V2.LayoutInput(i, out _, out Rect glyph, out Rect tag, out _);
                LayoutItem result = f.Markers.ExternalResult(i);
                Assert.That(glyph.width, Is.GreaterThan(0), "v2 tag " + i + " has its square");
                f.Glyphs.Add(glyph);
                if (tag.width > 0) f.V2Reserved.Add(new Rect(tag.x, tag.y + result.TagShift, tag.width, tag.height));
                if (tag.width <= 0 || !result.TagVisible) { f.V2Hidden++; continue; }
                f.V2Tags.Add(new Rect(tag.x, tag.y + result.TagShift, tag.width, tag.height));
            }
            foreach (VisualElement child in f.Markers.Children())
            {
                if (!(child is Label label) || label.style.display == DisplayStyle.None) continue;
                Vector2 size = MarkerLayer.TagSize(label.text, label.ClassListContains("tag-lagging") ? TagStyle.Lagging : label.ClassListContains("tag-muted") ? TagStyle.Muted : TagStyle.Text);
                float left = label.style.left.value.value, top = label.style.top.value.value;
                float pivot = label.style.translate.value.y.value;   // percent: 0 below, -50 centre, -100 above
                f.MarkerTags.Add(new Rect(left - size.x * .5f, top + size.y * pivot / 100f, size.x, size.y));
            }
        }

        private static bool Inside(Rect inner, Rect outer, float eps = 1e-3f)
            => inner.xMin >= outer.xMin - eps && inner.yMin >= outer.yMin - eps && inner.xMax <= outer.xMax + eps && inner.yMax <= outer.yMax + eps;

        // pendingF01: skip the two assertions the F-01 rounding defect fails (reserved rect inside the safe rect, alert tag
        // not hidden) and check the rest of the frame with the reserved alert rectangles (0.5 u tolerance at the edge).
        private static void AssertReadable(Frame f, int alerts, int chevrons, string label, bool pendingF01 = false)
        {
            var screenRect = new Rect(0, 0, f.Screen.x, f.Screen.y);
            Assert.That(f.Dropped, Is.EqualTo(0), label + ": the spread dropped a tag");
            Assert.That(f.V2.LayoutCount, Is.EqualTo(alerts), label + ": one v2 tag per alert");
            // The rectangle the alert reserves in the layout must itself be inside the safe rect, or TagLayout.Free rejects
            // every candidate and the alert tag is hidden (PlaceEdge rounds x but keeps the fractional width).
            for (int i = 0; i < f.V2.LayoutCount && !pendingF01; i++)
            {
                f.V2.LayoutInput(i, out _, out _, out Rect reserved, out _);
                Assert.That(Inside(reserved, f.Safe, 0), Is.True, label + ": reserved v2 rect " + i + " inside the safe rect " + f.Safe + ": " + reserved + " (xMax " + reserved.xMax + ")");
            }
            if (!pendingF01) Assert.That(f.V2Hidden, Is.EqualTo(0), label + ": the layout hid an Overloaded tag");
            Assert.That((pendingF01 ? f.V2Reserved : f.V2Tags).Count, Is.EqualTo(alerts), label);
            Assert.That(f.MarkerTags.Count, Is.EqualTo(chevrons), label + ": every chevron keeps its distance tag");
            foreach (string text in f.V2.Tags) Assert.That(text, Does.Contain("overloaded · "), label + ": alert copy");
            var all = new List<Rect>(pendingF01 ? f.V2Reserved : f.V2Tags); all.AddRange(f.MarkerTags);
            for (int a = 0; a < all.Count; a++)
            {
                Assert.That(Inside(all[a], f.Safe, pendingF01 ? .5f : 1e-3f), Is.True, label + ": tag " + a + " inside the safe rect " + all[a]);
                Assert.That(all[a].Overlaps(f.KeepOut), Is.False, label + ": tag " + a + " clear of the cluster keep-out");
                for (int b = a + 1; b < all.Count; b++) Assert.That(all[a].Overlaps(all[b]), Is.False, label + ": tags " + a + " / " + b + " overlap: " + all[a] + " vs " + all[b]);
                // A tag never covers a Survivor chevron (the bug's other half: the alert tag over an edge chevron).
                for (int g = 0; g < f.Spread.Count; g++)
                    if (f.Spread.HasTag(g) && f.Spread.Side(g) == EdgeSide.Right && f.Marks[g].V2Tag < 0)
                        Assert.That(all[a].Overlaps(f.Spread.GlyphRect(g)), Is.False, label + ": tag " + a + " covers chevron " + g);
            }
            for (int g = 0; g < f.Glyphs.Count; g++)
            {
                Rect glyph = f.Glyphs[g];
                Rect visible = Rect.MinMaxRect(Mathf.Max(glyph.xMin, 0), Mathf.Max(glyph.yMin, 0), Mathf.Min(glyph.xMax, f.Screen.x), Mathf.Min(glyph.yMax, f.Screen.y));
                Assert.That(visible.width >= 16 && visible.height >= 16, Is.True, label + ": glyph " + g + " on screen " + glyph);
            }
            // The v2 square really moved to the spread point (PlaceEdge wrote the shape, LayoutInput read it back).
            for (int m = 0; m < f.Marks.Count; m++)
            {
                if (f.Marks[m].V2Tag < 0) continue;
                f.V2.LayoutInput(f.Marks[m].V2Tag, out _, out Rect glyph, out _, out _);
                Assert.That(glyph.Contains(f.Spread.Glyph(f.Marks[m].Spread)), Is.True, label + ": v2 square " + m + " at the spread point");
            }
            // Same-edge footprints keep the 4 u gap.
            for (int i = 0; i < f.Spread.Count; i++)
                for (int j = i + 1; j < f.Spread.Count; j++)
                {
                    if (f.Spread.Side(i) != f.Spread.Side(j)) continue;
                    Rect a = f.Spread.Footprint(i), b = f.Spread.Footprint(j);
                    float gap = Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax);
                    Assert.That(gap, Is.GreaterThanOrEqualTo(EdgeSpread.Gap - 1e-3f), label + ": footprints " + i + " / " + j);
                }
        }

        // All targets a little right of the camera, so every marker lands near the middle of the right edge (ideal points
        // within a few units of each other, the U6c screenshots' situation).
        private static readonly Vector2[] OneAlert = { new Vector2(1, 0) };
        private static readonly Vector2[] ThreeAlerts = { new Vector2(1, 0), new Vector2(1, -.02f), new Vector2(1, .03f) };
        private static readonly Vector2[] TwoChevrons = { new Vector2(1, -.01f), new Vector2(1, .005f) };
        private static readonly Vector2[] ThreeChevrons = { new Vector2(1, -.01f), new Vector2(1, .005f), new Vector2(1, .02f) };

        [Test]
        public void OneOffScreenOverloadedAlertAndTwoSurvivorChevronsOnTheRightEdgeNeverOverlap_RealWiring()
        {
            AssertReadable(RunFrame(Panel720, OneAlert, TwoChevrons), 1, 2, "720p 1+2");
            AssertReadable(RunFrame(Panel1080, OneAlert, TwoChevrons), 1, 2, "1080p 1+2");
        }

        [Test]
        public void ThreeOffScreenOverloadedAlertsAndTwoSurvivorChevronsOnTheRightEdgeNeverOverlap_RealWiring()
        {
            AssertReadable(RunFrame(Panel720, ThreeAlerts, TwoChevrons), 3, 2, "720p 3+2");
            AssertReadable(RunFrame(Panel1080, ThreeAlerts, TwoChevrons), 3, 2, "1080p 3+2");
        }

        [Test]
        public void ThreeAlertsAndThreeChevronsStillFitAndTheSecondFrameReusesTheSameSlots_RealWiring()
        {
            Frame f = RunFrame(Panel720, ThreeAlerts, ThreeChevrons);
            AssertReadable(f, 3, 3, "720p 3+3 frame 1");
            var first = new List<Rect>(f.V2Tags); first.AddRange(f.MarkerTags);
            // Frame 2 with the same inputs on the same (pooled) layers: identical output, no tag flickers.
            RunFrame(Panel720, ThreeAlerts, ThreeChevrons, f);
            AssertReadable(f, 3, 3, "720p 3+3 frame 2");
            var second = new List<Rect>(f.V2Tags); second.AddRange(f.MarkerTags);
            Assert.That(second, Is.EqualTo(first), "the second frame places every tag where the first did");
        }

        // Everything except the F-01 rounding defect: with the alerts' reserved rectangles in place of the (hidden) visible
        // ones, the spread and the single layout pass keep every tag apart, inside the safe rect and out of the keep-out at
        // both resolutions. Evidence that the fix for F-01 is the rounding alone.
        [Test]
        public void ApartFromTheF01RoundingTheCrowdedRightEdgeIsCleanAtBothResolutions_RealWiring()
        {
            AssertReadable(RunFrame(Panel720, OneAlert, TwoChevrons), 1, 2, "720p 1+2", true);
            AssertReadable(RunFrame(Panel1080, OneAlert, TwoChevrons), 1, 2, "1080p 1+2", true);
            AssertReadable(RunFrame(Panel720, ThreeAlerts, TwoChevrons), 3, 2, "720p 3+2", true);
            AssertReadable(RunFrame(Panel1080, ThreeAlerts, TwoChevrons), 3, 2, "1080p 3+2", true);
            AssertReadable(RunFrame(Panel720, ThreeAlerts, ThreeChevrons), 3, 3, "720p 3+3", true);
            AssertReadable(RunFrame(Panel1080, ThreeAlerts, ThreeChevrons), 3, 3, "1080p 3+3", true);
        }

        [Test]
        public void AnUnmeasuredV2TagStaysHiddenForTheFrameButStillReservesItsPlace()
        {
            // Labels detached from a panel never measure: the one-frame hidden rule applies to every alert, yet the chevron
            // tags are still laid out around the reserved rectangles (nothing overlaps the hidden alert's place).
            Frame f = RunFrame(Panel720, ThreeAlerts, TwoChevrons);
            int hidden = 0;
            foreach (VisualElement child in f.V2.Children())
                if (child is Label label && label.style.visibility == Visibility.Hidden) hidden++;
            Assert.That(hidden, Is.EqualTo(3), "unmeasured alerts are hidden this frame");
            AssertReadable(f, 3, 2, "720p reserved");
        }

        // ---- the pure paths allocate nothing after the first frame ----------------------------------------------------------

        [Test]
        public void EdgeSpreadSolveAllocatesNoGcMemoryAfterWarmUp()
        {
            Rect safe = EdgePlacement.SafeRect(Panel720, false), keepOut = KillerKeepOut(Panel720);
            var spread = new EdgeSpread();
            Vector2 alertTag = MarkerLayer.TagSize("Terminal 3 overloaded · 41 m", TagStyle.Text), chevronTag = MarkerLayer.TagSize("42 m", TagStyle.Text);
            void Fill(float drift)
            {
                spread.Begin(safe, keepOut);
                for (int i = 0; i < 6; i++)
                    spread.Add(new Vector2(safe.xMax, 440 + i * 3 + drift), new Vector2(9, 9), alertTag, EdgeTagMode.Below, LayoutPriority.Overloaded, -1 - i, 30 + i);
                for (int i = 0; i < 6; i++)
                    spread.Add(new Vector2(safe.xMin, 300 + i * 2 - drift), new Vector2(10, 10), chevronTag, EdgeTagMode.Beside, LayoutPriority.Edge, 1 + i, 20 + i);
                for (int i = 0; i < 20; i++)
                    spread.Add(new Vector2(600 + i * 5, safe.yMin), new Vector2(10, 10), chevronTag, EdgeTagMode.Beside, LayoutPriority.Edge, 100 + i, 50 + i);
            }
            Fill(0); spread.Solve();
            Fill(.4f); spread.Solve();
            Assert.That(() => { Fill(.8f); spread.Solve(); }, Is.Not.AllocatingGCMemory(), "the third frame of the spread");
        }

        // GC.Alloc profiler samples during one action on this thread (the same measurement Is.AllocatingGCMemory uses).
        private static int GcAllocs(System.Action action)
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            Profiler.BeginSample("q2-gc");
            action();
            Profiler.EndSample();
            recorder.enabled = false;
            recorder.CollectFromAllThreads();
            return recorder.sampleBlockCount;
        }

        // The Q2 slot path (Overloaded / Exit tags, KeepGlyph) adds no allocation over the pre-Q2 solve. The pre-Q2 solve's
        // own count is reported (TagLayout.Solve calls List.Sort(Comparison), which allocates a comparer wrapper on Mono).
        [Test]
        public void TagLayoutSolveWithSlottedV2TagsAllocatesNoMoreThanThePreQ2Solve()
        {
            Rect safe = EdgePlacement.SafeRect(Panel720, false), keepOut = KillerKeepOut(Panel720);
            var layout = new TagLayout();
            // Ten items, added in the solved order (priority, then glyph y, then x) in both variants, so the sort makes the
            // same number of comparisons for both and any difference comes from the slot path alone. (Each comparison boxes
            // twice: TagLayout.Compare calls LayoutPriority.CompareTo(object), which is pre-Q2 and reported, not failed.)
            void Fill(bool q2)
            {
                layout.Clear();
                layout.Block(keepOut);
                for (int i = 0; i < 4; i++) layout.Add(LayoutPriority.Survivor, TagLayout.Around(new Vector2(800 + i * 30, 400 + i), 10, 10), new Rect(780 + i * 30, 412 + i, 40, 23));
                for (int i = 0; i < 2; i++) layout.Add(LayoutPriority.Edge, TagLayout.Around(new Vector2(1576, 430 + i * 30), 10, 10), new Rect(1520, 420 + i * 30, 40, 23));
                if (q2)
                {
                    for (int i = 0; i < 2; i++) layout.Add(LayoutPriority.Overloaded, TagLayout.Around(new Vector2(1576, 530 + i * 5), 9, 9), new Rect(1376, 542 + i * 5, 200, 23));
                    for (int i = 0; i < 2; i++) layout.Add(LayoutPriority.Exit, TagLayout.Around(new Vector2(1500, 600 + i * 3), 13, 13), new Rect(1440, 565 + i * 3, 120, 23), true);
                }
                else
                    for (int i = 0; i < 4; i++) layout.Add(LayoutPriority.Terminal, TagLayout.Around(new Vector2(700 + i * 12, 500), 9, 9), new Rect(670 + i * 12, 512, 70, 23));
            }
            Fill(false); layout.Solve(safe);
            Fill(false); layout.Solve(safe);
            int baseline = int.MaxValue; for (int r = 0; r < 5; r++) baseline = Mathf.Min(baseline, GcAllocs(() => { Fill(false); layout.Solve(safe); })); // minimum of 5: a stray editor allocation in a long run must not decide the comparison
            Fill(true); layout.Solve(safe);
            Fill(true); layout.Solve(safe);
            int q2 = int.MaxValue; for (int r = 0; r < 5; r++) q2 = Mathf.Min(q2, GcAllocs(() => { Fill(true); layout.Solve(safe); }));
            TestContext.Out.WriteLine("TagLayout.Solve GC.Alloc samples per frame (10 items): pre-Q2 priorities " + baseline + ", with Overloaded/Exit items " + q2);
            Assert.That(q2, Is.EqualTo(baseline), "the Q2 slot path allocates (pre-Q2 solve: " + baseline + " sample(s), with v2 tags: " + q2 + ")");
        }

        // ---- the Terminal priority stays fuzzed after the Q2 renumbering --------------------------------------------------------

        // TagLayoutQaTests' random items use rng.Next(4), which since Q2 (Overloaded = 2, Exit = 3, Terminal = 4) never yields
        // Terminal. This fuzz covers all five priorities and KeepGlyph, with the brief's invariants.
        [Test]
        public void RandomLayoutsOverAllFivePrioritiesKeepTheInvariants()
        {
            var screen = new Rect(24, 24, 1552, 852);
            var rng = new System.Random(20261109);
            var layout = new TagLayout();
            int terminals = 0, keptExits = 0;
            for (int round = 0; round < 300; round++)
            {
                layout.Clear();
                int n = 2 + rng.Next(14);
                for (int i = 0; i < n; i++)
                {
                    var priority = (LayoutPriority)rng.Next(5);
                    if (priority == LayoutPriority.Terminal) terminals++;
                    float x = 40 + (float)rng.NextDouble() * 1500, y = 40 + (float)rng.NextDouble() * 800;
                    bool glyph = rng.Next(4) != 0, tag = !glyph || rng.Next(5) != 0;
                    bool keep = priority == LayoutPriority.Exit && rng.Next(2) == 0;
                    if (keep) keptExits++;
                    layout.Add(priority, glyph ? TagLayout.Around(new Vector2(x, y), 8 + rng.Next(3) * 2, 8 + rng.Next(3) * 2) : default,
                        tag ? new Rect(x - 25 - rng.Next(60), y + 12, 50 + rng.Next(150), 23) : default, keep);
                }
                layout.Solve(screen);
                string context = "round " + round;
                for (int i = 0; i < layout.Count; i++)
                {
                    LayoutItem item = layout[i];
                    if (item.HasGlyph && (item.Priority < LayoutPriority.Exit || item.KeepGlyph))
                        Assert.That(item.GlyphVisible, Is.True, context + ": a Survivor, Edge, Overloaded or kept Exit glyph is never hidden (item " + i + ")");
                    if (!item.HasTag) Assert.That(item.TagVisible, Is.False, context);
                    if (item.HasGlyph && !item.GlyphVisible) Assert.That(item.TagVisible, Is.False, context + ": a hidden glyph takes its tag with it");
                    if (!item.TagVisible) continue;
                    var placed = new Rect(item.Tag.x, item.Tag.y + item.TagShift, item.Tag.width, item.Tag.height);
                    Assert.That(Inside(placed, screen), Is.True, context + ": tag " + i + " inside the bounds");
                    for (int j = 0; j < layout.Count; j++)
                    {
                        LayoutItem other = layout[j];
                        if (j != i && other.TagVisible)
                            Assert.That(placed.Overlaps(new Rect(other.Tag.x, other.Tag.y + other.TagShift, other.Tag.width, other.Tag.height)), Is.False, context + ": tags " + i + " / " + j);
                        if (other.GlyphVisible && other.Priority <= item.Priority)
                            Assert.That(placed.Overlaps(other.Glyph), Is.False, context + ": tag " + i + " covers glyph " + j);
                    }
                    if (item.HasGlyph && item.Priority >= LayoutPriority.Exit && !item.KeepGlyph)
                        for (int j = 0; j < layout.Count; j++)
                            if (layout[j].GlyphVisible && layout[j].Priority < item.Priority)
                                Assert.That(item.Glyph.Overlaps(layout[j].Glyph), Is.False, context + ": glyph " + i + " covers the higher glyph " + j);
                }
            }
            Assert.That(terminals, Is.GreaterThan(100), "Terminal items were generated");
            Assert.That(keptExits, Is.GreaterThan(30), "KeepGlyph Exits were generated");
        }
    }
}
