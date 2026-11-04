using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // U4d: the Survivor readouts fed by S2C_SelfState (HUD_SPEC 4.1 row 3 and the Protected tag, 4.4 rescuer row, 4.8 skill
    // ring), plus the rest of the 4.1 precedence and the 4.3 prompt row they share the cluster with.
    public sealed class SurvivorHudTests
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

        private static OwnEntry Own(PlayerStatus status = PlayerStatus.Normal, float x = 10, float z = 10)
            => new OwnEntry { Status = status, X = x, Z = z };

        [Test]
        public void ALaggingSurvivorBeingRescuedSeesRowThreeWithTheAllyFill()
        {
            var own = Own(PlayerStatus.Lagging); own.StatusTimer = 41.2f; own.Signal = true;
            MeterState m = SurvivorReadout.Meter(own, new Self { RescueProgress = .35f }, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Lagging"));
            Assert.That(m.Reason, Is.EqualTo("Rescue in progress " + Dash + " stay close"));
            Assert.That(m.Fill, Is.EqualTo(BarFill.Ally));
            Assert.That(m.Width, Is.EqualTo(.35f));
            // Row 4 when no rescue runs, or before the first SelfState.
            m = SurvivorReadout.Meter(own, new Self { RescueProgress = 0 }, 0, 0);
            Assert.That(m.Reason, Is.EqualTo("Always visible " + Dot + " 42 s left"));
            Assert.That(m.Fill, Is.EqualTo(BarFill.Danger));
            Assert.That(m.Width, Is.EqualTo(41.2f / 60).Within(1e-5f));
            m = SurvivorReadout.Meter(own, new Self { Available = false, RescueProgress = .9f }, 0, 0);
            Assert.That(m.Fill, Is.EqualTo(BarFill.Danger), "unavailable SelfState: no rescue row");
        }

        [Test]
        public void TheCaptureTagShowsProtectedWhileTheImmunityRunsThenCapturedOnce()
        {
            var own = Own(); own.CaptureCount = 1;
            MeterState m = SurvivorReadout.Meter(own, new Self { InvulnerableLeft = 1.44f }, 0, 0);
            Assert.That(m.Tag, Is.EqualTo("Protected 1.4 s"));
            Assert.That(m.TagMuted, Is.False);
            m = SurvivorReadout.Meter(own, new Self(), 0, 0);
            Assert.That(m.Tag, Is.EqualTo("Captured once"));
            Assert.That(m.TagMuted, Is.True);
            Assert.That(SurvivorReadout.Meter(Own(), new Self(), 0, 0).Tag, Is.Null);
            Assert.That(SurvivorReadout.Meter(Own(), null, 0, 0).Tag, Is.Null);
            Assert.That("Protected 1.4 s".Length, Is.LessThanOrEqualTo(15), "HUD_SPEC 4.1 tag budget");
        }

        [Test]
        public void TheMeterPrecedenceFollowsTheSpecTable()
        {
            var own = Own(); own.Signal = true; own.Hacking = true; own.Rescuing = true;
            Assert.That(SurvivorReadout.Meter(own, null, 1.2f, 1.4f).Reason, Is.EqualTo("Scanned by the Killer " + Dot + " 1.2 s"));
            Assert.That(SurvivorReadout.Meter(own, null, 0, 1.4f).Reason, Is.EqualTo("Traffic overload " + Dot + " 1.4 s"));
            Assert.That(SurvivorReadout.Meter(own, null, 0, 0).Reason, Is.EqualTo("Visible while you hack."));
            own.Hacking = false;
            Assert.That(SurvivorReadout.Meter(own, null, 0, 0).Reason, Is.EqualTo("Visible while you rescue."));
            own.Rescuing = false;
            MeterState m = SurvivorReadout.Meter(own, null, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Signal")); Assert.That(m.WordSignal, Is.True);
            Assert.That(m.Reason, Is.EqualTo("Visible while you move.")); Assert.That(m.Fill, Is.EqualTo(BarFill.Signal));
            own.Signal = false; own.Stealthed = true;
            m = SurvivorReadout.Meter(own, null, 0, 0);
            Assert.That(m.Word, Is.EqualTo("Silent")); Assert.That(m.AllyOutline, Is.True); Assert.That(m.Fill, Is.EqualTo(BarFill.Empty));
            own.Stealthed = false;
            m = SurvivorReadout.Meter(own, null, 0, 0);
            Assert.That(m.Reason, Is.EqualTo("Not visible to the Killer.")); Assert.That(m.ReasonMuted, Is.True);
            Assert.That(SurvivorReadout.Meter(Own(PlayerStatus.Escaped), null, 0, 0).Word, Is.EqualTo("Escaped"));
            Assert.That(SurvivorReadout.Meter(Own(PlayerStatus.Eliminated), null, 0, 0).Word, Is.EqualTo("Eliminated"));
            foreach (string reason in new[] { "Rescue in progress " + Dash + " stay close", "Always visible " + Dot + " 60 s left", "Scanned by the Killer " + Dot + " 1.5 s" })
                Assert.That(reason.Length, Is.LessThanOrEqualTo(34), "HUD_SPEC 4.1 row B budget: " + reason);
        }

        [Test]
        public void TheSkillRingFollowsTheServerCooldown()
        {
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Normal, 7.2f, false), Is.EqualTo(SkillRing.State.Cooldown));
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Normal, 0f, false), Is.EqualTo(SkillRing.State.Ready));
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Normal, null, false), Is.EqualTo(SkillRing.State.Ready), "no SelfState yet: no cooldown ring");
            Assert.That(SurvivorReadout.SkillState(SkillIds.DummyPacket, PlayerStatus.Lagging, 0f, false), Is.EqualTo(SkillRing.State.Disabled));
            Assert.That(SurvivorReadout.SkillState(SkillIds.EmergencyRestore, PlayerStatus.Normal, 0f, false), Is.EqualTo(SkillRing.State.Disabled));
            Assert.That(SurvivorReadout.SkillState(SkillIds.EmergencyRestore, PlayerStatus.Normal, 0f, true), Is.EqualTo(SkillRing.State.Ready));
            Assert.That(SurvivorReadout.Ceil(6.01f), Is.EqualTo(7));
            Assert.That(SurvivorReadout.Ceil(15f), Is.EqualTo(15));
            Assert.That(SurvivorReadout.Ceil(float.NaN), Is.EqualTo(0));
            Assert.That(SurvivorReadout.SkillName(SkillIds.TrafficOverload), Is.EqualTo("Traffic overload"));
            Assert.That(SurvivorReadout.SkillName(SkillIds.EmergencyRestore), Is.EqualTo("Emergency restore"));
        }

        private static List<Mate> Mates(params Mate[] mates) => new List<Mate>(mates);
        private static string Names(int id) => id == 3 ? "Mina" : id == 4 ? "AVeryLongNicknameForTesting" : null;

        [Test]
        public void TheRescuerRowNamesTheAllyAndFillsWithTheServerProgress()
        {
            var own = Own(); own.Rescuing = true; own.Signal = true;
            var mates = Mates(new Mate { Id = 3, X = 11, Z = 10, Status = PlayerStatus.Lagging });
            PromptState p = SurvivorReadout.Prompt(own, mates, new TerminalState[0], new Self { RescueProgress = .68f }, Names, false, SkillIds.DummyPacket, true);
            Assert.That(p.Text, Is.EqualTo("Rescuing Mina " + Dash + " you are Signal"));
            Assert.That(p.Bar, Is.EqualTo(BarFill.Ally));
            Assert.That(p.Width, Is.EqualTo(.68f));
            p = SurvivorReadout.Prompt(own, mates, null, new Self { Available = false, RescueProgress = .68f }, Names, false, 0, true);
            Assert.That(p.Width, Is.EqualTo(0f), "no estimate before the first SelfState");
            // Long names are ellipsized at 12 characters.
            mates = Mates(new Mate { Id = 4, X = 11, Z = 10, Status = PlayerStatus.Lagging });
            p = SurvivorReadout.Prompt(own, mates, null, new Self(), Names, false, 0, true);
            Assert.That(p.Text, Is.EqualTo("Rescuing AVeryLongNi… " + Dash + " you are Signal"));
        }

        [Test]
        public void TheOtherPromptRowsFollowTheirPrecedence()
        {
            var lagging = Mates(new Mate { Id = 3, X = 14, Z = 10, Status = PlayerStatus.Lagging });
            PromptState p = SurvivorReadout.Prompt(Own(), lagging, null, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Mina is Lagging " + Dot + " 4 m " + Dash + " stay within 1.5 m to rescue them"));
            Assert.That(SurvivorReadout.Prompt(Own(), lagging, null, new Self(), Names, true, 0, false).Text, Is.EqualTo(SurvivorReadout.CursorHint));

            var terminals = new[] { new TerminalState { Id = 1, X = 10.5f, Z = 10, Progress = 47 } };
            var own = Own(); own.Hacking = true;
            var cohack = Mates(new Mate { Id = 5, X = 11, Z = 10.5f, Hacking = true, Status = PlayerStatus.Normal });
            p = SurvivorReadout.Prompt(own, Mates(), terminals, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Hacking Terminal 2 " + Dot + " 47%"));
            Assert.That(p.Bar, Is.EqualTo(BarFill.Text)); Assert.That(p.Width, Is.EqualTo(.47f).Within(1e-5f));
            Assert.That(SurvivorReadout.Prompt(own, cohack, terminals, new Self(), Names, false, 0, false).Text, Is.EqualTo("Hacking Terminal 2 " + Dot + " 47% " + Dot + " 2 hacking"));
            p = SurvivorReadout.Prompt(Own(), Mates(), terminals, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Hold [E] to hack Terminal 2 " + Dot + " 47% " + Dash + " you will be Signal"));
            p = SurvivorReadout.Prompt(Own(PlayerStatus.Lagging), Mates(), terminals, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Lagging Survivors cannot hack.")); Assert.That(p.Muted, Is.True);
            p = SurvivorReadout.Prompt(Own(PlayerStatus.Lagging, 40, 40), Mates(), terminals, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Skills are off while Lagging."));
            p = SurvivorReadout.Prompt(Own(), Mates(), null, new Self(), Names, false, SkillIds.EmergencyRestore, false);
            Assert.That(p.Text, Is.EqualTo("Emergency restore needs a Lagging Survivor next to you."));
            Assert.That(SurvivorReadout.Prompt(Own(), Mates(), null, new Self { SkillCooldown = 3 }, Names, false, SkillIds.EmergencyRestore, false).Text, Is.Null,
                "the reason belongs to Ready only");
            Assert.That(SurvivorReadout.Prompt(Own(), Mates(), null, new Self(), Names, false, SkillIds.DummyPacket, false).Text, Is.Null);
            Assert.That(SurvivorReadout.Prompt(Own(PlayerStatus.Escaped), lagging, null, new Self(), Names, false, 0, false).Text, Is.Null);
        }

        [Test]
        public void SurvivorEffectsSplitOwnAndKillerEffectsByCaster()
        {
            var e = new SurvivorEffects();
            e.Accept(7, SkillIds.TrafficOverload, 2f, 10, 7, true);
            Assert.That(e.OwnActive(11), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(e.Overload(11), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(e.OwnActive(12.5), Is.EqualTo(-1f));
            e.Accept(3, SkillIds.DummyPacket, 3f, 20, 7, true); // an ally's Dummy packet shares the Killer's PingScanner id 0
            Assert.That(e.Scanned(20.5), Is.EqualTo(0f));
            e.Accept(1, SkillIds.PingScanner, 1.5f, 30, 7, false);
            Assert.That(e.Scanned(30.5), Is.EqualTo(1f).Within(1e-5f));
            e.Accept(1, SkillIds.FirewallBlock, 5f, 40, 7, false);
            Assert.That(e.Locked(41).Value, Is.EqualTo(4f).Within(1e-5f));
            // HUD_SPEC 13.4 (U6c-wire S3): the Killer's Firewall block seconds reach the Locked exit cell; the v1 line 3 is gone.
            var world = new S2C_WorldState();
            world.Exits.Add(new ExitState { Id = 0, Flags = 1 | 4, SwitchProgress = 40 });
            world.Exits.Add(new ExitState { Id = 1, Flags = 1 | 2, SwitchProgress = 100 });
            Assert.That(ExitCopy.State(V2Wire.Exits(world, e.Locked(41) ?? -1)[0]), Is.EqualTo("Locked 4 s"));
            Assert.That(ExitCopy.State(V2Wire.Exits(world, e.Locked(50) ?? -1)[0]), Is.EqualTo("Locked"));
            Assert.That(ExitCopy.State(V2Wire.Exits(world)[1]), Is.EqualTo("Open"));
            e.Reset();
            Assert.That(e.Locked(41).HasValue, Is.False);
        }

        [Test]
        public void TheSurvivorClusterShowsTheReadouts()
        {
            var parent = new VisualElement();
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            var hud = new SurvivorHud(parent, font);
            hud.Begin(SkillIds.WhiteNoise);
            Assert.That(parent.Query<Label>().Where(l => l.text == "White noise").ToList().Count, Is.EqualTo(1));
            var own = Own(PlayerStatus.Lagging); own.StatusTimer = 30;
            hud.SetMeter(SurvivorReadout.Meter(own, new Self { RescueProgress = .5f }, 0, 0));
            Assert.That(hud.Word, Is.EqualTo("Lagging"));
            Assert.That(hud.Reason, Is.EqualTo("Rescue in progress " + Dash + " stay close"));
            Assert.That(hud.MeterFillHas("fill-ally"), Is.True);
            Assert.That(hud.MeterFillPercent, Is.EqualTo(50f).Within(1e-3f));
            var normal = Own(); normal.CaptureCount = 1;
            hud.SetMeter(SurvivorReadout.Meter(normal, new Self { InvulnerableLeft = 1.4f }, 0, 0));
            Assert.That(hud.Tag, Is.EqualTo("Protected 1.4 s"));
            Assert.That(hud.MeterFillHas("fill-ally"), Is.False);

            hud.SetSkill(SkillIds.WhiteNoise, SkillRing.State.Cooldown, 7.2f, -1, false);
            Assert.That(hud.SkillCount, Is.EqualTo("8"), "ceil(SkillCooldown) in the ring centre");
            hud.SetSkill(SkillIds.WhiteNoise, SkillRing.State.Ready, 0f, -1, false);
            Assert.That(hud.SkillCount, Is.Null, "the icon again");

            Assert.That(hud.PromptText, Is.Null);
            hud.SetPrompt(new PromptState { Text = "Rescuing Mina " + Dash + " you are Signal", Bar = BarFill.Ally, Width = .25f });
            Assert.That(hud.PromptText, Is.EqualTo("Rescuing Mina " + Dash + " you are Signal"));
            Assert.That(hud.PromptFillHas("fill-ally"), Is.True);
            Assert.That(hud.PromptFillPercent, Is.EqualTo(25f).Within(1e-3f));
            Rect withPrompt = hud.ClusterRect(new Vector2(1600, 900));
            Assert.That(withPrompt.width, Is.EqualTo(672f));
            Assert.That(withPrompt.height, Is.EqualTo(72f + 8 + 40));
            Assert.That(withPrompt.yMax, Is.EqualTo(900f - 24));
            hud.SetPrompt(default);
            Assert.That(hud.PromptText, Is.Null);
            Assert.That(hud.ClusterRect(new Vector2(1600, 900)).height, Is.EqualTo(72f));
        }
    }
}
