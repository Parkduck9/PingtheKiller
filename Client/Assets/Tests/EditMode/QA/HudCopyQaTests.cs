using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 4 (play readouts) and GDD 3.3 to 3.5, 4. "·" is the middle dot.
    public sealed class HudCopyQaTests
    {
        private const string Dot = "·";

        private static TerminalState Terminal(byte id, byte progress, float x = 0, float z = 0)
            => new TerminalState { Id = id, X = x, Z = z, Progress = progress };

        // ---- status line --------------------------------------------------------------------------------------------------

        [TestCase(42.01f, "Lagging " + Dot + " 43 s")]
        [TestCase(0.2f, "Lagging " + Dot + " 1 s")]
        [TestCase(0.001f, "Lagging " + Dot + " 1 s")]
        [TestCase(1.0f, "Lagging " + Dot + " 1 s")]
        [TestCase(1.01f, "Lagging " + Dot + " 2 s")]
        [TestCase(59.99f, "Lagging " + Dot + " 60 s")]
        [TestCase(60f, "Lagging " + Dot + " 60 s")]
        [TestCase(42f, "Lagging " + Dot + " 42 s")]
        public void TheLaggingCountdownRoundsUp(float seconds, string expected)
        {
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, seconds), Is.EqualTo(expected));
        }

        [Test]
        public void ANegativeOrNotANumberTimerNeverShowsANegativeNumberOrThrows()
        {
            foreach (float timer in new[] { -5f, float.NaN, float.NegativeInfinity, float.PositiveInfinity, 1e9f })
            {
                string text = null;
                Assert.DoesNotThrow(() => text = HudCopy.StatusLine(PlayerStatus.Lagging, timer), timer.ToString());
                Assert.That(text, Does.StartWith("Lagging"));
                Assert.That(text, Does.Not.Contain("-"), text);
                Assert.That(text, Does.Not.Contain("NaN"), text);
                Assert.That(text, Does.Not.Contain("Infinity"), text);
            }
        }

        [Test]
        public void NormalAddsNothingToTheHud()
        {
            Assert.That(HudCopy.StatusLine(PlayerStatus.Normal, 12f), Is.Empty);
        }

        [Test]
        public void EscapedAndEliminatedUseTheSpecSentences()
        {
            Assert.That(HudCopy.StatusLine(PlayerStatus.Escaped, 0), Is.EqualTo("Escaped. Waiting for the match to end."));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Eliminated, 0), Is.EqualTo("Eliminated. Watching until the match ends."));
        }

        [Test]
        public void AnUnknownStatusByteDoesNotThrow()
        {
            string text = null;
            Assert.DoesNotThrow(() => text = HudCopy.StatusLine((PlayerStatus)200, 1f));
            Assert.That(text, Is.Not.Null);
            Assert.That(text, Does.Not.Contain("Lagging"));
        }

        // ---- terminals line -----------------------------------------------------------------------------------------------

        [Test]
        public void ProgressOneHundredCountsAsCompleteAndNinetyNineDoesNot()
        {
            Assert.That(HudCopy.CompletedTerminals(new[] { Terminal(0, 100) }), Is.EqualTo(1));
            Assert.That(HudCopy.CompletedTerminals(new[] { Terminal(0, 99) }), Is.EqualTo(0));
            Assert.That(HudCopy.CompletedTerminals(new[] { Terminal(0, 100), Terminal(1, 99), Terminal(2, 0), Terminal(3, 100), Terminal(4, 1) }), Is.EqualTo(2));
            Assert.That(HudCopy.CompletedTerminals(null), Is.EqualTo(0));
            Assert.That(HudCopy.CompletedTerminals(new TerminalState[0]), Is.EqualTo(0));
        }

        [Test]
        public void TheTerminalsLineReadsCompletedOverRequiredAndTheExitState()
        {
            TerminalState[] terminals = { Terminal(0, 100), Terminal(1, 99), Terminal(2, 0), Terminal(3, 100) };
            Assert.That(HudCopy.TerminalsLine(terminals, 3, false), Is.EqualTo("Terminals 2/3 " + Dot + " Exit locked"));
            Assert.That(HudCopy.TerminalsLine(terminals, 2, true), Is.EqualTo("Terminals 2/2 " + Dot + " Exit open"));
            Assert.That(HudCopy.TerminalsLine(new[] { Terminal(0, 99) }, 2, false), Is.EqualTo("Terminals 0/2 " + Dot + " Exit locked"));
            Assert.That(HudCopy.TerminalsLine(null, 4, false), Is.EqualTo("Terminals 0/4 " + Dot + " Exit locked"));
            Assert.That(HudCopy.TerminalsLine(new TerminalState[0], 2, true), Is.EqualTo("Terminals 0/2 " + Dot + " Exit open"));
        }

        [Test]
        public void TheRequiredCountComesFromTheServerNotFromTheNumberOfPlacedTerminals()
        {
            TerminalState[] placed = { Terminal(0, 100), Terminal(1, 100), Terminal(2, 0), Terminal(3, 0), Terminal(4, 0) };
            Assert.That(HudCopy.TerminalsLine(placed, 3, false), Does.Contain("2/3"));
        }

        [Test]
        public void TheNearestTerminalIsTheClosestOneAndMinusOneWhenThereIsNone()
        {
            TerminalState[] terminals = { Terminal(0, 0, 10, 25), Terminal(1, 0, 28, 12), Terminal(2, 0, 49, 25) };
            Assert.That(HudCopy.NearestTerminal(terminals, 27, 13), Is.EqualTo(1));
            Assert.That(HudCopy.NearestTerminal(terminals, 50, 24), Is.EqualTo(2));
            Assert.That(HudCopy.NearestTerminal(terminals, 0, 0), Is.EqualTo(0));
            Assert.That(HudCopy.NearestTerminal(new TerminalState[0], 1, 1), Is.EqualTo(-1));
            Assert.That(HudCopy.NearestTerminal(null, 1, 1), Is.EqualTo(-1));
        }

        [Test]
        public void TheHackingLineShowsTheNearestTerminalPercentageAndClampsAtOneHundred()
        {
            Assert.That(HudCopy.HackingLine(0), Is.EqualTo("Hacking " + Dot + " 0%"));
            Assert.That(HudCopy.HackingLine(37), Is.EqualTo("Hacking " + Dot + " 37%"));
            Assert.That(HudCopy.HackingLine(100), Is.EqualTo("Hacking " + Dot + " 100%"));
            Assert.That(HudCopy.HackingLine(255), Is.EqualTo("Hacking " + Dot + " 100%"));
        }

        [Test]
        public void TheExitLabelUsesTheFixedVocabulary()
        {
            Assert.That(HudCopy.ExitLabel(false), Is.EqualTo("Exit " + Dot + " Locked"));
            Assert.That(HudCopy.ExitLabel(true), Is.EqualTo("Exit " + Dot + " Open"));
        }

        // ---- skill names (GDD 4 order) ------------------------------------------------------------------------------------

        private static readonly string[] KillerNames = { "Ping Scanner", "Overclock", "Packet Afterimage", "Firewall Block" };
        private static readonly string[] SurvivorNames = { "Dummy Packet", "Stealth Relay", "Traffic Overload", "Emergency Restore", "White Noise" };

        [Test]
        public void EveryKillerSkillHasItsGddName()
        {
            for (byte id = 0; id < KillerNames.Length; id++)
                Assert.That(HudCopy.SkillName(Role.Killer, id), Is.EqualTo(KillerNames[id]), "killer " + id);
        }

        [Test]
        public void EverySurvivorSkillHasItsGddName()
        {
            for (byte id = 0; id < SurvivorNames.Length; id++)
                Assert.That(HudCopy.SkillName(Role.Survivor, id), Is.EqualTo(SurvivorNames[id]), "survivor " + id);
        }

        [Test]
        public void AnUnknownSkillIdGivesANonEmptyNameThatBorrowsNothingFromTheOtherRole()
        {
            foreach (byte id in new byte[] { 4, 5, 9, 200, 255 })
            {
                string killer = null;
                Assert.DoesNotThrow(() => killer = HudCopy.SkillName(Role.Killer, id), "killer " + id);
                Assert.That(killer, Is.Not.Null.And.Not.Empty);
                Assert.That(Array.IndexOf(KillerNames, killer), Is.EqualTo(-1), "killer " + id + " must not alias a known killer skill");
                Assert.That(Array.IndexOf(SurvivorNames, killer), Is.EqualTo(-1), "killer " + id + " must not read as a survivor skill");
            }
            foreach (byte id in new byte[] { 5, 6, 9, 200, 255 })
            {
                string survivor = null;
                Assert.DoesNotThrow(() => survivor = HudCopy.SkillName(Role.Survivor, id), "survivor " + id);
                Assert.That(survivor, Is.Not.Null.And.Not.Empty);
                Assert.That(Array.IndexOf(SurvivorNames, survivor), Is.EqualTo(-1));
                Assert.That(Array.IndexOf(KillerNames, survivor), Is.EqualTo(-1));
            }
        }

        [Test]
        public void OnlyTheKillersPacketAfterimageIsPassive()
        {
            for (byte id = 0; id < 10; id++)
            {
                Assert.That(HudCopy.IsPassive(Role.Killer, id), Is.EqualTo(id == 2), "killer " + id);
                Assert.That(HudCopy.IsPassive(Role.Survivor, id), Is.False, "survivor " + id);
            }
        }

        [Test]
        public void TheSkillLineSaysQForActiveSkillsAndPassiveForTheAfterimage()
        {
            Assert.That(HudCopy.SkillLine(Role.Killer, 0), Is.EqualTo("Q " + Dot + " Ping Scanner"));
            Assert.That(HudCopy.SkillLine(Role.Killer, 1), Is.EqualTo("Q " + Dot + " Overclock"));
            Assert.That(HudCopy.SkillLine(Role.Killer, 2), Is.EqualTo("Passive " + Dot + " Packet Afterimage"));
            Assert.That(HudCopy.SkillLine(Role.Killer, 3), Is.EqualTo("Q " + Dot + " Firewall Block"));
            Assert.That(HudCopy.SkillLine(Role.Survivor, 2), Is.EqualTo("Q " + Dot + " Traffic Overload"), "survivor skill 2 is not passive");
            Assert.That(HudCopy.SkillLine(Role.Survivor, 4), Is.EqualTo("Q " + Dot + " White Noise"));
            Assert.That(HudCopy.SkillLine(Role.Killer, 9), Does.StartWith("Q "), "an unknown killer skill is not shown as passive");
        }

        [Test]
        public void EveryIdTheServerCanDealHasANamedSkill()
        {
            // Server.Core deals killer 0..3 and survivor 0..4 (GameServer.StartMatch).
            for (byte id = 0; id < 4; id++) Assert.That(HudCopy.SkillName(Role.Killer, id), Does.Not.StartWith("Skill "));
            for (byte id = 0; id < 5; id++) Assert.That(HudCopy.SkillName(Role.Survivor, id), Does.Not.StartWith("Skill "));
        }

        [Test]
        public void TheSkillTableKeepsItsTodoForTheSharedSkillIds()
        {
            string source = QaPaths.ReadScript("UI/HudCopy.cs");
            Assert.That(source, Does.Contain("TODO: switch to Shared SkillIds when U2b merges"));
        }

        // ---- lobby rows ---------------------------------------------------------------------------------------------------

        [Test]
        public void LobbyRowsShowNicknamePlusMarkersOnly()
        {
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 4, Nickname = "Player", IsHost = 1 }, 4), Is.EqualTo("Player " + Dot + " host " + Dot + " you"));
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 2, Nickname = "Bot 2", IsBot = 1 }, 4), Is.EqualTo("Bot 2 " + Dot + " bot"));
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 5, Nickname = "Ana" }, 4), Is.EqualTo("Ana"), "a human that is not you or host has no marker");
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 4, Nickname = "Ana" }, 4), Is.EqualTo("Ana " + Dot + " you"));
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 5, Nickname = "Ana", IsHost = 1 }, 4), Is.EqualTo("Ana " + Dot + " host"));
        }

        [Test]
        public void ARowNeverReadsPlayerDotPlayer()
        {
            string text = LobbyCopy.SlotText(new Slot { PlayerId = 1, Nickname = "Player", IsHost = 1 }, 1);
            Assert.That(text, Does.Not.Contain(Dot + " player"));
            Assert.That(text, Does.Not.Contain("Player " + Dot + " Player"));
        }

        [Test]
        public void AnEmptyOrMissingSlotReadsEmpty()
        {
            Assert.That(LobbyCopy.SlotText(new Slot(), 1), Is.EqualTo("Empty"));
            Assert.That(LobbyCopy.SlotText(null, 1), Is.EqualTo("Empty"));
        }

        [Test]
        public void TheLobbyTitleMarksOfflineAndNothingElse()
        {
            Assert.That(LobbyCopy.Title(true), Is.EqualTo("Lobby " + Dot + " offline"));
            Assert.That(LobbyCopy.Title(false), Is.EqualTo("Lobby"));
        }

        [Test]
        public void TheRoleLineNamesTheChoiceAndWhoseRoleItIs()
        {
            Assert.That(LobbyCopy.RoleLine((byte)RoleChoice.Killer, true), Is.EqualTo("Your role: Killer"));
            Assert.That(LobbyCopy.RoleLine((byte)RoleChoice.Survivor, true), Is.EqualTo("Your role: Survivor"));
            Assert.That(LobbyCopy.RoleLine((byte)RoleChoice.Random, true), Is.EqualTo("Your role: Random"));
            Assert.That(LobbyCopy.RoleLine((byte)RoleChoice.Killer, false), Does.StartWith("Host role"));
        }

        [Test]
        public void TheLobbyRowNumberColumnIsAFixedWidthInTheStylesheet()
        {
            string uss = System.IO.File.ReadAllText(System.IO.Path.Combine(QaPaths.Assets(), "Resources/ClientUI.uss"));
            Assert.That(uss, Does.Match(@"\.slot-number\s*\{[^}]*\bwidth:\s*\d+px"), "numbers sit in a fixed-width column so names stay aligned");
        }
    }
}
