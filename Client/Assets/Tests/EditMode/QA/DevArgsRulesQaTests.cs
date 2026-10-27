using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a2.md section 4 "Flags": -ptk-wander <seconds> and -ptk-rematch (no value). Invalid or
    // missing values disable that flag and add an error (DevArgs contract); both flags are inert when absent.
    [Category("U4bQaA2")]
    public sealed class DevArgsRulesQaTests
    {
        private static DevArgs Parse(params string[] args) => DevArgs.Parse(args);

        // ---- inert when absent -----------------------------------------------------------------------------------------------

        [Test]
        public void WithoutTheTwoFlagsBothStayOffAndTheArgsAreInert()
        {
            DevArgs plain = Parse("PingTheKiller.exe", "-screen-width", "1280", "-batchmode");
            Assert.That(plain.Wander, Is.EqualTo(0f));
            Assert.That(plain.Rematch, Is.False);
            Assert.That(plain.Active, Is.False);
            Assert.That(plain.Errors, Is.Empty);
            DevArgs older = Parse("-ptk-offline", "-ptk-bots", "6", "-ptk-role", "survivor", "-ptk-start", "-ptk-hold-w", "3", "-ptk-shots", "shots", "-ptk-quit-after", "20");
            Assert.That(older.Wander, Is.EqualTo(0f), "-ptk-hold-w does not turn wander on");
            Assert.That(older.Rematch, Is.False, "-ptk-start does not turn rematch on");
            Assert.That(older.Errors, Is.Empty);
            Assert.That(DevArgs.Parse(null).Wander, Is.EqualTo(0f));
            Assert.That(DevArgs.Parse(null).Rematch, Is.False);
            Assert.That(Parse().Active, Is.False);
        }

        [Test]
        public void NonPtkLookalikesDoNotSetEitherFlag()
        {
            DevArgs a = Parse("-wander", "10", "-rematch", "ptk-wander", "30");
            Assert.That(a.Wander, Is.EqualTo(0f));
            Assert.That(a.Rematch, Is.False);
            Assert.That(a.Active, Is.False);
            Assert.That(a.Errors, Is.Empty);
        }

        // ---- -ptk-wander valid ------------------------------------------------------------------------------------------------

        [TestCase("600", 600f)]
        [TestCase("1", 1f)]
        [TestCase("12.5", 12.5f)]
        [TestCase("0.5", 0.5f)]
        [TestCase("720", 720f)]
        public void WanderTakesPositiveSecondsAndActivatesTheCapture(string value, float expected)
        {
            DevArgs a = Parse("-ptk-wander", value);
            Assert.That(a.Wander, Is.EqualTo(expected));
            Assert.That(a.Active, Is.True, "wander alone activates the dev capture component");
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void WanderReadsDecimalPointsInTheInvariantCultureOnly()
        {
            DevArgs a = Parse("-ptk-wander", "2.5");
            Assert.That(a.Wander, Is.EqualTo(2.5f), "a dot is the decimal separator whatever the machine locale");
            DevArgs comma = Parse("-ptk-wander", "2,5");
            Assert.That(comma.Wander, Is.EqualTo(0f), "a comma is not accepted");
            Assert.That(comma.Errors, Has.Count.EqualTo(1));
        }

        // ---- -ptk-wander invalid or missing ----------------------------------------------------------------------------------

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("-600")]
        [TestCase("abc")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        [TestCase("1e999")]
        [TestCase("ten")]
        public void WanderRejectsNonPositiveAndNonNumericValuesAndSetsNothing(string value)
        {
            DevArgs a = Parse("-ptk-wander", value);
            Assert.That(a.Wander, Is.EqualTo(0f), "value " + value);
            Assert.That(a.Active, Is.False, "an invalid value must not activate the capture");
            Assert.That(a.Errors, Has.Count.EqualTo(1));
            Assert.That(a.Errors[0], Does.Contain("-ptk-wander"));
        }

        [Test]
        public void WanderAtTheEndOfTheLineOrBeforeAnotherFlagNeedsAValue()
        {
            DevArgs last = Parse("-ptk-start", "-ptk-wander");
            Assert.That(last.Wander, Is.EqualTo(0f));
            Assert.That(last.Errors, Has.Count.EqualTo(1));
            Assert.That(last.Errors[0], Does.Contain("-ptk-wander").And.Contain("needs a value"));
            Assert.That(last.Start, Is.True, "the other flag is unaffected");

            DevArgs before = Parse("-ptk-wander", "-ptk-start");
            Assert.That(before.Wander, Is.EqualTo(0f));
            Assert.That(before.Start, Is.True, "the next flag is not swallowed as the value");
            Assert.That(before.Errors, Has.Count.EqualTo(1));

            DevArgs blank = Parse("-ptk-wander", "  ");
            Assert.That(blank.Wander, Is.EqualTo(0f));
            Assert.That(blank.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void AnInvalidWanderDoesNotDisturbTheOtherFlags()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-wander", "oops", "-ptk-role", "survivor", "-ptk-hold-w", "3");
            Assert.That(a.Offline, Is.True);
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Survivor));
            Assert.That(a.HoldW, Is.EqualTo(3f));
            Assert.That(a.Wander, Is.EqualTo(0f));
            Assert.That(a.Errors, Has.Count.EqualTo(1));
        }

        // ---- -ptk-rematch ----------------------------------------------------------------------------------------------------

        [Test]
        public void RematchTakesNoValueActivatesTheCaptureAndLeavesTheRestUnset()
        {
            DevArgs a = Parse("-ptk-rematch");
            Assert.That(a.Rematch, Is.True);
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Wander, Is.EqualTo(0f));
            Assert.That(a.Start, Is.False);
            Assert.That(a.Offline, Is.False);
            Assert.That(a.Join, Is.Null);
        }

        [Test]
        public void RematchDoesNotConsumeTheNextFlagAsAValue()
        {
            DevArgs a = Parse("-ptk-rematch", "-ptk-start", "-ptk-wander", "30");
            Assert.That(a.Rematch, Is.True);
            Assert.That(a.Start, Is.True);
            Assert.That(a.Wander, Is.EqualTo(30f));
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void RematchWorksAtTheEndAndTheStartOfTheLine()
        {
            Assert.That(Parse("-ptk-rematch", "-ptk-offline").Rematch, Is.True);
            Assert.That(Parse("-ptk-offline", "-ptk-rematch").Rematch, Is.True);
            Assert.That(Parse("PingTheKiller.exe", "-batchmode", "-ptk-rematch").Rematch, Is.True);
        }

        [Test]
        public void RematchIsExactAndCaseSensitiveLikeEveryOtherFlag()
        {
            DevArgs upper = Parse("-ptk-REMATCH");
            Assert.That(upper.Rematch, Is.False);
            Assert.That(upper.Errors, Has.Count.EqualTo(1));
            DevArgs longer = Parse("-ptk-rematch2");
            Assert.That(longer.Rematch, Is.False);
            Assert.That(longer.Errors, Has.Count.EqualTo(1));
            Assert.That(longer.Errors[0], Does.Contain("Unknown flag"));
            DevArgs twice = Parse("-ptk-rematch", "-ptk-rematch");
            Assert.That(twice.Rematch, Is.True);
            Assert.That(twice.Errors, Is.Empty);
        }

        // ---- the brief's run (a) command line ----------------------------------------------------------------------------------

        [Test]
        public void TheSurvivorRunCommandLineOfTheBriefParsesCleanly()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-bots", "6", "-ptk-role", "survivor", "-ptk-start", "-ptk-wander", "600", "-ptk-rematch",
                "-ptk-hold-w", "3", "-ptk-shots", "shots", "-ptk-quit-after", "720");
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Bots, Is.EqualTo(6));
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Survivor));
            Assert.That(a.Start, Is.True);
            Assert.That(a.Wander, Is.EqualTo(600f));
            Assert.That(a.Rematch, Is.True);
            Assert.That(a.HoldW, Is.EqualTo(3f));
            Assert.That(a.Shots, Is.EqualTo("shots"));
            Assert.That(a.QuitAfter, Is.EqualTo(720f));
            Assert.That(a.Active, Is.True);
        }

        [Test]
        public void TheOnlineRunCommandLineKeepsJoinInsteadOfOffline()
        {
            DevArgs a = Parse("-ptk-join", "127.0.0.1:7777", "-ptk-role", "survivor", "-ptk-start", "-ptk-wander", "600", "-ptk-rematch", "-ptk-hold-w", "3");
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Join, Is.EqualTo("127.0.0.1:7777"));
            Assert.That(a.Offline, Is.False);
            Assert.That(a.Wander, Is.EqualTo(600f));
            Assert.That(a.Rematch, Is.True);
        }

        [Test]
        public void TheKillerRunCommandLineSetsNeitherWanderNorRematch()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-bots", "6", "-ptk-role", "killer", "-ptk-start", "-ptk-shots", "shots");
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Wander, Is.EqualTo(0f));
            Assert.That(a.Rematch, Is.False);
        }
    }
}
