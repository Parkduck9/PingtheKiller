using System.Linq;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4-a3.md "Dev capture mode": a pure parser over the command line, inert without -ptk-* flags.
    public sealed class DevArgsQaTests
    {
        private static DevArgs Parse(params string[] args) => DevArgs.Parse(args);

        private static void AssertInert(DevArgs a)
        {
            Assert.That(a.Active, Is.False, "Active");
            Assert.That(a.Join, Is.Null);
            Assert.That(a.Bots, Is.LessThan(0), "Bots must be unset");
            Assert.That(a.RoleChoice.HasValue, Is.False);
            Assert.That(a.Start, Is.False);
            Assert.That(a.HoldW, Is.EqualTo(0f));
            Assert.That(a.Shots, Is.Null);
            Assert.That(a.QuitAfter, Is.EqualTo(0f));
        }

        // ---- inert ---------------------------------------------------------------------------------------------------

        [Test]
        public void NullAndEmptyArgumentListsAreInert()
        {
            AssertInert(DevArgs.Parse(null));
            AssertInert(Parse());
            Assert.That(Parse().Errors, Is.Empty);
        }

        [Test]
        public void OrdinaryPlayerCommandLineIsInertAndProducesNoErrors()
        {
            DevArgs a = Parse(@"C:\Games\PingTheKiller\PingTheKiller.exe", "-screen-fullscreen", "0", "-screen-width", "1280",
                "-screen-height", "720", "-batchmode", "-logFile", "x.log");
            AssertInert(a);
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void NonPtkFlagsWithPtkLookingValuesAreIgnored()
        {
            DevArgs a = Parse("-other", "ptk-join", "127.0.0.1:7777");
            AssertInert(a);
            Assert.That(a.Errors, Is.Empty);
        }

        // ---- each flag, valid ----------------------------------------------------------------------------------------

        [Test]
        public void JoinWithValidEndpointIsAccepted()
        {
            DevArgs a = Parse("-ptk-join", "192.168.0.10:7777");
            Assert.That(a.Join, Is.EqualTo("192.168.0.10:7777"));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("0")]
        [TestCase("3")]
        [TestCase("7")]
        public void BotsInRangeAreAccepted(string value)
        {
            DevArgs a = Parse("-ptk-bots", value);
            Assert.That(a.Bots, Is.EqualTo(int.Parse(value)));
            Assert.That(a.Active, Is.True, "zero bots is still a request");
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("killer", (byte)RoleChoice.Killer)]
        [TestCase("survivor", (byte)RoleChoice.Survivor)]
        [TestCase("random", (byte)RoleChoice.Random)]
        [TestCase("Killer", (byte)RoleChoice.Killer)]
        public void RoleValuesMapToTheProtocolRoleChoice(string value, byte expected)
        {
            DevArgs a = Parse("-ptk-role", value);
            Assert.That(a.RoleChoice, Is.EqualTo(expected));
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void StartIsAFlagWithoutAValue()
        {
            DevArgs a = Parse("-ptk-start");
            Assert.That(a.Start, Is.True);
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void StartDoesNotConsumeTheNextArgument()
        {
            DevArgs a = Parse("-ptk-start", "-ptk-bots", "2");
            Assert.That(a.Start, Is.True);
            Assert.That(a.Bots, Is.EqualTo(2));
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("3", 3f)]
        [TestCase("2.5", 2.5f)]
        [TestCase("0.5", 0.5f)]
        public void HoldWAcceptsPositiveSeconds(string value, float expected)
        {
            DevArgs a = Parse("-ptk-hold-w", value);
            Assert.That(a.HoldW, Is.EqualTo(expected).Within(1e-5f));
            Assert.That(a.Errors, Is.Empty);
        }

        [TestCase("8", 8f)]
        [TestCase("12.25", 12.25f)]
        public void QuitAfterAcceptsPositiveSeconds(string value, float expected)
        {
            DevArgs a = Parse("-ptk-quit-after", value);
            Assert.That(a.QuitAfter, Is.EqualTo(expected).Within(1e-5f));
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void ShotsKeepsTheDirectoryIncludingSpacesAndBackslashes()
        {
            DevArgs a = Parse("-ptk-shots", @"C:\temp\ptk shots\run 1");
            Assert.That(a.Shots, Is.EqualTo(@"C:\temp\ptk shots\run 1"));
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void FullCaptureCommandLineSetsEveryField()
        {
            DevArgs a = Parse("PingTheKiller.exe", "-screen-fullscreen", "0", "-ptk-join", "127.0.0.1:7777", "-ptk-role", "survivor",
                "-ptk-bots", "6", "-ptk-start", "-ptk-hold-w", "3", "-ptk-shots", "shots", "-ptk-quit-after", "8");
            Assert.That(a.Active, Is.True);
            Assert.That(a.Join, Is.EqualTo("127.0.0.1:7777"));
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Survivor));
            Assert.That(a.Bots, Is.EqualTo(6));
            Assert.That(a.Start, Is.True);
            Assert.That(a.HoldW, Is.EqualTo(3f));
            Assert.That(a.Shots, Is.EqualTo("shots"));
            Assert.That(a.QuitAfter, Is.EqualTo(8f));
            Assert.That(a.Errors, Is.Empty);
        }

        // ---- each flag, invalid --------------------------------------------------------------------------------------

        [TestCase("-ptk-join", "not-an-endpoint")]
        [TestCase("-ptk-join", "127.0.0.1")]
        [TestCase("-ptk-join", "127.0.0.1:0")]
        [TestCase("-ptk-join", "127.0.0.1:65536")]
        [TestCase("-ptk-join", "999.1.1.1:7777")]
        [TestCase("-ptk-bots", "8")]
        [TestCase("-ptk-bots", "100")]
        [TestCase("-ptk-bots", "-1")]
        [TestCase("-ptk-bots", "abc")]
        [TestCase("-ptk-bots", "1.5")]
        [TestCase("-ptk-role", "admin")]
        [TestCase("-ptk-role", "kill")]
        [TestCase("-ptk-hold-w", "abc")]
        [TestCase("-ptk-hold-w", "-1")]
        [TestCase("-ptk-hold-w", "NaN")]
        [TestCase("-ptk-hold-w", "Infinity")]
        [TestCase("-ptk-quit-after", "abc")]
        [TestCase("-ptk-quit-after", "-5")]
        [TestCase("-ptk-quit-after", "NaN")]
        public void InvalidValueIsReportedByFlagNameAndNotApplied(string flag, string value)
        {
            DevArgs a = Parse(flag, value);
            Assert.That(a.Errors.Count, Is.EqualTo(1), "one error");
            Assert.That(a.Errors[0], Does.Contain(flag), "error names the flag");
            AssertInert(a);
        }

        [Test]
        public void AnInvalidValueDoesNotStopLaterFlags()
        {
            DevArgs a = Parse("-ptk-bots", "99", "-ptk-role", "killer", "-ptk-quit-after", "5");
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Bots, Is.LessThan(0));
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Killer));
            Assert.That(a.QuitAfter, Is.EqualTo(5f));
        }

        // ---- missing values ------------------------------------------------------------------------------------------

        [TestCase("-ptk-join")]
        [TestCase("-ptk-bots")]
        [TestCase("-ptk-role")]
        [TestCase("-ptk-hold-w")]
        [TestCase("-ptk-shots")]
        [TestCase("-ptk-quit-after")]
        public void FlagAtTheEndOfTheCommandLineWithoutAValueIsAnError(string flag)
        {
            DevArgs a = Parse("PingTheKiller.exe", flag);
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Errors[0], Does.Contain(flag));
            AssertInert(a);
        }

        [TestCase("-ptk-join")]
        [TestCase("-ptk-bots")]
        [TestCase("-ptk-role")]
        [TestCase("-ptk-hold-w")]
        [TestCase("-ptk-shots")]
        [TestCase("-ptk-quit-after")]
        public void AFollowingFlagIsNeverUsedAsTheValueAndStillApplies(string flag)
        {
            DevArgs a = Parse(flag, "-ptk-start");
            Assert.That(a.Errors.Count, Is.EqualTo(1), "the value-less flag is reported");
            Assert.That(a.Errors[0], Does.Contain(flag));
            Assert.That(a.Start, Is.True, "-ptk-start after the broken flag still applies");
            Assert.That(a.Join, Is.Null);
            Assert.That(a.Shots, Is.Null);
            Assert.That(a.Bots, Is.LessThan(0));
            Assert.That(a.RoleChoice.HasValue, Is.False);
            Assert.That(a.HoldW, Is.EqualTo(0f));
            Assert.That(a.QuitAfter, Is.EqualTo(0f));
        }

        [Test]
        public void ValuedFlagFollowedByValuedFlagKeepsTheSecondPair()
        {
            DevArgs a = Parse("-ptk-bots", "-ptk-role", "killer");
            Assert.That(a.Bots, Is.LessThan(0));
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Killer));
            Assert.That(a.Errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void EmptyOrWhitespaceValueCountsAsMissing()
        {
            DevArgs a = Parse("-ptk-shots", "  ", "-ptk-start");
            Assert.That(a.Shots, Is.Null);
            Assert.That(a.Start, Is.True);
            Assert.That(a.Errors.Count, Is.EqualTo(1));
        }

        // ---- unknown flags -------------------------------------------------------------------------------------------

        [Test]
        public void UnknownPtkFlagIsReportedAndIgnored()
        {
            DevArgs a = Parse("-ptk-bogus");
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Errors[0], Does.Contain("-ptk-bogus"));
            AssertInert(a);
        }

        [Test]
        public void UnknownPtkFlagDoesNotBreakKnownFlagsAroundIt()
        {
            DevArgs a = Parse("-ptk-bots", "2", "-ptk-bogus", "-ptk-role", "killer");
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(a.Errors.Single(), Does.Contain("-ptk-bogus"));
            Assert.That(a.Bots, Is.EqualTo(2));
            Assert.That(a.RoleChoice, Is.EqualTo((byte)RoleChoice.Killer));
        }

        [Test]
        public void UnknownPtkFlagIsNotConfusedWithAKnownPrefix()
        {
            DevArgs a = Parse("-ptk-joined", "127.0.0.1:7777", "-ptk-bots2", "3");
            Assert.That(a.Join, Is.Null);
            Assert.That(a.Bots, Is.LessThan(0));
            Assert.That(a.Errors.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ParsingTwiceGivesIndependentResults()
        {
            DevArgs a = Parse("-ptk-bots", "99");
            DevArgs b = Parse("-ptk-start");
            Assert.That(a.Errors.Count, Is.EqualTo(1));
            Assert.That(b.Errors, Is.Empty);
            Assert.That(b.Start, Is.True);
            Assert.That(a.Start, Is.False);
        }
    }
}
