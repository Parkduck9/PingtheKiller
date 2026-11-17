using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI.RulesV2;
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
            public float CollapseLeft { get; set; } = -1;
            public ActionKind ActionKind { get; set; }
            public float ActionProgress { get; set; }
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
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, 7f), Is.EqualTo("Cooldown 7 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, 6.01f), Is.EqualTo("Cooldown 7 s"), "ceil");
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, .01f), Is.EqualTo("Cooldown 1 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, 0f), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, null), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.Overclock, 11.99f), Is.EqualTo("Cooldown 12 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketTrace, 5f), Is.EqualTo("Cooldown 5 s"), "R1: Packet trace has a cooldown");
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketTrace, null), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, 4f), Is.EqualTo("Cooldown 4 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, 0f), Is.EqualTo("Ready"));
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
            hud.Begin(SkillIds.PacketTrace);
            hud.SetSkill(SkillIds.PacketTrace, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Cooldown"), "R1: Packet trace is active, its ring shows the cooldown");
            hud.Begin(SkillIds.FirewallBlock);
            hud.SetSkill(SkillIds.FirewallBlock, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Cooldown"));
            self.SkillCooldown = 0;
            hud.SetSkill(SkillIds.FirewallBlock, self, -1);
            Assert.That(field.GetValue(ring).ToString(), Is.EqualTo("Ready"));
        }

        // ---- Killer: the prompt slot (GDD v2 has no execution; U6c-wire K1/K2, HUD_SPEC 13.3) ---------------------------
        // These six tests checked the v1 execution guidance (6.4). Each now checks the part of the prompt slot that replaced it.

        private static List<TerminalState> Terminals() => new List<TerminalState>
        {
            new TerminalState { Id = 0, X = 10, Z = 10, Progress = 40 },
            new TerminalState { Id = 1, X = 12, Z = 10, Progress = 100 },
            new TerminalState { Id = 4, X = 30, Z = 30, Progress = 0 }
        };

        [Test]
        public void TheKillerPromptLinesAreTheSpecCopyAndFitTheBlock()
        {
            Assert.That(InteractCopy.Kick(2), Is.EqualTo("Hold [E] to jam Terminal 3"));
            Assert.That(InteractCopy.HatchClose, Is.EqualTo("Hold [E] to close the Emergency port"));
            foreach (string line in new[] { InteractCopy.Kick(7), InteractCopy.HatchClose, InteractCopy.KickHint, InteractCopy.HatchCloseHint })
                Assert.That(line.Length, Is.LessThanOrEqualTo(38), line);
            Assert.That(typeof(KillerHudCopy).GetMethod("PromptLine"), Is.Null, "the execution lines are gone");
        }

        [Test]
        public void TheHoldBlockFloorsThePercentSoItNeverShowsAHundredEarly()
        {
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.KickTerminal, Progress01 = .999f }), Is.EqualTo("Jamming the Terminal " + Dot + " 99%"));
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.CloseHatch, Progress01 = .7f }), Is.EqualTo("Closing the Emergency port " + Dot + " 70%"));
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.None, Progress01 = .5f }), Is.Empty);
        }

        [Test]
        public void TheHatchCloseComesBeforeTheKickWhenBothAreInReach()
        {
            InteractPrompt prompt = InteractCopy.Killer(10.5f, 10, Terminals(), true, 10, 11);
            Assert.That(prompt.Text, Is.EqualTo(InteractCopy.HatchClose));
            Assert.That(prompt.Hint, Is.EqualTo(InteractCopy.HatchCloseHint));
            Assert.That(InteractCopy.Killer(10.5f, 10, Terminals(), false, 10, 11).Text, Is.EqualTo("Hold [E] to jam Terminal 1"), "an unseen hatch is not offered");
        }

        [Test]
        public void ADoneTerminalOrOneOutOfReachGivesNoPrompt()
        {
            Assert.That(InteractCopy.Killer(12, 10.4f, new List<TerminalState> { Terminals()[1] }, false, 0, 0).Text, Is.Null, "done");
            Assert.That(InteractCopy.Killer(30, 31.51f, Terminals(), false, 0, 0).Text, Is.Null, "1.51 m is out of the 1.5 m reach");
            Assert.That(InteractCopy.Killer(30, 31.5f, Terminals(), false, 0, 0).Text, Is.EqualTo("Hold [E] to jam Terminal 5"), "exactly 1.5 m is in reach");
        }

        [Test]
        public void WithoutASelfStateThereIsNoHoldBlock()
        {
            Assert.That(V2Wire.Hold(null).Kind, Is.EqualTo(HoldKind.None));
            var self = new Self { Available = false, ActionKind = ActionKind.Kick, ActionProgress = .5f };
            Assert.That(V2Wire.Hold(self).Kind, Is.EqualTo(HoldKind.None), "not received this match");
            self.Available = true;
            Assert.That(V2Wire.Hold(self).Kind, Is.EqualTo(HoldKind.KickTerminal));
            Assert.That(V2Wire.Hold(self).Progress01, Is.EqualTo(.5f));
        }

        [Test]
        public void TheHoldHintsNameWhatCancelsTheHold()
        {
            Assert.That(HoldCopy.Hint(HoldKind.KickTerminal, "[E]"), Is.EqualTo("Hold [E] " + Dot + " moving cancels the jam"));
            Assert.That(HoldCopy.Hint(HoldKind.CloseHatch, "[E]"), Is.EqualTo("Hold [E] " + Dot + " releasing cancels"));
            Assert.That(HoldCopy.Hint(HoldKind.ExitSwitch, "[E]"), Is.Empty, "the Survivor row has no hint line");
        }
    }
}
