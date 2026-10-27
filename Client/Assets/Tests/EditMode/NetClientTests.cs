using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Tests
{
    public sealed class NetClientTests
    {
        [Test]
        public void WorkersSendJoinPingAndMovementWhilePumpDeliversSplitPacketsOnCallerThread()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new NetClient();
                int caller = Thread.CurrentThread.ManagedThreadId, deliveredOn = -1;
                S2C_JoinResult received = null;
                client.PacketReceived += item =>
                {
                    if (item.Packet is S2C_JoinResult joined) { received = joined; deliveredOn = Thread.CurrentThread.ManagedThreadId; }
                };
                client.Connect((IPEndPoint)listener.LocalEndpoint);
                Assert.That(SpinWait.SpinUntil(() => listener.Pending(), 3000), Is.True, "Background connect timed out.");
                using TcpClient peer = listener.AcceptTcpClient();
                NetworkStream stream = peer.GetStream(); stream.ReadTimeout = 3000;
                Assert.That(ReadPacket(stream), Is.TypeOf<C2S_Join>());
                var ping = ReadPacket(stream) as C2S_Ping;
                Assert.That(ping, Is.Not.Null);
                byte[] reply = PacketCodec.Encode(new S2C_JoinResult { PlayerId = 42 });
                foreach (byte value in reply) stream.WriteByte(value);
                byte[] pong = PacketCodec.Encode(new S2C_Pong { Timestamp = ping.Timestamp });
                stream.Write(pong, 0, pong.Length);
                PumpUntil(client, () => received != null && client.RttMilliseconds >= 0);
                Assert.That(received.PlayerId, Is.EqualTo(42));
                Assert.That(deliveredOn, Is.EqualTo(caller));
                Assert.That(client.RttMilliseconds, Is.GreaterThanOrEqualTo(0));
                Assert.That(client.Send(new C2S_MoveInput { DirX = 1, Seq = 9 }), Is.True);
                IPacket packet;
                do { packet = ReadPacket(stream); } while (packet is C2S_Ping);
                Assert.That(packet, Is.TypeOf<C2S_MoveInput>());
                Assert.That(((C2S_MoveInput)packet).Seq, Is.EqualTo(9));
                // A second heartbeat proves this is periodic, not just a connection handshake.
                Assert.That(ReadPacket(stream), Is.TypeOf<C2S_Ping>());
                Assert.That(((C2S_Ping)ReadPacket(stream)).Timestamp, Is.GreaterThan(ping.Timestamp));
            }
            finally { listener.Stop(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClosedSocketOrMalformedFrameDisconnectsOnlyThisClient(bool malformed)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try
            {
                using var client = new NetClient();
                string notice = null; client.Disconnected += value => notice = value;
                client.Connect((IPEndPoint)listener.LocalEndpoint);
                Assert.That(SpinWait.SpinUntil(() => listener.Pending(), 3000), Is.True);
                using TcpClient peer = listener.AcceptTcpClient();
                if (malformed)
                {
                    byte[] invalid = { 1, 0, 0, 0 }; peer.GetStream().Write(invalid, 0, invalid.Length);
                }
                else peer.Close();
                PumpUntil(client, () => notice != null);
                Assert.That(client.IsConnected, Is.False);
                Assert.That(notice, Is.Not.Empty);
            }
            finally { listener.Stop(); }
        }

        private static void PumpUntil(NetClient client, Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.ElapsedMilliseconds < 4000) { client.Pump(); Thread.Sleep(2); }
            Assert.That(condition(), Is.True, "Main-thread queue did not receive the expected event.");
        }
        private static IPacket ReadPacket(NetworkStream stream)
        {
            byte[] header = ReadExactly(stream, 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            var assembler = new PacketAssembler(); assembler.Append(header); assembler.Append(ReadExactly(stream, length));
            Assert.That(assembler.TryRead(out ushort id, out var payload), Is.EqualTo(AssembleResult.Packet));
            return PacketCodec.Decode(id, payload.Span);
        }
        private static byte[] ReadExactly(NetworkStream stream, int length)
        {
            var data = new byte[length]; int offset = 0;
            while (offset < length)
            {
                int count = stream.Read(data, offset, length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
            return data;
        }
    }
}
