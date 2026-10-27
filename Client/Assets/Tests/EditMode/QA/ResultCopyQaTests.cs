using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 3.3 (GameResult screen copy) and GDD 3.5.
    public sealed class ResultCopyQaTests
    {
        private static S2C_LobbyState Lobby(params string[] nicknames)
        {
            var lobby = new S2C_LobbyState();
            for (int i = 0; i < nicknames.Length; i++)
                lobby.Slots[i] = new Slot { PlayerId = i + 1, Nickname = nicknames[i] };
            return lobby;
        }

        private static S2C_GameResult Result(GameEndReason reason, params int[] escaped)
        {
            var result = new S2C_GameResult
            {
                Reason = (byte)reason,
                WinnerRole = (byte)(reason == GameEndReason.SurvivorEscaped ? Role.Survivor : Role.Killer)
            };
            result.EscapedIds.AddRange(escaped);
            return result;
        }

        // ---- the three reasons ------------------------------------------------------------------------------------------

        [Test]
        public void SurvivorEscapedNamesTheEscapedPlayersAndTheExit()
        {
            S2C_LobbyState lobby = Lobby("Ana", "Bo", "Cy");
            Assert.That(ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 2), lobby), Is.EqualTo("Survivors win. Bo reached the Exit."));
            Assert.That(ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 1, 3), lobby), Is.EqualTo("Survivors win. Ana and Cy reached the Exit."));
            string three = ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 1, 2, 3), lobby);
            Assert.That(three, Does.StartWith("Survivors win."));
            Assert.That(three, Does.Contain("Ana"));
            Assert.That(three, Does.Contain("Bo"));
            Assert.That(three, Does.Contain("Cy"));
            Assert.That(three, Does.EndWith("reached the Exit."));
            Assert.That(three.IndexOf("Ana", StringComparison.Ordinal), Is.LessThan(three.IndexOf("Bo", StringComparison.Ordinal)), "list order is kept");
        }

        [Test]
        public void AllSurvivorsDownIsAKillerWinWithTheFixedSentence()
        {
            Assert.That(ResultCopy.Headline(Result(GameEndReason.AllSurvivorsDown), Lobby("Ana")), Is.EqualTo("Killer wins. No survivor is left in play."));
        }

        [Test]
        public void TimeUpIsAKillerWinWithTheFixedSentence()
        {
            Assert.That(ResultCopy.Headline(Result(GameEndReason.TimeUp), Lobby("Ana")), Is.EqualTo("Killer wins. Time ran out."));
        }

        [Test]
        public void KillerReasonsIgnoreAStrayEscapedList()
        {
            string text = ResultCopy.Headline(Result(GameEndReason.TimeUp, 1, 2), Lobby("Ana", "Bo"));
            Assert.That(text, Is.EqualTo("Killer wins. Time ran out."));
        }

        // ---- names ------------------------------------------------------------------------------------------------------

        [Test]
        public void AnIdMissingFromTheLobbyFallsBackToPlayerId()
        {
            Assert.That(ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 9), Lobby("Ana")), Is.EqualTo("Survivors win. Player 9 reached the Exit."));
            Assert.That(ResultCopy.NameOf(9, Lobby("Ana")), Is.EqualTo("Player 9"));
        }

        [Test]
        public void ABlankNicknameFallsBackToPlayerId()
        {
            S2C_LobbyState lobby = Lobby("", "Bo");
            Assert.That(ResultCopy.NameOf(1, lobby), Is.EqualTo("Player 1"));
            Assert.That(ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 1, 2), lobby), Is.EqualTo("Survivors win. Player 1 and Bo reached the Exit."));
        }

        [Test]
        public void NoLobbyAtAllStillGivesPlayerIds()
        {
            Assert.DoesNotThrow(() => ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 4), null));
            Assert.That(ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped, 4), null), Is.EqualTo("Survivors win. Player 4 reached the Exit."));
            var broken = new S2C_LobbyState { Slots = null };
            Assert.That(ResultCopy.NameOf(4, broken), Is.EqualTo("Player 4"));
        }

        [Test]
        public void ANullSlotInTheLobbyIsSkipped()
        {
            var lobby = Lobby("Ana");
            lobby.Slots[3] = null;
            Assert.That(ResultCopy.NameOf(1, lobby), Is.EqualTo("Ana"));
            Assert.That(ResultCopy.NameOf(7, lobby), Is.EqualTo("Player 7"));
        }

        [Test]
        public void AnEmptyEscapedListGivesAsentenceWithoutBlankNamesOrNulls()
        {
            string text = ResultCopy.Headline(Result(GameEndReason.SurvivorEscaped), Lobby("Ana"));
            Assert.That(text, Does.StartWith("Survivors win."));
            Assert.That(text, Does.Not.Contain("  "), "no blank where the names would be");
            Assert.That(text, Does.Not.Contain("null"));
            Assert.That(text, Does.Not.Contain("Player 0"));
            Assert.That(ResultCopy.Names(new List<int>(), Lobby("Ana")), Is.Empty);
            Assert.That(ResultCopy.Names(null, Lobby("Ana")), Is.Empty);
        }

        [Test]
        public void NamesJoinsWithCommasAndAnd()
        {
            S2C_LobbyState lobby = Lobby("A", "B", "C", "D");
            Assert.That(ResultCopy.Names(new[] { 1 }, lobby), Is.EqualTo("A"));
            Assert.That(ResultCopy.Names(new[] { 1, 2 }, lobby), Is.EqualTo("A and B"));
            Assert.That(ResultCopy.Names(new[] { 1, 2, 3 }, lobby), Is.EqualTo("A, B and C"));
            Assert.That(ResultCopy.Names(new[] { 1, 2, 3, 4 }, lobby), Is.EqualTo("A, B, C and D"));
        }

        // ---- unknown reason ---------------------------------------------------------------------------------------------

        [TestCase((byte)3)]
        [TestCase((byte)99)]
        [TestCase((byte)255)]
        public void AnUnknownReasonByteDoesNotThrowAndStillReadsAsPlainWords(byte reason)
        {
            var result = new S2C_GameResult { Reason = reason, WinnerRole = 7 };
            result.EscapedIds.Add(2);
            string headline = null, outcome = null;
            Assert.DoesNotThrow(() => headline = ResultCopy.Headline(result, Lobby("Ana", "Bo")));
            Assert.DoesNotThrow(() => outcome = ResultCopy.Outcome(result, 1, Role.Survivor, PlayerStatus.Normal));
            Assert.DoesNotThrow(() => ResultCopy.Outcome(result, 1, Role.Killer, PlayerStatus.Normal));
            Assert.That(headline, Is.Not.Null.And.Not.Empty);
            Assert.That(outcome, Is.Not.Null.And.Not.Empty);
            Assert.That(headline, Does.Not.Contain("Exception"));
            Assert.That(headline, Does.Not.Contain(reason.ToString()), "no raw byte value on screen");
        }

        [Test]
        public void ANullResultListDoesNotThrow()
        {
            var result = new S2C_GameResult { Reason = (byte)GameEndReason.SurvivorEscaped, EscapedIds = null };
            Assert.DoesNotThrow(() => ResultCopy.Headline(result, Lobby("Ana")));
            Assert.DoesNotThrow(() => ResultCopy.Outcome(result, 1, Role.Survivor, PlayerStatus.Normal));
        }

        // ---- the local player's own outcome -----------------------------------------------------------------------------

        [Test]
        public void ASurvivorOnTheEscapedListEscaped()
        {
            Assert.That(ResultCopy.Outcome(Result(GameEndReason.SurvivorEscaped, 2), 2, Role.Survivor, PlayerStatus.Normal), Is.EqualTo("You escaped."));
        }

        [Test]
        public void ASurvivorWhoseStatusIsEscapedEscapedEvenIfTheListIsMissing()
        {
            Assert.That(ResultCopy.Outcome(Result(GameEndReason.SurvivorEscaped), 2, Role.Survivor, PlayerStatus.Escaped), Is.EqualTo("You escaped."));
        }

        [TestCase(GameEndReason.SurvivorEscaped)]
        [TestCase(GameEndReason.AllSurvivorsDown)]
        [TestCase(GameEndReason.TimeUp)]
        public void AnEliminatedSurvivorWasEliminatedWhateverTheReason(GameEndReason reason)
        {
            Assert.That(ResultCopy.Outcome(Result(reason, 5), 2, Role.Survivor, PlayerStatus.Eliminated), Is.EqualTo("You were eliminated."));
        }

        [TestCase(GameEndReason.AllSurvivorsDown)]
        [TestCase(GameEndReason.TimeUp)]
        public void ASurvivorSideLossNeverClaimsAnEscapeOrAWin(GameEndReason reason)
        {
            foreach (PlayerStatus status in new[] { PlayerStatus.Normal, PlayerStatus.Lagging })
            {
                string text = ResultCopy.Outcome(Result(reason), 2, Role.Survivor, status);
                Assert.That(text, Is.Not.Null.And.Not.Empty);
                Assert.That(text, Is.Not.EqualTo("You escaped."), status.ToString());
                Assert.That(text, Does.Not.Contain("were eliminated"), "a survivor who was not eliminated is not told so");
                Assert.That(text, Does.Not.Contain("won"), "no win claim on a survivor-side loss");
                Assert.That(text, Does.Not.Contain("Your team won"));
            }
        }

        [Test]
        public void ASurvivorWhoDidNotEscapeWhileTheTeamWonIsToldSo()
        {
            string text = ResultCopy.Outcome(Result(GameEndReason.SurvivorEscaped, 5), 2, Role.Survivor, PlayerStatus.Normal);
            Assert.That(text, Is.Not.EqualTo("You escaped."));
            Assert.That(text, Does.Not.Contain("were eliminated"));
            Assert.That(text, Does.Not.Contain("lost"), "the survivors won");
        }

        [Test]
        public void TheKillerViewReportsAKillerWinOrLossAndNeverAnEscape()
        {
            string lost = ResultCopy.Outcome(Result(GameEndReason.SurvivorEscaped, 2), 1, Role.Killer, PlayerStatus.Normal);
            Assert.That(lost, Does.Contain("Killer"));
            Assert.That(lost, Does.Contain("lost"));
            Assert.That(lost, Does.Not.Contain("won"));
            Assert.That(lost, Is.Not.EqualTo("You escaped."));
            foreach (GameEndReason reason in new[] { GameEndReason.AllSurvivorsDown, GameEndReason.TimeUp })
            {
                string won = ResultCopy.Outcome(Result(reason), 1, Role.Killer, PlayerStatus.Normal);
                Assert.That(won, Does.Contain("Killer"), reason.ToString());
                Assert.That(won, Does.Contain("won"), reason.ToString());
                Assert.That(won, Does.Not.Contain("lost"), reason.ToString());
                Assert.That(won, Is.Not.EqualTo("You escaped."));
            }
        }

        [Test]
        public void TheKillerNeverSeesAnEscapedListContainingItsOwnId()
        {
            // A killer id inside EscapedIds would be a server bug; the killer outcome must still be a killer sentence.
            string text = ResultCopy.Outcome(Result(GameEndReason.SurvivorEscaped, 1), 1, Role.Killer, PlayerStatus.Normal);
            Assert.That(text, Is.Not.EqualTo("You escaped."));
        }

        // ---- lobby line (Back to lobby gating copy) ---------------------------------------------------------------------

        [Test]
        public void TheWaitingLineIsTheSpecSentenceOnlineAndGoesAwayWhenTheLobbyOpens()
        {
            Assert.That(ResultCopy.LobbyLine(false, false), Is.EqualTo("Waiting for the host to reopen the lobby."));
            string offlineWaiting = ResultCopy.LobbyLine(false, true);
            Assert.That(offlineWaiting, Is.Not.Null.And.Not.Empty);
            Assert.That(offlineWaiting, Does.Contain("lobby"));
            foreach (bool offline in new[] { false, true })
            {
                string open = ResultCopy.LobbyLine(true, offline);
                Assert.That(open, Is.Not.Null.And.Not.Empty);
                Assert.That(open, Does.Not.Contain("Waiting"));
            }
        }

        // ---- slop ---------------------------------------------------------------------------------------------------------

        [Test]
        public void NoResultTextUsesBannerWordsExclamationsOrInternalNames()
        {
            S2C_LobbyState lobby = Lobby("Ana", "Bo");
            var texts = new List<string>();
            foreach (GameEndReason reason in new[] { GameEndReason.SurvivorEscaped, GameEndReason.AllSurvivorsDown, GameEndReason.TimeUp, (GameEndReason)77 })
            {
                S2C_GameResult result = Result(reason, 1);
                texts.Add(ResultCopy.Headline(result, lobby));
                foreach (Role role in new[] { Role.Survivor, Role.Killer })
                    foreach (PlayerStatus status in new[] { PlayerStatus.Normal, PlayerStatus.Lagging, PlayerStatus.Escaped, PlayerStatus.Eliminated })
                        texts.Add(ResultCopy.Outcome(result, 1, role, status));
            }
            texts.Add(ResultCopy.LobbyLine(false, false)); texts.Add(ResultCopy.LobbyLine(false, true));
            texts.Add(ResultCopy.LobbyLine(true, false)); texts.Add(ResultCopy.LobbyLine(true, true));
            foreach (string text in texts)
            {
                Assert.That(text, Does.Not.Contain("!"), text);
                Assert.That(text.ToUpperInvariant(), Does.Not.Contain("VICTORY"), text);
                Assert.That(text.ToUpperInvariant(), Does.Not.Contain("DEFEAT"), text);
                Assert.That(text.ToLowerInvariant(), Does.Not.Contain("confetti"), text);
                Assert.That(text, Does.Not.Contain("U4"), text);
                Assert.That(text, Does.Not.Contain("U2b"), text);
            }
        }
    }
}
