using NUnit.Framework;
using PingTheKiller.Client.QA.Tests;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests.Reverify
{
    // Re-verification Attacks 5 & 6: Threat visuals and capture / elimination feedback (Area C of Reverify-flash.md).
    // - Threat visuals, HUD_SPEC 4.2 as amended (10/30): strip 8 u tall at 45 % density; levels 1, 2 and 3;
    //   mask re-rolled only on a level change; hidden while Eliminated or Escaped.
    // - Capture cue and banner; elimination reason inference (timeout, executed, caught a second time).
    [TestFixture]
    public sealed class ThreatAndCaptureFeedbackReverifyTests
    {
        [Test]
        public void ThreatVisual_Constants_MatchAmendedSpec()
        {
            Assert.That(ThreatVisual.StripHeight, Is.EqualTo(8f), "Strip height is 8 u");
            Assert.That(ThreatVisual.StripDensity, Is.EqualTo(0.45f).Within(1e-4f), "Strip density is 45 %");
            Assert.That(ThreatVisual.Density, Is.EqualTo(0.30f).Within(1e-4f), "Band density is 30 %");
            Assert.That(ThreatVisual.BandWidth, Is.EqualTo(24f), "Band width is 24 u");
            Assert.That(ThreatVisual.StripCell, Is.EqualTo(2f), "Texel size is 2 u");
            Assert.That(ThreatVisual.Cell, Is.EqualTo(2f));
        }

        [Test]
        public void ThreatVisual_EffectsAndFractions_FollowLevelsCorrectly()
        {
            var tv = new ThreatVisual();

            // Level 0: None
            tv.Update(0, PlayerStatus.Normal);
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.None));
            Assert.That(tv.StripFraction, Is.EqualTo(0f));

            // Level 1: MeterStrip covering 1/3
            tv.Update(1, PlayerStatus.Normal);
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.MeterStrip));
            Assert.That(tv.StripFraction, Is.EqualTo(1f / 3f).Within(1e-4f));

            // Level 2: MeterStrip covering 2/3
            tv.Update(2, PlayerStatus.Normal);
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.MeterStrip));
            Assert.That(tv.StripFraction, Is.EqualTo(2f / 3f).Within(1e-4f));

            // Level 3: ScreenEdge
            tv.Update(3, PlayerStatus.Normal);
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.ScreenEdge));
        }

        [Test]
        public void ThreatVisual_MaskRerolledOnlyOnLevelChange()
        {
            var tv = new ThreatVisual();
            tv.Update(1, PlayerStatus.Normal);
            int initialSeed = tv.Seed;
            int initialRolls = tv.Rolls;
            Assert.That(initialRolls, Is.EqualTo(1));

            // Updating repeatedly with the same level must NOT re-roll the mask
            for (int i = 0; i < 50; i++)
            {
                bool changed = tv.Update(1, PlayerStatus.Normal);
                Assert.That(changed, Is.False);
                Assert.That(tv.Seed, Is.EqualTo(initialSeed));
                Assert.That(tv.Rolls, Is.EqualTo(initialRolls));
            }

            // Changing level triggers re-roll
            bool changedTo2 = tv.Update(2, PlayerStatus.Normal);
            Assert.That(changedTo2, Is.True);
            Assert.That(tv.Rolls, Is.EqualTo(2));
            Assert.That(tv.Seed, Is.Not.EqualTo(initialSeed));
        }

        [Test]
        public void ThreatVisual_HiddenWhileEliminatedOrEscaped()
        {
            var tv = new ThreatVisual();
            // Threat 3 while Normal
            tv.Update(3, PlayerStatus.Normal);
            Assert.That(tv.Level, Is.EqualTo(3));
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.ScreenEdge));

            // Transition to Eliminated -> effective threat becomes 0 (hidden)
            tv.Update(3, PlayerStatus.Eliminated);
            Assert.That(tv.Level, Is.EqualTo(0));
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.None));

            // Threat while Escaped -> hidden
            tv.Update(2, PlayerStatus.Escaped);
            Assert.That(tv.Level, Is.EqualTo(0));
            Assert.That(tv.Effect, Is.EqualTo(ThreatEffect.None));

            // Out-of-range threat values read as 0
            Assert.That(ThreatVisual.Effective(4, PlayerStatus.Normal), Is.EqualTo(0));
            Assert.That(ThreatVisual.Effective(255, PlayerStatus.Normal), Is.EqualTo(0));
        }

        [Test]
        public void CaptureWatch_NormalToLagging_FiresCapturedAndShowsBannerFor3Seconds()
        {
            var watch = new CaptureWatch();
            // First snapshot establishes baseline
            watch.Observe(PlayerStatus.Normal, 0, 0, 1.0);

            // Transition to Lagging
            StatusEvent evt = watch.Observe(PlayerStatus.Lagging, 60f, 1, 2.0);
            Assert.That(evt, Is.EqualTo(StatusEvent.Captured));
            Assert.That(watch.Banners, Is.EqualTo(1));

            // Banner visible at 2.5s (within 3s window)
            Assert.That(watch.BannerVisible(4.9), Is.True);

            // Banner expires at 3.0s after capture (t=5.0)
            Assert.That(watch.BannerVisible(5.1), Is.False);

            // Banner wording check
            string banner = CaptureWatch.Banner;
            Assert.That(banner, Is.EqualTo("Lagging. A Survivor holding [E] within 1.5 m for 3 s restores you · 60 s left"));
            Assert.That(CaptureWatch.BannerPrompt.Danger, Is.True);
        }

        [Test]
        public void CaptureWatch_BannerDisappearsImmediatelyWhenLeavingLagging()
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 1.0);
            watch.Observe(PlayerStatus.Lagging, 60f, 1, 2.0);
            Assert.That(watch.BannerVisible(2.5), Is.True);

            // Rescued before 3s window ends
            watch.Observe(PlayerStatus.Normal, 0, 1, 3.0);
            Assert.That(watch.BannerVisible(3.1), Is.False, "Banner is cleared immediately on leaving Lagging");
        }

        [Test]
        public void CaptureWatch_EliminationReasonInference_AllThreeCases()
        {
            // Case 1: Timeout (last Lagging StatusTimer <= 0.2 s)
            var reason1 = CaptureWatch.Infer(PlayerStatus.Lagging, 0.15f, 1);
            Assert.That(reason1, Is.EqualTo(EliminationReason.TimedOut));
            Assert.That(CaptureWatch.ReasonText(reason1), Is.EqualTo("Disconnected: no rescue in the second Lagging stage (30 s)."));

            var reason1Boundary = CaptureWatch.Infer(PlayerStatus.Lagging, 0.20f, 1);
            Assert.That(reason1Boundary, Is.EqualTo(EliminationReason.TimedOut));

            // Case 2 (GDD v2, no execution): Lagging -> Eliminated with time left is the collapse while it runs, else unknown
            var reason2 = CaptureWatch.Infer(PlayerStatus.Lagging, 0.25f, 1, true);
            Assert.That(reason2, Is.EqualTo(EliminationReason.Collapse));
            Assert.That(CaptureWatch.ReasonText(reason2), Is.EqualTo("Disconnected: the shutdown reached you."));

            var reason2Fresh = CaptureWatch.Infer(PlayerStatus.Lagging, 58.0f, 1);
            Assert.That(reason2Fresh, Is.EqualTo(EliminationReason.None));

            // Case 3: Caught a third time (Normal -> Eliminated with CaptureCount >= 2)
            var reason3 = CaptureWatch.Infer(PlayerStatus.Normal, 0f, 2);
            Assert.That(reason3, Is.EqualTo(EliminationReason.ThirdCapture));
            Assert.That(CaptureWatch.ReasonText(reason3), Is.EqualTo("Disconnected: caught with no Lagging stage left."));

            var reason3Once = CaptureWatch.Infer(PlayerStatus.Normal, 0f, 1);
            Assert.That(reason3Once, Is.EqualTo(EliminationReason.None), "one capture only lags");

            // Unhandled cases return None
            Assert.That(CaptureWatch.Infer(PlayerStatus.Normal, 0f, 0), Is.EqualTo(EliminationReason.None));
            Assert.That(CaptureWatch.Infer(PlayerStatus.Escaped, 0f, 0), Is.EqualTo(EliminationReason.None));
        }

        // GDD v2 / HUD_SPEC 13.7 (U6c-wire R1): the v1 outcome line carried the inferred reason; the v2 result names the
        // outcome only, and the reason stays on the meter.
        [Test]
        public void ResultV2_Outcome_NamesTheOwnOutcomeWithoutAReason()
        {
            var result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            result.EliminatedIds.Add(1);
            result.EscapedIds.Add(2);
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(result, null, 1, Role.Survivor)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(result, null, 2, Role.Survivor)), Is.EqualTo("You escaped."));
        }
    }
}
