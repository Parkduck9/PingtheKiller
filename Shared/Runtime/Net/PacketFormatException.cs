using System;

namespace PingTheKiller.Shared.Net
{
    public sealed class PacketFormatException : FormatException
    {
        public PacketFormatException(string message) : base(message) { }
        public PacketFormatException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
