using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, orchestrator ruling 2 and the colour-role review. HUD_SPEC 0.4: `ally` is "shown to Survivors only";
    // `signal` on the Killer client is for Survivor information only (outlines, markers, edge indicators); Q11: the Killer
    // sees a `text` ring for the Exit, Survivors an `ally` ring.
    //
    // WorldView has no role-aware colour seam today (the colours are constants chosen inside Accept), so these tests read the
    // statements that pick them. The first two are EXPECTED TO FAIL against the U4c implementation: they are findings, not
    // flaky tests (see docs/qa/U4c-qa-a1.md F-03).
    [Category("U4cQaA1")]
    public sealed class KillerColourRolesQaTests
    {
        // The terminal and Exit colouring is WorldProps.Accept; the avatar building is CharacterViews.Accept.
        private static string AcceptOf(string file) => QaSource.MethodText(QaSource.StripComments(QaPaths.ReadScript(file)), "public void Accept(");
        private static string WorldAccept() => AcceptOf(WorldSources.Props);
        // U4h moved the state -> look statements into one small method each (SetTerminalLook, SetExitLook); Accept calls them.
        private static string PropsMethod(string signature) => QaSource.MethodText(QaSource.StripComments(QaPaths.ReadScript(WorldSources.Props)), signature);

        [Test]
        public void AKillerClientNeverPaintsACompletedTerminalWithTheAllyTeal()
        {
            string accept = WorldAccept();
            Assert.That(accept, Is.Not.Null);
            Assert.That(accept, Does.Contain("SetTerminalLook(item, terminal.Progress == 100);"), "Accept hands the done state to the look method");
            Match done = Regex.Match(PropsMethod("void SetTerminalLook("), @"Panel\.color\s*=([^;]*);");
            Assert.That(done.Success, Is.True, "the statement that colours a terminal's panel");
            Assert.That(done.Groups[1].Value, Does.Match(@"\brole\b"),
                "the done colour must depend on the viewer's role: ally teal for Survivors, a non-ally colour for the Killer. Statement: Panel.color =" + done.Groups[1].Value);
        }

        [Test]
        public void AKillerClientNeverPaintsTheOpenExitRingWithTheAllyTeal()
        {
            Assert.That(WorldAccept(), Does.Contain("SetExitLook(i, open ? ExitLook.Open : ExitLook.Closed);"), "Accept hands the open state to the look method");
            Match ring = Regex.Match(PropsMethod("void SetExitLook("), @"RingMaterial\.color\s*=([^;]*);");
            Assert.That(ring.Success, Is.True, "the statement that colours the exit ring");
            Assert.That(ring.Groups[1].Value, Does.Match(@"\brole\b"),
                "Q11: Survivors see an ally ring, the Killer a text ring, so the open colour must depend on the role. Statement: color =" + ring.Groups[1].Value);
        }

        [Test]
        public void NeitherRoleEverSeesATerminalOrTheExitInSignalAmber()
        {
            Assert.That(WorldAccept(), Does.Not.Contain("Signal"));
            Assert.That(PropsMethod("void SetTerminalLook("), Does.Not.Contain("Signal"));
            Assert.That(PropsMethod("void SetExitLook("), Does.Not.Contain("Signal"));
            Assert.That(AcceptOf(WorldSources.Characters), Does.Not.Contain("Signal"));
            Assert.That(AcceptOf(WorldSources.View), Does.Not.Contain("Signal"));
        }

        [Test]
        public void TheKillerClientBuildsNoCharacterFromAnEntryInWorldViewSoNoAllyTealCharacterCanAppearThere()
        {
            string accept = AcceptOf(WorldSources.Characters);
            int stop = accept.IndexOf("role == Role.Killer) break;", System.StringComparison.Ordinal);
            int tint = accept.IndexOf("Color tint =", System.StringComparison.Ordinal);
            Assert.That(stop, Is.GreaterThanOrEqualTo(0));
            Assert.That(tint, Is.GreaterThan(stop), "the teal avatar tint is chosen only after the Killer has left the loop");
        }
    }
}
