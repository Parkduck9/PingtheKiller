using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

// Outcome of one survivor-bound WorldState under the U3d/U2e line-of-sight contract.
// Visible: a legal killer entry (line of sight clear). BlockedAbsent / ClearAbsent: no entry, sight blocked / clear.
// Ambiguous: within LineOfSight.Band of a wall boundary; not judged (an entry present there still had to match every field).
// Unknown: no killer tick (or no own entry) to judge with. Inside a finished window this never passes: SightJoin.Finish()
// fails every survivor world whose tick the killer never received (U3d a3: coverage, no tolerance).
// Ineligible: no entry and the recipient is not Normal/Lagging (a spectator).
public enum SightOutcome { Visible, BlockedAbsent, ClearAbsent, Ambiguous, Unknown, Ineligible }

// Entry: index of the killer entry in the world, or -1. Leak: failure evidence.
public readonly record struct SightVerdict(SightOutcome Outcome, int Entry, string? Leak)
{
    // U3d a2 (QA F1): only a final Visible or Ambiguous verdict lets the leak scan treat the entry's X/Z as legitimate;
    // an undecided (Unknown) entry gets no allowance.
    public int AllowedEntry => Leak == null && Outcome is SightOutcome.Visible or SightOutcome.Ambiguous ? Entry : -1;
}

public static class KillerSight
{
    // The entry's X/Z against the killer's own position at the same tick (both come from the same server state).
    public const double Epsilon = 0.05;
    public const double YawEpsilon = 0.01;

    public static bool IsKillerEntry(PlayerState p, int killerId) => p.Id == killerId || p.Role == (byte)Role.Killer;

    public static bool HasKillerEntry(S2C_WorldState world, int killerId) => world.Players.Exists(p => IsKillerEntry(p, killerId));

    public static bool Eligible(PlayerState recipient) =>
        recipient.Status is (byte)PlayerStatus.Normal or (byte)PlayerStatus.Lagging;

    // The killer's own entry in its own stream: real Id and Role Killer, exactly one.
    public static PlayerState? Own(S2C_WorldState killerWorld, int killerId)
    {
        PlayerState? own = null;
        foreach (var p in killerWorld.Players)
            if (p.Id == killerId && p.Role == (byte)Role.Killer)
            {
                if (own != null) return null;
                own = p;
            }
        return own;
    }

    // world: survivor-bound, received by recipientId. killer: the killer's own entry at world.Tick (null = unknown).
    // previous: the killer's own entry at world.Tick - 1 (evidence only).
    public static SightVerdict Judge(S2C_WorldState world, int recipientId, int killerId, PlayerState? killer,
        PlayerState? previous = null, IReadOnlyList<Wall>? walls = null)
    {
        int index = -1, count = 0, own = 0;
        PlayerState self = default;
        for (int i = 0; i < world.Players.Count; i++)
        {
            var p = world.Players[i];
            if (IsKillerEntry(p, killerId)) { count++; if (index < 0) index = i; }
            else if (p.Id == recipientId) { own++; self = p; }
        }
        bool known = own == 1 && float.IsFinite(self.X) && float.IsFinite(self.Z);
        SightVerdict Leak(string why) => new(SightOutcome.Visible, -1,
            $"Killer {killerId} leaked to survivor {recipientId} at tick={world.Tick}: {why}");
        if (count == 0)
        {
            if (killer is not { } k || !known) return new(SightOutcome.Unknown, -1, null);
            if (!Eligible(self)) return new(SightOutcome.Ineligible, -1, null);
            return LineOfSight.Classify(self.X, self.Z, k.X, k.Z, walls) switch
            {
                Sight.Clear => new(SightOutcome.ClearAbsent, -1, null),
                Sight.Blocked => new(SightOutcome.BlockedAbsent, -1, null),
                _ => new(SightOutcome.Ambiguous, -1, null)
            };
        }
        if (count > 1) return Leak($"{count} killer entries in one WorldState");
        var e = world.Players[index];
        if (e.Id != killerId || e.Role != (byte)Role.Killer)
            return Leak($"entry Id={e.Id} Role={e.Role}; only the real Id {killerId} with Role Killer may appear");
        if (!known) return Leak($"the recipient's own entry is missing or invalid ({own} found), so it cannot be judged");
        if (!Eligible(self))
            return Leak($"the recipient is a spectator (status {self.Status} {(PlayerStatus)self.Status}); spectators never see the Killer");
        if (e.Flags != 0 || BitConverter.SingleToInt32Bits(e.StatusTimer) != 0 || e.CaptureCount != 0)
            return Leak($"Flags={e.Flags}, StatusTimer={e.StatusTimer:R}, CaptureCount={e.CaptureCount}; the contract requires all three 0");
        if (!float.IsFinite(e.X) || !float.IsFinite(e.Z) || !float.IsFinite(e.Yaw))
            return Leak($"non-finite entry ({e.X:R},{e.Z:R}) yaw={e.Yaw:R}");
        if (killer is not { } truth) return new(SightOutcome.Unknown, index, null);
        double d = Distance(e, truth);
        if (d > Epsilon)
            return Leak($"entry at ({e.X:R},{e.Z:R}) is {d:F3} m from the killer's own position ({truth.X:R},{truth.Z:R}) at that tick " +
                $"(epsilon {Epsilon} m)" + (previous is { } p0 && Distance(e, p0) <= Epsilon ? $"; it equals the killer's position at tick={world.Tick - 1} (stale)" : ""));
        double turn = Math.Abs(Math.IEEERemainder((double)e.Yaw - truth.Yaw, 2 * Math.PI));
        if (turn > YawEpsilon) return Leak($"Yaw={e.Yaw:R} but the killer's own Yaw is {truth.Yaw:R}");
        if (e.Status != truth.Status) return Leak($"Status={e.Status} but the killer's own Status is {truth.Status}");
        double margin = LineOfSight.Margin(self.X, self.Z, truth.X, truth.Z, walls);
        if (margin < -LineOfSight.Band)
            return Leak($"line of sight from the recipient at ({self.X:R},{self.Z:R}) to the killer at ({truth.X:R},{truth.Z:R}) is blocked " +
                $"(penetrates a wall by {-margin:F3} m)");
        return new(margin > LineOfSight.Band ? SightOutcome.Visible : SightOutcome.Ambiguous, index, null);
    }

