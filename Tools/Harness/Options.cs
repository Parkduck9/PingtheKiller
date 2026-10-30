using System.Globalization;

namespace PingTheKiller.Harness;

public sealed record Options
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 7777;
    public string Report { get; init; } = "docs/qa/harness-latest.json";
    public int Threshold { get; init; } = 90;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public IReadOnlySet<string> Only { get; init; } = new HashSet<string>(Scorer.Ids);
    // H6 needs a fresh lobby of its own; H2-H5 occupy the match of the --host/--port server.
    public string? H6Host { get; init; }
    public int? H6Port { get; init; }
    // U3d: the H6 server shows survivors the Killer in line of sight (U2e). H6 then also requires at least one legal visible
    // killer entry and at least one blocked-and-absent tick. Off by default: legal entries are accepted and counted either way.
    public bool ExpectLos { get; init; }
    // Run state, not a CLI option: skipped unknown-Id frames of every connection. HarnessRunner gives each run a fresh one.
    public UnknownPackets Unknown { get; init; } = new();

    public Options ForH6() => this with { Host = H6Host ?? Host, Port = H6Port ?? Port };

    // Failure evidence when H6 would share the H2-H5 server (decided without network I/O), otherwise null.
    public string? H6Conflict()
    {
        if (!Only.Contains("H6") || !Only.Any(id => id is "H2" or "H3" or "H4" or "H5")) return null;
        bool sameServer = H6Port == null ||
            (H6Port == Port && string.Equals(H6Host ?? Host, Host, StringComparison.OrdinalIgnoreCase));
        return sameServer ? ScenarioCheck.NeedsOwnServer : null;
    }

    public static Options Parse(string[] args)
    {
        var result = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--expect-los")
            {
                result = result with { ExpectLos = true };
                continue;
            }
            if (++i == args.Length) throw new ArgumentException($"Missing value for {key}.");
            string value = args[i];
            result = key switch
            {
                "--host" when !string.IsNullOrWhiteSpace(value) => result with { Host = value },
                "--port" when int.TryParse(value, out int n) && n is > 0 and <= 65535 => result with { Port = n },
                "--report" when !string.IsNullOrWhiteSpace(value) => result with { Report = value },
                "--threshold" when int.TryParse(value, out int n) && n is >= 0 and <= 100 => result with { Threshold = n },
                "--duration" => result with { Duration = Seconds(value, key) },
                "--timeout" => result with { Timeout = Seconds(value, key) },
                "--only" => result with { Only = ParseOnly(value) },
                "--h6-host" when !string.IsNullOrWhiteSpace(value) => result with { H6Host = value },
                "--h6-port" when int.TryParse(value, out int n) && n is > 0 and <= 65535 => result with { H6Port = n },
                _ => throw new ArgumentException($"Unknown option or invalid value: {key} {value}")
            };
        }
        return result;
    }

    private static TimeSpan Seconds(string value, string key)
    {
        if (!double.TryParse(value, CultureInfo.InvariantCulture, out double seconds) ||
            !double.IsFinite(seconds) || seconds < 0.1 || seconds > 3600)
            throw new ArgumentException($"{key} must be between 0.1 and 3600 seconds.");
        return TimeSpan.FromSeconds(seconds);
    }

    private static HashSet<string> ParseOnly(string value)
    {
        var ids = value.Split(',', StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0 || ids.Any(id => !Scorer.Ids.Contains(id.ToUpperInvariant())))
            throw new ArgumentException("--only expects a comma-separated subset of H1,H2,H3,H4,H5,H6.");
        return ids.Select(id => id.ToUpperInvariant()).ToHashSet();
    }
}
