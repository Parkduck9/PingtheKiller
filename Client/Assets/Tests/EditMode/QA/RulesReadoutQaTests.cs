using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
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
                Assert.That(HudCopy.TerminalsLine(placed, required), Is.EqualTo("Terminals 0/" + required), "survivors " + survivors);
                for (int i = 0; i < placed.Length; i++) placed[i].Progress = 100;
                Assert.That(HudCopy.TerminalsLine(placed, required), Does.StartWith("Terminals " + placed.Length + "/" + required),
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
                string expected = "Terminals " + done + "/" + required;
                Assert.That(HudCopy.TerminalsLine(terminals, required), Is.EqualTo(expected), "done " + done + ": 1 to 99 percent never counts");
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
            Assert.DoesNotThrow(() => text = HudCopy.TerminalsLine(five, 4));
            Assert.That(text, Does.EndWith("/4"));
        }

        // ---- Exit: open, locked, firewall-locked ----------------------------------------------------------------------------

        // Protocol v0.4: each exit carries Powered, Open and Locked (its switch locked by Firewall Block) flags. U6c-wire: the
        // v1 single-exit wording is gone; each exit is read on its own (HUD_SPEC 13.4).
        [Test]
        public void ExitWordingForTheStatesTheServerCanSend()
        {
            Assert.That(ExitCopy.State(ExitView.FromWire(0, 0, 0)), Is.EqualTo("No power"));
            Assert.That(ExitCopy.State(ExitView.FromWire(0, 1, 0)), Is.EqualTo("Powered"));
            Assert.That(ExitCopy.State(ExitView.FromWire(0, 1, 62)), Is.EqualTo("62%"));
            Assert.That(ExitCopy.State(ExitView.FromWire(0, 1 | 4, 62)), Is.EqualTo("Locked"));
            Assert.That(ExitCopy.State(ExitView.FromWire(0, 1 | 2, 100)), Is.EqualTo("Open"));
            Assert.That(HudCopy.TerminalsLine(new TerminalState[0], 4), Does.Not.Contain("Exit"));
        }

        [Test]
        public void TheDisplayedExitStateIsOpenOnlyWhenAnExitIsOpen()
        {
            string app = Squash(QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs")));
            Assert.That(app, Does.Contain("ExitView[]exits=exitCache.For(lastWorld,"), "ClientApp reads each exit's v0.4 flags");
            string world = Squash(QaSource.StripComments(WorldSources.ReadAll()));
            Assert.That(world, Does.Contain("open=WorldExits.Has(exitState,ExitFlags.Open);"), "WorldView rings read each exit's own v0.4 Open flag");
            string dev = Squash(QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs")));
            Assert.That(dev, Does.Contain("boolexitOpen=WorldExits.AnyOpen(state);"), "the dev log HUD text uses the same rule");
        }

        // ---- result copy per end reason (winner side and reason must agree) -----------------------------------------------------

        // U6c-wire R1: the v1 headline / outcome sentences became the rules v2 result (HUD_SPEC 13.7); the intent stays:
        // the winner and the reason agree with the server, and the own line matches what happened to the local player.
        [Test]
        public void EachEndReasonNamesTheRightWinnerAndReason()
        {
            var lobby = new S2C_LobbyState();
            lobby.Slots[0] = new Slot { PlayerId = 1, Nickname = "Player", IsHost = 1 };
            lobby.Slots[1] = new Slot { PlayerId = 6, Nickname = "Bot 6", IsBot = 1 };

            var escaped = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, Reason = (byte)GameEndReason.AllResolved };
            escaped.EscapedIds.Add(6);
            ResultV2View won = V2Wire.Result(escaped, lobby, 1, Role.Killer);
            Assert.That(ResultV2Copy.Headline(won.Winner), Is.EqualTo("Survivors win"));
            Assert.That(ResultV2Copy.RowName(won.Rows[0], 0), Is.EqualTo("Bot 6"));

            var down = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            Assert.That(ResultV2Copy.Headline(V2Wire.Result(down, lobby, 1, Role.Killer).Winner), Is.EqualTo("Killer wins"));
            Assert.That(ResultV2Copy.Reason(V2Wire.Result(down, lobby, 1, Role.Killer).Reason), Is.EqualTo("Every Survivor was eliminated before anyone escaped."));

            var time = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.TimeUp };
            Assert.That(ResultV2Copy.Reason(V2Wire.Result(time, lobby, 1, Role.Killer).Reason), Is.EqualTo("Time ran out. Survivors still inside were eliminated."));
        }

        [Test]
        public void TheOutcomeLineMatchesWhatHappenedToTheLocalPlayerForEveryRuleEnding()
        {
            var escaped = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, Reason = (byte)GameEndReason.AllResolved };
            escaped.EscapedIds.Add(5);
            escaped.EliminatedIds.Add(4);
            var down = new S2C_GameResult { WinnerRole = (byte)Role.Killer, Reason = (byte)GameEndReason.AllSurvivorsDown };
            down.EliminatedIds.Add(4);
            down.EliminatedIds.Add(5);

            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(escaped, null, 5, Role.Survivor)), Is.EqualTo("You escaped."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(escaped, null, 4, Role.Survivor)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(down, null, 4, Role.Survivor)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(escaped, null, 1, Role.Killer)), Is.EqualTo("You eliminated 1 of 2 Survivors."));
            Assert.That(ResultV2Copy.Outcome(V2Wire.Result(down, null, 1, Role.Killer)), Is.EqualTo("You eliminated 2 of 2 Survivors."));
        }

        // ---- the play readouts never carry the killer's position (SLOP 6, AGENTS 1 visibility) -----------------------------------

        [Test]
        public void ASurvivorClientDropsTheKillerEntryBeforeCreatingAnyAvatar()
        {
            string world = Squash(QaSource.StripComments(QaPaths.ReadScript(WorldSources.Characters)));
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
            // U8 (11/17): the Survivor -ptk-chase seek reads exactly one other coordinate before the local-player guard, the
            // Killer-role entry, into killerEntry. killerEntry is only the Chase seek target and never reaches a Debug.Log;
            // the chase log drops its to= part while seeking.
            string code = QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs"));
            string squashed = Squash(code);
            int guard = squashed.IndexOf("if(player.Id!=localId)continue;", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "OnWorld skips every player but the local one");
            const string killerRead = "if(player.Role==(byte)Role.Killer){killerEntry=newVector3(player.X,0,player.Z);killerEntryKnown=true;continue;}";
            int killer = squashed.IndexOf(killerRead, StringComparison.Ordinal);
            Assert.That(killer, Is.GreaterThanOrEqualTo(0).And.LessThan(guard), "the Killer-role entry is read just before the guard");
            int firstPositionRead = squashed.IndexOf("player.X", StringComparison.Ordinal);
            Assert.That(firstPositionRead, Is.EqualTo(killer + killerRead.IndexOf("player.X", StringComparison.Ordinal)), "the only coordinate read before the guard is the Killer entry's");
            Assert.That(squashed.IndexOf("player.X", firstPositionRead + 1, StringComparison.Ordinal), Is.GreaterThan(guard), "no other coordinate before the guard");
            Assert.That(Regex.Matches(squashed, @"killerEntry(?!Known)").Count, Is.EqualTo(3), "declared, assigned, and passed to Chase only");
            Assert.That(squashed, Does.Contain("returnChase(killerEntryKnown,killerEntry,outyaw);"));
            foreach (Match log in Regex.Matches(squashed, @"Debug\.Log\((?:[^;])*;"))
                Assert.That(log.Value, Does.Not.Contain("killerEntry"), "a dev log never prints the Killer entry");
            Assert.That(squashed, Does.Contain("(seek?string.Empty:\"to=\"+Stamp(goal.x)"), "the seek target is not logged");
        }

        private static string Squash(string source)
        {
            var text = new System.Text.StringBuilder(source.Length);
            foreach (char c in source) if (!char.IsWhiteSpace(c)) text.Append(c);
            return text.ToString();
        }
    }
}
