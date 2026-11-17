using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // U6c-wire: protocol v0.4 -> the rules v2 HUD (HUD_SPEC 13). The own route from SelfState (11/17, R2), the SelfState v0.4 fields, exit and
    // terminal-flag mapping, the result rows, the absence of execution copy, the dev flags and the autopilot.
    public sealed class RulesV2WiringTests
    {
        private const string Dot = "·";

        private static S2C_SelfState Self(byte id, byte dirs, byte index)
            => new S2C_SelfState { SkillCooldown = 0, CollapseLeft = -1, RouteId = id, RouteLength = id == 0 ? (byte)0 : (byte)HackRoute.MaxLength, RouteDirs = dirs, RouteIndex = index };

        // ---- 13.2 packet routing (11/17, R2) ------------------------------------------------------------------------------

        [Test]
        public void TheOwnRouteComesFromSelfStateAndOnlyANewIdCountsAsNew()
        {
            var route = new RouteTracker();
            byte dirs = HackRoute.WithDirection(HackRoute.WithDirection(0, 0, RouteDirection.Right), 1, RouteDirection.Down);
            Assert.That(route.OnSelf(Self(7, dirs, 0), 1), Is.True);
            Assert.That(route.Present, Is.True);
            Assert.That(route.Expected, Is.EqualTo(RouteDirection.Right));
            Assert.That(route.OnSelf(Self(7, dirs, 1), 1.05), Is.False, "the same route, the server's index");
            Assert.That(route.Index, Is.EqualTo(1));
            Assert.That(route.Expected, Is.EqualTo(RouteDirection.Down));
            Assert.That(route.OnSelf(Self(0, 0, 0), 1.1), Is.True, "cleared: the gap after a route or a done terminal");
            Assert.That(route.Held, Is.False);
            Assert.That(route.OnSelf(Self(0, 0, 0), 1.15), Is.False);
        }

        [Test]
        public void TheSurvivorHudShowsTheRoutePanelAboveThePromptRow()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                Assert.That(hud.RoutePanel.style.display.value, Is.EqualTo(DisplayStyle.None));
                hud.Route.OnSelf(Self(1, 0, 0), 0);
                hud.SetRoute(true, RouteCopy.KeyboardGlyph, false, .2);
                Assert.That(hud.RoutePanel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(hud.RoutePanel.LabelText, Is.EqualTo(RouteCopy.Hint), "the first route of the match");
                Rect withPanel = hud.ClusterRect(new Vector2(1600, 900));
                Assert.That(withPanel.height, Is.EqualTo(SurvivorHud.Height + RoutePanel.Height + 8), "the panel and its 8 u gap join the keep-out");
                hud.SetRoute(false, RouteCopy.KeyboardGlyph, false, .3);
                Assert.That(hud.RoutePanel.style.display.value, Is.EqualTo(DisplayStyle.None), "E released: the panel hides at once");
                hud.Begin(SkillIds.DummyPacket);
                Assert.That(hud.Route.Held, Is.False, "a new match starts without a route");
            }
            finally { hud.Dispose(); }
        }

        // ---- SelfState v0.4 -> hold and collapse ---------------------------------------------------------------------------

        [Test]
        public void SelfStateV04FieldsReachTheHoldAndCollapseViewModels()
        {
            var self = new SelfStateHolder();
            self.Feed(0, 0, 0, 48f, .62f, (byte)ActionKind.Switch, 10);
            HoldActionView hold = V2Wire.Hold(self);
            Assert.That(hold.Kind, Is.EqualTo(HoldKind.ExitSwitch));
            Assert.That(HoldCopy.Label(hold), Is.EqualTo("Opening the Exit " + Dot + " 62% — you are Signal"));
            CollapseView collapse = V2Wire.Collapse(112f, self.CollapseLeft, true);
            Assert.That(CollapseCopy.Main(collapse), Is.EqualTo("Shutdown 1:52"));
            Assert.That(CollapseCopy.Personal(collapse), Is.EqualTo("Lagging: 2× faster " + Dot + " you 0:48"));
            Assert.That(CollapseCopy.Urgent(collapse), Is.False);
            self.CollapseRate = 2;
            self.Advance(19.5);
            Assert.That(V2Wire.Collapse(103f, self.CollapseLeft, true).PersonalLeft, Is.EqualTo(29f).Within(1e-3f), "48 - 2 x 9.5");
            Assert.That(CollapseCopy.Urgent(V2Wire.Collapse(103f, self.CollapseLeft, true)), Is.True, "the personal clock decides");
            Assert.That(V2Wire.Collapse(-1, -1, false).Running, Is.False);
            Assert.That(V2Wire.Collapse(float.NaN, float.NaN, false).Running, Is.False);
        }

        [Test]
        public void TheSurvivorHoldRowReplacesThePromptRowAndInfoLineThreeIsGone()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                hud.SetInfo(300, 1, 5);
                var prompt = new PromptState { Text = "Hold [E] to open Exit 1 " + Dot + " 0% — you will be Signal" };
                hud.SetPrompt(prompt, default);
                Assert.That(hud.PromptText, Is.EqualTo(prompt.Text));
                Assert.That(hud.HoldText, Is.Null);
                hud.SetPrompt(prompt, new HoldActionView { Kind = HoldKind.ExitSwitch, Progress01 = .4f });
                Assert.That(hud.PromptText, Is.Null);
                Assert.That(hud.HoldText, Is.EqualTo("Opening the Exit " + Dot + " 40% — you are Signal"));
                Assert.That(hud.ClusterRect(new Vector2(1600, 900)).height, Is.EqualTo(SurvivorHud.Height + 48), "one row either way");
                foreach (Label label in hud.Root.Query<Label>().ToList())
                    Assert.That(label.text, Does.Not.StartWith("Exit Locked").And.Not.StartWith("Exit Open"), "no v1 info line 3");
            }
            finally { hud.Dispose(); }
        }

        // ---- 13.4 / 13.6 mapping from a WorldState ---------------------------------------------------------------------------

        private static S2C_WorldState World()
        {
            var world = new S2C_WorldState { CollapseLeft = 95.2f };
            world.Exits.Add(new ExitState { Id = 0, X = 4, Z = 4, Flags = 1, SwitchProgress = 62 });
            world.Exits.Add(new ExitState { Id = 1, X = 76, Z = 76, Flags = 1 | 2, SwitchProgress = 100 });
            world.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 10, Progress = 40, Flags = 1 });
            world.Terminals.Add(new TerminalState { Id = 1, X = 20, Z = 10, Progress = 15, Flags = 2 });
            world.Terminals.Add(new TerminalState { Id = 2, X = 30, Z = 10, Progress = 100, Flags = 0 });
            return world;
        }

        [Test]
        public void ExitsAndTerminalFlagsMapFromAWorldState()
        {
            S2C_WorldState world = World();
            ExitView[] exits = V2Wire.Exits(world);
            Assert.That(exits.Length, Is.EqualTo(2));
            Assert.That(ExitCopy.Tag(exits[0]), Is.EqualTo("Exit 1 " + Dot + " 62%"));
            Assert.That(ExitCopy.Tag(exits[1]), Is.EqualTo("Exit 2 " + Dot + " Open"));
            Assert.That(ObjectiveRow.Applies(exits, V2Wire.Collapse(world.CollapseLeft, -1, false)), Is.True);
            TerminalFlagsView regressing = V2Wire.Terminal(world.Terminals[0]);
            Assert.That(regressing.Regressing, Is.True);
            Assert.That(TerminalFlagCopy.Regressing(regressing, Role.Survivor), Is.EqualTo("Terminal 1 " + Dot + " 40% " + Dot + " Rolling back"));
            TerminalFlagsView overloaded = V2Wire.Terminal(world.Terminals[1]);
            Assert.That(overloaded.Overloaded && !overloaded.Regressing, Is.True);
            Assert.That(V2Wire.Terminal(world.Terminals[2]).Regressing || V2Wire.Terminal(world.Terminals[2]).Overloaded, Is.False);
            Assert.That(V2Wire.SwitchInProgress(world), Is.True, "exit 1 at 62% and not open");
            var dark = new S2C_WorldState();
            dark.Exits.Add(new ExitState { Id = 0, Flags = 0 });
            dark.Exits.Add(new ExitState { Id = 1, Flags = 0 });
            Assert.That(ObjectiveRow.Applies(V2Wire.Exits(dark), V2Wire.Collapse(dark.CollapseLeft, -1, false)), Is.False, "act 1: the row hides");
        }

        [Test]
        public void TheObjectiveRowShowsBothExitsAndTheCollapseCell()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                Assert.That(hud.ObjectiveShown, Is.False);
                S2C_WorldState world = World();
                hud.SetObjective(V2Wire.Exits(world), V2Wire.Collapse(world.CollapseLeft, 30, false));
                Assert.That(hud.ObjectiveShown, Is.True);
                Assert.That(hud.Objective.Exits[0].Text, Is.EqualTo("Exit 1 62%"));
                Assert.That(hud.Objective.Exits[1].Text, Is.EqualTo("Exit 2 Open"));
                Assert.That(hud.Objective.Collapse.Main, Is.EqualTo("Shutdown 1:36"));
                Assert.That(hud.Objective.Collapse.Personal, Is.EqualTo("You 0:30"));
                Assert.That(hud.Objective.Collapse.Urgent, Is.True);
            }
            finally { hud.Dispose(); }
        }

        // ---- 13.3 Survivor interact prompts ----------------------------------------------------------------------------------

        [Test]
        public void TheSwitchAndHatchPromptsShowInReach()
        {
            var switches = new List<MapPoint> { new MapPoint(7, 7), new MapPoint(73, 73) };
            ExitView[] exits = V2Wire.Exits(World());
            InteractPrompt atSwitch = InteractCopy.Survivor(7.5f, 7, exits, switches, false, 0, 0);
            Assert.That(atSwitch.Text, Is.EqualTo("Hold [E] to open Exit 1 " + Dot + " 62% — you will be Signal"));
            Assert.That(InteractCopy.Survivor(73, 73, exits, switches, false, 0, 0).Text, Is.Null, "an open exit's switch offers nothing");
            Assert.That(InteractCopy.Survivor(7, 9, exits, switches, false, 0, 0).Text, Is.Null, "2 m is out of the 1.5 m reach");
            InteractPrompt hatch = InteractCopy.Survivor(7.5f, 7, exits, switches, true, 8, 7);
            Assert.That(hatch.Text, Is.EqualTo("Press [E] to escape through the Emergency port"), "the hatch comes first");
            Assert.That(InteractCopy.Survivor(7.5f, 7, exits, switches, true, 8, 9).Text, Does.StartWith("Hold [E] to open"), "a hatch 2 m away is out of reach");
            ExitView[] locked = { ExitView.FromWire(0, 1 | 4, 30) };
            locked[0].LockedLeft = 2.2f;
            InteractPrompt blocked = InteractCopy.Survivor(7, 7, locked, switches, false, 0, 0);
            Assert.That(blocked.Text, Is.EqualTo("Exit 1 is Locked 3 s by the Killer"));
            Assert.That(blocked.Muted, Is.True);
            ExitView[] unpowered = { ExitView.FromWire(0, 0, 0) };
            Assert.That(InteractCopy.Survivor(7, 7, unpowered, switches, false, 0, 0).Text, Is.Null, "no power: no switch prompt");
        }

        // ---- 13.7 result rows ----------------------------------------------------------------------------------------------

        private static S2C_LobbyState SevenSurvivorsAndAKiller()
        {
            var lobby = new S2C_LobbyState();
            string[] names = { "Killer", "Mina", "Jae", "Rio", "Hana", "Theo", "Sora", "Ben" };
            for (int i = 0; i < 8; i++) lobby.Slots[i] = new Slot { PlayerId = i + 1, Nickname = names[i] };
            return lobby;
        }

        [Test]
        public void ThreeOfSevenEscapedIsAKillerWinWithSevenRowsInSlotOrder()
        {
            var result = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.Collapse };
            result.EscapedIds.AddRange(new[] { 3, 5, 7 });
            result.EliminatedIds.AddRange(new[] { 8, 2, 4, 6 });
            ResultV2View view = V2Wire.Result(result, SevenSurvivorsAndAKiller(), 2, Role.Survivor);
            Assert.That(view.Rows.Count, Is.EqualTo(7), "the Killer has no row");
            Assert.That(view.Rows[0].PlayerId, Is.EqualTo(2));
            Assert.That(view.Rows[6].PlayerId, Is.EqualTo(8), "lobby slot order, not list order");
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Killer wins"));
            Assert.That(ResultV2Copy.Outcome(view), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Rule(view.Escaped, view.Starting), Is.EqualTo("3 of 7 escaped " + Dot + " Survivors win when half or more escape (4 of 7)"));
            Assert.That(ResultV2Copy.Reason(view.Reason), Is.EqualTo("The shutdown eliminated the Survivors still inside."));
            Assert.That(ResultV2Copy.RowName(view.Rows[0], 0), Is.EqualTo("Mina (you)"));
            Assert.That(ResultV2Copy.RowState(view.Rows[1]), Is.EqualTo("Escaped"));
            Assert.That(ResultV2Copy.Consistent(view), Is.True);
        }

        [Test]
        public void FourOfSevenEscapedIsASurvivorWinAndThePanelShowsIt()
        {
            var result = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, Reason = (byte)GameEndReason.AllResolved };
            result.EscapedIds.AddRange(new[] { 2, 4, 6, 8 });
            result.EliminatedIds.AddRange(new[] { 3, 5, 7 });
            ResultV2View view = V2Wire.Result(result, SevenSurvivorsAndAKiller(), 1, Role.Killer);
            Assert.That(view.Escaped, Is.EqualTo(4));
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Survivors win"));
            Assert.That(ResultV2Copy.Outcome(view), Is.EqualTo("You eliminated 3 of 7 Survivors."));
            Assert.That(ResultV2Copy.Consistent(view), Is.True);
            var panel = new ResultV2Panel(Resources.Load<Font>("Fonts/Inter-Regular"));
            panel.Set(view);
            Assert.That(panel.Headline, Is.EqualTo("Survivors win"));
            Assert.That(panel.Rule, Does.StartWith("4 of 7 escaped"));
            Assert.That(panel.RowCount, Is.EqualTo(7));
        }

        [Test]
        public void ThePreviewResultFillsBothListsByTheMajorityRule()
        {
            var ids = new List<int> { 4, 5, 6, 7, 2, 3, 8 };
            var win = new S2C_GameResult { WinnerRole = (byte)Role.Survivor };
            ClientApp.PreviewRows(win, ids, true, 2);
            Assert.That(win.EscapedIds.Count, Is.EqualTo(4));
            Assert.That(win.EscapedIds[0], Is.EqualTo(2), "the own Survivor first");
            Assert.That(win.EliminatedIds.Count, Is.EqualTo(3));
            var loss = new S2C_GameResult { WinnerRole = (byte)Role.Killer };
            ClientApp.PreviewRows(loss, ids, false, -1);
            Assert.That(loss.EscapedIds.Count, Is.EqualTo(3));
            Assert.That(loss.EliminatedIds.Count, Is.EqualTo(4));
        }

        // ---- no execution copy remains (GDD v2) ------------------------------------------------------------------------------

        [Test]
        public void NoExecutionCopyOrCodeRemainsInTheClient()
        {
            string scripts = Path.Combine(Application.dataPath, "Scripts");
            Assert.That(File.Exists(Path.Combine(scripts, "Killer", "ExecutionGuidance.cs")), Is.False);
            foreach (string path in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string code = File.ReadAllText(path);
                string name = Path.GetFileName(path);
                Assert.That(code, Does.Not.Contain("ExecutionGraceLeft"), name);
                Assert.That(code, Does.Not.Contain("ExecutionPrompt"), name);
                Assert.That(code, Does.Not.Contain("touch again").IgnoreCase, name);
                Assert.That(Regex.IsMatch(code, "\"[^\"\\n]*Executed[^\"\\n]*\""), Is.False, name + ": an 'Executed' string");
                Assert.That(code, Does.Not.Contain("reached the Exit"), name);
                Assert.That(code, Does.Not.Contain("reaches the Exit"), name);
            }
        }

        // ---- dev flags and the autopilot --------------------------------------------------------------------------------------

        [Test]
        public void TheNewDevFlagsParseAndStayOffByDefault()
        {
            DevArgs none = DevArgs.Parse(new[] { "PingTheKiller.exe" });
            Assert.That(none.DevHack, Is.EqualTo(0f));
            Assert.That(none.DevSwitch, Is.False);
            Assert.That(none.AutoRoute, Is.EqualTo(AutoRoute.None));
            DevArgs args = DevArgs.Parse(new[] { "x", "-ptk-dev-hack", "40", "-ptk-auto-route", "FAST", "-ptk-dev-switch" });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Active, Is.True);
            Assert.That(args.DevHack, Is.EqualTo(40f));
            Assert.That(args.AutoRoute, Is.EqualTo(AutoRoute.Fast));
            Assert.That(args.DevSwitch, Is.True);
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-route", "slow" }).AutoRoute, Is.EqualTo(AutoRoute.Slow));
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-route", "sloppy" }).AutoRoute, Is.EqualTo(AutoRoute.Sloppy));
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-route", "sloppy" }).Active, Is.True);
            DevArgs bad = DevArgs.Parse(new[] { "x", "-ptk-dev-hack", "-3", "-ptk-auto-route", "perfect", "-ptk-dev-hack" });
            Assert.That(bad.Errors.Count, Is.EqualTo(3));
            Assert.That(bad.DevHack, Is.EqualTo(0f));
            Assert.That(bad.AutoRoute, Is.EqualTo(AutoRoute.None));
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-sync", "great" }).Errors, Is.Not.Empty, "the sync flag is retired");
            DevArgs walk = DevArgs.Parse(new[] { "x", "-ptk-dev-hack", "8", "-ptk-walk-after-hack", "6" });
            Assert.That(walk.Errors, Is.Empty);
            Assert.That(walk.WalkAfterHack, Is.EqualTo(6f));
            Assert.That(none.WalkAfterHack, Is.EqualTo(0f));
        }

        [Test]
        public void AutoRoutePacesKeysAndSloppyMissesEveryFifth()
        {
            Assert.That(HackPilot.RouteKeySeconds(AutoRoute.Fast), Is.EqualTo(.15f));
            Assert.That(HackPilot.RouteKeySeconds(AutoRoute.Sloppy), Is.EqualTo(.15f));
            Assert.That(HackPilot.RouteKeySeconds(AutoRoute.Slow), Is.EqualTo(.5f));
            int wrong = 0;
            for (int n = 1; n <= 100; n++)
            {
                RouteDirection expected = (RouteDirection)(n % 4);
                Assert.That(HackPilot.RouteKey(AutoRoute.Fast, n, expected), Is.EqualTo(expected));
                Assert.That(HackPilot.RouteKey(AutoRoute.Slow, n, expected), Is.EqualTo(expected));
                if (HackPilot.RouteKey(AutoRoute.Sloppy, n, expected) != expected) wrong++;
            }
            Assert.That(wrong, Is.EqualTo(20), "20 % errors");
        }

        [Test]
        public void TheAutopilotWalksToTheNearestUnfinishedTerminalAndHoldsThere()
        {
            var pilot = new HackPilot(1f, false);
            S2C_WorldState world = World();
            world.Exits.Clear();
            pilot.Update(26, 10, true, world, null, .1f);
            Assert.That(pilot.Phase, Is.EqualTo(PilotPhase.ToTerminal), "terminal 3 is done; terminal 2 at 6 m is the nearest unfinished");
            Assert.That(pilot.Goal.x, Is.EqualTo(20f));
            Assert.That(pilot.Walk && !pilot.Hold, Is.True);
            pilot.Update(20.8f, 10, true, world, null, .1f);
            Assert.That(pilot.Phase, Is.EqualTo(PilotPhase.Hacking));
            Assert.That(pilot.Hold && !pilot.Walk, Is.True);
            pilot.Update(21.3f, 10, true, world, null, .5f);
            Assert.That(pilot.Hold, Is.True, "1.3 m still holds once arrived");
            pilot.Update(20.8f, 10, false, world, null, .5f);
            Assert.That(pilot.Hold || pilot.Walk, Is.False, "not Normal: nothing");
            pilot.Update(20.8f, 10, true, world, null, .5f);
            Assert.That(pilot.Held, Is.GreaterThanOrEqualTo(1f));
            pilot.Update(20.8f, 10, true, world, null, .5f);
            Assert.That(pilot.Phase, Is.EqualTo(PilotPhase.Idle), "the hack seconds are used up");
        }

        [Test]
        public void TheSwitchAutopilotHoldsTheNearestUnopenedSwitchThenWalksIntoTheOpenExit()
        {
            var switches = new List<MapPoint> { new MapPoint(7, 7), new MapPoint(73, 73) };
            var pilot = new HackPilot(0, true);
            var world = new S2C_WorldState();
            world.Exits.Add(new ExitState { Id = 0, X = 4, Z = 4, Flags = 1 });
            world.Exits.Add(new ExitState { Id = 1, X = 76, Z = 76, Flags = 1 });
            pilot.Update(20, 20, true, world, switches, .1f);
            Assert.That(pilot.Phase, Is.EqualTo(PilotPhase.ToSwitch));
            Assert.That(pilot.Goal.x, Is.EqualTo(7f));
            pilot.Update(7.5f, 7, true, world, switches, .1f);
            Assert.That(pilot.Phase, Is.EqualTo(PilotPhase.Switching));
            Assert.That(pilot.Hold, Is.True);
            world.Exits[0] = new ExitState { Id = 0, X = 4, Z = 4, Flags = 3, SwitchProgress = 100 };
            pilot.Update(7.5f, 7, true, world, switches, .1f);
            Assert.That(pilot.Phase, Is.EqualTo(PilotPhase.ToExit));
            Assert.That(pilot.Goal.x, Is.EqualTo(4f));
            Assert.That(pilot.Walk && !pilot.Hold, Is.True);
        }

        // ---- B1: the threat strip stacks its noise from the bottom --------------------------------------------------------

        [Test]
        public void TheThreatStripKeepsItsDensityButStacksEachColumnFromTheBottom()
        {
            const int w = 48, h = 4;
            bool[] mask = ThreatVisual.Mask(21, w, h, ThreatVisual.StripDensity);
            bool[] stacked = SurvivorHud.Columns(mask, w, h);
            int before = 0, after = 0;
            for (int i = 0; i < mask.Length; i++) { if (mask[i]) before++; if (stacked[i]) after++; }
            Assert.That(after, Is.EqualTo(before), "same cell count, same 45 % density");
            for (int x = 0; x < w; x++)
                for (int y = 1; y < h; y++)
                    Assert.That(!stacked[y * w + x] || stacked[(y - 1) * w + x], Is.True, "no gap under a set cell (column " + x + ")");
        }

        // ---- attempt 2: S4 Survivor Regressing tags through the HUD path --------------------------------------------------

        private static PingTheKiller.Client.World.ViewPose LookingNorthFrom(float x, float z)
            => new PingTheKiller.Client.World.ViewPose(new Vector3(x, 1.5f, z), 0, 0, 60, 16f / 9);

        [Test]
        public void ARegressingTerminalOnScreenGetsTheSurvivorTagThroughTheHudPath()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                var world = new S2C_WorldState();
                world.Terminals.Add(new TerminalState { Id = 2, X = 0, Z = 10, Progress = 40, Flags = 1 });
                world.Terminals.Add(new TerminalState { Id = 3, X = 1, Z = 12, Progress = 70, Flags = 0 });
                world.Terminals.Add(new TerminalState { Id = 4, X = 0, Z = -10, Progress = 20, Flags = 1 });
                world.Terminals.Add(new TerminalState { Id = 5, X = 60, Z = 11, Progress = 30, Flags = 1 | 2 });
                hud.DrawTerminalFlags(world, LookingNorthFrom(0, 0), new Vector2(1600, 900));
                Assert.That(hud.RegressingShown, Is.EqualTo(1), "only the Regressing terminal in front: not the plain one, not the one behind, not the off-screen one");
                Assert.That(hud.V2Markers.Tags, Is.EqualTo(new[] { "Terminal 3 " + Dot + " 40% " + Dot + " Rolling back" }));
                world.Terminals[0] = new TerminalState { Id = 2, X = 0, Z = 10, Progress = 38, Flags = 1 };
                hud.DrawTerminalFlags(world, LookingNorthFrom(0, 0), new Vector2(1600, 900));
                Assert.That(hud.V2Markers.Tags[0], Does.Contain("38%"), "the percentage follows the snapshot");
                world.Terminals[0] = new TerminalState { Id = 2, X = 0, Z = 10, Progress = 38, Flags = 0 };
                hud.DrawTerminalFlags(world, LookingNorthFrom(0, 0), new Vector2(1600, 900));
                Assert.That(hud.RegressingShown, Is.EqualTo(0));
                Assert.That(hud.V2Markers.Tags, Is.Empty, "the tag goes when the flag clears");
                hud.DrawTerminalFlags(null, LookingNorthFrom(0, 0), new Vector2(1600, 900));
                Assert.That(hud.V2Markers.Tags, Is.Empty);
            }
            finally { hud.Dispose(); }
        }

        [Test]
        public void ClientAppDrawsTheSurvivorTerminalFlagsEveryFrameFromTheLastWorldState()
        {
            string app = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "ClientApp.cs")).Replace("\r\n", "\n");
            Assert.That(app, Does.Contain("UpdateSurvivorHud(now, introOn);\n                screens.Survivor.DrawTerminalFlags(lastWorld, rig.Pose);"));
        }

        // ---- attempt 2: the capture banner names the stage's seconds ------------------------------------------------------

        [Test]
        public void TheCaptureBannerSays60sOnTheFirstStageAnd30sOnTheSecond()
        {
            Assert.That(CaptureWatch.Banner, Does.EndWith(Dot + " 60 s left"));
            Assert.That(CaptureWatch.SecondBanner, Does.EndWith(Dot + " 30 s left"));
            Assert.That(CaptureWatch.BannerFor(1), Is.SameAs(CaptureWatch.Banner));
            Assert.That(CaptureWatch.BannerFor(2), Is.SameAs(CaptureWatch.SecondBanner));
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 0);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 1);
            Assert.That(watch.BannerVisible(1.5), Is.True);
            Assert.That(watch.Prompt.Text, Is.EqualTo(CaptureWatch.Banner));
            watch.Observe(PlayerStatus.Normal, 0, 1, 10);
            watch.Observe(PlayerStatus.Lagging, 30, 2, 20);
            Assert.That(watch.BannerVisible(21), Is.True);
            Assert.That(watch.Prompt.Text, Is.EqualTo(CaptureWatch.SecondBanner), "the second capture: 30 s");
            Assert.That(watch.Prompt.Danger, Is.True);
        }

        [Test]
        public void TheFirstStageRunningOutShowsTheBannerAgainWithThirtySeconds()
        {
            var watch = new CaptureWatch();
            watch.Observe(PlayerStatus.Normal, 0, 0, 0);
            watch.Observe(PlayerStatus.Lagging, 60, 1, 1);
            watch.Observe(PlayerStatus.Lagging, .1f, 1, 60.9);
            Assert.That(watch.BannerVisible(60.9), Is.False);
            watch.Observe(PlayerStatus.Lagging, 30, 2, 61);
            Assert.That(watch.BannerVisible(61.5), Is.True, "the server escalated to the second stage (CaptureCount 2)");
            Assert.That(watch.Prompt.Text, Is.EqualTo(CaptureWatch.SecondBanner));
            Assert.That(watch.Banners, Is.EqualTo(2));
            watch.Observe(PlayerStatus.Lagging, 29, 2, 62);
            Assert.That(watch.Banners, Is.EqualTo(2), "once per escalation");
            Assert.That(SurvivorReadout.LaggingTotal(2), Is.EqualTo(30f), "the same source as the meter");
        }

        // ---- attempt 2: no per-frame allocation for the exits and the memoized texts ---------------------------------------

        [Test]
        public void TheExitBufferIsBuiltOncePerWorldStateAndReused()
        {
            var cache = new ExitCache();
            S2C_WorldState first = World();
            ExitView[] a = cache.For(first);
            Assert.That(cache.For(first, 3.2f), Is.SameAs(a), "same snapshot, same buffer");
            Assert.That(cache.SwitchInProgress, Is.True);
            S2C_WorldState second = World();
            second.Exits[0] = new ExitState { Id = 0, X = 4, Z = 4, Flags = 1 | 4, SwitchProgress = 70 };
            ExitView[] b = cache.For(second, 3.2f);
            Assert.That(b, Is.SameAs(a), "a new snapshot refills the same array");
            Assert.That(ExitCopy.State(b[0]), Is.EqualTo("Locked 4 s"));
            Assert.That(ExitCopy.State(cache.For(second)[0]), Is.EqualTo("Locked"), "the Locked seconds are applied per call");
            var one = new S2C_WorldState();
            one.Exits.Add(new ExitState { Id = 0, Flags = 0 });
            Assert.That(cache.For(one).Length, Is.EqualTo(1));
            Assert.That(cache.SwitchInProgress, Is.False);
        }

        [Test]
        public void PerFrameTextsAreTheSameStringWhileTheValueIsUnchanged()
        {
            ExitView exit = ExitView.FromWire(0, 1, 62);
            string open = InteractCopy.SwitchOpen(exit);
            Assert.That(InteractCopy.SwitchOpen(exit), Is.SameAs(open));
            Assert.That(InteractCopy.SwitchOpen(ExitView.FromWire(0, 1, 63)), Does.Contain("63%"));
            Assert.That(InteractCopy.Kick(4), Is.SameAs(InteractCopy.Kick(4)));
            var layer = new V2MarkerLayer(null);
            TerminalFlagsView flags = TerminalFlagsView.FromWire(1, 40, 2);
            Assert.That(layer.OverloadedTag(flags, 20.2f), Is.SameAs(layer.OverloadedTag(flags, 19.9f)), "both show 20 m");
            Assert.That(layer.OverloadedTag(flags, 21f), Does.EndWith("21 m"));
            var timer = new CollapseTimer(Role.Survivor, null);
            timer.Set(V2Wire.Collapse(95.6f, -1, false));
            string main = timer.Main;
            timer.Set(V2Wire.Collapse(95.2f, -1, false));
            Assert.That(timer.Main, Is.SameAs(main), "1:36 both times: not rebuilt");
            timer.Set(V2Wire.Collapse(94.9f, -1, false));
            Assert.That(timer.Main, Is.EqualTo("Shutdown 1:35"));
        }
    }
}
