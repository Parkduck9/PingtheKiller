using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // 11/17 R3: explicit rescue clarity (helper prompt, downed line, Lagging-ally markers) and onboarding (intro card,
    // How to play, first-time hints), with the SLOP copy rules over every string they add.
    public sealed class HelpTests
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

        private static OwnEntry Own(PlayerStatus status = PlayerStatus.Normal, float x = 10, float z = 10, float timer = 0)
            => new OwnEntry { Status = status, X = x, Z = z, StatusTimer = timer };
        private static List<Mate> Mates(params Mate[] mates) => new List<Mate>(mates);
        private static string Names(int id) => id == 3 ? "Mina" : id == 4 ? "AVeryLongNicknameForTesting" : null;

        // ---- S2.1 helper prompt ---------------------------------------------------------------------------------------

        [Test]
        public void WithinTheRescueRadiusThePromptNamesTheEHold()
        {
            var mates = Mates(new Mate { Id = 3, X = 11, Z = 10, Status = PlayerStatus.Lagging });
            PromptState p = SurvivorReadout.Prompt(Own(), mates, new TerminalState[0], new Self(), Names, false, SkillIds.DummyPacket, true);
            Assert.That(p.Text, Is.EqualTo("Hold [E] to rescue Mina " + Dash + " you will be Signal"));
            Assert.That(p.Bar, Is.EqualTo(BarFill.Empty), "the bar belongs to the held rescue, not the prompt");
            mates = Mates(new Mate { Id = 3, X = 11.5f, Z = 10, Status = PlayerStatus.Lagging }); // exactly 1.5 m
            Assert.That(SurvivorReadout.Prompt(Own(), mates, null, new Self(), Names, false, 0, true).Text, Does.StartWith("Hold [E] to rescue Mina"));
        }

        // ---- S5 getting up ----------------------------------------------------------------------------------------------

        [Test]
        public void TheGettingUpFlagShowsTheGettingUpLineAheadOfEveryPromptAndTheGettingUpPose()
        {
            var own = Own(); own.GettingUp = true;
            var mates = Mates(new Mate { Id = 3, X = 11, Z = 10, Status = PlayerStatus.Lagging });
            Assert.That(SurvivorReadout.Prompt(own, mates, new TerminalState[0], new Self(), Names, false, SkillIds.DummyPacket, true).Text, Is.EqualTo("Getting up…"));
            Assert.That(HelpCopy.GettingUp.Length, Is.LessThanOrEqualTo(80));
            // The Survivor view maps the flag to the pose for 1.5 s, the Killer view never does.
            var t = new MotionTracker(MotionView.Survivor);
            t.Update(1, (byte)PlayerStatus.Lagging, 0, 1, 0);
            t.Update(2, (byte)PlayerStatus.Normal, (byte)PlayerFlags.GettingUp, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.GettingUp));
            Assert.That(t.PoseSince, Is.EqualTo(2));
            t.Update(3.4, (byte)PlayerStatus.Normal, (byte)PlayerFlags.GettingUp, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.GettingUp));
            t.Update(3.6, (byte)PlayerStatus.Normal, 0, 1, 0);
            Assert.That(t.Pose, Is.Not.EqualTo(MotionPose.GettingUp));
            var k = new MotionTracker(MotionView.Killer);
            k.Update(1, (byte)PlayerStatus.Lagging, 0, 0, 0);
            k.Update(2, (byte)PlayerStatus.Normal, (byte)PlayerFlags.GettingUp, 0, 0);
            Assert.That(k.Pose, Is.EqualTo(MotionPose.Locomotion));
        }

        [Test]
        public void BeyondTheRadiusButWithinSixMetresThePromptSaysToGoNextToThem()
        {
            var mates = Mates(new Mate { Id = 3, X = 11.6f, Z = 10, Status = PlayerStatus.Lagging });
            Assert.That(SurvivorReadout.Prompt(Own(), mates, null, new Self(), Names, false, 0, true).Text,
                Is.EqualTo("Mina is Lagging " + Dot + " 2 m " + Dash + " move next to them, hold [E] to rescue"));
            mates = Mates(new Mate { Id = 3, X = 18, Z = 10, Status = PlayerStatus.Lagging });
            Assert.That(SurvivorReadout.Prompt(Own(), mates, null, new Self(), Names, false, 0, true).Text, Is.Null, "7 m: no prompt (the marker covers it)");
        }

        [Test]
        public void TheRescuerHoldsAndSeesTheServerProgress()
        {
            var own = Own(); own.Rescuing = true; own.Signal = true;
            var mates = Mates(new Mate { Id = 3, X = 11, Z = 10, Status = PlayerStatus.Lagging });
            PromptState p = SurvivorReadout.Prompt(own, mates, null, new Self { RescueProgress = .4f }, Names, false, 0, true);
            Assert.That(p.Text, Is.EqualTo("Rescuing Mina " + Dash + " you are Signal"));
            Assert.That(p.Bar, Is.EqualTo(BarFill.Ally));
            Assert.That(p.Width, Is.EqualTo(.4f));
        }

        // ---- S2.3 the downed player's line ------------------------------------------------------------------------------

        [Test]
        public void TheDownedLineIsPersistentAndNamesTheNearestNormalAllyAndTheTimer()
        {
            var mates = Mates(
                new Mate { Id = 3, X = 28, Z = 10, Status = PlayerStatus.Normal },
                new Mate { Id = 4, X = 40, Z = 10, Status = PlayerStatus.Normal },
                new Mate { Id = 5, X = 11, Z = 10, Status = PlayerStatus.Lagging },
                new Mate { Id = 6, X = 12, Z = 10, Status = PlayerStatus.Eliminated });
            PromptState p = SurvivorReadout.Prompt(Own(PlayerStatus.Lagging, timer: 53.2f), mates, null, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Lagging " + Dot + " an ally can rescue you with [E] " + Dot + " nearest ally 18 m " + Dot + " 54 s"));
            Assert.That(p.Muted, Is.False);
            Assert.That(p.Bar, Is.EqualTo(BarFill.Empty));
        }

        [Test]
        public void TheDownedLineDropsTheDistanceWhenNoAllyIsNormalAndFillsWhileRestored()
        {
            var alone = Mates(new Mate { Id = 5, X = 11, Z = 10, Status = PlayerStatus.Lagging });
            PromptState p = SurvivorReadout.Prompt(Own(PlayerStatus.Lagging, timer: 20), alone, null, new Self(), Names, false, 0, false);
            Assert.That(p.Text, Is.EqualTo("Lagging " + Dot + " an ally can rescue you with [E] " + Dot + " 20 s"));
            p = SurvivorReadout.Prompt(Own(PlayerStatus.Lagging, timer: 20), alone, null, new Self { RescueProgress = .5f }, Names, false, 0, false);
            Assert.That(p.Bar, Is.EqualTo(BarFill.Ally));
            Assert.That(p.Width, Is.EqualTo(.5f));
        }

        [Test]
        public void TheDownedLineShowsInTheHudPromptRowAndKeepsItsHeight()
        {
            var parent = new VisualElement();
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            var hud = new SurvivorHud(parent, font);
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                hud.SetPrompt(SurvivorReadout.Downed(Own(PlayerStatus.Lagging, timer: 54), Mates(new Mate { Id = 3, X = 28, Z = 10, Status = PlayerStatus.Normal }), new Self { RescueProgress = .25f }));
                Assert.That(hud.PromptText, Does.StartWith(HelpCopy.DownedPrefix));
                Assert.That(hud.PromptFillHas("fill-ally"), Is.True);
                Assert.That(hud.ClusterRect(new Vector2(1600, 900)).height, Is.EqualTo(72f + 8 + 40));
            }
            finally { hud.Dispose(); }
        }

        // ---- S2.2 Lagging-ally markers ----------------------------------------------------------------------------------

        private static readonly Vector2 Screen1600 = new Vector2(1600, 900);
        private static readonly Rect Cluster = new Rect(464, 804, 672, 72);
        // At (10, 1.7, 10) looking along +Z, 50 degrees vertical.
        private static ViewPose Pose => new ViewPose(new Vector3(10, 1.7f, 10), 0, 0, 50, 16f / 9);

        private static AllyMarkers Build(params Mate[] mates)
        {
            var markers = new AllyMarkers();
            markers.Build(10, 10, mates, Names, Pose, Screen1600, Cluster);
            return markers;
        }

        [Test]
        public void AnOnScreenLaggingAllyGetsAWorldTagWithItsDistance()
        {
            AllyMarkers m = Build(new Mate { Id = 3, X = 10, Z = 30, Status = PlayerStatus.Lagging });
            Assert.That(m.OnScreenCount, Is.EqualTo(1)); Assert.That(m.EdgeCount, Is.EqualTo(0));
            Assert.That(m.Marks[0].Text, Is.EqualTo("Mina " + Dot + " Lagging " + Dot + " 20 m"));
            Assert.That(m.Marks[0].OnScreen, Is.True);
            Assert.That(m.Marks[0].At.x, Is.EqualTo(800f).Within(2f), "straight ahead: screen centre column");
        }

        [Test]
        public void AnOffScreenLaggingAllyGetsAnEdgeArrowInsideTheSafeRectangleAndOutsideTheClusterKeepOut()
        {
            Rect safe = EdgePlacement.SafeRect(Screen1600, false), keepOut = EdgePlacement.KeepOut(Cluster);
            foreach (var at in new[] { new Vector2(40, 10), new Vector2(-20, 10), new Vector2(10, -20), new Vector2(10, 60) }) // right, left, behind, ahead-far
            {
                AllyMarkers m = Build(new Mate { Id = 3, X = at.x, Z = at.y, Status = PlayerStatus.Lagging });
                Assert.That(m.Marks.Count, Is.EqualTo(1));
                AllyMarkers.Mark mark = m.Marks[0];
                if (mark.OnScreen) continue; // the far point ahead may still project on screen
                Assert.That(safe.Contains(mark.At) || Mathf.Approximately(mark.At.x, safe.xMax) || Mathf.Approximately(mark.At.y, safe.yMax), Is.True, "arrow at " + mark.At);
                bool insideKeepOut = mark.At.x > keepOut.xMin && mark.At.x < keepOut.xMax && mark.At.y > keepOut.yMin && mark.At.y < keepOut.yMax;
                Assert.That(insideKeepOut, Is.False, "the arrow never sits in the cluster keep-out");
                Assert.That(mark.Pointing.magnitude, Is.EqualTo(1f).Within(1e-3f));
            }
            // An ally to the right points right and sits on the right edge.
            AllyMarkers right = Build(new Mate { Id = 3, X = 60, Z = 10, Status = PlayerStatus.Lagging });
            Assert.That(right.EdgeCount, Is.EqualTo(1));
            Assert.That(right.Marks[0].Pointing.x, Is.GreaterThan(.9f));
            Assert.That(right.Marks[0].At.x, Is.EqualTo(safe.xMax).Within(.6f));
            Assert.That(right.Marks[0].Text, Does.EndWith(" 50 m"));
        }

        [Test]
        public void TwoArrowsOnOneEdgeKeepTheirFootprintsApart()
        {
            AllyMarkers m = Build(new Mate { Id = 3, X = 60, Z = 10, Status = PlayerStatus.Lagging }, new Mate { Id = 4, X = 62, Z = 11, Status = PlayerStatus.Lagging });
            Assert.That(m.EdgeCount, Is.EqualTo(2));
            Vector2 a = m.Marks[0].At, b = m.Marks[1].At;
            Assert.That(Mathf.Abs(a.y - b.y), Is.GreaterThanOrEqualTo(MarkerLayer.ChevronHalf(true) * 2 - .01f), "glyphs do not overlap");
        }

        [Test]
        public void OnlyLaggingAlliesGetMarkersAndAtMostSevenAreDrawnNearestFirst()
        {
            var list = new List<Mate> { new Mate { Id = 1, X = 10, Z = 30, Status = PlayerStatus.Normal }, new Mate { Id = 2, X = 10, Z = 30, Status = PlayerStatus.Eliminated } };
            for (int i = 0; i < 9; i++) list.Add(new Mate { Id = 10 + i, X = 10 + i, Z = 30, Status = PlayerStatus.Lagging });
            AllyMarkers m = Build(list.ToArray());
            Assert.That(m.Marks.Count, Is.EqualTo(7));
            Assert.That(m.Marks.All(mark => mark.Id >= 10), Is.True);
            Assert.That(m.Marks.Select(mark => mark.Distance).Max(), Is.LessThan(30f), "the two farthest were dropped");
            Assert.That(Build().Marks.Count, Is.EqualTo(0));
        }

        [Test]
        public void TheMarkerTagEllipsizesTheNameAtTwelveCharacters()
        {
            AllyMarkers m = Build(new Mate { Id = 4, X = 10, Z = 30, Status = PlayerStatus.Lagging });
            Assert.That(m.Marks[0].Text, Is.EqualTo("AVeryLongNi… " + Dot + " Lagging " + Dot + " 20 m"));
            m = Build(new Mate { Id = 99, X = 10, Z = 30, Status = PlayerStatus.Lagging });
            Assert.That(m.Marks[0].Text, Does.StartWith("a Survivor " + Dot));
        }

        [Test]
        public void TheHudDrawsAndClearsTheMarkerLayer()
        {
            var parent = new VisualElement();
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            var hud = new SurvivorHud(parent, font);
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                hud.DrawAllyMarkers(10, 10, Mates(new Mate { Id = 3, X = 10, Z = 30, Status = PlayerStatus.Lagging }, new Mate { Id = 4, X = 60, Z = 10, Status = PlayerStatus.Lagging }), Names, Pose, Screen1600);
                Assert.That(hud.Allies.OnScreenCount, Is.EqualTo(1));
                Assert.That(hud.Allies.EdgeCount, Is.EqualTo(1));
                Assert.That(hud.AllyLayer.Query<Label>().Where(l => l.style.display != DisplayStyle.None).ToList().Count, Is.GreaterThanOrEqualTo(1));
                hud.ClearAllyMarkers();
                Assert.That(hud.Allies.Marks.Count, Is.EqualTo(0));
                Assert.That(hud.AllyLayer.Query<Label>().Where(l => l.style.display != DisplayStyle.None).ToList().Count, Is.EqualTo(0));
            }
            finally { hud.Dispose(); }
        }

        // ---- S3.1 intro card ----------------------------------------------------------------------------------------------

        [Test]
        public void TheSurvivorIntroNamesTheGoalTheSkillAndTheControls()
        {
            var card = new IntroCardState();
            card.Begin(Role.Survivor, SkillIds.EmergencyRestore, 100);
            Assert.That(card.Goal, Is.EqualTo("Survivor " + Dot + " Hack the terminals, then hold an exit switch and escape."), "until the count is known");
            card.SetTerminals(3);
            Assert.That(card.Goal, Is.EqualTo("Survivor " + Dot + " Hack 3 terminals, then hold an exit switch and escape."));
            Assert.That(card.SkillLine, Is.EqualTo("Emergency restore [Q] " + Dot + " Cooldown 15 s"));
            Assert.That(card.Effect, Is.EqualTo(PlayMenuCopy.SurvivorEffect(SkillIds.EmergencyRestore)));
            Assert.That(card.Controls, Is.EqualTo("WASD Move · Hold E Hack / Rescue / Exit switch / Shutter · While hacking: WASD routes packets · Q Skill · Esc Menu"));
        }

        [Test]
        public void TheKillerIntroNamesTheGoalTheSkillAndTheControls()
        {
            var card = new IntroCardState();
            card.Begin(Role.Killer, SkillIds.Overclock, 5);
            Assert.That(card.Goal, Is.EqualTo("Killer " + Dot + " Capture every Survivor before they escape."));
            Assert.That(card.SkillLine, Is.EqualTo("Overclock [Q] " + Dot + " Cooldown 12 s"));
            Assert.That(card.Effect, Is.EqualTo(PlayMenuCopy.KillerEffect(SkillIds.Overclock)));
            Assert.That(card.Controls, Is.EqualTo("WASD Move · Left click Attack · Hold E Kick terminal / Break shutter / Close hatch · Q Skill · Esc Menu"));
            card.Begin(Role.Killer, SkillIds.PacketTrace, 5);
            Assert.That(card.SkillLine, Is.EqualTo("Packet trace [Q] " + Dot + " Cooldown 12 s"), "R1: Packet trace is active, with a key and the cooldown");
        }

        [Test]
        public void EverySkillOfBothRolesHasAnEffectLineFromTheOneTable()
        {
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++)
            {
                Assert.That(PlayMenuCopy.SurvivorEffect(id), Is.Not.Empty);
                Assert.That(HelpCopy.SkillEffect(Role.Survivor, id), Is.EqualTo(PlayMenuCopy.SurvivorEffect(id)));
                Assert.That(HelpCopy.IntroSkillLine(Role.Survivor, id), Does.StartWith(SurvivorReadout.SkillName(id)));
            }
            for (byte id = 0; id < SkillIds.KillerSkillCount; id++)
            {
                Assert.That(PlayMenuCopy.KillerEffect(id), Is.Not.Empty);
                Assert.That(HelpCopy.IntroSkillLine(Role.Killer, id), Does.StartWith(PingTheKiller.Client.Killer.KillerHudCopy.SkillName(id)));
            }
        }

        [Test]
        public void TheIntroShowsForSevenSecondsAndAnyKeyOrTheMenuEndsIt()
        {
            var card = new IntroCardState();
            Assert.That(card.Visible(0, false), Is.False, "never begun");
            card.Begin(Role.Survivor, 0, 100);
            Assert.That(HelpCopy.IntroSeconds, Is.EqualTo(7f));
            Assert.That(card.Visible(100, false), Is.True);
            Assert.That(card.Visible(106.9, false), Is.True);
            Assert.That(card.Visible(107, false), Is.False);
            Assert.That(card.Visible(103, true), Is.False, "never under the play menu");
            Assert.That(card.Visible(103, false), Is.True, "and back after it");
            card.Dismiss();
            Assert.That(card.Visible(103, false), Is.False, "any key ends it for good");
            card.Begin(Role.Killer, 0, 200);
            Assert.That(card.Visible(201, false), Is.True, "a new match shows it again");
            card.End();
            Assert.That(card.Visible(201, false), Is.False);
        }

        [Test]
        public void TheIntroCardViewShowsTheStateAndFitsBelowTheFeet()
        {
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            var view = new IntroCard(font);
            var card = new IntroCardState();
            card.Begin(Role.Survivor, SkillIds.DummyPacket, 0); card.SetTerminals(3);
            Assert.That(view.Shown, Is.False);
            view.Set(card, true);
            Assert.That(view.Shown, Is.True);
            Assert.That(view.GoalText, Is.EqualTo(card.Goal));
            Assert.That(view.SkillText, Is.EqualTo(card.SkillLine));
            Assert.That(view.EffectText, Is.EqualTo(card.Effect));
            Assert.That(view.ControlsText, Is.EqualTo(card.Controls));
            view.Set(card, false);
            Assert.That(view.Shown, Is.False);
            // HUD_SPEC 4.10: the character's feet are at most 622 u from the top at 720p (900 u); the card's top stays below that.
            float top = 900 - IntroCard.SurvivorBottom - 134;
            Assert.That(top, Is.GreaterThan(622f));
            Assert.That(ClientApp.SurvivorIntroHeight, Is.EqualTo(134f));
        }

        [Test]
        public void TheIntroTakesThePromptSlotInTheSurvivorKeepOut()
        {
            var parent = new VisualElement();
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            var hud = new SurvivorHud(parent, font);
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                float plain = hud.ClusterRect(new Vector2(1600, 900)).height;
                hud.IntroHeight = ClientApp.SurvivorIntroHeight;
                Assert.That(hud.ClusterRect(new Vector2(1600, 900)).height, Is.EqualTo(plain + ClientApp.SurvivorIntroHeight + 8));
            }
            finally { hud.Dispose(); }
        }

        // ---- S3.2 How to play ---------------------------------------------------------------------------------------------

        [Test]
        public void EachHowToPlayPageCarriesTheGoalControlsLadderVisibilityAndEverySkill()
        {
            HelpPage survivor = HelpCopy.Page(Role.Survivor), killer = HelpCopy.Page(Role.Killer);
            foreach (HelpPage page in new[] { survivor, killer })
            {
                Assert.That(page.Goal, Is.Not.Empty);
                Assert.That(page.Ladder.Length, Is.EqualTo(3));
                Assert.That(page.Ladder[0], Does.StartWith("Normal:"));
                Assert.That(page.Ladder[1], Does.StartWith("Lagging:"));
                Assert.That(page.Ladder[2], Does.StartWith("Rescue:"), "how a rescue works");
                Assert.That(page.Visibility, Does.Contain("Signal").And.Contain("Silent"));
            }
            Assert.That(survivor.Controls, Is.EqualTo(HelpCopy.SurvivorControls));
            Assert.That(killer.Controls, Is.EqualTo(HelpCopy.KillerControls));
            Assert.That(survivor.Skills.Length, Is.EqualTo(SkillIds.SurvivorSkillCount));
            Assert.That(killer.Skills.Length, Is.EqualTo(SkillIds.KillerSkillCount));
            foreach (HelpSkill skill in survivor.Skills)
            {
                Assert.That(skill.Name, Is.EqualTo(SurvivorReadout.SkillName(skill.Id)));
                Assert.That(skill.Effect, Is.EqualTo(PlayMenuCopy.SurvivorEffect(skill.Id)));
            }
            foreach (HelpSkill skill in killer.Skills)
            {
                Assert.That(skill.Name, Is.EqualTo(PingTheKiller.Client.Killer.KillerHudCopy.SkillName(skill.Id)));
                Assert.That(skill.Effect, Is.EqualTo(PlayMenuCopy.KillerEffect(skill.Id)));
            }
            Assert.That(survivor.Ladder[1], Does.Contain("60 s").And.Contain("30 s"));
            Assert.That(survivor.Ladder[2], Does.Contain("3 s").And.Contain("1.5 m").And.Contain("2 s"));
            Assert.That(survivor.Ladder[0], Does.Contain("eliminates"), "the ladder reaches Eliminated");
        }

        [Test]
        public void TheHowToPlayScreenOpensOnTheSurvivorPageSwitchesAndCloses()
        {
            var root = new VisualElement();
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            var screen = new HowToPlayScreen(root, font);
            int closed = 0; screen.Closed += () => closed++;
            Assert.That(screen.Visible, Is.False);
            screen.Show(Role.Survivor);
            Assert.That(screen.Visible, Is.True);
            Assert.That(screen.Page, Is.EqualTo(Role.Survivor));
            Assert.That(screen.Texts, Does.Contain(HelpCopy.Page(Role.Survivor).Goal));
            Assert.That(screen.Texts.Any(t => t.StartsWith("Emergency restore ")), Is.True);
            Assert.That(screen.Texts.Any(t => t.StartsWith("Ping scanner")), Is.False, "Killer skills are on the Killer page");
            screen.SetPage(Role.Killer);
            Assert.That(screen.Page, Is.EqualTo(Role.Killer));
            Assert.That(screen.Texts, Does.Contain(HelpCopy.Page(Role.Killer).Goal));
            Assert.That(screen.Texts.Any(t => t.StartsWith("Ping scanner")), Is.True);
            Assert.That(screen.Texts.Count(t => t.StartsWith("Firewall block") || t.StartsWith("Overclock") || t.StartsWith("Packet trace") || t.StartsWith("Ping scanner")), Is.EqualTo(4), "R1: Packet trace replaced Packet afterimage");
            screen.Close();
            Assert.That(screen.Visible, Is.False);
            Assert.That(closed, Is.EqualTo(1));
            screen.Close();
            Assert.That(closed, Is.EqualTo(1), "closing a hidden screen does nothing");
        }

        // ---- S3.3 hints --------------------------------------------------------------------------------------------------

        [Test]
        public void EverySurvivorHintFiresOncePerMatch()
        {
            var hints = new HintTracker();
            Assert.That(hints.Survivor(0, true, true, true, "Mina", true), Is.Null, "none under the intro card, and nothing is spent");
            Assert.That(hints.FiredTerminal, Is.False);
            Assert.That(hints.Survivor(10, false, true, false, null, false), Is.Null);
            Assert.That(hints.Survivor(11, false, true, true, null, false), Is.EqualTo("Hold [E] at a terminal to hack it"));
            Assert.That(hints.Survivor(14, false, true, true, null, false), Is.EqualTo("Hold [E] at a terminal to hack it"), "stays while its time runs");
            Assert.That(hints.Survivor(11 + HelpCopy.HintSeconds + .1, false, true, true, null, false), Is.Null, "gone, and never again");
            Assert.That(hints.Survivor(30, false, true, true, "Mina", false), Is.EqualTo("Mina is Lagging " + Dot + " hold [E] next to them to rescue"));
            Assert.That(hints.Survivor(40, false, true, true, "Mina", false), Is.Null, "the Lagging hint fired once too");
            Assert.That(hints.Survivor(50, false, true, false, null, true), Is.EqualTo("You are Signal: the Killer can see you"));
            Assert.That(hints.Survivor(70, false, true, true, "Mina", true), Is.Null, "all three are spent");
            Assert.That(hints.FiredTerminal && hints.FiredLagging && hints.FiredSignal, Is.True);
            hints.Reset();
            Assert.That(hints.Survivor(80, false, true, true, null, false), Is.EqualTo("Hold [E] at a terminal to hack it"), "a new match re-arms them");
        }

        [Test]
        public void SurvivorHintsNeedANormalSurvivor()
        {
            var hints = new HintTracker();
            Assert.That(hints.Survivor(1, false, false, true, "Mina", true), Is.Null);
            Assert.That(hints.FiredTerminal || hints.FiredLagging || hints.FiredSignal, Is.False);
        }

        [Test]
        public void TheKillerHintFiresOnceWhenABlipIsWithinSixMetres()
        {
            var hints = new HintTracker();
            Assert.That(hints.Killer(0, true, true), Is.Null, "none under the intro card");
            Assert.That(hints.Killer(5, false, false), Is.Null);
            Assert.That(hints.Killer(6, false, true), Is.EqualTo("Get close and left click to attack"));
            Assert.That(hints.Killer(8, false, true), Is.EqualTo("Get close and left click to attack"));
            Assert.That(hints.Killer(6 + HelpCopy.HintSeconds + .1, false, true), Is.Null);
            Assert.That(hints.Killer(60, false, true), Is.Null);
            hints.Reset();
            Assert.That(hints.Killer(61, false, true), Is.Not.Null);
        }

        // ---- SLOP copy rules ------------------------------------------------------------------------------------------------

        private static readonly string[] Hype = { "unleash", "epic", "ultimate", "seamless", "immersive", "dive into", "embark", "get ready", "welcome", "let's go", "are you ready" };

        private static IEnumerable<string> Lines()
        {
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
            {
                foreach (string s in HelpCopy.AllText(HelpCopy.Page(role))) yield return s;
                yield return HelpCopy.IntroGoal(role, 3);
                yield return HelpCopy.IntroGoal(role, 0);
                byte count = role == Role.Killer ? SkillIds.KillerSkillCount : SkillIds.SurvivorSkillCount;
                for (byte id = 0; id < count; id++) { yield return HelpCopy.IntroSkillLine(role, id); yield return HelpCopy.SkillEffect(role, id); }
            }
            yield return HelpCopy.RescuePrompt("AVeryLongNi…");
            yield return HelpCopy.RescuingPrompt("AVeryLongNi…");
            yield return HelpCopy.ApproachPrompt("AVeryLongNi…", 6);
            yield return HelpCopy.DownedLine(138, 120);
            yield return HelpCopy.DownedLine(-1, 60);
            yield return HelpCopy.AllyTag("AVeryLongNi…", 138);
            yield return HelpCopy.HintTerminal; yield return HelpCopy.HintSignal; yield return HelpCopy.HintKillerBlip;
            yield return HelpCopy.HintLagging("AVeryLongNi…");
            yield return CaptureWatch.Banner; yield return CaptureWatch.SecondBanner;
        }

        [Test]
        public void EveryNewStringFollowsTheSlopCopyRules()
        {
            foreach (string line in Lines())
            {
                Assert.That(line, Is.Not.Empty);
                Assert.That(line, Does.Not.Contain("!"), line);
                Assert.That(line, Does.Not.Contain("  "), line);
                Assert.That(line, Does.Not.Contain("..."), line);
                foreach (string word in Hype) Assert.That(line.ToLowerInvariant(), Does.Not.Contain(word), line);
                Assert.That(char.IsUpper(line[0]) || char.IsDigit(line[0]), Is.True, "sentence case: " + line);
            }
        }

        [Test]
        public void NothingIsTitleCaseExceptTheControlsLegends()
        {
            foreach (string line in Lines())
            {
                if (line == HelpCopy.SurvivorControls || line == HelpCopy.KillerControls) continue;
                string[] words = line.Replace("AVeryLongNi…", "x").Split(' ').Where(w => w.Length > 1 && char.IsLetter(w[0])).ToArray();
                if (words.Length < 4) continue;
                int capital = words.Count(w => char.IsUpper(w[0]));
                Assert.That(capital * 2, Is.LessThanOrEqualTo(words.Length), "Title Case: " + line);
            }
        }

        [Test]
        public void PromptRowLinesFitTheRowAndInlineLinesFitOneLine()
        {
            // The prompt row is 656 u of M text (about 8.2 u a character): 80 characters at most. The names are the 12-character worst case.
            var promptRow = new List<string>
            {
                HelpCopy.RescuePrompt("AVeryLongNi…"), HelpCopy.RescuingPrompt("AVeryLongNi…"), HelpCopy.ApproachPrompt("AVeryLongNi…", 6),
                HelpCopy.DownedLine(138, 120), HelpCopy.DownedLine(-1, 60), HelpCopy.HintTerminal, HelpCopy.HintSignal, HelpCopy.HintKillerBlip,
                HelpCopy.HintLagging("AVeryLongNi…"), CaptureWatch.Banner, CaptureWatch.SecondBanner,
            };
            foreach (string line in promptRow) Assert.That(line.Length, Is.LessThanOrEqualTo(80), line);
            // The intro card's M lines (648 u): 79 characters.
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
            {
                Assert.That(HelpCopy.IntroGoal(role, 10).Length, Is.LessThanOrEqualTo(79));
                byte count = role == Role.Killer ? SkillIds.KillerSkillCount : SkillIds.SurvivorSkillCount;
                for (byte id = 0; id < count; id++)
                {
                    Assert.That(HelpCopy.IntroSkillLine(role, id).Length, Is.LessThanOrEqualTo(79));
                    Assert.That(HelpCopy.SkillEffect(role, id).Length, Is.LessThanOrEqualTo(87), "one S line (646 u at 7.4 u a character)");
                }
            }
            // The controls legend is at most two S lines on the card.
            Assert.That(HelpCopy.SurvivorControls.Length, Is.LessThanOrEqualTo(2 * 87));
            Assert.That(HelpCopy.KillerControls.Length, Is.LessThanOrEqualTo(2 * 87));
            Assert.That(HelpCopy.AllyTag("AVeryLongNi…", 999).Length, Is.LessThanOrEqualTo(30));
        }

        [Test]
        public void TheRescueCopyNoLongerPromisesAnAutomaticRescue()
        {
            foreach (string line in Lines().Concat(new[] { PlayMenuCopy.SurvivorEffect(SkillIds.EmergencyRestore) }))
            {
                Assert.That(line, Does.Not.Contain("stay within 1.5 m"), line);
                Assert.That(line, Does.Not.Contain("A Survivor within"), line);
            }
            Assert.That(CaptureWatch.Banner, Does.Contain("[E]"));
        }
    }
}
