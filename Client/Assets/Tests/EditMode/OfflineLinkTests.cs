using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Tests
{
    // End to end against the real GameServer thread through InMemoryTransport. Every wait is bounded (<= 5 s).
    public sealed class OfflineLinkTests
    {
        private const int Timeout = 5000;
        private OfflineLink link;
        private readonly List<IPacket> packets = new List<IPacket>();
        private string notice;
        private int caller, wrongThread;
        private bool pumping;
        private int outsidePump, cursor;

        [SetUp]
        public void SetUp()
        {
            packets.Clear(); cursor = 0; notice = null; wrongThread = 0; outsidePump = 0;
            caller = Thread.CurrentThread.ManagedThreadId;
        }

        [TearDown]
        public void TearDown()
        {
            if (link == null) return;
            link.Dispose();
            link.WaitStopped(2000);
            link = null;
        }

        private void Open(int bots = OfflineLink.DefaultBots)
        {
            link = new OfflineLink(bots);
            link.PacketReceived += received =>
            {
                if (Thread.CurrentThread.ManagedThreadId != caller) wrongThread++;
                if (!pumping) outsidePump++;
                packets.Add(received.Packet);
            };
            link.Disconnected += text => notice = text;
        }

        private void PumpOnce()
        {
            pumping = true;
            try { link.Pump(); } finally { pumping = false; }
        }

        // Consumes packets in arrival order until one of type T matches; returns it.
        private T Next<T>(Func<T, bool> condition = null) where T : class, IPacket
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < Timeout)
            {
                PumpOnce();
                while (cursor < packets.Count)
                    if (packets[cursor++] is T packet && (condition == null || condition(packet))) return packet;
                Thread.Sleep(5);
            }
            Assert.Fail("No " + typeof(T).Name + " within " + Timeout + " ms.");
            return null;
        }

        private static int Count(S2C_LobbyState lobby, bool bots)
        {
            int count = 0;
            foreach (Slot slot in lobby.Slots) if (slot.PlayerId != 0 && (slot.IsBot != 0) == bots) count++;
            return count;
        }

        [Test]
        public void OfflineLinkJoinsConfiguresStartsAndStopsTheInProcessServerWithoutASocket()
        {
            int netClients = NetClient.Created;
            Open();
            Assert.That(link.Transport, Is.TypeOf<InMemoryTransport>());
            Assert.That(link.IsOffline, Is.True);
            Assert.That(link.RttMilliseconds, Is.LessThan(0));

            // 1. JoinResult
            S2C_JoinResult joined = Next<S2C_JoinResult>();
            Assert.That(joined.Result, Is.EqualTo(0));
            Assert.That(joined.PlayerId, Is.GreaterThan(0));

            // 2. Offline lobby; the local player is host.
            S2C_LobbyState lobby = Next<S2C_LobbyState>();
            Assert.That(lobby.IsOffline, Is.EqualTo(1));
            Slot local = Array.Find(lobby.Slots, slot => slot.PlayerId == joined.PlayerId);
            Assert.That(local, Is.Not.Null);
            Assert.That(local.IsHost, Is.EqualTo(1));
            Assert.That(Count(lobby, true), Is.EqualTo(OfflineLink.DefaultBots));

            // 3. Bots between 1 and 7.
            Assert.That(link.Send(new C2S_SetBots { Count = 7 }), Is.True);
            Next<S2C_LobbyState>(state => Count(state, true) == 7);
            link.Send(new C2S_SetBots { Count = 1 });
            S2C_LobbyState one = Next<S2C_LobbyState>(state => Count(state, true) == 1);
            Assert.That(Count(one, false), Is.EqualTo(1));

            // 4. Role, start, world.
            link.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            Next<S2C_LobbyState>(state => state.HostRoleChoice == (byte)RoleChoice.Killer);
            link.Send(new C2S_StartMatch());
            S2C_MatchStart match = Next<S2C_MatchStart>();
            Assert.That(match.Role, Is.EqualTo((byte)Role.Killer));
            S2C_WorldState world = Next<S2C_WorldState>();
            Assert.That(world.Players.Exists(player => player.Id == joined.PlayerId), Is.True, "the killer sees itself");

            Assert.That(wrongThread, Is.EqualTo(0), "PacketReceived fired off the Pump thread");
            Assert.That(outsidePump, Is.EqualTo(0), "PacketReceived fired outside Pump");

            // 5. Dispose: Disconnected arrives through Pump; the game-loop thread stops within 2 s.
            var watch = Stopwatch.StartNew();
            link.Dispose();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(300), "Dispose must not wait for the game thread");
            Assert.That(notice, Is.Null, "Disconnected must wait for Pump");
            Assert.That(link.IsConnected, Is.False);
            watch.Restart();
            while (notice == null && watch.ElapsedMilliseconds < Timeout) { PumpOnce(); Thread.Sleep(5); }
            Assert.That(notice, Is.Not.Null.And.Not.Empty);
            Assert.That(link.WaitStopped(2000), Is.True, "game-loop thread still running 2 s after Dispose");
            Assert.That(link.Send(new C2S_StartMatch()), Is.False, "Send after Dispose must fail");

            Assert.That(NetClient.Created, Is.EqualTo(netClients), "the offline path constructed a NetClient");
        }

        [Test]
        public void PacketsWaitForPumpAndKeepArrivalTimes()
        {
            Open();
            Thread.Sleep(300); // the game thread has answered Join by now
            Assert.That(packets, Is.Empty, "nothing may be dispatched without Pump");
            double before = LinkClock.Now;
            var times = new List<double>();
            link.PacketReceived += received => times.Add(received.Time);
            Next<S2C_LobbyState>();
            Assert.That(times.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(times[0], Is.LessThan(before), "Time is the arrival time, not the Pump time");
        }

        [Test]
        public void DisposeTwiceIsHarmlessAndReportsOnce()
        {
            Open();
            Next<S2C_JoinResult>();
            int raised = 0;
            link.Disconnected += _ => raised++;
            link.Dispose();
            link.Dispose();
            var watch = Stopwatch.StartNew();
            while (raised == 0 && watch.ElapsedMilliseconds < Timeout) { PumpOnce(); Thread.Sleep(5); }
            PumpOnce(); PumpOnce();
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(link.WaitStopped(2000), Is.True);
        }
    }
}
