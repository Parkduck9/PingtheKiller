using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Net
{
    // One instance per connection. Pump is called exclusively from Unity Update.
    public sealed class NetClient : IGameLink
    {
        private static int created;
        private readonly ConcurrentQueue<ReceivedPacket> incoming = new ConcurrentQueue<ReceivedPacket>();
        private readonly ConcurrentQueue<IPacket> outgoing = new ConcurrentQueue<IPacket>();
        private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly TcpClient socket = new TcpClient();
        private int stopped, started, connected, queuedOut, queuedIn;
        private string endpointText;
        private volatile string failureDetail;
        public static double Now => LinkClock.Now;
        // Instances ever constructed (each owns a TcpClient); the offline tests assert this does not move.
        public static int Created => Volatile.Read(ref created);
        public NetClient() { Interlocked.Increment(ref created); }
        public bool IsConnected => Volatile.Read(ref connected) != 0 && Volatile.Read(ref stopped) == 0;
        public bool IsOffline => false;
        public double RttMilliseconds { get; private set; } = -1;
        // Raw cause of the last failure, for Debug.Log only; never shown on screen.
        public string FailureDetail => failureDetail;
        public event Action<ReceivedPacket> PacketReceived;
        public event Action<string> Disconnected;

        public void Connect(IPEndPoint endpoint)
        {
            if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Use a new NetClient for each connection.");
            endpointText = endpoint.ToString();
            new Thread(() => ReceiveLoop(endpoint)) { IsBackground = true, Name = "PTK receive" }.Start();
        }

        public bool Send(IPacket packet)
        {
            if (!IsConnected) return false;
            if (Interlocked.Increment(ref queuedOut) > 256)
            {
                Stop(ConnectionMessages.Describe(LinkFailure.Lost, endpointText), "send queue full");
                return false;
            }
            outgoing.Enqueue(packet);
            wake.Set();
            return true;
        }

        public void Pump()
        {
            int budget = 512;
            while (budget-- > 0 && incoming.TryDequeue(out ReceivedPacket item))
            {
                Interlocked.Decrement(ref queuedIn);
                // ReceivedPacket.Time is stamped on the receive thread, so frame stalls do not inflate RTT.
                if (item.Packet is S2C_Pong pong)
                    RttMilliseconds = Math.Max(0, (item.Time - (double)pong.Timestamp / Stopwatch.Frequency) * 1000.0);
                PacketReceived?.Invoke(item);
            }
            while (notices.TryDequeue(out string notice)) Disconnected?.Invoke(notice);
        }

        private void ReceiveLoop(IPEndPoint endpoint)
        {
            try
            {
                // Connect, timeout wait, and every socket operation run on worker threads.
                var connection = socket.ConnectAsync(endpoint.Address, endpoint.Port);
                if (!connection.Wait(5000)) throw new TimeoutException("Connect timed out after 5 s.");
                if (Volatile.Read(ref stopped) != 0) return;
                socket.NoDelay = true;
                NetworkStream stream = socket.GetStream();
                Volatile.Write(ref connected, 1);
                outgoing.Enqueue(new C2S_Join { Nickname = "Player" });
                Interlocked.Increment(ref queuedOut);
                new Thread(() => SendLoop(stream)) { IsBackground = true, Name = "PTK send" }.Start();
                var assembler = new PacketAssembler();
                var bytes = new byte[8192];
                while (Volatile.Read(ref stopped) == 0)
                {
                    int count = stream.Read(bytes, 0, bytes.Length);
                    if (count == 0) throw new IOException("The server closed the connection.");
                    assembler.Append(bytes.AsSpan(0, count));
                    while (true)
                    {
                        AssembleResult result = assembler.TryRead(out ushort id, out ReadOnlyMemory<byte> payload);
                        if (result == AssembleResult.NeedMoreData) break;
                        if (result == AssembleResult.Malformed) throw new PacketFormatException("Invalid frame length.");
                        IPacket packet = PacketCodec.Decode(id, payload.Span);
                        if (!IsServerPacket(packet.Id)) throw new PacketFormatException("Unexpected client packet from server.");
                        if (Interlocked.Increment(ref queuedIn) > 512) throw new IOException("Receive queue is full.");
                        incoming.Enqueue(new ReceivedPacket(packet));
                    }
                }
            }
            catch (Exception exception)
            {
                bool wasConnected = Volatile.Read(ref connected) != 0;
                Stop(ConnectionMessages.Describe(exception, wasConnected, endpointText), exception.GetBaseException().ToString());
            }
        }

        private void SendLoop(NetworkStream stream)
        {
            double nextPing = 0;
            try
            {
                while (Volatile.Read(ref stopped) == 0)
                {
                    while (outgoing.TryDequeue(out IPacket packet))
                    {
                        Interlocked.Decrement(ref queuedOut);
                        Write(stream, packet);
                    }
                    if (Now >= nextPing)
                    {
                        Write(stream, new C2S_Ping { Timestamp = Stopwatch.GetTimestamp() });
                        nextPing = Now + 1;
                    }
                    wake.WaitOne(20);
                }
            }
            catch (Exception exception) { Stop(ConnectionMessages.Describe(LinkFailure.Lost, endpointText), exception.GetBaseException().ToString()); }
        }

        // Every S2C_ packet the client accepts from the socket (protocol v0.3 adds S2C_SelfState); a C2S_ id disconnects.
        public static bool IsServerPacket(PacketId id) => id is PacketId.S2C_JoinResult or PacketId.S2C_WorldState or PacketId.S2C_SkillEffect
            or PacketId.S2C_GameResult or PacketId.S2C_Pong or PacketId.S2C_LobbyState or PacketId.S2C_MatchStart or PacketId.S2C_SelfState
            or PacketId.S2C_SyncCheck or PacketId.S2C_SyncResult; // v0.4 sync check packets

        private static void Write(NetworkStream stream, IPacket packet)
        {
            byte[] frame = PacketCodec.Encode(packet);
            stream.Write(frame, 0, frame.Length);
        }

        private void Stop(string reason, string detail)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            failureDetail = detail;
            Volatile.Write(ref connected, 0);
            socket.Close();
            wake.Set();
            notices.Enqueue(reason);
        }

        public void Dispose() => Stop(ConnectionMessages.Left, null);
    }
}
