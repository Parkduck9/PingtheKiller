using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 2, test item 2. Written from docs/briefs/U4c-a2.md fix 1 (F-01, F-02, F-04): a pure screen-space layout
    // pass. No two tags overlap; a colliding lower-priority tag is stacked by tag height + TagGap (4 u since 11/10; alternating below/above, up to
    // 3 steps) and then hidden; priority is Survivor > Edge > Exit > Terminal; a Survivor glyph is never hidden; a lower-priority
    // glyph or tag never covers a higher-priority glyph; the result is deterministic and the input order carries nothing.
    [Category("U4cQaA2")]
    public sealed class TagLayoutQaTests
    {
        private const float TagW = 60, TagH = 23, Step = TagH + TagLayout.TagGap;
        private static readonly Rect Screen = new Rect(0, 0, 1920, 1080);
        private static readonly Rect None = default;

        private static Rect Tag(float x, float y) => new Rect(x, y, TagW, TagH);
        private static Rect Glyph(float cx, float cy, float half = 10) => TagLayout.Around(new Vector2(cx, cy), half, half);
        private static Rect Placed(LayoutItem item) => new Rect(item.Tag.x, item.Tag.y + item.TagShift, item.Tag.width, item.Tag.height);

        private static TagLayout Solved(params (LayoutPriority priority, Rect glyph, Rect tag)[] items)
        {
            var layout = new TagLayout();
            foreach (var item in items) layout.Add(item.priority, item.glyph, item.tag);
            layout.Solve(Screen);
            return layout;
        }

        // The invariants of the brief, over any solved layout.
        private static void AssertValid(TagLayout layout, Rect bounds, string context)
        {
            var visibleTags = new List<int>();
            for (int i = 0; i < layout.Count; i++)
            {
                LayoutItem item = layout[i];
                if (item.HasTag && item.TagVisible) visibleTags.Add(i);
                if (item.HasGlyph && item.Priority < LayoutPriority.Exit)
                    Assert.That(item.GlyphVisible, Is.True, context + ": a Survivor or Edge glyph is never hidden (item " + i + ")");
                if (!item.HasGlyph) Assert.That(item.GlyphVisible, Is.False, context + ": no glyph, nothing to show");
                if (!item.HasTag) Assert.That(item.TagVisible, Is.False, context + ": no tag, nothing to show");
            }
            for (int a = 0; a < visibleTags.Count; a++)
            {
                Rect ra = Placed(layout[visibleTags[a]]);
                Assert.That(ra.xMin >= bounds.xMin && ra.yMin >= bounds.yMin && ra.xMax <= bounds.xMax && ra.yMax <= bounds.yMax, Is.True, context + ": tag " + visibleTags[a] + " stays inside the screen");
                for (int b = a + 1; b < visibleTags.Count; b++)
                    Assert.That(ra.Overlaps(Placed(layout[visibleTags[b]])), Is.False, context + ": tags " + visibleTags[a] + " and " + visibleTags[b] + " overlap");
                // A tag never covers a visible glyph of a strictly higher priority.
                for (int g = 0; g < layout.Count; g++)
                {
                    LayoutItem glyphItem = layout[g];
                    if (!glyphItem.GlyphVisible || glyphItem.Priority >= layout[visibleTags[a]].Priority) continue;
                    Assert.That(ra.Overlaps(glyphItem.Glyph), Is.False, context + ": tag " + visibleTags[a] + " (" + layout[visibleTags[a]].Priority + ") covers the " + glyphItem.Priority + " glyph " + g);
                }
            }
            // A lower-priority glyph never covers a visible higher-priority glyph.
            for (int lo = 0; lo < layout.Count; lo++)
            {
                if (!layout[lo].GlyphVisible) continue;
                for (int hi = 0; hi < layout.Count; hi++)
                    if (layout[lo].Priority >= LayoutPriority.Exit && layout[hi].GlyphVisible && layout[hi].Priority < layout[lo].Priority)
                        Assert.That(layout[lo].Glyph.Overlaps(layout[hi].Glyph), Is.False, context + ": " + layout[lo].Priority + " glyph " + lo + " covers the " + layout[hi].Priority + " glyph " + hi);
            }
        }

        // ---- two coincident tags end up disjoint ------------------------------------------------------------------------------

        [Test]
        public void TwoCoincidentTagsEndUpDisjointAndExactlyOneOfThemMoves()
        {
            TagLayout layout = Solved((LayoutPriority.Survivor, None, Tag(500, 500)), (LayoutPriority.Survivor, None, Tag(500, 500)));
            Assert.That(layout[0].TagVisible && layout[1].TagVisible, Is.True, "one step is enough, nothing is hidden");
            Assert.That(Placed(layout[0]).Overlaps(Placed(layout[1])), Is.False);
            Assert.That((layout[0].TagShift == 0) ^ (layout[1].TagShift == 0), Is.True, "exactly one tag moves");
            AssertValid(layout, Screen, "two coincident");
        }

        [Test]
        public void ADisplacedTagMovesByExactlyTheTagHeightPlusTheTagGap()
        {
            TagLayout layout = Solved((LayoutPriority.Survivor, None, Tag(500, 500)), (LayoutPriority.Terminal, None, Tag(500, 500)));
            Assert.That(layout[0].TagShift, Is.EqualTo(0f), "the Survivor tag stays");
            Assert.That(Mathf.Abs(layout[1].TagShift), Is.EqualTo(Step).Within(1e-4f), "tag height 23 + 4 u gap");
        }

        [Test]
        public void TagsThatDoNotCollideNeverMove()
        {
            TagLayout layout = Solved(
                (LayoutPriority.Survivor, None, Tag(100, 100)), (LayoutPriority.Edge, None, Tag(300, 100)),
                (LayoutPriority.Exit, None, Tag(100, 400)), (LayoutPriority.Terminal, None, Tag(300, 400)));
            for (int i = 0; i < layout.Count; i++)
            {
                Assert.That(layout[i].TagVisible, Is.True);
                Assert.That(layout[i].TagShift, Is.EqualTo(0f), "item " + i);
            }
        }

        [Test]
        public void TagsExactlyTagGapApartDoNotMove()
        {
            // 11/10: touching is no longer free, a tag keeps TagGap clear; exactly TagGap apart does not move.
            TagLayout layout = Solved((LayoutPriority.Survivor, None, Tag(100, 100)), (LayoutPriority.Survivor, None, Tag(100, 100 + TagH + TagLayout.TagGap)));
            Assert.That(layout[0].TagShift, Is.EqualTo(0f));
            Assert.That(layout[1].TagShift, Is.EqualTo(0f));
        }

        // ---- priority decides who moves ---------------------------------------------------------------------------------------

        [Test]
        public void ASurvivorTagKeepsItsPlaceAgainstEveryOtherPriorityWhateverTheOrderTheyWereAdded()
        {
            LayoutPriority[] others = { LayoutPriority.Edge, LayoutPriority.Exit, LayoutPriority.Terminal };
            foreach (LayoutPriority other in others)
                foreach (bool survivorFirst in new[] { true, false })
                {
                    var layout = new TagLayout();
                    int s, o;
                    if (survivorFirst) { s = layout.Add(LayoutPriority.Survivor, None, Tag(700, 300)); o = layout.Add(other, None, Tag(700, 300)); }
                    else { o = layout.Add(other, None, Tag(700, 300)); s = layout.Add(LayoutPriority.Survivor, None, Tag(700, 300)); }
                    layout.Solve(Screen);
                    Assert.That(layout[s].TagShift, Is.EqualTo(0f), "Survivor vs " + other + (survivorFirst ? " (survivor first)" : " (survivor last)"));
                    Assert.That(layout[o].TagShift, Is.Not.EqualTo(0f), other + " must give way");
                }
        }

        [Test]
        public void AllFourPrioritiesOnOnePointStackInPriorityOrderAndAreDisjoint()
        {
            TagLayout layout = Solved(
                (LayoutPriority.Terminal, None, Tag(900, 500)), (LayoutPriority.Exit, None, Tag(900, 500)),
                (LayoutPriority.Edge, None, Tag(900, 500)), (LayoutPriority.Survivor, None, Tag(900, 500)));
            LayoutItem survivor = layout[3], edge = layout[2], exit = layout[1], terminal = layout[0];
            Assert.That(survivor.TagShift, Is.EqualTo(0f));
            foreach (LayoutItem item in new[] { survivor, edge, exit, terminal })
            {
                Assert.That(item.TagVisible, Is.True, item.Priority + " fits within 3 steps");
                float steps = item.TagShift / Step;
                Assert.That(Mathf.Abs(steps - Mathf.Round(steps)), Is.LessThan(1e-3f), item.Priority + " moves by whole steps");
                Assert.That(Mathf.Abs(Mathf.Round(steps)), Is.LessThanOrEqualTo(3), item.Priority + " moves at most 3 steps");
            }
            Assert.That(Mathf.Abs(edge.TagShift), Is.LessThanOrEqualTo(Mathf.Abs(exit.TagShift) + 1e-3f), "Edge moves no further than Exit");
            Assert.That(Mathf.Abs(exit.TagShift), Is.LessThanOrEqualTo(Mathf.Abs(terminal.TagShift) + 1e-3f), "Exit moves no further than Terminal");
            AssertValid(layout, Screen, "four priorities");
        }

        [Test]
        public void TheStackAlternatesBelowAndAbove()
        {
            // Four same-priority tags on one point use the four places 0, one step one way, one step the other way, then further.
            TagLayout layout = Solved(
                (LayoutPriority.Survivor, None, Tag(900, 500)), (LayoutPriority.Survivor, None, Tag(900, 500)),
                (LayoutPriority.Survivor, None, Tag(900, 500)), (LayoutPriority.Survivor, None, Tag(900, 500)));
            var shifts = new List<float>();
            for (int i = 0; i < 4; i++) { Assert.That(layout[i].TagVisible, Is.True); shifts.Add(layout[i].TagShift); }
            shifts.Sort();
            Assert.That(shifts[0], Is.LessThan(0f), "at least one tag went above");
            Assert.That(shifts[3], Is.GreaterThan(0f), "at least one tag went below");
            Assert.That(shifts[1], Is.EqualTo(0f).Within(1e-4f).Or.EqualTo(-Step).Within(1e-4f), "the first alternate is one step");
            AssertValid(layout, Screen, "alternating");
        }

        // ---- the step limit leads to a hide -------------------------------------------------------------------------------------

        [Test]
        public void AfterThreeStepsTheLowerPriorityTagIsHiddenAndTheHigherOnesAllStay()
        {
            var items = new List<(LayoutPriority, Rect, Rect)>();
            for (int i = 0; i < 4; i++) items.Add((LayoutPriority.Survivor, None, Tag(900, 500)));
            items.Add((LayoutPriority.Terminal, None, Tag(900, 500)));
            TagLayout layout = Solved(items.ToArray());
            for (int i = 0; i < 4; i++) Assert.That(layout[i].TagVisible, Is.True, "Survivor tag " + i + " (the 4 places 0, +1, -1, +2 steps are enough)");
            Assert.That(layout[4].TagVisible, Is.False, "the fifth tag finds no place within 3 steps and is hidden");
            AssertValid(layout, Screen, "step limit");
        }

        [Test]
        public void ManySameLevelTagsHideOnlyTheOnesThatDoNotFit()
        {
            var items = new List<(LayoutPriority, Rect, Rect)>();
            for (int i = 0; i < 9; i++) items.Add((LayoutPriority.Terminal, None, Tag(900, 500)));
            TagLayout layout = Solved(items.ToArray());
            int shown = 0;
            for (int i = 0; i < layout.Count; i++) if (layout[i].TagVisible) shown++;
            Assert.That(shown, Is.EqualTo(4), "the point itself plus three steps");
            AssertValid(layout, Screen, "nine terminal tags");
        }

        [Test]
        public void ATagThatWouldLeaveTheScreenIsMovedOrHiddenNeverClipped()
        {
            TagLayout layout = Solved((LayoutPriority.Survivor, None, Tag(1900, 500)), (LayoutPriority.Terminal, None, Tag(-20, 500)),
                (LayoutPriority.Survivor, None, Tag(500, 1070)), (LayoutPriority.Survivor, None, Tag(500, -10)));
            for (int i = 0; i < layout.Count; i++)
                if (layout[i].TagVisible)
                {
                    Rect r = Placed(layout[i]);
                    Assert.That(r.xMin >= 0 && r.yMin >= 0 && r.xMax <= 1920 && r.yMax <= 1080, Is.True, "tag " + i + " " + r);
                }
        }

        [Test]
        public void ATagNeverEntersABlockedRectangle()
        {
            var layout = new TagLayout();
            Rect cluster = new Rect(24, 688, 320, 368);
            layout.Block(cluster);
            layout.Add(LayoutPriority.Edge, None, Tag(100, 700));
            layout.Add(LayoutPriority.Survivor, None, Tag(200, 800));
            layout.Add(LayoutPriority.Terminal, None, Tag(300, 650));
            layout.Solve(Screen);
            for (int i = 0; i < layout.Count; i++)
                if (layout[i].TagVisible) Assert.That(Placed(layout[i]).Overlaps(cluster), Is.False, "tag " + i);
        }

        // ---- glyphs --------------------------------------------------------------------------------------------------------------

        [Test]
        public void ASurvivorGlyphIsNeverHiddenNoMatterWhatStandsOnIt()
        {
            var items = new List<(LayoutPriority, Rect, Rect)> { (LayoutPriority.Survivor, Glyph(800, 500), Tag(780, 520)) };
            for (int i = 0; i < 6; i++) items.Add((LayoutPriority.Terminal, Glyph(800 + i, 500), Tag(780, 520)));
            for (int i = 0; i < 3; i++) items.Add((LayoutPriority.Exit, Glyph(800, 500 + i), Tag(780, 520)));
            for (int i = 0; i < 3; i++) items.Add((LayoutPriority.Edge, Glyph(800, 500), Tag(780, 520)));
            for (int i = 0; i < 3; i++) items.Add((LayoutPriority.Survivor, Glyph(800, 500), Tag(780, 520)));
            TagLayout layout = Solved(items.ToArray());
            for (int i = 0; i < layout.Count; i++)
                if (layout[i].Priority <= LayoutPriority.Edge) Assert.That(layout[i].GlyphVisible, Is.True, layout[i].Priority + " glyph " + i);
            AssertValid(layout, Screen, "pile of glyphs");
        }

        [Test]
        public void AnExitOrTerminalGlyphOnASurvivorGlyphIsHiddenTogetherWithItsTag()
        {
            TagLayout layout = Solved(
                (LayoutPriority.Survivor, Glyph(800, 500), Tag(780, 520)),
                (LayoutPriority.Terminal, Glyph(805, 505, 9), Tag(780, 560)),
                (LayoutPriority.Exit, Glyph(795, 495), Tag(780, 600)));
            Assert.That(layout[0].GlyphVisible, Is.True);
            Assert.That(layout[1].GlyphVisible, Is.False, "the Terminal square covered a Survivor marker");
            Assert.That(layout[2].GlyphVisible, Is.False, "the Exit marker covered a Survivor marker");
            Assert.That(layout[1].TagVisible, Is.False, "the tag of a hidden mark is hidden too");
            Assert.That(layout[2].TagVisible, Is.False);
        }

        [Test]
        public void ATerminalGlyphThatCoversNothingStaysAndKeepsItsTag()
        {
            TagLayout layout = Solved((LayoutPriority.Survivor, Glyph(100, 100), Tag(80, 120)), (LayoutPriority.Terminal, Glyph(900, 500, 9), Tag(870, 515)));
            Assert.That(layout[1].GlyphVisible, Is.True);
            Assert.That(layout[1].TagVisible, Is.True);
            Assert.That(layout[1].TagShift, Is.EqualTo(0f));
        }

        [Test]
        public void ASurvivorBodyRectangleIsProtectedLikeAGlyph()
        {
            // F-04: a Terminal square and its tag standing on a Survivor body.
            Rect body = Rect.MinMaxRect(900, 400, 940, 520);
            TagLayout layout = Solved((LayoutPriority.Survivor, body, None), (LayoutPriority.Terminal, Glyph(920, 450, 9), Tag(890, 470)));
            Assert.That(layout[0].GlyphVisible, Is.True);
            Assert.That(layout[1].GlyphVisible, Is.False);
            Assert.That(layout[1].TagVisible, Is.False);
            AssertValid(layout, Screen, "body");
        }

        [Test]
        public void AChevronsDistanceTagNeverCoversADiamond()
        {
            // F-02: the chevron (Edge) tag lands on a through-wall diamond (Survivor). The diamond's own tag sits under it.
            Rect diamond = Glyph(1500, 500, 10);
            TagLayout layout = Solved(
                (LayoutPriority.Survivor, diamond, Tag(1470, 515)),
                (LayoutPriority.Edge, Glyph(1560, 500, 10), Tag(1470, 488)));
            Assert.That(layout[0].GlyphVisible, Is.True, "the diamond is drawn");
            if (layout[1].TagVisible) Assert.That(Placed(layout[1]).Overlaps(diamond), Is.False, "the chevron tag is not on the diamond");
            if (layout[1].TagVisible && layout[0].TagVisible) Assert.That(Placed(layout[1]).Overlaps(Placed(layout[0])), Is.False);
            AssertValid(layout, Screen, "chevron tag over diamond");
        }

        [Test]
        public void ALowerPriorityTagIsNeverLeftOnAHigherPriorityGlyph()
        {
            Rect glyph = Glyph(300, 300, 12);
            TagLayout layout = Solved((LayoutPriority.Survivor, glyph, None), (LayoutPriority.Terminal, None, Tag(280, 290)), (LayoutPriority.Edge, None, Tag(290, 300)), (LayoutPriority.Exit, None, Tag(300, 305)));
            for (int i = 1; i < 4; i++)
                if (layout[i].TagVisible) Assert.That(Placed(layout[i]).Overlaps(glyph), Is.False, layout[i].Priority + " tag");
            AssertValid(layout, Screen, "tags on a glyph");
        }

        [Test]
        public void ATerminalGlyphUnderAnEdgeGlyphIsHiddenToo()
        {
            TagLayout layout = Solved((LayoutPriority.Edge, Glyph(1890, 540), None), (LayoutPriority.Terminal, Glyph(1888, 538, 9), Tag(1830, 555)));
            Assert.That(layout[0].GlyphVisible, Is.True);
            Assert.That(layout[1].GlyphVisible, Is.False);
        }

        // ---- determinism and order independence ---------------------------------------------------------------------------------

        private static List<(LayoutPriority priority, Rect glyph, Rect tag)> RandomItems(int seed, int count)
        {
            var rng = new System.Random(seed);
            var items = new List<(LayoutPriority, Rect, Rect)>();
            for (int i = 0; i < count; i++)
            {
                var priority = (LayoutPriority)rng.Next(5);
                // A small area, so collisions are the rule; coordinates on a coarse grid so exact ties happen too.
                float x = 400 + rng.Next(0, 6) * 20, y = 300 + rng.Next(0, 5) * 12;
                bool glyph = rng.Next(3) != 0, tag = !glyph || rng.Next(3) != 0;
                items.Add((priority, glyph ? Glyph(x, y, 8 + rng.Next(3) * 2) : None, tag ? Tag(x - 25, y + 12) : None));
            }
            return items;
        }

        private static string RectText(Rect r) => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###},{3:0.###}", r.x, r.y, r.width, r.height);

        private static string Signature(TagLayout layout)
        {
            var lines = new List<string>();
            for (int i = 0; i < layout.Count; i++)
            {
                LayoutItem item = layout[i];
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|g{3}|t{4}|{5:0.###}", item.Priority, RectText(item.Glyph), RectText(item.Tag), item.GlyphVisible, item.TagVisible, item.TagShift));
            }
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void PermutingTheInputGivesTheSameLayoutAndEveryLayoutIsValid(int seed)
        {
            List<(LayoutPriority priority, Rect glyph, Rect tag)> items = RandomItems(seed, 14);
            TagLayout reference = Solved(items.ToArray());
            AssertValid(reference, Screen, "seed " + seed);
            string expected = Signature(reference);
            var rng = new System.Random(seed * 31);
            for (int round = 0; round < 25; round++)
            {
                for (int i = items.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var t = items[i]; items[i] = items[j]; items[j] = t;
                }
                TagLayout shuffled = Solved(items.ToArray());
                Assert.That(Signature(shuffled), Is.EqualTo(expected), "seed " + seed + " permutation " + round);
            }
        }

        [Test]
        public void SolvingTheSameInputTwiceOrAfterAClearGivesTheSameResult()
        {
            List<(LayoutPriority priority, Rect glyph, Rect tag)> items = RandomItems(42, 16);
            var layout = new TagLayout();
            foreach (var item in items) layout.Add(item.priority, item.glyph, item.tag);
            layout.Solve(Screen);
            string first = Signature(layout);
            layout.Solve(Screen);
            Assert.That(Signature(layout), Is.EqualTo(first), "a second Solve on the same items");
            layout.Clear();
            Assert.That(layout.Count, Is.EqualTo(0));
            foreach (var item in items) layout.Add(item.priority, item.glyph, item.tag);
            layout.Solve(Screen);
            Assert.That(Signature(layout), Is.EqualTo(first), "after Clear and the same items again");
        }

        [Test]
        public void IdenticalItemsGetIdenticalTreatmentRegardlessOfWhichIsWhich()
        {
            var a = Solved((LayoutPriority.Terminal, None, Tag(5, 5)), (LayoutPriority.Terminal, None, Tag(5, 5)), (LayoutPriority.Terminal, None, Tag(5, 5)));
            var b = Solved((LayoutPriority.Terminal, None, Tag(5, 5)), (LayoutPriority.Terminal, None, Tag(5, 5)), (LayoutPriority.Terminal, None, Tag(5, 5)));
            Assert.That(Signature(a), Is.EqualTo(Signature(b)));
        }

        // ---- many tags --------------------------------------------------------------------------------------------------------------

        [Test]
        public void SevenSurvivorsAndFiveTerminalsStackedOnOnePointFinishWithoutOverlapOrException()
        {
            var items = new List<(LayoutPriority, Rect, Rect)>();
            for (int i = 0; i < 7; i++) items.Add((LayoutPriority.Survivor, Glyph(960, 540), Tag(930, 556)));
            for (int i = 0; i < 5; i++) items.Add((LayoutPriority.Terminal, Glyph(960, 540, 9), Tag(930, 556)));
            TagLayout layout = null;
            Assert.DoesNotThrow(() => layout = Solved(items.ToArray()));
            AssertValid(layout, Screen, "7 + 5 on one point");
            for (int i = 0; i < 7; i++) Assert.That(layout[i].GlyphVisible, Is.True, "Survivor glyph " + i);
            for (int i = 7; i < 12; i++)
            {
                Assert.That(layout[i].GlyphVisible, Is.False, "Terminal square " + i + " stands on Survivor markers");
                Assert.That(layout[i].TagVisible, Is.False, "and so does its tag");
            }
            int survivorTags = 0;
            for (int i = 0; i < 7; i++) if (layout[i].TagVisible) survivorTags++;
            Assert.That(survivorTags, Is.InRange(3, 4), "the point plus at most 3 steps (a step onto the glyphs is refused); the rest are hidden, not overlapped");
        }

        [Test]
        public void SevenSurvivorsAndFiveTerminalsOnTheSameSpotWithSeparateGlyphsStillAreValid()
        {
            // Survivors 12 u apart (glyphs overlap their neighbours), terminals on the same spot with slightly offset glyphs.
            var items = new List<(LayoutPriority, Rect, Rect)>();
            for (int i = 0; i < 7; i++) items.Add((LayoutPriority.Survivor, Glyph(960 + i * 12, 540), Tag(930 + i * 12, 556)));
            for (int i = 0; i < 5; i++) items.Add((LayoutPriority.Terminal, Glyph(960 + i * 14, 545, 9), Tag(930 + i * 14, 560)));
            AssertValid(Solved(items.ToArray()), Screen, "spread stack");
        }

        [Test]
        public void ARandomCrowdOfThreeHundredItemsIsAlwaysValid()
        {
            var rng = new System.Random(7);
            for (int round = 0; round < 20; round++)
            {
                var items = new List<(LayoutPriority, Rect, Rect)>();
                for (int i = 0; i < 15; i++)
                {
                    float x = 300 + rng.Next(0, 200), y = 300 + rng.Next(0, 120);
                    items.Add(((LayoutPriority)rng.Next(5), rng.Next(4) == 0 ? None : Glyph(x, y, 8), rng.Next(5) == 0 ? None : Tag(x - 25, y + 10)));
                }
                AssertValid(Solved(items.ToArray()), Screen, "round " + round);
            }
        }

        [Test]
        public void AnEmptyLayoutAndItemsWithNothingToShowSolveWithoutError()
        {
            var layout = new TagLayout();
            Assert.DoesNotThrow(() => layout.Solve(Screen));
            layout.Add(LayoutPriority.Survivor, None, None);
            Assert.DoesNotThrow(() => layout.Solve(Screen));
            Assert.That(layout[0].GlyphVisible, Is.False);
            Assert.That(layout[0].TagVisible, Is.False);
        }
    }
}
