using NUnit.Framework;
using PingTheKiller.Client.UI;

namespace PingTheKiller.Client.QA.Tests
{
    // Superseded for U4c (orchestrator ruling 1, a spec correction and not a weakening): HUD_SPEC 0.2 and Q15 clamp the panel
    // scale to [0.8, 1.0], so scale = clamp(min(w/1920, h/1080), 0.8, 1.0). The U4b version of this file capped at 2.0.
    public sealed class PanelScaleQaTests
    {
        [TestCase(1920, 1080, 1.0f)]
        [TestCase(1280, 720, 0.8f)]        // raw 0.667: the floor
        [TestCase(1366, 768, 0.8f)]        // raw 0.711: the floor
        [TestCase(1600, 900, 0.8333333f)]  // raw 0.833: inside the range
        [TestCase(1700, 960, 0.8854167f)]  // width is the smaller axis
        [TestCase(1920, 1000, 0.9259259f)] // height is the smaller axis
        [TestCase(2560, 1440, 1.0f)]       // raw 1.333: the cap
        [TestCase(3840, 2160, 1.0f)]       // raw 2.0: the cap
        [TestCase(5120, 2880, 1.0f)]
        [TestCase(7680, 4320, 1.0f)]
        [TestCase(800, 600, 0.8f)]
        [TestCase(2560, 900, 0.8333333f)]  // ultra-wide: the smaller axis is the height
        [TestCase(1920, 2160, 1.0f)]       // portrait-ish: the smaller axis is the width, exactly 1
        [TestCase(2560, 1080, 1.0f)]
        public void TheScaleIsTheSmallerAxisRatioClampedBetweenPointEightAndOne(int width, int height, float expected)
        {
            Assert.That(ClientScreens.PanelScale(width, height), Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void ADegenerateWindowStillGivesTheMinimumScale()
        {
            Assert.That(ClientScreens.PanelScale(0, 0), Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(ClientScreens.PanelScale(1, 1), Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(ClientScreens.PanelScale(-5, 720), Is.EqualTo(0.8f).Within(1e-4f));
        }

        [Test]
        public void TheBoundsAreTheSpecValuesZeroPointEightAndOne()
        {
            Assert.That(ClientScreens.MinScale, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(ClientScreens.MaxScale, Is.EqualTo(1.0f).Within(1e-6f));
        }

        [Test]
        public void NoWindowSizeEverScalesAboveOneOrBelowPointEight()
        {
            for (int width = 200; width <= 8000; width += 197)
                for (int height = 150; height <= 4500; height += 163)
                {
                    float scale = ClientScreens.PanelScale(width, height);
                    Assert.That(scale, Is.InRange(0.8f - 1e-6f, 1.0f + 1e-6f), width + "x" + height);
                }
        }

        [Test]
        public void TheLogicalCanvasIs1920By1080At1080pAnd1600By900At720p()
        {
            float full = ClientScreens.PanelScale(1920, 1080), small = ClientScreens.PanelScale(1280, 720);
            Assert.That(1920 / full, Is.EqualTo(1920f).Within(1e-2f));
            Assert.That(1080 / full, Is.EqualTo(1080f).Within(1e-2f));
            Assert.That(1280 / small, Is.EqualTo(1600f).Within(1e-2f), "HUD_SPEC 0.2: every layout is checked against 1600 x 900 u");
            Assert.That(720 / small, Is.EqualTo(900f).Within(1e-2f));
        }

        [Test]
        public void SmallTextNeverRendersBelowTwelvePhysicalPixelsAtAnyAllowedScale()
        {
            foreach (float scale in new[] { 0.8f, 0.85f, 0.9f, 0.95f, 1.0f })
            {
                Assert.That(ClientScreens.SmallSize(scale) * scale, Is.GreaterThanOrEqualTo(12f - 1e-3f), "scale " + scale);
                Assert.That(ClientScreens.SmallSize(scale), Is.GreaterThanOrEqualTo(14f - 1e-3f), "S is never smaller than 14 u");
            }
            Assert.That(ClientScreens.SmallSize(0.8f), Is.EqualTo(15f).Within(1e-3f), "HUD_SPEC 0.3: S budgets 15 u at 720p");
            Assert.That(ClientScreens.SmallSize(1.0f), Is.EqualTo(14f).Within(1e-3f));
        }
    }
}
