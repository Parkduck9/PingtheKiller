using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Tests
{
    public sealed class DevArgsTests
    {
        [Test]
        public void NoFlagsIsInert()
        {
            DevArgs args = DevArgs.Parse(new[] { "PingTheKiller.exe", "-screen-width", "1280", "-batchmode" });
            Assert.That(args.Active, Is.False);
            Assert.That(args.Errors, Is.Empty);
            Assert.That(DevArgs.Parse(null).Active, Is.False);
        }

        [Test]
        public void AllValidFlagsAreParsed()
        {
            DevArgs args = DevArgs.Parse(new[]
            {
                "PingTheKiller.exe", "-screen-fullscreen", "0", "-ptk-join", "127.0.0.1:7777", "-ptk-bots", "6", "-ptk-role", "Killer",
                "-ptk-start", "-ptk-hold-w", "2.5", "-ptk-shots", @"C:\shots dir", "-ptk-quit-after", "8"
            });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Active, Is.True);
            Assert.That(args.Join, Is.EqualTo("127.0.0.1:7777"));
            Assert.That(args.Bots, Is.EqualTo(6));
            Assert.That(args.RoleChoice, Is.EqualTo((byte)RoleChoice.Killer));
            Assert.That(args.Start, Is.True);
            Assert.That(args.HoldW, Is.EqualTo(2.5f));
            Assert.That(args.Shots, Is.EqualTo(@"C:\shots dir"));
            Assert.That(args.QuitAfter, Is.EqualTo(8f));
        }

        [TestCase("survivor", (byte)RoleChoice.Survivor)]
        [TestCase("random", (byte)RoleChoice.Random)]
        [TestCase("KILLER", (byte)RoleChoice.Killer)]
        public void RoleNamesMapToRoleChoice(string value, byte expected)
        {
            Assert.That(DevArgs.Parse(new[] { "-ptk-role", value }).RoleChoice, Is.EqualTo(expected));
        }

        [TestCase("-ptk-join", "localhost:7777")]
        [TestCase("-ptk-join", "127.0.0.1:0")]
        [TestCase("-ptk-bots", "8")]
        [TestCase("-ptk-bots", "-1")]
        [TestCase("-ptk-bots", "two")]
        [TestCase("-ptk-role", "ghost")]
        [TestCase("-ptk-hold-w", "0")]
        [TestCase("-ptk-hold-w", "NaN")]
        [TestCase("-ptk-hold-w", "Infinity")]
        [TestCase("-ptk-quit-after", "-3")]
        public void InvalidValueDisablesTheFlagAndReportsIt(string flag, string value)
        {
            DevArgs args = DevArgs.Parse(new[] { flag, value });
            Assert.That(args.Active, Is.False);
            Assert.That(args.Errors.Count, Is.EqualTo(1));
            Assert.That(args.Errors[0], Does.Contain(flag));
        }

        [Test]
        public void MissingValueDoesNotSwallowTheNextFlag()
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-join", "-ptk-start", "-ptk-shots" });
            Assert.That(args.Join, Is.Null);
            Assert.That(args.Shots, Is.Null);
            Assert.That(args.Start, Is.True);
            Assert.That(args.Errors.Count, Is.EqualTo(2));
        }

        [Test]
        public void OfflineFlagNeedsNoValueAndCombinesWithTheCaptureFlags()
        {
            DevArgs args = DevArgs.Parse(new[]
            {
                "PingTheKiller.exe", "-ptk-offline", "-ptk-bots", "7", "-ptk-role", "survivor", "-ptk-start", "-ptk-hold-w", "3",
                "-ptk-shots", "shots", "-ptk-quit-after", "12", "-ptk-preview-result", "TimeUp"
            });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Offline, Is.True);
            Assert.That(args.Join, Is.Null);
            Assert.That(args.Bots, Is.EqualTo(7));
            Assert.That(args.RoleChoice, Is.EqualTo((byte)RoleChoice.Survivor));
            Assert.That(args.Start, Is.True);
            Assert.That(args.HoldW, Is.EqualTo(3f));
            Assert.That(args.QuitAfter, Is.EqualTo(12f));
            Assert.That(args.PreviewResult, Is.EqualTo((byte)GameEndReason.TimeUp));
            Assert.That(DevArgs.Parse(new[] { "-ptk-offline" }).Active, Is.True);
            Assert.That(DevArgs.Parse(new[] { "-ptk-offline", "-ptk-start" }).Start, Is.True, "-ptk-offline never swallows the next flag");
        }

        [TestCase("SurvivorEscaped", GameEndReason.AllResolved)]
        [TestCase("allsurvivorsdown", GameEndReason.AllSurvivorsDown)]
        [TestCase("TIMEUP", GameEndReason.TimeUp)]
        [TestCase("AllResolved", GameEndReason.AllResolved)]
        [TestCase("collapse", GameEndReason.Collapse)]
        public void PreviewResultNamesMapToGameEndReason(string value, GameEndReason expected)
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-preview-result", value });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Active, Is.True);
            Assert.That(args.PreviewResult, Is.EqualTo((byte)expected));
        }

        [TestCase("Draw")]
        [TestCase("0")]
        [TestCase("-ptk-start")]
        public void InvalidOrMissingPreviewResultIsReported(string value)
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-preview-result", value });
            Assert.That(args.PreviewResult.HasValue, Is.False);
            Assert.That(args.Errors.Count, Is.EqualTo(1));
            Assert.That(args.Errors[0], Does.Contain("-ptk-preview-result"));
        }

        [Test]
        public void NewFlagsStayInertWhenAbsent()
        {
            DevArgs args = DevArgs.Parse(new[] { "PingTheKiller.exe", "-screen-width", "1280" });
            Assert.That(args.Offline, Is.False);
            Assert.That(args.PreviewResult.HasValue, Is.False);
            Assert.That(args.Active, Is.False);
        }

        [Test]
        public void WanderAndRematchParseAndCombineWithTheRunFlags()
        {
            DevArgs args = DevArgs.Parse(new[]
            {
                "PingTheKiller.exe", "-ptk-offline", "-ptk-bots", "6", "-ptk-role", "survivor", "-ptk-start", "-ptk-wander", "600",
                "-ptk-rematch", "-ptk-hold-w", "3", "-ptk-shots", "shots", "-ptk-quit-after", "720"
            });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Wander, Is.EqualTo(600f));
            Assert.That(args.Rematch, Is.True);
            Assert.That(args.HoldW, Is.EqualTo(3f));
            Assert.That(args.QuitAfter, Is.EqualTo(720f));
            Assert.That(DevArgs.Parse(new[] { "-ptk-rematch" }).Active, Is.True);
            Assert.That(DevArgs.Parse(new[] { "-ptk-wander", "1.5" }).Active, Is.True);
            Assert.That(DevArgs.Parse(new[] { "-ptk-rematch", "-ptk-start" }).Start, Is.True, "-ptk-rematch never swallows the next flag");
        }

        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("soon")]
        [TestCase("-ptk-start")]
        public void InvalidOrMissingWanderIsReported(string value)
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-wander", value });
            Assert.That(args.Wander, Is.EqualTo(0f));
            Assert.That(args.Errors.Count, Is.EqualTo(1));
            Assert.That(args.Errors[0], Does.Contain("-ptk-wander"));
        }

        [Test]
        public void WanderAndRematchStayInertWhenAbsent()
        {
            DevArgs args = DevArgs.Parse(new[] { "PingTheKiller.exe", "-ptk-offline" });
            Assert.That(args.Wander, Is.EqualTo(0f));
            Assert.That(args.Rematch, Is.False);
        }

        [Test]
        public void UnknownFlagIsReportedAndIgnored()
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-fly", "-ptk-bots", "3" });
            Assert.That(args.Bots, Is.EqualTo(3));
            Assert.That(args.Errors.Count, Is.EqualTo(1));
            Assert.That(args.Errors[0], Does.Contain("-ptk-fly"));
        }

        [Test]
        public void CameraCaptureFlagsParse()
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-offline", "-ptk-orbit", "180", "-ptk-killer-look", "Nearest", "-ptk-menu", "5" });
            Assert.That(args.Errors, Is.Empty);
            Assert.That(args.Orbit, Is.EqualTo(180f));
            Assert.That(args.Look, Is.EqualTo(KillerLook.Nearest));
            Assert.That(args.Menu, Is.EqualTo(5f));
            Assert.That(DevArgs.Parse(new[] { "-ptk-killer-look", "away" }).Look, Is.EqualTo(KillerLook.Away));
            DevArgs occluded = DevArgs.Parse(new[] { "-ptk-killer-look", "Occluded" });
            Assert.That(occluded.Errors, Is.Empty);
            Assert.That(occluded.Look, Is.EqualTo(KillerLook.Occluded));
            Assert.That(occluded.Active, Is.True);
            DevArgs walk = DevArgs.Parse(new[] { "-ptk-killer-look", "70", "-ptk-killer-look", "occluded" });
            Assert.That(walk.Look, Is.EqualTo(KillerLook.Occluded));
            Assert.That(walk.LookYaw, Is.EqualTo(70f), "an earlier number is the yaw held until a blip is behind a wall");
            DevArgs fixedYaw = DevArgs.Parse(new[] { "-ptk-killer-look", "-90" });
            Assert.That(fixedYaw.Errors, Is.Empty, "a negative yaw is a value, not a flag");
            Assert.That(fixedYaw.Look, Is.EqualTo(KillerLook.Fixed));
            Assert.That(fixedYaw.LookYaw, Is.EqualTo(-90f));
            Assert.That(DevArgs.Parse(new[] { "-ptk-orbit", "-45" }).Orbit, Is.EqualTo(-45f));
            Assert.That(DevArgs.Parse(new[] { "-ptk-orbit", "0" }).Active, Is.True, "0 is a valid offset");
        }

        [TestCase("-ptk-orbit", "behind")]
        [TestCase("-ptk-orbit", "NaN")]
        [TestCase("-ptk-killer-look", "left")]
        [TestCase("-ptk-killer-look", "Infinity")]
        [TestCase("-ptk-menu", "0")]
        [TestCase("-ptk-menu", "soon")]
        public void InvalidCameraCaptureValuesAreReported(string flag, string value)
        {
            DevArgs args = DevArgs.Parse(new[] { flag, value });
            Assert.That(args.Errors.Count, Is.EqualTo(1));
            Assert.That(args.Errors[0], Does.Contain(flag));
            Assert.That(args.Orbit, Is.Null);
            Assert.That(args.Look, Is.EqualTo(KillerLook.None));
            Assert.That(args.Menu, Is.EqualTo(0f));
            Assert.That(args.Active, Is.False);
        }

        [Test]
        public void CameraCaptureFlagsStayInertWhenAbsent()
        {
            DevArgs args = DevArgs.Parse(new[] { "PingTheKiller.exe", "-ptk-offline" });
            Assert.That(args.Orbit, Is.Null);
            Assert.That(args.Look, Is.EqualTo(KillerLook.None));
            Assert.That(args.Menu, Is.EqualTo(0f));
        }

        // U4d: -ptk-skill-at <s>, -ptk-chase <s>, -ptk-rescue. Inert when absent; invalid values add an error and stay off.
        [Test]
        public void TheSelfStateShotFlagsParseAndStayInertWhenAbsent()
        {
            DevArgs none = DevArgs.Parse(new[] { "PingTheKiller.exe", "-ptk-offline" });
            Assert.That(none.SkillAt, Is.EqualTo(0f));
            Assert.That(none.Chase, Is.EqualTo(0f));
            Assert.That(none.Rescue, Is.False);
            Assert.That(DevArgs.Parse(new[] { "PingTheKiller.exe" }).Active, Is.False);

            DevArgs a = DevArgs.Parse(new[] { "x", "-ptk-skill-at", "3.5", "-ptk-chase", "120", "-ptk-rescue" });
            Assert.That(a.Errors, Is.Empty);
            Assert.That(a.SkillAt, Is.EqualTo(3.5f));
            Assert.That(a.Chase, Is.EqualTo(120f));
            Assert.That(a.Rescue, Is.True);
            Assert.That(a.Active, Is.True);
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-rescue" }).Active, Is.True, "the rescue flag alone activates the dev component");
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-skill-at", "2" }).Active, Is.True);
            Assert.That(DevArgs.Parse(new[] { "x", "-ptk-chase", "2" }).Active, Is.True);

            foreach (string bad in new[] { "0", "-1", "NaN", "Infinity", "soon" })
            {
                DevArgs b = DevArgs.Parse(new[] { "x", "-ptk-skill-at", bad, "-ptk-chase", bad });
                Assert.That(b.SkillAt, Is.EqualTo(0f), bad);
                Assert.That(b.Chase, Is.EqualTo(0f), bad);
                Assert.That(b.Errors.Count, Is.EqualTo(2), bad);
                Assert.That(b.Active, Is.False, bad);
            }
            DevArgs missing = DevArgs.Parse(new[] { "x", "-ptk-skill-at", "-ptk-rescue" });
            Assert.That(missing.SkillAt, Is.EqualTo(0f));
            Assert.That(missing.Rescue, Is.True, "a flag never swallows the next -ptk- flag as its value");
            Assert.That(missing.Errors, Has.Count.EqualTo(1));
        }
    }
}
