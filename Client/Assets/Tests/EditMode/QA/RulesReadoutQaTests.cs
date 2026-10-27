using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Readouts checked against the U2b rules (GDD 3.3 to 3.5, Server.Core GameServer.Rules / Visibility), written from the
    // rules and not from the implementation: Lagging countdown from StatusTimer, Terminals k/N with N = TerminalsRequired,
    // Exit open / locked / firewall-locked, Escaped and Eliminated text, result copy per end reason.
    [Category("U4bQaA2")]
    public sealed class RulesReadoutQaTests
    {
        private const string Dot = "·";

        private static TerminalState Terminal(byte id, byte progress) => new TerminalState { Id = id, Progress = progress };

        // ---- Lagging countdown ---------------------------------------------------------------------------------------------

        // GameServer.StatusTimer is the remaining Lagging seconds (LaggingUntil - now), counted down from the 60 s timeout.
        [Test]
        public void ALaggingPlayerWhoJustGotCaughtSeesTheFullSixtySeconds()
        {
            Assert.That(GameConstants.LaggingTimeoutSeconds, Is.EqualTo(60f), "GDD 3.3: 60 s to be rescued");
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, GameConstants.LaggingTimeoutSeconds), Is.EqualTo("Lagging " + Dot + " 60 s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, GameConstants.LaggingTimeoutSeconds - 0.05f), Is.EqualTo("Lagging " + Dot + " 60 s"), "one tick later is still 60");
        }

        [Test]
        public void TheCountdownRunsOneSecondAtATimeWithoutSkippingOrRepeatingAValue()
        {
            // Simulate the 20 Hz server timer from 60 s down to the last tick before the timeout.
            float tickSeconds = 1f / GameConstants.TickRate;
            int previous = int.MaxValue;
            var ticksPerValue = new Dictionary<int, int>();
            for (int tick = 0; tick < 60 * GameConstants.TickRate; tick++)
            {
                float timer = 60f - tick * tickSeconds;
                string text = HudCopy.StatusLine(PlayerStatus.Lagging, timer);
                Assert.That(text, Does.StartWith("Lagging " + Dot + " ").And.EndWith(" s"), text);
                int shown = int.Parse(text.Substring(("Lagging " + Dot + " ").Length).Replace(" s", string.Empty));
                Assert.That(shown, Is.GreaterThanOrEqualTo(1).And.LessThanOrEqualTo(60), "timer " + timer);
                Assert.That(shown, Is.LessThanOrEqualTo(previous), "the number never goes up (timer " + timer + ")");
                Assert.That(previous == int.MaxValue || previous - shown <= 1, Is.True, "no skipped second at timer " + timer);
                previous = shown;
                int seen;
                ticksPerValue.TryGetValue(shown, out seen);
                ticksPerValue[shown] = seen + 1;
            }
            Assert.That(previous, Is.EqualTo(1), "the last value before the timeout is 1");
            Assert.That(ticksPerValue.Count, Is.EqualTo(60), "every value from 60 to 1 is shown");
            foreach (KeyValuePair<int, int> pair in ticksPerValue)
                Assert.That(pair.Value, Is.InRange(19, 21), "value " + pair.Key + " is on screen for about one second (20 ticks)");
        }

        [Test]
        public void TheCountdownTextBelongsToLaggingOnly()
        {
            // The server sends StatusTimer 0 for every other status; the HUD must not turn that into a countdown.
            Assert.That(HudCopy.StatusLine(PlayerStatus.Normal, 0), Is.Empty);
            Assert.That(HudCopy.StatusLine(PlayerStatus.Normal, 42), Is.Empty, "a stale timer on a Normal player shows nothing");
            Assert.That(HudCopy.StatusLine(PlayerStatus.Escaped, 0), Does.Not.Contain(" s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Eliminated, 0), Does.Not.Contain(" s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Escaped, 30), Does.Not.Contain("30"));
        }

        [Test]
        public void ALaggingLineUsesTheMiddleDotAndASpaceBeforeTheUnit()
        {
            string text = HudCopy.StatusLine(PlayerStatus.Lagging, 12.4f);
            Assert.That(text, Is.EqualTo("Lagging " + Dot + " 13 s"));
            Assert.That(text, Does.Not.Contain("..."));
            Assert.That(text, Does.Not.Contain("!"));
        }

        // ---- Escaped and Eliminated ---------------------------------------------------------------------------------------

        [Test]
        public void EscapedAndEliminatedAreSentencesThatNameTheStateAndTheWait()
        {
            string escaped = HudCopy.StatusLine(PlayerStatus.Escaped, 0), eliminated = HudCopy.StatusLine(PlayerStatus.Eliminated, 0);
            Assert.That(escaped, Is.EqualTo("Escaped. Waiting for the match to end."));
            Assert.That(eliminated, Is.EqualTo("Eliminated. Watching until the match ends."));
            Assert.That(escaped, Is.Not.EqualTo(eliminated));
            Assert.That(escaped, Does.Not.Contain("Lagging"));
            Assert.That(eliminated, Does.Not.Contain("Lagging"));
            Assert.That(escaped, Does.Not.Contain("won"), "an escaped survivor is not told the team won; the match is still running");
        }

        // ---- Terminals k/N ---------------------------------------------------------------------------------------------------

        [Test]
        public void TheRequiredNumberIsTheServersNotTheNumberOfTerminalsOnTheMap()
        {
            // GameConstants.TerminalsFor: survivors 1-2 need 2 of 4, 3-5 need 3 of 5, 6-7 need 4 of 6.
            var map = new FixedMap();
            foreach (int survivors in new[] { 1, 2, 3, 5, 6, 7 })
            {
                TerminalState[] placed = map.CreateTerminals(survivors, out byte required);
                Assert.That(placed.Length, Is.GreaterThan(required), "the map has spare terminals (survivors " + survivors + ")");
                Assert.That(HudCopy.TerminalsLine(placed, required, false), Is.EqualTo("Terminals 0/" + required + " " + Dot + " Exit locked"), "survivors " + survivors);
                for (int i = 0; i < placed.Length; i++) placed[i].Progress = 100;
                Assert.That(HudCopy.TerminalsLine(placed, required, true), Does.StartWith("Terminals " + placed.Length + "/" + required),
                    "k counts every finished terminal; N stays the server's number");
            }
        }

        [Test]
        public void OnlyAFinishedTerminalCountsAndTheCountFollowsEachCompletion()
        {
            var terminals = new[] { Terminal(0, 0), Terminal(1, 0), Terminal(2, 0), Terminal(3, 0), Terminal(4, 0), Terminal(5, 0) };
            const byte required = 4;
            for (int done = 0; done <= 4; done++)
            {
                for (int i = 0; i < terminals.Length; i++) terminals[i].Progress = (byte)(i < done ? 100 : i == done ? 99 : 1);
                string expected = "Terminals " + done + "/" + required + " " + Dot + " Exit locked";
                Assert.That(HudCopy.TerminalsLine(terminals, required, false), Is.EqualTo(expected), "done " + done + ": 1 to 99 percent never counts");
            }
        }

        [Test]
        public void TheHackingPercentageIsTheNearestTerminalsProgressFromTheServer()
        {
            // The server writes floor(percent), capped at 99 until the hack completes (Hack in GameServer.Rules).
            Assert.That(HudCopy.HackingLine(1), Is.EqualTo("Hacking " + Dot + " 1%"));
            Assert.That(HudCopy.HackingLine(99), Is.EqualTo("Hacking " + Dot + " 99%"));
            Assert.That(HudCopy.HackingLine(100), Is.EqualTo("Hacking " + Dot + " 100%"));
        }

        [Test]
        public void MoreFinishedTerminalsThanRequiredStillReadAsKOverTheServersN()
        {
            // The server keeps hacking after the exit opens, so a count above N is reachable (for example 5/4).
            // The readout must at least keep the server's N and not throw; see the QA report for the design note.
            TerminalState[] five = { Terminal(0, 100), Terminal(1, 100), Terminal(2, 100), Terminal(3, 100), Terminal(4, 100), Terminal(5, 0) };
            string text = null;
            Assert.DoesNotThrow(() => text = HudCopy.TerminalsLine(five, 4, true));
            Assert.That(text, Does.Contain("/4 " + Dot + " Exit open"));
        }

        // ---- Exit: open, locked, firewall-locked ----------------------------------------------------------------------------

        // GameServer.Visibility: ExitOpen = _exitOpen, ExitLocked = ExitBlocked = !_exitOpen || the Firewall Block timer is active.
        // So (0,1) is "not opened yet", (1,0) is open, (1,1) is open but blocked by the killer's Firewall Block for 5 s.
        // ClientApp and WorldView both derive the displayed state as ExitOpen != 0 && ExitLocked == 0.
        [Test]
        public void ExitWordingForTheThreeStatesTheServerCanSend()
        {
            Assert.That(HudCopy.TerminalsLine(new TerminalState[0], 4, false), Does.EndWith(Dot + " Exit locked"), "ExitOpen 0, ExitLocked 1");
            Assert.That(HudCopy.TerminalsLine(new TerminalState[0], 4, true), Does.EndWith(Dot + " Exit open"), "ExitOpen 1, ExitLocked 0");
            Assert.That(HudCopy.ExitLabel(false), Is.EqualTo("Exit " + Dot + " Locked"));
            Assert.That(HudCopy.ExitLabel(true), Is.EqualTo("Exit " + Dot + " Open"));
            Assert.That(HudCopy.TerminalsLine(new TerminalState[0], 4, false), Does.Not.Contain("open"));
        }

        [Test]
        public void TheDisplayedExitStateIsOpenOnlyWhenExitOpenIsSetAndExitLockedIsNot()
        {
            string app = Squash(QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs")));
            Assert.That(app, Does.Contain("exitOpen=state.ExitOpen!=0&&state.ExitLocked==0;"),
                "ClientApp: firewall-locked (1,1) must read as locked, never as open");
            string world = Squash(QaSource.StripComments(QaPaths.ReadScript("World/WorldView.cs")));
            Assert.That(world, Does.Contain("boolopen=state.ExitOpen!=0&&state.ExitLocked==0;"), "WorldView ring uses the same rule");
            string dev = Squash(QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs")));
            Assert.That(dev, Does.Contain("exitOpen=state.ExitOpen!=0&&state.ExitLocked==0;"), "the dev log HUD text uses the same rule");
        }

        // ---- result copy per end reason (winner side and reason must agree) -----------------------------------------------------

        [Test]
        public void EachEndReasonNamesTheRightWinnerAndReason()
        {
            var lobby = new S2C_LobbyState();
            lobby.Slots[0] = new Slot { PlayerId = 1, Nickname = "Player", IsHost = 1 };
            lobby.Slots[1] = new Slot { PlayerId = 6, Nickname = "Bot 6", IsBot = 1 };

            var escaped = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, Reason = (byte)GameEndReason.SurvivorEscaped };
            escaped.EscapedIds.Add(6);
            Assert.That(ResultCopy.Headline(escaped, lobby), Is.EqualTo("Survivors win. Bot 6 reached the Exit."));

            var down = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            Assert.That(ResultCopy.Headline(down, lobby), Is.EqualTo("Killer wins. No survivor is left in play."));

            var time = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };
            Assert.That(ResultCopy.Headline(time, lobby), Is.EqualTo("Killer wins. Time ran out."));
        }

        [Test]
        public void TheOutcomeLineMatchesWhatHappenedToTheLocalPlayerForEveryRuleEnding()
        {
            var escaped = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, Reason = (byte)GameEndReason.SurvivorEscaped };
            escaped.EscapedIds.Add(5);
            var down = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            var time = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };

            // The local survivor is the one who escaped, even if the last status update was lost.
            Assert.That(ResultCopy.Outcome(escaped, 5, Role.Survivor, PlayerStatus.Escaped), Is.EqualTo("You escaped."));
            Assert.That(ResultCopy.Outcome(escaped, 5, Role.Survivor, PlayerStatus.Normal), Is.EqualTo("You escaped."));
            // A survivor who was not at the exit: team won, did not escape.
            Assert.That(ResultCopy.Outcome(escaped, 4, Role.Survivor, PlayerStatus.Normal), Does.Contain("did not reach the Exit"));
            // The a2 real run: AllSurvivorsDown while the local player was Lagging.
            Assert.That(ResultCopy.Outcome(down, 4, Role.Survivor, PlayerStatus.Lagging), Is.EqualTo("You were still Lagging when the match ended."));
            Assert.That(ResultCopy.Outcome(down, 4, Role.Survivor, PlayerStatus.Eliminated), Is.EqualTo("You were eliminated."));
            // The a2 real run: TimeUp, local survivor never caught.
            Assert.That(ResultCopy.Outcome(time, 4, Role.Survivor, PlayerStatus.Normal), Is.EqualTo("You did not escape."));
            // The killer sees win or loss by the reason.
            Assert.That(ResultCopy.Outcome(escaped, 1, Role.Killer, PlayerStatus.Normal), Is.EqualTo("You lost as the Killer."));
            Assert.That(ResultCopy.Outcome(down, 1, Role.Killer, PlayerStatus.Normal), Is.EqualTo("You won as the Killer."));
            Assert.That(ResultCopy.Outcome(time, 1, Role.Killer, PlayerStatus.Normal), Is.EqualTo("You won as the Killer."));
        }

        // ---- the play readouts never carry the killer's position (SLOP 6, AGENTS 1 visibility) -----------------------------------

        [Test]
        public void ASurvivorClientDropsTheKillerEntryBeforeCreatingAnyAvatar()
        {
            string world = Squash(QaSource.StripComments(QaPaths.ReadScript("World/WorldView.cs")));
            int accept = world.IndexOf("publicvoidAccept(", StringComparison.Ordinal);
            Assert.That(accept, Is.GreaterThanOrEqualTo(0));
            int guard = world.IndexOf("if(role==Role.Survivor&&player.Role==(byte)Role.Killer)continue;", accept, StringComparison.Ordinal);
            int create = world.IndexOf("PrimitiveType.Capsule", accept, StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThan(accept), "WorldView.Accept has the survivor-side killer guard");
            Assert.That(create, Is.GreaterThan(guard), "the guard comes before the avatar is created");
        }

        [Test]
        public void TheDevLogOnlyEverPrintsTheLocalPlayersPosition()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs"));
            string squashed = Squash(code);
            int guard = squashed.IndexOf("if(player.Id!=localId)continue;", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "OnWorld skips every player but the local one");
            int firstPositionRead = squashed.IndexOf("player.X", StringComparison.Ordinal);
            Assert.That(firstPositionRead, Is.GreaterThan(guard), "no player coordinate is read before the local-player guard");
        }

        private static string Squash(string source)
        {
            var text = new System.Text.StringBuilder(source.Length);
            foreach (char c in source) if (!char.IsWhiteSpace(c)) text.Append(c);
            return text.ToString();
        }
    }
}
