using System;
using System.Collections.Concurrent;
using System.Threading;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Net
{
    // Offline bot mode (GDD 6.2): the real GameServer runs in this process on its own game-loop thread and talks to
    // the client through InMemoryTransport. No socket type is constructed on this path (Store: no firewall prompt).
    // Packets arrive on the game-loop thread and are dispatched only in Pump, like NetClient.
    public sealed class OfflineLink : IGameLink
    {
        public const int DefaultBots = 3;
        private readonly ConcurrentQueue<ReceivedPacket> incoming = new ConcurrentQueue<ReceivedPacket>();
        private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();
        private readonly GameServer server;
        private readonly InMemoryTransport transport;
        private readonly IConnection connection;
        private Thread stopper;
        private int stopped, queuedIn;

        public OfflineLink(int bots = DefaultBots)
        {
            var options = new ServerOptions { IsOffline = true, BotCount = bots };
            ITransportListener created = options.CreateTransport();
            transport = created as InMemoryTransport;
            if (transport == null)
            {
                created?.Dispose();
                throw new InvalidOperationException("Offline mode requires InMemoryTransport, got " + (created == null ? "null" : created.GetType().Name) + ".");
            }
            server = new GameServer(options);
            server.Log += line => logs.Enqueue(line);
            transport.ConnectionAccepted += server.AddConnection;
            transport.Start();
            connection = transport.Connect();
            connection.PacketAvailable += Drain;
            connection.Closed += OnClosed;
            server.Start();
            Send(new C2S_Join { Nickname = "Player" });
        }

        public ITransportListener Transport => transport;
        public bool IsConnected => Volatile.Read(ref stopped) == 0 && !connection.IsClosed;
        public bool IsOffline => true;
        public double RttMilliseconds => -1;
        public event Action<ReceivedPacket> PacketReceived;
        public event Action<string> Disconnected;
        // Server log lines, raised inside Pump (main thread).
        public event Action<string> ServerLog;

        public bool Send(IPacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            if (!IsConnected) return false;
            connection.Send(PacketCodec.Encode(packet));
            return true;
        }

        public void Pump()
        {
            while (logs.TryDequeue(out string line)) ServerLog?.Invoke(line);
            int budget = 512;
            while (budget-- > 0 && incoming.TryDequeue(out ReceivedPacket item))
            {
                Interlocked.Decrement(ref queuedIn);
                PacketReceived?.Invoke(item);
            }
            while (notices.TryDequeue(out string notice)) Disconnected?.Invoke(notice);
        }

        // Game-loop thread (or the sender's thread for direct replies): queue with the arrival time, never dispatch here.
        private void Drain()
        {
            while (connection.TryReceive(out IPacket packet))
            {
                if (Interlocked.Increment(ref queuedIn) > 512) { Stop(ConnectionMessages.OfflineStopped); return; }
                incoming.Enqueue(new ReceivedPacket(packet));
            }
        }

        private void OnClosed() => Stop(ConnectionMessages.OfflineStopped);

        private void Stop(string notice)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            notices.Enqueue(notice);
            connection.PacketAvailable -= Drain;
            connection.Close();
            // GameServer.Stop joins the game-loop thread, so it runs on a helper thread: the caller never waits.
            var thread = new Thread(() =>
            {
                try { server.Stop(); }
                finally { transport.Stop(); }
            }) { IsBackground = true, Name = "PTK offline stop" };
            Volatile.Write(ref stopper, thread);
            thread.Start();
        }

        // True once the server's game-loop thread has ended (tests and diagnostics only; never call on the main thread).
        public bool WaitStopped(int milliseconds)
        {
            Thread thread = Volatile.Read(ref stopper);
            return thread != null && thread.Join(milliseconds);
        }

        public void Dispose() => Stop(ConnectionMessages.Left);
    }
}
