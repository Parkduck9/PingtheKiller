using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // U4e: the Killer model for Survivors (U2e contract), threat visuals (HUD_SPEC 4.2), capture banner, elimination reason,
    // the Lagging stutter clock, the dev fake-Killer injector and the U4d minor fixes F-01..F-03.
    public sealed class SurvivorFeedbackTests
    {
        private const double Tick = .05;
        private const int KillerId = 3, OwnId = 7;

        private static S2C_WorldState Snapshot(uint tick, float? killerX, float killerZ = 0, float killerYaw = 0, byte threat = 0)
        {
            var state = new S2C_WorldState { Tick = tick, ThreatLevel = threat };
            state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = 1, Z = 1, Flags = (byte)PlayerFlags.Signal });
            if (killerX.HasValue) state.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = killerX.Value, Z = killerZ, Yaw = killerYaw });
            return state;
        }

        // Snapshot n arrives at n * 50 ms; the render time is now - 100 ms.
        private static SnapshotBuffer Feed(int count, System.Func<int, float?> killerX)
        {
            var buffer = new SnapshotBuffer();
            for (int i = 1; i <= count; i++) buffer.Add(Snapshot((uint)i, killerX(i)), i * Tick);
            return buffer;
        }

        private static double RenderAt(double renderTime) => renderTime + SnapshotBuffer.Delay;

        // ---- 1. Killer model: present / absent / fade / snap ------------------------------------------------------------

        [Test]
        public void APresentKillerEntryIsInterpolatedAndFacesItsYaw()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(1, 10, 5, 0), 1 * Tick);
            buffer.Add(Snapshot(2, 12, 5, 90), 2 * Tick);
            Assert.That(buffer.SampleKiller(RenderAt(1.5 * Tick), out Vector3 p, out float yaw, out float alpha), Is.True);
            Assert.That(p.x, Is.EqualTo(11).Within(1e-4));
            Assert.That(p.z, Is.EqualTo(5).Within(1e-4));
            Assert.That(yaw, Is.EqualTo(45).Within(1e-3));
            Assert.That(alpha, Is.EqualTo(1));
        }

        [Test]
        public void NoKillerEntryMeansNoModel()
        {
            SnapshotBuffer buffer = Feed(10, i => null);
            for (double t = 0; t < 1; t += .01) Assert.That(buffer.SampleKiller(t, out _, out _, out _), Is.False, "t=" + t);
        }

        [Test]
        public void AnEntryThatDisappearsFadesOutOverThreeTenthsOfASecondAtItsLastPositionWithoutMoving()
        {
            // Present (moving +1 m per tick) in 1..5, absent from 6 on.
            SnapshotBuffer buffer = Feed(20, i => i <= 5 ? i : (float?)null);
            double absentAt = 6 * Tick;
            Assert.That(buffer.SampleKiller(RenderAt(5 * Tick), out Vector3 p, out _, out float alpha), Is.True);
            Assert.That(p.x, Is.EqualTo(5).Within(1e-4), "held at the last received position, not extrapolated toward 6");
            Assert.That(alpha, Is.EqualTo(1));
            foreach (double after in new[] { 0, .075, .15, .225, .29 })
            {
                Assert.That(buffer.SampleKiller(RenderAt(absentAt + after), out p, out _, out alpha), Is.True, "+" + after);
                Assert.That(p.x, Is.EqualTo(5).Within(1e-4), "never keeps moving, +" + after);
                Assert.That(alpha, Is.EqualTo(1 - after / SnapshotBuffer.KillerFade).Within(1e-3), "+" + after);
            }
            Assert.That(SnapshotBuffer.KillerFade, Is.EqualTo(.3).Within(1e-9));
            Assert.That(buffer.SampleKiller(RenderAt(absentAt + .301), out _, out _, out _), Is.False, "gone after 0.3 s");
            Assert.That(buffer.SampleKiller(RenderAt(absentAt + .5), out _, out _, out _), Is.False);
        }

        [Test]
        public void AnEntryThatReappearsAfterMoreThanThreeTenthsOfASecondSnapsAndIsNeverInterpolatedAcrossTheGap()
        {
            // At x = 0 in 1..5, hidden in 6..15 (0.5 s), then at x = 30 from 16 on.
            SnapshotBuffer buffer = Feed(25, i => i <= 5 ? 0 : i >= 16 ? 30 : (float?)null);
            for (double r = 5 * Tick; r < 16 * Tick - 1e-6; r += .01)
                if (buffer.SampleKiller(RenderAt(r), out Vector3 p, out _, out _))
                    Assert.That(p.x, Is.EqualTo(0).Within(1e-4), "no point of the hidden path is drawn, r=" + r);
            Assert.That(buffer.SampleKiller(RenderAt(16 * Tick + .001), out Vector3 back, out _, out float alpha), Is.True);
            Assert.That(back.x, Is.EqualTo(30).Within(1e-4), "snaps to the new position");
            Assert.That(alpha, Is.EqualTo(1), "and is fully shown at once");
        }

        [Test]
        public void AOneTickGapIsBridgedInsteadOfFlickering()
        {
            // At 0, 1, 2 in 1..3, missing in 4, at 4 in 5 (gap of 0.1 s between sightings, within the 0.3 s fade).
            SnapshotBuffer buffer = Feed(8, i => i <= 3 ? i - 1 : i == 5 ? 4 : (float?)null);
            Assert.That(buffer.SampleKiller(RenderAt(4 * Tick), out Vector3 p, out _, out float alpha), Is.True);
            Assert.That(alpha, Is.EqualTo(1));
            Assert.That(p.x, Is.EqualTo(3).Within(1e-4), "halfway between the two sightings");
        }

        [Test]
        public void TheKillerIsFoundByRoleAndASurvivorWithTheKillersIdIsNeverDrawnAsTheKiller()
        {
            var buffer = new SnapshotBuffer();
            for (uint t = 1; t <= 4; t++)
            {
                var state = new S2C_WorldState { Tick = t, ThreatLevel = 3 };
                state.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Survivor, X = 50, Z = 50 });
                buffer.Add(state, t * Tick);
            }
            Assert.That(buffer.SampleKiller(RenderAt(3 * Tick), out _, out _, out _), Is.False);
        }

        // ---- 1b. audit: no Killer rendering from any source other than the Killer entry -----------------------------------

        [Test]
        public void TheKillerModelIsPlacedOnlyFromTheKillerEntryOfTheSnapshots()
        {
            string world = Strip(string.Join('\n', WorldFiles.Select(Read)));
            string render = Method(world, "private void RenderKiller(");
            Assert.That(render, Is.Not.Null);
            Assert.That(render, Does.Contain("snapshots.SampleKiller(now, out Vector3 position, out float yaw, out float alpha)"));
            Assert.That(render, Does.Contain("SetPositionAndRotation(new Vector3(position.x, 0, position.z), Quaternion.Euler(0, yaw, 0))"));
            // The model is touched nowhere else in WorldView except its creation, and only Survivor clients build it.
            foreach (Match m in Regex.Matches(world, @"killerModel\.Root\.transform"))
                Assert.That(render.Contains("killerModel.Root.transform"), Is.True);
            Assert.That(Regex.Matches(world, @"killerModel\.Root\.transform").Count, Is.EqualTo(1), "one placement, inside RenderKiller");
            Assert.That(world, Does.Contain("if (role == Role.Survivor) killerModel = MakeKillerModel();"));
            Assert.That(world, Does.Not.Contain("ThreatLevel"), "the threat level never places anything in the world");
            Assert.That(world, Does.Not.Contain("SkillEffect"), "nor does any skill effect");
            // No other script draws the Killer for a Survivor: nothing else samples the Killer entry or names the model.
            foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (WorldFiles.Select(Path.GetFileName).Contains(name) || name == "SnapshotBuffer.cs") continue;
                string code = Strip(File.ReadAllText(path));
                Assert.That(code, Does.Not.Contain("SampleKiller("), name);
                Assert.That(code, Does.Not.Contain("killerModel"), name);
                Assert.That(code, Does.Not.Contain("KillerBodyScale"), name);
            }
            // The Survivor HUD files carry no Killer position: no marker, outline or through-wall indicator for the Killer.
            foreach (string relative in new[] { "UI/SurvivorHud.cs", "UI/SurvivorReadout.cs", "UI/SurvivorFeedback.cs", "World/LaggingStutter.cs" })
            {
                string code = Strip(Read(relative));
                Assert.That(code, Does.Not.Match(@"Role\.Killer"), relative + " reads a Killer entry");
                Assert.That(code, Does.Not.Contain("Players"), relative + " reads the player list");
            }
        }

        // ---- 2. threat visuals --------------------------------------------------------------------------------------------

        [TestCase(0, ThreatEffect.None, 0f)]
        [TestCase(1, ThreatEffect.MeterStrip, 1f / 3)]
        [TestCase(2, ThreatEffect.MeterStrip, 2f / 3)]
        [TestCase(3, ThreatEffect.ScreenEdge, 0f)]
        [TestCase(4, ThreatEffect.None, 0f)]
        [TestCase(255, ThreatEffect.None, 0f)]
        public void EachLevelHasExactlyOneScreenEffect(int level, ThreatEffect effect, float fraction)
        {
            var visual = new ThreatVisual(new System.Random(1));
            visual.Update((byte)level, PlayerStatus.Normal);
            Assert.That(visual.Effect, Is.EqualTo(effect));
            Assert.That(visual.StripFraction, Is.EqualTo(fraction).Within(1e-6));
        }

        [TestCase(PlayerStatus.Eliminated)]
        [TestCase(PlayerStatus.Escaped)]
        public void TheCueIsHiddenWhileEliminatedOrEscaped(PlayerStatus status)
        {
            var visual = new ThreatVisual(new System.Random(1));
            visual.Update(3, PlayerStatus.Normal);
            Assert.That(visual.Effect, Is.EqualTo(ThreatEffect.ScreenEdge));
            visual.Update(3, status);
            Assert.That(visual.Effect, Is.EqualTo(ThreatEffect.None));
            Assert.That(ThreatVisual.Effective(2, status), Is.EqualTo(0), "the beep gets the same hidden level");
            Assert.That(ThreatVisual.Effective(2, PlayerStatus.Lagging), Is.EqualTo(2), "a Lagging Survivor still gets the cue");
        }

        [Test]
        public void TheMaskIsReRolledOnlyWhenTheLevelChanges()
        {
            var visual = new ThreatVisual(new System.Random(5));
            for (int i = 0; i < 20; i++) visual.Update(1, PlayerStatus.Normal);
            Assert.That(visual.Rolls, Is.EqualTo(1), "a steady level never re-rolls");
            int first = visual.Seed;
            Assert.That(visual.Update(2, PlayerStatus.Normal), Is.True);
            Assert.That(visual.Rolls, Is.EqualTo(2));
            Assert.That(visual.Seed, Is.Not.EqualTo(first));
            int second = visual.Seed;
            for (int i = 0; i < 20; i++) Assert.That(visual.Update(2, PlayerStatus.Normal), Is.False);
            Assert.That(visual.Seed, Is.EqualTo(second));
            visual.Update(0, PlayerStatus.Normal);
            Assert.That(visual.Rolls, Is.EqualTo(2), "level 0 shows nothing, so nothing is rolled");
            visual.Update(2, PlayerStatus.Normal);
            Assert.That(visual.Rolls, Is.EqualTo(3), "back to 2 is a change");
        }

        [Test]
        public void TheMaskIsOneBitStaticForASeed_StripFortyFivePercentBandThirtyPercent()
        {
            bool[] a = ThreatVisual.Mask(42, 200, 12), b = ThreatVisual.Mask(42, 200, 12), c = ThreatVisual.Mask(43, 200, 12);
            Assert.That(a, Is.EqualTo(b), "the same seed draws the same mask (static, never animated)");
            Assert.That(a, Is.Not.EqualTo(c));
            Assert.That(Fraction(a), Is.EqualTo(.3f).Within(.04f), "level-3 band: 30 %, unchanged");
            bool[] strip = ThreatVisual.Mask(42, 200, 12, ThreatVisual.StripDensity);
            Assert.That(Fraction(strip), Is.EqualTo(.45f).Within(.04f), "(10/30) meter strip: 45 %");
            Assert.That(ThreatVisual.Density, Is.EqualTo(.3f));
            Assert.That(ThreatVisual.StripDensity, Is.EqualTo(.45f));
            Assert.That(ThreatVisual.StripHeight, Is.EqualTo(8f), "(10/30) the strip is 8 u tall");
            Assert.That(ThreatVisual.BandWidth, Is.EqualTo(24f), "the band is unchanged");
        }

        private static float Fraction(bool[] mask)
        {
            int set = 0;
            foreach (bool on in mask) if (on) set++;
            return set / (float)mask.Length;
        }

        [Test]
        public void TheHudShowsTheStripAtOneAndTwoThirdsTheBandAtThreeAndRebuildsTexturesOnlyOnAReRoll()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.SetThreat(ThreatEffect.MeterStrip, 1f / 3, 11);
                Assert.That(hud.ThreatShown, Is.EqualTo(ThreatEffect.MeterStrip));
                Assert.That(hud.ThreatStripWidth, Is.EqualTo(Mathf.Round(SurvivorHud.MeterWidth / 3)));
                Texture2D texture = hud.ThreatTexture(0);
                Color32[] pixels = texture.GetPixels32();
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(texture.height * ThreatVisual.StripCell, Is.EqualTo(8f), "8 u of noise rows");
                Assert.That(hud.ThreatStripHeight, Is.EqualTo(8f), "the strip element is 8 u tall");
                int danger = 0;
                foreach (Color32 p in pixels)
                {
                    Assert.That(p.a == 0 || (p.r == 229 && p.g == 72 && p.b == 77 && p.a == 255), Is.True, "1-bit: `danger` or transparent");
                    if (p.a != 0) danger++;
                }
                Assert.That(danger / (float)pixels.Length, Is.EqualTo(ThreatVisual.StripDensity).Within(.1f), "about 45 % of the strip");
                hud.SetThreat(ThreatEffect.MeterStrip, 1f / 3, 11);
                Assert.That(hud.ThreatTexture(0).GetPixels32(), Is.EqualTo(pixels), "the same seed keeps the same noise");
                hud.SetThreat(ThreatEffect.MeterStrip, 2f / 3, 12);
                Assert.That(hud.ThreatStripWidth, Is.EqualTo(Mathf.Round(SurvivorHud.MeterWidth * 2 / 3)));
                hud.SetThreat(ThreatEffect.ScreenEdge, 0, 13);
                Assert.That(hud.ThreatShown, Is.EqualTo(ThreatEffect.ScreenEdge), "the band replaces the strip");
                for (int slot = 1; slot <= 4; slot++) Assert.That(hud.ThreatTexture(slot), Is.Not.Null, "band " + slot);
                hud.SetThreat(ThreatEffect.None, 0, 13);
                Assert.That(hud.ThreatShown, Is.EqualTo(ThreatEffect.None));
            }
            finally { hud.Dispose(); }
        }

        // ---- 3. capture banner ------------------------------------------------------------------------------------------

        [Test]
        public void TheCaptureBannerFiresOncePerCaptureAndShowsForAboutThreeSeconds()
        {
            var watch = new CaptureWatch();
            double t = 0;
            for (int i = 0; i < 10; i++, t += Tick) Assert.That(watch.Observe(PlayerStatus.Normal, 0, 0, t), Is.EqualTo(StatusEvent.None));
            double capturedAt = t;
            Assert.That(watch.Observe(PlayerStatus.Lagging, 60, 1, t), Is.EqualTo(StatusEvent.Captured));
            for (int i = 0; i < 100; i++) { t += Tick; Assert.That(watch.Observe(PlayerStatus.Lagging, 60 - (float)(t - capturedAt), 1, t), Is.EqualTo(StatusEvent.None)); }
            Assert.That(watch.Banners, Is.EqualTo(1), "Lagging snapshots never re-fire it");
            Assert.That(watch.BannerVisible(capturedAt + .1), Is.True);
            Assert.That(watch.BannerVisible(capturedAt + 2.9), Is.True);
            Assert.That(watch.BannerVisible(capturedAt + 3.01), Is.False);
            Assert.That(CaptureWatch.BannerSeconds, Is.EqualTo(3));
            // Rescued, then captured again: the second capture eliminates directly, so there is no second banner.
            t += Tick; Assert.That(watch.Observe(PlayerStatus.Normal, 0, 1, t), Is.EqualTo(StatusEvent.Rescued));
            t += Tick; Assert.That(watch.Observe(PlayerStatus.Eliminated, 0, 2, t), Is.EqualTo(StatusEvent.Eliminated));
            Assert.That(watch.Banners, Is.EqualTo(1));
        }

        [Test]
        public void ABannerIsRemovedAtOnceWhenLaggingEndsAndAReconnectInLaggingFiresNothing()
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 0);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 1);
            watch.Observe(PlayerStatus.Eliminated, 0, 1, 2);
            Assert.That(watch.BannerVisible(2.1), Is.False, "eliminated within the banner time: banner gone");
            var reconnect = new CaptureWatch();
            Assert.That(reconnect.Observe(PlayerStatus.Lagging, 31, 1, 0), Is.EqualTo(StatusEvent.None), "the first entry is the baseline");
            Assert.That(reconnect.BannerVisible(.1), Is.False);
        }

        [Test]
        public void TheBannerUsesTheFixedVocabularyAndFitsThePromptRow()
        {
            string banner = CaptureWatch.Banner;
            Assert.That(banner, Is.EqualTo("Lagging. A Survivor holding [E] within 1.5 m for 3 s restores you " + SurvivorReadout.Dot + " 60 s left"));
            // R3 (11/17): the banner names the E hold, so it is 77 characters. HUD_SPEC 4.5 measures M at about 8.2 u per character
            // (HelpTests measures the real font): 8.4 u is the budget here, inside the 656 u inner width minus the 4 u bar and gap.
            Assert.That(banner.Length * 8.4f, Is.LessThanOrEqualTo(656 - 8), "M budget 8.4 u per character, 656 u inner width minus the 4 u bar and gap");
            Assert.That(banner, Does.Not.Contain("ally").And.Not.Contain("!").And.Not.Contain("Caught"));
            PromptState prompt = CaptureWatch.BannerPrompt;
            Assert.That(prompt.Text, Is.EqualTo(banner));
            Assert.That(prompt.Danger, Is.True, "a `danger` mark paired with `text` words");
            Assert.That(prompt.Muted, Is.False);
        }

        // ---- 4. elimination reason --------------------------------------------------------------------------------------

        [TestCase(.05f, EliminationReason.TimedOut)]
        [TestCase(0f, EliminationReason.TimedOut)]
        [TestCase(.2f, EliminationReason.TimedOut)]
        [TestCase(.25f, EliminationReason.None)]
        [TestCase(41f, EliminationReason.None)]
        public void LaggingToEliminatedIsATimeoutAtOrBelowTwoTenthsAndOtherwiseNotInferredWithoutACollapse(float lastTimer, EliminationReason expected)
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 0);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 1);
            watch.Observe(PlayerStatus.Lagging, lastTimer, 1, 2);
            Assert.That(watch.Observe(PlayerStatus.Eliminated, 0, 1, 2.05), Is.EqualTo(StatusEvent.Eliminated));
            Assert.That(watch.Reason, Is.EqualTo(expected));
        }

        [Test]
        public void ANormalSurvivorCapturedTwiceWhoIsEliminatedDirectlyWasCaughtAThirdTime()
        {
            // GDD v2 4 (U6c-wire S5): the elimination is the third capture; one capture before it leaves the Survivor Lagging.
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 2, 0);
            watch.Observe(PlayerStatus.Eliminated, 0, 3, .05);
            Assert.That(watch.Reason, Is.EqualTo(EliminationReason.ThirdCapture));
            Assert.That(CaptureWatch.Infer(PlayerStatus.Normal, 0, 1), Is.EqualTo(EliminationReason.None), "captured once: a capture only lags");
            Assert.That(CaptureWatch.Infer(PlayerStatus.Normal, 0, 0), Is.EqualTo(EliminationReason.None), "never captured: nothing is claimed");
        }

        [Test]
        public void TheThreeReasonsUseTheSpecCopyOnTheMeter()
        {
            // GDD v2 4 (U6c-wire S5): no execution wording; the result screen (13.7) names the outcome only.
            Assert.That(CaptureWatch.ReasonText(EliminationReason.Collapse), Is.EqualTo("Disconnected: the shutdown reached you."));
            Assert.That(CaptureWatch.ReasonText(EliminationReason.TimedOut), Is.EqualTo("Disconnected: no rescue in the second Lagging stage (30 s)."));
            Assert.That(CaptureWatch.ReasonText(EliminationReason.ThirdCapture), Is.EqualTo("Disconnected: caught with no Lagging stage left."));
            Assert.That(CaptureWatch.ReasonText(EliminationReason.None), Is.Null);
            foreach (EliminationReason reason in new[] { EliminationReason.Collapse, EliminationReason.TimedOut, EliminationReason.ThirdCapture })
            {
                string text = CaptureWatch.ReasonText(reason);
                MeterState m = SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Eliminated }, null, 0, 0, text);
                Assert.That(m.Word, Is.EqualTo("Eliminated"));
                Assert.That(m.Reason, Is.EqualTo(text));
                Assert.That(m.Wrap, Is.True, "up to two lines in rows B-C (no bar)");
                Assert.That(m.ReasonMuted, Is.False);
                Assert.That(text.Length * 8.3f, Is.LessThanOrEqualTo(2 * 288), "fits two S lines of 288 u");
                Assert.That(text, Does.Not.Contain("Executed"));
            }
            MeterState plain = SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Eliminated }, null, 0, 0, null);
            Assert.That(plain.Reason, Is.EqualTo("Watching until the match ends."), "no inferred reason: the old line");
            Assert.That(plain.Wrap, Is.False);
            var down = new S2C_GameResult { Reason = (byte)GameEndReason.AllSurvivorsDown, WinnerRole = (byte)Role.Killer };
            down.EliminatedIds.Add(OwnId);
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(down, null, OwnId, Role.Survivor)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(down, null, 99, Role.Killer)), Is.EqualTo("You eliminated 1 of 1 Survivors."));
        }

        [Test]
        public void TheHudWrapsTheReasonHidesTheBarAndDrawsTheBannerBar()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.SetMeter(SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Eliminated }, null, 0, 0, CaptureWatch.Collapse));
                Assert.That(hud.Reason, Is.EqualTo(CaptureWatch.Collapse));
                Assert.That(hud.ReasonWraps, Is.True);
                Assert.That(hud.MeterBarShown, Is.False);
                hud.SetMeter(SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Lagging, StatusTimer = 40 }, null, 0, 0, null));
                Assert.That(hud.ReasonWraps, Is.False);
                Assert.That(hud.MeterBarShown, Is.True);
                hud.SetPrompt(CaptureWatch.BannerPrompt);
                Assert.That(hud.PromptText, Is.EqualTo(CaptureWatch.Banner));
                Assert.That(hud.PromptDanger, Is.True);
                hud.SetPrompt(new PromptState { Text = SurvivorReadout.SkillsOffLagging, Muted = true });
                Assert.That(hud.PromptDanger, Is.False);
            }
            finally { hud.Dispose(); }
        }

        // ---- cues and stutter -------------------------------------------------------------------------------------------

        [TestCase(PlayerStatus.Normal, true)]
        [TestCase(PlayerStatus.Lagging, false)]
        [TestCase(PlayerStatus.Eliminated, false)]
        [TestCase(PlayerStatus.Escaped, false)]
        public void SkillReadyIsAudibleOnlyForANormalSurvivor(PlayerStatus status, bool audible)
            => Assert.That(CaptureWatch.SkillReadyAudible(status), Is.EqualTo(audible));

        [Test]
        public void TheSurvivorCuesHaveTheirSpecLengthsAndStayUnderTheReference()
        {
            foreach (SurvivorCue cue in new[] { SurvivorCue.Captured, SurvivorCue.Eliminated, SurvivorCue.ThreatScratch })
            {
                float[] samples = SurvivorCueSynth.Build(cue, 44100);
                Assert.That(samples.Length, Is.EqualTo((int)(44100 * SurvivorCueSynth.Seconds(cue))), cue.ToString());
                float peak = 0;
                foreach (float s in samples) { Assert.That(float.IsNaN(s), Is.False); peak = Mathf.Max(peak, Mathf.Abs(s)); }
                Assert.That(peak, Is.GreaterThan(.005f), cue + " is audible");
                Assert.That(peak, Is.LessThanOrEqualTo(SurvivorCueSynth.Reference + 1e-4f), cue + " stays at or under the 0 dB reference");
            }
            Assert.That(SurvivorCueSynth.Seconds(SurvivorCue.Captured), Is.EqualTo(.25f), "captured: 250 ms");
            Assert.That(SurvivorCueSynth.Seconds(SurvivorCue.Eliminated), Is.EqualTo(.6f), "eliminated: 600 ms");
        }

        // ---- U8: Lagging look, hit-stop, swipe timing, capture impact pool ----------------------------------------------

        [Test]
        public void TheLaggingLookRunsAtFullRate_TheBurstLasts04s_AndTheFlashOnlyItsStart()
        {
            var look = new LaggingLook();
            Assert.That(look.Evaluate(5, false).Steady, Is.Zero, "nothing before Lagging");
            look.SetActive(true, 10); look.Capture(10);
            int frames = 0;
            for (int frame = 1; frame <= 60; frame++) if (look.Evaluate(10 + frame / 60.0, false).Steady > 0) frames++;
            Assert.That(frames, Is.EqualTo(60), "every frame shows the look (no hold)");
            Assert.That(LaggingLook.BurstSeconds, Is.InRange(.3f, .5f));
            Assert.That(look.Evaluate(10 + .01, false).Flash, Is.GreaterThan(0));
            Assert.That(look.Evaluate(10 + LaggingLook.FlashSeconds + .001, false).Flash, Is.Zero, "one short flash");
            Assert.That(look.Evaluate(10 + LaggingLook.BurstSeconds - .01, false).Burst, Is.GreaterThan(0));
            Assert.That(look.Evaluate(10 + LaggingLook.BurstSeconds + 1e-6, false).Burst, Is.Zero, "the burst ends at 0.4 s");
            Assert.That(look.Evaluate(10 + LaggingLook.BurstSeconds + 1e-6, false).Steady, Is.EqualTo(1), "the steady look stays while Lagging");
            Assert.That(look.Evaluate(10 + .02, false).TearSeed, Is.Not.EqualTo(look.Evaluate(10 + .07, false).TearSeed), "the tear moves every 50 ms");
            look.SetActive(false, 20);
            Assert.That(look.Evaluate(20.01, false).Steady, Is.Zero, "off with Lagging");
            for (int bar = 0; bar < LaggingLook.TearBars; bar++)
            {
                LaggingLook.Tear(3, bar, out float top, out float height, out float direction);
                Assert.That(top, Is.InRange(0f, .9f)); Assert.That(height, Is.InRange(.01f, .07f)); Assert.That(Mathf.Abs(direction), Is.EqualTo(1));
            }
        }

        [Test]
        public void TheLaggingStutterComponentShowsTheBurstOnlyOutsideReducedMode()
        {
            var go = new GameObject("u8-look");
            try
            {
                var look = go.AddComponent<LaggingStutter>();
                var back = new VisualElement(); var front = new VisualElement();
                look.Initialize(null, back, front, false);
                look.SetActive(true, 50); look.Capture(50); look.Apply(50.05);
                Assert.That(look.BackShown && look.FrontShown, Is.True, "steady look + burst");
                look.Apply(50.5);
                Assert.That(look.BackShown, Is.True); Assert.That(look.FrontShown, Is.False, "burst over");
                look.SetReducedOption(true);
                look.Capture(60); look.Apply(60.05);
                Assert.That(look.FrontShown, Is.False, "reduced: no burst, tear or flash");
                Assert.That(look.BackShown, Is.True, "reduced: the steady look only");
                look.SetActive(false, 61); look.Apply(61.01);
                Assert.That(look.BackShown || look.FrontShown, Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheHitStopHoldsThePresentationFor100MsOncePerImpact()
        {
            var stop = new HitStop();
            Assert.That(stop.Present(5), Is.EqualTo(5));
            Assert.That(stop.Begin(10), Is.True);
            Assert.That(HitStop.Seconds, Is.InRange(.08, .12));
            Assert.That(stop.Present(10.05), Is.EqualTo(10), "frozen inside the window");
            Assert.That(stop.Begin(10.06), Is.False, "a hit during an active stop does not extend it");
            Assert.That(stop.Present(10 + HitStop.Seconds - 1e-6), Is.EqualTo(10));
            Assert.That(stop.Present(10 + HitStop.Seconds), Is.EqualTo(10).Within(1e-9), "no jump when the stop ends");
            double previous = 10, end = 10 + HitStop.Seconds + HitStop.EaseSeconds;
            for (double t = 10 + HitStop.Seconds; t <= end + .05; t += 1 / 60.0)
            {
                double shown = stop.Present(t);
                Assert.That(shown - previous, Is.LessThanOrEqualTo(2.0 / 60 + 1e-9), "eases back: at most 2 frames of world time per frame");
                Assert.That(shown, Is.GreaterThanOrEqualTo(previous - 1e-9));
                previous = shown;
            }
            Assert.That(stop.Present(end), Is.EqualTo(end).Within(1e-9), "real time again 0.15 s after the stop");
            Assert.That(stop.Present(9.99), Is.EqualTo(9.99), "a stop scheduled for the contact leaves earlier frames alone");
            Assert.That(stop.Begin(11), Is.True);
            Assert.That(stop.Count, Is.EqualTo(2));
            stop.Reset();
            Assert.That(stop.Present(11.05), Is.EqualTo(11.05));
        }

        [Test]
        public void TheSwipeWindsUpThenStrikesThenHoldsTheStrikePose()
        {
            Assert.That(MotionTiming.SwipeSeconds, Is.InRange(.9f, 1.3f), "visible swing 0.9-1.3 s");
            Assert.That(MotionTiming.SwipeRealKeys[MotionTiming.SwipeRealKeys.Length - 1], Is.EqualTo(MotionTiming.SwipeSeconds));
            Assert.That(MotionTiming.SwipeClipKeys[MotionTiming.SwipeClipKeys.Length - 1], Is.EqualTo(MotionTiming.SwipeClipSeconds));
            Assert.That(MotionTiming.SwipeClipTime(0), Is.EqualTo(MotionTiming.SwipeClipKeys[0]), "starts with the arm already cocking (frame 12)");
            Assert.That(MotionTiming.SwipeClipTime(-1), Is.EqualTo(MotionTiming.SwipeClipKeys[0]));
            Assert.That(MotionTiming.SwipeRealKeys[1], Is.InRange(.15f, .2f), "fast anticipation");
            Assert.That(MotionTiming.SwipeContactSeconds, Is.InRange(.2f, .3f), "contact about 0.25 s after the capture");
            Assert.That(MotionTiming.SwipeContactSeconds, Is.EqualTo(MotionTiming.SwipeStrikeAt));
            Assert.That(MotionTiming.SwipeClipTime(9), Is.EqualTo(MotionTiming.SwipeClipSeconds));
            float previous = MotionTiming.SwipeClipKeys[0];
            for (double t = 0; t <= MotionTiming.SwipeSeconds; t += 1 / 120.0)
            {
                float clip = MotionTiming.SwipeClipTime(t);
                Assert.That(clip, Is.GreaterThanOrEqualTo(previous - 1e-5f), "never runs backwards");
                previous = clip;
            }
            // The strike pose holds: the same clip time for SwipeHoldSeconds, i.e. at least 3 frames at 30 fps.
            float strike = MotionTiming.SwipeClipTime(MotionTiming.SwipeStrikeAt);
            Assert.That(MotionTiming.SwipeClipTime(MotionTiming.SwipeStrikeAt + MotionTiming.SwipeHoldSeconds), Is.EqualTo(strike).Within(1e-5));
            Assert.That(MotionTiming.SwipeHoldSeconds * 30, Is.GreaterThanOrEqualTo(3));
            // The strike (keys 1-2) plays slower than the wind-up (keys 0-1), at about clip rate (>= 3 frames at 30 fps),
            // and the follow-through (keys 3-4) is slow.
            float windRate = (MotionTiming.SwipeClipKeys[1] - MotionTiming.SwipeClipKeys[0]) / (MotionTiming.SwipeRealKeys[1] - MotionTiming.SwipeRealKeys[0]);
            float strikeRate = (MotionTiming.SwipeClipKeys[2] - MotionTiming.SwipeClipKeys[1]) / (MotionTiming.SwipeRealKeys[2] - MotionTiming.SwipeRealKeys[1]);
            Assert.That(strikeRate, Is.LessThan(windRate));
            Assert.That((MotionTiming.SwipeRealKeys[2] - MotionTiming.SwipeRealKeys[1]) * 30, Is.GreaterThanOrEqualTo(3));
            float followRate = (MotionTiming.SwipeClipKeys[4] - MotionTiming.SwipeClipKeys[3]) / (MotionTiming.SwipeRealKeys[4] - MotionTiming.SwipeRealKeys[3]);
            Assert.That(followRate, Is.LessThan(.6f), "slow follow-through");
            Assert.That(MotionTiming.SwipeFromSeconds, Is.LessThan(.45f), "starts earlier than the old trim (the arm raise shows)");
            var attack = new AttackTracker();
            attack.Trigger(3, true);
            Assert.That(attack.ClipTime(3 + MotionTiming.SwipeStrikeAt), Is.EqualTo(strike).Within(1e-5));
        }

        [Test]
        public void TheCaptureImpactPoolIsBuiltOnceAndReusedWithoutGrowth()
        {
            var go = new GameObject("u8-impact");
            try
            {
                var fx = go.AddComponent<CaptureImpactFx>();
                fx.Play(Vector3.zero, 0);
                Assert.That(fx.Plays, Is.Zero, "nothing plays before warm-up");
                fx.Warm(null);
                int created = fx.Created;
                Assert.That(created, Is.EqualTo(CaptureImpactFx.PoolSize * 3), "root + 2 systems per pooled impact");
                fx.Warm(null);
                Assert.That(fx.Created, Is.EqualTo(created), "warm is idempotent");
                for (int i = 0; i < 10; i++) fx.Play(new Vector3(i, 0, 2), 1 + i * .1);
                Assert.That(fx.Created, Is.EqualTo(created), "no allocation on play: the oldest is reused");
                Assert.That(fx.Plays, Is.EqualTo(10));
                Assert.That(fx.Busy, Is.EqualTo(CaptureImpactFx.PoolSize));
                Assert.That(fx.LastPoint, Is.EqualTo(new Vector3(9, CaptureImpactFx.ChestHeight, 2)), "at the captured Survivor's chest");
                Assert.That(CaptureImpactFx.Seconds, Is.InRange(.5f, .8f));
                fx.Tick(1 + .9 + CaptureImpactFx.Seconds);
                Assert.That(fx.Busy, Is.Zero, "each impact ends after its lifetime");
                // Scheduled on the contact: nothing before its time, played once after it, no growth.
                fx.FirstPerson = true;
                fx.Schedule(new Vector3(1, 0, 1), 20.27);
                fx.Tick(20.2);
                Assert.That(fx.Plays, Is.EqualTo(10)); Assert.That(fx.Pending, Is.EqualTo(1));
                fx.Tick(20.3);
                Assert.That(fx.Plays, Is.EqualTo(11)); Assert.That(fx.Pending, Is.Zero);
                Assert.That(fx.Created, Is.EqualTo(created));
                foreach (ParticleSystemRenderer r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (r.transform.parent.gameObject.activeSelf) Assert.That(r.maxParticleSize, Is.EqualTo(CaptureImpactFx.FirstPersonMaxSize), "Killer view: hard size cap");
                for (int i = 0; i < 6; i++) fx.Schedule(Vector3.zero, 30);
                Assert.That(fx.Pending, Is.EqualTo(CaptureImpactFx.PendingSize), "bounded queue");
                fx.Clear();
                Assert.That(fx.Pending, Is.Zero);
                Assert.That(go.transform.childCount, Is.EqualTo(CaptureImpactFx.PoolSize));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheFrameLogReportsSpikesAroundACapture()
        {
            var log = new CaptureFrameLog();
            string line = null;
            double t = 0;
            for (int i = 0; i < 200 && line == null; i++)
            {
                float dt = i == 100 ? .05f : 1 / 60f;
                t += dt;
                if (i == 90) log.Mark(t, "test");
                line = log.Frame(t, dt);
            }
            Assert.That(line, Does.Contain("spikes>33ms=1"));
            Assert.That(line, Does.Contain("max=50.0"));
        }

        // ---- dev: -ptk-fake-killer and -ptk-reduce-stutter --------------------------------------------------------------

        [Test]
        public void TheNewFlagsAreInertWhenAbsentAndParseWhenPresent()
        {
            DevArgs none = DevArgs.Parse(new[] { "PingTheKiller.exe" });
            Assert.That(none.FakeKiller, Is.EqualTo(0f));
            Assert.That(none.ReduceStutter, Is.False);
            DevArgs fake = DevArgs.Parse(new[] { "-ptk-fake-killer", "6" });
            Assert.That(fake.FakeKiller, Is.EqualTo(6f));
            Assert.That(fake.Active, Is.True);
            Assert.That(fake.Errors, Is.Empty);
            DevArgs reduce = DevArgs.Parse(new[] { "-ptk-reduce-stutter" });
            Assert.That(reduce.ReduceStutter, Is.True);
            Assert.That(reduce.Active, Is.False, "a player option, not the dev capture");
            Assert.That(reduce.Errors, Is.Empty);
            foreach (string bad in new[] { "0", "-2", "x", "31", "NaN" })
            {
                DevArgs b = DevArgs.Parse(new[] { "-ptk-fake-killer", bad });
                Assert.That(b.FakeKiller, Is.EqualTo(0f), bad);
                Assert.That(b.Errors.Count, Is.EqualTo(1), bad);
                Assert.That(b.Active, Is.False, bad);
            }
            Assert.That(DevArgs.Parse(new[] { "-ptk-fake-killer" }).Errors[0], Does.Contain("needs a value"));
        }

        private static List<PlayerState> Players(PlayerStatus status, float yaw = 0, bool withKiller = false)
        {
            var list = new List<PlayerState>
            {
                new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 40, Z = 40, Status = (byte)PlayerStatus.Normal },
                new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = 5, Z = 36, Yaw = yaw, Status = (byte)status },
            };
            if (withKiller) list.Add(new PlayerState { Id = 2, Role = (byte)Role.Killer, X = 9, Z = 9 });
            return list;
        }

        [Test]
        public void TheFakeKillerAddsOneContractShapedEntryInLineOfSightAndCyclesItsShowing()
        {
            var fake = new FakeKiller(6);
            List<PlayerState> players = Players(PlayerStatus.Normal, yaw: 90);
            byte implied = fake.Inject(players, OwnId, 100);
            Assert.That(players.Count, Is.EqualTo(3));
            PlayerState killer = players[2];
            Assert.That(killer.Role, Is.EqualTo((byte)Role.Killer));
            Assert.That(killer.Flags, Is.EqualTo(0)); Assert.That(killer.StatusTimer, Is.EqualTo(0f)); Assert.That(killer.CaptureCount, Is.EqualTo(0));
            float d = new Vector2(killer.X - 5, killer.Z - 36).magnitude;
            Assert.That(d, Is.GreaterThanOrEqualTo(1f).And.LessThanOrEqualTo(6.001f));
            Assert.That(MapGeometry.SegmentBlocked(SharedMap.Map, 5, 36, killer.X, killer.Z), Is.False, "in line of sight");
            Assert.That(implied, Is.EqualTo(d <= 6 ? 3 : 2));
            Assert.That(fake.Showing, Is.True);
            List<PlayerState> later = Players(PlayerStatus.Normal, yaw: 90);
            fake.Inject(later, OwnId, 100 + FakeKiller.OnSeconds + .5);
            Assert.That(later.Count, Is.EqualTo(2), "hidden part of the cycle: no entry (the model fades)");
            Assert.That(fake.Showing, Is.False);
            // Facing a wall from 3 m away: pulled closer until the line of sight is clear. The wall is the west face of the
            // building Walls[12] (x 12..18, z 24..37; formerly x 18..20 seen from x = 15), so the Survivor stands at x = 9.
            PingTheKiller.ServerCore.Map.Wall wall = SharedMap.Map.Walls[12];
            float wallX = wall.MinX - 3, wallZ = (wall.MinZ + wall.MaxZ) * .5f;
            var walled = new FakeKiller(6);
            var list = new List<PlayerState> { new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = wallX, Z = wallZ, Yaw = 90 } };
            walled.Inject(list, OwnId, 0);
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list[1].X, Is.LessThanOrEqualTo(wall.MinX));
            Assert.That(MapGeometry.SegmentBlocked(SharedMap.Map, wallX, wallZ, list[1].X, list[1].Z), Is.False);
        }

        [Test]
        public void TheFakeKillerNeverDuplicatesARealEntryAndNeverShowsToASpectator()
        {
            var fake = new FakeKiller(6);
            List<PlayerState> real = Players(PlayerStatus.Normal, withKiller: true);
            Assert.That(fake.Inject(real, OwnId, 0), Is.EqualTo(0));
            Assert.That(real.Count, Is.EqualTo(3));
            foreach (PlayerStatus status in new[] { PlayerStatus.Eliminated, PlayerStatus.Escaped })
            {
                List<PlayerState> list = Players(status);
                fake.Inject(list, OwnId, 0);
                Assert.That(list.Count, Is.EqualTo(2), status.ToString());
            }
            Assert.That(fake.Inject(null, OwnId, 0), Is.EqualTo(0));
        }

        // ---- U4d F-01 / F-02 ----------------------------------------------------------------------------------------------

        private static readonly Vector2 Screen1600 = new Vector2(1600, 900);
        private static ViewPose KillerPose() => new ViewPose(new Vector3(10, KillerViewRig.EyeHeight, 23), 90, 0, KillerViewRig.VerticalFov, 16f / 9);

        [Test]
        public void F01ASurvivorAtExecutionRangeStillRegistersAScreenClampedBodyRectSoTerminalTagsAvoidIt()
        {
            ViewPose pose = KillerPose();
            var ground = new Vector3(11, 0, 23); // 1 m ahead: the feet project below the bottom edge
            Assert.That(pose.ToViewport(ground, out Vector2 feet) && feet.y < 0, Is.True, "scenario: feet are off-screen");
            Assert.That(KillerWorldLayer.BodyRect(pose, ground, KillerWorldLayer.HeadPoint(ground, 0, false), Screen1600, out Rect body), Is.True);
            Assert.That(body.yMax, Is.EqualTo(900).Within(.01f), "clamped to the bottom edge");
            Assert.That(body.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(body.xMax, Is.LessThanOrEqualTo(1600));
            Assert.That(body.Contains(new Vector2(800, 700)), Is.True, "covers the body in the screen centre");
            var layout = new TagLayout();
            layout.Add(LayoutPriority.Survivor, body, default);
            int terminal = layout.Add(LayoutPriority.Terminal, default, TagLayout.Around(new Vector2(800, 700), 40, 10));
            layout.Solve(new Rect(0, 0, 1600, 900));
            LayoutItem tag = layout[terminal];
            if (tag.TagVisible)
            {
                Rect placed = TagLayout.Around(new Vector2(800, 700 + tag.TagShift), 40, 10);
                Assert.That(placed.Overlaps(body), Is.False, "the terminal tag never draws over the Survivor");
            }
            Assert.That(KillerWorldLayer.BodyRect(pose, new Vector3(5, 0, 23), new Vector3(5, 1.95f, 23), Screen1600, out _), Is.False, "behind the camera: no rect");
        }

        [Test]
        public void F02TheLaggingTagAnchorFollowsTheTwentyFiveDegreeTiltOfTheHead()
        {
            var ground = new Vector3(4, 0, 6);
            Vector3 upright = KillerWorldLayer.HeadPoint(ground, 0, false), tilted = KillerWorldLayer.HeadPoint(ground, 0, true);
            Assert.That(upright, Is.EqualTo(ground + Vector3.up * KillerWorldLayer.HeadHeight));
            float rad = KillerWorldLayer.LaggingTilt * Mathf.Deg2Rad;
            Assert.That(tilted.y, Is.EqualTo(KillerWorldLayer.HeadHeight * Mathf.Cos(rad)).Within(1e-4f), "lower by the tilt");
            Assert.That(tilted.z - ground.z, Is.EqualTo(KillerWorldLayer.HeadHeight * Mathf.Sin(rad)).Within(1e-4f), "forward along the facing");
            Vector3 east = KillerWorldLayer.HeadPoint(ground, 90, true);
            Assert.That(east.x - ground.x, Is.EqualTo(KillerWorldLayer.HeadHeight * Mathf.Sin(rad)).Within(1e-4f), "the tilt turns with the facing yaw");
            // The model applies the same rotation: root yaw, then the Pose child's X tilt about the ground point.
            Vector3 model = ground + Quaternion.Euler(0, 0, 0) * (Quaternion.Euler(KillerWorldLayer.LaggingTilt, 0, 0) * new Vector3(0, KillerWorldLayer.HeadHeight, 0));
            Assert.That((model - tilted).magnitude, Is.LessThan(1e-4f));
            string code = Strip(Read("Killer/KillerWorldLayer.cs"));
            Assert.That(code, Does.Contain("Vector3 headPoint = HeadPoint(ground, view.FacingYaw, lagging);"));
            Assert.That(code, Does.Not.Contain("view.Position + Vector3.up * HeadHeight"), "no untilted head anchor is left");
        }

        [Test]
        public void F03SkillReadyIsGatedOnTheOwnStatusInTheApp()
        {
            string app = Strip(Read("ClientApp.cs"));
            Assert.That(app, Does.Contain("bool audible = !survivor || CaptureWatch.SkillReadyAudible(own.Status);"));
            Assert.That(app, Does.Contain("if (survivor && audible) cueSource.PlayOneShot(readyClip);"));
        }

        // ---- U4e attempt 2: Q-02 skill-name width, Q-03 visor fade, Q-04b no Killer for spectators ------------------------

        // TextCore's synthesized bold (Inter ships only Regular): 7/100 em extra advance per glyph.
        private const float BoldSpacing = .07f;

        private static float Measure(Font font, string text, float size, bool bold)
        {
            const int Probe = 200;
            font.RequestCharactersInTexture(text, Probe, FontStyle.Normal);
            float units = 0;
            foreach (char c in text)
            {
                Assert.That(font.GetCharacterInfo(c, out CharacterInfo info, Probe, FontStyle.Normal), Is.True, "glyph '" + c + "'");
                units += info.advance;
            }
            return units * size / Probe + (bold ? BoldSpacing * size * text.Length : 0);
        }

        private static List<string> Names(System.Func<byte, string> name)
        {
            var names = new List<string>();
            for (byte id = 0; id < 16; id++) { string n = name(id); if (!n.StartsWith("Skill ")) names.Add(n); }
            return names;
        }

        [Test]
        public void Q02EverySkillNameWrapsOnlyAtSpaces_TheLongestWordFitsTheNameColumn()
        {
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            Assert.That(font, Is.Not.Null);
            Assert.That(Measure(font, "Emergency", 14, false), Is.InRange(72f, 78f), "measurement sanity: Inter 'Emergency' at 14 u is ~75 u");
            List<string> survivor = Names(SurvivorReadout.SkillName), killer = Names(KillerHudCopy.SkillName);
            Assert.That(survivor.Count, Is.EqualTo(5)); Assert.That(killer.Count, Is.EqualTo(4));
            foreach (float size in new[] { 14f, ClientScreens.SmallSize(.8f) }) // S at 1080p and the 15 u floor at 720p
                foreach (string name in survivor)
                {
                    int lines = 1; float line = 0, space = Measure(font, " ", size, true);
                    foreach (string word in name.Split(' '))
                    {
                        float w = Measure(font, word, size, true);
                        Assert.That(w, Is.LessThanOrEqualTo(SurvivorHud.SkillNameWidth), "'" + word + "' at " + size + " u would break inside the word");
                        if (line > 0 && line + space + w > SurvivorHud.SkillNameWidth) { lines++; line = w; } else line += (line > 0 ? space : 0) + w;
                    }
                    Assert.That(lines, Is.LessThanOrEqualTo(2), name + ": at most two lines (36 u)");
                }
            // The Killer cluster's name is M 600 on one line in its 232 u column.
            foreach (string name in killer) Assert.That(Measure(font, name, 18, true), Is.LessThanOrEqualTo(232f), name);

            string uss = File.ReadAllText(Path.Combine(Application.dataPath, "Resources", "ClientUI.uss"));
            float Width(string selector) => float.Parse(Regex.Match(uss, Regex.Escape(selector) + @"\s*\{[^}]*?\bwidth:\s*(\d+)px").Groups[1].Value);
            Assert.That(Width(".unity-label.sname"), Is.EqualTo(SurvivorHud.SkillNameWidth));
            Assert.That(Width(".sskill-text"), Is.EqualTo(SurvivorHud.SkillNameWidth));
            Assert.That(Width(".sskill"), Is.EqualTo(SurvivorHud.SkillBlockWidth));
            Assert.That(Width(".sinfo"), Is.EqualTo(SurvivorHud.InfoBlockWidth));
            Assert.That(8 + 64 + 4 + SurvivorHud.SkillNameWidth, Is.EqualTo(SurvivorHud.SkillBlockWidth), "padding 8 + ring 64 + gap 4 + column");
            Assert.That(7 + SurvivorHud.SkillBlockWidth + 8 + SurvivorHud.MeterWidth + 8 + SurvivorHud.InfoBlockWidth + 7, Is.EqualTo(672 - 2), "the cluster stays 672 u");
            Assert.That(Measure(font, "Terminals 4/4", 18, true), Is.LessThanOrEqualTo(SurvivorHud.InfoBlockWidth - 16), "the info block still fits its longest line");
        }

        private static S2C_WorldState WorldSnapshot(uint tick, bool killer, PlayerStatus own)
        {
            var state = new S2C_WorldState { Tick = tick };
            state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, Status = (byte)own, X = 5, Z = 36 });
            if (killer) state.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = 9, Z = 36, Yaw = 270 });
            for (byte i = 0; i < SharedMap.Map.TerminalPositions.Count; i++)
                state.Terminals.Add(new TerminalState { Id = i, X = SharedMap.Map.TerminalPositions[i].X, Z = SharedMap.Map.TerminalPositions[i].Z });
            return state;
        }

        private static WorldView SurvivorWorld(GameObject host, PlayerStatus own, int count, int killerUntil)
        {
            var view = host.AddComponent<WorldView>();
            view.Initialize(OwnId, Role.Survivor);
            for (int i = 1; i <= count; i++) view.Accept(WorldSnapshot((uint)i, i <= killerUntil, own), i * Tick);
            return view;
        }

        [Test]
        public void Q03TheVisorFadesTogetherWithTheBody()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // edit mode: WorldView's collider Destroy() logs
            var host = new GameObject("U4e Q-03 world");
            try
            {
                WorldView view = SurvivorWorld(host, PlayerStatus.Normal, 14, 5);
                view.Render(RenderAt(5 * Tick));
                Assert.That(view.KillerAlpha, Is.EqualTo(1f));
                Assert.That(view.KillerVisorAlpha, Is.EqualTo(1f));
                foreach (double after in new[] { .05, .15, .25 })
                {
                    view.Render(RenderAt(6 * Tick + after));
                    Assert.That(view.KillerAlpha, Is.EqualTo((float)(1 - after / SnapshotBuffer.KillerFade)).Within(1e-3f), "+" + after);
                    Assert.That(view.KillerVisorAlpha, Is.EqualTo(view.KillerAlpha).Within(1e-4f), "the visor fades with the body, +" + after);
                }
                view.Render(RenderAt(6 * Tick + .35));
                Assert.That(view.KillerAlpha, Is.EqualTo(0f)); Assert.That(view.KillerVisorAlpha, Is.EqualTo(0f));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void Q04bASpectatingSurvivorNeverSeesTheKillerModelEvenIfAnEntryArrives()
        {
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Normal), Is.True);
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Lagging), Is.True);
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Eliminated), Is.False);
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Escaped), Is.False);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            foreach (PlayerStatus spectator in new[] { PlayerStatus.Eliminated, PlayerStatus.Escaped })
            {
                var host = new GameObject("U4e Q-04b world");
                try
                {
                    WorldView view = SurvivorWorld(host, spectator, 10, 10); // the entry arrives in every snapshot
                    for (double r = 2 * Tick; r <= 9 * Tick; r += .02)
                    {
                        view.Render(RenderAt(r));
                        Assert.That(view.KillerAlpha, Is.EqualTo(0f), spectator + " r=" + r);
                        Assert.That(view.KillerVisorAlpha, Is.EqualTo(0f));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
            var live = new GameObject("U4e Q-04b live");
            try
            {
                WorldView view = SurvivorWorld(live, PlayerStatus.Lagging, 10, 10);
                view.Render(RenderAt(5 * Tick));
                Assert.That(view.KillerAlpha, Is.EqualTo(1f), "a Lagging Survivor still sees the Killer in line of sight");
            }
            finally { UnityEngine.Object.DestroyImmediate(live); }
        }

        // ---- helpers ----------------------------------------------------------------------------------------------------

        // The world view is split over four files; checks about it as a whole read them together.
        private static readonly string[] WorldFiles = { "World/WorldView.cs", "World/CharacterViews.cs", "World/WorldProps.cs", "World/WorldMaterials.cs" };

        private static string Read(string relative) => File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", relative));

        private static string Strip(string source) => Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);

        private static string Method(string source, string signature)
        {
            int at = source.IndexOf(signature, System.StringComparison.Ordinal);
            if (at < 0) return null;
            int open = source.IndexOf('{', at), depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            return null;
        }
    }
}
