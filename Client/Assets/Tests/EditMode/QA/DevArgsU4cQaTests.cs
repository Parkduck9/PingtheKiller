using System.Linq;
using NUnit.Framework;
using PingTheKiller.Client.Dev;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test item 9. docs/briefs/U4c-a1.md section 7: -ptk-orbit <deg>, -ptk-killer-look nearest|away|<deg>,
    // and the extra -ptk-menu <seconds>. Every flag is inert when absent; an invalid or missing value disables only that flag
    // and adds an error.
    [Category("U4cQaA1")]
    public sealed class DevArgsU4cQaTests
    {
        private static DevArgs Parse(params string[] args) => DevArgs.Parse(args);

        // ---- inert when absent ------------------------------------------------------------------------------------------------

        [Test]
        public void WithoutTheNewFlagsNothingIsSetAndTheArgumentsStayInert()
        {
            foreach (DevArgs a in new[] { Parse(), DevArgs.Parse(null), Parse("game.exe", "-screen-width", "1280", "-batchmode") })
            {
                Assert.That(a.Orbit.HasValue, Is.False);
                Assert.That(a.Look, Is.EqualTo(KillerLook.None));
                Assert.That(a.LookYaw, Is.EqualTo(0f));
                Assert.That(a.Menu, Is.EqualTo(0f));
                Assert.That(a.Active, Is.False);
                Assert.That(a.Errors, Is.Empty);
            }
        }

        [Test]
        public void OtherDevFlagsDoNotTurnTheNewOnesOn()
        {
            DevArgs a = Parse("-ptk-offline", "-ptk-role", "killer", "-ptk-bots", "6", "-ptk-start", "-ptk-shots", "out");
            Assert.That(a.Active, Is.True);
            Assert.That(a.Orbit.HasValue, Is.False);
            Assert.That(a.Look, Is.EqualTo(KillerLook.None));
            Assert.That(a.Menu, Is.EqualTo(0f));
            Assert.That(a.Errors, Is.Empty);
        }

        // ---- -ptk-orbit -------------------------------------------------------------------------------------------------------

        [TestCase("180", 180f)]
        [TestCase("-90", -90f)]
        [TestCase("0", 0f)]
        [TestCase("45.5", 45.5f)]
        [TestCase("+45", 45f)]
        [TestCase("1e2", 100f)]
        [TestCase("720", 720f)]
        [TestCase("  180  ", 180f)]
        public void OrbitAcceptsAnyFiniteNumberOfDegrees(string text, float expected)
        {
            DevArgs a = Parse("-ptk-orbit", text);
            Assert.That(a.Orbit.HasValue, Is.True, "a zero offset is a value, not 'unset'");
            Assert.That(a.Orbit.Value, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("abc")]
        [TestCase("180deg")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        [TestCase("1e999")]
        [TestCase("1,5")]
        [TestCase("--5")]
        public void OrbitRejectsAnythingThatIsNotAFiniteNumber(string text)
        {
            DevArgs a = Parse("-ptk-orbit", text);
            Assert.That(a.Orbit.HasValue, Is.False, text);
            Assert.That(a.Active, Is.False, text);
            Assert.That(a.Errors.Count, Is.EqualTo(1), text);
            Assert.That(a.Errors[0], Does.Contain("-ptk-orbit"));
            Assert.That(a.Errors[0], Does.Contain("invalid value"));
        }

        [Test]
        public void OrbitWithNoValueAtTheEndOrBeforeAnotherFlagIsAnErrorAndTheNextFlagStillApplies()
        {
            DevArgs atEnd = Parse("-ptk-orbit");
            Assert.That(atEnd.Orbit.HasValue, Is.False);
            Assert.That(atEnd.Errors.Single(), Does.Contain("needs a value"));

            DevArgs beforeFlag = Parse("-ptk-orbit", "-ptk-offline");
            Assert.That(beforeFlag.Orbit.HasValue, Is.False);
            Assert.That(beforeFlag.Offline, Is.True, "the following flag is not swallowed as a value");
            Assert.That(beforeFlag.Errors.Single(), Does.Contain("-ptk-orbit needs a value"));

            Assert.That(Parse("-ptk-orbit", "").Errors.Single(), Does.Contain("needs a value"));
            Assert.That(Parse("-ptk-orbit", "   ").Errors.Single(), Does.Contain("needs a value"));
        }

        [Test]
        public void TheLastOrbitWinsAndAnInvalidOneNeverClobbersAValidOne()
        {
            Assert.That(Parse("-ptk-orbit", "90", "-ptk-orbit", "180").Orbit, Is.EqualTo(180f));
            DevArgs a = Parse("-ptk-orbit", "90", "-ptk-orbit", "bogus");
            Assert.That(a.Orbit, Is.EqualTo(90f));
            Assert.That(a.Errors.Count, Is.EqualTo(1));
        }

        // ---- -ptk-killer-look -------------------------------------------------------------------------------------------------

        [TestCase("nearest", KillerLook.Nearest)]
        [TestCase("Nearest", KillerLook.Nearest)]
        [TestCase("NEAREST", KillerLook.Nearest)]
        [TestCase("away", KillerLook.Away)]
        [TestCase("Away", KillerLook.Away)]
        [TestCase(" away ", KillerLook.Away)]
        public void KillerLookAcceptsTheTwoNamedModesInAnyCase(string text, KillerLook expected)
        {
            DevArgs a = Parse("-ptk-killer-look", text);
            Assert.That(a.Look, Is.EqualTo(expected));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("45", 45f)]
        [TestCase("-30", -30f)]
        [TestCase("0", 0f)]
        [TestCase("135.25", 135.25f)]
        [TestCase("359.9", 359.9f)]
        [TestCase("720", 720f)]
        public void KillerLookAcceptsAFixedYawInDegrees(string text, float yaw)
        {
            DevArgs a = Parse("-ptk-killer-look", text);
            Assert.That(a.Look, Is.EqualTo(KillerLook.Fixed), "a yaw of 0 is a fixed look, not 'none'");
            Assert.That(a.LookYaw, Is.EqualTo(yaw).Within(1e-4f));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("sideways")]
        [TestCase("nearest,away")]
        [TestCase("towards")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("12abc")]
        public void KillerLookRejectsUnknownWordsAndNonFiniteNumbers(string text)
        {
            DevArgs a = Parse("-ptk-killer-look", text);
            Assert.That(a.Look, Is.EqualTo(KillerLook.None), text);
            Assert.That(a.LookYaw, Is.EqualTo(0f), text);
            Assert.That(a.Active, Is.False, text);
            Assert.That(a.Errors.Single(), Does.Contain("-ptk-killer-look").And.Contain("invalid value"));
        }

        [Test]
        public void KillerLookWithNoValueIsAnErrorAndDoesNotSwallowTheNextFlag()
        {
            DevArgs a = Parse("-ptk-killer-look");
            Assert.That(a.Look, Is.EqualTo(KillerLook.None));
            Assert.That(a.Errors.Single(), Does.Contain("needs a value"));
            DevArgs b = Parse("-ptk-killer-look", "-ptk-menu", "3");
            Assert.That(b.Look, Is.EqualTo(KillerLook.None));
            Assert.That(b.Menu, Is.EqualTo(3f));
            Assert.That(b.Errors.Single(), Does.Contain("-ptk-killer-look needs a value"));
        }

        [Test]
        public void KillerLookLastValidWinsAndAnInvalidOneKeepsTheEarlierMode()
        {
            Assert.That(Parse("-ptk-killer-look", "90", "-ptk-killer-look", "away").Look, Is.EqualTo(KillerLook.Away));
            DevArgs a = Parse("-ptk-killer-look", "away", "-ptk-killer-look", "bogus");
            Assert.That(a.Look, Is.EqualTo(KillerLook.Away));
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            DevArgs b = Parse("-ptk-killer-look", "nearest", "-ptk-killer-look", "60");
            Assert.That(b.Look, Is.EqualTo(KillerLook.Fixed));
            Assert.That(b.LookYaw, Is.EqualTo(60f));
        }

        // ---- -ptk-menu --------------------------------------------------------------------------------------------------------

        [TestCase("3", 3f)]
        [TestCase("0.5", 0.5f)]
        [TestCase("12.25", 12.25f)]
        [TestCase("1e1", 10f)]
        public void MenuAcceptsPositiveSeconds(string text, float expected)
        {
            DevArgs a = Parse("-ptk-menu", text);
            Assert.That(a.Menu, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("0")]
        [TestCase("-2")]
        [TestCase("abc")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("3s")]
        public void MenuRejectsZeroNegativeAndNonNumbers(string text)
        {
            DevArgs a = Parse("-ptk-menu", text);
            Assert.That(a.Menu, Is.EqualTo(0f), text);
            Assert.That(a.Active, Is.False, text);
            Assert.That(a.Errors.Single(), Does.Contain("-ptk-menu").And.Contain("invalid value"));
        }

        [Test]
        public void MenuWithNoValueIsAnError()
        {
            Assert.That(Parse("-ptk-menu").Errors.Single(), Does.Contain("-ptk-menu needs a value"));
            DevArgs a = Parse("-ptk-menu", "-ptk-start");
            Assert.That(a.Start, Is.True);
            Assert.That(a.Menu, Is.EqualTo(0f));
            Assert.That(a.Errors.Single(), Does.Contain("needs a value"));
        }

        // ---- together ---------------------------------------------------------------------------------------------------------

        [Test]
        public void ARealCaptureCommandLineSetsEveryFlagWithoutErrors()
        {
            DevArgs a = Parse(@"C:\Builds\PingTheKiller.exe", "-screen-width", "1280", "-ptk-offline", "-ptk-role", "killer", "-ptk-bots", "6", "-ptk-start",
                "-ptk-killer-look", "nearest", "-ptk-menu", "3", "-ptk-shots", @"C:\shots", "-ptk-quit-after", "20");
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Look, Is.EqualTo(KillerLook.Nearest));
            Assert.That(a.Menu, Is.EqualTo(3f));
            Assert.That(a.QuitAfter, Is.EqualTo(20f));
            DevArgs survivor = Parse("-ptk-offline", "-ptk-role", "survivor", "-ptk-orbit", "180", "-ptk-wander", "10");
            Assert.That(survivor.Errors, Is.Empty);
            Assert.That(survivor.Orbit, Is.EqualTo(180f));
            Assert.That(survivor.Look, Is.EqualTo(KillerLook.None));
        }

        [Test]
        public void AnUnknownPtkFlagNearTheNewOnesIsReportedAndNothingElseChanges()
        {
            DevArgs a = Parse("-ptk-orbits", "180", "-ptk-killer-looks", "away");
            Assert.That(a.Orbit.HasValue, Is.False);
            Assert.That(a.Look, Is.EqualTo(KillerLook.None));
            Assert.That(a.Errors.Count, Is.EqualTo(2));
            foreach (string error in a.Errors) Assert.That(error, Does.StartWith("Unknown flag"));
        }
    }
}
