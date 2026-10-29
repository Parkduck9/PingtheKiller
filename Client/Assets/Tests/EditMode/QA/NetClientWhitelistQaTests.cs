using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // U4d QA attempt 1, test item 3. NetClient accepts a server packet only if its id is on a whitelist; v0.3 added S2C_SelfState and
    // the whitelist missed it (disconnect online, invisible offline). These tests enumerate PacketId by name prefix, so a future
    // S2C_ id added to Shared without a whitelist update fails here, and a C2S_ id can never become acceptable by accident.
    [Category("U4dQaA1")]
    public sealed class NetClientWhitelistQaTests
    {
        private static IEnumerable<PacketId> All() => (PacketId[])Enum.GetValues(typeof(PacketId));

        [Test]
        public void EveryS2CPacketIdIsAcceptedFromTheServer()
        {
            int seen = 0;
            foreach (PacketId id in All())
            {
                if (!id.ToString().StartsWith("S2C_", StringComparison.Ordinal)) continue;
                seen++;
                Assert.That(NetClient.IsServerPacket(id), Is.True, id + " is a server packet and must not disconnect the client");
            }
            Assert.That(seen, Is.GreaterThanOrEqualTo(8), "the enumeration found the S2C_ ids (v0.3 has 8)");
        }

        [Test]
        public void EveryC2SPacketIdIsRejectedWhenItComesFromTheServer()
        {
            int seen = 0;
            foreach (PacketId id in All())
            {
                if (!id.ToString().StartsWith("C2S_", StringComparison.Ordinal)) continue;
                seen++;
                Assert.That(NetClient.IsServerPacket(id), Is.False, id + " is a client packet and must be refused from the server");
            }
            Assert.That(seen, Is.GreaterThanOrEqualTo(7));
        }

        [Test]
        public void NoPacketIdIsOutsideTheTwoPrefixesAndUndefinedIdsAreRefused()
        {
            foreach (PacketId id in All())
                Assert.That(id.ToString().StartsWith("S2C_") || id.ToString().StartsWith("C2S_"), Is.True, id + " needs a direction prefix");
            Assert.That(NetClient.IsServerPacket((PacketId)0), Is.False);
            Assert.That(NetClient.IsServerPacket((PacketId)0x7FFF), Is.False);
            Assert.That(NetClient.IsServerPacket((PacketId)0xFFFF), Is.False);
        }

        [Test]
        public void EveryS2CPacketTypeCrossesARealSocketAndTheLinkStaysUp()
        {
            Assembly shared = typeof(S2C_SelfState).Assembly;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new NetClient();
                string notice = null;
                client.Disconnected += value => notice = value;
                var received = new List<PacketId>();
                client.PacketReceived += item => received.Add(item.Packet.Id);
                client.Connect((IPEndPoint)listener.LocalEndpoint);
                Assert.That(SpinWait.SpinUntil(() => listener.Pending(), 3000), Is.True, "the client connected");
                using TcpClient peer = listener.AcceptTcpClient();
                NetworkStream stream = peer.GetStream();
                var sent = new List<PacketId>();
                foreach (PacketId id in All())
                {
                    if (!id.ToString().StartsWith("S2C_", StringComparison.Ordinal)) continue;
                    Type type = shared.GetType("PingTheKiller.Shared.Packets." + id);
                    Assert.That(type, Is.Not.Null, "a packet class named like the id: " + id);
                    var packet = (IPacket)Activator.CreateInstance(type);
                    byte[] frame = PacketCodec.Encode(packet);
                    stream.Write(frame, 0, frame.Length);
                    sent.Add(id);
                }
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 4000 && notice == null && received.Count < sent.Count)
                {
                    client.Pump();
                    Thread.Sleep(5);
                }
                Assert.That(notice, Is.Null, "the client disconnected: " + notice);
                Assert.That(received, Is.EquivalentTo(sent), "every S2C packet reached the pump");
                Assert.That(client.IsConnected, Is.True);
            }
            finally { listener.Stop(); }
        }

        [Test]
        public void AClientPacketFromTheServerStillDisconnectsTheClient()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new NetClient();
                string notice = null;
                client.Disconnected += value => notice = value;
                client.Connect((IPEndPoint)listener.LocalEndpoint);
                Assert.That(SpinWait.SpinUntil(() => listener.Pending(), 3000), Is.True);
                using TcpClient peer = listener.AcceptTcpClient();
                byte[] frame = PacketCodec.Encode(new C2S_Ping { Timestamp = 1 });
                peer.GetStream().Write(frame, 0, frame.Length);
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 4000 && notice == null) { client.Pump(); Thread.Sleep(5); }
                Assert.That(notice, Is.Not.Null, "a C2S_ packet from the server is a protocol violation");
                Assert.That(client.IsConnected, Is.False);
            }
            finally { listener.Stop(); }
        }
    }
}
