using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // Rules v2 HUD widgets (HUD_SPEC 13): the packet-routing tracker (prediction, completion, error, rate limit), its key
    // mapping and copy (11/17, R2), hold fill clamping, exit phases, collapse urgency, terminal flags, result rows and the
    // majority winner line, and the preview flag.
    public sealed class RulesV2HudTests
    {
        private const string Dot = "\u00B7";
        private const RouteDirection U = RouteDirection.Up, D = RouteDirection.Down, Lf = RouteDirection.Left, R = RouteDirection.Right;

        private static byte Pack(params RouteDirection[] dirs)
        {
            byte packed = 0;
            for (int i = 0; i < dirs.Length; i++) packed = HackRoute.WithDirection(packed, i, dirs[i]);
            return packed;
        }

        private static RouteTracker Tracker(byte id, byte dirs, double now = 0)
        {
            var route = new RouteTracker();
            route.OnSelf(id, HackRoute.MaxLength, dirs, 0, now);
            return route;
        }

        // ---- 13.2 packet routing (11/17, R2) ------------------------------------------------------------------------------

        [Test]
        public void RightKeysAdvanceTheCurrentCellAndAFullRouteFlashesPlusEight()
        {
            var route = Tracker(5, Pack(U, R, D, Lf));
            var keys = new List<bool>();
            int done = 0, failed = 0;
            route.Key += keys.Add; route.RouteDone += () => done++; route.RouteFailed += () => failed++;
            Assert.That(route.Present && route.Held && route.Hint, Is.True);
            Assert.That(route.Expected, Is.EqualTo(U));
            Assert.That(route.TryPress(U, 1, out var first), Is.True);
            Assert.That(first.RouteId, Is.EqualTo(5));
            Assert.That(first.Direction, Is.EqualTo((byte)U));
            Assert.That(route.Index, Is.EqualTo(1));
            Assert.That(route.Pulse(1.01), Is.GreaterThan(0f), "a right key pulses");
            Assert.That(route.Pulse(1 + RouteTracker.PulseSeconds + .01), Is.EqualTo(0f));
            route.TryPress(R, 1.2, out _); route.TryPress(D, 1.4, out _);
            Assert.That(route.TryPress(Lf, 1.6, out _), Is.True);
            Assert.That(keys, Is.EqualTo(new[] { true, true, true, true }));
            Assert.That(done, Is.EqualTo(1));
            Assert.That(failed, Is.EqualTo(0));
            Assert.That(route.Present, Is.False, "nothing more is sent for a finished route");
            Assert.That(route.Held, Is.True, "until the server clears it");
            Assert.That(route.TryPress(U, 1.7, out _), Is.False);
            Assert.That(RouteCopy.Label(route, RouteCopy.KeyboardGlyph, 1.7), Is.EqualTo(RouteCopy.Done));
            Assert.That(RouteCopy.Label(route, RouteCopy.KeyboardGlyph, 1.6 + RouteTracker.DoneSeconds + .01), Is.EqualTo(RouteCopy.Next));
            route.OnSelf(0, 0, 0, 0, 1.75);
            Assert.That(route.Held, Is.False);
            Assert.That(route.GapFraction(1.75 + GameConstants.RouteGapSeconds * .5), Is.EqualTo(.5f).Within(1e-3f));
            route.OnSelf(6, 4, Pack(D, D, U, U), 0, 4.8);
            Assert.That(route.Present && route.Index == 0 && route.Expected == D, Is.True, "the next route");
            Assert.That(route.Hint, Is.False, "the hint was for the first route only");
            Assert.That(RouteCopy.Label(route, RouteCopy.KeyboardGlyph, 4.8), Is.EqualTo("Route " + Dot + " WASD"));
        }

        [Test]
        public void AWrongKeyDropsTheRouteShakesAndNamesTheAlert()
        {
            var route = Tracker(9, Pack(U, U, R, R));
            int failed = 0;
            route.RouteFailed += () => failed++;
            route.TryPress(U, 2, out _);
            Assert.That(route.TryPress(Lf, 2.1, out var wrong), Is.True, "a wrong key is still sent: the server judges it");
            Assert.That(wrong.Direction, Is.EqualTo((byte)Lf));
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(route.Errors, Is.EqualTo(1));
            Assert.That(route.Present, Is.False);
            Assert.That(route.TryPress(U, 2.2, out _), Is.False, "locked out until the server deals the next route");
            Assert.That(route.Shake(2.1), Is.EqualTo(1f));
            Assert.That(route.Shake(2.1 + RouteTracker.ShakeSeconds + .01), Is.EqualTo(0f));
            Assert.That(RouteCopy.Label(route, "WASD", 2.5), Is.EqualTo("Route dropped " + Dot + " the Killer was alerted"));
            Assert.That(route.ErrorShown(2.1 + RouteTracker.ErrorSeconds + .01), Is.False);
            route.OnSelf(10, 4, Pack(R, R, R, R), 0, 2.3);
            Assert.That(route.Present, Is.True, "a new route after the error");
            Assert.That(RouteCopy.Label(route, "WASD", 2.4), Is.EqualTo(RouteCopy.Dropped), "the error line outlives the swap");
        }

        [Test]
        public void TheServerIndexNeverMovesThePredictionBackAndANewIdStartsClean()
        {
            var route = Tracker(3, Pack(U, D, U, D));
            route.TryPress(U, 0, out _); route.TryPress(D, .1, out _);
            route.OnSelf(3, 4, Pack(U, D, U, D), 1, .15);
            Assert.That(route.Index, Is.EqualTo(2), "a SelfState from before the second key arrived");
            route.OnSelf(3, 4, Pack(U, D, U, D), 3, .3);
            Assert.That(route.Index, Is.EqualTo(3), "the server is ahead (keys from another path)");
            route.OnSelf(4, 4, Pack(Lf, Lf, Lf, Lf), 2, .4);
            Assert.That(route.Index, Is.EqualTo(2), "a fresh id takes the server's index");
            route.OnSelf(4, 9, Pack(Lf, Lf, Lf, Lf), 200, .5);
            Assert.That(route.Length, Is.EqualTo(HackRoute.MaxLength), "the length is clamped");
            Assert.That(route.Index, Is.LessThanOrEqualTo(HackRoute.MaxLength));
        }

        [Test]
        public void AtMostTwelveKeysPerSecondLeaveTheClient()
        {
            var route = new RouteTracker();
            byte id = 1;
            int sent = 0;
            for (int i = 0; i < 20; i++)
            {
                if (!route.Present) route.OnSelf(id++, 4, Pack(U, U, U, U), 0, i * .01);
                if (route.TryPress(U, i * .01, out _)) sent++;
            }
            Assert.That(sent, Is.EqualTo(GameConstants.RouteInputsPerSecond));
            if (!route.Present) route.OnSelf(id, 4, Pack(U, U, U, U), 0, 1.0);
            Assert.That(route.TryPress(U, 1.0, out _), Is.True, "the window slid");
        }

        [Test]
        public void ClearDropsTheRouteAndResetBringsTheHintBack()
        {
            var route = Tracker(1, Pack(U, U, U, U));
            for (int i = 0; i < 4; i++) route.TryPress(U, i * .2, out _);
            Assert.That(route.Hint, Is.False);
            route.OnSelf(2, 4, Pack(D, D, D, D), 0, 1);
            route.Clear(); // the own status left Normal
            Assert.That(route.Held || route.Present, Is.False);
            Assert.That(route.Hint, Is.False, "Clear keeps the first-route memory for the match");
            route.Reset(); // MatchStart / GameResult / disconnect
            Assert.That(route.Hint && route.Completed == 0 && route.Errors == 0 && route.KeysSent == 0, Is.True);
            route.OnSelf(1, 4, Pack(U, U, U, U), 0, 5);
            Assert.That(RouteCopy.Label(route, "WASD", 5), Is.EqualTo("Press the arrows in order"));
        }

        // Attempt 2 (F2): a SelfState of the same route well after the last key is the server's word.
        [Test]
        public void ASelfStateWellAfterTheLastKeyRollsAPredictedCompletionBack()
        {
            var route = Tracker(2, Pack(U, U, U, U));
            int done = 0;
            route.RouteDone += () => done++;
            for (int i = 0; i < 4; i++) route.TryPress(U, i * .1, out _);
            Assert.That(done, Is.EqualTo(1));
            Assert.That(route.Present, Is.False);
            route.OnSelf(2, 4, Pack(U, U, U, U), 3, .35);
            Assert.That(route.Present, Is.False, "within RollbackSeconds: the last key may still be in flight");
            route.OnSelf(2, 4, Pack(U, U, U, U), 3, .3 + RouteTracker.RollbackSeconds + .05);
            Assert.That(route.Present && route.Index == 3, Is.True, "the last key was dropped: answer it again");
            Assert.That(route.Completed, Is.EqualTo(0));
            Assert.That(route.DoneFlash(.7), Is.False, "no false +% flash");
        }

        [Test]
        public void KeysMapWasdArrowsAndPadAndAStickFlickCountsOnce()
        {
            Assert.That(RouteKeys.FromPresses(true, false, false, false), Is.EqualTo(U));
            Assert.That(RouteKeys.FromPresses(false, true, false, false), Is.EqualTo(D));
            Assert.That(RouteKeys.FromPresses(false, false, true, false), Is.EqualTo(Lf));
            Assert.That(RouteKeys.FromPresses(false, false, false, true), Is.EqualTo(R));
            Assert.That(RouteKeys.FromPresses(false, false, false, false), Is.Null);
            var keys = new RouteKeys();
            Assert.That(keys.Flick(new UnityEngine.Vector2(.5f, 0)), Is.Null, "inside the flick radius");
            Assert.That(keys.Flick(new UnityEngine.Vector2(.9f, .2f)), Is.EqualTo(R));
            Assert.That(keys.Flick(new UnityEngine.Vector2(1, 0)), Is.Null, "held out: once per flick");
            Assert.That(keys.Flick(new UnityEngine.Vector2(.2f, .9f)), Is.Null, "not re-armed until it returns");
            Assert.That(keys.Flick(new UnityEngine.Vector2(.1f, 0)), Is.Null);
            Assert.That(keys.Flick(new UnityEngine.Vector2(.1f, -.95f)), Is.EqualTo(D));
            keys.Reset();
            Assert.That(keys.Flick(new UnityEngine.Vector2(-.8f, .1f)), Is.EqualTo(Lf));
            // Keys go to the route (movement sends zero) only while E is held and a route or the hack exists.
            // Attempt 2 (F1) + QA a2 N1: E held AND own Hacking; the route only decides which keys are answered.
            Assert.That(RouteKeys.Capturing(true, true, true), Is.True);
            Assert.That(RouteKeys.Capturing(true, true, false), Is.True, "the gap between routes: WASD stays off movement");
            Assert.That(RouteKeys.Capturing(true, false, true), Is.False, "a route but no hack: WASD moves");
            Assert.That(RouteKeys.Capturing(true, false, false), Is.False, "E on a switch or nothing: WASD still moves");
            Assert.That(RouteKeys.Capturing(false, true, true), Is.False, "released E gives the keys back");
        }

        [Test]
        public void RouteCopyNamesTheKeysAndNeverSaysSkillCheck()
        {
            Assert.That(RouteCopy.Done, Is.EqualTo("+3.4 %"), "the solo burst (attempt 2 tuning)");
            Assert.That(RouteCopy.Hint, Is.EqualTo("Press the arrows in order"));
            Assert.That(RouteCopy.Dropped, Is.EqualTo("Route dropped " + Dot + " the Killer was alerted"));
            Assert.That(RouteCopy.Waiting(RouteCopy.PadGlyph), Is.EqualTo("Route " + Dot + " D-pad"));
            foreach (string line in new[] { RouteCopy.Done, RouteCopy.Hint, RouteCopy.Dropped, RouteCopy.Next, RouteCopy.Waiting("WASD") })
            {
                Assert.That(line.ToLowerInvariant(), Does.Not.Contain("skill check"));
                Assert.That(line.ToLowerInvariant(), Does.Not.Contain("sync"));
            }
        }

        [Test]
        public void ThePanelShowsOnlyWhileRoutingAndReducedMotionStopsTheShake()
        {
            var route = Tracker(1, Pack(U, R, D, Lf));
            var panel = new RoutePanel(route, null);
            panel.Refresh(false, RouteCopy.KeyboardGlyph, 0);
            Assert.That(panel.Shown, Is.False);
            Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.None));
            panel.Refresh(true, RouteCopy.KeyboardGlyph, 0);
            Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(panel.LabelText, Is.EqualTo(RouteCopy.Hint));
            route.TryPress(D, .5, out _);
            panel.Refresh(true, RouteCopy.KeyboardGlyph, .52);
            Assert.That(panel.ShakeOffset, Is.Not.EqualTo(0f), "the error shakes the panel");
            Assert.That(panel.LabelText, Is.EqualTo(RouteCopy.Dropped));
            panel.Reduced = true;
            panel.Refresh(true, RouteCopy.KeyboardGlyph, .52);
            Assert.That(panel.ShakeOffset, Is.EqualTo(0f), "Reduced motion: no shake");
            Assert.That(panel.LabelText, Is.EqualTo(RouteCopy.Dropped), "the line still shows");
            Assert.That(RoutePanel.CellX(3) + RoutePanel.CellWidth, Is.LessThanOrEqualTo(RoutePanel.Width), "four cells fit the 320 u panel");
            Assert.That(RoutePanel.CellTop + RoutePanel.CellHeight, Is.LessThan(52f), "above the label row");
        }

        [Test]
        public void HoldProgressIsClampedForFillAndPercent()
        {
            Assert.That(HoldCopy.Fill(-.3f), Is.EqualTo(0));
            Assert.That(HoldCopy.Fill(1.7f), Is.EqualTo(1));
            Assert.That(HoldCopy.Fill(float.NaN), Is.EqualTo(0));
            Assert.That(HoldCopy.Fill(.42f), Is.EqualTo(.42f));
            Assert.That(HoldCopy.Percent(.999f), Is.EqualTo(99), "never shows 100% before the server says done");
            Assert.That(HoldCopy.Percent(1f), Is.EqualTo(100));
            Assert.That(HoldCopy.Percent(.62f), Is.EqualTo(62));
            Assert.That(HoldCopy.Percent(5f), Is.EqualTo(100));
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.ExitSwitch, Progress01 = .62f }), Is.EqualTo("Opening the Exit · 62% — you are Signal"));
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.KickTerminal, Progress01 = 1.4f }), Is.EqualTo("Jamming the Terminal · 100%"));
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.CloseHatch, Progress01 = -1 }), Is.EqualTo("Closing the Emergency port · 0%"));
            Assert.That(HoldCopy.Label(new HoldActionView { Kind = HoldKind.None }), Is.Empty);
            Assert.That(HoldCopy.Hint(HoldKind.KickTerminal, "[E]"), Is.EqualTo("Hold [E] · moving cancels the jam"));
        }

        [Test]
        public void HoldBarHidesForNoneAndFillsAtMostFull()
        {
            var bar = new HoldActionBar(Role.Killer, null);
            bar.Set(new HoldActionView { Kind = HoldKind.None }, "[E]");
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.None));
            bar.Set(new HoldActionView { Kind = HoldKind.KickTerminal, Progress01 = 1.5f }, "X");
            Assert.That(bar.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(bar.Text, Is.EqualTo("Jamming the Terminal · 100%"));
            VisualElement fill = bar.Q(className: "v2-fill");
            Assert.That(fill.style.width.value.value, Is.EqualTo(100f));
            Assert.That(fill.style.width.value.unit, Is.EqualTo(LengthUnit.Percent));
        }

        [Test]
        public void ExitPhasesFollowPrecedenceAndWireFlags()
        {
            ExitView none = ExitView.FromWire(0, 0, 0);
            Assert.That(ExitCopy.Phase(none), Is.EqualTo(ExitPhase.NoPower));
            Assert.That(ExitCopy.Tag(none), Is.EqualTo("Exit 1 · No power"));
            ExitView powered = ExitView.FromWire(1, 1, 0);
            Assert.That(ExitCopy.Tag(powered), Is.EqualTo("Exit 2 · Powered"));
            ExitView switching = ExitView.FromWire(0, 1, 62);
            Assert.That(ExitCopy.Phase(switching), Is.EqualTo(ExitPhase.Switching));
            Assert.That(ExitCopy.State(switching), Is.EqualTo("62%"));
            ExitView locked = ExitView.FromWire(0, 1 | 4, 40);
            Assert.That(ExitCopy.State(locked), Is.EqualTo("Locked"), "no FirewallBlock effect known");
            locked.LockedLeft = 3.2f;
            Assert.That(ExitCopy.State(locked), Is.EqualTo("Locked 4 s"));
            ExitView open = ExitView.FromWire(1, 1 | 2 | 4, 100);
            Assert.That(ExitCopy.Phase(open), Is.EqualTo(ExitPhase.Open), "Open wins over Locked");
            Assert.That(ExitCopy.State(open), Is.EqualTo("Open"));
        }

        [Test]
        public void ObjectiveRowAppearsWithPowerOrCollapse()
        {
            var unpowered = new[] { ExitView.FromWire(0, 0, 0), ExitView.FromWire(1, 0, 0) };
            var idle = new CollapseView { GlobalLeft = -1, PersonalLeft = -1 };
            Assert.That(ObjectiveRow.Applies(unpowered, idle), Is.False);
            Assert.That(ObjectiveRow.Applies(new[] { ExitView.FromWire(0, 1, 0), ExitView.FromWire(1, 0, 0) }, idle), Is.True);
            Assert.That(ObjectiveRow.Applies(unpowered, new CollapseView { GlobalLeft = 100, PersonalLeft = 100 }), Is.True, "hatch closed: collapse before any power");
            var row = new ObjectiveRow(null);
            row.Set(new[] { ExitView.FromWire(0, 1, 62), ExitView.FromWire(1, 1 | 2, 100) }, idle);
            Assert.That(row.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(row.Exits[0].Text, Is.EqualTo("Exit 1 62%"));
            Assert.That(row.Exits[1].Text, Is.EqualTo("Exit 2 Open"));
            Assert.That(row.Collapse.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void CollapseTurnsUrgentAtThirtySecondsShown()
        {
            Assert.That(CollapseCopy.Urgent(30.01f), Is.False, "shows 0:31");
            Assert.That(CollapseCopy.Urgent(30f), Is.True, "shows 0:30");
            Assert.That(CollapseCopy.Urgent(29.5f), Is.True);
            Assert.That(CollapseCopy.Urgent(0f), Is.True);
            Assert.That(CollapseCopy.Urgent(-1f), Is.False, "not running");
            Assert.That(CollapseCopy.Clock(30.01f), Is.EqualTo("0:31"));
            Assert.That(CollapseCopy.Clock(119.2f), Is.EqualTo("2:00"));
            Assert.That(CollapseCopy.Clock(-1), Is.EqualTo("0:00"));
            var lagging = new CollapseView { GlobalLeft = 52, PersonalLeft = 25, Lagging = true };
            Assert.That(CollapseCopy.Urgent(lagging), Is.True, "the personal clock decides");
            Assert.That(CollapseCopy.Main(lagging), Is.EqualTo("Shutdown 0:52"));
            Assert.That(CollapseCopy.Personal(lagging), Is.EqualTo("Lagging: 2× faster · you 0:25"));
            var recovered = new CollapseView { GlobalLeft = 52, PersonalLeft = 40 };
            Assert.That(CollapseCopy.Personal(recovered), Is.EqualTo("You 0:40"));
            var same = new CollapseView { GlobalLeft = 52.2f, PersonalLeft = 52.1f };
            Assert.That(CollapseCopy.Personal(same), Is.Empty, "same m:ss: one clock");
            Assert.That(CollapseCopy.Urgent(new CollapseView { GlobalLeft = -1, PersonalLeft = -1 }), Is.False);
        }

        [Test]
        public void CollapseUrgencyChangesOnlyTheBarColourPerRole()
        {
            var survivor = new CollapseTimer(Role.Survivor, null);
            survivor.Set(new CollapseView { GlobalLeft = 31, PersonalLeft = 31 });
            Assert.That(survivor.Urgent, Is.False);
            VisualElement bar = survivor.Q(className: "v2-bar");
            Assert.That(bar.ClassListContains("v2-urgent-danger"), Is.False);
            survivor.Set(new CollapseView { GlobalLeft = 30, PersonalLeft = 30 });
            Assert.That(survivor.Urgent && bar.ClassListContains("v2-urgent-danger"), Is.True);
            var killer = new CollapseTimer(Role.Killer, null);
            killer.Set(new CollapseView { GlobalLeft = 45, PersonalLeft = 10, Lagging = true });
            Assert.That(killer.Urgent, Is.False, "the Killer has no personal clock");
            Assert.That(killer.Personal, Is.Empty);
            killer.Set(new CollapseView { GlobalLeft = 12, PersonalLeft = -1 });
            Assert.That(killer.Q(className: "v2-bar").ClassListContains("v2-urgent-text"), Is.True, "no danger on the Killer HUD");
        }

        [Test]
        public void TerminalFlagsDecodeAndCopyPerRole()
        {
            TerminalFlagsView t = TerminalFlagsView.FromWire(2, 40, 1 | 2);
            Assert.That(t.Regressing && t.Overloaded, Is.True);
            Assert.That(TerminalFlagCopy.Regressing(t, Role.Survivor), Is.EqualTo("Terminal 3 · 40% · Rolling back"));
            Assert.That(TerminalFlagCopy.Regressing(t, Role.Killer), Is.EqualTo("Terminal 3 · Rolling back"), "no percentage for the Killer");
            Assert.That(TerminalFlagCopy.Overloaded(t, 23.6f), Is.EqualTo("Terminal 3 overloaded · 24 m"));
            Assert.That(TerminalFlagCopy.ShowOverloaded(t, Role.Killer), Is.True);
            Assert.That(TerminalFlagCopy.ShowOverloaded(t, Role.Survivor), Is.False, "Overloaded is the Killer alert");
            Assert.That(TerminalFlagsView.FromWire(0, 10, 0).Regressing, Is.False);
        }

        private static ResultV2View Result(int escapedCount, Role winner, byte ownId, Role ownRole)
        {
            var escaped = new List<byte>(); var eliminated = new List<byte>();
            for (byte id = 1; id <= 7; id++) (id <= escapedCount ? escaped : eliminated).Add(id);
            byte[] order = { 0, 1, 2, 3, 4, 5, 6, 7 };
            return ResultV2View.FromIds((byte)winner, (byte)ResultReasonV2.Collapse, escaped, eliminated, order, id => "P" + id, ownId, ownRole);
        }

        [Test]
        public void FourOfSevenEscapedIsASurvivorWin()
        {
            ResultV2View view = Result(4, Role.Survivor, 4, Role.Survivor);
            Assert.That(view.Starting, Is.EqualTo(7), "the Killer id 0 is in neither list");
            Assert.That(view.Escaped, Is.EqualTo(4));
            Assert.That(ResultV2Copy.SurvivorsWin(4, 7), Is.True);
            Assert.That(ResultV2Copy.Consistent(view), Is.True);
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Survivors win"));
            Assert.That(ResultV2Copy.Rule(view.Escaped, view.Starting), Is.EqualTo("4 of 7 escaped · Survivors win when half or more escape (4 of 7)"));
            Assert.That(ResultV2Copy.Outcome(view), Is.EqualTo("You escaped."));
            Assert.That(ResultV2Copy.Reason(view.Reason), Is.EqualTo("The shutdown eliminated the Survivors still inside."));
            Assert.That(ResultV2Copy.RowName(view.Rows[3], 3), Is.EqualTo("P4 (you)"));
            Assert.That(ResultV2Copy.RowState(view.Rows[3]), Is.EqualTo("Escaped"));
            Assert.That(ResultV2Copy.RowState(view.Rows[4]), Is.EqualTo("Eliminated"));
            var panel = new ResultV2Panel(null);
            panel.Set(view);
            Assert.That(panel.Headline, Is.EqualTo("Survivors win"));
            Assert.That(panel.Query(className: "v2-result-row").ToList().Count, Is.EqualTo(7));
            Assert.That(panel.Query(className: "v2-self").ToList().Count, Is.EqualTo(1));
        }

        [Test]
        public void ThreeOfSevenEscapedIsAKillerWin()
        {
            ResultV2View view = Result(3, Role.Killer, 0, Role.Killer);
            Assert.That(view.Escaped, Is.EqualTo(3));
            Assert.That(ResultV2Copy.SurvivorsWin(3, 7), Is.False);
            Assert.That(ResultV2Copy.Consistent(view), Is.True);
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Killer wins"));
            Assert.That(ResultV2Copy.Rule(3, 7), Is.EqualTo("3 of 7 escaped · Survivors win when half or more escape (4 of 7)"));
            Assert.That(ResultV2Copy.Outcome(view), Is.EqualTo("You eliminated 4 of 7 Survivors."));
            Assert.That(view.Rows.TrueForAll(r => !r.Self), Is.True);
            Assert.That(ResultV2Copy.Consistent(Result(3, Role.Survivor, 0, Role.Killer)), Is.False, "a winner that breaks the rule is flagged");
        }

        [Test]
        public void MajorityBoundaryAndReasons()
        {
            Assert.That(ResultV2Copy.SurvivorsWin(1, 2), Is.True, "half counts");
            Assert.That(ResultV2Copy.SurvivorsWin(0, 1), Is.False);
            Assert.That(ResultV2Copy.SurvivorsWin(0, 0), Is.False);
            Assert.That(ResultV2Copy.Needed(7), Is.EqualTo(4));
            Assert.That(ResultV2Copy.Needed(2), Is.EqualTo(1));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.AllResolved), Is.EqualTo("Every Survivor escaped or was eliminated."));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.AllSurvivorsDown), Is.EqualTo("Every Survivor was eliminated before anyone escaped."));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.TimeUp), Is.EqualTo("Time ran out. Survivors still inside were eliminated."));
            ResultV2View odd = ResultV2View.FromIds(9, 9, new byte[] { 1 }, null, new byte[] { 1 }, null, 1, Role.Survivor);
            Assert.That(odd.Reason, Is.EqualTo(ResultReasonV2.AllResolved), "an out-of-range reason falls back");
            Assert.That(ResultV2Copy.RowName(odd.Rows[0], 0), Is.EqualTo("Survivor 1 (you)"), "a missing name");
            var longName = new ResultRow { Name = new string('a', 40) };
            Assert.That(ResultV2Copy.RowName(longName, 0).Length, Is.EqualTo(ResultV2Copy.NameBudget));
        }

        [Test]
        public void PreviewFlagTakesOnlyKnownScenarios()
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-preview-v2-hud", "Kick-Terminal" });
            Assert.That(args.PreviewV2Hud, Is.EqualTo("kick-terminal"));
            Assert.That(args.Active, Is.True);
            Assert.That(args.Errors, Is.Empty);
            DevArgs bad = DevArgs.Parse(new[] { "-ptk-preview-v2-hud", "everything" });
            Assert.That(bad.PreviewV2Hud, Is.Null);
            Assert.That(bad.Errors, Has.Count.EqualTo(1));
            Assert.That(DevArgs.Parse(new[] { "-ptk-preview-v2-hud" }).Errors, Has.Count.EqualTo(1), "needs a value");
            Assert.That(RulesV2Preview.RoleOf("kick-terminal"), Is.EqualTo(Role.Killer));
            Assert.That(RulesV2Preview.RoleOf("route-panel"), Is.EqualTo(Role.Survivor));
            Assert.That(RulesV2Preview.Known("sync-check"), Is.False, "retired (11/17, R2)");
        }
    }
}
