using NUnit.Framework;
using PingTheKiller.Client.UI;

namespace PingTheKiller.Client.Tests
{
    // HUD_SPEC 0.2 and Q15: min(w/1920, h/1080) clamped to [0.8, 1.0] (U4c reverts U4b's upper bound of 2.0).
    public sealed class PanelScaleTests
    {
        [TestCase(1920, 1080, 1f)]
        [TestCase(3840, 2160, 1f)]
        [TestCase(2560, 1440, 1f)]
        [TestCase(7680, 4320, 1f)]
        [TestCase(1600, 900, 1600f / 1920)]
        [TestCase(1280, 720, .8f)]
        [TestCase(800, 600, .8f)]
        [TestCase(2560, 900, 900f / 1080)]
        public void ScaleFollowsTheSmallerAxisBetweenMinAndMax(int width, int height, float expected)
        {
            Assert.That(ClientScreens.PanelScale(width, height), Is.EqualTo(expected).Within(.0001f));
        }

        [Test]
        public void TheBoundsAreZeroPointEightAndOne()
        {
            Assert.That(ClientScreens.MinScale, Is.EqualTo(.8f));
            Assert.That(ClientScreens.MaxScale, Is.EqualTo(1f));
        }
    }
}
