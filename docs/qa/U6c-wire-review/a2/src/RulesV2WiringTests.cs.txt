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
    // U6c-wire: protocol v0.4 -> the rules v2 HUD (HUD_SPEC 13). Sync check tracking, the SelfState v0.4 fields, exit and
    // terminal-flag mapping, the result rows, the absence of execution copy, the dev flags and the autopilot.
    public sealed class RulesV2WiringTests
    {
        private const string Dot = "·";

        private static S2C_SyncCheck Check(uint id, float zoneStart = .6f)
            => new S2C_SyncCheck { CheckId = id, TerminalId = 2, Duration = 1.1f, ZoneStart = zoneStart, ZoneSize = .12f, GreatSize = .035f };

        // ---- 13.2 sync check ---------------------------------------------------------------------------------------------

        [Test]
        public void ACheckShowsTheRingAndTheNeedleFollowsLocalTimeSinceArrival()
        {
            var sync = new SyncCheckTracker();
            Assert.That(sync.OnCheck(Check(5), 10), Is.True);
            sync.Tick(10.55);
            Assert.That(sync.State.Visible, Is.True);
            Assert.That(sync.View.Elapsed, Is.EqualTo(.55f).Within(1e-4f));
            Assert.That(sync.State.Needle, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(sync.TerminalId, Is.EqualTo(2));
        }

        [Test]
        public void SpaceSendsExactlyOnePressPerCheck()
        {
            var sync = new SyncCheckTracker();
            var sent = new List<uint>();
            sync.State.Press += sent.Add;
            sync.OnCheck(Check(1), 0);
            sync.Tick(.3);
            Assert.That(sync.TryPress(), Is.True);
            sync.Tick(.4);
            Assert.That(sync.TryPress(), Is.False, "a second press for the same check does nothing");
            Assert.That(sync.TryPress(), Is.False);
            Assert.That(sent, Is.EqualTo(new[] { 1u }));
            sync.OnResult(1, (byte)SyncResult.Fail, .45);
            Assert.That(sync.TryPress(), Is.False, "nor after the result");
            sync.Tick(2);
            sync.OnCheck(Check(2), 20);
            sync.Tick(20.2);
            Assert.That(sync.TryPress(), Is.True, "the next check takes one press again");
            Assert.That(sent, Is.EqualTo(new[] { 1u, 2u }));
        }

        [Test]
        public void TheResultShowsForFourTenthsThenTheRingHides()
        {
            var sync = new SyncCheckTracker();
            sync.OnCheck(Check(3), 0);
            sync.Tick(.7);
            sync.TryPress();
            Assert.That(sync.OnResult(3, (byte)SyncResult.Great, .75), Is.True);
            sync.Tick(.8);
            Assert.That(sync.State.Visible, Is.True);
            Assert.That(sync.State.Result, Is.EqualTo(SyncResult.Great));
            Assert.That(SyncCheckMath.ResultWord(sync.State.Result.Value), Is.EqualTo("Great"));
            sync.Tick(1.1);
            Assert.That(sync.State.Visible, Is.True, "0.35 s after the result");
            sync.Tick(1.16);
            Assert.That(sync.State.Visible, Is.False, "0.41 s after the result");
            Assert.That(sync.Current, Is.False);
        }

        [Test]
        public void StaleAndUnknownCheckIdsAreIgnored()
        {
            var sync = new SyncCheckTracker();
            sync.OnCheck(Check(7), 0);
            Assert.That(sync.OnCheck(Check(6), .1), Is.False, "older than the current check");
            Assert.That(sync.OnCheck(Check(7), .1), Is.False, "a repeat of the current check");
            Assert.That(sync.View.CheckId, Is.EqualTo(7u));
            Assert.That(sync.OnResult(99, (byte)SyncResult.Great, .2), Is.False, "unknown id");
            Assert.That(sync.OnResult(6, (byte)SyncResult.Great, .2), Is.False, "stale id");
            sync.Tick(.3);
            Assert.That(sync.State.Result.HasValue, Is.False);
            Assert.That(sync.OnResult(7, (byte)SyncResult.Good, .4), Is.True);
            Assert.That(sync.OnResult(7, (byte)SyncResult.Great, .45), Is.False, "only the first result counts");
            Assert.That(sync.View.Result, Is.EqualTo(SyncResult.Good));
            Assert.That(new SyncCheckTracker().OnResult(1, 2, 0), Is.False, "no check at all");
            var odd = new SyncCheckTracker();
            odd.OnCheck(Check(1), 0);
            odd.OnResult(1, 9, .1);
            Assert.That(odd.View.Result, Is.EqualTo(SyncResult.Fail), "an out-of-range result byte reads as Fail");
        }

        [Test]
        public void ACheckWithoutAResultHidesOneSecondAfterItsSweep()
        {
            var sync = new SyncCheckTracker();
            sync.OnCheck(Check(1), 0);
            sync.Tick(2.05);
            Assert.That(sync.State.Visible, Is.True, "Duration 1.1 + 0.95 s");
            sync.Tick(2.11);
            Assert.That(sync.State.Visible, Is.False, "Duration 1.1 + 1.01 s");
            Assert.That(sync.Current, Is.False);
            Assert.That(sync.OnResult(1, (byte)SyncResult.Fail, 2.2), Is.False, "a late result for a hidden check is ignored");
            Assert.That(sync.TryPress(), Is.False);
        }

        [Test]
        public void ClearAndResetHideAtOnceAndOnlyResetForgetsTheIds()
        {
            var sync = new SyncCheckTracker();
            sync.OnCheck(Check(4), 0);
            sync.Tick(.2);
            sync.OnResult(4, (byte)SyncResult.Good, .3);
            sync.Tick(.31);
            sync.Clear(); // the own status left Normal
            Assert.That(sync.State.Visible, Is.False, "even during the result word");
            Assert.That(sync.TryPress(), Is.False);
            sync.Tick(.32);
            Assert.That(sync.State.Visible, Is.False);
            Assert.That(sync.OnCheck(Check(3), .4), Is.False, "Clear keeps the stale-id memory");
            Assert.That(sync.OnCheck(Check(5), .5), Is.True);
            sync.Reset(); // MatchStart / GameResult / disconnect
            Assert.That(sync.State.Visible, Is.False);
            Assert.That(sync.OnCheck(Check(1), 1), Is.True, "a new match may start the ids again");
        }

        [Test]
        public void TheSurvivorHudShowsTheRingAboveThePromptRow()
        {
            var hud = new SurvivorHud(new VisualElement(), Resources.Load<Font>("Fonts/Inter-Regular"));
            try
            {
                hud.Begin(SkillIds.DummyPacket);
                Assert.That(hud.SyncRing.style.display.value, Is.EqualTo(DisplayStyle.None));
                hud.Sync.OnCheck(Check(1), 0);
                hud.Sync.Tick(.2);
                hud.SetSync("Space");
                Assert.That(hud.SyncRing.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Rect withRing = hud.ClusterRect(new Vector2(1600, 900));
                Assert.That(withRing.height, Is.EqualTo(SurvivorHud.Height + SyncCheckRing.Size + 8), "the ring and its 8 u gap join the keep-out");
                hud.Sync.Clear();
                hud.SetSync("Space");
                Assert.That(hud.SyncRing.style.display.value, Is.EqualTo(DisplayStyle.None));
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
            Assert.That(CollapseCopy.Main(collapse), Is.EqualTo("Collapse 1:52"));
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
            Assert.That(TerminalFlagCopy.Regressing(regressing, Role.Survivor), Is.EqualTo("Terminal 1 " + Dot + " 40% " + Dot + " Regressing"));
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
                Assert.That(hud.Objective.Collapse.Main, Is.EqualTo("Collapse 1:36"));
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
            Assert.That(hatch.Text, Is.EqualTo("Press [E] to escape through the Hatch"), "the hatch comes first");
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
            Assert.That(ResultV2Copy.Reason(view.Reason), Is.EqualTo("The collapse eliminated the Survivors still inside."));
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
            Assert.That(none.AutoSync, Is.EqualTo(AutoSync.None));
            DevArgs args = DevArgs.Parse(new[] { "x", "-ptk-dev-hack", "40", "-ptk-auto-sync", "GREAT", "-ptk-dev-switch" });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Active, Is.True);
            Assert.That(args.DevHack, Is.EqualTo(40f));
            Assert.That(args.AutoSync, Is.EqualTo(AutoSync.Great));
            Assert.That(args.DevSwitch, Is.True);
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-sync", "good" }).AutoSync, Is.EqualTo(AutoSync.Good));
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-sync", "miss" }).AutoSync, Is.EqualTo(AutoSync.Miss));
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-auto-sync", "miss" }).Active, Is.True);
            DevArgs bad = DevArgs.Parse(new[] { "x", "-ptk-dev-hack", "-3", "-ptk-auto-sync", "perfect", "-ptk-dev-hack" });
            Assert.That(bad.Errors.Count, Is.EqualTo(3));
            Assert.That(bad.DevHack, Is.EqualTo(0f));
            Assert.That(bad.AutoSync, Is.EqualTo(AutoSync.None));
        }

        [Test]
        public void AutoSyncPressesAtTheGreatPartTheRestOfTheZoneOrNever()
        {
            var check = new SyncCheckView { Duration = 1.1f, ZoneStart = .6f, ZoneSize = .12f, GreatSize = .035f };
            Assert.That(HackPilot.SyncDue(AutoSync.Great, check, .58f), Is.False);
            Assert.That(HackPilot.SyncDue(AutoSync.Great, check, .595f), Is.True, "the lead before the zone (0.59)");
            Assert.That(HackPilot.SyncDue(AutoSync.Good, check, .65f), Is.False);
            Assert.That(HackPilot.SyncDue(AutoSync.Good, check, .678f), Is.True, "the middle of the zone after the great part (0.6775)");
            Assert.That(HackPilot.SyncDue(AutoSync.Miss, check, .99f), Is.False);
            Assert.That(HackPilot.SyncDue(AutoSync.None, check, .99f), Is.False);
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
                Assert.That(hud.V2Markers.Tags, Is.EqualTo(new[] { "Terminal 3 " + Dot + " 40% " + Dot + " Regressing" }));
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
            Assert.That(app, Does.Contain("UpdateSurvivorHud(now);\n                screens.Survivor.DrawTerminalFlags(lastWorld, rig.Pose);"));
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
            Assert.That(timer.Main, Is.EqualTo("Collapse 1:35"));
        }

        // ---- attempt 2: the needle is the moving hand ------------------------------------------------------------------------

        [Test]
        public void TheNeedleIsALongHandAndTheGreatPartIsCutFromTheGoodPart()
        {
            Assert.That(SyncCheckRing.NeedleOuter - SyncCheckRing.NeedleInner, Is.GreaterThan(2 * SyncCheckRing.GreatWidth), "longer than twice the Great head is thick");
            Assert.That(SyncCheckRing.NeedleInner, Is.LessThan(SyncCheckRing.BandRadius - SyncCheckRing.GreatWidth * .5f), "starts inside the band");
            Assert.That(SyncCheckRing.NeedleOuter, Is.EqualTo(SyncCheckRing.Size * .5f + 8), "ends 8 u past the disc, inside the stack gap");
            Assert.That(SyncCheckRing.NeedleWidth, Is.EqualTo(3f));
            Assert.That(SyncCheckRing.GreatGap, Is.GreaterThan(0f));
        }
    }
}
