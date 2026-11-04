using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Tests
{
    public sealed class ConnectionMessageTests
    {
        private const string Endpoint = "192.168.0.10:7777";

        [Test]
        public void RefusedOrUnreachableBeforeConnectingSaysNotReachable()
        {
            foreach (Exception error in new Exception[]
            {
                new SocketException((int)SocketError.ConnectionRefused),
                new AggregateException(new SocketException((int)SocketError.ConnectionRefused)),
                new SocketException((int)SocketError.HostUnreachable),
                new SocketException((int)SocketError.NetworkUnreachable),
                new IOException("anything")
            })
            {
                Assert.That(ConnectionMessages.Classify(error, false), Is.EqualTo(LinkFailure.Unreachable), error.GetType().Name);
                Assert.That(ConnectionMessages.Describe(error, false, Endpoint),
                    Is.EqualTo("Server not reachable at 192.168.0.10:7777. Check that the host is running and the port is right."));
            }
        }

        [Test]
        public void TimeoutBeforeConnectingSaysNoAnswerWithinFiveSeconds()
        {
            foreach (Exception error in new Exception[]
            {
                new TimeoutException(), new AggregateException(new SocketException((int)SocketError.TimedOut)),
                new SocketException((int)SocketError.TimedOut)
            })
                Assert.That(ConnectionMessages.Describe(error, false, Endpoint),
                    Is.EqualTo("No answer from 192.168.0.10:7777 within 5 s. Check the IP and that both PCs are on the same network."));
        }

        [Test]
        public void AnyFailureAfterConnectingSaysLost()
        {
            foreach (Exception error in new Exception[] { new IOException("closed"), new TimeoutException(), new SocketException((int)SocketError.ConnectionReset) })
                Assert.That(ConnectionMessages.Describe(error, true, Endpoint),
                    Is.EqualTo("Connection to 192.168.0.10:7777 lost. Rejoin when the host is back."));
        }

        [Test]
        public void RawExceptionTextNeverReachesTheMessage()
        {
            var error = new AggregateException(new SocketException((int)SocketError.ConnectionRefused));
            string raw = error.GetBaseException().Message;
            foreach (bool connected in new[] { false, true })
            {
                string text = ConnectionMessages.Describe(error, connected, Endpoint);
                if (raw.Length > 0) Assert.That(text, Does.Not.Contain(raw));
                Assert.That(text, Does.Not.Contain("Exception"));
            }
        }

        [Test]
        public void FixedMessagesNameNoProtocolVersionOrUnit()
        {
            foreach (string text in new[] { ConnectionMessages.LobbyTimeout, ConnectionMessages.Left, ConnectionMessages.OfflineStopped,
                         ConnectionMessages.Rejected(1, Endpoint), ConnectionMessages.Rejected(2, Endpoint) })
            {
                Assert.That(text, Does.Not.Contain("protocol").IgnoreCase);
                Assert.That(text, Does.Not.Match(@"v\d|U\d"));
                Assert.That(text, Does.Not.Contain("!"));
            }
            Assert.That(ConnectionMessages.LobbyTimeout, Is.EqualTo("The server did not open a lobby. Check that the host runs the same version as this client."));
        }
    }

    public sealed class ResultCopyTests
    {
        private static S2C_LobbyState Lobby()
        {
            var lobby = new S2C_LobbyState();
            lobby.Slots[0] = new Slot { PlayerId = 1, Nickname = "Mina" };
            lobby.Slots[1] = new Slot { PlayerId = 2, Nickname = "Bot 2", IsBot = 1 };
            lobby.Slots[2] = new Slot { PlayerId = 3, Nickname = "Bot 3", IsBot = 1 };
            return lobby;
        }

        private static S2C_GameResult Result(GameEndReason reason, params int[] escaped) => new S2C_GameResult
        {
            WinnerRole = (byte)(reason == GameEndReason.AllResolved ? Role.Survivor : Role.Killer), Reason = (byte)reason,
            EscapedIds = new List<int>(escaped)
        };

        // U6c-wire R1: the v1 headline / outcome tests now check the rules v2 result (HUD_SPEC 13.7) built from the same
        // GameResult and lobby. Every lobby player not escaped is eliminated here (Survivors only; the Killer is in neither list).
        private static ResultV2View V2(GameEndReason reason, int ownId, Role ownRole, params int[] escaped)
        {
            S2C_GameResult result = Result(reason, escaped);
            foreach (int id in new[] { 1, 2, 3 }) if (Array.IndexOf(escaped, id) < 0) result.EliminatedIds.Add(id);
            return V2Wire.Result(result, Lobby(), ownId, ownRole);
        }

        [Test]
        public void HeadlineStatesTheWinnerAndTheReasonLineNamesEveryReason()
        {
            Assert.That(ResultV2Copy.Headline(V2(GameEndReason.AllResolved, 1, Role.Survivor, 1, 2).Winner), Is.EqualTo("Survivors win"));
            Assert.That(ResultV2Copy.Headline(V2(GameEndReason.AllSurvivorsDown, 1, Role.Survivor).Winner), Is.EqualTo("Killer wins"));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.AllResolved), Is.EqualTo("Every Survivor escaped or was eliminated."));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.AllSurvivorsDown), Is.EqualTo("Every Survivor was eliminated before anyone escaped."));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.TimeUp), Is.EqualTo("Time ran out. Survivors still inside were eliminated."));
            Assert.That(ResultV2Copy.Reason(ResultReasonV2.Collapse), Is.EqualTo("The collapse eliminated the Survivors still inside."));
        }

        // v0.4 (GDD v2): the winner comes from WinnerRole (at least half escaped), whatever the reason.
        [Test]
        public void TheWinnerFollowsWinnerRoleAndTheRowsFollowTheIdLists()
        {
            S2C_GameResult wire = Result(GameEndReason.Collapse, 1, 3);
            wire.WinnerRole = (byte)Role.Survivor;
            wire.EliminatedIds.Add(2);
            ResultV2View collapse = V2Wire.Result(wire, Lobby(), 1, Role.Survivor);
            Assert.That(collapse.Winner, Is.EqualTo(Role.Survivor));
            Assert.That(collapse.Rows.Count, Is.EqualTo(3));
            Assert.That(ResultV2Copy.Rule(collapse.Escaped, collapse.Starting), Is.EqualTo("2 of 3 escaped · Survivors win when half or more escape (2 of 3)"));
            ResultV2View killer = V2(GameEndReason.TimeUp, 9, Role.Killer, 1);
            Assert.That(killer.Winner, Is.EqualTo(Role.Killer));
            Assert.That(ResultV2Copy.Outcome(killer), Is.EqualTo("You eliminated 2 of 3 Survivors."));
            Assert.That(ResultV2Copy.Consistent(killer), Is.True);
        }

        // v0.4: both exits from the per-exit flags (the v1 single Exit line is gone, HUD_SPEC 13.1).
        [Test]
        public void ExitCellsReadThePerExitFlags()
        {
            static S2C_WorldState World(params ExitFlags[] flags)
            {
                var world = new S2C_WorldState();
                for (int i = 0; i < flags.Length; i++) world.Exits.Add(new ExitState { Id = (byte)i, Flags = (byte)flags[i] });
                return world;
            }
            Assert.That(V2Wire.Exits(World()).Length, Is.EqualTo(0));
            Assert.That(ExitCopy.Tag(V2Wire.Exits(World(0, 0))[1]), Is.EqualTo("Exit 2 · No power"));
            Assert.That(ExitCopy.State(V2Wire.Exits(World(ExitFlags.Powered, ExitFlags.Powered))[0]), Is.EqualTo("Powered"));
            Assert.That(ExitCopy.State(V2Wire.Exits(World(ExitFlags.Powered, ExitFlags.Powered | ExitFlags.Locked))[1]), Is.EqualTo("Locked"));
            Assert.That(ExitCopy.State(V2Wire.Exits(World(ExitFlags.Powered | ExitFlags.Open, ExitFlags.Powered | ExitFlags.Locked))[0]), Is.EqualTo("Open"));
            Assert.That(WorldExits.Powered(World(ExitFlags.Powered, ExitFlags.Powered)), Is.True);
            Assert.That(WorldExits.Powered(World(ExitFlags.Powered, 0)), Is.False);
            Assert.That(WorldExits.Powered(World()), Is.False);
            Assert.That(WorldExits.AnyOpen(null), Is.False);
        }

        [Test]
        public void UnknownIdsAndMissingLobbyFallBackToASurvivorNumber()
        {
            var result = new S2C_GameResult { WinnerRole = (byte)Role.Survivor, EscapedIds = new List<int> { 9 } };
            ResultV2View view = V2Wire.Result(result, Lobby(), 1, Role.Survivor);
            Assert.That(view.Rows.Count, Is.EqualTo(1), "an id outside the lobby still gets its row");
            Assert.That(ResultV2Copy.RowName(view.Rows[0], 0), Is.EqualTo("Survivor 1"));
            ResultV2View noLobby = V2Wire.Result(Result(GameEndReason.AllResolved, 3, 1), null, 1, Role.Survivor);
            Assert.That(noLobby.Rows[0].PlayerId, Is.EqualTo(1), "without a lobby the rows follow the ids in order");
            Assert.That(ResultV2Copy.RowName(noLobby.Rows[0], 0), Is.EqualTo("Survivor 1 (you)"));
        }

        [Test]
        public void OutcomeIsThePlayersOwn()
        {
            Assert.That(ResultV2Copy.Outcome(V2(GameEndReason.AllResolved, 1, Role.Survivor, 1, 2)), Is.EqualTo("You escaped."));
            Assert.That(ResultV2Copy.Outcome(V2(GameEndReason.AllResolved, 1, Role.Survivor, 2, 3)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2(GameEndReason.TimeUp, 1, Role.Survivor)), Is.EqualTo("You were eliminated."));
            Assert.That(ResultV2Copy.Outcome(V2(GameEndReason.TimeUp, 7, Role.Killer)), Is.EqualTo("You eliminated 3 of 3 Survivors."));
            Assert.That(ResultV2Copy.Outcome(V2(GameEndReason.AllResolved, 7, Role.Killer, 1, 2)), Is.EqualTo("You eliminated 1 of 3 Survivors."));
        }

        [Test]
        public void LobbyLineWaitsUntilTheLobbyReopens()
        {
            Assert.That(ResultCopy.LobbyLine(false, false), Is.EqualTo("Waiting for the host to reopen the lobby."));
            Assert.That(ResultCopy.LobbyLine(false, true), Is.EqualTo("Waiting for the lobby to reopen."));
            Assert.That(ResultCopy.LobbyLine(true, false), Is.EqualTo(ResultCopy.LobbyOpen));
        }

        [Test]
        public void NoBannerWordsOrExclamations()
        {
            foreach (GameEndReason reason in Enum.GetValues(typeof(GameEndReason)))
            {
                ResultV2View view = V2(reason, 1, Role.Survivor, 1);
                string text = ResultV2Copy.Headline(view.Winner) + " " + ResultV2Copy.Reason(view.Reason) + " " + ResultV2Copy.Outcome(view);
                Assert.That(text, Does.Not.Contain("VICTORY").IgnoreCase.And.Not.Contain("DEFEAT").IgnoreCase.And.Not.Contain("!"));
            }
        }
    }

    public sealed class HudCopyTests
    {
        [TestCase(Role.Killer, 0, "Ping Scanner")]
        [TestCase(Role.Killer, 1, "Overclock")]
        [TestCase(Role.Killer, 2, "Packet Afterimage")]
        [TestCase(Role.Killer, 3, "Firewall Block")]
        [TestCase(Role.Survivor, 0, "Dummy Packet")]
        [TestCase(Role.Survivor, 1, "Stealth Relay")]
        [TestCase(Role.Survivor, 2, "Traffic Overload")]
        [TestCase(Role.Survivor, 3, "Emergency Restore")]
        [TestCase(Role.Survivor, 4, "White Noise")]
        public void SkillNamesFollowGddOrder(Role role, int id, string name)
        {
            Assert.That(HudCopy.SkillName(role, (byte)id), Is.EqualTo(name));
        }

        [Test]
        public void OnlyPacketAfterimageIsPassive()
        {
            Assert.That(HudCopy.SkillLine(Role.Killer, 2), Is.EqualTo("Passive · Packet Afterimage"));
            Assert.That(HudCopy.SkillLine(Role.Killer, 0), Is.EqualTo("Q · Ping Scanner"));
            Assert.That(HudCopy.SkillLine(Role.Survivor, 2), Is.EqualTo("Q · Traffic Overload"));
            for (byte id = 0; id < 5; id++) Assert.That(HudCopy.IsPassive(Role.Survivor, id), Is.False);
            Assert.That(HudCopy.SkillName(Role.Survivor, 9), Is.EqualTo("Skill 9"), "unknown IDs never throw");
        }

        [Test]
        public void StatusLineShowsNothingWhileNormal()
        {
            Assert.That(HudCopy.StatusLine(PlayerStatus.Normal, 5), Is.Empty);
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, 42.2f), Is.EqualTo("Lagging · 43 s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, 60f), Is.EqualTo("Lagging · 60 s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, 0.01f), Is.EqualTo("Lagging · 1 s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Lagging, float.NaN), Is.EqualTo("Lagging · 0 s"));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Escaped, 0), Is.EqualTo("Escaped. Waiting for the match to end."));
            Assert.That(HudCopy.StatusLine(PlayerStatus.Eliminated, 0), Is.EqualTo("Eliminated. Watching until the match ends."));
        }

        [Test]
        public void TerminalsLineCountsOnlyFullProgress()
        {
            var terminals = new List<TerminalState>
            {
                new TerminalState { Id = 0, Progress = 100 }, new TerminalState { Id = 1, Progress = 99 },
                new TerminalState { Id = 2, Progress = 0 }, new TerminalState { Id = 3, Progress = 100 }
            };
            // U6c-wire S5: the v1 single-exit suffix is gone; the exits live in the objective row (HUD_SPEC 13.1).
            Assert.That(HudCopy.TerminalsLine(terminals, 3), Is.EqualTo("Terminals 2/3"));
            Assert.That(HudCopy.TerminalsLine(terminals, 2), Is.EqualTo("Terminals 2/2"));
            Assert.That(HudCopy.TerminalsLine(new List<TerminalState>(), 2), Is.EqualTo("Terminals 0/2"));
        }

        [Test]
        public void HackingReadsTheNearestTerminal()
        {
            var terminals = new List<TerminalState>
            {
                new TerminalState { Id = 0, X = 10, Z = 25, Progress = 10 }, new TerminalState { Id = 1, X = 28, Z = 12, Progress = 42 }
            };
            Assert.That(HudCopy.NearestTerminal(terminals, 27, 13), Is.EqualTo(1));
            Assert.That(HudCopy.HackingLine(terminals[HudCopy.NearestTerminal(terminals, 27, 13)].Progress), Is.EqualTo("Hacking · 42%"));
            Assert.That(HudCopy.NearestTerminal(new List<TerminalState>(), 0, 0), Is.EqualTo(-1));
        }
    }

    public sealed class OnScreenCopyTests
    {
        // No internal unit names (U4b, U2b ...) or protocol version strings in any string literal of the client.
        [Test]
        public void ClientStringLiteralsNameNoUnitOrProtocolVersion()
        {
            string scripts = Path.Combine(UnityEngine.Application.dataPath, "Scripts");
            var literal = new System.Text.RegularExpressions.Regex(@"""(?:[^""\\\r\n]|\\.)*""");
            var forbidden = new System.Text.RegularExpressions.Regex(@"\bU\d+[a-z]?\b|protocol v|\bv0\.\d");
            string[] files = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(10), "client scripts not found under " + scripts);
            foreach (string file in files)
                foreach (System.Text.RegularExpressions.Match match in literal.Matches(File.ReadAllText(file)))
                    Assert.That(forbidden.IsMatch(match.Value), Is.False, Path.GetFileName(file) + ": " + match.Value);
        }
    }

    public sealed class LobbyCopyTests
    {
        [Test]
        public void RowsAreNicknamePlusMarkersOnly()
        {
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 4, Nickname = "Player", IsHost = 1 }, 4), Is.EqualTo("Player · host · you"));
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 5, Nickname = "Player" }, 4), Is.EqualTo("Player"));
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 6, Nickname = "Bot 6", IsBot = 1 }, 4), Is.EqualTo("Bot 6 · bot"));
            Assert.That(LobbyCopy.SlotText(new Slot(), 4), Is.EqualTo("Empty"));
            Assert.That(LobbyCopy.SlotText(new Slot { PlayerId = 5, Nickname = "Player" }, 4), Does.Not.Contain("player"));
        }

        [Test]
        public void OfflineLobbyIsMarked()
        {
            Assert.That(LobbyCopy.Title(true), Is.EqualTo("Lobby · offline"));
            Assert.That(LobbyCopy.Title(false), Is.EqualTo("Lobby"));
            Assert.That(LobbyCopy.RoleLine((byte)RoleChoice.Killer, true), Is.EqualTo("Your role: Killer"));
            Assert.That(LobbyCopy.RoleLine((byte)RoleChoice.Random, false), Is.EqualTo("Host role: Random"));
        }
    }
}
