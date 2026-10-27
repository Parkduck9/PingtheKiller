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
    public sealed class NetClient : IDisposable
    {
        public readonly struct Received
        {
            public readonly IPacket Packet;
            public readonly double Time;
            public Received(IPacket packet) { Packet = packet; Time = Now; }
        }

        private readonly ConcurrentQueue<Received> incoming = new ConcurrentQueue<Received>();
        private readonly ConcurrentQueue<IPacket> outgoing = new ConcurrentQueue<IPacket>();
        private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly TcpClient socket = new TcpClient();
        private int stopped, started, connected, queuedOut, queuedIn;
        private string endpointText;
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public bool IsConnected => Volatile.Read(ref connected) != 0 && Volatile.Read(ref stopped) == 0;
        public double RttMilliseconds { get; private set; } = -1;
        public event Action<Received> PacketReceived;
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
                Stop("Send queue is full. Rejoin when the connection is stable.");
                return false;
            }
            outgoing.Enqueue(packet);
            wake.Set();
            return true;
        }

        public void Pump()
        {
            int budget = 512;
            while (budget-- > 0 && incoming.TryDequeue(out Received item))
            {
                Interlocked.Decrement(ref queuedIn);
                // Received.Time is stamped on the receive thread, so frame stalls do not inflate RTT.
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
                if (!connection.Wait(5000)) throw new IOException("Connection timed out.");
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
                        incoming.Enqueue(new Received(packet));
                    }
                }
            }
            catch (Exception exception) { Stop($"Server not reachable at {endpointText} or connection lost. Check that the host is running. {exception.GetBaseException().Message}"); }
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
            catch (Exception exception) { Stop($"Connection to {endpointText} lost. Rejoin after checking the server. {exception.Message}"); }
        }

        private static bool IsServerPacket(PacketId id) => id is PacketId.S2C_JoinResult or PacketId.S2C_WorldState or PacketId.S2C_SkillEffect
            or PacketId.S2C_GameResult or PacketId.S2C_Pong or PacketId.S2C_LobbyState or PacketId.S2C_MatchStart;

        private static void Write(NetworkStream stream, IPacket packet)
        {
            byte[] frame = PacketCodec.Encode(packet);
            stream.Write(frame, 0, frame.Length);
        }

        private void Stop(string reason)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            Volatile.Write(ref connected, 0);
            socket.Close();
            wake.Set();
            notices.Enqueue(reason);
        }

        public void Dispose() => Stop("Disconnected. Join a server to play.");
    }
}