    private static double Distance(PlayerState a, PlayerState b)
    {
        double dx = (double)a.X - b.X, dz = (double)a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}

public sealed class SightCounts
{
    private readonly int[] counts = new int[Enum.GetValues<SightOutcome>().Length];

    public int this[SightOutcome outcome] => counts[(int)outcome];
    public int Visible => this[SightOutcome.Visible];
    public int BlockedAbsent => this[SightOutcome.BlockedAbsent];
    public int Total => counts.Sum();

    public void Add(SightOutcome outcome) => counts[(int)outcome]++;

    // Never uses the word "skipped" (that word is reserved for unknown-packet evidence).
    // Never uses the word "skipped" (that word is reserved for unknown-packet evidence).
    public override string ToString() =>
        $"{Visible} legal line-of-sight killer entries, {BlockedAbsent} blocked-and-absent, {this[SightOutcome.Ambiguous]} ambiguous " +
        $"within {LineOfSight.Band} m of a wall (not judged), {this[SightOutcome.Unknown]} undecided (without own entry; a survivor tick " +
        $"the killer never received fails), {this[SightOutcome.ClearAbsent]} clear-and-absent, " +
        $"{this[SightOutcome.Ineligible]} spectator worlds without the entry";
}

// U3d a3: per-stream WorldState tick integrity inside a judged window: strictly increasing, gap <= 2 (as H2 requires). The
// first violation is kept; the check that owns the stream fails with it once its window is complete.
public sealed class TickSequence(string stream)
{
    private readonly object sync = new();
    private uint? first, last;
    private int count;

    public string? Violation { get; private set; }

    public void Add(uint tick)
    {
        lock (sync)
        {
            if (last is { } previous && (tick <= previous || tick - previous > 2))
                Violation ??= $"tick sequence violated in {stream} stream: {previous} -> {tick} (WorldState ticks must increase by 1 or 2)";
            first ??= tick;
            last = tick;
            count++;
        }
    }

    public uint? Last { get { lock (sync) return last; } }

    public override string ToString() { lock (sync) return count == 0 ? $"{stream}: none" : $"{stream} {first}..{last} ({count})"; }

