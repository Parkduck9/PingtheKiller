using NUnit.Framework;
using PingTheKiller.Client.UI.RulesV2;
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

        // U6c-wire K1/K2: GDD v2 has no execution. The two ExecutionGuidance tests that stood here now check what replaced the
        // prompt slot: the kick / hatch-close prompt in reach, and the hold block fed by SelfState.ActionKind / ActionProgress.
        [Test]
        public void ThePromptSlotShowsTheKickPromptAndTheHoldBlockReplacesIt()
        {
            var hud = new KillerHud(new UnityEngine.UIElements.VisualElement(), UnityEngine.Resources.Load<UnityEngine.Font>("Fonts/Inter-Regular"));
            hud.Begin(0);
            var terminals = new System.Collections.Generic.List<TerminalState> { new TerminalState { Id = 2, X = 5, Z = 5, Progress = 0 } };
            hud.SetPrompt(InteractCopy.Killer(5.5f, 5, terminals, false, 0, 0), default);
            Assert.That(hud.PromptText, Is.EqualTo("Hold [E] to jam Terminal 3"));
            hud.SetPrompt(InteractCopy.Killer(5.5f, 5, terminals, false, 0, 0), new HoldActionView { Kind = HoldKind.KickTerminal, Progress01 = .55f });
            Assert.That(hud.PromptText, Is.Null, "the hold block takes the prompt slot");
            Assert.That(hud.Hold.Text, Is.EqualTo("Jamming the Terminal " + Dot + " 55%"));
            hud.SetPrompt(InteractCopy.Killer(30, 30, terminals, false, 0, 0), default);
            Assert.That(hud.PromptText, Is.Null, "out of reach, no hold: nothing (a Lagging Survivor adds no prompt)");
        }

        [Test]
        public void BeforeTheFirstSelfStateOfAMatchNoHoldShowsAndTheFirstPacketShowsItAtOnce()
        {
            var self = new SelfStateHolder();
            Assert.That(V2Wire.Hold(self).Kind, Is.EqualTo(HoldKind.None), "nothing received yet");
            self.Feed(0, 0, 0, -1, .5f, (byte)ActionKind.HatchClose, 1);
            HoldActionView hold = V2Wire.Hold(self);
            Assert.That(hold.Kind, Is.EqualTo(HoldKind.CloseHatch));
            Assert.That(HoldCopy.Label(hold), Is.EqualTo("Closing the Emergency port " + Dot + " 50%"));
        }

        [Test]
        public void TheSelfStateHolderIsTheOnlyFeedAndResets()
        {
            var self = new SelfStateHolder();
            self.Feed(7.2f, float.NaN, -1, 1.6f, 0, 0);
            Assert.That(self.Available, Is.True);
            Assert.That(self.SkillCooldown, Is.EqualTo(7.2f));
            Assert.That(self.RescueProgress, Is.EqualTo(0f));
            Assert.That(self.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(self.CollapseLeft, Is.EqualTo(1.6f));
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
            Assert.That(KillerHudCopy.Terminals(1, 3), Is.EqualTo("Terminals 1/3"));
            Assert.That(KillerHudCopy.OneMoreLine(2, 3), Is.EqualTo("One more powers the Exits."));
            Assert.That(KillerHudCopy.OneMoreLine(1, 3), Is.Null);
            Assert.That(KillerHudCopy.OneMoreLine(3, 3), Is.Null);
            Assert.That(KillerHudCopy.Overclock(2.14f), Is.EqualTo("Overclock: 1.5× speed " + Dot + " 2.1 s"));
            Assert.That(KillerHudCopy.Recovering(.8f), Is.EqualTo("Recovering: cannot move " + Dot + " 0.8 s"));
            Assert.That(KillerHudCopy.Slowed(1.6f), Is.EqualTo("Slowed by traffic overload " + Dot + " 1.6 s"));
            Assert.That(KillerHudCopy.Distance(23.6f), Is.EqualTo("24 m"));
            Assert.That(KillerHudCopy.LaggingDistance(24), Is.EqualTo("Lagging " + Dot + " 24 m"));
            Assert.That(KillerHudCopy.Terminal(1), Is.EqualTo("Terminal 2"), "number = Id + 1, no percentage");
            // U6c-wire: the exit tag is per exit (HUD_SPEC 13.4) and the execution prompt lines are gone (GDD v2).
            ExitView locked = ExitView.FromWire(0, 1 | 4, 40);
            locked.LockedLeft = 3.2f;
            Assert.That(ExitCopy.Tag(locked), Is.EqualTo("Exit 1 " + Dot + " Locked 4 s"));
            Assert.That(ExitCopy.Tag(ExitView.FromWire(0, 1 | 4, 40)), Is.EqualTo("Exit 1 " + Dot + " Locked"), "no effect received");
            Assert.That(typeof(KillerHudCopy).GetMethod("PromptLine"), Is.Null);
        }

        [Test]
        public void TheSkillStateLineCoversEveryKillerSkillWithAndWithoutSelfState()
        {
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketAfterimage, false, null), Is.EqualTo("Passive — always on"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, false, null), Is.EqualTo("Needs an Exit switch in progress"));
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
            rules.OnSkillReady();
            Assert.That(rules.Pending, Is.EqualTo(new[] { KillerCue.CaptureConfirm, KillerCue.ExecuteConfirm, KillerCue.SkillUse, KillerCue.SkillReady, KillerCue.SkillReady }));
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
