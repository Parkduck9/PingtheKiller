using System;
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

namespace PingTheKiller.Client.QA.Tests
{
    // U4e QA attempt 1: written from docs/briefs/U4e-a1.md and docs/briefs/U4e-qa-a1.md, not from the implementer's tests.
    // Covers the Killer model on the Survivor client, the source audit, threat visuals, the capture banner, the elimination
    // reason (meter and result line), F-03, the stutter clock and the dev fake-Killer flag. Every test needs the Unity
    // EditMode gate (UnityEngine types); none needs a scene, a socket, the game or the server.
    public sealed class SurvivorFeedbackQaTests
    {
        private const double Tick = .05;
        private const int OwnId = 7, KillerId = 3;

        // ------------------------------------------------------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------------------------------------------------------

        private static S2C_WorldState Snapshot(uint tick, float? killerX, float killerYaw = 0, byte threat = 0)
        {
            var state = new S2C_WorldState { Tick = tick, ThreatLevel = threat };
            state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = 1, Z = 1 });
            if (killerX.HasValue)
                state.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = killerX.Value, Z = 5, Yaw = killerYaw });
            return state;
        }

        // Snapshot n (1-based) arrives at n * 50 ms.
        private static SnapshotBuffer Feed(int count, Func<int, float?> killerX)
        {
            var buffer = new SnapshotBuffer();
            for (int i = 1; i <= count; i++) buffer.Add(Snapshot((uint)i, killerX(i)), i * Tick);
            return buffer;
        }

        private static double At(double renderTime) => renderTime + SnapshotBuffer.Delay;

        private static string Code(string relative) => QaSource.StripComments(QaPaths.ReadScript(relative));

        private static string DocsFile(string relative)
        {
            string path = Path.GetFullPath(Path.Combine(QaPaths.Assets(), "..", "..", "docs", relative));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static OwnEntry Own(PlayerStatus status, float timer = 0, byte captures = 0)
            => new OwnEntry { Status = status, StatusTimer = timer, CaptureCount = captures };

        // ------------------------------------------------------------------------------------------------------------------
        // 1. Killer model on the Survivor client (fake snapshots)
        // ------------------------------------------------------------------------------------------------------------------

        [Test]
        public void KillerPresent_IsDrawnAtTheInterpolatedPosition_FacingTheShortestWayRoundTheYaw()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(1, 10, 350), 1 * Tick);
            buffer.Add(Snapshot(2, 20, 10), 2 * Tick);
            Assert.That(buffer.SampleKiller(At(1.5 * Tick), out Vector3 p, out float yaw, out float alpha), Is.True);
            Assert.That(p.x, Is.EqualTo(15).Within(1e-3), "halfway between the two entries");
            Assert.That(p.z, Is.EqualTo(5).Within(1e-3));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, 0)), Is.LessThan(.05f), "350 -> 10 turns through 0, not through 180");
            Assert.That(alpha, Is.EqualTo(1f));
        }

        [Test]
        public void KillerAbsent_FadesOverThreeTenthsInPlace_NeverExtrapolated_AlphaNeverRises()
        {
            // A fast Killer (10 m per snapshot) present in snapshots 1..5, absent from 6 on.
            SnapshotBuffer buffer = Feed(30, i => i <= 5 ? i * 10f : (float?)null);
            double lastSeen = 5 * Tick, absentFrom = 6 * Tick;
            float previousAlpha = 1;
            bool gone = false;
            for (double r = lastSeen; r < absentFrom + .6; r += .005)
            {
                bool seen = buffer.SampleKiller(At(r), out Vector3 p, out _, out float alpha);
                if (seen)
                {
                    Assert.That(gone, Is.False, "a faded model never comes back without a new entry, r=" + r);
                    Assert.That(p.x, Is.EqualTo(50).Within(1e-3), "held at the last received position, never extrapolated, r=" + r);
                    Assert.That(alpha, Is.LessThanOrEqualTo(previousAlpha + 1e-6f), "alpha never rises, r=" + r);
                    Assert.That(alpha, Is.InRange(0f, 1f));
                    previousAlpha = alpha;
                }
                else gone = true;
            }
            Assert.That(gone, Is.True);
            Assert.That(buffer.SampleKiller(At(absentFrom + .15), out _, out _, out float half), Is.True);
            Assert.That(half, Is.EqualTo(.5f).Within(.02f), "linear fade: half way after 0.15 s");
            Assert.That(buffer.SampleKiller(At(absentFrom + .295), out _, out _, out float nearlyGone), Is.True);
            Assert.That(nearlyGone, Is.LessThan(.05f));
            Assert.That(buffer.SampleKiller(At(absentFrom + .305), out _, out _, out _), Is.False, "gone after 0.3 s");
        }

        [TestCase(7)]   // 0.35 s hidden
        [TestCase(12)]  // 0.6 s
        [TestCase(20)]  // 1.0 s
        public void KillerReappearingAfterMoreThanThreeTenths_SnapsAndShowsNoPointOfTheHiddenPath(int hiddenSnapshots)
        {
            int back = 5 + hiddenSnapshots + 1;
            SnapshotBuffer buffer = Feed(back + 3, i => i <= 5 ? 0 : i >= back ? 50 : (float?)null);
            for (double r = 5 * Tick; r < back * Tick - 1e-6; r += .0025)
                if (buffer.SampleKiller(At(r), out Vector3 p, out _, out _))
                    Assert.That(p.x, Is.EqualTo(0).Within(1e-3), "only the old position is ever drawn during the gap, r=" + r);
            Assert.That(buffer.SampleKiller(At(back * Tick + 1e-3), out Vector3 now, out _, out float alpha), Is.True);
            Assert.That(now.x, Is.EqualTo(50).Within(1e-3), "snaps to the new position");
            Assert.That(alpha, Is.EqualTo(1f), "and is at full alpha at once");
        }

        [Test]
        public void KillerNeverInSnapshots_NeverDrawn_AndAStaleSnapshotCannotRevive()
        {
            SnapshotBuffer empty = Feed(12, i => null);
            for (double r = 0; r < 1; r += .01) Assert.That(empty.SampleKiller(At(r), out _, out _, out _), Is.False);

            // Killer in 1..3, absent in 4..12; then an old tick (with the Killer) and a duplicate tick arrive late.
            SnapshotBuffer buffer = Feed(12, i => i <= 3 ? 10 : (float?)null);
            buffer.Add(Snapshot(2, 99), 13 * Tick);
            buffer.Add(Snapshot(12, 99), 14 * Tick);
            Assert.That(buffer.SampleKiller(At(14 * Tick), out _, out _, out _), Is.False, "old and duplicate ticks are dropped, the model stays gone");
        }

        [Test]
        public void KillerModel_IsFoundByRole_NotById()
        {
            // The only entry with the Killer's Id is a Survivor: it must never be drawn as the Killer model.
            var buffer = new SnapshotBuffer();
            for (uint t = 1; t <= 6; t++)
            {
                var s = new S2C_WorldState { Tick = t };
                s.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Survivor, X = 20, Z = 20 });
                buffer.Add(s, t * Tick);
            }
            Assert.That(buffer.SampleKiller(At(5 * Tick), out _, out _, out _), Is.False);
        }

        // ------------------------------------------------------------------------------------------------------------------
        // 1b/2. Source audit: the only Killer visual on a Survivor client is WorldView.RenderKiller from the Killer entry
        // ------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Audit_OnlyTheExpectedFilesReadTheKillerEntryOfTheSnapshotPlayerList()
        {
            var readers = new List<string>();
            foreach (string path in QaPaths.AllScripts())
            {
                string code = QaSource.StripComments(File.ReadAllText(path));
                if (code.Contains("Role.Killer") && Regex.IsMatch(code, @"\.Players\b")) readers.Add(Path.GetFileName(path));
            }
            // CharacterViews/SnapshotBuffer: the Survivor model; ClientApp: own-entry lookup (Killer client by Role); KillerScene: the
            // Killer client's own entry; DevCapture: dev only (FakeKiller takes the list as a parameter). Any new file here is a new Killer-entry consumer.
            Assert.That(readers.ToArray(), Is.EquivalentTo(new[] { "ClientApp.cs", "DevCapture.cs", "KillerScene.cs", "SnapshotBuffer.cs", "CharacterViews.cs" }), "found: " + string.Join(",", readers));
        }

        [Test]
        public void Audit_ThreatLevel_SoundAndSkillEffectsNeverPlaceOrCreateAKillerVisual()
        {
            foreach (string path in QaPaths.AllScripts())
            {
                string file = Path.GetFileName(path);
                string code = QaSource.StripComments(File.ReadAllText(path));
                if (Regex.IsMatch(code, @"\.ThreatLevel\b"))
                    Assert.That(new[] { "ClientApp.cs", "DevCapture.cs" }, Does.Contain(file), file + " reads ThreatLevel");
            }
            foreach (string file in new List<string>(WorldSources.All) { "World/SnapshotBuffer.cs" })
            {
                string code = Code(file);
                foreach (string forbidden in new[] { "ThreatLevel", "SkillEffect", "AudioSource", "PlayOneShot", "ThreatAudio", "AudioClip" })
                    Assert.That(code, Does.Not.Contain(forbidden), file + " must not use " + forbidden);
            }
            string world = QaSource.StripComments(WorldSources.ReadAll());
            Assert.That(world, Does.Contain("if (role == Role.Survivor) killerModel = MakeKillerModel();"), "only a Survivor client builds the model");
            Assert.That(Regex.Matches(world, @"(?<![=!<>])killerModel\s*=(?!=)").Count, Is.EqualTo(1), "one creation site");
            // Nothing outside the world view builds a Killer-looking object (primitive capsule named Killer).
            foreach (string path in QaPaths.AllScripts())
            {
                string file = Path.GetFileName(path);
                if (new List<string>(WorldSources.FileNames()).Contains(file)) continue;
                Assert.That(QaSource.StripComments(File.ReadAllText(path)), Does.Not.Contain("\"Killer body\""), file);
            }
        }

        [Test]
        public void Audit_TheKillerClientPathStillReadsNoSurvivorIdYawOrTimers()
        {
            string layer = Code("Killer/KillerWorldLayer.cs");
            Assert.That(layer, Does.Not.Match(@"\bPlayerState\b"));
            Assert.That(layer, Does.Not.Match(@"\bStatusTimer\b"));
            Assert.That(layer, Does.Not.Match(@"\bCaptureCount\b"));
            Assert.That(layer, Does.Not.Match(@"\.Yaw\b"), "facing comes from matched motion (view.FacingYaw), never an entry Yaw");
            string world = QaSource.StripComments(WorldSources.ReadAll());
            Assert.That(world, Does.Contain("if (role == Role.Killer) break;"), "the Killer view builds no character from an entry");
            Assert.That(world, Does.Contain("if (role == Role.Survivor) killerModel"));
        }

        [Test]
        public void Audit_TheFakeKillerExistsOnlyBehindItsFlagOnASurvivorClient()
        {
            int creations = 0;
            foreach (string path in QaPaths.AllScripts())
            {
                string code = QaSource.StripComments(File.ReadAllText(path));
                creations += Regex.Matches(code, @"new\s+FakeKiller\s*\(").Count;
            }
            Assert.That(creations, Is.EqualTo(1));
            string app = Code("ClientApp.cs");
            Assert.That(app, Does.Match(@"if\s*\(\s*devArgs\.FakeKiller\s*>\s*0\s*\)\s*fakeKiller\s*=\s*new FakeKiller\("));
            Assert.That(app, Does.Match(@"if\s*\(\s*fakeKiller\s*!=\s*null\s*&&\s*role\s*==\s*Role\.Survivor\s*\)"));
            Assert.That(Regex.Matches(app, @"fakeKiller\.Inject\(").Count, Is.EqualTo(1));
        }

        [Test]
        public void FakeKillerFlag_IsInertWhenAbsent_AndValidatedWhenPresent()
        {
            DevArgs none = DevArgs.Parse(new string[0]);
            Assert.That(none.FakeKiller, Is.EqualTo(0f));
            Assert.That(none.Active, Is.False);
            Assert.That(none.Errors, Is.Empty);

            DevArgs on = DevArgs.Parse(new[] { "-ptk-fake-killer", "8" });
            Assert.That(on.FakeKiller, Is.EqualTo(8f));
            Assert.That(on.Active, Is.True);
            Assert.That(on.Errors, Is.Empty);

            foreach (string bad in new[] { "0", "-3", "abc", "NaN", "Infinity", "31" })
            {
                DevArgs parsed = DevArgs.Parse(new[] { "-ptk-fake-killer", bad });
                Assert.That(parsed.FakeKiller, Is.EqualTo(0f), "value '" + bad + "' is rejected");
                Assert.That(parsed.Errors.Count, Is.EqualTo(1), "value '" + bad + "' reports one error");
            }
            DevArgs missing = DevArgs.Parse(new[] { "-ptk-fake-killer" });
            Assert.That(missing.FakeKiller, Is.EqualTo(0f));
            Assert.That(missing.Errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void FakeKiller_AddsExactlyOneContractShapedEntry_NeverToSpectators_NeverDuplicates()
        {
            List<PlayerState> Players(PlayerStatus own) => new List<PlayerState>
            {
                new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, Status = (byte)own, X = 100, Z = 100, Yaw = 0 }
            };
            List<PlayerState> live = Players(PlayerStatus.Normal);
            new FakeKiller(8).Inject(live, OwnId, 0);
            PlayerState[] killers = live.Where(p => p.Role == (byte)Role.Killer).ToArray();
            Assert.That(killers.Length, Is.EqualTo(1));
            Assert.That(killers[0].Flags, Is.EqualTo((byte)0), "contract: Flags = 0");
            Assert.That(killers[0].StatusTimer, Is.EqualTo(0f), "contract: StatusTimer = 0");
            Assert.That(killers[0].CaptureCount, Is.EqualTo((byte)0), "contract: CaptureCount = 0");
            Assert.That(live.Count, Is.EqualTo(2));

            foreach (PlayerStatus status in new[] { PlayerStatus.Eliminated, PlayerStatus.Escaped })
            {
                List<PlayerState> spectator = Players(status);
                new FakeKiller(8).Inject(spectator, OwnId, 0);
                Assert.That(spectator.Count, Is.EqualTo(1), "a spectator gets no Killer entry (" + status + ")");
            }
            List<PlayerState> real = Players(PlayerStatus.Normal);
            real.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = 10, Z = 10 });
            new FakeKiller(8).Inject(real, OwnId, 0);
            Assert.That(real.Count(p => p.Role == (byte)Role.Killer), Is.EqualTo(1), "a real entry is never doubled");
        }

        // ------------------------------------------------------------------------------------------------------------------
        // 3. Threat visuals
        // ------------------------------------------------------------------------------------------------------------------

        [TestCase((byte)0, ThreatEffect.None, 0f)]
        [TestCase((byte)1, ThreatEffect.MeterStrip, 1f / 3)]
        [TestCase((byte)2, ThreatEffect.MeterStrip, 2f / 3)]
        [TestCase((byte)3, ThreatEffect.ScreenEdge, 0f)]
        [TestCase((byte)4, ThreatEffect.None, 0f)]
        [TestCase((byte)9, ThreatEffect.None, 0f)]
        [TestCase((byte)255, ThreatEffect.None, 0f)]
        public void ThreatLevel_ShowsExactlyOneEffect_OutOfRangeReadsAsZero(byte level, ThreatEffect effect, float fraction)
        {
            var visual = new ThreatVisual(new System.Random(1));
            visual.Update(level, PlayerStatus.Normal);
            Assert.That(visual.Effect, Is.EqualTo(effect));
            Assert.That(visual.StripFraction, Is.EqualTo(fraction).Within(1e-6f));
            if (effect == ThreatEffect.None) Assert.That(visual.Level, Is.EqualTo((byte)0));
            Assert.That(ThreatVisual.Effective(level, PlayerStatus.Normal), Is.EqualTo(effect == ThreatEffect.None ? (byte)0 : level));
        }

        [Test]
        public void ThreatMask_IsRerolledOnlyWhenTheLevelChanges()
        {
            var visual = new ThreatVisual(new System.Random(12345));
            Assert.That(visual.Update(0, PlayerStatus.Normal), Is.False);
            Assert.That(visual.Rolls, Is.EqualTo(0), "level 0 draws nothing and rolls nothing");
            Assert.That(visual.Update(1, PlayerStatus.Normal), Is.True);
            int seed1 = visual.Seed;
            bool[] first = ThreatVisual.Mask(seed1, 100, 4);
            for (int frame = 0; frame < 2000; frame++)
            {
                Assert.That(visual.Update(1, PlayerStatus.Normal), Is.False, "same level, frame " + frame);
                if (frame % 400 == 0) Assert.That(ThreatVisual.Mask(visual.Seed, 100, 4), Is.EqualTo(first), "identical mask, frame " + frame);
            }
            Assert.That(visual.Seed, Is.EqualTo(seed1));
            Assert.That(visual.Rolls, Is.EqualTo(1));

            Assert.That(visual.Update(2, PlayerStatus.Normal), Is.True);
            Assert.That(visual.Rolls, Is.EqualTo(2));
            Assert.That(visual.Seed, Is.Not.EqualTo(seed1), "a level change draws a new mask");
            Assert.That(ThreatVisual.Mask(visual.Seed, 100, 4), Is.Not.EqualTo(first));
            int seed2 = visual.Seed;
            Assert.That(visual.Update(3, PlayerStatus.Normal), Is.True);
            Assert.That(visual.Rolls, Is.EqualTo(3));
            Assert.That(visual.Seed, Is.Not.EqualTo(seed2));
            Assert.That(visual.Update(0, PlayerStatus.Normal), Is.True, "0 is a change");
            Assert.That(visual.Rolls, Is.EqualTo(3), "but shows nothing, so no roll");
            Assert.That(visual.Update(3, PlayerStatus.Normal), Is.True);
            Assert.That(visual.Rolls, Is.EqualTo(4), "coming back rolls again");
        }

        [Test]
        public void ThreatVisual_IsHiddenWhileEliminatedOrEscaped_AndStaysQuiet()
        {
            foreach (PlayerStatus hidden in new[] { PlayerStatus.Eliminated, PlayerStatus.Escaped })
            {
                var visual = new ThreatVisual(new System.Random(2));
                visual.Update(3, PlayerStatus.Normal);
                int rolls = visual.Rolls;
                visual.Update(3, hidden);
                Assert.That(visual.Effect, Is.EqualTo(ThreatEffect.None), hidden.ToString());
                Assert.That(visual.Level, Is.EqualTo((byte)0));
                foreach (byte level in new byte[] { 1, 2, 3, 2, 1, 3 })
                {
                    visual.Update(level, hidden);
                    Assert.That(visual.Effect, Is.EqualTo(ThreatEffect.None), hidden + " level " + level);
                }
                Assert.That(visual.Rolls, Is.EqualTo(rolls), "no rolls while hidden");
                Assert.That(ThreatVisual.Effective(3, hidden), Is.EqualTo((byte)0), "the beep is fed 0 too");
            }
        }

        [Test]
        public void ThreatScratch_PlaysOncePerEntryIntoLevelTwo_AndNeverWhileHidden()
        {
            // The ClientApp rule: `if (threatVisual.Update(level, status) && threatVisual.Level == 2) PlayOneShot(scratch)`.
            string app = Code("ClientApp.cs");
            Assert.That(Regex.Matches(app, @"PlayOneShot\(survivorCues\[SurvivorCue\.ThreatScratch\]\)").Count, Is.EqualTo(1));
            Assert.That(app, Does.Match(@"if\s*\(\s*threatVisual\.Update\(threatLevel,\s*own\.Status\)\s*&&\s*threatVisual\.Level\s*==\s*2\s*\)\s*cueSource\.PlayOneShot\(survivorCues\[SurvivorCue\.ThreatScratch\]\)"));

            var visual = new ThreatVisual(new System.Random(3));
            int scratches = 0;
            void Push(byte level, PlayerStatus status) { if (visual.Update(level, status) && visual.Level == 2) scratches++; }
            foreach (byte level in new byte[] { 0, 1, 2, 2, 2, 3, 2, 1, 2, 0, 2, 2 }) Push(level, PlayerStatus.Normal);
            Assert.That(scratches, Is.EqualTo(4), "entries into 2: 1->2, 3->2, 1->2, 0->2");
            Push(2, PlayerStatus.Eliminated);
            Push(2, PlayerStatus.Eliminated);
            Assert.That(scratches, Is.EqualTo(4), "nothing while Eliminated");
        }

        // ------------------------------------------------------------------------------------------------------------------
        // 4. Capture banner
        // ------------------------------------------------------------------------------------------------------------------

        [Test]
        public void CaptureBanner_FiresOncePerNormalToLaggingTransition_AndAgainAfterARescue()
        {
            var watch = new CaptureWatch();
            Assert.That(watch.Observe(PlayerStatus.Normal, 0, 0, 100), Is.EqualTo(StatusEvent.None));
            Assert.That(watch.Observe(PlayerStatus.Lagging, 60, 1, 101), Is.EqualTo(StatusEvent.Captured));
            Assert.That(watch.Banners, Is.EqualTo(1));
            for (int i = 0; i < 30; i++)
                Assert.That(watch.Observe(PlayerStatus.Lagging, 60 - i * .05f, 1, 101 + i * .05), Is.Not.EqualTo(StatusEvent.Captured), "repeat snapshot " + i);
            Assert.That(watch.Banners, Is.EqualTo(1));
            Assert.That(watch.Observe(PlayerStatus.Normal, 0, 1, 110), Is.EqualTo(StatusEvent.Rescued));
            Assert.That(watch.BannerVisible(110), Is.False);
            Assert.That(watch.Observe(PlayerStatus.Lagging, 60, 1, 120), Is.EqualTo(StatusEvent.Captured), "a second capture after a rescue");
            Assert.That(watch.Banners, Is.EqualTo(2));
            Assert.That(watch.BannerVisible(120.1), Is.True);
        }

        [Test]
        public void CaptureBanner_ShowsForAboutThreeSeconds_ThenStaysGoneWhileStillLagging()
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 50);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 51);
            Assert.That(watch.BannerVisible(51), Is.True);
            Assert.That(watch.BannerVisible(53.9), Is.True);
            Assert.That(watch.BannerVisible(54.1), Is.False, "about 3 s");
            watch.Observe(PlayerStatus.Lagging, 40, 1, 70);
            Assert.That(watch.BannerVisible(70), Is.False, "later Lagging snapshots never show it again");
            Assert.That(watch.Banners, Is.EqualTo(1));
        }

        [Test]
        public void CaptureBanner_NeverFiresOnOtherTransitions_OrOnTheFirstSnapshotOfAMatch()
        {
            // Reconnect / first snapshot already Lagging: baseline only.
            var reconnect = new CaptureWatch();
            Assert.That(reconnect.Observe(PlayerStatus.Lagging, 30, 1, 10), Is.EqualTo(StatusEvent.None));
            Assert.That(reconnect.Banners, Is.EqualTo(0));
            Assert.That(reconnect.BannerVisible(10.5), Is.False);
            // ... and its elimination is still explained from that baseline.
            Assert.That(reconnect.Observe(PlayerStatus.Eliminated, 0, 1, 40, true), Is.EqualTo(StatusEvent.Eliminated));
            Assert.That(reconnect.Reason, Is.EqualTo(EliminationReason.Collapse), "GDD v2: no execution; with the collapse running it is the collapse");

            var others = new (PlayerStatus from, PlayerStatus to)[]
            {
                (PlayerStatus.Normal, PlayerStatus.Eliminated), (PlayerStatus.Normal, PlayerStatus.Escaped),
                (PlayerStatus.Lagging, PlayerStatus.Eliminated), (PlayerStatus.Lagging, PlayerStatus.Normal)
            };
            foreach (var step in others)
            {
                var watch = new CaptureWatch();
                watch.Observe(step.from, step.from == PlayerStatus.Lagging ? 30 : 0, 1, 1);
                StatusEvent e = watch.Observe(step.to, 0, 1, 2);
                Assert.That(e, Is.Not.EqualTo(StatusEvent.Captured), step.from + " -> " + step.to);
                Assert.That(watch.Banners, Is.EqualTo(0), step.from + " -> " + step.to);
                Assert.That(watch.BannerVisible(2.1), Is.False);
            }
        }

        [Test]
        public void CaptureBanner_IsRemovedAtOnceWhenTheStatusLeavesLagging_AndResetForgetsTheMatch()
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 1);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 2);
            watch.Observe(PlayerStatus.Eliminated, 0, 1, 3);
            Assert.That(watch.BannerVisible(3.1), Is.False, "executed within the 3 s: no banner over the elimination reason");
            watch.Reset();
            Assert.That(watch.Banners, Is.EqualTo(0));
            Assert.That(watch.Reason, Is.EqualTo(EliminationReason.None));
            Assert.That(watch.ReasonText(), Is.Null);
            Assert.That(watch.Observe(PlayerStatus.Lagging, 60, 1, 10), Is.EqualTo(StatusEvent.None), "after a reset the next snapshot is a baseline again");
        }

        [Test]
        public void CaptureBanner_CopyIsPlainFixedVocabularyWithTheRuleNumbers()
        {
            // Superseded by the approved Q-04 change (HUD_SPEC 4.1 (10/30), attempt 2): the banner starts with the meter's
            // state word, `Lagging.`, instead of `Captured.`. The rule numbers and the plain-copy checks are kept.
            string banner = CaptureWatch.Banner;
            string stateWord = SurvivorReadout.Meter(Own(PlayerStatus.Lagging, 60, 1), null, 0, 0, null).Word;
            Assert.That(stateWord, Is.EqualTo("Lagging"));
            Assert.That(banner, Does.StartWith(stateWord + "."), "the banner names the state the meter shows");
            Assert.That(banner, Does.Not.Contain("Captured"), "one term per concept (SLOP 4)");
            Assert.That(banner, Does.Contain("Survivor"));
            Assert.That(banner, Does.Contain("1.5 m"));
            Assert.That(banner, Does.Contain(" 3 s"));
            Assert.That(banner, Does.Contain("60 s left"));
            Assert.That(banner, Does.Not.Contain("!"));
            Assert.That(banner, Does.Not.Match(@"\b(ally|Caught|caught|Rescue it|Get)\b"), "no draft wording, no hype");
            Assert.That(GameConstants.RescueRadius, Is.EqualTo(1.5f));
            Assert.That(GameConstants.RescueTimeSeconds, Is.EqualTo(3f));
            Assert.That(GameConstants.LaggingTimeoutSeconds, Is.EqualTo(60f));
            Assert.That(banner.Length, Is.LessThanOrEqualTo(70), "one line in the 656 u prompt row at M");
            Assert.That(CaptureWatch.BannerPrompt.Danger, Is.True);
            Assert.That(CaptureWatch.BannerPrompt.Text, Is.EqualTo(banner));
            string spec = DocsFile(Path.Combine("ui", "HUD_SPEC.md"));
            if (spec != null) Assert.That(spec, Does.Contain(banner), "HUD_SPEC 4.1 carries the same banner copy");
        }

        // ------------------------------------------------------------------------------------------------------------------
        // 5. Elimination reason
        // ------------------------------------------------------------------------------------------------------------------

        // Server-shaped sequence: Normal, Lagging snapshots (the last one carries `lastLagging`), then the Eliminated snapshot
        // (the server writes StatusTimer = 0 and keeps CaptureCount = 1 on it).
        private static CaptureWatch Eliminate(float lastLagging)
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 1);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 2);
            watch.Observe(PlayerStatus.Lagging, lastLagging + .05f, 1, 3);
            watch.Observe(PlayerStatus.Lagging, lastLagging, 1, 3.05);
            Assert.That(watch.Observe(PlayerStatus.Eliminated, 0, 1, 3.1), Is.EqualTo(StatusEvent.Eliminated));
            return watch;
        }

        [TestCase(0f, EliminationReason.TimedOut)]
        [TestCase(.05f, EliminationReason.TimedOut)]
        [TestCase(.2f, EliminationReason.TimedOut)]
        [TestCase(.21f, EliminationReason.None)]
        [TestCase(1f, EliminationReason.None)]
        [TestCase(30f, EliminationReason.None)]
        [TestCase(59.9f, EliminationReason.None)]
        public void LaggingToEliminated_UsesTheLastLaggingTimer_NotTheEliminatedSnapshots(float lastLaggingTimer, EliminationReason expected)
        {
            Assert.That(Eliminate(lastLaggingTimer).Reason, Is.EqualTo(expected));
        }

        [Test]
        // GDD v2 4 (U6c-wire S5): the third capture eliminates; this test checked the v1 second capture.
        public void NormalToEliminated_AfterTwoCaptures_IsTheThirdCapture_AndOtherwiseNothingIsInferred()
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 1);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 2);
            watch.Observe(PlayerStatus.Normal, 0, 2, 20);
            Assert.That(watch.Reason, Is.EqualTo(EliminationReason.None), "nothing is inferred before an elimination");
            Assert.That(watch.Observe(PlayerStatus.Eliminated, 0, 3, 30), Is.EqualTo(StatusEvent.Eliminated));
            Assert.That(watch.Reason, Is.EqualTo(EliminationReason.ThirdCapture));

            var direct = new CaptureWatch();
            direct.Observe(PlayerStatus.Normal, 0, 2, 1);
            direct.Observe(PlayerStatus.Eliminated, 0, 2, 2);
            Assert.That(direct.Reason, Is.EqualTo(EliminationReason.ThirdCapture), "Normal with CaptureCount 2 -> Eliminated, directly");

            var unexplained = new CaptureWatch();
            unexplained.Observe(PlayerStatus.Normal, 0, 0, 1);
            unexplained.Observe(PlayerStatus.Eliminated, 0, 0, 2);
            Assert.That(unexplained.Reason, Is.EqualTo(EliminationReason.None));
            Assert.That(unexplained.ReasonText(), Is.Null, "no reason line is invented");
        }

        [Test]
        // GDD v2 (U6c-wire S5, R1): the reasons have no execution wording; the 13.7 result names the outcome only.
        public void TheThreeReasonLines_HaveTheSpecCopy_OnTheMeter_AndTheResultNamesTheOutcome()
        {
            var cases = new (EliminationReason reason, string text)[]
            {
                (EliminationReason.TimedOut, "Disconnected: no rescue in the second Lagging stage (30 s)."),
                (EliminationReason.Collapse, "Disconnected: the shutdown reached you."),
                (EliminationReason.ThirdCapture, "Disconnected: caught with no Lagging stage left."),
            };
            var result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            result.EliminatedIds.Add(OwnId);
            foreach (var c in cases)
            {
                Assert.That(CaptureWatch.ReasonText(c.reason), Is.EqualTo(c.text), c.reason.ToString());
                MeterState meter = SurvivorReadout.Meter(Own(PlayerStatus.Eliminated), null, 0, 0, c.text);
                Assert.That(meter.Word, Is.EqualTo("Eliminated"));
                Assert.That(meter.Reason, Is.EqualTo(c.text), "the meter shows the reason");
                Assert.That(meter.Wrap, Is.True);
                Assert.That(ResultV2Copy.Outcome(V2Wire.Result(result, null, OwnId, Role.Survivor)), Is.EqualTo("You were eliminated."), "the result screen's personal line");
            }
            Assert.That(CaptureWatch.ReasonText(EliminationReason.None), Is.Null);
        }

        [Test]
        public void ReasonLine_IsOnlyForAnEliminatedSurvivor()
        {
            const string reason = "Disconnected: caught with no Lagging stage left.";
            var killerWins = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };
            killerWins.EliminatedIds.Add(OwnId);
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(killerWins, null, OwnId, Role.Survivor)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(killerWins, null, 77, Role.Killer)), Does.Not.Contain(reason), "Killer client");
            foreach (PlayerStatus status in new[] { PlayerStatus.Normal, PlayerStatus.Lagging, PlayerStatus.Escaped })
                Assert.That(SurvivorReadout.Meter(Own(status, 30, 1), null, 0, 0, reason).Reason, Is.Not.EqualTo(reason), "meter, " + status);
            Assert.That(SurvivorReadout.Meter(Own(PlayerStatus.Eliminated), null, 0, 0, null).Reason, Is.EqualTo("Watching until the match ends."), "no reason: the old line");
        }

        [Test]
        public void Docs_HudSpecCarriesTheSameReasonAndBannerCopyAsTheCode()
        {
            string spec = DocsFile(Path.Combine("ui", "HUD_SPEC.md"));
            if (spec == null) Assert.Ignore("docs/ui/HUD_SPEC.md not reachable from Application.dataPath");
            Assert.That(spec, Does.Contain(CaptureWatch.Collapse));
            Assert.That(spec, Does.Contain(CaptureWatch.TimedOut));
            Assert.That(spec, Does.Contain(CaptureWatch.ThirdCapture));
            Assert.That(spec, Does.Contain(CaptureWatch.Caught(3, false)));
            Assert.That(spec, Does.Contain(CaptureWatch.Caught(2, true)));
            Assert.That(spec, Does.Contain(SurvivorReadout.NextCaptureEliminates));
            Assert.That(spec, Does.Contain(CaptureWatch.Banner));
        }

        // ------------------------------------------------------------------------------------------------------------------
        // 6. F-03: skill_ready gated on the own status
        // ------------------------------------------------------------------------------------------------------------------

        [Test]
        public void SkillReady_PlaysOnlyWhileNormal()
        {
            foreach (PlayerStatus status in Enum.GetValues(typeof(PlayerStatus)))
                Assert.That(CaptureWatch.SkillReadyAudible(status), Is.EqualTo(status == PlayerStatus.Normal), status.ToString());
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Lagging), Is.False);
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Eliminated), Is.False, "spectating");
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Escaped), Is.False);

            string app = Code("ClientApp.cs");
            Assert.That(Regex.Matches(app, @"PlayOneShot\(readyClip\)").Count, Is.EqualTo(1), "one skill_ready play site");
            Assert.That(app, Does.Match(@"if\s*\(\s*survivor\s*&&\s*audible\s*\)\s*cueSource\.PlayOneShot\(readyClip\)"));
            Assert.That(app, Does.Match(@"bool audible = !survivor \|\| CaptureWatch\.SkillReadyAudible\(own\.Status\)"));
            Assert.That(app, Does.Contain("killerLayer?.OnSkillReady()"), "the Killer client's skill_ready is not gated");
        }

        // ------------------------------------------------------------------------------------------------------------------
        // Lagging stutter
        // ------------------------------------------------------------------------------------------------------------------

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void StutterClock_PresentsEightFramesPerSecond_NoMatterTheFrameRate(int fps)
        {
            var clock = new StutterClock();
            int presents = 0;
            double last = double.NaN;
            for (int k = 0; k < fps; k++)
            {
                double t = 100.0 + (double)k / fps;
                if (!clock.Present(t, false)) continue;
                presents++;
                if (!double.IsNaN(last)) Assert.That(t - last, Is.GreaterThan(.125 - 1.0 / fps), "frames are held about 125 ms");
                last = t;
            }
            Assert.That(presents, Is.EqualTo(8));
        }

        [Test]
        public void StutterClock_ReducedModePresentsEveryFrame_AndResetPresentsTheNextOne()
        {
            var reduced = new StutterClock();
            for (int k = 0; k < 120; k++) Assert.That(reduced.Present(50 + k / 60.0, true), Is.True, "reduced, frame " + k);
            var clock = new StutterClock();
            Assert.That(clock.Present(10.0, false), Is.True);
            Assert.That(clock.Present(10.0, false), Is.False);
            Assert.That(clock.Present(10.05, false), Is.False, "still inside the 125 ms hold");
            clock.Reset();
            Assert.That(clock.Present(10.05, false), Is.True, "a new Lagging period shows its first frame at once");
            Assert.That(StutterClock.PreviousWeight, Is.EqualTo(.3f), "30 % previous-frame blend");
            Assert.That(StutterClock.Hz, Is.EqualTo(8d));
        }

        [Test]
        public void ReduceStutterFlag_IsAPlayerOption_NotADevCaptureFlag()
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-reduce-stutter" });
            Assert.That(args.ReduceStutter, Is.True);
            Assert.That(args.Active, Is.False, "does not turn the dev capture component on");
            Assert.That(args.Errors, Is.Empty);
            Assert.That(DevArgs.Parse(new string[0]).ReduceStutter, Is.False);
            string app = Code("ClientApp.cs");
            Assert.That(app, Does.Contain("stutter.Initialize(view, screens.StutterBack, screens.StutterFront, devArgs.ReduceStutter);"));
            Assert.That(Code("World/LaggingStutter.cs"), Does.Contain("clock.Present(Time.unscaledTimeAsDouble, reduced)"), "reduced reaches the clock");
        }

        [Test]
        public void Stutter_RunsOnlyWhileTheOwnSurvivorIsLagging_AndStopsOnEveryExit()
        {
            string app = Code("ClientApp.cs");
            Assert.That(Regex.Matches(app, @"stutter\.SetActive\(own\.Status == PlayerStatus\.Lagging\)").Count, Is.EqualTo(1));
            int off = Regex.Matches(app, @"stutter\??\.SetActive\(false\)").Count;
            Assert.That(off, Is.GreaterThanOrEqualTo(3), "match start, match end and leaving the match all switch it off");
            Assert.That(Code("World/LaggingStutter.cs"), Does.Contain("view.targetTexture = null"), "the camera is given back to the screen");
        }

        // ------------------------------------------------------------------------------------------------------------------
        // Attempt 2: the fix round and the Q-01 ruling (HUD_SPEC 4.2 (10/30): strip 8 u tall at 45 % density)
        // ------------------------------------------------------------------------------------------------------------------

        [Test]
        public void A2_Q01_StripSpec_EightUnitsTallAtFortyFivePercent_BandUnchanged_FractionsOneAndTwoThirds()
        {
            Assert.That(ThreatVisual.StripHeight, Is.EqualTo(8f));
            Assert.That(ThreatVisual.StripDensity, Is.EqualTo(.45f).Within(1e-6f));
            Assert.That(ThreatVisual.BandWidth, Is.EqualTo(24f), "level 3 band unchanged");
            Assert.That(ThreatVisual.Density, Is.EqualTo(.3f).Within(1e-6f), "level 3 density unchanged");
            Assert.That(ThreatVisual.StripCell * 4, Is.EqualTo(ThreatVisual.StripHeight), "at least four rows of noise in the strip");

            int set = 0, total = 0, bandSet = 0, bandTotal = 0;
            for (int seed = 1; seed <= 60; seed++)
            {
                foreach (bool b in ThreatVisual.Mask(seed * 7919, 100, 4, ThreatVisual.StripDensity)) { total++; if (b) set++; }
                foreach (bool b in ThreatVisual.Mask(seed * 104729, 100, 12)) { bandTotal++; if (b) bandSet++; }
            }
            Assert.That(set / (float)total, Is.EqualTo(.45f).Within(.02f), "strip mask is about 45 % `danger`");
            Assert.That(bandSet / (float)bandTotal, Is.EqualTo(.3f).Within(.02f), "band mask stays about 30 %");

            var visual = new ThreatVisual(new System.Random(5));
            visual.Update(1, PlayerStatus.Normal);
            Assert.That(visual.Effect, Is.EqualTo(ThreatEffect.MeterStrip));
            Assert.That(visual.StripFraction, Is.EqualTo(1f / 3).Within(1e-6f));
            visual.Update(2, PlayerStatus.Normal);
            Assert.That(visual.StripFraction, Is.EqualTo(2f / 3).Within(1e-6f));
            visual.Update(3, PlayerStatus.Normal);
            Assert.That(visual.Effect, Is.EqualTo(ThreatEffect.ScreenEdge), "level 3 is the band, no strip");

            string spec = DocsFile(Path.Combine("ui", "HUD_SPEC.md"));
            if (spec != null)
            {
                Assert.That(spec, Does.Contain("8 u (10/30)"));
                Assert.That(spec, Does.Contain("45 % density (10/30)"));
            }
        }

        [Test]
        public void A2_Q01_TheHudDrawsTheStrip_EightUnitsTall_OneThirdAndTwoThirdsOfTheMeter()
        {
            var hud = new SurvivorHud(new UnityEngine.UIElements.VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.SetThreat(ThreatEffect.MeterStrip, 1f / 3, 21);
                Assert.That(hud.ThreatShown, Is.EqualTo(ThreatEffect.MeterStrip));
                Assert.That(hud.ThreatStripHeight, Is.EqualTo(8f), "8 u tall");
                Assert.That(hud.ThreatStripWidth, Is.EqualTo(Mathf.Round(SurvivorHud.MeterWidth / 3f)), "1/3 of the meter at level 1");
                Texture2D one = hud.ThreatTexture(0);
                Assert.That(one.height * ThreatVisual.StripCell, Is.EqualTo(8f));
                int danger = 0;
                Color32[] px = one.GetPixels32();
                foreach (Color32 p in px) if (p.a != 0) danger++;
                Assert.That(danger / (float)px.Length, Is.EqualTo(.45f).Within(.1f), "about 45 % `danger` texels");
                hud.SetThreat(ThreatEffect.MeterStrip, 2f / 3, 22);
                Assert.That(hud.ThreatStripHeight, Is.EqualTo(8f));
                Assert.That(hud.ThreatStripWidth, Is.EqualTo(Mathf.Round(SurvivorHud.MeterWidth * 2f / 3f)), "2/3 at level 2");
                hud.SetThreat(ThreatEffect.ScreenEdge, 0, 23);
                Assert.That(hud.ThreatShown, Is.EqualTo(ThreatEffect.ScreenEdge));
                hud.SetThreat(ThreatEffect.None, 0, 23);
                Assert.That(hud.ThreatShown, Is.EqualTo(ThreatEffect.None));
            }
            finally { hud.Dispose(); }
        }

        // Independent width estimate (not the implementer's font measurement): 0.62 x size per character at weight 600, wider
        // than HUD_SPEC 0.1's 0.55 average so a pass here has headroom. Survivor name: S, 14 u at 1080p and the 15 u 720p floor,
        // in the 100 u name column. Killer name: M 18 u on one line in the 232 u column.
        private const float BoldAdvance = .62f;
        private static float Estimate(string text, float size) => text.Length * BoldAdvance * size;

        [Test]
        public void A2_Q02_NoSkillNameBreaksInsideAWord_AtEitherCanvas()
        {
            var survivorNames = new List<string>();
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++)
            {
                string hud = SurvivorReadout.SkillName(id), table = HudCopy.SkillName(Role.Survivor, id);
                Assert.That(hud, Is.Not.Null.And.Not.Empty);
                Assert.That(table.ToLowerInvariant(), Is.EqualTo(hud.ToLowerInvariant()), "both tables name skill " + id + " alike");
                survivorNames.Add(hud);
            }
            Assert.That(survivorNames.Count, Is.EqualTo(5));
            float column = SurvivorHud.SkillNameWidth;
            foreach (float size in new[] { 14f, 15f })
                foreach (string name in survivorNames)
                {
                    int lines = 1; float line = 0, space = Estimate(" ", size);
                    foreach (string word in name.Split(' '))
                    {
                        float w = Estimate(word, size);
                        Assert.That(w, Is.LessThanOrEqualTo(column), "'" + word + "' (" + name + ") at S " + size + " u would break inside the word in a " + column + " u column");
                        if (line > 0 && line + space + w > column) { lines++; line = w; } else line += line > 0 ? space + w : w;
                    }
                    Assert.That(lines, Is.LessThanOrEqualTo(2), name + " at " + size + " u: at most two lines in the 36 u name label");
                }

            for (byte id = 0; id < SkillIds.KillerSkillCount; id++)
            {
                string cluster = KillerHudCopy.SkillName(id), table = HudCopy.SkillName(Role.Killer, id);
                Assert.That(table.ToLowerInvariant(), Is.EqualTo(cluster.ToLowerInvariant()));
                Assert.That(Estimate(cluster, 18), Is.LessThanOrEqualTo(232f), cluster + " fits one line in the Killer cluster's 232 u column");
            }

            string uss = File.ReadAllText(Path.Combine(QaPaths.Assets(), "Resources", "ClientUI.uss"));
            Match nameWidth = Regex.Match(uss, @"\.unity-label\.sname\s*\{[^}]*?\bwidth:\s*(\d+)px");
            Assert.That(nameWidth.Success, Is.True, "the name label has a fixed width in the USS");
            Assert.That(float.Parse(nameWidth.Groups[1].Value), Is.EqualTo(column), "USS width equals the constant the layout was checked against");
            Assert.That(uss, Does.Match(@"\.unity-label\.sname\s*\{[^}]*white-space:\s*normal"), "wraps at spaces");
            // 1280x720 and 1920x1080 share one logical layout in u (the panel scale only changes the canvas), so one check covers both.
        }

        private static S2C_WorldState A2World(uint tick, bool killer, PlayerStatus own)
        {
            var state = new S2C_WorldState { Tick = tick };
            state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, Status = (byte)own, X = 5, Z = 36 });
            if (killer) state.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = 9, Z = 36, Yaw = 270 });
            return state;
        }

        private static float A2Alpha(Transform root, string child) => root.Find(child).GetComponent<Renderer>().sharedMaterial.color.a;

        [Test]
        public void A2_Q03_TheVisorFadesWithTheBody_ReadFromTheMaterials()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // edit mode: the world view's Destroy() logs
            var host = new GameObject("U4e QA a2 visor");
            try
            {
                var view = host.AddComponent<WorldView>();
                view.Initialize(OwnId, Role.Survivor);
                for (int i = 1; i <= 16; i++) view.Accept(A2World((uint)i, i <= 5, PlayerStatus.Normal), i * Tick);
                Transform root = host.transform.Find("Killer (line of sight)");
                Assert.That(root, Is.Not.Null);
                view.Render(At(5 * Tick));
                Assert.That(root.gameObject.activeSelf, Is.True);
                Assert.That(A2Alpha(root, "Killer body"), Is.EqualTo(1f).Within(1e-3f));
                Assert.That(A2Alpha(root, "Killer visor"), Is.EqualTo(1f).Within(1e-3f));
                foreach (double after in new[] { .05, .15, .25, .29 })
                {
                    view.Render(At(6 * Tick + after));
                    float body = A2Alpha(root, "Killer body"), visor = A2Alpha(root, "Killer visor");
                    Assert.That(body, Is.EqualTo(1 - after / .3).Within(.02), "body +" + after);
                    Assert.That(visor, Is.EqualTo(body).Within(1e-3f), "the visor is exactly as faded as the body, +" + after);
                }
                view.Render(At(6 * Tick + .35));
                Assert.That(root.gameObject.activeSelf, Is.False, "gone after 0.3 s, body and visor together");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [TestCase(PlayerStatus.Eliminated)]
        [TestCase(PlayerStatus.Escaped)]
        public void A2_Q04b_NoKillerModelIsDrawnWhileSpectating_EvenWithAnEntryInEverySnapshot(PlayerStatus spectator)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var host = new GameObject("U4e QA a2 spectator");
            try
            {
                var view = host.AddComponent<WorldView>();
                view.Initialize(OwnId, Role.Survivor);
                for (int i = 1; i <= 12; i++) view.Accept(A2World((uint)i, true, spectator), i * Tick);
                Transform root = host.transform.Find("Killer (line of sight)");
                Assert.That(root, Is.Not.Null);
                for (double r = 2 * Tick; r <= 11 * Tick; r += .01)
                {
                    view.Render(At(r));
                    Assert.That(root.gameObject.activeSelf, Is.False, spectator + " r=" + r);
                    Assert.That(view.KillerAlpha, Is.EqualTo(0f), spectator + " r=" + r);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [TestCase(PlayerStatus.Normal)]
        [TestCase(PlayerStatus.Lagging)]
        public void A2_Q04b_ControlTheModelIsStillDrawnForALivingSurvivor(PlayerStatus own)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var host = new GameObject("U4e QA a2 control");
            try
            {
                var view = host.AddComponent<WorldView>();
                view.Initialize(OwnId, Role.Survivor);
                for (int i = 1; i <= 12; i++) view.Accept(A2World((uint)i, true, own), i * Tick);
                view.Render(At(6 * Tick));
                Assert.That(host.transform.Find("Killer (line of sight)").gameObject.activeSelf, Is.True, own.ToString());
                Assert.That(view.KillerAlpha, Is.EqualTo(1f));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
