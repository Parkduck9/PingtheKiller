using System;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore
{
    public sealed partial class GameServer
    {
        internal const double RttProbeIntervalSeconds = 1.0;
        internal const double RttProbeTimeoutSeconds = 1.0;
        internal const double MaxRttSeconds = 0.25;
        private const double RttSmoothingFactor = 1.0 / 8;
        // Independent of gameplay streams: exposing nonces reveals nothing about their state or draw counts.
        private PrivateRandom _rttRandom;
        private uint _lastRttNonce;

        private void UpdateRttProbes()
        {
            if (_offline) return;
            foreach (var player in _players)
            {
                if (player.IsBot || player.ConnectionId == null ||
                    !_connections.TryGetValue(player.ConnectionId, out var binding) || binding.Connection.IsClosed) continue;
                double now = _clock();
                if (player.RttProbePending)
                {
                    if (now - player.RttProbeSentAt < RttProbeTimeoutSeconds) continue;
                    player.RttProbePending = false; // Timeout keeps the estimate and the original schedule.
                }
                if (now < player.RttNextProbeAt) continue;
                uint nonce;
                do nonce = (uint)_rttRandom.NextUInt64();
                while (nonce == 0 || nonce == _lastRttNonce || nonce == unchecked(_lastRttNonce + 1) ||
                    nonce == unchecked(_lastRttNonce - 1) || nonce == player.RttProbeNonce ||
                    nonce == unchecked(player.RttProbeNonce + 1) || nonce == unchecked(player.RttProbeNonce - 1));
                _lastRttNonce = nonce;
                var probe = new S2C_RttProbe { Nonce = nonce };
                player.RttProbeNonce = nonce;
                player.RttProbePending = true;
                player.RttProbeSentAt = _clock();
                Send(binding.Connection, probe);
                player.RttNextProbeAt = player.RttProbeSentAt + RttProbeIntervalSeconds;
            }
        }

        // Both ends use the server clock. An unpredictable nonce prevents echoing before receiving the probe,
        // so the measured RTT cannot be shorter than the real path. Delaying/dropping echoes can only inflate it
        // (bounded at 0.25 s): the judge shifts the same-width window and Fail limit by that constant, granting
        // nothing beyond a fair client with that RTT. S2C_SyncCheck already tells clients the zone.
        private void ReceiveRttEcho(Player player, uint nonce, double receivedAt)
        {
            if (_offline || player.IsBot || !player.RttProbePending || nonce != player.RttProbeNonce) return;
            player.RttProbePending = false;
            double elapsed = receivedAt - player.RttProbeSentAt;
            if (elapsed >= RttProbeTimeoutSeconds) return;
            double sample = Math.Max(0, Math.Min(MaxRttSeconds, elapsed));
            player.RttSeconds = player.RttSampleCount == 0 ? sample :
                player.RttSeconds + (sample - player.RttSeconds) * RttSmoothingFactor;
            player.RttSeconds = Math.Max(0, Math.Min(MaxRttSeconds, player.RttSeconds));
            if (player.RttSampleCount < int.MaxValue) ++player.RttSampleCount;
        }

        private static void ResetRtt(Player player)
        {
            player.RttSeconds = 0;
            player.RttProbePending = false;
            player.RttProbeNonce = 0;
            player.RttSampleCount = 0;
            player.RttProbeSentAt = player.RttNextProbeAt = 0;
        }

        // Read-only, locked inspection for tests, including a running TCP game loop.
        internal (double Seconds, int Samples, bool Outstanding, double JudgeSeconds) RttStateForTests(int playerId)
        {
            lock (_tickGate)
                foreach (var player in _players)
                    if (player.Id == playerId)
                        return (player.RttSeconds, player.RttSampleCount, player.RttProbePending, RttOf(player));
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
    }
}
