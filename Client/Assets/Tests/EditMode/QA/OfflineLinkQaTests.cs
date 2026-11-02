using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 1 and docs/briefs/U4b-qa-a1.md item 1. The real GameServer thread runs behind
    // OfflineLink (InMemoryTransport). Every wait has a timeout of at most 5 s and every link is disposed in finally.
    public sealed class OfflineLinkQaTests
    {
        private const int WaitMs = 5000;

        private sealed class Probe : IDisposable
        {
            public readonly OfflineLink Link;
            public readonly List<IPacket> Seen = new List<IPacket>();
            public readonly List<string> Notices = new List<string>();
            public int OffThread, OutsidePump;
            private readonly int owner = Thread.CurrentThread.ManagedThreadId;
            private bool inPump;
            private int cursor;

            public Probe(int bots = 3)
            {
                Link = new OfflineLink(bots);
                Link.PacketReceived += received =>
                {
                    if (Thread.CurrentThread.ManagedThreadId != owner) OffThread++;
                    if (!inPump) OutsidePump++;
                    Seen.Add(received.Packet);
                };
                Link.Disconnected += text =>
                {
                    if (Thread.CurrentThread.ManagedThreadId != owner) OffThread++;
                    if (!inPump) OutsidePump++;
                    Notices.Add(text);
                };
            }

            public void Pump()
            {
                inPump = true;
                try { Link.Pump(); } finally { inPump = false; }
            }

            // Next packet of type T (in arrival order, after the previous match) that satisfies the predicate.
            public T Await<T>(Func<T, bool> predicate = null, int timeoutMs = WaitMs) where T : class, IPacket
            {
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    Pump();
                    while (cursor < Seen.Count)
                    {
                        T packet = Seen[cursor++] as T;
                        if (packet != null && (predicate == null || predicate(packet))) return packet;
                    }
                    if (watch.ElapsedMilliseconds >= timeoutMs) break;
                    Thread.Sleep(5);
                }
                Assert.Fail("No " + typeof(T).Name + " within " + timeoutMs + " ms. Saw " + Seen.Count + " packets.");
                return null;
            }

            public bool WaitUntil(Func<bool> condition, int timeoutMs = WaitMs)
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < timeoutMs)
                {
                    Pump();
                    if (condition()) return true;
                    Thread.Sleep(5);
                }
                Pump();
                return condition();
            }

            public Thread GameThread()
            {
                FieldInfo serverField = typeof(OfflineLink).GetField("server", BindingFlags.NonPublic | BindingFlags.Instance);
                object server = serverField == null ? null : serverField.GetValue(Link);
                if (server == null) return null;
                FieldInfo threadField = server.GetType().GetField("_thread", BindingFlags.NonPublic | BindingFlags.Instance);
                return threadField == null ? null : threadField.GetValue(server) as Thread;
            }

            public void Dispose()
            {
                try { Link.Dispose(); } catch (Exception) { }
                Link.WaitStopped(2000);
            }
        }

        private static int Bots(S2C_LobbyState lobby) => Count(lobby, true);
        private static int Humans(S2C_LobbyState lobby) => Count(lobby, false);

        private static int Count(S2C_LobbyState lobby, bool bots)
        {
            int count = 0;
            foreach (Slot slot in lobby.Slots) if (slot.PlayerId != 0 && (slot.IsBot != 0) == bots) count++;
            return count;
        }

        // ---- join, lobby, host ------------------------------------------------------------------------------------------

        [Test]
        public void JoinResultComesFirstThenAnOfflineLobbyWhereTheLocalPlayerIsHost()
        {
            int netClients = NetClient.Created;
            using (var probe = new Probe())
            {
                Assert.That(probe.Link.Transport, Is.TypeOf<InMemoryTransport>(), "the offline transport");
                Assert.That(probe.Link.IsOffline, Is.True);
                Assert.That(probe.Link.IsConnected, Is.True);
                Assert.That(probe.Link.RttMilliseconds, Is.EqualTo(-1.0), "RTT is not applicable offline");

                S2C_JoinResult joined = probe.Await<S2C_JoinResult>();
                Assert.That(joined.Result, Is.EqualTo(0), "JoinResult.Result");
                Assert.That(joined.PlayerId, Is.GreaterThan(0));
                Assert.That(probe.Seen[0], Is.InstanceOf<S2C_JoinResult>(), "JoinResult is the first packet");

                S2C_LobbyState lobby = probe.Await<S2C_LobbyState>();
                Assert.That(lobby.IsOffline, Is.EqualTo(1), "LobbyState.IsOffline");
                int hosts = 0;
                Slot local = null;
                foreach (Slot slot in lobby.Slots)
                {
                    if (slot.PlayerId == 0) continue;
                    if (slot.IsHost != 0) { hosts++; Assert.That(slot.IsBot, Is.EqualTo(0), "a bot is never host"); }
                    if (slot.PlayerId == joined.PlayerId) local = slot;
                }
                Assert.That(local, Is.Not.Null, "the local player has a slot");
                Assert.That(local.IsHost, Is.EqualTo(1), "the local player is host");
                Assert.That(hosts, Is.EqualTo(1), "exactly one host");
                Assert.That(Humans(lobby), Is.EqualTo(1), "one human");
                Assert.That(Bots(lobby), Is.EqualTo(3), "the default offline lobby has 3 bots");
            }
            Assert.That(NetClient.Created, Is.EqualTo(netClients), "no NetClient (and so no TcpClient) was constructed");
        }

        // ---- bot count --------------------------------------------------------------------------------------------------

        [Test]
        public void SetBotsOneAndSevenGiveExactlyThoseBotCounts()
        {
            using (var probe = new Probe())
            {
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();

                Assert.That(probe.Link.Send(new C2S_SetBots { Count = 1 }), Is.True);
                S2C_LobbyState one = probe.Await<S2C_LobbyState>(state => Bots(state) == 1);
                Assert.That(Humans(one), Is.EqualTo(1));

                probe.Link.Send(new C2S_SetBots { Count = 7 });
                S2C_LobbyState seven = probe.Await<S2C_LobbyState>(state => Bots(state) == 7);
                Assert.That(Humans(seven), Is.EqualTo(1));
                Assert.That(Bots(seven) + Humans(seven), Is.EqualTo(8), "1v7 fills the eight slots");

                probe.Link.Send(new C2S_SetBots { Count = 1 });
                S2C_LobbyState back = probe.Await<S2C_LobbyState>(state => Bots(state) == 1);
                Assert.That(Humans(back), Is.EqualTo(1));
                Assert.That(probe.Link.IsConnected, Is.True);
            }
        }

        [Test]
        public void SetBotsZeroIsClampedToAtLeastAOneVOneMatch()
        {
            using (var probe = new Probe())
            {
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_SetBots { Count = 0 });
                S2C_LobbyState lobby = probe.Await<S2C_LobbyState>(state => Bots(state) < 3);
                Assert.That(Bots(lobby), Is.EqualTo(1), "0 bots is clamped to 1 (1v1)");
                Assert.That(Humans(lobby), Is.EqualTo(1));
            }
        }

        // Spec correction by the orchestrator (not a weakening): Shared protocol v0.2 is frozen and C2S_SetBots.Read rejects Count > 7
        // at decode, closing the connection. The contract is therefore (a) the client never emits Count > 7 and (b) a raw Count = 9
        // closes the offline session. Server-side clamping of 0 stays covered by SetBotsZeroIsClampedToAtLeastAOneVOneMatch.
        [Test]
        public void TheClientNeverEmitsASetBotsCountAboveSevenAndAPlusAtSevenBotsIsDisabled()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(app, @"new\s+C2S_SetBots\s*\{\s*Count\s*=\s*\(byte\)Mathf\.Clamp\(botCount\s*\+\s*delta,\s*0,\s*7\)\s*\}"),
                Is.True, "the only SetBots the client builds is clamped to 0..7");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(app, @"new\s+C2S_SetBots").Count, Is.EqualTo(1),
                "ClientApp builds exactly one SetBots (the dev path goes through DevCapture, whose value DevArgs limits to 7)");
            string screens = QaSource.StripComments(QaPaths.ReadScript("UI/ClientScreens.cs"));
            Assert.That(screens, Does.Contain("plus.SetEnabled(host && bots + humans < Protocol.MaxPlayers)"),
                "+ bot is disabled once the eight slots are full (1v7)");
            string devArgs = QaSource.StripComments(QaPaths.ReadScript("Dev/DevArgs.cs"));
            Assert.That(devArgs, Does.Contain("bots > 7"), "-ptk-bots above 7 is rejected");
        }

        [Test]
        public void ARawSetBotsCountOfNineClosesTheOfflineSessionPerTheFrozenProtocol()
        {
            using (var probe = new Probe())
            {
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_SetBots { Count = 9 });
                Assert.That(probe.WaitUntil(() => !probe.Link.IsConnected, 3000), Is.True, "the server closes on a malformed SetBots");
                Assert.That(probe.WaitUntil(() => probe.Notices.Count > 0, 3000), Is.True, "and the link reports it through Pump");
            }
        }

        // ---- role, start, world -----------------------------------------------------------------------------------------

        [Test]
        public void KillerRoleStartsAMatchAsKillerAndWorldStateTicksIncrease()
        {
            using (var probe = new Probe())
            {
                S2C_JoinResult joined = probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
                S2C_LobbyState chosen = probe.Await<S2C_LobbyState>(state => state.HostRoleChoice == (byte)RoleChoice.Killer);
                Assert.That(chosen.IsOffline, Is.EqualTo(1));
                probe.Link.Send(new C2S_StartMatch());

                S2C_MatchStart match = probe.Await<S2C_MatchStart>();
                Assert.That(match.Role, Is.EqualTo((byte)Role.Killer), "MatchStart.Role");
                Assert.That(match.SkillId, Is.LessThanOrEqualTo(3), "a killer skill id is 0..3 (GDD section 4)");

                var ticks = new List<uint>();
                while (ticks.Count < 3)
                {
                    S2C_WorldState world = probe.Await<S2C_WorldState>();
                    ticks.Add(world.Tick);
                    bool sawSelf = false;
                    foreach (PlayerState player in world.Players)
                    {
                        if (player.Id == joined.PlayerId) { sawSelf = true; Assert.That(player.Role, Is.EqualTo((byte)Role.Killer)); }
                        if (player.Role == (byte)Role.Survivor)
                            Assert.That(player.Flags & (byte)PlayerFlags.Signal, Is.Not.EqualTo(0),
                                "the killer's state lists a survivor only while it is Signal (Silent entries are omitted)");
                    }
                    Assert.That(sawSelf, Is.True, "the killer sees itself");
                    Assert.That(world.Terminals.Count, Is.GreaterThanOrEqualTo(4));
                }
                for (int i = 1; i < ticks.Count; i++)
                    Assert.That(ticks[i], Is.GreaterThan(ticks[i - 1]), "WorldState.Tick increases (" + ticks[i - 1] + " then " + ticks[i] + ")");
            }
        }

        [Test]
        // U2e (GDD section 2, 10/30): replaces U2b rule 10 ("the survivor never receives the killer's position").
        // A survivor-bound S2C_WorldState contains the Killer entry only while that survivor's line of sight to the Killer is clear.
        public void SurvivorRoleStartsAMatchAsSurvivorAndSeesTheKillerOnlyInLineOfSight()
        {
            using (var probe = new Probe())
            {
                S2C_JoinResult joined = probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
                probe.Await<S2C_LobbyState>(state => state.HostRoleChoice == (byte)RoleChoice.Survivor);
                probe.Link.Send(new C2S_StartMatch());

                S2C_MatchStart match = probe.Await<S2C_MatchStart>();
                Assert.That(match.Role, Is.EqualTo((byte)Role.Survivor));
                Assert.That(match.SkillId, Is.LessThanOrEqualTo(4), "a survivor skill id is 0..4 (GDD section 4)");

                FixedMap map = SharedMap.Map;
                PlayerState selfEntry = default;
                PlayerState killerEntry = default;
                int killerCount = 0;

                for (int i = 0; i < 6; i++)
                {
                    S2C_WorldState world = probe.Await<S2C_WorldState>();
                    bool sawSelf = false;
                    foreach (PlayerState player in world.Players)
                    {
                        if (player.Role == (byte)Role.Killer)
                        {
                            killerCount++;
                            killerEntry = player;
                        }
                        else
                        {
                            Assert.That(player.Role, Is.EqualTo((byte)Role.Survivor), "every non-killer entry is a survivor");
                        }
                        if (player.Id == joined.PlayerId)
                        {
                            sawSelf = true;
                            selfEntry = player;
                        }
                    }
                    Assert.That(sawSelf, Is.True, "a survivor sees itself and the team");
                    Assert.That(world.ThreatLevel, Is.LessThanOrEqualTo(3));
                    Assert.That(killerCount, Is.LessThanOrEqualTo(1), "at most one killer entry per survivor world");

                    if (killerCount == 1)
                    {
                        Assert.That(killerEntry.Flags, Is.EqualTo(0), "killer entry Flags == 0");
                        Assert.That(killerEntry.StatusTimer, Is.EqualTo(0f), "killer entry StatusTimer == 0");
                        Assert.That(killerEntry.CaptureCount, Is.EqualTo(0), "killer entry CaptureCount == 0");

                        // Line of sight check (U2e): when a Killer entry is present, the segment from the receiving
                        // survivor's own position to the Killer entry must not be blocked by a static wall.
                        // Tolerance: walls are shrunk by 0.05 m on each side to absorb the server's ambiguity band
                        // and corner-case floating point differences.
                        const float tolerance = 0.05f;
                        bool blocked = false;
                        foreach (Wall wall in map.Walls)
                        {
                            float minX = wall.MinX + tolerance;
                            float maxX = wall.MaxX - tolerance;
                            float minZ = wall.MinZ + tolerance;
                            float maxZ = wall.MaxZ - tolerance;
                            if (minX < maxX && minZ < maxZ &&
                                MapGeometry.SegmentHitsBox(selfEntry.X, selfEntry.Z, killerEntry.X, killerEntry.Z, minX, minZ, maxX, maxZ, out _))
                            {
                                blocked = true;
                                break;
                            }
                        }

                        Assert.That(blocked, Is.False, "killer entry present only when line of sight is clear (walls shrunk by 0.05 m tolerance)");
                    }

                    killerCount = 0;
                    killerEntry = default;
                    selfEntry = default;
                }
            }
        }

        [Test]
        public void RandomRoleStartsAMatchWithExactlyOneKillerAmongTheTeam()
        {
            using (var probe = new Probe())
            {
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Random });
                probe.Await<S2C_LobbyState>(state => state.HostRoleChoice == (byte)RoleChoice.Random);
                probe.Link.Send(new C2S_StartMatch());
                S2C_MatchStart match = probe.Await<S2C_MatchStart>();
                Assert.That(match.Role == (byte)Role.Killer || match.Role == (byte)Role.Survivor, Is.True);
                probe.Await<S2C_WorldState>();
            }
        }

        // ---- threading --------------------------------------------------------------------------------------------------

        [Test]
        public void PacketsAreHeldBackUntilPumpAndDispatchedOnlyOnTheCallingThread()
        {
            using (var probe = new Probe())
            {
                Thread.Sleep(400); // the game thread has long since answered the Join
                Assert.That(probe.Seen, Is.Empty, "nothing may be dispatched without Pump");
                Assert.That(probe.OutsidePump, Is.EqualTo(0));
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_StartMatch());
                probe.Await<S2C_MatchStart>();
                Thread.Sleep(300); // world states pile up on the game thread
                int before = probe.Seen.Count;
                Assert.That(before, Is.GreaterThan(0));
                Thread.Sleep(300);
                Assert.That(probe.Seen.Count, Is.EqualTo(before), "no dispatch between Pump calls");
                probe.Pump();
                Assert.That(probe.Seen.Count, Is.GreaterThan(before), "Pump delivers the queued world states");
                Assert.That(probe.OffThread, Is.EqualTo(0), "every event fired on the thread that called Pump");
                Assert.That(probe.OutsidePump, Is.EqualTo(0), "every event fired inside Pump");
            }
        }

        [Test]
        public void ArrivalTimeIsStampedWhenThePacketArrivesNotWhenItIsPumped()
        {
            using (var probe = new Probe())
            {
                Thread.Sleep(300);
                double beforePump = LinkClock.Now;
                var times = new List<double>();
                probe.Link.PacketReceived += received => times.Add(received.Time);
                probe.Await<S2C_LobbyState>();
                Assert.That(times.Count, Is.GreaterThan(0));
                Assert.That(times[0], Is.LessThan(beforePump), "ReceivedPacket.Time predates the Pump call that dispatched it");
            }
        }

        [Test]
        public void DisposeReturnsAtOnceDeliversDisconnectedOnlyThroughPumpAndEndsTheGameThread()
        {
            var probe = new Probe();
            try
            {
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                Thread gameThread = probe.GameThread();
                if (gameThread != null)
                {
                    Assert.That(gameThread.IsAlive, Is.True, "the game-loop thread runs while the link is open");
                    Assert.That(gameThread.IsBackground, Is.True, "the game-loop thread is a background thread");
                }

                var watch = Stopwatch.StartNew();
                probe.Link.Dispose();
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500), "Dispose must not wait for the game thread");
                Assert.That(probe.Link.IsConnected, Is.False);
                Thread.Sleep(200);
                Assert.That(probe.Notices, Is.Empty, "Disconnected must wait for Pump");
                Assert.That(probe.OutsidePump, Is.EqualTo(0));

                Assert.That(probe.WaitUntil(() => probe.Notices.Count > 0), Is.True, "Disconnected arrives through Pump");
                Assert.That(probe.Notices.Count, Is.EqualTo(1));
                Assert.That(probe.Notices[0], Is.Not.Null.And.Not.Empty);
                Assert.That(probe.OffThread, Is.EqualTo(0));

                Assert.That(probe.Link.WaitStopped(2000), Is.True, "the stop helper finished within 2 s");
                if (gameThread != null) Assert.That(gameThread.Join(2000), Is.True, "the game-loop thread ended within 2 s");
                Assert.That(probe.Link.Send(new C2S_StartMatch()), Is.False, "Send after Dispose reports failure");
                probe.Pump();
                Assert.That(probe.Notices.Count, Is.EqualTo(1), "reported once");
            }
            finally { probe.Dispose(); }
        }

        [Test]
        public void DisposeDuringAMatchStillEndsTheGameThreadAndLeavesNoRunningWorldTraffic()
        {
            var probe = new Probe();
            try
            {
                probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_StartMatch());
                probe.Await<S2C_MatchStart>();
                probe.Await<S2C_WorldState>();
                Thread gameThread = probe.GameThread();

                var watch = Stopwatch.StartNew();
                probe.Link.Dispose();
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500), "quitting in the middle of a match never blocks the caller");
                Assert.That(probe.Link.WaitStopped(2000), Is.True);
                if (gameThread != null) Assert.That(gameThread.Join(2000), Is.True, "the game-loop thread ended within 2 s");
                Assert.That(probe.WaitUntil(() => probe.Notices.Count > 0), Is.True);
            }
            finally { probe.Dispose(); }
        }

        [Test]
        public void DisposeTwiceIsHarmlessAndReportsOnce()
        {
            var probe = new Probe();
            try
            {
                probe.Await<S2C_JoinResult>();
                probe.Link.Dispose();
                probe.Link.Dispose();
                Assert.That(probe.WaitUntil(() => probe.Notices.Count > 0), Is.True);
                probe.Pump();
                probe.Pump();
                Assert.That(probe.Notices.Count, Is.EqualTo(1));
                Assert.That(probe.Link.WaitStopped(2000), Is.True);
            }
            finally { probe.Dispose(); }
        }

        [Test]
        public void ADisposeRightAfterConstructionStillStopsTheServerThread()
        {
            var probe = new Probe();
            try
            {
                Thread gameThread = probe.GameThread();
                probe.Link.Dispose();
                Assert.That(probe.Link.WaitStopped(2000), Is.True, "stop helper finished");
                if (gameThread != null) Assert.That(gameThread.Join(2000), Is.True, "game-loop thread ended within 2 s");
            }
            finally { probe.Dispose(); }
        }

        // Leave, a second Offline and Join after Offline each replace the link: the old one must end.
        [Test]
        public void SuccessiveLinksEachEndTheirOwnGameThread()
        {
            var threads = new List<Thread>();
            int netClients = NetClient.Created;
            for (int round = 0; round < 4; round++)
            {
                var probe = new Probe();
                try
                {
                    probe.Await<S2C_JoinResult>();
                    probe.Await<S2C_LobbyState>();
                    Thread gameThread = probe.GameThread();
                    if (gameThread != null) threads.Add(gameThread);
                    probe.Link.Dispose();
                    Assert.That(probe.Link.WaitStopped(2000), Is.True, "round " + round);
                }
                finally { probe.Dispose(); }
            }
            foreach (Thread thread in threads) Assert.That(thread.Join(2000), Is.True, "a game-loop thread outlived its link");
            Assert.That(NetClient.Created, Is.EqualTo(netClients));
        }

        [Test]
        public void TwoOfflineLinksCanRunAtOnceWithoutCrossTalk()
        {
            using (var first = new Probe(1))
            using (var second = new Probe(5))
            {
                first.Await<S2C_JoinResult>();
                second.Await<S2C_JoinResult>();
                S2C_LobbyState a = first.Await<S2C_LobbyState>();
                S2C_LobbyState b = second.Await<S2C_LobbyState>();
                Assert.That(Bots(a), Is.EqualTo(1));
                Assert.That(Bots(b), Is.EqualTo(5));
            }
        }
    }
}
