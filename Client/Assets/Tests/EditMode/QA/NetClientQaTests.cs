using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // A loopback TcpListener plays the server. Everything is observed through NetClient's public surface:
    // Connect / Send / Pump / PacketReceived / Disconnected / IsConnected / RttMilliseconds.
    public sealed class NetClientQaTests
    {
        private const int Timeout = 4000;

        private sealed class FakeServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private TcpClient peer;
            public NetworkStream Stream;

            public FakeServer() { listener.Start(); }
            public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;

            public void Accept()
            {
                if (!SpinWait.SpinUntil(() => listener.Pending(), Timeout))
                    Assert.Fail("The client never connected to the fake server.");
                peer = listener.AcceptTcpClient();
                peer.NoDelay = true;
                Stream = peer.GetStream();
                Stream.ReadTimeout = Timeout;
            }

            public void Send(IPacket packet)
            {
                byte[] frame = PacketCodec.Encode(packet);
                Stream.Write(frame, 0, frame.Length);
            }

            public void SendRaw(byte[] bytes) { Stream.Write(bytes, 0, bytes.Length); }

            public IPacket Read()
            {
                byte[] header = ReadExactly(4);
                int length = BitConverter.ToInt32(header, 0);
                byte[] body = ReadExactly(length);
                ushort id = BitConverter.ToUInt16(body, 0);
                var payload = new byte[length - 2];
                Array.Copy(body, 2, payload, 0, payload.Length);
                return PacketCodec.Decode(id, payload);
            }

            private byte[] ReadExactly(int count)
            {
                var buffer = new byte[count];
                int done = 0;
                while (done < count)
                {
                    int read = Stream.Read(buffer, done, count - done);
                    if (read == 0) throw new InvalidOperationException("Client closed the connection.");
                    done += read;
                }
                return buffer;
            }

            public void CloseConnection() { peer?.Close(); }

            public void Dispose()
            {
                try { peer?.Close(); } catch (Exception) { }
                try { listener.Stop(); } catch (Exception) { }
            }
        }

        // True only while the test thread is inside NetClient.Pump.
        private static volatile bool pumping;

        private static void PumpOnce(NetClient client)
        {
            pumping = true;
            try { client.Pump(); }
            finally { pumping = false; }
        }

        private static bool PumpUntil(NetClient client, Func<bool> condition, int timeoutMs = Timeout)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                PumpOnce(client);
                if (condition()) return true;
                Thread.Sleep(5);
            }
            PumpOnce(client);
            return condition();
        }

        [Test]
        public void SendBeforeConnectIsRejectedWithoutThrowing()
        {
            using (var client = new NetClient())
            {
                Assert.That(client.IsConnected, Is.False);
                Assert.That(client.Send(new C2S_StartMatch()), Is.False);
            }
        }

        [Test]
        public void ConnectPumpAndDisposeNeverBlockTheCallingThread()
        {
            // 10.255.255.1 is normally unroutable, so the connect stays pending on the worker thread.
            var client = new NetClient();
            var watch = Stopwatch.StartNew();
            client.Connect(new IPEndPoint(IPAddress.Parse("10.255.255.1"), 7777));
            long connectMs = watch.ElapsedMilliseconds;
            watch.Restart();
            client.Pump();
            long pumpMs = watch.ElapsedMilliseconds;
            watch.Restart();
            client.Dispose();
            long disposeMs = watch.ElapsedMilliseconds;

            Assert.That(connectMs, Is.LessThan(300), "Connect must return immediately.");
            Assert.That(pumpMs, Is.LessThan(100), "Pump must not wait on the socket.");
            Assert.That(disposeMs, Is.LessThan(300), "Dispose must not wait on the socket.");
        }

        [Test]
        public void PacketsFromTheBackgroundThreadAreOnlyDispatchedInsidePumpOnTheCallingThread()
        {
            using (var server = new FakeServer())
            using (var client = new NetClient())
            {
                int testThread = Thread.CurrentThread.ManagedThreadId;
                int dispatchedOutsidePump = 0;
                var wrongThread = new List<int>();
                var order = new List<int>();

                client.PacketReceived += received =>
                {
                    if (!pumping) dispatchedOutsidePump++;
                    if (Thread.CurrentThread.ManagedThreadId != testThread) wrongThread.Add(Thread.CurrentThread.ManagedThreadId);
                    if (received.Packet is S2C_JoinResult joined) order.Add(joined.PlayerId);
                };

                client.Connect(server.Endpoint);
                server.Accept();
                const int count = 50;
                for (int i = 1; i <= count; i++) server.Send(new S2C_JoinResult { PlayerId = i });

                // Give the receive thread ample time to decode and enqueue. Without a Pump nothing may surface.
                Thread.Sleep(500);
                Assert.That(order.Count, Is.EqualTo(0), "Packets were dispatched without Pump being called.");
                Assert.That(dispatchedOutsidePump, Is.EqualTo(0));

                bool done = PumpUntil(client, () => order.Count >= count);
                Assert.That(done, Is.True, "Only " + order.Count + " of " + count + " packets were delivered by Pump.");
                Assert.That(order.Count, Is.EqualTo(count));
                for (int i = 0; i < count; i++) Assert.That(order[i], Is.EqualTo(i + 1), "Delivery order broken at index " + i);
                Assert.That(wrongThread, Is.Empty, "PacketReceived fired on a thread other than the Pump caller.");
                Assert.That(dispatchedOutsidePump, Is.EqualTo(0));
            }
        }

        [Test]
        public void FrameSplitAcrossReadsIsDeliveredOnceAndCoalescedFramesKeepOrder()
        {
            using (var server = new FakeServer())
            using (var client = new NetClient())
            {
                var order = new List<int>();
                client.PacketReceived += received =>
                {
                    if (received.Packet is S2C_JoinResult joined) order.Add(joined.PlayerId);
                };
                client.Connect(server.Endpoint);
                server.Accept();

                byte[] first = PacketCodec.Encode(new S2C_JoinResult { PlayerId = 11 });
                server.SendRaw(new[] { first[0], first[1], first[2] });
                Thread.Sleep(200);
                client.Pump();
                Assert.That(order, Is.Empty, "A partial frame must not be dispatched.");

                var rest = new byte[first.Length - 3];
                Array.Copy(first, 3, rest, 0, rest.Length);
                byte[] second = PacketCodec.Encode(new S2C_JoinResult { PlayerId = 12 });
                byte[] third = PacketCodec.Encode(new S2C_JoinResult { PlayerId = 13 });
                var joined3 = new byte[rest.Length + second.Length + third.Length];
                Array.Copy(rest, 0, joined3, 0, rest.Length);
                Array.Copy(second, 0, joined3, rest.Length, second.Length);
                Array.Copy(third, 0, joined3, rest.Length + second.Length, third.Length);
                server.SendRaw(joined3);

                Assert.That(PumpUntil(client, () => order.Count >= 3), Is.True);
                Thread.Sleep(100);
                client.Pump();
                Assert.That(order, Is.EqualTo(new[] { 11, 12, 13 }));
            }
        }

        [Test]
        public void ServerClosingIsReportedOnlyThroughPumpOnTheCallingThreadAndOnlyOnce()
        {
            using (var server = new FakeServer())
            using (var client = new NetClient())
            {
                int testThread = Thread.CurrentThread.ManagedThreadId;
                int raised = 0, wrongThread = 0;
                string notice = null;
                client.Disconnected += text =>
                {
                    raised++;
                    notice = text;
                    if (Thread.CurrentThread.ManagedThreadId != testThread) wrongThread++;
                };
                client.Connect(server.Endpoint);
                server.Accept();
                Assert.That(SpinWait.SpinUntil(() => client.IsConnected, Timeout), Is.True);

                server.CloseConnection();
                Assert.That(SpinWait.SpinUntil(() => !client.IsConnected, Timeout), Is.True, "Client never noticed the closed connection.");
                Thread.Sleep(200);
                Assert.That(raised, Is.EqualTo(0), "Disconnected fired without Pump being called.");

                Assert.That(PumpUntil(client, () => raised > 0), Is.True);
                client.Pump();
                client.Pump();
                Assert.That(raised, Is.EqualTo(1));
                Assert.That(wrongThread, Is.EqualTo(0));
                Assert.That(notice, Is.Not.Null.And.Not.Empty);
                Assert.That(client.Send(new C2S_StartMatch()), Is.False, "Send after a disconnect must fail, not queue.");
            }
        }

        [Test]
        public void UnreachableServerNoticeNamesTheEndpointAndTellsThePlayerWhatToCheck()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            using (var client = new NetClient())
            {
                string notice = null;
                client.Disconnected += text => notice = text;
                client.Connect(new IPEndPoint(IPAddress.Loopback, port));
                Assert.That(PumpUntil(client, () => notice != null, 9000), Is.True, "No Disconnected notice for a refused connection.");
                Assert.That(notice, Does.Contain("127.0.0.1:" + port), "Notice must name the address (SLOP section 4). Got: " + notice);
                bool saysWhatToCheck = notice.IndexOf("host", StringComparison.OrdinalIgnoreCase) >= 0
                    || notice.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0;
                Assert.That(saysWhatToCheck, Is.True, "Notice must say what to check. Got: " + notice);
                Assert.That(notice, Does.Not.Contain("went wrong").IgnoreCase);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-5)]
        [TestCase(16385)]
        [TestCase(int.MaxValue)]
        public void FrameLengthOutsideTwoToSixteenThousandThreeEightyFourDisconnects(int length)
        {
            using (var server = new FakeServer())
            using (var client = new NetClient())
            {
                int packets = 0;
                string notice = null;
                client.PacketReceived += _ => packets++;
                client.Disconnected += text => notice = text;
                client.Connect(server.Endpoint);
                server.Accept();

                server.SendRaw(BitConverter.GetBytes(length));
                Assert.That(PumpUntil(client, () => notice != null), Is.True, "Client did not disconnect on length " + length);
                Assert.That(packets, Is.EqualTo(0));
                Assert.That(client.IsConnected, Is.False);
            }
        }

        [Test]
        public void ClientSendsPingAboutEverySecondAndPongProducesRtt()
        {
            using (var server = new FakeServer())
            using (var client = new NetClient())
            {
                client.Connect(server.Endpoint);
                server.Accept();
                Assert.That(client.RttMilliseconds, Is.LessThan(0), "RTT must read 'unknown' before the first pong.");

                var arrivals = new List<long>();
                var watch = Stopwatch.StartNew();
                bool sawJoin = false;
                while (arrivals.Count < 2)
                {
                    IPacket packet = server.Read();
                    if (packet is C2S_Join) sawJoin = true;
                    if (packet is C2S_Ping ping)
                    {
                        arrivals.Add(watch.ElapsedMilliseconds);
                        if (arrivals.Count == 1)
                        {
                            server.Send(new S2C_Pong { Timestamp = ping.Timestamp });
                            Assert.That(PumpUntil(client, () => client.RttMilliseconds >= 0, 2000), Is.True, "Pong did not update RttMilliseconds.");
                            Assert.That(client.RttMilliseconds, Is.LessThan(1000));
                        }
                    }
                }
                Assert.That(sawJoin, Is.True, "The client must introduce itself with C2S_Join.");
                long interval = arrivals[1] - arrivals[0];
                Assert.That(interval, Is.InRange(700L, 1600L), "Ping interval was " + interval + " ms; the brief says 1 s.");
            }
        }

        [Test]
        public void SendFromFourThreadsConcurrentlyLosesNothingAndKeepsPerThreadOrder()
        {
            // The incoming queue has a single producer (the receive thread) and is private; the multi-producer
            // queue reachable through the public surface is the outgoing one, so that is what is stressed here.
            const int threads = 4, perThread = 50;
            using (var server = new FakeServer())
            using (var client = new NetClient())
            {
                client.Connect(server.Endpoint);
                server.Accept();
                Assert.That(SpinWait.SpinUntil(() => client.IsConnected, Timeout), Is.True);

                var start = new ManualResetEventSlim(false);
                var failures = new int[threads];
                var workers = new List<Thread>();
                for (int t = 0; t < threads; t++)
                {
                    int id = t;
                    var worker = new Thread(() =>
                    {
                        start.Wait();
                        for (int i = 0; i < perThread; i++)
                            if (!client.Send(new C2S_MoveInput { DirX = 1, Seq = (uint)(id * 1000 + i) })) failures[id]++;
                    }) { IsBackground = true };
                    workers.Add(worker);
                    worker.Start();
                }
                start.Set();
                foreach (Thread worker in workers) Assert.That(worker.Join(Timeout), Is.True);
                for (int t = 0; t < threads; t++) Assert.That(failures[t], Is.EqualTo(0), "Send returned false on thread " + t);

                var seen = new HashSet<uint>();
                var lastPerThread = new long[threads];
                for (int t = 0; t < threads; t++) lastPerThread[t] = -1;
                int received = 0;
                while (received < threads * perThread)
                {
                    IPacket packet = server.Read();
                    if (!(packet is C2S_MoveInput move)) continue;
                    received++;
                    Assert.That(seen.Add(move.Seq), Is.True, "Duplicate packet seq " + move.Seq);
                    int thread = (int)(move.Seq / 1000);
                    long index = move.Seq % 1000;
                    Assert.That(index, Is.GreaterThan(lastPerThread[thread]), "Per-thread order broken on thread " + thread);
                    lastPerThread[thread] = index;
                }
                Assert.That(seen.Count, Is.EqualTo(threads * perThread));
            }
        }
    }
}
