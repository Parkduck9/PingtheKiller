using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Tests
{
    // Killer HUD logic (HUD_SPEC 3, 6.1, 6.2, 6.4, 6.5): hysteresis, execution guidance, copy, cue rules, SelfState seam.
    public sealed class KillerHudTests
    {
        private const string Dot = "·";

        [Test]
        public void AClassificationSwitchesOnlyAfterHoldingOneHundredMilliseconds()
        {
            var held = new HeldClass();
            held.BeginFrame();
            Assert.That(held.Update(1, 0, 0), Is.EqualTo(0), "a new key takes its value at once");
            held.EndFrame();
            held.BeginFrame(); Assert.That(held.Update(1, 1, .02), Is.EqualTo(0)); held.EndFrame();
            held.BeginFrame(); Assert.That(held.Update(1, 0, .05), Is.EqualTo(0), "flicker back resets"); held.EndFrame();
            held.BeginFrame(); Assert.That(held.Update(1, 1, .06), Is.EqualTo(0)); held.EndFrame();
            held.BeginFrame(); Assert.That(held.Update(1, 1, .15), Is.EqualTo(0), "90 ms"); held.EndFrame();
            held.BeginFrame(); Assert.That(held.Update(1, 1, .16), Is.EqualTo(1), "100 ms"); held.EndFrame();
            held.BeginFrame(); held.EndFrame();
            Assert.That(held.Count, Is.EqualTo(0), "a key not updated in a frame is forgotten");
        }

        [Test]
        public void ExecutionGuidanceMirrorsTheServerReArmRule()
        {
            var guide = new ExecutionGuidance();
            // A new capture: the target appears in contact, in grace.
            Assert.That(guide.Update(7, 1f, true, 1.6f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(guide.Armed, Is.False);
            // Grace over, never stepped out: step back.
            Assert.That(guide.Update(7, 1f, true, 0), Is.EqualTo(ExecutionPrompt.StepBack));
            // Out of contact: armed.
            Assert.That(guide.Update(7, 1.5f, true, 0), Is.EqualTo(ExecutionPrompt.Ready));
            Assert.That(guide.Update(7, 1f, true, 0), Is.EqualTo(ExecutionPrompt.Ready), "touching again after the grace eliminates");
            // A touch during the grace is spent: disarmed.
            guide.Update(7, 1.5f, true, .5f);
            Assert.That(guide.Update(7, 1f, true, .4f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(guide.Armed, Is.False);
            Assert.That(guide.Update(7, 1f, true, 0), Is.EqualTo(ExecutionPrompt.StepBack));
            // A different instance becomes the target: disarmed even if it was far.
            guide.Update(7, 2f, true, 0);
            Assert.That(guide.Armed, Is.True);
            guide.Update(9, 1f, true, 0);
            Assert.That(guide.Armed, Is.False);
            Assert.That(guide.Target, Is.EqualTo(9));
            // No Lagging instance within 3 m: no prompt.
            Assert.That(guide.Update(9, 3.5f, true, 0), Is.EqualTo(ExecutionPrompt.None));
            Assert.That(guide.Update(-1, 0, true, 0), Is.EqualTo(ExecutionPrompt.None));
        }

        [Test]
        public void WithoutSelfStateThePromptIsHiddenButTheArmedFlagStillTracks()
        {
            var guide = new ExecutionGuidance();
            Assert.That(guide.Update(3, 2f, false, 0), Is.EqualTo(ExecutionPrompt.None));
            Assert.That(guide.Armed, Is.True);
            Assert.That(new SelfStateHolder().Available, Is.False, "S2C_SelfState does not exist yet (pending U2c)");
        }

        [Test]
        public void TheSelfStateHolderIsTheOnlyFeedAndResets()
        {
            var self = new SelfStateHolder();
            self.Feed(7.2f, float.NaN, -1, 1.6f);
            Assert.That(self.Available, Is.True);
            Assert.That(self.SkillCooldown, Is.EqualTo(7.2f));
            Assert.That(self.RescueProgress, Is.EqualTo(0f));
            Assert.That(self.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(self.ExecutionGraceLeft, Is.EqualTo(1.6f));
            self.Reset();
            Assert.That(self.Available, Is.False);
            Assert.That(self.SkillCooldown, Is.EqualTo(0f));
        }

        [Test]
        public void TheKillerHudCopyFollowsTheSpec()
        {
            Assert.That(KillerHudCopy.Time(582), Is.EqualTo("9:42"));
            Assert.That(KillerHudCopy.Time(0), Is.EqualTo("0:00"));
            Assert.That(KillerHudCopy.Time(float.NaN), Is.EqualTo("0:00"));
            Assert.That(KillerHudCopy.Time(599.93f), Is.EqualTo("10:00"));
            Assert.That(KillerHudCopy.TimePressure(60), Is.True);
            Assert.That(KillerHudCopy.TimePressure(61), Is.False);
            Assert.That(KillerHudCopy.Terminals(1, 3), Is.EqualTo("Terminals 1 of 3"));
            Assert.That(KillerHudCopy.OneMoreLine(2, 3), Is.EqualTo("One more opens the Exit."));
            Assert.That(KillerHudCopy.OneMoreLine(1, 3), Is.Null);
            Assert.That(KillerHudCopy.OneMoreLine(3, 3), Is.Null);
            Assert.That(KillerHudCopy.Overclock(2.14f), Is.EqualTo("Overclock " + Dot + " 2.1 s"));
            Assert.That(KillerHudCopy.Recovering(.8f), Is.EqualTo("Recovering " + Dot + " 0.8 s"));
            Assert.That(KillerHudCopy.Slowed(1.6f), Is.EqualTo("Slowed by traffic overload " + Dot + " 1.6 s"));
            Assert.That(KillerHudCopy.Distance(23.6f), Is.EqualTo("24 m"));
            Assert.That(KillerHudCopy.LaggingDistance(24), Is.EqualTo("Lagging " + Dot + " 24 m"));
            Assert.That(KillerHudCopy.Terminal(1), Is.EqualTo("Terminal 2"), "number = Id + 1, no percentage");
            Assert.That(KillerHudCopy.ExitLockedFor(3.2f), Is.EqualTo("Exit Locked 4 s"));
            Assert.That(KillerHudCopy.ExitLockedFor(null), Is.EqualTo("Exit Locked"), "no effect received");
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Grace, 1.64f), Is.EqualTo("Wait 1.6 s, step back, touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.StepBack, 0), Is.EqualTo("Step back 1.2 m, then touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Ready, 0), Is.EqualTo("Touch again to eliminate."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.None, 0), Is.Null);
        }

        [Test]
        public void TheSkillStateLineCoversEveryKillerSkillWithAndWithoutSelfState()
        {
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketAfterimage, false, null), Is.EqualTo("Passive — always on"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, false, null), Is.EqualTo("After the Exit opens"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, true, null), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, false, null), Is.EqualTo("Ready"), "no SelfState: no cooldown");
            Assert.That(KillerHudCopy.SkillState(SkillIds.Overclock, false, 6.2f), Is.EqualTo("Cooldown 7 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.Overclock, false, 0), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillName(SkillIds.PacketAfterimage), Is.EqualTo("Packet afterimage"));
            for (byte id = 0; id < SkillIds.KillerSkillCount; id++)
            {
                Assert.That(KillerHudCopy.SkillName(id), Does.Not.StartWith("Skill "));
                Assert.That(KillerHudCopy.SkillEffect(id), Does.EndWith("."));
                Assert.That(KillerHudCopy.SkillName(id).Length, Is.LessThanOrEqualTo(17), "HUD_SPEC 3: one M line");
            }
        }

        [Test]
        public void TheIntroRulesLineIsTheSpecCopy()
        {
            Assert.That(KillerHudCopy.IntroTitle, Is.EqualTo("You are the Killer"));
            Assert.That(KillerHudCopy.IntroRules.Length, Is.EqualTo(97));
        }

        [Test]
        public void CueRulesFireOnlyOnObservableTransitions()
        {
            var rules = new KillerCueRules();
            rules.Begin(0);
            Assert.That(rules.Pending, Is.EqualTo(new[] { KillerCue.MatchStart }));
            rules.Pending.Clear();
            rules.OnWorld(61, false);
            rules.OnWorld(59.95f, false);
            rules.OnWorld(59.9f, true);
            rules.OnWorld(59.85f, true);
            Assert.That(rules.Pending, Is.EqualTo(new[] { KillerCue.Time60, KillerCue.ExitOpen }));
            rules.Pending.Clear();
            rules.OnSignalCount(1, 1);
            Assert.That(rules.Pending, Is.Empty, "less than 2 s with none since the start");
            rules.OnSignalCount(0, 1.5);
            rules.OnSignalCount(2, 3.6);
            Assert.That(rules.Pending, Is.EqualTo(new[] { KillerCue.BlipFirst }));
            rules.Pending.Clear();
            rules.OnSignalCount(0, 3.7);
            rules.OnSignalCount(1, 4.5);
            Assert.That(rules.Pending, Is.Empty);
            rules.OnTrackerEvent(BlipEventKind.CaptureConfirm);
            rules.OnTrackerEvent(BlipEventKind.Spawned);
            rules.OnTrackerEvent(BlipEventKind.ExecuteConfirm);
            rules.OnOwnEffect();
            rules.OnCooldown(3); rules.OnCooldown(0);
            Assert.That(rules.Pending, Is.EqualTo(new[] { KillerCue.CaptureConfirm, KillerCue.ExecuteConfirm, KillerCue.SkillUse, KillerCue.SkillReady }));
        }

        [Test]
        public void AFirstSnapshotWithTheExitAlreadyOpenPlaysNoCue()
        {
            var rules = new KillerCueRules();
            rules.Begin(0); rules.Pending.Clear();
            rules.OnWorld(30, true);
            Assert.That(rules.Pending, Is.Empty);
        }

        [Test]
        public void TheCueTonesAreShortQuietAndClickFree()
        {
            foreach (KillerCue cue in System.Enum.GetValues(typeof(KillerCue)))
            {
                float[] samples = KillerCueSynth.Build(cue, 44100);
                Assert.That(samples.Length, Is.GreaterThan(100), cue.ToString());
                Assert.That(samples.Length, Is.LessThanOrEqualTo(44100 * .6f + 1), cue + " at most 600 ms");
                float peak = 0;
                foreach (float s in samples) peak = System.Math.Max(peak, System.Math.Abs(s));
                Assert.That(peak, Is.LessThanOrEqualTo(KillerCueSynth.Reference + 1e-4f), cue.ToString());
                Assert.That(System.Math.Abs(samples[0]), Is.LessThan(.01f), cue + " starts at zero");
                Assert.That(System.Math.Abs(samples[samples.Length - 1]), Is.LessThan(.01f), cue + " ends at zero");
            }
            Assert.That(KillerCueSynth.Build(KillerCue.MatchStart, 44100).Length, Is.EqualTo(17640), "400 ms");
            Assert.That(KillerCueSynth.Build(KillerCue.ExitOpen, 44100).Length, Is.EqualTo(26460), "three notes, 600 ms");
        }

        [Test]
        public void TheSmallTextFloorIsTwelvePixelsAt720p()
        {
            Assert.That(ClientScreens.SmallSize(.8f) * .8f, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(ClientScreens.SmallSize(1f), Is.EqualTo(14f));
        }
    }
}
