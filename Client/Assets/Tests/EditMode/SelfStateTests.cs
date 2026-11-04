using NUnit.Framework;
using PingTheKiller.Shared.Game;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;

namespace PingTheKiller.Client.Tests
{
    // U4d: the S2C_SelfState clock (SelfStateHolder). Between packets the three timers count down by real time, floored at
    // 0; every packet snaps them; RescueProgress is shown exactly as received; skill_ready fires once per cooldown.
    public sealed class SelfStateTests
    {
        [Test]
        public void TimersCountDownByRealTimeAndFloorAtZero()
        {
            var s = new SelfStateHolder();
            s.Feed(12f, .4f, 2f, 1.6f, 0, 0, 100);
            Assert.That(s.Available, Is.True);
            s.Advance(100.5);
            Assert.That(s.SkillCooldown, Is.EqualTo(11.5f).Within(1e-4f));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(s.CollapseLeft, Is.EqualTo(1.1f).Within(1e-4f));
            Assert.That(s.RescueProgress, Is.EqualTo(.4f), "rescue progress is never extrapolated");
            s.Advance(102.0);
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f), "floored at 0");
            Assert.That(s.CollapseLeft, Is.EqualTo(0f));
            Assert.That(s.SkillCooldown, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(s.RescueProgress, Is.EqualTo(.4f));
            s.Advance(500);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
        }

