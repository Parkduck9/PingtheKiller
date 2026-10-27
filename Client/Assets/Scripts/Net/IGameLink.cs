using System;
using System.Diagnostics;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Net
{
    // A packet plus its arrival time (LinkClock domain), stamped on the thread that received it.
    public readonly struct ReceivedPacket
    {
        public readonly IPacket Packet;
        public readonly double Time;
        public ReceivedPacket(IPacket packet) { Packet = packet; Time = LinkClock.Now; }
    }

    public static class LinkClock
    {
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }

    // What ClientApp talks to: TCP (NetClient) or the in-process server (OfflineLink).
    // Events fire only inside Pump, on the thread that calls it (Unity main thread).
    public interface IGameLink : IDisposable
    {
        bool IsConnected { get; }
        bool IsOffline { get; }
        // -1 when unknown or not applicable (offline).
        double RttMilliseconds { get; }
        event Action<ReceivedPacket> PacketReceived;
        event Action<string> Disconnected;
        bool Send(IPacket packet);
        void Pump();
    }
}
