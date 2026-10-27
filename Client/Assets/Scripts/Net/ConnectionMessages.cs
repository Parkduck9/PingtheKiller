using System;
using System.Net.Sockets;

namespace PingTheKiller.Client.Net
{
    public enum LinkFailure : byte { Unreachable, Timeout, Lost }

    // On-screen connection copy. Fixed sentences only: raw OS/exception text (often localized) is logged, never shown.
    public static class ConnectionMessages
    {
        public const string LobbyTimeout = "The server did not open a lobby. Check that the host runs the same version as this client.";
        public const string Left = "Disconnected. Join a server to play.";
        public const string OfflineStopped = "The offline match stopped. Start it again with Offline.";

        public static LinkFailure Classify(Exception error, bool connected)
        {
            if (connected) return LinkFailure.Lost;
            // InnerException walks through AggregateException (Task.Wait) to the first cause.
            for (Exception e = error; e != null; e = e.InnerException)
                if (e is TimeoutException || e is SocketException socket && socket.SocketErrorCode == SocketError.TimedOut)
                    return LinkFailure.Timeout;
            return LinkFailure.Unreachable;
        }

        public static string Describe(LinkFailure failure, string endpoint)
        {
            switch (failure)
            {
                case LinkFailure.Timeout: return "No answer from " + endpoint + " within 5 s. Check the IP and that both PCs are on the same network.";
                case LinkFailure.Lost: return "Connection to " + endpoint + " lost. Rejoin when the host is back.";
                default: return "Server not reachable at " + endpoint + ". Check that the host is running and the port is right.";
            }
        }

        public static string Describe(Exception error, bool connected, string endpoint) => Describe(Classify(error, connected), endpoint);

        // S2C_JoinResult.Result: 1 = no free slot, 2 = reconnect token refused.
        public static string Rejected(byte code, string endpoint)
            => code == 1 ? "The server at " + endpoint + " is full. Rejoin when a slot is free."
                : "The server at " + endpoint + " refused this player. Rejoin from the menu.";
    }
}
