using System;
using PingTheKiller.ServerCore.Transport;

namespace PingTheKiller.ServerCore
{
    public sealed class ServerOptions
    {
        public bool IsOffline { get; set; }
        public int BotCount { get; set; }
        public bool AutoStart { get; set; }
        public int? Seed { get; set; }

        // The online factory is lazy: offline hosts never construct a TCP listener.
        public ITransportListener CreateTransport(Func<ITransportListener> onlineFactory = null)
        {
            if (IsOffline) return new InMemoryTransport();
            return onlineFactory != null ? onlineFactory() : new TcpTransport();
        }
    }
}
