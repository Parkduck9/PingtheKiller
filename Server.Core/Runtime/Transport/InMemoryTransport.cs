using System;
using System.Collections.Generic;

namespace PingTheKiller.ServerCore.Transport
{
    // This type and its connection contain no socket-related objects or APIs.
    public sealed class InMemoryTransport : ITransportListener
    {
        private readonly object _gate = new object();
        private readonly List<IConnection> _connections = new List<IConnection>();
        private bool _started;
        public event Action<IConnection> ConnectionAccepted;
        public void Start() { lock (_gate) _started = true; }
        public IConnection Connect()
        {
            lock (_gate)
            {
                if (!_started) throw new InvalidOperationException("Start the listener before connecting.");
                var server = new MemoryConnection();
                var client = new MemoryConnection();
                server.Peer = client;
                client.Peer = server;
                _connections.Add(server);
                server.Closed += () => { lock (_gate) _connections.Remove(server); };
                ConnectionAccepted?.Invoke(server);
                return client;
            }
        }
        public void Stop()
        {
            IConnection[] connections;
            lock (_gate)
            {
                _started = false;
                connections = _connections.ToArray();
                _connections.Clear();
            }
            foreach (var connection in connections) connection.Close();
        }
        public void Dispose() => Stop();

        private sealed class MemoryConnection : FramedConnection
        {
            internal MemoryConnection Peer;
            internal MemoryConnection() : base("memory:" + Guid.NewGuid().ToString("N")) { }
            public override void Send(byte[] frame)
            {
                if (frame == null) throw new ArgumentNullException(nameof(frame));
                if (!IsClosed && !Peer.IsClosed) Peer.ReceiveBytes(frame);
            }
            protected override void OnClose() => Peer?.Close();
        }
    }
}
