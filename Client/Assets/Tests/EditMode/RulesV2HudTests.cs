using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // Rules v2 HUD widgets (HUD_SPEC 13): sync check needle and zones, press-once and result flash, hold fill clamping,
    // exit phases, collapse urgency, terminal flags, result rows and the majority winner line, and the preview flag.
    public sealed class RulesV2HudTests
    {
        private const float Duration = 1.1f, ZoneStart = .62f, ZoneSize = .12f, GreatSize = .035f;
        private const float Nudge = 1e-4f;

        private static SyncCheckView Check(uint id = 1, float elapsed = 0, SyncResult? result = null)
            => new SyncCheckView { CheckId = id, Duration = Duration, ZoneStart = ZoneStart, ZoneSize = ZoneSize, GreatSize = GreatSize, Elapsed = elapsed, Result = result };

        [Test]
        public void NeedleSweepsClockwiseFromTwelveOverDuration()
        {
            Assert.That(SyncCheckMath.NeedleFraction(0, Duration), Is.EqualTo(0));
            Assert.That(SyncCheckMath.NeedleFraction(.55f, Duration), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(SyncCheckMath.NeedleDegrees(.55f, Duration), Is.EqualTo(180f).Within(1e-3f));
            Assert.That(SyncCheckMath.NeedleFraction(.275f, Duration), Is.EqualTo(.25f).Within(1e-5f));
            Assert.That(SyncCheckMath.NeedleFraction(Duration, Duration), Is.EqualTo(1f));
            Assert.That(SyncCheckMath.NeedleFraction(2f, Duration), Is.EqualTo(1f), "stops at a full turn");
            Assert.That(SyncCheckMath.NeedleFraction(-.2f, Duration), Is.EqualTo(0f));
            Assert.That(SyncCheckMath.NeedleFraction(.5f, 0), Is.EqualTo(0f), "a zero duration never divides");
            Assert.That(SyncCheckMath.NeedleFraction(float.NaN, Duration), Is.EqualTo(0f));
        }

        [Test]
        public void ZoneJudgingIncludesBothEdgesAndTheGreatPartFirst()
        {
            float greatEnd = ZoneStart + GreatSize, zoneEnd = ZoneStart + ZoneSize;
            Assert.That(SyncCheckMath.Judge(ZoneStart - Nudge, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Fail), "just before the zone");
            Assert.That(SyncCheckMath.Judge(ZoneStart, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Great), "zone start edge");
            Assert.That(SyncCheckMath.Judge(greatEnd, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Great), "great end edge");
            Assert.That(SyncCheckMath.Judge(greatEnd + Nudge, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Good));
            Assert.That(SyncCheckMath.Judge(zoneEnd, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Good), "zone end edge");
            Assert.That(SyncCheckMath.Judge(zoneEnd + Nudge, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Fail));
            Assert.That(SyncCheckMath.Judge(0, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Fail));
            Assert.That(SyncCheckMath.Judge(1, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Fail));
            Assert.That(SyncCheckMath.Judge(float.NaN, ZoneStart, ZoneSize, GreatSize), Is.EqualTo(SyncResult.Fail));
        }

        [Test]
        public void ToleranceWidensBothZoneEdges()
        {
            float tolerance = SyncCheckMath.ToleranceFraction(Duration);
            Assert.That(tolerance, Is.EqualTo(.15f / 1.1f).Within(1e-6f));
            SyncCheckView view = Check();
            Assert.That(SyncCheckMath.Judge(ZoneStart - tolerance + Nudge, view, tolerance), Is.EqualTo(SyncResult.Good), "leading tolerance is Good, never Great");
            Assert.That(SyncCheckMath.Judge(ZoneStart - Nudge, view, tolerance), Is.EqualTo(SyncResult.Good), "just before the zone, inside the tolerance");
            Assert.That(SyncCheckMath.Judge(ZoneStart, view, tolerance), Is.EqualTo(SyncResult.Great), "Great starts at the real zone start");
            Assert.That(SyncCheckMath.Judge(ZoneStart - tolerance - Nudge, view, tolerance), Is.EqualTo(SyncResult.Fail));
            Assert.That(SyncCheckMath.Judge(ZoneStart + ZoneSize + tolerance - Nudge, view, tolerance), Is.EqualTo(SyncResult.Good));
            Assert.That(SyncCheckMath.Judge(ZoneStart + ZoneSize + tolerance + Nudge, view, tolerance), Is.EqualTo(SyncResult.Fail));
            Assert.That(SyncCheckMath.Judge(.5f, ZoneStart, ZoneSize, GreatSize, -1), Is.EqualTo(SyncResult.Fail), "a negative tolerance is zero");
        }

        [Test]
        public void ToleranceNeverCreatesGreatOnEitherEdge()
        {
            float tolerance = SyncCheckMath.ToleranceFraction(Duration);
            float greatEnd = ZoneStart + GreatSize, zoneEnd = ZoneStart + ZoneSize;
            // Leading edge: everything in [ZoneStart - tolerance, ZoneStart) is Good.
            for (int i = 1; i <= 9; i++)
                Assert.That(SyncCheckMath.Judge(ZoneStart - tolerance * i / 10f, ZoneStart, ZoneSize, GreatSize, tolerance), Is.EqualTo(SyncResult.Good), "lead " + i);
            // Trailing edge: everything in (zone end, zone end + tolerance] is Good.
            for (int i = 1; i <= 9; i++)
                Assert.That(SyncCheckMath.Judge(zoneEnd + tolerance * i / 10f, ZoneStart, ZoneSize, GreatSize, tolerance), Is.EqualTo(SyncResult.Good), "trail " + i);
            // The great part is the same with or without tolerance.
            Assert.That(SyncCheckMath.Judge(greatEnd, ZoneStart, ZoneSize, GreatSize, tolerance), Is.EqualTo(SyncResult.Great));
            Assert.That(SyncCheckMath.Judge(greatEnd + Nudge, ZoneStart, ZoneSize, GreatSize, tolerance), Is.EqualTo(SyncResult.Good));
        }

        [Test]
        public void ACheckTakesOnePressAndFreezesTheNeedle()
        {
            var state = new SyncCheckState();
            var pressed = new List<uint>();
            state.Press += pressed.Add;
            Assert.That(state.TryPress(), Is.False, "no check yet");
            state.Update(Check(7, .7f), true, 10);
            Assert.That(state.Visible, Is.True);
            Assert.That(state.TryPress(), Is.True);
            Assert.That(state.TryPress(), Is.False, "at most one press per check");
            Assert.That(pressed, Is.EqualTo(new[] { 7u }));
            float frozen = state.Needle;
            Assert.That(frozen, Is.EqualTo(.7f / Duration).Within(1e-5f));
            state.Update(Check(7, 1f), true, 10.3);
            Assert.That(state.Needle, Is.EqualTo(frozen), "the needle stays where it was pressed");
        }

        [Test]
        public void TheResultShowsForFourTenthsOfASecondThenHides()
        {
            var state = new SyncCheckState();
            state.Update(Check(1, .7f), true, 0);
            state.TryPress();
            state.Update(Check(1, .76f, SyncResult.Good), true, 1.0);
            Assert.That(state.Result, Is.EqualTo(SyncResult.Good));
            Assert.That(state.Visible, Is.True);
            state.Update(Check(1, .8f, SyncResult.Good), false, 1.39);
            Assert.That(state.Visible, Is.True, "the flash outlives the check");
            state.Update(Check(1, .8f, SyncResult.Good), false, 1.4);
            Assert.That(state.Visible, Is.False);
            Assert.That(SyncCheckMath.ResultWord(SyncResult.Great), Is.EqualTo("Great"));
            Assert.That(SyncCheckMath.ResultWord(SyncResult.Good), Is.EqualTo("Good"));
            Assert.That(SyncCheckMath.ResultWord(SyncResult.Fail), Is.EqualTo("Fail"));
        }

        [Test]
        public void ACancelledCheckHidesAndANewCheckStartsClean()
        {
            var state = new SyncCheckState();
            state.Update(Check(1, .3f), true, 0);
            state.Update(Check(1, .3f), false, .1);
            Assert.That(state.Visible, Is.False, "cancelled: no result, hidden at once");
            Assert.That(state.TryPress(), Is.False);
            state.Update(Check(2, .1f), true, 5);
            Assert.That(state.Visible && !state.Pressed && !state.Result.HasValue, Is.True);
            state.Update(Check(2, Duration + SyncCheckMath.ToleranceSeconds + .01f), true, 6);
            Assert.That(state.TryPress(), Is.False, "too late: the server has already judged it a Fail");
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
            Assert.That(RulesV2Preview.RoleOf("sync-check"), Is.EqualTo(Role.Survivor));
        }
    }
}
