using NUnit.Framework;
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
            s.Feed(12f, .4f, 2f, 1.6f, 100);
            Assert.That(s.Available, Is.True);
            s.Advance(100.5);
            Assert.That(s.SkillCooldown, Is.EqualTo(11.5f).Within(1e-4f));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(1.1f).Within(1e-4f));
            Assert.That(s.RescueProgress, Is.EqualTo(.4f), "rescue progress is never extrapolated");
            s.Advance(102.0);
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f), "floored at 0");
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(0f));
            Assert.That(s.SkillCooldown, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(s.RescueProgress, Is.EqualTo(.4f));
            s.Advance(500);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
        }

        [Test]
        public void EveryPacketSnapsTheValuesAndTimeNeverRunsBackwards()
        {
            var s = new SelfStateHolder();
            s.Feed(12f, 0, 0, 0, 10);
            s.Advance(11);
            Assert.That(s.SkillCooldown, Is.EqualTo(11f).Within(1e-4f));
            // The 1 Hz refresh says 10.8 (the local clock ran 0.2 s late): snap to the server's value.
            s.Feed(10.8f, .35f, 0, 0, 11.05);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.8f).Within(1e-4f));
            Assert.That(s.RescueProgress, Is.EqualTo(.35f));
            s.Advance(11.0); // an older frame time is ignored
            Assert.That(s.SkillCooldown, Is.EqualTo(10.8f).Within(1e-4f));
            s.Advance(11.55);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.3f).Within(1e-4f));
            s.Advance(double.NaN);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.3f).Within(1e-4f), "a garbage clock changes nothing");
            s.Feed(0, 0, 0, 0, 12);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
        }

        [Test]
        public void GarbageValuesAreCleanedAndAFeedWithoutATimeUsesTheClock()
        {
            var s = new SelfStateHolder();
            s.Advance(50);
            s.Feed(float.NaN, 7f, -2f, float.PositiveInfinity);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.RescueProgress, Is.EqualTo(1f));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(3600f));
            s.Advance(51);
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(3599f).Within(1e-3f));
        }

        [Test]
        public void ResetClearsEverythingForTheNextMatch()
        {
            var s = new SelfStateHolder();
            s.Feed(5, .5f, 1, 1, 3);
            Assert.That(s.Received, Is.EqualTo(1));
            s.Reset();
            Assert.That(s.Available, Is.False);
            Assert.That(s.Received, Is.EqualTo(0));
            Assert.That(s.SkillCooldown + s.RescueProgress + s.InvulnerableLeft + s.ExecutionGraceLeft, Is.EqualTo(0f));
            Assert.That(s.TakeSkillReady(), Is.False, "a reset cycle never plays skill_ready");
            s.Advance(10);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.Available, Is.False, "only a packet makes it available");
        }

        [Test]
        public void SkillReadyFiresOnceWhenTheCountdownReachesZero()
        {
            var s = new SelfStateHolder();
            s.Feed(0, 0, 0, 0, 0);
            s.Advance(1);
            Assert.That(s.TakeSkillReady(), Is.False, "a match that starts ready plays nothing");
            s.Feed(12, 0, 0, 0, 2); // the skill was used
            s.Advance(8);
            Assert.That(s.TakeSkillReady(), Is.False);
            s.Feed(5.9f, 0, 0, 0, 8.1); // 1 Hz refresh
            s.Advance(14.1);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.TakeSkillReady(), Is.True, "reached 0 by the local countdown");
            Assert.That(s.TakeSkillReady(), Is.False, "once");
            // A late refresh a few hundredths above 0 never re-opens the cycle.
            s.Feed(.03f, 0, 0, 0, 14.0);
            s.Advance(14.2);
            Assert.That(s.TakeSkillReady(), Is.False);
            // A new use opens a new cycle; a 0 packet ends it at once.
            s.Feed(12, 0, 0, 0, 20);
            s.Feed(0, 0, 0, 0, 20.5);
            Assert.That(s.TakeSkillReady(), Is.True);
        }

        [Test]
        public void TheFirstPacketMidCooldownStillEndsWithSkillReady()
        {
            var s = new SelfStateHolder();
            s.Feed(4, 0, 0, 0, 0); // a reconnect mid-cooldown
            s.Advance(4.01);
            Assert.That(s.TakeSkillReady(), Is.True);
        }

        [Test]
        public void TheExecutionPromptFollowsTheCountingGraceBetweenSnapshots()
        {
            var g = new ExecutionGuidance();
            Assert.That(g.Update(5, .8f, true, 1.9f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Display(.4f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Display(0), Is.EqualTo(ExecutionPrompt.StepBack), "grace over, still in contact and never stepped out");
            Assert.That(g.Display(float.NaN), Is.EqualTo(ExecutionPrompt.StepBack));
            g.Update(5, 2f, true, .3f);
            Assert.That(g.Display(.2f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Display(0), Is.EqualTo(ExecutionPrompt.Ready), "stepped out during the grace: armed");
            g.Update(-1, 0, true, 0);
            Assert.That(g.Display(1f), Is.EqualTo(ExecutionPrompt.None), "no target, no prompt");
        }

        [Test]
        public void ReRunningTheGuidanceForTheSameSnapshotWithAFreshGraceIsStable()
        {
            // ClientApp feeds the SelfState after the WorldState of the same tick; the layer re-runs Update for that snapshot.
            var g = new ExecutionGuidance();
            Assert.That(g.Update(9, .9f, true, 0), Is.EqualTo(ExecutionPrompt.StepBack), "capture tick: the old SelfState still says 0");
            Assert.That(g.Update(9, .9f, true, 2f), Is.EqualTo(ExecutionPrompt.Grace), "the same snapshot with the fresh grace");
            Assert.That(g.Armed, Is.False);
            Assert.That(g.Update(9, .9f, true, 2f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Armed, Is.False);
        }
    }
}
