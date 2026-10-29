using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

// One non-own entry of a killer-bound WorldState, attributed by tick and position. U2c sends killer-bound survivor
// entries with Id 0 in shuffled order, so the entry's Id is never read. Nearest holds every survivor at the minimal
// distance; ExactTie means those survivors stand at bit-identical positions (the only genuinely ambiguous case).
public sealed record Attribution(PlayerState Entry, double Distance, IReadOnlyList<int> Nearest, bool ExactTie)
{
    public bool Explained => Nearest.Count > 0 && Distance <= KillerView.Epsilon;
}

public static class KillerView
{
    // Positions in both views come from the same server state, so a correct match is bit-identical.
    public const float Epsilon = 0.05f;

    // The killer finds itself by its real Id (allowed); every other entry is attributed by position.
    public static bool IsOwn(PlayerState p, int killerId) => p.Id == killerId && p.Role == (byte)Role.Killer;

    // truth: every survivor of a survivor-bound WorldState with the same Tick (real Ids, true positions, bots included).
    public static List<Attribution> Attribute(S2C_WorldState killerWorld, int killerId, IReadOnlyList<PlayerState> truth)
    {
        var result = new List<Attribution>();
        foreach (var entry in killerWorld.Players)
        {
            if (IsOwn(entry, killerId)) continue;
            double best = double.PositiveInfinity;
            var nearest = new List<PlayerState>();
            foreach (var survivor in truth)
            {
                double d = Distance(entry, survivor);
                if (d < best) { best = d; nearest.Clear(); }
                if (d == best) nearest.Add(survivor);
            }
            bool exactTie = nearest.Count > 1 && nearest.All(s => SamePosition(s, nearest[0]));
            result.Add(new(entry, best, nearest.Select(s => s.Id).ToList(), exactTie));
        }
        return result;
    }

    public static bool SamePosition(PlayerState a, PlayerState b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) &&
        BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    private static double Distance(PlayerState a, PlayerState b)
    {
        double dx = (double)a.X - b.X, dz = (double)a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}

// Joins killer-bound WorldStates with survivor-bound ground truth by Tick. The two streams may arrive in any order:
// a killer frame waits until its tick's truth arrives; ticks the survivor stream never delivers are skipped.
// Every joined entry must be explained (sandbox: no decoys); onJoined(world, attributions, survivor count) then runs once
// per joined killer frame, under the join's lock, and may throw a CheckFailure.
public sealed class TickJoin(int killerId, Action<S2C_WorldState, List<Attribution>, int> onJoined)
{
    private readonly object sync = new();
    private readonly Dictionary<uint, PlayerState[]> truth = new();
    private readonly Dictionary<uint, List<S2C_WorldState>> waiting = new();
    private uint? maxTruth;

    public int Joined { get; private set; }
    public int Entries { get; private set; }
    public int Skipped { get { lock (sync) return waiting.Values.Sum(w => w.Count); } }

    // True while a waiting killer frame has a tick newer than every truth received (it may still be joined).
    public bool AwaitingTruth
    {
        get { lock (sync) return waiting.Keys.Any(t => maxTruth == null || t > maxTruth); }
    }

    public void AddTruth(S2C_WorldState world)
    {
        lock (sync)
        {
            truth[world.Tick] = world.Players.Where(p => p.Role != (byte)Role.Killer).ToArray();
            if (maxTruth == null || world.Tick > maxTruth) maxTruth = world.Tick;
            if (waiting.Remove(world.Tick, out var frames))
                foreach (var frame in frames) Join(frame);
        }
    }

    public void AddKiller(S2C_WorldState world)
    {
        lock (sync)
        {
            if (truth.ContainsKey(world.Tick)) Join(world);
            else
            {
                if (!waiting.TryGetValue(world.Tick, out var frames)) waiting[world.Tick] = frames = new List<S2C_WorldState>();
                frames.Add(world);
            }
        }
    }

    private void Join(S2C_WorldState world)
    {
        var attributions = KillerView.Attribute(world, killerId, truth[world.Tick]);
        Joined++;
        Entries += attributions.Count;
        foreach (var a in attributions) RequireExplained(world, a);
        onJoined(world, attributions, truth[world.Tick].Length);
    }

    public int TruthCount(uint tick)
    {
        lock (sync) return truth.TryGetValue(tick, out var t) ? t.Length : 0;
    }

    // Diagnostic only (never a pass rule): which survivor's position at tick-1 or tick+1 the entry matches exactly.
    public string Neighbours(uint tick, PlayerState entry)
    {
        lock (sync)
        {
            foreach (uint other in new[] { tick - 1, tick + 1 })
                if (truth.TryGetValue(other, out var states))
                    foreach (var s in states)
                        if (KillerView.SamePosition(s, entry))
                            return $"; it equals survivor {s.Id}'s position at tick={other} (stale or early snapshot)";
            return "";
        }
    }

    private void RequireExplained(S2C_WorldState world, Attribution a)
    {
        if (a.Explained) return;
        int survivors = TruthCount(world.Tick);
        throw new CheckFailure($"unexplained killer-view entry at tick={world.Tick}: ({a.Entry.X:R},{a.Entry.Z:R}) flags={a.Entry.Flags} " +
            (survivors == 0 ? "with no survivor in the ground truth" : $"is {a.Distance:F3} m from the nearest of {survivors} survivors' true positions") +
            $" (epsilon {KillerView.Epsilon} m; the sandbox has no decoys, so this is a possible leak){Neighbours(world.Tick, a.Entry)}.");
    }
}
