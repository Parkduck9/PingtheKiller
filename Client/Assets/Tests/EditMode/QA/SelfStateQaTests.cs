using NUnit.Framework;
using PingTheKiller.Client.World;

namespace PingTheKiller.Client.QA.Tests
{
    // U4d QA attempt 1, test item 1. Written from U4d-a1.md scope 2 and HUD_SPEC 4.4 / 4.8 / 6.5: the local countdown of the
    // three S2C_SelfState timers, snapping to every packet, RescueProgress as received, Available, and the skill_ready edge.
    // Pure code: no Unity API is needed, so these also run in a plain .NET runner.
    [Category("U4dQaA1")]
    public sealed class SelfStateQaTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void NothingIsAvailableBeforeTheFirstPacketAndTimeAloneNeverMakesItAvailable()
        {
            var s = new SelfStateHolder();
            Assert.That(s.Available, Is.False);
            Assert.That(s.Received, Is.EqualTo(0));
            s.Advance(10); s.Advance(20);
            Assert.That(s.Available, Is.False, "only a packet makes the seam available");
            Assert.That(s.SkillCooldown + s.RescueProgress + s.InvulnerableLeft + s.ExecutionGraceLeft, Is.EqualTo(0f));
            Assert.That(s.TakeSkillReady(), Is.False);
            s.Feed(0, 0, 0, 0, 21);
            Assert.That(s.Available, Is.True);
            Assert.That(s.Received, Is.EqualTo(1));
        }

