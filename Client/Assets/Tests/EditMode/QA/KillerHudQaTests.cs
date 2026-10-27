using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test items 6, 7 and 8. Written from HUD_SPEC 3, 6.1 (a, c, d, e, p), 6.2 (terminals, exit), 6.4 and 8.
    [Category("U4cQaA1")]
    public sealed class KillerHudQaTests
    {
        private const string Dot = "·", Dash = "—";

        private sealed class StubSelf : ISelfState
        {
            public bool Available { get; set; }
            public float SkillCooldown { get; set; }
            public float RescueProgress { get; set; }
            public float InvulnerableLeft { get; set; }
            public float ExecutionGraceLeft { get; set; }
        }

        private static KillerHud NewHud(out VisualElement parent)
        {
            parent = new VisualElement();
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            Assert.That(font, Is.Not.Null, "Resources/Fonts/Inter-Regular is the HUD font");
            return new KillerHud(parent, font);
        }

        private static bool Shown(VisualElement element, VisualElement root)
        {
            for (VisualElement e = element; e != null && e != root; e = e.parent)
                if (e.style.display.value == DisplayStyle.None) return false;
            return true;
        }

        private static Label LabelWithText(VisualElement root, string text)
        {
            foreach (Label label in root.Query<Label>().ToList()) if (label.text == text) return label;
            return null;
        }

        private static bool LabelShown(VisualElement root, string text)
        {
            Label label = LabelWithText(root, text);
            return label != null && Shown(label, root);
        }

        private static VisualElement Block(VisualElement root, string cssClass) => root.Q<VisualElement>(className: cssClass);

        // ---- 7. copy ---------------------------------------------------------------------------------------------------------

        [Test]
        public void TheClockIsMinutesColonTwoDigitSeconds()
        {
            Assert.That(KillerHudCopy.Time(582), Is.EqualTo("9:42"));
            Assert.That(KillerHudCopy.Time(600), Is.EqualTo("10:00"));
            Assert.That(KillerHudCopy.Time(599), Is.EqualTo("9:59"));
            Assert.That(KillerHudCopy.Time(61), Is.EqualTo("1:01"));
            Assert.That(KillerHudCopy.Time(60), Is.EqualTo("1:00"));
            Assert.That(KillerHudCopy.Time(5), Is.EqualTo("0:05"));
            Assert.That(KillerHudCopy.Time(0), Is.EqualTo("0:00"));
            Assert.That(KillerHudCopy.Time(-3), Is.EqualTo("0:00"));
            Assert.That(KillerHudCopy.Time(float.NaN), Is.EqualTo("0:00"));
            Assert.That(KillerHudCopy.Time(float.PositiveInfinity), Does.Match(@"^\d+:\d\d$"), "never a broken string");
        }

        [Test]
        public void TheLastSixtySecondsGetTheTimePressureBarAndNothingBefore()
        {
            Assert.That(KillerHudCopy.TimePressure(600), Is.False);
            Assert.That(KillerHudCopy.TimePressure(60.5f), Is.False);
            Assert.That(KillerHudCopy.TimePressure(60), Is.True);
            Assert.That(KillerHudCopy.TimePressure(30), Is.True);
            Assert.That(KillerHudCopy.TimePressure(0), Is.True);
        }

        [Test]
        public void TheTerminalCountReadsKOfNAndTheHintAppearsExactlyWhenOneIsMissing()
        {
            Assert.That(KillerHudCopy.Terminals(0, 4), Is.EqualTo("Terminals 0 of 4"));
            Assert.That(KillerHudCopy.Terminals(3, 4), Is.EqualTo("Terminals 3 of 4"));
            for (int needed = 1; needed <= 8; needed++)
                for (int done = 0; done <= needed + 1; done++)
                {
                    string line = KillerHudCopy.OneMoreLine(done, needed);
                    if (done == needed - 1) Assert.That(line, Is.EqualTo("One more opens the Exit."), done + " of " + needed);
                    else Assert.That(line, Is.Null, done + " of " + needed);
                }
            Assert.That(KillerHudCopy.OneMoreLine(0, 0), Is.Null, "no requirement known yet");
        }

        [Test]
        public void TheEffectLinesUseTheSpecWordsAndOneDecimalOfSeconds()
        {
            Assert.That(KillerHudCopy.Overclock(2.1f), Is.EqualTo("Overclock " + Dot + " 2.1 s"));
            Assert.That(KillerHudCopy.Recovering(.8f), Is.EqualTo("Recovering " + Dot + " 0.8 s"));
            Assert.That(KillerHudCopy.Slowed(1.6f), Is.EqualTo("Slowed by traffic overload " + Dot + " 1.6 s"));
            Assert.That(SkillIds.OverclockRecoverySeconds, Is.EqualTo(1f), "Recovering lasts a static 1 s after the Overclock Duration");
            Assert.That(KillerHudCopy.Slowed(-4f), Does.Not.Contain("-"), "a negative remainder never shows");
            Assert.That(KillerHudCopy.Slowed(float.NaN), Does.Not.Contain("NaN"));
        }

        [Test]
        public void TheSkillBlockStateLineCoversReadyCooldownPassiveAndAfterTheExitOpens()
        {
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, false, null), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, false, 0f), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PingScanner, false, 6.2f), Is.EqualTo("Cooldown 7 s"), "ceil of the server's value");
            Assert.That(KillerHudCopy.SkillState(SkillIds.Overclock, true, .1f), Is.EqualTo("Cooldown 1 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.Overclock, true, 12f), Is.EqualTo("Cooldown 12 s"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketAfterimage, false, null), Is.EqualTo("Passive " + Dash + " always on"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.PacketAfterimage, true, 5f), Is.EqualTo("Passive " + Dash + " always on"), "a passive skill has no cooldown");
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, false, null), Is.EqualTo("After the Exit opens"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, false, 3f), Is.EqualTo("After the Exit opens"), "disabled until the Exit opens");
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, true, null), Is.EqualTo("Ready"));
            Assert.That(KillerHudCopy.SkillState(SkillIds.FirewallBlock, true, 4.5f), Is.EqualTo("Cooldown 5 s"));
        }

        [Test]
        public void TheKillerSkillNamesAndEffectLinesAreTheSpecTable()
        {
            string[] names = { "Ping scanner", "Overclock", "Packet afterimage", "Firewall block" };
            string[] effects =
            {
                "Shows the nearest Survivor for 1.5 s.",
                "1.5× speed for 3 s, then 1 s unable to move.",
                "Passive: Survivors stay visible 1 s after their Signal ends.",
                "Locks the open Exit for 5 s.",
            };
            for (byte id = 0; id < SkillIds.KillerSkillCount; id++)
            {
                Assert.That(KillerHudCopy.SkillName(id), Is.EqualTo(names[id]));
                Assert.That(KillerHudCopy.SkillEffect(id), Is.EqualTo(effects[id]));
            }
        }

        [Test]
        public void TheExitAndTerminalTagsFollowTheSpecAndNeverCarryAPercentage()
        {
            for (byte id = 0; id < 6; id++) Assert.That(KillerHudCopy.Terminal(id), Is.EqualTo("Terminal " + (id + 1)));
            Assert.That(KillerHudCopy.ExitLocked, Is.EqualTo("Exit Locked"));
            Assert.That(KillerHudCopy.ExitOpen, Is.EqualTo("Exit Open"));
            Assert.That(KillerHudCopy.ExitLockedFor(4.0f), Is.EqualTo("Exit Locked 4 s"));
            Assert.That(KillerHudCopy.ExitLockedFor(.2f), Is.EqualTo("Exit Locked 1 s"));
            Assert.That(KillerHudCopy.ExitLockedFor(null), Is.EqualTo("Exit Locked"), "no effect received, for example after a reconnect");
            Assert.That(KillerHudCopy.ExitLockedFor(0f), Is.EqualTo("Exit Locked"));
            Assert.That(KillerHudCopy.Distance(23.6f), Is.EqualTo("24 m"));
            Assert.That(KillerHudCopy.Distance(.4f), Is.EqualTo("0 m"));
            Assert.That(KillerHudCopy.LaggingDistance(24), Is.EqualTo("Lagging " + Dot + " 24 m"));
        }

        [Test]
        public void NoKillerCopyConstantOrGeneratedLineContainsAPercentSignOrTheForbiddenVocabulary()
        {
            var texts = new List<string>();
            foreach (FieldInfo f in typeof(KillerHudCopy).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(string)) texts.Add((string)f.GetRawConstantValue());
            for (byte id = 0; id < 8; id++) { texts.Add(KillerHudCopy.SkillName(id)); texts.Add(KillerHudCopy.SkillEffect(id)); texts.Add(KillerHudCopy.Terminal(id)); }
            texts.Add(KillerHudCopy.Time(123)); texts.Add(KillerHudCopy.Overclock(1)); texts.Add(KillerHudCopy.Recovering(1)); texts.Add(KillerHudCopy.Slowed(1));
            foreach (ExecutionPrompt p in Enum.GetValues(typeof(ExecutionPrompt))) texts.Add(KillerHudCopy.PromptLine(p, 1.2f));
            Assert.That(texts.Count, Is.GreaterThan(20));
            foreach (string text in texts)
            {
                if (text == null) continue;
                Assert.That(text, Does.Not.Contain("%"), text);
                Assert.That(text, Does.Not.Contain("Frozen"), "the vocabulary is Lagging, not Frozen: " + text);
                Assert.That(text, Does.Not.Contain("ally"), text);
                Assert.That(text, Does.Not.Match(@"\bally\b|\bAlly\b"), text);
            }
        }

        [Test]
        public void ExecutionPromptLinesFitTheThirtyFiveCharacterBudgetAtTheLongestGrace()
        {
            Assert.That(KillerHudCopy.LaggingTitle, Is.EqualTo("Lagging Survivor"));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Grace, 1.64f), Is.EqualTo("Wait 1.6 s, step back, touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Grace, 2.0f).Length, Is.LessThanOrEqualTo(35), "the server grace is 2 s");
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.StepBack, 0), Is.EqualTo("Step back 1.2 m, then touch again."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.StepBack, 0).Length, Is.LessThanOrEqualTo(35));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.Ready, 0), Is.EqualTo("Touch again to eliminate."));
            Assert.That(KillerHudCopy.PromptLine(ExecutionPrompt.None, 0), Is.Null);
        }

        [Test]
        public void TheIntroCopyIsTheSpecCopy()
        {
            Assert.That(KillerHudCopy.IntroTitle, Is.EqualTo("You are the Killer"));
            Assert.That(KillerHudCopy.IntroRules, Is.EqualTo("Only Signal Survivors show, even through walls. Catch every Survivor before one reaches the Exit."));
        }

        // ---- 7. the HUD widget itself ----------------------------------------------------------------------------------------

        [Test]
        public void TheClusterStartsWithTheIntroSkillNameAndEffectAndNothingElseOptional()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.Overclock);
            Assert.That(LabelShown(root, "You are the Killer"), Is.True);
            Assert.That(root.Query<Label>().Where(l => l.text == "Overclock").ToList().Count, Is.GreaterThanOrEqualTo(2), "intro, menu and skill block carry the name");
            Assert.That(LabelShown(root, KillerHudCopy.SkillEffect(SkillIds.Overclock)), Is.True);
            Assert.That(Shown(Block(root, "kintro"), root), Is.True);
            Assert.That(Shown(Block(root, "kmenu"), root), Is.False);
            Assert.That(Shown(Block(root, "kprompt"), root), Is.False, "no execution prompt without a target");
            Assert.That(Shown(Block(root, "keffect"), root), Is.False, "no effect line without an effect");
            Assert.That(hud.MenuOpen, Is.False);
        }

        [Test]
        public void TheInfoBlockShowsTheClockTheTerminalCountAndTheHintOnlyWhenOneIsMissing()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.PingScanner);
            VisualElement info = Block(root, "kinfo");
            Label hint = LabelWithText(root, "One more opens the Exit.");
            Assert.That(hint, Is.Not.Null);

            hud.SetInfo(582, 1, 3);
            Assert.That(LabelShown(root, "9:42"), Is.True);
            Assert.That(LabelShown(root, "Terminals 1 of 3"), Is.True);
            Assert.That(hint.style.visibility.value, Is.EqualTo(Visibility.Hidden), "1 of 3: two are missing");

            hud.SetInfo(581, 2, 3);
            Assert.That(LabelShown(root, "Terminals 2 of 3"), Is.True);
            Assert.That(hint.style.visibility.value, Is.EqualTo(Visibility.Visible), "2 of 3: one more opens the Exit");

            hud.SetInfo(580, 3, 3);
            Assert.That(hint.style.visibility.value, Is.EqualTo(Visibility.Hidden), "3 of 3: nothing left to say");
            Assert.That(Shown(info, root), Is.True, "the info block is always shown");
        }

        [Test]
        public void TheTimePressureBarShowsOnlyInTheLastSixtySeconds()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.PingScanner);
            VisualElement row = Block(root, "krow");
            Assert.That(row, Is.Not.Null);
            VisualElement bar = row.Q<VisualElement>(className: "kbar");
            Assert.That(bar, Is.Not.Null);
            hud.SetInfo(300, 0, 3);
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.None));
            hud.SetInfo(61, 0, 3);
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.None));
            hud.SetInfo(59, 0, 3);
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(bar.ClassListContains("danger"), Is.False, "time pressure is not Killer-caused threat, so it is never danger red");
        }

        [Test]
        public void TheEffectLineShowsOverclockRecoveringAndSlowedAndOnlyTheSlowedOneHasTheDangerBar()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.Overclock);
            VisualElement effect = Block(root, "keffect");
            VisualElement bar = effect.Q<VisualElement>(className: "kbar");
            hud.SetEffect(null, false);
            Assert.That(Shown(effect, root), Is.False);
            hud.SetEffect(KillerHudCopy.Overclock(2.1f), false);
            Assert.That(Shown(effect, root), Is.True);
            Assert.That(LabelShown(root, "Overclock " + Dot + " 2.1 s"), Is.True);
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.None), "Overclock is not a threat");
            hud.SetEffect(KillerHudCopy.Recovering(.8f), false);
            Assert.That(LabelShown(root, "Recovering " + Dot + " 0.8 s"), Is.True);
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.None));
            hud.SetEffect(KillerHudCopy.Slowed(1.6f), true);
            Assert.That(LabelShown(root, "Slowed by traffic overload " + Dot + " 1.6 s"), Is.True);
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(bar.ClassListContains("danger"), Is.True, "danger is a non-text bar next to the words");
            hud.SetEffect(null, false);
            Assert.That(Shown(effect, root), Is.False, "hidden again when none");
        }

        [Test]
        public void SkillStateLineAndRingNeverShowACooldownWhileSelfStateIsUnavailable()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.Overclock);
            FieldInfo state = typeof(SkillRing).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(state, Is.Not.Null, "SkillRing keeps its draw state in a private field named state");
            SkillRing ring = root.Q<SkillRing>();
            Assert.That(ring, Is.Not.Null);
            VisualElement skill = Block(root, "kskill");

            // S2C_SelfState has not arrived: even a stale 9 s on the holder must not draw a cooldown.
            var self = new StubSelf { Available = false, SkillCooldown = 9 };
            hud.SetSkill(SkillIds.Overclock, true, self, -1);
            Assert.That(LabelShown(skill, "Ready"), Is.True);
            Assert.That(LabelWithText(skill, "Cooldown 9 s"), Is.Null);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Ready"), "a full ring, never the cooldown arc");
            hud.SetSkill(SkillIds.Overclock, true, null, -1);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Ready"), "a null seam behaves the same");

            // Once it is available, the same call shows the server's cooldown.
            self.Available = true;
            hud.SetSkill(SkillIds.Overclock, true, self, -1);
            Assert.That(LabelShown(skill, "Cooldown 9 s"), Is.True);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Cooldown"));
            self.SkillCooldown = 0;
            hud.SetSkill(SkillIds.Overclock, true, self, -1);
            Assert.That(LabelShown(skill, "Ready"), Is.True);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Ready"));
        }

        [Test]
        public void PassiveAndAfterExitSkillsHaveTheirOwnRingStateAndNoKeyGlyphWhenPassive()
        {
            KillerHud hud = NewHud(out VisualElement root);
            FieldInfo state = typeof(SkillRing).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
            SkillRing ring = root.Q<SkillRing>();
            VisualElement skill = Block(root, "kskill");

            hud.Begin(SkillIds.PacketAfterimage);
            hud.SetSkill(SkillIds.PacketAfterimage, false, null, -1);
            Assert.That(LabelShown(skill, "Passive " + Dash + " always on"), Is.True);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Passive"), "no ring for a passive skill");
            Assert.That(LabelShown(skill, "[Q]"), Is.False, "nothing to press");

            hud.Begin(SkillIds.FirewallBlock);
            hud.SetSkill(SkillIds.FirewallBlock, false, null, -1);
            Assert.That(LabelShown(skill, "After the Exit opens"), Is.True);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Disabled"));
            Assert.That(LabelShown(skill, "[Q]"), Is.True);
            hud.SetSkill(SkillIds.FirewallBlock, true, null, -1);
            Assert.That(LabelShown(skill, "Ready"), Is.True);
            Assert.That(state.GetValue(ring).ToString(), Is.EqualTo("Ready"));
        }

        [Test]
        public void TheExecutionPromptBlockShowsTheTitleAndTheLineAndHidesWithTheMenuOrWithoutATarget()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.PingScanner);
            VisualElement prompt = Block(root, "kprompt");
            hud.SetPrompt(ExecutionPrompt.Grace, 1.64f);
            Assert.That(Shown(prompt, root), Is.True);
            Assert.That(LabelShown(prompt, "Lagging Survivor"), Is.True);
            Assert.That(LabelShown(prompt, "Wait 1.6 s, step back, touch again."), Is.True);
            hud.SetPrompt(ExecutionPrompt.StepBack, 0);
            Assert.That(LabelShown(prompt, "Step back 1.2 m, then touch again."), Is.True);
            hud.SetPrompt(ExecutionPrompt.Ready, 0);
            Assert.That(LabelShown(prompt, "Touch again to eliminate."), Is.True);
            hud.SetPrompt(ExecutionPrompt.None, 0);
            Assert.That(Shown(prompt, root), Is.False);
            hud.SetPrompt(ExecutionPrompt.Ready, 0);
            hud.SetMenu(true);
            Assert.That(Shown(prompt, root), Is.False, "the play menu replaces the prompt area");
            hud.SetPrompt(ExecutionPrompt.Ready, 0);
            Assert.That(Shown(prompt, root), Is.False, "and a prompt cannot reappear over the open menu");
            hud.SetMenu(false);
        }

        [Test]
        public void ThePlayMenuReplacesTheIntroHasResumeAndLeaveMatchAndKeepsTheInfoAndSkillBlocks()
        {
            KillerHud hud = NewHud(out VisualElement root);
            hud.Begin(SkillIds.FirewallBlock);
            hud.SetMenu(true);
            Assert.That(hud.MenuOpen, Is.True);
            Assert.That(Shown(Block(root, "kmenu"), root), Is.True);
            Assert.That(Shown(Block(root, "kintro"), root), Is.False, "the menu takes the intro's place");
            hud.SetIntro(true);
            Assert.That(Shown(Block(root, "kintro"), root), Is.False, "the intro timer cannot bring the intro back over the menu");
            var buttons = new List<string>();
            foreach (Button b in Block(root, "kmenu").Query<Button>().ToList()) buttons.Add(b.text);
            Assert.That(buttons, Is.EqualTo(new[] { "Resume", "Leave match" }));
            Assert.That(LabelShown(Block(root, "kmenu"), "Firewall block"), Is.True, "the menu names the skill");
            Assert.That(LabelShown(Block(root, "kmenu"), "Locks the open Exit for 5 s."), Is.True, "and states its effect");
            Assert.That(Shown(Block(root, "kinfo"), root), Is.True);
            Assert.That(Shown(Block(root, "kskill"), root), Is.True);
            hud.SetMenu(false);
            Assert.That(hud.MenuOpen, Is.False);
            hud.SetIntro(true);
            Assert.That(Shown(Block(root, "kintro"), root), Is.True);
        }

        [Test]
        public void TheClusterRectangleGrowsByTheSpecBlockHeightsAndTheWorstCaseIs472()
        {
            KillerHud hud = NewHud(out _);
            hud.Begin(SkillIds.PingScanner);
            // HUD_SPEC 6.1: info 72 + skill 72 + 8 gap = 152 in normal play; intro 208 + 8; prompt 56 + 8; effect line 32 + 8.
            hud.SetIntro(false);
            Rect normal = hud.ClusterRect(900);
            Assert.That(normal.width, Is.EqualTo(320f));
            Assert.That(normal.height, Is.EqualTo(152f));
            Assert.That(normal.x, Is.EqualTo(24f));
            Assert.That(normal.yMax, Is.EqualTo(900f - 24), "bottom-left, margin 24");
            hud.SetEffect("Overclock", false);
            Assert.That(hud.ClusterRect(900).height, Is.EqualTo(192f), "normal play with the effect line");
            hud.SetEffect(null, false);
            hud.SetPrompt(ExecutionPrompt.Ready, 0);
            Assert.That(hud.ClusterRect(900).height, Is.EqualTo(152f + 64));
            hud.SetEffect("Overclock", false);
            hud.SetIntro(true);
            Rect worst = hud.ClusterRect(900);
            Assert.That(worst.height, Is.EqualTo(472f), "everything shown");
            Assert.That(worst.yMin, Is.EqualTo(404f), "HUD_SPEC 6.1: the top edge sits at 900 - 24 - 472 = 404 u");
            // The play menu uses the same 208 u slot as the intro.
            hud.SetMenu(true);
            Assert.That(hud.ClusterRect(900).height, Is.EqualTo(472f - 64), "menu replaces the intro; the prompt is hidden while the menu is open");
        }

        // ---- 8. execution guidance (pure) -------------------------------------------------------------------------------------

        [Test]
        public void ExecutionGuidanceFollowsTheServerRulesAcrossACaptureAndAnEliminatingTouch()
        {
            var g = new ExecutionGuidance();
            // The capture: the Lagging Survivor is in contact, 2 s of grace left.
            for (float grace = 2.0f; grace > 0; grace -= .05f)
            {
                Assert.That(g.Update(4, .8f, true, grace), Is.EqualTo(ExecutionPrompt.Grace), "grace " + grace);
                Assert.That(g.Armed, Is.False);
            }
            Assert.That(g.Update(4, .8f, true, 0), Is.EqualTo(ExecutionPrompt.StepBack), "the grace is over but the Killer never left contact");
            Assert.That(g.Update(4, 1.5f, true, 0), Is.EqualTo(ExecutionPrompt.Ready), "out of contact: armed");
            Assert.That(g.Update(4, .8f, true, 0), Is.EqualTo(ExecutionPrompt.Ready), "touching again eliminates");
            Assert.That(g.Armed, Is.True);
        }

        [Test]
        public void ATouchDuringTheGraceIsSpentAndNeedsAnotherStepBack()
        {
            var g = new ExecutionGuidance();
            g.Update(4, .8f, true, 2.0f);
            g.Update(4, 2.0f, true, 1.5f);          // stepped out during the grace: armed, but the grace still shows
            Assert.That(g.Armed, Is.True);
            Assert.That(g.Update(4, 2.0f, true, 1.4f), Is.EqualTo(ExecutionPrompt.Grace));
            g.Update(4, .8f, true, 1.2f);           // the in-grace touch
            Assert.That(g.Armed, Is.False, "spent");
            Assert.That(g.Update(4, .8f, true, 0), Is.EqualTo(ExecutionPrompt.StepBack), "after the grace it asks for one more step back");
            Assert.That(g.Update(4, 1.3f, true, 0), Is.EqualTo(ExecutionPrompt.Ready));
        }

        [Test]
        public void TheTargetIsTheNearestLaggingInstanceWithinThreeMetresAndAFreshTargetStartsDisarmed()
        {
            var g = new ExecutionGuidance();
            Assert.That(g.Update(1, 3.0f, true, 0), Is.Not.EqualTo(ExecutionPrompt.None), "exactly 3 m is still in range");
            Assert.That(g.Target, Is.EqualTo(1));
            Assert.That(g.Update(1, 3.001f, true, 0), Is.EqualTo(ExecutionPrompt.None), "beyond 3 m there is no target");
            Assert.That(g.Target, Is.EqualTo(-1));
            Assert.That(g.Update(-1, 0, true, 0), Is.EqualTo(ExecutionPrompt.None));
            Assert.That(g.Update(2, float.NaN, true, 0), Is.EqualTo(ExecutionPrompt.None), "a garbage distance never makes a prompt");

            // A new instance becoming the target starts disarmed; if it is already out of contact the same snapshot arms it.
            g.Update(5, 2.0f, true, 0);
            Assert.That(g.Armed, Is.True);
            g.Update(6, 1.0f, true, 0);
            Assert.That(g.Armed, Is.False, "a different instance became the nearest");
            g.Update(7, 2.0f, true, 0);
            Assert.That(g.Armed, Is.True, "...but one that is out of contact on arrival is armed by the same snapshot rule");

            // Losing the target and finding it again (a new capture or a reconnect) is a new target.
            g.Update(7, 2.0f, true, 0);
            g.Update(-1, 0, true, 0);
            g.Update(7, 1.0f, true, 0);
            Assert.That(g.Armed, Is.False);
        }

        [Test]
        public void TheContactDistanceIsTheServerCaptureRadiusAndExactlyOnePointTwoIsStillContact()
        {
            Assert.That(ExecutionGuidance.Contact, Is.EqualTo(GameConstants.CaptureRadius));
            Assert.That(GameConstants.CaptureRadius, Is.EqualTo(1.2f));
            var g = new ExecutionGuidance();
            Assert.That(g.Update(3, 1.2f, true, 0), Is.EqualTo(ExecutionPrompt.StepBack), "1.2 m is contact: not armed");
            Assert.That(g.Update(3, 1.21f, true, 0), Is.EqualTo(ExecutionPrompt.Ready));
        }

        [Test]
        public void WithoutSelfStateNoPromptIsShownButNothingThrowsAndTheFlagKeepsTracking()
        {
            var g = new ExecutionGuidance();
            Assert.That(g.Update(3, 2.0f, false, 0), Is.EqualTo(ExecutionPrompt.None));
            Assert.That(g.Update(3, 1.0f, false, 1.5f), Is.EqualTo(ExecutionPrompt.None), "even with a grace value on the wire, no SelfState means no prompt");
            Assert.That(g.Prompt, Is.EqualTo(ExecutionPrompt.None));
            // SelfState arrives: the prompt appears at once.
            Assert.That(g.Update(3, 1.0f, true, 1.5f), Is.EqualTo(ExecutionPrompt.Grace));
            // Garbage grace values are treated as zero.
            Assert.That(g.Update(3, 1.0f, true, float.NaN), Is.EqualTo(ExecutionPrompt.StepBack));
            Assert.That(g.Update(3, 1.0f, true, -2f), Is.EqualTo(ExecutionPrompt.StepBack));
        }

        [Test]
        public void ResetForgetsTheTargetAndTheArmedFlag()
        {
            var g = new ExecutionGuidance();
            g.Update(3, 2.0f, true, 0);
            Assert.That(g.Armed, Is.True);
            g.Reset();
            Assert.That(g.Target, Is.EqualTo(-1));
            Assert.That(g.Armed, Is.False);
            Assert.That(g.Prompt, Is.EqualTo(ExecutionPrompt.None));
        }

        [Test]
        public void TheSelfStateSeamStartsUnavailableCleansWhatItIsFedAndResets()
        {
            var holder = new SelfStateHolder();
            Assert.That(holder.Available, Is.False, "S2C_SelfState does not exist in Shared yet");
            Assert.That(holder.ExecutionGraceLeft, Is.EqualTo(0f));
            holder.Feed(float.NaN, 5f, -3f, float.PositiveInfinity);
            Assert.That(holder.Available, Is.True);
            Assert.That(holder.SkillCooldown, Is.EqualTo(0f));
            Assert.That(holder.RescueProgress, Is.EqualTo(1f), "clamped to 0..1");
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(0f));
            Assert.That(holder.ExecutionGraceLeft, Is.LessThanOrEqualTo(3600f).And.GreaterThanOrEqualTo(0f));
            holder.Reset();
            Assert.That(holder.Available, Is.False);
            Assert.That(holder.SkillCooldown, Is.EqualTo(0f));
            ISelfState seam = holder;
            Assert.That(seam.Available, Is.False);
        }

        [Test]
        public void ThereIsExactlyOneMarkedAdapterPointForTheFutureSelfStatePacket()
        {
            string selfState = QaPaths.ReadScript("World/SelfState.cs");
            Assert.That(selfState, Does.Contain("// U2c: feed from S2C_SelfState"));
            int feeds = 0;
            foreach (string path in QaPaths.AllScripts())
            {
                if (Path.GetFileName(path) == "SelfState.cs") continue;
                feeds += Regex.Matches(QaSource.StripComments(File.ReadAllText(path)), @"\.Feed\(").Count;
            }
            Assert.That(feeds, Is.EqualTo(0), "nothing feeds the holder until S2C_SelfState exists in Shared");
        }

        // ---- 6. terminal and exit markers (code read: the placement lives inside a MonoBehaviour that needs the whole scene) -----

        [Test]
        public void TheMarkerConstantsAreTheSpecNumbers()
        {
            Assert.That(KillerWorldLayer.TerminalMarkerHeight, Is.EqualTo(1.5f), "terminal marker 1.5 m above the terminal");
            Assert.That(KillerWorldLayer.ExitMarkerHeight, Is.EqualTo(2.5f), "exit marker 2.5 m above the exit");
            Assert.That(KillerWorldLayer.TerminalTagOnlyWithin, Is.EqualTo(8f), "in view and within 8 m: tag only");
            Assert.That(KillerWorldLayer.DistanceTagBeyond, Is.EqualTo(20f), "distance tag under in-view Survivors beyond 20 m");
            Assert.That(KillerWorldLayer.IntroSeconds, Is.EqualTo(3f));
            Assert.That(KillerScene.MarkerHeight, Is.EqualTo(1.2f), "blips are classified at 1.2 m");
            Assert.That(KillerScene.EdgeCount, Is.EqualTo(6));
        }

        [Test]
        public void ProgressOneHundredIsDoneAndEveryOtherValueIsNotDoneAndNoNumberIsEverShown()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Killer/KillerWorldLayer.cs"));
            var uses = Regex.Matches(code, @"\.Progress\b([^;]*)");
            Assert.That(uses.Count, Is.GreaterThanOrEqualTo(2), "the done count and the marker loop both read Progress");
            foreach (Match use in uses)
                Assert.That(use.Groups[1].Value.TrimStart(), Does.StartWith("== 100"), "Progress is only ever compared with 100 (0 and 99 are both 'not done'): " + use.Value);
            string statics = QaSource.MethodText(code, "private void DrawStatic(");
            Assert.That(statics, Does.Contain("if (terminal.Progress == 100) continue;"), "a completed terminal gets no marker");
            string done = QaSource.MethodText(code, "private static int HudDone(");
            Assert.That(done, Does.Contain("terminal.Progress == 100"));
            Assert.That(done, Does.Not.Contain(">="), "99 is not done");
        }

        [Test]
        public void NoStringInTheKillerViewCanFormatAPercentage()
        {
            foreach (string relative in new[] { "Killer/KillerWorldLayer.cs", "Killer/KillerHudCopy.cs", "UI/KillerHud.cs", "UI/MarkerLayer.cs", "Killer/EdgePlacement.cs" })
            {
                string code = QaSource.StripComments(QaPaths.ReadScript(relative));
                foreach (string literal in QaSource.StringLiterals(code))
                {
                    Assert.That(literal, Does.Not.Contain("%"), relative + ": \"" + literal + "\"");
                    Assert.That(literal, Does.Not.Match(@"^[Pp]\d*$"), relative + ": a percent format specifier \"" + literal + "\"");
                }
                Assert.That(code, Does.Not.Match(@"Progress\s*\*\s*100|Progress\s*/\s*100|\bProgress\s*\.\s*ToString"), relative + ": a derived percentage");
            }
        }

        [Test]
        public void ATerminalInViewWithinEightMetresDrawsOnlyItsTagAndOtherwiseTheSquareAndTheTag()
        {
            string statics = QaSource.MethodText(QaSource.StripComments(QaPaths.ReadScript("Killer/KillerWorldLayer.cs")), "private void DrawStatic(");
            Assert.That(statics, Does.Contain("bool tagOnly = !blocked && distance <= TerminalTagOnlyWithin;"));
            Assert.That(statics, Does.Contain("if (!tagOnly) markers.Square("), "the square is skipped for tag-only");
            Assert.That(statics, Does.Contain("KillerHudCopy.Terminal(terminal.Id)"), "the label is Terminal N, number = Id + 1");
            Assert.That(statics, Does.Contain("EdgePlacement.ClampOutside("), "an on-screen marker never hides behind the cluster (step 6)");
        }

        [Test]
        public void TheExitMarkerHasThreeStatesAndLockedByTheKillerUsesTheOwnFirewallDuration()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Killer/KillerWorldLayer.cs"));
            string statics = QaSource.MethodText(code, "private void DrawStatic(");
            Assert.That(statics, Does.Match(@"last\.ExitOpen == 0 \? ExitMark\.Locked : last\.ExitLocked != 0 \? ExitMark\.LockedByKiller : ExitMark\.Open"));
            Assert.That(statics, Does.Contain("KillerHudCopy.ExitLockedFor("));
            Assert.That(QaSource.MethodText(code, "public void OnEffect("), Does.Contain("effect.CasterId == playerId"), "seconds come from the Killer's own FirewallBlock effect");
        }

        [Test]
        public void MarkerColoursFollowTheRolesSignalOnlyForSurvivorInfoAndTextOrMutedForTerminalsAndTheExit()
        {
            string layer = QaSource.StripComments(QaPaths.ReadScript("UI/MarkerLayer.cs"));
            foreach (string method in new[] { "private void StrokedSquare(", "private void DrawExit(", "private void Ring(" })
                Assert.That(QaSource.MethodText(layer, method), Does.Not.Contain("Signal"), method + " (terminal, exit and ring marks are never amber)");
            Assert.That(QaSource.MethodText(layer, "private void FilledDiamond("), Does.Contain("Signal"));
            Assert.That(QaSource.MethodText(layer, "private void FilledChevron("), Does.Contain("Signal"));
            string exit = QaSource.MethodText(layer, "private void DrawExit(");
            Assert.That(exit, Does.Match(@"Ring\(painter, p, 8, state == ExitMark\.Locked \? Muted : Text, state == ExitMark\.Locked\)"), "locked = dashed muted, otherwise solid text");
            Assert.That(exit, Does.Contain("state != ExitMark.LockedByKiller"), "the bar across it is only for Locked by the Killer");
            Assert.That(layer, Does.Not.Match(@"3FB8AF|63,\s*184,\s*175"), "no ally teal in the marker layer");
        }

        [Test]
        public void TheKillerHudUsesNoAllyTealAndNoSignalArcAndDangerOnlyOnTheSlowBar()
        {
            foreach (string relative in new[] { "UI/KillerHud.cs", "UI/SkillRing.cs", "Killer/KillerWorldLayer.cs", "UI/MarkerLayer.cs" })
                Assert.That(QaSource.StripComments(QaPaths.ReadScript(relative)), Does.Not.Match(@"3FB8AF|63,\s*184,\s*175"), relative);
            string hud = QaSource.StripComments(QaPaths.ReadScript("UI/KillerHud.cs"));
            Assert.That(QaSource.MethodText(hud, "public void SetSkill("), Does.Match(@"ring\.Set\([^;]*,\s*false\)"), "Killer skills never draw a signal-coloured active arc");
            string uss = File.ReadAllText(Path.Combine(QaPaths.Assets(), "Resources/ClientUI.uss"));
            int killerSection = uss.IndexOf("Killer HUD (HUD_SPEC 6.1)", StringComparison.Ordinal);
            Assert.That(killerSection, Is.GreaterThan(0));
            string killerCss = uss.Substring(killerSection);
            Assert.That(killerCss, Does.Not.Contain("#3FB8AF"), "no ally colour in the Killer HUD styles");
            Assert.That(killerCss, Does.Not.Contain("#FFB020"), "signal is drawn by the marker layer only");
            Assert.That(Regex.Matches(killerCss, "#E5484D").Count, Is.EqualTo(1), "danger appears once: the Traffic-overload bar");
            Assert.That(killerCss, Does.Contain(".kbar.danger { background-color: #E5484D; }"));
        }
    }
}
