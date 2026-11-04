using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // docs/ui/SLOP_DEFINITION.md section 4 (copy) and the U4b brief 3.2: no internal unit names, no protocol version strings,
    // no exclamation marks, no hype words, fixed vocabulary. Covers every string literal a player can see.
    public sealed class CopyHygieneQaTests
    {
        private static readonly Regex Internal = new Regex(@"\bU\d+[a-z]?\b|\bv\d+\.\d+\b|protocol v|\bTODO\b|lorem", RegexOptions.IgnoreCase);
        private static readonly Regex Hype = new Regex(@"\b(unleash|epic|ultimate|seamless|immersive|embark|dive into|get ready|welcome|let's go|are you ready|victory|defeat|confetti)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Vocabulary = new Regex(@"\b(portal|goal|escape zone|frozen|freeze|cooldown ring)\b", RegexOptions.IgnoreCase);

        // String literals on lines that are not Debug.* log calls (those never reach the screen).
        private static IEnumerable<KeyValuePair<string, string>> OnScreenLiterals()
        {
            foreach (string path in QaPaths.AllScripts())
            {
                string name = Path.GetFileName(path);
                if (name == "DevArgs.cs" || name == "DevCapture.cs") continue; // dev-only flags and logs
                string stripped = QaSource.StripComments(File.ReadAllText(path));
                foreach (string line in stripped.Split('\n'))
                {
                    if (line.Contains("Debug.") || line.Contains("Resources.Load")) continue;
                    foreach (string literal in QaSource.StringLiterals(line))
                        if (literal.Length > 0) yield return new KeyValuePair<string, string>(name, literal);
                }
            }
        }

        [Test]
        public void NoStringLiteralInTheClientNamesAnInternalUnitOrAProtocolVersion()
        {
            int checkedCount = 0;
            foreach (KeyValuePair<string, string> item in OnScreenLiterals())
            {
                checkedCount++;
                Assert.That(Internal.IsMatch(item.Value), Is.False, item.Key + ": \"" + item.Value + "\"");
            }
            Assert.That(checkedCount, Is.GreaterThan(30), "the scan saw the UI strings");
        }

        [Test]
        public void NoStringLiteralUsesHypeWordsExclamationMarksOrOffVocabularyTerms()
        {
            foreach (KeyValuePair<string, string> item in OnScreenLiterals())
            {
                string where = item.Key + ": \"" + item.Value + "\"";
                Assert.That(item.Value, Does.Not.Contain("!"), where);
                Assert.That(Hype.IsMatch(item.Value), Is.False, where);
                Assert.That(Vocabulary.IsMatch(item.Value), Is.False, where);
            }
        }

        [Test]
        public void ThePreviousOfflineStubLineIsGone()
        {
            foreach (KeyValuePair<string, string> item in OnScreenLiterals())
                Assert.That(item.Value, Does.Not.Contain("arrives in"), item.Key + ": \"" + item.Value + "\"");
        }

        [Test]
        public void EveryGeneratedLineIsPlainTextWithoutRawFormatting()
        {
            var lobby = new S2C_LobbyState();
            lobby.Slots[0] = new Slot { PlayerId = 1, Nickname = "Ana", IsHost = 1 };
            lobby.Slots[1] = new Slot { PlayerId = 2, Nickname = "Bot 2", IsBot = 1 };
            var generated = new List<string>();
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
                for (byte id = 0; id < 6; id++) generated.Add(HudCopy.SkillLine(role, id));
            foreach (PlayerStatus status in new[] { PlayerStatus.Normal, PlayerStatus.Lagging, PlayerStatus.Escaped, PlayerStatus.Eliminated })
                generated.Add(HudCopy.StatusLine(status, 12.5f));
            generated.Add(HudCopy.TerminalsLine(new TerminalState[0], 3));
            generated.Add(HudCopy.HackingLine(50));
            // U6c-wire: the v1 single Exit label is gone; the two exit cells' copy takes its place.
            generated.Add(ExitCopy.Tag(ExitView.FromWire(0, 1, 0))); generated.Add(ExitCopy.Tag(ExitView.FromWire(1, 3, 100)));
            generated.Add(LobbyCopy.Title(true)); generated.Add(LobbyCopy.Title(false));
            foreach (Slot slot in lobby.Slots) generated.Add(LobbyCopy.SlotText(slot, 1));
            foreach (byte choice in new byte[] { 0, 1, 255 }) { generated.Add(LobbyCopy.RoleLine(choice, true)); generated.Add(LobbyCopy.RoleLine(choice, false)); }
            foreach (string text in generated)
            {
                if (text.Length == 0) continue;
                Assert.That(text, Does.Not.Contain("!"), text);
                Assert.That(text, Does.Not.Contain("{"), text);
                Assert.That(text, Does.Not.Contain("null"), text);
                Assert.That(Internal.IsMatch(text), Is.False, text);
                Assert.That(Hype.IsMatch(text), Is.False, text);
                Assert.That(Vocabulary.IsMatch(text), Is.False, text);
            }
        }
    }
}
