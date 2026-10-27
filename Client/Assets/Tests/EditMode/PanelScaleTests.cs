using NUnit.Framework;
using PingTheKiller.Client.UI;

namespace PingTheKiller.Client.Tests
{
    public sealed class PanelScaleTests
    {
        [TestCase(1920, 1080, 1f)]
        [TestCase(3840, 2160, 1f)]
        [TestCase(1600, 900, 1600f / 1920)]
        [TestCase(1280, 720, ClientScreens.MinScale)]
        [TestCase(800, 600, ClientScreens.MinScale)]
        [TestCase(2560, 900, 900f / 1080)]
        public void ScaleFollowsTheSmallerAxisBetweenMinAndOne(int width, int height, float expected)
        {
            Assert.That(ClientScreens.PanelScale(width, height), Is.EqualTo(expected).Within(.0001f));
        }
    }
}
