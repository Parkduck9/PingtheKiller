using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.QA.Tests
{
    // U4d QA attempt 1, test item 2. Written from HUD_SPEC 4.1 (rows 3-4, Protected), 4.4, 4.8, 6.1d and 6.4, not from the code.
    [Category("U4dQaA1")]
    public sealed class SelfStateReadoutQaTests
    {
        private const string Dot = "·", Dash = "—";

        private sealed class Self : ISelfState
        {
            public bool Available { get; set; } = true;
            public float SkillCooldown { get; set; }
            public float RescueProgress { get; set; }
            public float InvulnerableLeft { get; set; }
            public float ExecutionGraceLeft { get; set; }
        }

        private static OwnEntry Own(PlayerStatus status = PlayerStatus.Normal) => new OwnEntry { Status = status, X = 10, Z = 10 };

        private static Font HudFont()
        {
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            Assert.That(font, Is.Not.Null, "Resources/Fonts/Inter-Regular is the HUD font");
            return font;
        }

        // ---- Survivor: transmit meter (4.1) --------------------------------------------------------------------------------

        [Test]
        public void ProtectedShowsOneDecimalOnlyWhileInvulnerableLeftIsAbove0AndBeatsCapturedOnce()
        {
            OwnEntry own = Own(); own.Signal = true; own.CaptureCount = 1;
            MeterState m = SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 1.4f }, 0, 0);
            Assert.That(m.Tag, Is.EqualTo("Protected 1.4 s"));
            Assert.That(m.TagMuted, Is.False, "Protected is `text`, Captured once is `muted`");
            m = SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 2f }, 0, 0);
            Assert.That(m.Tag, Is.EqualTo("Protected 2.0 s"), "one decimal, always");
            m = SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 0f }, 0, 0);
            Assert.That(m.Tag, Is.EqualTo("Captured once"), "tag falls back at exactly 0");
            Assert.That(m.TagMuted, Is.True);
            own.CaptureCount = 0;
            m = SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 0f }, 0, 0);
            Assert.That(m.Tag, Is.Null.Or.Empty, "no tag at all");
            m = SurvivorReadout.Meter(own, new Self { Available = false, InvulnerableLeft = 1.4f }, 0, 0);
            Assert.That(m.Tag, Is.Null.Or.Empty, "a stale value before the first packet of a match is never shown");
        }

        [Test]
        public void RowThreeNeedsLaggingPlusRescueProgressAndRowFourTakesOverAtZero()
        {
            OwnEntry own = Own(PlayerStatus.Lagging); own.StatusTimer = 41.2f; own.Signal = true;
            MeterState m = SurvivorReadout.Meter(own, new Self { RescueProgress = .6f }, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Lagging"));
            Assert.That(m.Reason, Is.EqualTo("Rescue in progress " + Dash + " stay close"));
            Assert.That(m.Fill, Is.EqualTo(BarFill.Ally));
            Assert.That(m.Width, Is.EqualTo(.6f).Within(1e-4f));
            m = SurvivorReadout.Meter(own, new Self { RescueProgress = 0f }, 0, 0);
            Assert.That(m.Reason, Is.EqualTo("Always visible " + Dot + " 42 s left"), "ceil(StatusTimer)");
            Assert.That(m.Fill, Is.EqualTo(BarFill.Danger));
            Assert.That(m.Width, Is.EqualTo(41.2f / 60f).Within(1e-4f));
            m = SurvivorReadout.Meter(own, new Self { Available = false, RescueProgress = .6f }, 0, 0);
            Assert.That(m.Fill, Is.EqualTo(BarFill.Danger), "no SelfState yet: row 4");
            m = SurvivorReadout.Meter(own, null, 0, 0);
            Assert.That(m.Fill, Is.EqualTo(BarFill.Danger), "a null seam: row 4");
        }

        [Test]
        public void ARescuerWhoIsNormalNeverGetsRowThreeAndEscapedAndEliminatedKeepTheirRows()
        {
            OwnEntry rescuer = Own(); rescuer.Signal = true; rescuer.Rescuing = true;
            MeterState m = SurvivorReadout.Meter(rescuer, new Self { RescueProgress = .5f }, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Signal"));
            Assert.That(m.Reason, Is.EqualTo("Visible while you rescue."), "row 8, not the Lagging row");
            Assert.That(m.Fill, Is.EqualTo(BarFill.Signal));
            m = SurvivorReadout.Meter(Own(PlayerStatus.Escaped), new Self { RescueProgress = .5f }, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Escaped"));
            Assert.That(m.Reason, Is.EqualTo("Waiting for the result."));
            Assert.That(m.Fill, Is.EqualTo(BarFill.Empty));
            m = SurvivorReadout.Meter(Own(PlayerStatus.Eliminated), new Self { RescueProgress = .5f }, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Eliminated"), "row 1 outranks row 3");
        }

        [Test]
        public void RescueProgressOutsideZeroToOneIsClampedInTheBarWidths()
        {
            OwnEntry lagging = Own(PlayerStatus.Lagging);
            Assert.That(SurvivorReadout.Meter(lagging, new Self { RescueProgress = 3f }, 0, 0).Width, Is.EqualTo(1f));
            Assert.That(SurvivorReadout.Meter(lagging, new Self { RescueProgress = float.NaN }, 0, 0).Fill, Is.Not.EqualTo(BarFill.Ally), "NaN is not progress");
            OwnEntry rescuer = Own(); rescuer.Rescuing = true;
            var mates = new List<Mate> { new Mate { Id = 7, X = 10.5f, Z = 10, Status = PlayerStatus.Lagging } };
            PromptState p = SurvivorReadout.Prompt(rescuer, mates, new List<TerminalState>(), new Self { RescueProgress = 5f }, id => "Mina", false, SkillIds.DummyPacket, false);
            Assert.That(p.Width, Is.LessThanOrEqualTo(1f).And.GreaterThanOrEqualTo(0f));
        }

        // ---- Survivor: rescuer row (4.4) ------------------------------------------------------------------------------------

        [Test]
        public void TheRescuerRowShowsTheNameTheAllyBarAndTheServersProgress()
        {
            OwnEntry own = Own(); own.Rescuing = true; own.Signal = true;
            var mates = new List<Mate> { new Mate { Id = 7, X = 10.5f, Z = 10, Status = PlayerStatus.Lagging } };
            System.Func<int, string> names = id => id == 7 ? "Mina" : null;
            PromptState p = SurvivorReadout.Prompt(own, mates, new List<TerminalState>(), new Self { RescueProgress = .45f }, names, false, SkillIds.EmergencyRestore, true);
            Assert.That(p.Text, Is.EqualTo("Rescuing Mina " + Dash + " you are Signal"));
            Assert.That(p.Bar, Is.EqualTo(BarFill.Ally));
            Assert.That(p.Width, Is.EqualTo(.45f).Within(1e-4f));
            Assert.That(p.Muted, Is.False);
            p = SurvivorReadout.Prompt(own, mates, new List<TerminalState>(), new Self { Available = false, RescueProgress = .45f }, names, false, SkillIds.EmergencyRestore, true);
            Assert.That(p.Width, Is.EqualTo(0f), "no progress is invented before the first SelfState");
            p = SurvivorReadout.Prompt(own, mates, new List<TerminalState>(), new Self { RescueProgress = .45f }, names, true, SkillIds.EmergencyRestore, true);
            Assert.That(p.Text, Is.EqualTo("Click to resume mouse look"), "a released cursor outranks the rescuer row");
        }

        // ---- Survivor: skill ring and cooldown count (4.8) ------------------------------------------------------------------

        [Test]
        public void TheSurvivorRingStateFollowsTheServersCooldownStatusAndPrecondition()
        {
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Normal, 3.2f, true), Is.EqualTo(SkillRing.State.Cooldown));
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Normal, 0f, true), Is.EqualTo(SkillRing.State.Ready));
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Normal, null, true), Is.EqualTo(SkillRing.State.Ready), "before the first packet: no cooldown ring");
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Lagging, 0f, true), Is.EqualTo(SkillRing.State.Disabled));
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Lagging, 5f, true), Is.EqualTo(SkillRing.State.Disabled), "Lagging beats Cooldown");
            Assert.That(SurvivorReadout.SkillState(SkillIds.EmergencyRestore, PlayerStatus.Normal, 0f, false), Is.EqualTo(SkillRing.State.Disabled), "no Lagging ally in reach");
            Assert.That(SurvivorReadout.SkillState(SkillIds.EmergencyRestore, PlayerStatus.Normal, 0f, true), Is.EqualTo(SkillRing.State.Ready));
        }

        [Test]
        public void TheSurvivorRingCentreIsCeilOfTheCooldownOnlyWhileCoolingDown()
        {
            var parent = new VisualElement();
            var hud = new SurvivorHud(parent, HudFont());
            hud.Begin(SkillIds.TrafficOverload);
            hud.SetSkill(SkillIds.TrafficOverload, SkillRing.State.Cooldown, 14.01f, -1, true);
            Assert.That(hud.SkillCount, Is.EqualTo("15"), "ceil, never round or floor");
            hud.SetSkill(SkillIds.TrafficOverload, SkillRing.State.Cooldown, 14.0f, -1, true);
            Assert.That(hud.SkillCount, Is.EqualTo("14"));
            hud.SetSkill(SkillIds.TrafficOverload, SkillRing.State.Cooldown, .02f, -1, true);
            Assert.That(hud.SkillCount, Is.EqualTo("1"), "the last fraction of a second still reads 1, never 0");
            hud.SetSkill(SkillIds.TrafficOverload, SkillRing.State.Ready, 0f, -1, true);
            Assert.That(hud.SkillCount, Is.Null, "Ready draws the icon, not a number");
            hud.SetSkill(SkillIds.TrafficOverload, SkillRing.State.Disabled, 9f, -1, true);
            Assert.That(hud.SkillCount, Is.Null, "Disabled draws the icon, not a number");
        }

        [Test]
        public void TheSurvivorHudDisplaysTheMeterAndTheProtectedTagExactly()
        {
            var parent = new VisualElement();
            var hud = new SurvivorHud(parent, HudFont());
            OwnEntry own = Own(); own.Signal = true;
            hud.SetMeter(SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 1.4f }, 0, 0));
            Assert.That(hud.Tag, Is.EqualTo("Protected 1.4 s"));
            hud.SetMeter(SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 0f }, 0, 0));
            Assert.That(hud.Tag, Is.Null, "the tag label is hidden again");
            OwnEntry lagging = Own(PlayerStatus.Lagging);
            hud.SetMeter(SurvivorReadout.Meter(lagging, new Self { RescueProgress = .25f }, 0, 0));
            Assert.That(hud.Reason, Is.EqualTo("Rescue in progress " + Dash + " stay close"));
            Assert.That(hud.MeterFillHas("fill-ally"), Is.True);
            Assert.That(hud.MeterFillPercent, Is.EqualTo(25f).Within(.01f));
        }

        [Test]
        public void SurvivorSkillNamesWrapToAtMostTwoLinesOfNineCharacters()
        {
            // HUD_SPEC 4.8: the name block is 80 u wide, S 600, at most 2 lines (about 9 characters per line).
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++)
            {
                string[] words = SurvivorReadout.SkillName(id).Split(' ');
                foreach (string word in words) Assert.That(word.Length, Is.LessThanOrEqualTo(9), "id " + id + " word " + word);
                Assert.That(words.Length, Is.LessThanOrEqualTo(2), "id " + id + " must wrap to at most 2 lines");
            }
        }

        // ---- Killer: skill block (6.1d) ------------------------------------------------------------------------------------

        [Test]
        public void TheKillerStateLineIsCooldownCeilOrReadyAndPassiveAndFirewallOverrideIt()
        {
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, true, 7f), Is.EqualTo("Cooldown 7 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, true, 6.01f), Is.EqualTo("Cooldown 7 s"), "ceil");
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, true, .01f), Is.EqualTo("Cooldown 1 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, true, 0f), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, true, null), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.Overclock, true, 11.99f), Is.EqualTo("Cooldown 12 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketAfterimage, true, 5f), Is.EqualTo("Passive " + Dash + " always on"), "the passive skill has no cooldown");
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketAfterimage, false, null), Is.EqualTo("Passive " + Dash + " always on"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, false, 4f), Is.EqualTo("After the Exit opens"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, false, null), Is.EqualTo("After the Exit opens"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, true, 4f), Is.EqualTo("Cooldown 4 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, true, 0f), Is.EqualTo("Ready"));
        }

        [Test]
        public void TheKillerHudRingHasNoCooldownArcForThePassiveSkillAndTheSelfStateClockDrivesTheOthers()
        {
            var parent = new VisualElement();
            var hud = new KillerHud(parent, HudFont());
            SkillRing ring = parent.Q<SkillRing>();
            Assert.That(ring, Is.Not.Null);
            var field = typeof(SkillRing).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var self = new Self { SkillCooldown = 5f };
            hud.Begin(SkillIds.PacketAfterimage);
            hud.SetSkill(SkillIds.PacketAfterimage, true, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Passive"), "no ring for the passive skill, even with a cooldown on the wire");
            hud.Begin(SkillIds.FirewallBlock);
            hud.SetSkill(SkillIds.FirewallBlock, false, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Disabled"), "FirewallBlock before the Exit opens");
            hud.SetSkill(SkillIds.FirewallBlock, true, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Cooldown"));
            self.SkillCooldown = 0;
            hud.SetSkill(SkillIds.FirewallBlock, true, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Ready"));
        }

        // ---- Killer: execution guidance (6.4) -------------------------------------------------------------------------------

        [Test]
        public void TheThreePromptLinesAreTheSpecCopyAndLineTwoFitsThirtyFiveCharacters()
        {
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Grace, 1.6f), Is.EqualTo("Wait 1.6 s, step back, touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Grace, 2f), Is.EqualTo("Wait 2.0 s, step back, touch again."), "one decimal");
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Grace, .04f), Is.EqualTo("Wait 0.0 s, step back, touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.StepBack, 0f), Is.EqualTo("Step back 1.2 m, then touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Ready, 0f), Is.EqualTo("Touch again to eliminate."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.None, 0f), Is.Null);
            foreach (ExecutionPrompt prompt in new[] { ExecutionPrompt.Grace, ExecutionPrompt.StepBack, ExecutionPrompt.Ready })
                Assert.That(KillerHudCopy.PromptLine(prompt, 2f).Length, Is.LessThanOrEqualTo(35), prompt.ToString());
        }

        [Test]
        public void GraceThenStepBackThenReadyFollowsTheRearmRuleAt1Point2Metres()
        {
            var g = new ExecutionGuidance();
            // Capture: touching at 0.9 m with 2 s of grace.
            Assert.That(g.Update(4, .9f, true, 2f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Armed, Is.False);
            // Still touching, grace running down.
            Assert.That(g.Update(4, .9f, true, .8f), Is.EqualTo(ExecutionPrompt.Grace));
            // Grace over, never stepped out: Step back.
            Assert.That(g.Update(4, .9f, true, 0f), Is.EqualTo(ExecutionPrompt.StepBack));
            Assert.That(g.Armed, Is.False);
            // Exactly 1.2 m is still contact ("more than 1.2 m" arms).
            Assert.That(g.Update(4, 1.2f, true, 0f), Is.EqualTo(ExecutionPrompt.StepBack));
            Assert.That(g.Armed, Is.False);
            // Beyond 1.2 m: armed, and the touch after the grace eliminates.
            Assert.That(g.Update(4, 1.21f, true, 0f), Is.EqualTo(ExecutionPrompt.Ready));
            Assert.That(g.Armed, Is.True);
            // Coming back to touch after the grace keeps it armed.
            Assert.That(g.Update(4, .5f, true, 0f), Is.EqualTo(ExecutionPrompt.Ready));
        }

        [Test]
        public void ATouchInsideTheGraceIsSpentAndASteppedOutKillerStaysArmedForTheWholeGrace()
        {
            var g = new ExecutionGuidance();
            g.Update(9, 2.0f, true, 1.5f);              // target appears 2 m away during the grace
            Assert.That(g.Armed, Is.True);
            Assert.That(g.Update(9, 2.0f, true, 1.0f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Update(9, 1.0f, true, .5f), Is.EqualTo(ExecutionPrompt.Grace), "in-grace touch: disarmed (spent)");
            Assert.That(g.Armed, Is.False);
            Assert.That(g.Update(9, 1.0f, true, 0f), Is.EqualTo(ExecutionPrompt.StepBack));
            Assert.That(g.Update(9, 2.5f, true, 0f), Is.EqualTo(ExecutionPrompt.Ready));
        }

        [Test]
        public void ANewTargetStartsDisarmedAndLosingTheTargetOrBeyondThreeMetresHidesThePrompt()
        {
            var g = new ExecutionGuidance();
            g.Update(1, 2.0f, true, 0f);
            Assert.That(g.Armed, Is.True);
            Assert.That(g.Update(2, 1.0f, true, 0f), Is.EqualTo(ExecutionPrompt.StepBack), "a different instance became the target: safe default");
            Assert.That(g.Armed, Is.False);
            Assert.That(g.Update(2, 3.01f, true, 0f), Is.EqualTo(ExecutionPrompt.None), "beyond 3 m there is no target");
            Assert.That(g.Target, Is.EqualTo(-1));
            Assert.That(g.Update(-1, 0f, true, 0f), Is.EqualTo(ExecutionPrompt.None));
            // Exactly 3 m is a target again (new instance: disarmed), but 3 m is more than 1.2 m, so the same snapshot arms it.
            Assert.That(g.Update(2, 3.0f, true, 0f), Is.EqualTo(ExecutionPrompt.Ready));
            Assert.That(g.Target, Is.EqualTo(2));
        }

        [Test]
        public void WithoutASelfStateThereIsNoPromptEvenWithATarget()
        {
            var g = new ExecutionGuidance();
            Assert.That(g.Update(3, .9f, false, 0f), Is.EqualTo(ExecutionPrompt.None));
            Assert.That(g.Display(2f), Is.EqualTo(ExecutionPrompt.None));
            Assert.That(g.Update(3, .9f, true, 1.2f), Is.EqualTo(ExecutionPrompt.Grace), "the prompt appears with the first SelfState");
        }

        [Test]
        public void TheLocalGraceCountdownTurnsGraceIntoStepBackOrReadyBetweenSnapshots()
        {
            var g = new ExecutionGuidance();
            g.Update(5, .9f, true, 1.2f);
            Assert.That(g.Display(1.2f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Display(.05f), Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(g.Display(0f), Is.EqualTo(ExecutionPrompt.StepBack));
            Assert.That(g.Display(-1f), Is.EqualTo(ExecutionPrompt.StepBack), "negative reads as 0");
            Assert.That(g.Display(float.NaN), Is.EqualTo(ExecutionPrompt.StepBack), "NaN reads as 0");
            g.Update(5, 1.5f, true, .3f);
            Assert.That(g.Display(0f), Is.EqualTo(ExecutionPrompt.Ready));
        }
    }
}
