using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // The screen-space marker layout (U4c fix list F-01/F-02/F-04): no two tags overlap, priority decides who moves, three
    // stacking steps then a hide, Survivor glyphs are never hidden, and the result does not depend on the input order.
    public sealed class TagLayoutTests
    {
        private static readonly Rect Screen = new Rect(0, 0, 1280, 720);
        private static Rect TagAt(float x, float y) => new Rect(x, y, 60, 23);

        [Test]
        public void TwoTagsAtTheSamePointEndUpDisjoint()
        {
            var layout = new TagLayout();
            layout.Add(LayoutPriority.Survivor, default, TagAt(600, 300));
            layout.Add(LayoutPriority.Survivor, default, TagAt(600, 300));
            layout.Solve(Screen);
            Assert.That(layout[0].TagVisible && layout[1].TagVisible, Is.True);
            Assert.That(Placed(layout[0]).Overlaps(Placed(layout[1])), Is.False);
            Assert.That(Mathf.Abs(layout[0].TagShift - layout[1].TagShift), Is.EqualTo(23 + TagLayout.StepGap), "stacked by tag height + TagGap (4 u)");
        }

        [Test]
        public void PriorityDecidesWhoMoves()
        {
            var layout = new TagLayout();
            int terminal = layout.Add(LayoutPriority.Terminal, default, TagAt(600, 300));
            int survivor = layout.Add(LayoutPriority.Survivor, default, TagAt(610, 305));
            layout.Solve(Screen);
            Assert.That(layout[survivor].TagShift, Is.EqualTo(0f), "the Survivor tag keeps its place");
            Assert.That(layout[terminal].TagVisible, Is.True);
            Assert.That(layout[terminal].TagShift, Is.Not.EqualTo(0f), "the terminal tag moves");
        }

        [Test]
        public void StackingAlternatesBelowAboveBelowAndThenHides()
        {
            var layout = new TagLayout();
            var indices = new List<int>();
            for (int i = 0; i < 5; i++) indices.Add(layout.Add(LayoutPriority.Survivor, default, TagAt(600, 300)));
            layout.Solve(Screen);
            var shifts = new List<float>();
            int hidden = 0;
            foreach (int i in indices) { if (layout[i].TagVisible) shifts.Add(layout[i].TagShift); else hidden++; }
            float step = 23 + TagLayout.StepGap;
            Assert.That(shifts, Is.EquivalentTo(new[] { 0, step, -step, 2 * step }));
            Assert.That(hidden, Is.EqualTo(1), "after three steps the tag is hidden");
        }

        [Test]
        public void ALowerPriorityTagNeverCoversAHigherPriorityGlyph()
        {
            // F-02: a chevron's tag over a through-wall diamond moves; the diamond stays.
            var layout = new TagLayout();
            int diamond = layout.Add(LayoutPriority.Survivor, TagLayout.Around(new Vector2(1200, 360), 10, 10), TagAt(1170, 372));
            int chevron = layout.Add(LayoutPriority.Edge, TagLayout.Around(new Vector2(1250, 360), 10, 10), TagAt(1170, 350));
            layout.Solve(Screen);
            Assert.That(layout[diamond].GlyphVisible && layout[diamond].TagVisible, Is.True);
            Assert.That(layout[diamond].TagShift, Is.EqualTo(0f));
            Assert.That(layout[chevron].GlyphVisible, Is.True, "an edge chevron is a Survivor marker and is never hidden");
            if (layout[chevron].TagVisible)
            {
                Assert.That(Placed(layout[chevron]).Overlaps(layout[diamond].Glyph), Is.False);
                Assert.That(Placed(layout[chevron]).Overlaps(Placed(layout[diamond])), Is.False);
            }
        }

        [Test]
        public void ATerminalSquareOverASurvivorIsHiddenWithItsTagButASurvivorGlyphNeverIs()
        {
            // F-04: a Survivor stands at a terminal.
            var layout = new TagLayout();
            int body = layout.Add(LayoutPriority.Survivor, new Rect(590, 280, 40, 90), default);
            int terminal = layout.Add(LayoutPriority.Terminal, TagLayout.Around(new Vector2(610, 300), 9, 9), TagAt(580, 312));
            int diamond = layout.Add(LayoutPriority.Survivor, TagLayout.Around(new Vector2(612, 300), 10, 10), default);
            layout.Solve(Screen);
            Assert.That(layout[body].GlyphVisible && layout[diamond].GlyphVisible, Is.True);
            Assert.That(layout[terminal].GlyphVisible, Is.False);
            Assert.That(layout[terminal].TagVisible, Is.False);
        }

        [Test]
        public void ATagOnlyTerminalLabelMovesOffASurvivorBody()
        {
            var layout = new TagLayout();
            int body = layout.Add(LayoutPriority.Survivor, new Rect(590, 280, 40, 90), default);
            int label = layout.Add(LayoutPriority.Terminal, default, TagAt(580, 300));
            layout.Solve(Screen);
            Assert.That(layout[label].TagVisible, Is.False, "every step still covers the 90 u tall body, so the label hides");
            layout.Clear();
            body = layout.Add(LayoutPriority.Survivor, new Rect(590, 280, 40, 30), default);
            label = layout.Add(LayoutPriority.Terminal, default, TagAt(580, 290));
            layout.Solve(Screen);
            Assert.That(layout[label].TagVisible, Is.True);
            Assert.That(Placed(layout[label]).Overlaps(layout[body].Glyph), Is.False);
        }

        [Test]
        public void TagsStayInsideTheBoundsAndOutOfTheBlockedCluster()
        {
            var layout = new TagLayout();
            layout.Block(new Rect(24, 400, 320, 296));
            int a = layout.Add(LayoutPriority.Terminal, default, TagAt(300, 390));
            int b = layout.Add(LayoutPriority.Survivor, default, TagAt(600, 710));
            layout.Solve(Screen);
            Assert.That(layout[a].TagVisible, Is.True);
            Assert.That(Placed(layout[a]).Overlaps(new Rect(24, 400, 320, 296)), Is.False);
            Assert.That(layout[b].TagVisible, Is.True);
            Assert.That(Placed(layout[b]).yMax, Is.LessThanOrEqualTo(720f));
        }

        [Test]
        public void TheLayoutIsDeterministicAndIgnoresTheInputOrder()
        {
            var items = new List<(LayoutPriority, Rect, Rect)>
            {
                (LayoutPriority.Survivor, TagLayout.Around(new Vector2(600, 300), 10, 10), TagAt(570, 312)),
                (LayoutPriority.Survivor, TagLayout.Around(new Vector2(605, 302), 10, 10), TagAt(575, 314)),
                (LayoutPriority.Edge, TagLayout.Around(new Vector2(1250, 300), 10, 10), TagAt(1170, 290)),
                (LayoutPriority.Exit, TagLayout.Around(new Vector2(610, 330), 13, 10), TagAt(580, 342)),
                (LayoutPriority.Terminal, TagLayout.Around(new Vector2(590, 310), 9, 9), TagAt(560, 322)),
                (LayoutPriority.Terminal, default, TagAt(600, 320)),
                (LayoutPriority.Terminal, default, TagAt(600, 320)),
                (LayoutPriority.Survivor, default, TagAt(560, 316)),
            };
            List<string> reference = Run(items);
            var rng = new System.Random(4);
            for (int round = 0; round < 20; round++)
            {
                var shuffled = new List<(LayoutPriority, Rect, Rect)>(items);
                for (int i = shuffled.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t; }
                Assert.That(Run(shuffled), Is.EqualTo(reference), "round " + round);
            }
            // And no two visible tags overlap.
            var layout = new TagLayout();
            foreach (var (p, g, t) in items) layout.Add(p, g, t);
            layout.Solve(Screen);
            for (int i = 0; i < layout.Count; i++)
                for (int j = i + 1; j < layout.Count; j++)
                    if (layout[i].TagVisible && layout[j].TagVisible) Assert.That(Placed(layout[i]).Overlaps(Placed(layout[j])), Is.False, i + " / " + j);
        }

        [Test]
        public void AV2OverloadedTagAndAMarkerLayerEdgeTagOnTheSameRectDoNotOverlap()
        {
            // Q2: the rules v2 tags join the MarkerLayer solve. Both start on the same rectangle near the right edge.
            var layout = new TagLayout();
            Rect shared = TagAt(1500, 440);
            int edge = layout.Add(LayoutPriority.Edge, TagLayout.Around(new Vector2(1576, 450), 10, 10), shared);
            int alert = layout.Add(LayoutPriority.Overloaded, TagLayout.Around(new Vector2(1576, 430), 9, 9), shared);
            int exit = layout.Add(LayoutPriority.Exit, TagLayout.Around(new Vector2(1530, 480), 13, 13), shared, true);
            layout.Solve(new Rect(24, 24, 1552, 852));
            Assert.That(layout[edge].TagVisible && layout[alert].TagVisible && layout[exit].TagVisible, Is.True);
            Assert.That(layout[edge].TagShift, Is.EqualTo(0f), "the Survivor edge tag keeps its place");
            Assert.That(Placed(layout[alert]).Overlaps(Placed(layout[edge])), Is.False);
            Assert.That(Placed(layout[exit]).Overlaps(Placed(layout[edge])) || Placed(layout[exit]).Overlaps(Placed(layout[alert])), Is.False);
            Assert.That(Placed(layout[alert]).Overlaps(layout[edge].Glyph), Is.False, "the alert tag never covers the Survivor chevron");
            Assert.That(layout[exit].GlyphVisible, Is.True, "KeepGlyph: the Exit marker stays");
        }

        private static List<string> Run(List<(LayoutPriority, Rect, Rect)> items)
        {
            var layout = new TagLayout();
            foreach (var (p, g, t) in items) layout.Add(p, g, t);
            layout.Solve(Screen);
            var result = new List<string>();
            for (int i = 0; i < layout.Count; i++)
            {
                LayoutItem item = layout[i];
                result.Add(item.Priority + " " + item.Glyph + " " + item.Tag + " g=" + item.GlyphVisible + " t=" + item.TagVisible + " s=" + item.TagShift);
            }
            result.Sort(System.StringComparer.Ordinal);
            return result;
        }

        private static Rect Placed(LayoutItem item) => new Rect(item.Tag.x, item.Tag.y + item.TagShift, item.Tag.width, item.Tag.height);
    }
}
