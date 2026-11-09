using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.QA.Tests
{
    // Q2-edge-labels QA (attempt 2, F-01 fix review). V2MarkerLayer.Tag and PlaceEdge now reserve Ceil'ed whole-unit sizes
    // and clamp the rounded origin inside the safe area (WholeInside). These tests pin the parts the wiring tests do not
    // assert: the label is drawn exactly at the reserved origin, the Survivor/Exit paths through Tag() (Above pivot) take
    // the same route, a layer with no size yet (SafeArea = 0..float.MaxValue) yields finite whole-unit places, and
    // PlaceEdge keeps the reserved size while taking a fractional spread rectangle.
    public sealed class EdgeLabelsPlacementQaTests
    {
        private static Font TestFont => Resources.Load<Font>("Fonts/Inter-Regular");

        private static bool Whole(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v - Mathf.Round(v)) < 1e-4f;

        private static Label LabelOf(V2MarkerLayer v2, int index)
        {
            int i = 0;
            foreach (VisualElement child in v2.Children())
                if (child is Label label && i++ == index) return label;
            Assert.Fail("no label " + index);
            return null;
        }

        private static Rect ReservedOf(V2MarkerLayer v2, int index)
        {
            v2.LayoutInput(index, out _, out _, out Rect tag, out _);
            return tag;
        }

        private static void AssertDrawnAtReservedOrigin(V2MarkerLayer v2, int index, string label)
        {
            Rect reserved = ReservedOf(v2, index);
            Label text = LabelOf(v2, index);
            float left = text.style.left.value.value, top = text.style.top.value.value;
            Assert.That(Whole(reserved.x) && Whole(reserved.y) && Whole(reserved.width) && Whole(reserved.height), Is.True, label + ": reserved rect is whole-unit " + reserved);
            Assert.That(reserved.width, Is.GreaterThan(0), label + ": reserved width");
            Assert.That(reserved.height, Is.GreaterThan(0), label + ": reserved height");
            Assert.That(left, Is.EqualTo(reserved.x), label + ": label left = reserved origin");
            Assert.That(top, Is.EqualTo(reserved.y), label + ": label top = reserved origin");
            Assert.That(v2.TagSize(index), Is.EqualTo(reserved.size), label + ": TagSize is the reserved size");
        }

        [Test]
        public void SurvivorRegressingExitAndOnScreenOverloadedTagsOnAnUnsizedLayerArePlacedWholeFromCeiledEstimates()
        {
            var v2 = new V2MarkerLayer(TestFont);
            v2.Begin();
            var terminal = new TerminalFlagsView { Id = 4, Regressing = true, Progress01 = .37f };
            var exit = new ExitView { Id = 1, Powered = true, SwitchProgress01 = .62f, LockedLeft = -1 };
            var regressingAt = new Vector2(1575.3f, 877.7f);  // flush to the 1600×900 right/bottom inset, fractional
            // An unsized layer's safe area is 0..float.MaxValue (not the 24 u inset), so anchors stay > half a tag from 0.
            var exitAt = new Vector2(124.4f, 130.2f);
            var alertAt = new Vector2(800.5f, 450.5f);
            v2.Regressing(regressingAt, terminal, Role.Survivor);           // tag 0, Above pivot, Survivor HUD path
            v2.Exit(exitAt, exit);                                          // tag 1, Above pivot, Killer
            v2.Overloaded(alertAt, true, Vector2.zero, v2.OverloadedTag(new TerminalFlagsView { Id = 2, Overloaded = true }, 41.4f));  // tag 2, Below
            v2.End();
            Assert.That(v2.LayoutCount, Is.EqualTo(3));

            string[] texts = { TerminalFlagCopy.Regressing(terminal, Role.Survivor), ExitCopy.Tag(exit), TerminalFlagCopy.Overloaded(new TerminalFlagsView { Id = 2, Overloaded = true }, 41.4f) };
            Vector2[] at = { regressingAt, exitAt, alertAt };
            for (int i = 0; i < 3; i++)
            {
                string label = "tag " + i + " '" + texts[i] + "'";
                AssertDrawnAtReservedOrigin(v2, i, label);
                Rect reserved = ReservedOf(v2, i);
                Vector2 estimate = MarkerLayer.TagSize(texts[i], TagStyle.Text);
                // Unmeasured (no panel): the reservation is the Ceil of the estimate, never smaller than the text.
                Assert.That(reserved.width, Is.EqualTo(Mathf.Ceil(estimate.x)), label + ": Ceil(estimate) width");
                Assert.That(reserved.height, Is.EqualTo(Mathf.Ceil(estimate.y)), label + ": Ceil(estimate) height");
                Assert.That(reserved.width - estimate.x, Is.LessThan(1), label + ": at most 1 u of slack");
                // Centred on the anchor (within the rounding), pivot respected.
                Assert.That(Mathf.Abs(reserved.center.x - at[i].x), Is.LessThanOrEqualTo(.5f + 1e-3f), label + ": centred on x");
                float expectedY = i < 2 ? Mathf.Round(at[i].y - V2MarkerLayer.TagGap - reserved.height) : Mathf.Round(at[i].y + V2MarkerLayer.TagGap);
                Assert.That(reserved.y, Is.EqualTo(expectedY), label + ": pivot y");
                // Hidden this frame (unmeasured), as the one-frame rule says, but still reserving.
                Assert.That(LabelOf(v2, i).style.visibility == Visibility.Hidden, Is.True, label + ": unmeasured tag hidden this frame");
            }
            // The Regressing tag has no glyph and its priority is Terminal; the Exit glyph is kept.
            v2.LayoutInput(0, out LayoutPriority p0, out Rect g0, out _, out bool keep0);
            v2.LayoutInput(1, out LayoutPriority p1, out Rect g1, out _, out bool keep1);
            v2.LayoutInput(2, out LayoutPriority p2, out Rect g2, out _, out bool keep2);
            Assert.That(p0 == LayoutPriority.Terminal && g0.width == 0 && !keep0, Is.True, "Regressing: Terminal priority, no glyph");
            Assert.That(p1 == LayoutPriority.Exit && keep1 && g1.Contains(exitAt), Is.True, "Exit: kept glyph around the marker");
            Assert.That(p2 == LayoutPriority.Overloaded && !keep2 && g2.Contains(alertAt), Is.True, "Overloaded: square around the terminal");
        }

        [Test]
        public void PlaceEdgeTakesAFractionalSpreadRectKeepsTheReservedSizeAndDrawsAtTheWholeOrigin()
        {
            var v2 = new V2MarkerLayer(TestFont);
            v2.Begin();
            string alert = v2.OverloadedTag(new TerminalFlagsView { Id = 3, Overloaded = true }, 41);
            int shown = v2.OverloadedEdge(new Vector2(1576, 300), Vector2.right, alert);
            int dropped = v2.OverloadedEdge(new Vector2(1576, 400), Vector2.right, alert);
            Vector2 size = v2.TagSize(shown);
            Assert.That(size, Is.EqualTo(new Vector2(Mathf.Ceil(MarkerLayer.TagSize(alert, TagStyle.Text).x), Mathf.Ceil(MarkerLayer.TagSize(alert, TagStyle.Text).y))), "Ceil'ed estimate");

            // A deliberately fractional rect with the wrong size: PlaceEdge rounds the origin and keeps its own reserved size.
            var spreadAt = new Vector2(1567, 352);
            v2.PlaceEdge(shown, spreadAt, new Rect(1360.6f, 99.4f, 300, 300), true);
            v2.PlaceEdge(dropped, new Vector2(1567, 452), new Rect(1360.6f, 199.4f, size.x, size.y), false);
            v2.End();

            Rect reserved = ReservedOf(v2, shown);
            Assert.That(reserved, Is.EqualTo(new Rect(1361, 99, size.x, size.y)), "rounded origin, reserved size");
            AssertDrawnAtReservedOrigin(v2, shown, "shown alert");
            v2.LayoutInput(shown, out _, out Rect glyph, out _, out _);
            Assert.That(glyph.Contains(spreadAt), Is.True, "the square moved to the spread point");

            // The dropped tag reserves nothing, is hidden, and ApplyLayout cannot show it again this frame.
            Assert.That(ReservedOf(v2, dropped).width, Is.EqualTo(0), "dropped tag reserves nothing");
            Assert.That(LabelOf(v2, dropped).style.visibility == Visibility.Hidden, Is.True, "dropped tag hidden");
            v2.ApplyLayout(dropped, true, 7);
            Assert.That(LabelOf(v2, dropped).style.visibility == Visibility.Hidden, Is.True, "dropped tag stays hidden after ApplyLayout");
        }

        [Test]
        public void ASecondFrameReusesThePooledLabelsAndKeepsEveryPlaceFiniteAndWhole()
        {
            var v2 = new V2MarkerLayer(TestFont);
            for (int frame = 0; frame < 3; frame++)
            {
                v2.Begin();
                for (int k = 0; k < 6; k++)
                {
                    float metres = 40 + frame + k * .4f;  // the metres tick: a pooled label's text changes between frames
                    string alert = v2.OverloadedTag(new TerminalFlagsView { Id = (byte)k, Overloaded = true }, metres);
                    int tag = v2.OverloadedEdge(new Vector2(1576, 100 + k * 90), Vector2.right, alert);
                    v2.PlaceEdge(tag, new Vector2(1567, 100 + k * 90), new Rect(1576 - v2.TagSize(tag).x, 100 + k * 90 + 12.5f, v2.TagSize(tag).x, v2.TagSize(tag).y), true);
                }
                // Anchored inside the top-left corner: the whole rect is clamped to the unsized layer's safe origin (0, 0),
                // never left partly outside and never NaN (SafeArea.max = float.MaxValue on the max side).
                v2.Regressing(new Vector2(10.5f, 10.5f), new TerminalFlagsView { Id = 7, Regressing = true, Progress01 = .5f }, Role.Killer);
                v2.End();
                Assert.That(v2.LayoutCount, Is.EqualTo(7), "frame " + frame);
                for (int i = 0; i < v2.LayoutCount; i++) AssertDrawnAtReservedOrigin(v2, i, "frame " + frame + " tag " + i);
                Assert.That(ReservedOf(v2, 6).position, Is.EqualTo(Vector2.zero), "frame " + frame + ": corner tag clamped to the safe origin");
            }
        }
    }
}