        [Test]
        public void ThreeTimersCountDownByRealTimeTogetherAndFloorAtExactlyZero()
        {
            var s = new SelfStateHolder();
            s.Feed(15f, 0f, 2f, 1.5f, 100.0);
            s.Advance(100.25);
            Assert.That(s.SkillCooldown, Is.EqualTo(14.75f).Within(Eps));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(1.75f).Within(Eps));
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(1.25f).Within(Eps));
            s.Advance(101.5);
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(0f), "grace floors at 0, never negative");
            Assert.That(s.InvulnerableLeft, Is.EqualTo(.5f).Within(Eps));
            s.Advance(102.0);
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(s.SkillCooldown, Is.EqualTo(13f).Within(Eps));
            s.Advance(1000.0);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(0f));
            Assert.That(s.SkillCooldown, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void EveryPacketSnapsEachTimerUpAndDownNotJustTheCooldown()
        {
            var s = new SelfStateHolder();
            s.Feed(5f, 0f, 0f, 0f, 0.0);
            s.Advance(1.0);
            Assert.That(s.SkillCooldown, Is.EqualTo(4f).Within(Eps));
            // Up: a new use of the skill, a fresh rescue protection and a fresh grace.
            s.Feed(15f, 0f, 2f, 2f, 1.0);
            Assert.That(s.SkillCooldown, Is.EqualTo(15f).Within(Eps));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(2f).Within(Eps));
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(2f).Within(Eps));
            s.Advance(1.5);
            // Down: the local clock ran 0.3 s late, the server says less is left.
            s.Feed(11f, 0f, .4f, .1f, 1.5);
            Assert.That(s.SkillCooldown, Is.EqualTo(11f).Within(Eps), "snap down");
            Assert.That(s.InvulnerableLeft, Is.EqualTo(.4f).Within(Eps));
            Assert.That(s.ExecutionGraceLeft, Is.EqualTo(.1f).Within(Eps));
            // The snapped values are the new baseline for the countdown.
            s.Advance(2.0);
            Assert.That(s.SkillCooldown, Is.EqualTo(10.5f).Within(Eps));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f));
            // And a packet that says 0 empties the timers at once even though the local value was above 0.
            s.Feed(0f, 0f, 0f, 0f, 2.0);
            Assert.That(s.SkillCooldown + s.InvulnerableLeft + s.ExecutionGraceLeft, Is.EqualTo(0f));
        }

        [Test]
        public void APacketStampedEarlierThanTheLastFrameIsCountedDownFromItsOwnArrivalTime()
        {
            // The receive thread stamps the packet; the main thread may already have advanced past it.
            var s = new SelfStateHolder();
            s.Advance(50.0);
            s.Feed(10f, 0f, 0f, 0f, 49.5);
            Assert.That(s.SkillCooldown, Is.EqualTo(9.5f).Within(Eps), "0.5 s of the 10 s already elapsed");
        }

        [Test]
        public void RescueProgressIsShownExactlyAsReceivedAndNeverExtrapolated()
        {
            var s = new SelfStateHolder();
            s.Feed(0f, .30f, 0f, 0f, 10.0);
            for (double t = 10.0; t < 40.0; t += .016)
            {
                s.Advance(t);
                Assert.That(s.RescueProgress, Is.EqualTo(.30f), "no estimate between packets, t=" + t);
            }
            s.Feed(0f, .55f, 0f, 0f, 40.0);
            Assert.That(s.RescueProgress, Is.EqualTo(.55f));
            s.Feed(0f, 0f, 0f, 0f, 40.1);
            Assert.That(s.RescueProgress, Is.EqualTo(0f), "a 0 packet ends the rescue display at once");
        }

        [Test]
        public void SkillReadyFiresOnceForTheWholeCooldownEvenWithPerFramePollingAndOneHertzRefreshes()
        {
            var s = new SelfStateHolder();
            s.Feed(15f, 0, 0, 0, 0.0);
            int edges = 0;
            double nextRefresh = 1.0;
            for (double t = 0; t <= 20.0; t += 1.0 / 60)
            {
                s.Advance(t);
                if (t >= nextRefresh)
                {
                    float server = (float)System.Math.Max(0, 15.0 - t - .01); // the server's value, a hair ahead of the client
                    s.Feed(server, 0, 0, 0, t);
                    nextRefresh += 1.0;
                }
                if (s.TakeSkillReady()) edges++;
            }
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(edges, Is.EqualTo(1), "one skill_ready per cooldown, not one per frame");
        }

        [Test]
        public void SkillReadyDoesNotFireAtMatchStartOrForAReadySkillThatStaysReady()
        {
            var s = new SelfStateHolder();
            s.Feed(0, 0, 0, 0, 0.0);              // first packet of a match: ready already
            for (double t = 0; t < 10; t += .05) { s.Advance(t); Assert.That(s.TakeSkillReady(), Is.False, "t=" + t); }
            s.Feed(0, 0, 0, 0, 10.0);             // the 1 Hz refresh of a ready skill
            s.Advance(11);
            Assert.That(s.TakeSkillReady(), Is.False);
        }

        [Test]
        public void SkillReadyFiresAgainForTheNextCooldownAndNeverForARefreshOfNearlyZero()
        {
            var s = new SelfStateHolder();
            s.Feed(15f, 0, 0, 0, 0.0);
            s.Advance(15.1);
            Assert.That(s.TakeSkillReady(), Is.True);
            Assert.That(s.TakeSkillReady(), Is.False, "the edge is consumed");
            s.Feed(.04f, 0, 0, 0, 15.1);          // a late refresh must not re-open the finished cycle
            s.Advance(15.3);
            Assert.That(s.TakeSkillReady(), Is.False);
            s.Feed(15f, 0, 0, 0, 30.0);           // the skill is used again
            s.Advance(44.0);
            Assert.That(s.TakeSkillReady(), Is.False, "still cooling down");
            s.Advance(45.1);
            Assert.That(s.TakeSkillReady(), Is.True, "second cycle");
        }

        [Test]
        public void ResetDropsAnOpenCooldownCycleSoNoStaleReadyCueLeaksIntoTheNextMatch()
        {
            var s = new SelfStateHolder();
            s.Feed(15f, .5f, 1f, 1f, 0.0);
            s.Advance(14.0);
            s.Reset();                             // match end, leave, reconnect: the cycle was in flight
            Assert.That(s.Available, Is.False);
            Assert.That(s.Received, Is.EqualTo(0));
            Assert.That(s.SkillCooldown + s.RescueProgress + s.InvulnerableLeft + s.ExecutionGraceLeft, Is.EqualTo(0f));
            s.Advance(30.0);
            Assert.That(s.TakeSkillReady(), Is.False);
            s.Feed(0, 0, 0, 0, 31.0);              // the new match starts ready
            s.Advance(32.0);
            Assert.That(s.TakeSkillReady(), Is.False, "the new match's own start plays nothing");
            Assert.That(s.Available, Is.True);
        }

        [Test]
        public void ResetAlsoDropsAnEdgeThatWasReachedButNotYetTaken()
        {
            var s = new SelfStateHolder();
            s.Feed(1f, 0, 0, 0, 0.0);
            s.Advance(2.0);                        // the edge is pending
            s.Reset();
            Assert.That(s.TakeSkillReady(), Is.False, "a pending edge from the previous match is cleared");
        }

        [Test]
        public void GarbageInputsNeverProduceNegativeNaNOrAbsurdValues()
        {
            var s = new SelfStateHolder();
            s.Feed(float.NaN, float.NegativeInfinity, -5f, float.PositiveInfinity, 1.0);
            Assert.That(s.SkillCooldown, Is.EqualTo(0f));
            Assert.That(s.RescueProgress, Is.EqualTo(0f));
            Assert.That(s.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(s.ExecutionGraceLeft, Is.LessThanOrEqualTo(3600f));
            s.Feed(1f, 9f, 1f, 1f, double.NaN);
            Assert.That(s.RescueProgress, Is.EqualTo(1f), "rescue is a 0..1 fraction");
            s.Advance(double.PositiveInfinity);
            s.Advance(double.NaN);
            Assert.That(float.IsNaN(s.SkillCooldown), Is.False);
            Assert.That(s.SkillCooldown, Is.EqualTo(1f).Within(Eps), "a garbage clock changes nothing");
        }
    }
}