        [Test]
        public void EveryPacketSnapsTheValuesAndTimeNeverRunsBackwards()
        {
            var s = new SelfStateHolder();
            s.Feed(12f, 0, 0, 0, 0, 0, 10);
            s.Advance(11);
            Assert.That(s.SkillCooldown, Is.EqualTo(11f).Within(1e-4f));
            // The 1 Hz refresh says 10.8 (the local clock ran 0.2 s late): snap to the server's value.
            s.Feed(10.8f, .35f, 0, 0, 0, 0, 11.05);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.8f).Within(1e-4f));
            Assert.That(s.RescueProgress, Is.EqualTo(.35f));
            s.Advance(11.0); // an older frame time is ignored
            Assert.That(s.SkillCooldown, Is.EqualTo(10.8f).Within(1e-4f));
            s.Advance(11.55);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.3f).Within(1e-4f));
            s.Advance(double.NaN);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.3f).Within(1e-4f), "a garbage clock changes nothing");
            s.Feed(0, 0, 0, 0, 0, 0, 12);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
        }

        [Test]
        public void GarbageValuesAreCleanedAndAFeedWithoutATimeUsesTheClock()
        {
            var s = new SelfStateHolder();
            s.Advance(50);
            s.Feed(float.NaN, 7f, -2f, float.PositiveInfinity, 0, 0);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.RescueProgress, Is.EqualTo(1f));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(s.CollapseLeft, Is.EqualTo(3600f));
            s.Advance(51);
            Assert.That(s.CollapseLeft, Is.EqualTo(3599f).Within(1e-3f));
        }

        [Test]
        public void ResetClearsEverythingForTheNextMatch()
        {
            var s = new SelfStateHolder();
            s.Feed(5, .5f, 1, 1, 0, 0, 3);
            Assert.That(s.Received, Is.EqualTo(1));
            s.Reset();
            Assert.That(s.Available, Is.False);
            Assert.That(s.Received, Is.EqualTo(0));
            Assert.That(s.SkillCooldown + s.RescueProgress + s.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(s.CollapseLeft, Is.EqualTo(-1f), "the collapse clock reads not running");
            Assert.That(s.TakeSkillReady(), Is.False, "a reset cycle never plays skill_ready");
            s.Advance(10);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.Available, Is.False, "only a packet makes it available");
        }

        [Test]
        public void SkillReadyFiresOnceWhenTheCountdownReachesZero()
        {
            var s = new SelfStateHolder();
            s.Feed(0, 0, 0, 0, 0, 0, 0);
            s.Advance(1);
            Assert.That(s.TakeSkillReady(), Is.False, "a match that starts ready plays nothing");
            s.Feed(12, 0, 0, 0, 0, 0, 2); // the skill was used
            s.Advance(8);
            Assert.That(s.TakeSkillReady(), Is.False);
            s.Feed(5.9f, 0, 0, 0, 0, 0, 8.1); // 1 Hz refresh
            s.Advance(14.1);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.TakeSkillReady(), Is.True, "reached 0 by the local countdown");
            Assert.That(s.TakeSkillReady(), Is.False, "once");
            // A late refresh a few hundredths above 0 never re-opens the cycle.
            s.Feed(.03f, 0, 0, 0, 0, 0, 14.0);
            s.Advance(14.2);
            Assert.That(s.TakeSkillReady(), Is.False);
            // A new use opens a new cycle; a 0 packet ends it at once.
            s.Feed(12, 0, 0, 0, 0, 0, 20);
            s.Feed(0, 0, 0, 0, 0, 0, 20.5);
            Assert.That(s.TakeSkillReady(), Is.True);
        }

        [Test]
        public void TheFirstPacketMidCooldownStillEndsWithSkillReady()
        {
            var s = new SelfStateHolder();
            s.Feed(4, 0, 0, 0, 0, 0, 0); // a reconnect mid-cooldown
            s.Advance(4.01);
            Assert.That(s.TakeSkillReady(), Is.True);
        }

        // GDD v2 has no execution (U6c-wire K1): the two ExecutionGuidance tests that stood here now cover the v0.4 fields
        // that replaced the grace on SelfState: the personal collapse clock and the own hold action.
        [Test]
        public void TheCollapseClockCountsAtItsRateBetweenPacketsAndReadsMinusOneWhenNotRunning()
        {
            var s = new SelfStateHolder();
            s.Feed(0, 0, 0, -1, 0, 0, 10);
            Assert.That(s.CollapseLeft, Is.EqualTo(-1f), "not running");
            s.Advance(20);
            Assert.That(s.CollapseLeft, Is.EqualTo(-1f), "a stopped clock never counts");
            s.Feed(0, 0, 0, 100, 0, 0, 20);
            s.Advance(21);
            Assert.That(s.CollapseLeft, Is.EqualTo(99f).Within(1e-4f));
            s.CollapseRate = GameConstants.CollapseLaggingRate; // the own Survivor is Lagging: 2x
            s.Feed(0, 0, 0, 90, 0, 0, 22);
            s.Advance(23);
            Assert.That(s.CollapseLeft, Is.EqualTo(88f).Within(1e-4f));
            s.Feed(0, 0, 0, float.NaN, 0, 0, 24);
            Assert.That(s.CollapseLeft, Is.EqualTo(-1f), "NaN reads as not running");
        }

        [Test]
        public void TheHoldActionIsShownExactlyAsReceivedAndAnUnknownKindIsNone()
        {
            var s = new SelfStateHolder();
            s.Feed(0, 0, 0, -1, .55f, (byte)ActionKind.Kick, 1);
            Assert.That(s.ActionKind, Is.EqualTo(ActionKind.Kick));
            s.Advance(3);
            Assert.That(s.ActionProgress, Is.EqualTo(.55f), "never extrapolated");
            s.Feed(0, 0, 0, -1, 7f, (byte)ActionKind.Switch, 3);
            Assert.That(s.ActionProgress, Is.EqualTo(1f), "clamped to 0..1");
            s.Feed(0, 0, 0, -1, .5f, 9, 4);
            Assert.That(s.ActionKind, Is.EqualTo(ActionKind.None));
            Assert.That(s.ActionProgress, Is.EqualTo(0f));
            s.Feed(0, 0, 0, -1, .5f, (byte)ActionKind.HatchClose, 5);
            s.Reset();
            Assert.That(s.ActionKind, Is.EqualTo(ActionKind.None));
            Assert.That(s.ActionProgress, Is.EqualTo(0f));
        }
    }
}
