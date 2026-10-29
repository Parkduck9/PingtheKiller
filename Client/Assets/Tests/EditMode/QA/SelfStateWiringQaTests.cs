using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PingTheKiller.Client.QA.Tests
{
    // U4d QA attempt 1: code-read checks of the S2C_SelfState wiring (the pump lives in a MonoBehaviour that needs the whole
    // scene) and of the information boundary. Comments are stripped before every pattern search.
    [Category("U4dQaA1")]
    public sealed class SelfStateWiringQaTests
    {
        private static string Stripped(string relative) => QaSource.StripComments(QaPaths.ReadScript(relative));

        private static IEnumerable<string> RelativeScripts(string folder)
        {
            string root = Path.Combine(QaPaths.ScriptsDir(), folder);
            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                yield return Path.Combine(folder, Path.GetFileName(path));
        }

        // ---- one pump path, no stub -------------------------------------------------------------------------------------------

        [Test]
        public void NoStubMarkerOrPendingPlaceholderForTheSelfStatePacketRemainsAnywhere()
        {
            foreach (string path in QaPaths.AllScripts())
            {
                string raw = File.ReadAllText(path);
                Assert.That(raw, Does.Not.Contain("U2c: feed from S2C_SelfState"), Path.GetFileName(path) + " still carries the adapter stub marker");
                Assert.That(Regex.IsMatch(raw, @"(pending|until|waiting for|hidden until)\s+U2c", RegexOptions.IgnoreCase), Is.False,
                    Path.GetFileName(path) + " still has a pending-U2c placeholder comment");
            }
        }

        [Test]
        public void SelfStateIsHandledByExactlyOnePumpCaseThatFeedsTheHolderOnce()
        {
            int cases = 0, feeds = 0;
            string handler = null;
            foreach (string path in QaPaths.AllScripts())
            {
                string name = Path.GetFileName(path);
                if (name == "SelfState.cs") continue;
                string code = QaSource.StripComments(File.ReadAllText(path));
                // A pump path = a pattern-match on the packet type (case S2C_SelfState x: / is S2C_SelfState x). Mentions of
                // PacketId.S2C_SelfState (the socket whitelist) and of the packet type in a Dev log are not pump paths.
                foreach (Match m in Regex.Matches(code, @"\b(case|is)\s+S2C_SelfState\b"))
                {
                    cases++;
                    handler = name;
                }
                feeds += Regex.Matches(code, @"\.Feed\(").Count;
            }
            Assert.That(cases, Is.EqualTo(1), "S2C_SelfState is pattern-matched in exactly one place");
            Assert.That(handler, Is.EqualTo("ClientApp.cs"), "and that place is the packet pump");
            Assert.That(feeds, Is.EqualTo(1), "exactly one call feeds the SelfStateHolder");

            string pump = QaSource.MethodText(Stripped("ClientApp.cs"), "private void Receive(");
            Assert.That(pump, Is.Not.Null, "ClientApp.Receive is the pump");
            int at = pump.IndexOf("case S2C_SelfState", System.StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0));
            int stop = pump.IndexOf("case S2C_SkillEffect", at, System.StringComparison.Ordinal);
            Assert.That(stop, Is.GreaterThan(at));
            string next = pump.Substring(at, stop - at);
            Assert.That(next, Does.Contain("selfState.Feed("), "the Feed call sits inside the S2C_SelfState case");
            Assert.That(next, Does.Contain("received.Time"), "stamped with the receive-thread arrival time, not the frame time");
        }

        [Test]
        public void TheHolderIsResetOnEveryPathThatEndsOrStartsAMatchOrChangesTheLink()
        {
            string app = Stripped("ClientApp.cs");
            string pump = QaSource.MethodText(app, "private void Receive(");
            int start = pump.IndexOf("case S2C_MatchStart", System.StringComparison.Ordinal);
            int end = pump.IndexOf("case S2C_WorldState", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            Assert.That(pump.Substring(start, end - start), Does.Contain("selfState.Reset()"), "match start");
            Assert.That(QaSource.MethodText(app, "private void EndMatch("), Does.Contain("selfState.Reset()"), "match end");
            Assert.That(QaSource.MethodText(app, "private void Disconnect("), Does.Contain("selfState.Reset()"), "leaving and a lost connection");
            Assert.That(QaSource.MethodText(app, "private void Attach("), Does.Contain("selfState.Reset()"), "a new link: join, reconnect, offline");
        }

        [Test]
        public void SelfStateOutsideAMatchIsDroppedAndTheOfflineLinkDoesNotFilterPackets()
        {
            string pump = QaSource.MethodText(Stripped("ClientApp.cs"), "private void Receive(");
            int at = pump.IndexOf("case S2C_SelfState", System.StringComparison.Ordinal);
            string body = pump.Substring(at, pump.IndexOf("case S2C_SkillEffect", at, System.StringComparison.Ordinal) - at);
            Assert.That(body, Does.Contain("if (!playing) break;"), "a packet after the result or before MatchStart never makes the seam available");
            string offline = Stripped("Net/OfflineLink.cs");
            Assert.That(offline, Does.Not.Contain("IsServerPacket"));
            Assert.That(offline, Does.Not.Match(@"\bis\s+S2C_|\bcase\s+S2C_|PacketId\.S2C_"), "the in-process link forwards every packet type unchanged");
        }

        // ---- information boundary ---------------------------------------------------------------------------------------------

        [Test]
        public void TheKillerPathNeverReadsTheRescueOrProtectionFieldsOfTheSeam()
        {
            foreach (string relative in RelativeScripts("Killer"))
                Assert.That(Regex.IsMatch(Stripped(relative), @"\b(RescueProgress|InvulnerableLeft)\b"), Is.False, relative + " reads a Survivor-only SelfState field");
            Assert.That(Regex.IsMatch(Stripped("UI/KillerHud.cs"), @"\b(RescueProgress|InvulnerableLeft)\b"), Is.False, "KillerHud");
        }

        [Test]
        public void TheSurvivorPathNeverReadsTheExecutionGrace()
        {
            foreach (string relative in new[] { "UI/SurvivorHud.cs", "UI/SurvivorReadout.cs" })
                Assert.That(Stripped(relative), Does.Not.Contain("ExecutionGraceLeft"), relative);
            string app = Stripped("ClientApp.cs");
            // The pump stores the packet's grace in the holder (the Killer needs it); nothing Survivor-side may consume it.
            foreach (Match m in Regex.Matches(app, @"[^\n]*ExecutionGraceLeft[^\n]*"))
                Assert.That(m.Value, Does.Contain("selfState.Feed(").Or.Contain("dev?.OnSelfState("), "an unexpected use of the grace in ClientApp: " + m.Value.Trim());
        }

        [Test]
        public void TheKillerPathStillReadsNoSurvivorIdYawStatusTimerCaptureCountOrForbiddenFlags()
        {
            // Files of the Killer path (the dev capture is excluded: it is behind DevArgs and U4c F-05 tracks it).
            string[] files = { "Killer/KillerScene.cs", "Killer/KillerWorldLayer.cs", "Killer/BlipTracker.cs", "Killer/BlipSoundModel.cs", "Killer/ExecutionGuidance.cs", "Killer/KillerCues.cs", "UI/KillerHud.cs" };
            foreach (string relative in files)
            {
                string code = Stripped(relative);
                Assert.That(Regex.IsMatch(code, @"\b(StatusTimer|CaptureCount)\b"), Is.False, relative + " reads StatusTimer or CaptureCount");
                Assert.That(Regex.IsMatch(code, @"PlayerFlags\.(Hacking|Rescuing|Stealthed|Afterimage)"), Is.False, relative + " reads a forbidden flag");
                Assert.That(Regex.IsMatch(code, @"\b(entry|player|survivor|p)\.(Id|Yaw)\b"), Is.False, relative + " reads Id or Yaw of an entry");
            }
        }
    }
}
