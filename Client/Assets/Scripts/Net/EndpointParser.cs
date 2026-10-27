using System.Globalization;
using System.Net;

namespace PingTheKiller.Client.Net
{
    public static class EndpointParser
    {
        public static bool TryParse(string value, out IPEndPoint endpoint, out string error)
        {
            endpoint = null;
            error = "Enter an IP address and port, for example 127.0.0.1:7777.";
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            int separator = value.LastIndexOf(':');
            if (separator <= 0) return false;
            string host = value.Substring(0, separator);
            if (host.StartsWith("[") && host.EndsWith("]")) host = host.Substring(1, host.Length - 2);
            else if (host.Contains(":")) return false;
            // IPAddress.TryParse also accepts abbreviated/hex IPv4; require four decimal octets.
            if (!host.Contains(":"))
            {
                string[] octets = host.Split('.');
                if (octets.Length != 4) return false;
                foreach (string octet in octets)
                    if (!byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
            }
            if (!IPAddress.TryParse(host, out IPAddress address)) return false;
            if (!int.TryParse(value.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                || port < 1 || port > 65535)
            {
                error = "Enter a port between 1 and 65535.";
                return false;
            }
            endpoint = new IPEndPoint(address, port);
            error = string.Empty;
            return true;
        }
    }
}
