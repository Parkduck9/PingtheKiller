using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

// Compares what the killer can measure about phantom tracks and real tracks, using the server's ground-truth labels.
// A track is one uninterrupted presence of one label in the killer view (live frames, then afterimage frames).
public sealed class IndistinguishabilityTests
{
    private readonly ITestOutputHelper _output;
    public IndistinguishabilityTests(ITestOutputHelper output) { _output = output; }

    private sealed class Track
    {
        // Excluded real tracks: Lagging (Status shows it), forced exposure (the killer's own Ping Scanner or the
        // Traffic Overload notice explains it), rescuing (a still blip beside a Lagging blip), holding an exit switch (a
        // still blip on a known switch point while its progress rises, GDD v2) and tracks that end because the survivor
        // escaped or was eliminated mid-match (GDD v2: the match goes on). Phantoms do none of these.
        internal bool Phantom, Lagging, Truncated, Excluded;
        internal readonly List<(float X, float Z)> Live = new();
        internal int Tail;
        internal float SpawnNearest;
    }

    private static List<Track> Record(int seed)
    {
        var tracks = new List<Track>();
        using var r = new KillerMatchRecorder(seed, SkillIds.PacketAfterimage);
        var open = new Dictionary<(bool, int), Track>();
        r.Run(packets =>
        {
            var world = packets.OfType<S2C_WorldState>().LastOrDefault();
            if (world == null) return;
            var sources = r.F.Server.KillerViewSources;
            if (sources.Count != world.Players.Count) return; // the final frame: the match has already been reset
            var truth = r.F.Server.PlayerSnapshot();
            var present = new HashSet<(bool, int)>();
            for (int i = 0; i < world.Players.Count; i++)
            {
                var entry = world.Players[i];
                if (entry.Role != (byte)Role.Survivor) continue;
                var key = (sources[i].Phantom, sources[i].Label);
                present.Add(key);
                if (!open.TryGetValue(key, out var track))
                {
                    track = new Track { Phantom = sources[i].Phantom };
                    track.SpawnNearest = truth.Where(p => p.Role == Role.Survivor && (p.Status == PlayerStatus.Normal || p.Status == PlayerStatus.Lagging) &&
                            (sources[i].Phantom || p.Id != sources[i].Label))
                        .Select(p => MathF.Sqrt((p.X - entry.X) * (p.X - entry.X) + (p.Z - entry.Z) * (p.Z - entry.Z)))
                        .DefaultIfEmpty(float.NaN).Min();
                    open[key] = track;
                    tracks.Add(track);
                }
                if (entry.Status == (byte)PlayerStatus.Lagging) track.Lagging = true;
                if (!sources[i].Phantom && truth.FirstOrDefault(p => p.Id == sources[i].Label) is var t && (t.Exposed || t.Rescuing || t.Switching))
                    track.Excluded = true;
                if (sources[i].Afterimage) track.Tail++;
                else { track.Tail = 0; track.Live.Add((entry.X, entry.Z)); }
            }
            foreach (var key in open.Keys.Where(k => !present.Contains(k)).ToList())
            {
                if (!key.Item1 && truth.FirstOrDefault(p => p.Id == key.Item2) is var gone &&
                    (gone.Status == PlayerStatus.Escaped || gone.Status == PlayerStatus.Eliminated))
                    open[key].Excluded = true;
                open.Remove(key);
            }
        });
        foreach (var track in open.Values) track.Truncated = true;
        return tracks;
    }

    // Two-sample Kolmogorov-Smirnov statistic and asymptotic p-value.
    internal static (double d, double p) KolmogorovSmirnov(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var x = a.OrderBy(v => v).ToArray();
        var y = b.OrderBy(v => v).ToArray();
        int i = 0, j = 0;
        double d = 0;
        while (i < x.Length && j < y.Length)
        {
            double v = Math.Min(x[i], y[j]);
            while (i < x.Length && x[i] <= v) i++;
            while (j < y.Length && y[j] <= v) j++;
            d = Math.Max(d, Math.Abs((double)i / x.Length - (double)j / y.Length));
        }
        double en = Math.Sqrt((double)x.Length * y.Length / (x.Length + y.Length));
        double lambda = (en + 0.12 + 0.11 / en) * d;
        double p = 0;
        for (int k = 1; k <= 100; k++) p += 2 * Math.Pow(-1, k - 1) * Math.Exp(-2 * k * k * lambda * lambda);
        return (d, Math.Max(0, Math.Min(1, d == 0 ? 1 : p)));
    }

    [Fact]
    public void KolmogorovSmirnovMatchesKnownCases()
    {
        Assert.Equal(1.0, KolmogorovSmirnov(new double[] { 1, 2, 3 }, new double[] { 1, 2, 3 }).p);
        var (d, p) = KolmogorovSmirnov(Enumerable.Range(0, 200).Select(v => (double)v).ToList(), Enumerable.Range(100, 200).Select(v => (double)v).ToList());
        Assert.Equal(0.5, d, 6);
        Assert.True(p < 1e-10);
    }

