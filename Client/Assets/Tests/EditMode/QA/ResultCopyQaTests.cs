using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 3.3 (GameResult screen copy) and GDD 3.5. U6c-wire R1: the v1 headline
    // and outcome sentences are gone; every test keeps its intent against the rules v2 result (HUD_SPEC 13.7): names from
    // the lobby, safe fallbacks, the own outcome, the Killer view, the lobby line and no banner words.
    public sealed class ResultCopyQaTests
    {
        private static S2C_LobbyState Lobby(params string[] nicknames)
        {
            var lobby = new S2C_LobbyState();
            for (int i = 0; i < nicknames.Length; i++)
                lobby.Slots[i] = new Slot { PlayerId = i + 1, Nickname = nicknames[i] };
            return lobby;
        }

        // Survivors 1..starting; the escaped ones listed, the rest eliminated.
        private static S2C_GameResult Result(GameEndReason reason, int starting, params int[] escaped)
        {
            var result = new S2C_GameResult
            {
                Reason = (byte)reason,
                WinnerRole = (byte)(escaped.Length * 2 >= starting ? Role.Survivor : Role.Killer)
            };
            result.EscapedIds.AddRange(escaped);
            for (int id = 1; id <= starting; id++) if (Array.IndexOf(escaped, id) < 0) result.EliminatedIds.Add(id);
            return result;
        }

        private static List<string> Names(ResultV2View view)
        {
            var names = new List<string>();
            for (int i = 0; i < view.Rows.Count; i++) names.Add(ResultV2Copy.RowName(view.Rows[i], i));
            return names;
        }

        // ---- the reasons and the rows -----------------------------------------------------------------------------------

        [Test]
        public void TheRowsNameEveryStartingSurvivorWithTheirOutcome()
        {
            S2C_LobbyState lobby = Lobby("Ana", "Bo", "Cy");
            ResultV2View view = V2Wire.Result(Result(GameEndReason.AllResolved, 3, 2), lobby, 1, Role.Survivor);
            Assert.That(Names(view), Is.EqualTo(new[] { "Ana (you)", "Bo", "Cy" }));
            Assert.That(ResultV2Copy.RowState(view.Rows[1]), Is.EqualTo("Escaped"));
            Assert.That(ResultV2Copy.RowState(view.Rows[0]), Is.EqualTo("Eliminated"));
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Killer wins"), "1 of 3 is fewer than half");
        }

        [Test]
        public void AllSurvivorsDownIsAKillerWinWithTheFixedSentence()
        {
            ResultV2View view = V2Wire.Result(Result(GameEndReason.AllSurvivorsDown, 2), Lobby("Ana", "Bo"), 1, Role.Survivor);
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Killer wins"));
            Assert.That(ResultV2Copy.Reason(view.Reason), Is.EqualTo("Every Survivor was eliminated before anyone escaped."));
        }

        [Test]
        public void TimeUpReadsTheFixedSentence()
        {
            ResultV2View view = V2Wire.Result(Result(GameEndReason.TimeUp, 2), Lobby("Ana", "Bo"), 1, Role.Survivor);
            Assert.That(ResultV2Copy.Reason(view.Reason), Is.EqualTo("Time ran out. Survivors still inside were eliminated."));
        }

        [Test]
        public void TheWinnerIsTheServersWinnerRoleNotTheReason()
        {
            S2C_GameResult stray = Result(GameEndReason.AllSurvivorsDown, 2, 1);
            stray.WinnerRole = (byte)Role.Killer;
            ResultV2View view = V2Wire.Result(stray, Lobby("Ana", "Bo"), 1, Role.Survivor);
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Killer wins"));
            Assert.That(ResultV2Copy.Consistent(view), Is.False, "1 of 2 escaped is a Survivors win by the rule: the dev check flags it");
        }

        [Test]
        public void AnIdMissingFromTheLobbyFallsBackToASurvivorNumber()
        {
            ResultV2View view = V2Wire.Result(Result(GameEndReason.AllResolved, 0, 9), Lobby("Ana"), 1, Role.Survivor);
            Assert.That(Names(view), Is.EqualTo(new[] { "Survivor 1" }));
        }

        [Test]
        public void ABlankNicknameFallsBackToASurvivorNumber()
        {
            S2C_LobbyState lobby = Lobby("Ana", "");
            ResultV2View view = V2Wire.Result(Result(GameEndReason.AllResolved, 2, 2), lobby, 1, Role.Survivor);
            Assert.That(Names(view), Is.EqualTo(new[] { "Ana (you)", "Survivor 2" }));
        }

        [Test]
        public void NoLobbyAtAllStillGivesRowsInIdOrder()
        {
            ResultV2View view = V2Wire.Result(Result(GameEndReason.AllResolved, 3, 3, 1), null, 2, Role.Survivor);
            Assert.That(Names(view), Is.EqualTo(new[] { "Survivor 1", "Survivor 2 (you)", "Survivor 3" }));
        }

        [Test]
        public void ANullSlotInTheLobbyIsSkipped()
        {
            S2C_LobbyState lobby = Lobby("Ana", "Bo");
            lobby.Slots[0] = null;
            ResultV2View view = null;
            Assert.DoesNotThrow(() => view = V2Wire.Result(Result(GameEndReason.AllResolved, 2, 2), lobby, 2, Role.Survivor));
            Assert.That(Names(view), Is.EqualTo(new[] { "Bo (you)", "Survivor 2" }), "the lobby order first, then ids missing from it");
        }

        [Test]
        public void AnEmptyResultGivesNoRowsAndNoBlankNames()
        {
            ResultV2View view = V2Wire.Result(new S2C_GameResult { WinnerRole = (byte)Role.Killer }, Lobby("Ana"), 1, Role.Survivor);
            Assert.That(view.Rows, Is.Empty);
            Assert.That(ResultV2Copy.Rule(view.Escaped, view.Starting), Does.Not.Contain("null"));
            Assert.That(ResultV2Copy.Outcome(view), Is.Empty, "no own row, no own line");
        }

        [Test]
        public void TheRuleLineStatesTheCountAndTheMajority()
        {
            Assert.That(ResultV2Copy.Rule(3, 7), Is.EqualTo("3 of 7 escaped · Survivors win when half or more escape (4 of 7)"));
            Assert.That(ResultV2Copy.Rule(1, 2), Is.EqualTo("1 of 2 escaped · Survivors win when half or more escape (1 of 2)"));
            Assert.That(ResultV2Copy.SurvivorsWin(4, 7), Is.True);
            Assert.That(ResultV2Copy.SurvivorsWin(3, 7), Is.False);
        }

        // ---- unknown reason ---------------------------------------------------------------------------------------------

        [TestCase((byte)4)]
        [TestCase((byte)99)]
        [TestCase((byte)255)]
        public void AnUnknownReasonByteDoesNotThrowAndStillReadsAsPlainWords(byte reason)
        {
            var result = new S2C_GameResult { Reason = reason, WinnerRole = 7 };
            result.EscapedIds.Add(2);
            ResultV2View view = null;
            Assert.DoesNotThrow(() => view = V2Wire.Result(result, Lobby("Ana", "Bo"), 1, Role.Survivor));
            string text = ResultV2Copy.Headline(view.Winner) + " " + ResultV2Copy.Reason(view.Reason);
            Assert.That(text, Does.Not.Contain("Exception"));
            Assert.That(text, Does.Not.Contain(reason.ToString()), "no raw byte value on screen");
            Assert.That(ResultV2Copy.Headline(view.Winner), Is.EqualTo("Survivors win"), "an unknown role byte reads as the Survivors");
        }

        [Test]
        public void ANullResultListDoesNotThrow()
        {
            var result = new S2C_GameResult { Reason = (byte)GameEndReason.AllResolved, EscapedIds = null, EliminatedIds = null };
            Assert.DoesNotThrow(() => V2Wire.Result(result, Lobby("Ana"), 1, Role.Survivor));
            Assert.DoesNotThrow(() => ResultV2Copy.Outcome(V2Wire.Result(result, null, 1, Role.Killer)));
        }

        // ---- the local player's own outcome -----------------------------------------------------------------------------

        [Test]
        public void ASurvivorOnTheEscapedListEscaped()
        {
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(Result(GameEndReason.AllResolved, 3, 2), null, 2, Role.Survivor)), Is.EqualTo("You escaped."));
        }

        [TestCase(GameEndReason.AllResolved)]
        [TestCase(GameEndReason.AllSurvivorsDown)]
        [TestCase(GameEndReason.TimeUp)]
        [TestCase(GameEndReason.Collapse)]
        public void AnEliminatedSurvivorWasEliminatedWhateverTheReason(GameEndReason reason)
        {
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(Result(reason, 3, 2), null, 1, Role.Survivor)), Is.EqualTo("You were eliminated."));
        }

        [Test]
        public void TheOwnRowIsMarkedOnceAndOnlyForTheOwnId()
        {
            ResultV2View view = V2Wire.Result(Result(GameEndReason.AllResolved, 3, 1, 3), Lobby("Ana", "Bo", "Cy"), 3, Role.Survivor);
            int own = 0;
            foreach (ResultRow row in view.Rows) if (row.Self) { own++; Assert.That(row.PlayerId, Is.EqualTo(3)); }
            Assert.That(own, Is.EqualTo(1));
        }

        [Test]
        public void TheKillerViewCountsEliminationsAndNeverAnEscape()
        {
            string lost = ResultV2Copy.Outcome(V2Wire.Result(Result(GameEndReason.AllResolved, 3, 1, 2), null, 9, Role.Killer));
            Assert.That(lost, Is.EqualTo("You eliminated 1 of 3 Survivors."));
            foreach (GameEndReason reason in new[] { GameEndReason.AllSurvivorsDown, GameEndReason.TimeUp, GameEndReason.Collapse })
            {
                string won = ResultV2Copy.Outcome(V2Wire.Result(Result(reason, 3), null, 9, Role.Killer));
                Assert.That(won, Is.EqualTo("You eliminated 3 of 3 Survivors."), reason.ToString());
                Assert.That(won, Is.Not.EqualTo("You escaped."));
            }
        }

        [Test]
        public void TheKillerNeverSeesAnEscapedListContainingItsOwnId()
        {
            // A killer id inside EscapedIds would be a server bug; the killer outcome must still be a killer sentence.
            string text = ResultV2Copy.Outcome(V2Wire.Result(Result(GameEndReason.AllResolved, 1, 1), null, 1, Role.Killer));
            Assert.That(text, Is.Not.EqualTo("You escaped."));
            Assert.That(text, Does.StartWith("You eliminated"));
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
            foreach (GameEndReason reason in new[] { GameEndReason.AllResolved, GameEndReason.AllSurvivorsDown, GameEndReason.TimeUp, GameEndReason.Collapse, (GameEndReason)77 })
                foreach (Role role in new[] { Role.Survivor, Role.Killer })
                {
                    ResultV2View view = V2Wire.Result(Result(reason, 2, 1), lobby, 1, role);
                    texts.Add(ResultV2Copy.Headline(view.Winner));
                    texts.Add(ResultV2Copy.Reason(view.Reason));
                    texts.Add(ResultV2Copy.Rule(view.Escaped, view.Starting));
                    texts.Add(ResultV2Copy.Outcome(view));
                    texts.AddRange(Names(view));
                }
            texts.Add(ResultCopy.LobbyLine(false, false)); texts.Add(ResultCopy.LobbyLine(false, true));
            texts.Add(ResultCopy.LobbyLine(true, false)); texts.Add(ResultCopy.LobbyLine(true, true));
            texts.Add(ResultCopy.PreviewNote);
            foreach (string text in texts)
            {
                Assert.That(text, Does.Not.Contain("!"), text);
                Assert.That(text.ToUpperInvariant(), Does.Not.Contain("VICTORY"), text);
                Assert.That(text.ToUpperInvariant(), Does.Not.Contain("DEFEAT"), text);
                Assert.That(text.ToLowerInvariant(), Does.Not.Contain("confetti"), text);
                Assert.That(text, Does.Not.Contain("U4"), text);
                Assert.That(text, Does.Not.Contain("U2b"), text);
                Assert.That(text, Does.Not.Contain("reached the Exit"), "v1 single-exit wording");
            }
        }
    }
}
