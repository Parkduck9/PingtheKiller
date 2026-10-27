using System;
using PingTheKiller.ServerCore.Transport;

namespace PingTheKiller.ServerCore
{
    public sealed class ServerOptions
    {
        public bool IsOffline { get; set; }
        public int BotCount { get; set; }
        public bool AutoStart { get; set; }
        // Test hook: seeds the private gameplay RNG (killer/skill assignment, bots, decoys) deterministically.
        // The console host and the offline client never set it; then every match draws a fresh CSPRNG seed.
        // This value is never sent to clients.
        public int? Seed { get; set; }
        // Test hook: fixes the cosmetic S2C_MatchStart.Seed. Unset, each match draws it independently from a CSPRNG.
        public int? MatchSeedForTests { get; set; }
        // Network-QA mode with U2a rules only: movement, masking, threat and reconnect. No hacking, capture,
        // skills, decoys, afterimage, escape or win/lose; only TimeUp ends a match. Never enabled implicitly.
        public bool Sandbox { get; set; }

        // The online factory is lazy: offline hosts never construct a TCP listener.
        public ITransportListener CreateTransport(Func<ITransportListener> onlineFactory = null)
        {
            if (IsOffline) return new InMemoryTransport();
            return onlineFactory != null ? onlineFactory() : new TcpTransport();
        }
    }
}
