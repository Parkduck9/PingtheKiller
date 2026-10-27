using NUnit.Framework;
using PingTheKiller.Client.UI;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 3.7: scale = min(w/1920, h/1080) clamped to [0.8, 2].
    public sealed class PanelScaleQaTests
    {
        [TestCase(1920, 1080, 1.0f)]
        [TestCase(1280, 720, 0.8f)]
        [TestCase(1366, 768, 0.8f)]
        [TestCase(1600, 900, 0.8333333f)]
        [TestCase(2560, 1440, 1.3333334f)]
        [TestCase(3840, 2160, 2.0f)]
        [TestCase(5120, 2880, 2.0f)]
        [TestCase(7680, 4320, 2.0f)]
        [TestCase(800, 600, 0.8f)]
        [TestCase(2560, 900, 0.8333333f)]
        [TestCase(1920, 2160, 1.0f)]
        public void TheScaleIsTheSmallerAxisRatioClampedBetweenPointEightAndTwo(int width, int height, float expected)
        {
            Assert.That(ClientScreens.PanelScale(width, height), Is.EqualTo(expected).Within(1e-4f));
        }

        [Test]
        public void ADegenerateWindowStillGivesTheMinimumScale()
        {
            Assert.That(ClientScreens.PanelScale(0, 0), Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(ClientScreens.PanelScale(1, 1), Is.EqualTo(0.8f).Within(1e-4f));
        }

        [Test]
        public void TheBoundsAreTheSpecValues()
        {
            Assert.That(ClientScreens.MinScale, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(ClientScreens.MaxScale, Is.EqualTo(2.0f).Within(1e-6f));
        }
    }
}
