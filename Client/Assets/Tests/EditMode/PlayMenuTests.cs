using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    // HUD_SPEC 5 / 6.6: one play menu for both roles. The input gate (look, WASD, E, Q, Space), the Row layout of the
    // Survivor menu, the unchanged Stacked Killer menu, focus order and where focus goes when Settings closes.
    public sealed class PlayMenuTests
    {
        private const double Frame = MovementInput.Interval;

        private static Font HudFont()
        {
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            Assert.That(font, Is.Not.Null);
            return font;
        }

        // ---- gate --------------------------------------------------------------------------------------------------------

        [Test]
        public void OpeningSendsOneZeroAndTheInteractReleaseThenNothingWhileKeysStayHeld()
        {
            var movement = new MovementInput();
            var interact = new InteractInput();
            var gate = new PlayMenuGate();
            Assert.That(movement.TryCreate(true, 0, 1, 0, 0, Frame, out C2S_MoveInput moving), Is.True, "W held before the menu");
            Assert.That(moving.DirZ, Is.GreaterThan(0));
            Assert.That(interact.TryCreate(true, out _), Is.True, "E held before the menu");

            Assert.That(gate.Set(true, 10), Is.True);
            Assert.That(gate.Open, Is.True);
            var sent = new List<IPacket>();
            PlayMenuGate.Stop(movement, interact, 30, sent.Add);
            Assert.That(sent.Count, Is.EqualTo(2), "one zero MoveInput, then the E release");
            var zero = (C2S_MoveInput)sent[0];
            Assert.That(zero.DirX, Is.EqualTo(0f));
            Assert.That(zero.DirZ, Is.EqualTo(0f));
            Assert.That(zero.Yaw, Is.EqualTo(30f));
            Assert.That(zero.Seq, Is.GreaterThan(moving.Seq));
            Assert.That(((C2S_Interact)sent[1]).Active, Is.EqualTo(0));

            // Keys stay physically held for 3 s: the gate turns them into "not held", so nothing more goes out.
            for (int i = 0; i < 90; i++)
            {
                bool active = gate.Active(true, false);
                Assert.That(active, Is.False);
                Assert.That(movement.TryCreate(active && true, 0, 1, 0, 0, Frame, out _), Is.False, "frame " + i);
                Assert.That(interact.TryCreate(active && true, out _), Is.False, "frame " + i);
                Assert.That(gate.Presses(active, 11 + i), Is.False, "no Space or Q press, frame " + i);
            }
        }

        [Test]
        public void OpeningWithoutAHeldInteractSendsOnlyTheZeroAndAStateChangeHappensOnce()
        {
            var gate = new PlayMenuGate();
            var sent = new List<IPacket>();
            Assert.That(gate.Set(true, 1), Is.True);
            PlayMenuGate.Stop(new MovementInput(), new InteractInput(), 0, sent.Add);
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0], Is.InstanceOf<C2S_MoveInput>());
            Assert.That(gate.Set(true, 2), Is.False, "opening twice is not a second change (no second zero)");
            Assert.That(gate.Set(false, 3), Is.True);
            Assert.That(gate.Set(false, 4), Is.False);
        }

        [Test]
        public void AClosedGateLetsMovementAndPressesThroughExceptOnTheFrameTheMenuClosed()
        {
            var movement = new MovementInput();
            var gate = new PlayMenuGate();
            gate.Set(true, 5);
            gate.Set(false, 6);
            bool active = gate.Active(true, false);
            Assert.That(active, Is.True);
            Assert.That(movement.TryCreate(active && true, 0, 1, 0, 0, Frame, out C2S_MoveInput input), Is.True);
            Assert.That(input.DirZ, Is.GreaterThan(0));
            Assert.That(gate.Presses(active, 6), Is.False, "a Submit on Resume closed the menu this frame: its key presses nothing");
            Assert.That(gate.Presses(active, 7), Is.True);
            Assert.That(gate.Active(false, false), Is.False, "focus lost");
            Assert.That(gate.Active(true, true), Is.False, "cursor released after a focus loss");
            gate.Set(true, 8);
            gate.Reset();
            Assert.That(gate.Open, Is.False);
            Assert.That(gate.Presses(true, 8), Is.True, "Reset (leave, result, match start) forgets the closing frame");
        }

        [Test]
        public void TheCameraIgnoresTheMouseWhileTheMenuIsOpenAndFollowsItWhenClosed()
        {
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
            {
                var go = new GameObject("play-menu-test-camera", typeof(Camera));
                try
                {
                    var rig = new RoleCamera(go.GetComponent<Camera>());
                    var gate = new PlayMenuGate();
                    rig.Begin(role);
                    rig.Place(new Vector3(40, 0, 40), .02f);
                    float yaw = rig.Yaw, pitch = rig.Pose.Pitch;
                    gate.Set(true, 1);
                    rig.Look(gate.Active(true, false), new Vector2(50, 20));
                    rig.Place(new Vector3(40, 0, 40), .02f);
                    Assert.That(rig.Yaw, Is.EqualTo(yaw), role + ": yaw unchanged under the open menu");
                    Assert.That(rig.Pose.Pitch, Is.EqualTo(pitch), role + ": pitch unchanged under the open menu");
                    gate.Set(false, 2);
                    rig.Look(gate.Active(true, false), new Vector2(50, 20));
                    rig.Place(new Vector3(40, 0, 40), .02f);
                    Assert.That(rig.Yaw, Is.EqualTo(Mathf.Repeat(yaw + 50 * RoleCamera.Sensitivity, 360)).Within(1e-3f), role + ": look works again");
                    Assert.That(rig.Pose.Pitch, Is.Not.EqualTo(pitch), role + ": pitch follows again");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                    UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true;
                }
            }
        }

        // ---- copy ----------------------------------------------------------------------------------------------------

        [Test]
        public void LineThreeStatesTheCurrentCaptureRuleOnlyWhileNormal()
        {
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 0), Is.Null);
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 1), Is.EqualTo(PlayMenuCopy.CapturedOnce));
            Assert.That(PlayMenuCopy.CapturedOnce, Does.Not.Contain("eliminat"), "GDD v2: the second capture is a 30 s Lagging stage, not an elimination");
            Assert.That(PlayMenuCopy.CapturedOnce, Does.Contain(GameConstants.SecondLaggingTimeoutSeconds.ToString("0") + " s"));
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 2), Is.EqualTo(PlayMenuCopy.CapturedTwice));
            Assert.That(GameConstants.EliminatingCaptureCount, Is.EqualTo(3), "the third capture eliminates");
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Lagging, 1), Is.Null);
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Eliminated, 3), Is.Null);
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Escaped, 2), Is.Null);
        }

        [Test]
        public void EverySurvivorSkillHasItsSpecEffectLineAndItFitsOneLine()
        {
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++)
            {
                string line = PlayMenuCopy.SurvivorEffect(id);
                Assert.That(line, Is.Not.Empty, "skill " + id);
                Assert.That(line.Length, Is.LessThanOrEqualTo(68), "HUD_SPEC 5: the longest is 68 characters (~564 u of 648 u)");
            }
            Assert.That(PlayMenuCopy.SurvivorEffect(SkillIds.WhiteNoise), Is.EqualTo("Five fake Signals across the map, 1.5 s."));
            Assert.That(PlayMenuCopy.SurvivorEffect(SkillIds.StealthRelay), Is.EqualTo("You and Survivors within 5 m stay Silent for 2 s, even while moving."));
        }

        // ---- layouts -------------------------------------------------------------------------------------------------

        private static List<string> Texts(PlayMenu menu)
        {
            var texts = new List<string>();
            foreach (Button b in menu.Buttons) texts.Add(b.text);
            return texts;
        }

        [Test]
        public void TheRowLayoutHasResumeSettingsLeaveMatchInOneRowWithTheSpecSizes()
        {
            var menu = new PlayMenu(PlayMenuLayout.Row, HudFont());
            Assert.That(menu.ClassListContains("smenu"), Is.True);
            Assert.That(menu.ClassListContains("kmenu"), Is.False);
            Assert.That(Texts(menu), Is.EqualTo(new[] { "Resume", "Settings", "Leave match" }));
            Assert.That(menu.Query<Button>().ToList(), Is.EqualTo(menu.Buttons), "DOM order = focus order");
            VisualElement row = menu.Buttons[0].parent;
            Assert.That(row.ClassListContains("smenu-actions"), Is.True);
            foreach (Button b in menu.Buttons)
            {
                Assert.That(b.parent, Is.SameAs(row), "one row");
                Assert.That(b.ClassListContains("smenu-button") && b.ClassListContains("kbutton"), Is.True, "Killer button states, Row size");
            }
            Assert.That(menu.Buttons[0].ClassListContains("primary"), Is.True, "Resume is primary");
            Assert.That(menu.Buttons[1].ClassListContains("primary") || menu.Buttons[2].ClassListContains("primary"), Is.False);
            Assert.That(menu.Open, Is.False, "closed until Esc / Start");
            Assert.That(PlayMenu.RowWidth, Is.EqualTo(SurvivorHud.Width));
            Assert.That(3 * PlayMenu.RowButtonWidth + 2 * PlayMenu.ButtonGap, Is.LessThanOrEqualTo(PlayMenu.RowWidth - 24), "496 <= 648 u inner width");
            Assert.That(12 + 23 + 4 + 18 + 4 + 18 + 4 + PlayMenu.ButtonHeight + 12, Is.LessThanOrEqualTo(PlayMenu.RowHeight), "three lines and the button row fit");

            string uss = File.ReadAllText(Path.Combine(Application.dataPath, "Resources", "ClientUI.uss")).Replace("\r\n", "\n");
            Assert.That(Regex.IsMatch(uss, @"\.root \.smenu \{[^}]*width: 672px; height: 152px; padding: 12px;"), Is.True, "672x152 block, padding 12");
            Assert.That(Regex.IsMatch(uss, @"\.root \.smenu \.unity-button\.kbutton\.smenu-button \{ width: 160px; min-width: 0; height: 40px; margin: 0; margin-right: 8px; \}"), Is.True, "160x40 buttons, gap 8");
            Assert.That(uss, Does.Contain(".root .smenu .unity-button.kbutton.smenu-button.klast { margin-right: 0; }"));
        }

        [Test]
        public void TheKillerMenuKeepsItsStackedBlockClassesAndApi()
        {
            var hud = new KillerHud(new VisualElement(), HudFont());
            PlayMenu menu = hud.Menu;
            Assert.That(menu.Layout, Is.EqualTo(PlayMenuLayout.Stacked));
            Assert.That(menu.ClassListContains("kblock") && menu.ClassListContains("kmenu"), Is.True);
            Assert.That(menu.ClassListContains("smenu"), Is.False);
            Assert.That(Texts(menu), Is.EqualTo(new[] { "Resume", "Settings", "Leave match" }));
            foreach (Button b in menu.Buttons)
            {
                Assert.That(b.parent, Is.SameAs(menu), "stacked directly in the block");
                Assert.That(b.ClassListContains("kbutton"), Is.True);
                Assert.That(b.ClassListContains("smenu-button"), Is.False);
            }
            Assert.That(menu.Buttons[0].ClassListContains("primary"), Is.True);
            Assert.That(menu.Buttons[2].ClassListContains("klast"), Is.True);
            Assert.That(menu.Query<Label>().ToList().Count, Is.EqualTo(2), "skill name and effect only, no line 3");
            Assert.That(KillerHud.MenuHeight, Is.EqualTo(256f));
            int resumes = 0, settings = 0, leaves = 0;
            hud.Resume += () => resumes++; hud.OpenSettings += () => settings++; hud.LeaveMatch += () => leaves++;
            hud.Begin(SkillIds.Overclock);
            Assert.That(menu.SkillText, Is.EqualTo("Overclock"));
            Assert.That(menu.PressLeave(), Is.False, "Leave needs the open menu");
            hud.SetMenu(true);
            Assert.That(menu.PressLeave(), Is.True);
            Assert.That(leaves, Is.EqualTo(1));
            Assert.That(resumes + settings, Is.EqualTo(0));
        }

        // ---- Survivor HUD slot -----------------------------------------------------------------------------------------

        [Test]
        public void TheSurvivorMenuTakesThePromptSlotAndThePromptAndHoldRowsStayHidden()
        {
            var hud = new SurvivorHud(new VisualElement(), HudFont());
            hud.Begin(SkillIds.WhiteNoise);
            var prompt = new PromptState { Text = "Hold [E] to hack Terminal 2" };
            hud.SetPrompt(prompt, default);
            Assert.That(hud.PromptText, Is.EqualTo(prompt.Text));
            Rect normal = hud.ClusterRect(new Vector2(1600, 900));

            hud.SetMenu(true);
            Assert.That(hud.MenuOpen, Is.True);
            Assert.That(hud.Menu.Layout, Is.EqualTo(PlayMenuLayout.Row));
            Assert.That(hud.Menu.SkillText, Is.EqualTo("White noise"));
            Assert.That(hud.Menu.EffectText, Is.EqualTo(PlayMenuCopy.SurvivorEffect(SkillIds.WhiteNoise)));
            Assert.That(hud.PromptText, Is.Null, "the menu replaces the prompt row");
            for (int i = 0; i < 3; i++)
            {
                hud.SetPrompt(prompt, default);
                Assert.That(hud.PromptText, Is.Null, "the per-frame prompt cannot come back over the menu");
                hud.SetPrompt(prompt, new HoldActionView { Kind = HoldKind.ExitSwitch, Progress01 = .4f });
                Assert.That(hud.HoldText, Is.Null, "nor the hold row");
                hud.SetPrompt(prompt);
                Assert.That(hud.PromptText, Is.Null);
            }
            Rect open = hud.ClusterRect(new Vector2(1600, 900));
            Assert.That(open.height - normal.height, Is.EqualTo(PlayMenu.RowHeight - SurvivorHud.PromptHeight), "152 u slot instead of the 40 u row");
            Assert.That(open.yMax, Is.EqualTo(normal.yMax), "same bottom-centre anchor, it grows upward");

            Assert.That(hud.Menu.NoteText, Is.Null);
            hud.SetMenuNote(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 2));
            Assert.That(hud.Menu.NoteText, Is.EqualTo(PlayMenuCopy.CapturedTwice));
            hud.SetMenuNote(null);
            Assert.That(hud.Menu.NoteText, Is.Null);

            hud.SetMenu(false);
            hud.SetPrompt(prompt, default);
            Assert.That(hud.PromptText, Is.EqualTo(prompt.Text), "closed: the prompt row is back");
            hud.SetMenu(true);
            hud.Begin(SkillIds.DummyPacket);
            Assert.That(hud.MenuOpen, Is.False, "a new match starts with the menu closed");
        }

        // ---- focus (runtime panel) -----------------------------------------------------------------------------------

        private static void Send(VisualElement target, EventBase evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }

        private static void Move(VisualElement target, NavigationMoveEvent.Direction direction)
            => Send(target, NavigationMoveEvent.GetPooled(direction));

        [Test]
        public void ResumeTakesFocusAndArrowsAndTabWalkResumeSettingsLeaveMatch()
        {
            var go = new GameObject("play-menu-test-document");
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            try
            {
                var document = go.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                VisualElement root = document.rootVisualElement;
                var menu = new PlayMenu(PlayMenuLayout.Row, HudFont());
                root.Add(menu);
                FocusController focus = root.panel.focusController;
                menu.SetOpen(true);
                Button resume = menu.Buttons[0], settings = menu.Buttons[1], leave = menu.Buttons[2];
                Assert.That(focus.focusedElement, Is.SameAs(resume), "Resume focused on open");
                Move(resume, NavigationMoveEvent.Direction.Right);
                Assert.That(focus.focusedElement, Is.SameAs(settings));
                Move(settings, NavigationMoveEvent.Direction.Down);
                Assert.That(focus.focusedElement, Is.SameAs(leave));
                Move(leave, NavigationMoveEvent.Direction.Right);
                Assert.That(focus.focusedElement, Is.SameAs(leave), "arrows stop at the last button");
                Move(leave, NavigationMoveEvent.Direction.Next);
                Assert.That(focus.focusedElement, Is.SameAs(resume), "Tab wraps inside the menu");
                Move(resume, NavigationMoveEvent.Direction.Previous);
                Assert.That(focus.focusedElement, Is.SameAs(leave));
                Move(leave, NavigationMoveEvent.Direction.Left);
                Assert.That(focus.focusedElement, Is.SameAs(settings));
                Move(settings, NavigationMoveEvent.Direction.Up);
                Assert.That(focus.focusedElement, Is.SameAs(resume));

                int resumed = 0;
                menu.Resume += () => resumed++;
                Send(resume, NavigationSubmitEvent.GetPooled());
                Assert.That(resumed, Is.EqualTo(1), "Enter / Space / A on Resume");

                settings.Focus();
                menu.SetOpen(false);
                Assert.That(focus.focusedElement, Is.Not.SameAs(settings), "a closed menu keeps no focused button");
                Assert.That(menu.Open, Is.False);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(panelSettings); }
        }

        [Test]
        public void ClosingSettingsFocusesTheOpenRolesMenu()
        {
            var go = new GameObject("play-menu-settings-document");
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            try
            {
                var document = go.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                VisualElement root = document.rootVisualElement;
                Font font = HudFont();
                var survivor = new SurvivorHud(root, font);
                var killer = new KillerHud(root, font);
                var elsewhere = new Button { text = "Done" }; // stands in for the Settings screen's focus
                root.Add(elsewhere);
                FocusController focus = root.panel.focusController;
                Assert.That(ClientScreens.OpenPlayMenu(killer, survivor), Is.Null, "no menu open");

                survivor.Begin(SkillIds.DummyPacket);
                survivor.SetMenu(true);
                elsewhere.Focus();
                PlayMenu open = ClientScreens.OpenPlayMenu(killer, survivor);
                Assert.That(open, Is.SameAs(survivor.Menu), "the Survivor's menu, not the Killer's");
                open.FocusSettings();
                Assert.That(focus.focusedElement, Is.SameAs(survivor.Menu.Buttons[1]), "back on the Survivor menu's Settings");
                survivor.SetMenu(false);

                survivor.SetVisible(false);
                killer.Begin(SkillIds.PingScanner);
                killer.SetMenu(true);
                elsewhere.Focus();
                Assert.That(ClientScreens.OpenPlayMenu(killer, survivor), Is.SameAs(killer.Menu));
                killer.FocusMenu();
                Assert.That(focus.focusedElement, Is.SameAs(killer.Menu.Buttons[1]), "back on the Killer menu's Settings");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(panelSettings); }
        }
    }
}