    // Gate (orchestrator ruling, attempt 2): KS p >= 0.01 for speed, still-away-from-terminal and afterimage tail,
    // phantoms against real Normal Signal tracks. Spawn distance (aim D < 0.2) and lifetime are reported only.
    // White Noise lives 1.5 s by design (GDD 4), so the gated features are per-step and do not scale with lifetime:
    // the track's median step speed, and still steps away from any terminal before the final stop frame (every
    // stopping track, real or phantom, ends with one still frame). The per-track means are reported as well.
    [Fact]
    public void PhantomTracksMatchRealNormalSignalTracksInSpeedStillnessAndTail()
    {
        var terminals = new PingTheKiller.ServerCore.Map.FixedMap().TerminalPositions.Select(p => (p.X,p.Z)).ToArray();
        var tracks = new List<Track>();
        foreach (int seed in new[] { 1, 40, 80, 120, 160, 200 }) tracks.AddRange(Record(seed));
        var usable = tracks.Where(t => !t.Lagging && !t.Excluded && !t.Truncated && t.Live.Count >= 2).ToList();
        bool StillAway(Track t, int k) => Step(t, k) < 0.01f &&
            terminals.All(c => MathF.Sqrt((t.Live[k].X - c.Item1) * (t.Live[k].X - c.Item1) + (t.Live[k].Z - c.Item2) * (t.Live[k].Z - c.Item2)) > GameConstants.TerminalRadius);
        // Quantize below 1 mm/s: coordinate ULPs vary across the larger map and must not split identical 4 m/s steps in KS.
        var features = new (string Name, Func<Track, double> Value, bool Gated)[]
        {
            ("speed m/s (median step)", t => Median(Enumerable.Range(1, t.Live.Count - 1).Select(k => Math.Round((double)Step(t, k) * GameConstants.TickRate, 3)).ToList()), true),
            ("still-away steps before the final frame", t => Enumerable.Range(1, t.Live.Count - 2).Count(k => StillAway(t, k)), true),
            ("afterimage tail ticks", t => t.Tail, true),
            ("mean speed m/s", t => Enumerable.Range(1, t.Live.Count - 1).Average(k => Step(t, k)) * GameConstants.TickRate, false),
            ("still-away fraction of all steps", t => Enumerable.Range(1, t.Live.Count - 1).Count(k => StillAway(t, k)) / (double)(t.Live.Count - 1), false),
            ("lifetime s", t => t.Live.Count / (double)GameConstants.TickRate, false),
            ("spawn distance to nearest real survivor m", t => t.SpawnNearest, false),
        };
        var phantoms = usable.Where(t => t.Phantom).ToList();
        var reals = usable.Where(t => !t.Phantom).ToList();
        _output.WriteLine($"tracks: {phantoms.Count} phantom, {reals.Count} real (of {tracks.Count} recorded)");
        var failures = new List<string>();
        foreach (var (name, value, gated) in features)
        {
            var a = phantoms.Select(value).Where(v => !double.IsNaN(v)).ToList();
            var b = reals.Select(value).Where(v => !double.IsNaN(v)).ToList();
            var (d, p) = KolmogorovSmirnov(a, b);
            string line = $"{name}: phantom median {Median(a):0.###} mean {a.Average():0.###}, real median {Median(b):0.###} mean {b.Average():0.###}, KS D={d:0.###} p={p:0.####}{(gated ? "" : " (reported, not gated)")}";
            _output.WriteLine(line);
            if (gated && p < 0.01) failures.Add($"{name} (D={d:0.###}, p={p:0.####})");
        }
        _output.WriteLine(failures.Count == 0 ? "GATE PASS" : "GATE FAIL: " + string.Join(", ", failures));
        Assert.True(phantoms.Count >= 100 && reals.Count >= 100, $"{phantoms.Count} phantom / {reals.Count} real tracks");
        // With Packet Afterimage every complete track, phantom or real, ends in a full 20-tick tail (no cap cuts it).
        Assert.Equal(20.0, phantoms.Average(t => t.Tail));
        Assert.Equal(20.0, reals.Average(t => t.Tail));
        Assert.True(failures.Count == 0, "KS gate failed: " + string.Join(", ", failures));
    }

    private static float Step(Track t, int k) =>
        MathF.Sqrt((t.Live[k].X - t.Live[k - 1].X) * (t.Live[k].X - t.Live[k - 1].X) + (t.Live[k].Z - t.Live[k - 1].Z) * (t.Live[k].Z - t.Live[k - 1].Z));

    private static double Median(List<double> values) => values.Count == 0 ? double.NaN : MatchLengthTests.Median(values);
}
