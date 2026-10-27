using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 5: -ptk-offline and -ptk-preview-result <SurvivorEscaped|AllSurvivorsDown|TimeUp>.
    // Flags stay inert when absent; the earlier DevArgsQaTests (U4) keep covering the older flags.
    public sealed class DevArgsOfflineQaTests
    {
        private static DevArgs Parse(params string[] args) => DevArgs.Parse(args);

        // ---- inert when absent ------------------------------------------------------------------------------------------

        [Test]
        public void WithoutTheNewFlagsNothingIsSetAndTheArgsAreInert()
        {
            DevArgs none = Parse("PingTheKiller.exe", "-screen-width", "1280", "-batchmode");
            Assert.That(none.Offline, Is.False);
            Assert.That(none.PreviewResult.HasValue, Is.False);
            Assert.That(none.Active, Is.False);
            Assert.That(none.Errors, Is.Empty);
            DevArgs older = Parse("-ptk-bots", "3", "-ptk-role", "killer", "-ptk-start");
            Assert.That(older.Offline, Is.False, "the old flags do not turn offline on");
            Assert.That(older.PreviewResult.HasValue, Is.False);
            Assert.That(DevArgs.Parse(null).Offline, Is.False);
            Assert.That(DevArgs.Parse(null).PreviewResult.HasValue, Is.False);
        }

        // ---- -ptk-offline -----------------------------------------------------------------------------------------------

        [Test]
        public void OfflineAloneIsActiveTakesNoValueAndLeavesTheRestUnset()
        {
            DevArgs a = Parse("-ptk-offline");
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Active, Is.True, "-ptk-offline alone activates the dev capture component");
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Join, Is.Null);
            Assert.That(a.Bots, Is.LessThan(0));
            Assert.That(a.RoleChoice.HasValue, Is.False);
            Assert.That(a.Start, Is.False);
            Assert.That(a.PreviewResult.HasValue, Is.False);
        }

        [Test]
        public void OfflineDoesNotConsumeTheNextFlagAsAValue()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-bots", "7");
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Bots, Is.EqualTo(7));
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void OfflineCombinesWithEveryOtherCaptureFlag()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-bots", "7", "-ptk-role", "survivor", "-ptk-start", "-ptk-hold-w", "3",
                "-ptk-shots", "shots", "-ptk-quit-after", "12", "-ptk-preview-result", "TimeUp");
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Bots, Is.EqualTo(7));
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Survivor));
            Assert.That(a.Start, Is.True);
            Assert.That(a.HoldW, Is.EqualTo(3f));
            Assert.That(a.Shots, Is.EqualTo("shots"));
            Assert.That(a.QuitAfter, Is.EqualTo(12f));
            Assert.That(a.PreviewResult, Is.EqualTo((byte)GameEndReason.TimeUp));
        }

        [Test]
        public void OfflineWorksAnywhereOnTheCommandLine()
        {
            Assert.That(Parse("-ptk-bots", "2", "-ptk-offline").Offline, Is.True);
            Assert.That(Parse("PingTheKiller.exe", "-batchmode", "-ptk-offline", "-nographics").Offline, Is.True);
        }

        [Test]
        public void OfflineAndJoinMayBothBeSetAndNeitherHidesTheOther()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-join", "127.0.0.1:7777");
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Join, Is.EqualTo("127.0.0.1:7777"));
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void ARepeatedOfflineFlagIsHarmless()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-offline");
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        // ---- -ptk-preview-result ----------------------------------------------------------------------------------------

        [TestCase("SurvivorEscaped", (byte)GameEndReason.SurvivorEscaped)]
        [TestCase("AllSurvivorsDown", (byte)GameEndReason.AllSurvivorsDown)]
        [TestCase("TimeUp", (byte)GameEndReason.TimeUp)]
        [TestCase("survivorescaped", (byte)GameEndReason.SurvivorEscaped)]
        [TestCase("ALLSURVIVORSDOWN", (byte)GameEndReason.AllSurvivorsDown)]
        [TestCase("timeup", (byte)GameEndReason.TimeUp)]
        public void APreviewReasonIsAcceptedAndMapsToTheGameEndReason(string value, byte expected)
        {
            DevArgs a = Parse("-ptk-preview-result", value);
            Assert.That(a.Errors, Is.Empty, value);
            Assert.That(a.PreviewResult.HasValue, Is.True, value);
            Assert.That(a.PreviewResult.Value, Is.EqualTo(expected), value);
            Assert.That(a.Active, Is.True, "the flag activates dev capture");
            Assert.That(a.Offline, Is.False, "the preview flag does not imply offline");
        }

        [TestCase("Victory")]
        [TestCase("survivor")]
        [TestCase("TimeOut")]
        [TestCase("Time Up")]
        [TestCase("0")]
        [TestCase("1")]
        [TestCase("9")]
        [TestCase("-1")]
        public void AnInvalidPreviewReasonIsReportedByFlagNameAndNotApplied(string value)
        {
            DevArgs a = Parse("-ptk-preview-result", value);
            Assert.That(a.PreviewResult.HasValue, Is.False, value);
            Assert.That(a.Errors.Count, Is.EqualTo(1), value);
            Assert.That(a.Errors[0], Does.Contain("-ptk-preview-result"));
            Assert.That(a.Active, Is.False, "an invalid value leaves the capture component off");
        }

        [Test]
        public void AMissingPreviewValueAtTheEndIsAnError()
        {
            DevArgs a = Parse("PingTheKiller.exe", "-ptk-preview-result");
            Assert.That(a.PreviewResult.HasValue, Is.False);
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Errors[0], Does.Contain("-ptk-preview-result"));
            Assert.That(a.Active, Is.False);
        }

        [Test]
        public void AFollowingFlagIsNeverTakenAsThePreviewValueAndStillApplies()
        {
            DevArgs a = Parse("-ptk-preview-result", "-ptk-offline");
            Assert.That(a.PreviewResult.HasValue, Is.False);
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Errors[0], Does.Contain("-ptk-preview-result"));
            Assert.That(a.Offline, Is.True, "the flag after the broken one still applies");
        }

        [Test]
        public void ABlankPreviewValueCountsAsMissing()
        {
            DevArgs a = Parse("-ptk-preview-result", "  ", "-ptk-start");
            Assert.That(a.PreviewResult.HasValue, Is.False);
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Start, Is.True);
        }

        [Test]
        public void AnInvalidPreviewValueDoesNotStopLaterFlags()
        {
            DevArgs a = Parse("-ptk-preview-result", "Victory", "-ptk-offline", "-ptk-bots", "3");
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Bots, Is.EqualTo(3));
        }

        [Test]
        public void TheLastValidPreviewValueWinsAndParsingTwiceIsIndependent()
        {
            DevArgs a = Parse("-ptk-preview-result", "TimeUp", "-ptk-preview-result", "SurvivorEscaped");
            Assert.That(a.PreviewResult, Is.EqualTo((byte)GameEndReason.SurvivorEscaped));
            DevArgs b = Parse("-ptk-offline");
            Assert.That(b.PreviewResult.HasValue, Is.False);
            Assert.That(a.Offline, Is.False);
        }

        [Test]
        public void NonPtkLookalikesAreIgnored()
        {
            DevArgs a = Parse("-offline", "-preview-result", "TimeUp", "ptk-offline");
            Assert.That(a.Offline, Is.False);
            Assert.That(a.PreviewResult.HasValue, Is.False);
            Assert.That(a.Active, Is.False);
            Assert.That(a.Errors, Is.Empty);
        }

        // ---- wiring: dev input never reaches a server -------------------------------------------------------------------

        [Test]
        public void ThePreviewResultIsDispatchedLocallyAndNeverSent()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string preview = QaSource.MethodText(app, "private void Preview(");
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview, Does.Not.Contain(".Send("), "the synthetic result is never sent");
            Assert.That(preview, Does.Contain("EndMatch(result, true)"), "it takes the local end-of-match path flagged as a preview");
            string dev = QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs"));
            Assert.That(dev, Does.Contain("PreviewResult?.Invoke("));
        }

        [Test]
        public void TheDevComponentIsOnlyCreatedWhenAFlagIsPresent()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(app, Does.Contain("if (devArgs.Active)"));
            Assert.That(app, Does.Contain("AddComponent<DevCapture>()"));
        }

        [Test]
        public void TheSyntheticResultIsMarkedAsAPreviewOnScreen()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(app, Does.Contain("screens.ShowResult("));
            Assert.That(app, Does.Contain(", preview);"), "the preview flag reaches the result screen");
            string screens = QaSource.StripComments(QaPaths.ReadScript("UI/ClientScreens.cs"));
            Assert.That(screens, Does.Contain("Preview: synthetic result from a dev flag, not sent by a server."));
            Assert.That(screens, Does.Contain("Show(resultPreview, preview)"));
        }
    }
}
