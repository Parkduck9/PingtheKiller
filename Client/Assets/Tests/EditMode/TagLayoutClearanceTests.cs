using NUnit.Framework;
using PingTheKiller.Client.Killer;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // minors-1110 F2 (qa-int-1110 minor 2): a placed tag keeps TagGap (4 u) from every other placed tag and from every other
    // visible glyph it avoids (a Survivor tag avoids Edge chevrons too); its own glyph keeps the plain no-overlap rule.
    public sealed class TagLayoutClearanceTests
    {
        private static readonly Rect Screen = new Rect(0, 0, 1280, 720);
        private static Rect TagAt(float x, float y) => new Rect(x, y, 60, 23);
        private static Rect Placed(LayoutItem item) => new Rect(item.Tag.x, item.Tag.y + item.TagShift, item.Tag.width, item.Tag.height);

        // At least gap apart on one axis (1e-3 float slack).
        private static bool Apart(Rect a, Rect b, float gap = TagLayout.TagGap)
            => a.xMax + gap <= b.xMin + 1e-3f || b.xMax + gap <= a.xMin + 1e-3f
            || a.yMax + gap <= b.yMin + 1e-3f || b.yMax + gap <= a.yMin + 1e-3f;

        private static bool Slotted(LayoutPriority p) => p == LayoutPriority.Overloaded || p == LayoutPriority.Exit;

        private static bool Avoids(LayoutPriority tag, LayoutPriority glyph)
            => glyph <= tag || (tag == LayoutPriority.Survivor && glyph == LayoutPriority.Edge);

        [Test]
        public void TheGapIsTheEdgeMarkerGapAndTheStackStepFollowsIt()
        {
            Assert.That(TagLayout.TagGap, Is.EqualTo(EdgeSpread.Gap), "Q2's edge-marker gap");
            Assert.That(TagLayout.MinTagGap, Is.EqualTo(EdgeSpread.MinGap), "the crowded fallback matches the edge spread");
            Assert.That(TagLayout.StepGap, Is.EqualTo(TagLayout.TagGap));
        }

        [Test]
        public void TwoInViewSurvivorTagsWhosePreferredRectsTouchEndUpTagGapApartOrStacked()
        {
            var layout = new TagLayout();
            int a = layout.Add(LayoutPriority.Survivor, TagLayout.Around(new Vector2(630, 280), 10, 10), TagAt(600, 292));
            int b = layout.Add(LayoutPriority.Survivor, TagLayout.Around(new Vector2(690, 280), 10, 10), TagAt(660, 292)); // x touches at 660
            layout.Solve(Screen);
            Assert.That(layout[a].TagVisible && layout[b].TagVisible, Is.True);
            Assert.That(Apart(Placed(layout[a]), Placed(layout[b])), Is.True);
            Assert.That(layout[a].TagShift == 0 ^ layout[b].TagShift == 0, Is.True, "one tag is stacked");
            Assert.That(Mathf.Abs(layout[a].TagShift + layout[b].TagShift), Is.EqualTo(23 + TagLayout.TagGap).Within(1e-3f));
        }

        [Test]
        public void ATagItsOwnGlyphDefaultOffsetIsNotPushedAway()
        {
            // Diamond 20 u, tag 12 u below the centre: 2 u from its own glyph, which stays legal.
            var layout = new TagLayout();
            int d = layout.Add(LayoutPriority.Survivor, TagLayout.Around(new Vector2(600, 300), 10, 10), TagAt(570, 312));
            layout.Solve(Screen);
            Assert.That(layout[d].TagVisible, Is.True);
            Assert.That(layout[d].TagShift, Is.EqualTo(0f));
        }

        [Test]
        public void ASurvivorTagKeepsTagGapFromAnEdgeChevronGlyph()
        {
            // Decision: Survivor and Edge glyphs are both Survivor information, so a Survivor tag never touches a chevron.
            var layout = new TagLayout();
            Rect chevron = TagLayout.Around(new Vector2(660, 300), 8, 8); // xMin 652
            layout.Add(LayoutPriority.Edge, chevron, default);
            int tag = layout.Add(LayoutPriority.Survivor, default, TagAt(592, 289)); // xMax 652: touches the chevron
            layout.Solve(Screen);
            Assert.That(layout[tag].TagVisible, Is.True);
            Assert.That(Apart(Placed(layout[tag]), chevron), Is.True);
        }

        [Test]
        public void ASlottedExitTagLandsExactlyTagGapFromTheObstacle()
        {
            var layout = new TagLayout();
            int survivor = layout.Add(LayoutPriority.Survivor, default, TagAt(600, 300));
            int exit = layout.Add(LayoutPriority.Exit, default, TagAt(600, 300));
            layout.Solve(Screen);
            Assert.That(layout[exit].TagVisible, Is.True);
            Rect s = Placed(layout[survivor]), e = Placed(layout[exit]);
            Assert.That(e.yMin - s.yMax, Is.EqualTo(TagLayout.TagGap).Within(1e-3f), "below, exactly TagGap");
        }

        [Test]
        public void RandomLayoutsWithoutSlottedTagsKeepFullTagGapEverywhere()
        {
            var rng = new System.Random(7);
            var layout = new TagLayout();
            for (int round = 0; round < 300; round++)
            {
                layout.Clear();
                int n = 2 + rng.Next(8);
                for (int i = 0; i < n; i++)
                {
                    var priority = (LayoutPriority)(rng.Next(2) == 0 ? 0 : rng.Next(2) == 0 ? 1 : 4);
                    float x = 500 + rng.Next(160), y = 300 + rng.Next(90);
                    Rect glyph = rng.Next(3) == 0 ? default : TagLayout.Around(new Vector2(x + 30, y - 10), 8 + rng.Next(5), 8 + rng.Next(5));
                    layout.Add(priority, glyph, new Rect(x, y, 40 + rng.Next(40), 23));
                }
                layout.Solve(Screen);
                for (int i = 0; i < layout.Count; i++)
                    for (int j = 0; j < layout.Count; j++)
                    {
                        if (j == i || !layout[i].TagVisible) continue;
                        if (j > i && layout[j].TagVisible) Assert.That(Apart(Placed(layout[i]), Placed(layout[j])), Is.True, "round " + round + " tags " + i + "/" + j);
                        if (layout[j].GlyphVisible && Avoids(layout[i].Priority, layout[j].Priority)) Assert.That(Apart(Placed(layout[i]), layout[j].Glyph), Is.True, "round " + round + " tag " + i + " glyph " + j);
                    }
            }
        }

        [Test]
        public void RandomLayoutsKeepTagGapBetweenVisibleTagsAndAvoidedGlyphs()
        {
            var rng = new System.Random(1110);
            var layout = new TagLayout();
            for (int round = 0; round < 300; round++)
            {
                layout.Clear();
                int n = 2 + rng.Next(10);
                for (int i = 0; i < n; i++)
                {
                    var priority = (LayoutPriority)rng.Next(5);
                    float x = 500 + rng.Next(160), y = 300 + rng.Next(90);
                    Rect glyph = rng.Next(3) == 0 ? default : TagLayout.Around(new Vector2(x + 30, y - 10), 8 + rng.Next(5), 8 + rng.Next(5));
                    Rect tag = rng.Next(5) == 0 ? default : new Rect(x, y, 40 + rng.Next(40), 23);
                    layout.Add(priority, glyph, tag, priority == LayoutPriority.Exit && rng.Next(2) == 0);
                }
                layout.Solve(Screen);
                for (int i = 0; i < layout.Count; i++)
                {
                    if (!layout[i].TagVisible) continue;
                    Rect a = Placed(layout[i]);
                    for (int j = 0; j < layout.Count; j++)
                    {
                        if (j == i) continue;
                        if (j > i && layout[j].TagVisible)
                        {
                            // Only an Overloaded or Exit tag may fall back (2 u, then plain no-overlap), and only when it has no slot at TagGap.
                            float need = Slotted(layout[i].Priority) || Slotted(layout[j].Priority) ? 0f : TagLayout.TagGap;
                            Assert.That(Apart(a, Placed(layout[j]), need), Is.True, "round " + round + " tags " + i + "/" + j);
                        }
                        if (layout[j].GlyphVisible && Avoids(layout[i].Priority, layout[j].Priority))
                            Assert.That(Apart(a, layout[j].Glyph, Slotted(layout[i].Priority) ? 0f : TagLayout.TagGap), Is.True, "round " + round + " tag " + i + " glyph " + j);
                    }
                    if (layout[i].GlyphVisible && Avoids(layout[i].Priority, layout[i].Priority))
                        Assert.That(a.Overlaps(layout[i].Glyph), Is.False, "round " + round + " own glyph " + i);
                }
            }
        }
    }
}
