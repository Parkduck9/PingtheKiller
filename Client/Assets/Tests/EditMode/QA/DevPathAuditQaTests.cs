using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Dev;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 2, test items 4 and 5 (regression after F-05, plus the new -ptk-killer-look occluded value).
    // docs/briefs/U4c-a2.md fix 3: the [DEV] status log and Preview must not read Survivor Id, StatusTimer or Status per entry on
    // the Killer client; the Killer logs blip counts from the tracker instead.
    [Category("U4cQaA2")]
    public sealed class DevPathAuditQaTests
    {
        private static string Stripped(string relative) => QaSource.StripComments(QaPaths.ReadScript(relative));

        [Test]
        public void TheDevStatusLogReturnsBeforeItsFirstEntryLoopOnAnyClientThatIsNotASurvivor()
        {
            string source = Stripped("Dev/DevCapture.cs");
            string onWorld = QaSource.MethodText(source, "public void OnWorld(");
            Assert.That(onWorld, Is.Not.Null);
            int guard = onWorld.IndexOf("role != Role.Survivor) return;", System.StringComparison.Ordinal);
            int loop = onWorld.IndexOf("foreach (PlayerState player in state.Players)", System.StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "a Survivor-only guard exists");
            Assert.That(loop, Is.GreaterThan(guard), "the guard comes before the entry loop, so the Killer client never reads an entry");
            Assert.That(Regex.Matches(onWorld, @"\.Players\b").Count, Is.EqualTo(1), "OnWorld has exactly one entry loop");
        }

        [Test]
        public void NoOtherDevFileReadsAnEntryOfTheWorldStatePlayerList()
        {
            foreach (string path in Directory.GetFiles(Path.Combine(QaPaths.ScriptsDir(), "Dev"), "*.cs"))
            {
                string name = Path.GetFileName(path);
                string source = QaSource.StripComments(File.ReadAllText(path));
                int loops = Regex.Matches(source, @"\.Players\b").Count;
                Assert.That(loops, Is.EqualTo(name == "DevCapture.cs" ? 1 : 0), name + ": entry loops over the Players list");
            }
        }

        [Test]
        public void TheDevCaptureNeverMentionsAnEntryFieldOutsideTheGuardedLoop()
        {
            string source = Stripped("Dev/DevCapture.cs");
            int guard = source.IndexOf("role != Role.Survivor) return;", System.StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThan(0));
            foreach (Match m in Regex.Matches(source, @"\bplayer\.(Id|Yaw|StatusTimer|CaptureCount|Flags|Status)\b"))
                Assert.That(m.Index, Is.GreaterThan(guard), "player." + m.Groups[1].Value + " at offset " + m.Index + " is before the Survivor-only guard");
        }

        [Test]
        public void TheKillerLogsBlipCountsFromTheTrackerNotFromEntries()
        {
            string source = Stripped("Dev/DevCapture.cs");
            string frame = QaSource.MethodText(source, "public void OnKillerFrame(");
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame, Does.Contain("blips inView="));
            Assert.That(frame, Does.Not.Contain("Players"));
            Assert.That(frame, Does.Not.Match(@"\.(Id|Yaw|StatusTimer|CaptureCount|Flags)\b"));
        }

        [Test]
        public void ThePreviewReadsSurvivorEntryIdsOnlyOnASurvivorClientAndNamesLobbySlotsForTheKiller()
        {
            string source = Stripped("ClientApp.cs");
            string preview = QaSource.MethodText(source, "private void Preview(");
            Assert.That(preview, Is.Not.Null);
            int loop = preview.IndexOf("lastWorld.Players", System.StringComparison.Ordinal);
            Assert.That(loop, Is.GreaterThan(0));
            string before = preview.Substring(0, loop);
            Assert.That(before, Does.Contain("role == Role.Survivor && lastWorld != null"), "the world-entry loop is behind a Survivor-only condition");
            Assert.That(preview, Does.Contain("lobby.Slots"), "the Killer branch uses the lobby slots");
            Assert.That(Regex.Matches(preview, @"\.Players\b").Count, Is.EqualTo(1));
        }

        [Test]
        public void TheKillerBranchOfTheWorldHandlerStillStopsAtItsOwnEntryBeforeAnySurvivorOnlyField()
        {
            string source = Stripped("ClientApp.cs");
            Match own = Regex.Match(source, @"if \(role == Role\.Killer \? player\.Role != \(byte\)Role\.Killer : player\.Id != playerId\) continue;");
            Assert.That(own.Success, Is.True, "the own entry is found by Role on the Killer, by Id on a Survivor (no Id read for the Killer)");
            int breakAt = source.IndexOf("if (role == Role.Killer) break;", own.Index, System.StringComparison.Ordinal);
            int flags = source.IndexOf("player.Flags", own.Index, System.StringComparison.Ordinal);
            int timer = source.IndexOf("player.StatusTimer", own.Index, System.StringComparison.Ordinal);
            Assert.That(breakAt, Is.GreaterThan(own.Index));
            Assert.That(flags, Is.GreaterThan(breakAt));
            Assert.That(timer, Is.GreaterThan(breakAt));
        }

        // ---- -ptk-killer-look occluded -------------------------------------------------------------------------------------

        [TestCase("occluded")]
        [TestCase("Occluded")]
        [TestCase("OCCLUDED")]
        [TestCase(" occluded ")]
        public void KillerLookOccludedIsAcceptedInAnyCase(string text)
        {
            DevArgs a = DevArgs.Parse(new[] { "-ptk-killer-look", text });
            Assert.That(a.Look, Is.EqualTo(KillerLook.Occluded));
            Assert.That(a.Active, Is.True);
            Assert.That(a.Errors, Is.Empty);
        }

        [Test]
        public void AnEarlierNumberStaysAsTheFallbackYawForOccludedAndALaterNumberReplacesIt()
        {
            DevArgs a = DevArgs.Parse(new[] { "-ptk-killer-look", "45", "-ptk-killer-look", "occluded" });
            Assert.That(a.Look, Is.EqualTo(KillerLook.Occluded));
            Assert.That(a.LookYaw, Is.EqualTo(45f), "the walk-to-a-wall yaw");
            DevArgs b = DevArgs.Parse(new[] { "-ptk-killer-look", "occluded", "-ptk-killer-look", "90" });
            Assert.That(b.Look, Is.EqualTo(KillerLook.Fixed));
            Assert.That(b.LookYaw, Is.EqualTo(90f));
            DevArgs c = DevArgs.Parse(new[] { "-ptk-killer-look", "occluded" });
            Assert.That(c.LookYaw, Is.EqualTo(0f), "default fallback yaw is 0");
        }

        [Test]
        public void OccludedDoesNotChangeTheOtherLookModesOrTheRejectionOfUnknownWords()
        {
            Assert.That(DevArgs.Parse(new[] { "-ptk-killer-look", "nearest" }).Look, Is.EqualTo(KillerLook.Nearest));
            Assert.That(DevArgs.Parse(new[] { "-ptk-killer-look", "away" }).Look, Is.EqualTo(KillerLook.Away));
            DevArgs bad = DevArgs.Parse(new[] { "-ptk-killer-look", "occlude" });
            Assert.That(bad.Look, Is.EqualTo(KillerLook.None));
            Assert.That(bad.Errors.Count, Is.EqualTo(1));
            DevArgs mixed = DevArgs.Parse(new[] { "-ptk-killer-look", "occluded", "-ptk-killer-look", "bogus" });
            Assert.That(mixed.Look, Is.EqualTo(KillerLook.Occluded), "an invalid later value keeps the earlier mode");
            Assert.That(mixed.Errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void OccludedWithoutAValueIsAnErrorLikeTheOtherModes()
        {
            DevArgs a = DevArgs.Parse(new[] { "-ptk-killer-look", "-ptk-offline" });
            Assert.That(a.Look, Is.EqualTo(KillerLook.None));
            Assert.That(a.Offline, Is.True);
            Assert.That(a.Errors.Count, Is.EqualTo(1));
        }
    }
}
