using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.QA.Tests
{
    // M1 minors (11/18): the swing verdict for every swing, the miss timing, Start and the gamepad face buttons on the intro card,
    // and the indexed blip scans.
    public sealed class M1MinorsTests
    {
        [Test]
        public void ALateHitCountsAsAHitAndOnlyAFlagOfHitLengthMakesOne()
        {
            // A hit on a Survivor never seen: no capture confirm, no vanish. Missed at the result window, then the flag falls at 16 ticks.
            var watch = new KillerSwingWatch();
            watch.Observe(true, 600, 50);
            Assert.That(watch.Tick(50 + KillerSwingWatch.ResultSeconds + .001), Is.EqualTo(SwingEvent.Missed));
            Assert.That(watch.Observe(false, 616, 50.85), Is.EqualTo(SwingEvent.LateHit));
            Assert.That(watch.Hits, Is.EqualTo(1)); Assert.That(watch.Misses, Is.Zero);
            // A real miss (20 ticks) never turns into a hit; neither does a stun interrupt (4 ticks).
            watch.Observe(true, 700, 60);
            Assert.That(watch.Tick(60 + KillerSwingWatch.ResultSeconds + .001), Is.EqualTo(SwingEvent.Missed));
            Assert.That(watch.Observe(false, 720, 61.05), Is.EqualTo(SwingEvent.None));
            watch.Observe(true, 800, 70);
            Assert.That(watch.Tick(70 + KillerSwingWatch.ResultSeconds + .001), Is.EqualTo(SwingEvent.Missed));
            Assert.That(watch.Observe(false, 804, 70.7), Is.EqualTo(SwingEvent.None), "interrupted: not a hit");
            Assert.That(watch.Hits, Is.EqualTo(1)); Assert.That(watch.Misses, Is.EqualTo(2));
        }

        [Test]
        public void AFlagThatFallsBeforeTheResultWindowIsJudgedByItsLength()
        {
            var watch = new KillerSwingWatch();
            watch.Observe(true, 10, 1);
            Assert.That(watch.Observe(false, 26, 1.5), Is.EqualTo(SwingEvent.Hit), "hit length with no blip and no confirm");
            watch.Observe(true, 40, 2);
            Assert.That(watch.Observe(false, 60, 2.9), Is.EqualTo(SwingEvent.Missed));
        }

        [Test]
        public void AMissReadsAtTheResultWindowUnlessABlipVanishedThenAtTheFlagFall()
        {
            Assert.That(KillerSwingWatch.ResultSeconds, Is.EqualTo(.55).Within(1e-6));
            var plain = new KillerSwingWatch();
            plain.Observe(true, 10, 1);
            Assert.That(plain.Tick(1 + KillerSwingWatch.ResultSeconds - .01), Is.EqualTo(SwingEvent.None));
            Assert.That(plain.Tick(1 + KillerSwingWatch.ResultSeconds + .001), Is.EqualTo(SwingEvent.Missed), "0.55 s after the flag rose");
            // A blip went Silent near the Killer: it could be an eliminating hit, so the verdict waits for the flag (miss length: 1.0 s).
            var silent = new KillerSwingWatch();
            silent.Observe(true, 10, 1);
            silent.OnVanish(1.2);
            Assert.That(silent.Tick(1.7), Is.EqualTo(SwingEvent.None));
            Assert.That(silent.Observe(false, 30, 2.0), Is.EqualTo(SwingEvent.Missed));
        }

        [Test]
        public void ALateHitReplacesTheMissedLine()
        {
            var fx = new KillerFxState();
            fx.OnMissed(10);
            Assert.That(fx.Line(10.5).Text, Is.EqualTo("Missed"));
            fx.OnHit(10.8);
            Assert.That(fx.Line(10.9).Text, Is.EqualTo(SkillFeedback.Hit));
            Assert.That(fx.Line(10.8 + SkillFeedback.MissedSeconds).Text, Is.Null);
            fx.OnMissed(20);
            Assert.That(fx.Line(20.1).Text, Is.EqualTo("Missed"));
        }

        [Test]
        public void AHeldVerdictWhoseFlagIsStillUpAt18TicksIsAMiss()
        {
            var watch = new KillerSwingWatch();
            watch.Observe(true, 100, 1);
            watch.OnVanish(1.2);
            Assert.That(watch.Observe(true, 116, 1.8), Is.EqualTo(SwingEvent.None), "16 ticks: could still be a hit that falls next");
            Assert.That(watch.Observe(true, 117, 1.85), Is.EqualTo(SwingEvent.None), "17 ticks: still the hit length");
            Assert.That(watch.Observe(true, 118, 1.9), Is.EqualTo(SwingEvent.Missed), "18 ticks: past the hit length, 0.9 s");
            Assert.That(watch.Misses, Is.EqualTo(1)); Assert.That(watch.Hits, Is.Zero);
            Assert.That(watch.Observe(false, 120, 2.0), Is.EqualTo(SwingEvent.None), "the fall adds nothing");
            Assert.That(watch.Misses, Is.EqualTo(1));
            // A hit's flag falls at 16: still a hit, and an unheld flag-up swing is never cut short.
            watch.Observe(true, 200, 10);
            watch.OnVanish(10.2);
            Assert.That(watch.Observe(false, 216, 10.8), Is.EqualTo(SwingEvent.Hit));
            Assert.That(watch.LastHitWasHeld, Is.True);
            watch.Observe(true, 300, 20);
            Assert.That(watch.Observe(true, 320, 21), Is.EqualTo(SwingEvent.None));
        }

        [Test]
        public void ANewSwingOrAConfirmedHitClearsAStaleMissedLine()
        {
            var fx = new KillerFxState();
            fx.OnMissed(10);
            Assert.That(fx.Line(10.4).Text, Is.EqualTo("Missed"));
            fx.ClearMissed();
            Assert.That(fx.Line(10.5).Text, Is.Null);
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(QaSource.MethodText(code, "void ObserveOwnSwing(").Substring(0, 300), Does.Contain("Fx.ClearMissed()"));
            Assert.That(code, Does.Contain("killerLayer.Fx.ClearMissed(); dev?.OnSwing(SwingEvent.Hit);"), "the capture-confirm hit");
        }

        [Test]
        public void TheImpactPlaysOnlyWhenTheVerdictWasHeldForAVanish()
        {
            var watch = new KillerSwingWatch();
            watch.Observe(true, 100, 1);
            Assert.That(watch.Observe(false, 116, 1.8), Is.EqualTo(SwingEvent.Hit), "no vanish, fall seen before the window ran out");
            Assert.That(watch.LastHitWasHeld, Is.False, "no blip position belongs to this swing");
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(code, Does.Contain("swingWatch.LastHitWasHeld) PlayImpact(killerLayer.VanishedPoint)"));
        }

        [Test]
        public void ClientAppPlaysTheHitFeedbackForALateHit()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string observe = QaSource.MethodText(code, "void ObserveOwnSwing(");
            string late = observe.Substring(observe.IndexOf("SwingEvent.LateHit"));
            Assert.That(late, Does.Contain("PlayUnseenHit()").And.Contain("Fx.OnHit("));
            Assert.That(late, Does.Not.Contain("PlayImpact"), "no blip position is known for a late hit");
        }

        [Test]
        public void StartAndTheGamepadFaceButtonsEndTheIntroCard()
        {
            Assert.That(IntroCardState.DismissInput(false, false, false, false, true), Is.True, "gamepad A or B");
            Assert.That(IntroCardState.DismissInput(false, false, false, false), Is.False);
            var card = new IntroCardState();
            card.Begin(Role.Survivor, SkillIds.EmergencyRestore, 100);
            Assert.That(card.ConsumeBack(101, true, false, true), Is.False, "the menu is open: Start closes the menu, not the card");
            Assert.That(card.ConsumeBack(101, false, false, false), Is.False);
            Assert.That(card.ConsumeBack(101, false, false, true), Is.True, "Start on the card: consumed, no menu");
            Assert.That(card.Visible(101, false), Is.False);
            Assert.That(card.ConsumeBack(102, false, false, true), Is.False, "nothing to consume once it is gone: Start opens the menu");
            card.Begin(Role.Killer, SkillIds.Overclock, 200);
            Assert.That(card.ConsumeBack(201, false, true, false), Is.True, "Esc still works");
        }

        [Test]
        public void TheBlipScansUseIndexedLoops()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Killer/KillerWorldLayer.cs"));
            foreach (string name in new[] { "float NearestNormalBlip(", "float NearestSwingableBlip(" })
            {
                string body = QaSource.MethodText(code, name);
                Assert.That(body, Does.Contain("for (int i").And.Not.Contain("foreach"), name);
            }
        }
    }
}