    public static void Require(params TickSequence[] streams)
    {
        foreach (var stream in streams)
            if (stream.Violation is { } violation) throw new CheckFailure(violation + ".");
    }
}

// Joins survivor-bound WorldStates with the killer's own entry (from the harness killer client's stream) by Tick, in any
// arrival order. Leaks that need no killer position (wrong Id/Role, fields, spectator, duplicates) fail at once; the rest
// is decided when the killer tick arrives.
// U3d a3 contract clarification: a correct server sends every WorldState tick to every human, so every survivor world in a
// judged window must join. There is no tolerance: Finish() fails any survivor world whose killer tick never arrived, with or
// without a killer entry. The caller reads the killer stream until it covers the window's last survivor tick (AwaitingKiller)
// and seeds killer ticks from before the window (SeedKiller, WireClient.RecentWorlds).
public sealed class SightJoin(int killerId)
{
    private readonly object sync = new();
    private readonly Dictionary<uint, PlayerState> killer = new();
    private readonly Dictionary<uint, List<(int Recipient, S2C_WorldState World)>> pending = new();
    private uint? lastKiller;
    private readonly HashSet<uint> withoutOwn = new();

    public SightCounts Counts { get; } = new();

    // Killer WorldStates retained from before the window (no order requirement; duplicates of later reads are harmless).
    public void SeedKiller(S2C_WorldState world) => AddKiller(world);

    public void AddKiller(S2C_WorldState world)
    {
        lock (sync)
        {
            if (lastKiller == null || world.Tick > lastKiller) lastKiller = world.Tick;
            if (KillerSight.Own(world, killerId) is not { } own)
            {
                withoutOwn.Add(world.Tick);
                return;
            }
            killer[world.Tick] = own;
            if (pending.Remove(world.Tick, out var frames))
                foreach (var (recipient, survivorWorld) in frames) Decide(recipient, survivorWorld);
        }
    }

    // Returns the verdict when decided now, or null while the killer tick is missing.
    public SightVerdict? AddSurvivor(int recipientId, S2C_WorldState world)
    {
        lock (sync)
        {
            if (killer.ContainsKey(world.Tick)) return Decide(recipientId, world);
            var early = KillerSight.Judge(world, recipientId, killerId, null);
            if (early.Leak != null) throw new CheckFailure(early.Leak + ".");
            if (!pending.TryGetValue(world.Tick, out var frames)) pending[world.Tick] = frames = new();
            frames.Add((recipientId, world));
            return null;
        }
    }

    // True while a survivor world waits for a killer tick newer than every killer tick read so far.
    public bool AwaitingKiller
    {
        get { lock (sync) return pending.Count > 0 && (lastKiller == null || pending.Keys.Max() > lastKiller); }
    }

    // Completes the window: every survivor world must have joined. A world still waiting fails (one with a killer entry first).
    public void Finish()
    {
        lock (sync)
        {
            if (pending.Count == 0) return;
            var waiting = pending.OrderBy(p => p.Key).SelectMany(p => p.Value.Select(f => (Tick: p.Key, f.Recipient, f.World))).ToList();
            var (tick, recipient, world) = waiting.FirstOrDefault(w => KillerSight.HasKillerEntry(w.World, killerId)) is { World: not null } leak
                ? leak : waiting[0];
            string what = withoutOwn.Contains(tick)
                ? $"survivor tick {tick} cannot be joined: the killer's WorldState tick={tick} has no unique own entry (its real Id with Role " +
                  $"Killer), so survivor {recipient}'s WorldState there has no ground truth ({waiting.Count} unjoined survivor world(s))"
                : $"survivor tick {tick} never delivered to the killer: survivor {recipient}'s WorldState tick={tick} has no killer " +
                  $"WorldState with that tick (the killer's own stream never carried it; latest killer tick {lastKiller?.ToString() ?? "none"}; " +
                  $"{waiting.Count} such survivor world(s); a correct server sends every tick to every human)";
            throw new CheckFailure(KillerSight.HasKillerEntry(world, killerId)
                ? $"Killer {killerId} leaked to survivor {recipient} at tick={tick}: {what}." : $"{what}.");
        }
    }

    // Pure re-judgement with every killer tick known so far (no counting, no throwing).
    public SightVerdict Judge(int recipientId, S2C_WorldState world)
    {
        lock (sync)
            return KillerSight.Judge(world, recipientId, killerId, Truth(world.Tick), Truth(world.Tick - 1));
    }

    private PlayerState? Truth(uint tick) => killer.TryGetValue(tick, out var p) ? p : null;

    private SightVerdict Decide(int recipientId, S2C_WorldState world)
    {
        var verdict = KillerSight.Judge(world, recipientId, killerId, Truth(world.Tick), Truth(world.Tick - 1));
        if (verdict.Leak != null) throw new CheckFailure(verdict.Leak + ".");
        Counts.Add(verdict.Outcome);
        return verdict;
    }
}
