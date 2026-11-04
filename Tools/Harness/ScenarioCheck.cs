using System.Diagnostics;
using System.Numerics;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

// H6 (GDD v2): sight -> hack (answering sync checks) -> exits powered -> switch held 15 s -> exit open -> capture -> retreat
// -> rescue -> both survivors escape -> GameResult, played by three real TCP clients on a dedicated fresh lobby server
// (--bots 0, no --autostart), with a killer-coordinate leak scan.
public static class ScenarioCheck
{
    // Three 45 s terminals co-hacked at 1.8x (25 s each), a 15 s switch and the walks between them.
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);
    public const string NeedsOwnServer = "H6 needs its own fresh server: pass --h6-port";

    public static async Task<string> RunAsync(Options options, CancellationToken ct)
    {
        await using var run = new ScenarioRun(options);
        return await run.RunAsync(ct);
    }
}

// One human client. A single pump is the WireClient's only consumer; it keeps the latest WorldState
// and every frame (GameResult and leak checks) and runs streaming checks before publishing a frame.
public sealed class Actor : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly List<ReceivedFrame> frames = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TimeSpan timeout;
    private TaskCompletionSource changed = NewSignal();
    private ReceivedFrame? world;
    private long worlds, results;
    private Exception? failure;
    private Task pump = Task.CompletedTask;
    private int sequence;
    private int syncChecks, syncPresses, syncGreat, syncGood, syncFail;

    private Actor(string name, WireClient wire, S2C_JoinResult identity, TimeSpan timeout)
    {
        Name = name;
        Wire = wire;
        Identity = identity;
        this.timeout = timeout;
    }

    public string Name { get; }
    public WireClient Wire { get; }
    public S2C_JoinResult Identity { get; }
    public int Id => Identity.PlayerId;
    public float Speed { get; set; } = GameConstants.SurvivorSpeed;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<Actor> JoinAsync(string name, Options options, CancellationToken ct)
    {
        var wire = await WireClient.ConnectAsync(options, ct);
        try { return new Actor(name, wire, await MatchSession.JoinAsync(wire, $"Harness H6 {name}", 0, ct), options.Timeout); }
        catch { await wire.DisposeAsync(); throw; }
    }

    public void Start(Action<Actor, ReceivedFrame> inspect, Action<Exception> onFailure) => pump = PumpAsync(inspect, onFailure);

    private async Task PumpAsync(Action<Actor, ReceivedFrame> inspect, Action<Exception> onFailure)
    {
        await Task.Yield();
        Exception? error = null;
        try
        {
            while (true)
            {
                var frame = await Wire.ReadAsync(stop.Token);
                inspect(this, frame);
                if (frame.Packet is S2C_SyncCheck check) AnswerLater(check);
                if (frame.Packet is S2C_SyncResult syncResult) CountResult(syncResult);
                TaskCompletionSource signal;
                lock (sync)
                {
                    frames.Add(frame);
                    if (frame.Packet is S2C_WorldState) { world = frame; worlds++; }
                    if (frame.Packet is S2C_GameResult) results++;
                    signal = changed;
                    changed = NewSignal();
                }
                signal.TrySetResult();
            }
        }
        catch (Exception ex) when (!stop.IsCancellationRequested)
        {
            // After a GameResult a quiet or closing socket is the normal end of the match stream.
            if (ex is CheckFailure || Interlocked.Read(ref results) == 0)
                error = ex is CheckFailure ? ex : new CheckFailure(ex is OperationCanceledException
                    ? $"{Name}: server sent nothing for {timeout.TotalSeconds:F1} s (no S2C_GameResult received)."
                    : $"{Name}: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception) { }
        lock (sync)
        {
            failure = error ?? new CheckFailure($"{Name}: frame stream ended.");
            changed.TrySetResult();
        }
        if (error != null) onFailure(error);
    }

    // GDD v2 sync check: press when the needle (started on receipt) is in the middle of the zone after the great part.
    // A check the server cancelled (the hack stopped) ignores the press.
    private void AnswerLater(S2C_SyncCheck check)
    {
        Interlocked.Increment(ref syncChecks);
        double fraction = check.ZoneStart + (check.GreatSize + check.ZoneSize) / 2;
        var delay = TimeSpan.FromSeconds(Math.Clamp(fraction * check.Duration, 0, 5));
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, stop.Token);
                await Wire.SendAsync(new C2S_SyncPress { CheckId = check.CheckId }, stop.Token);
                Interlocked.Increment(ref syncPresses);
            }
            catch (Exception) { }
        });
    }

    private void CountResult(S2C_SyncResult result)
    {
        if (result.Result == (byte)SyncResult.Great) Interlocked.Increment(ref syncGreat);
        else if (result.Result == (byte)SyncResult.Good) Interlocked.Increment(ref syncGood);
        else Interlocked.Increment(ref syncFail);
    }

    public string SyncSummary =>
        $"{Name} sync checks {Volatile.Read(ref syncChecks)}, presses {Volatile.Read(ref syncPresses)}, " +
        $"great/good/fail {Volatile.Read(ref syncGreat)}/{Volatile.Read(ref syncGood)}/{Volatile.Read(ref syncFail)}";

    // The killer's own entry is its real Id with Role Killer (U2c: survivor entries in its view carry no real Id).
    public byte? OwnRole { get; set; }

    public PlayerState Self(ReceivedFrame frame) => Measurements.Self(frame, Id, OwnRole);

    public S2C_WorldState Latest
    {
        get { lock (sync) return (S2C_WorldState)(world ?? throw new CheckFailure($"{Name}: no WorldState yet.")).Packet; }
    }

    public PlayerState LatestSelf
    {
        get { lock (sync) return Self(world ?? throw new CheckFailure($"{Name}: no WorldState yet.")); }
    }

    public List<ReceivedFrame> Frames() { lock (sync) return frames.ToList(); }

    // Waits for a WorldState newer than `seen` (a running count). With endOnResult a received GameResult
    // returns (null, seen); otherwise a GameResult before the escape step is a failure.
    public async Task<(ReceivedFrame? World, long Seen)> NextWorldAsync(long seen, CancellationToken ct, bool endOnResult = false)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                if (results > 0)
                {
                    if (endOnResult) return (null, seen);
                    var result = (S2C_GameResult)frames.First(f => f.Packet is S2C_GameResult).Packet;
                    throw new CheckFailure($"{Name} received an early GameResult: winner={result.WinnerRole}, reason={result.Reason}, " +
                        $"escaped=[{string.Join(",", result.EscapedIds)}].");
                }
                if (worlds > seen) return (world, worlds);
                if (failure != null) throw failure;
                wait = changed.Task;
            }
            try { await wait.WaitAsync(timeout, ct); }
            catch (TimeoutException) { throw new CheckFailure($"{Name}: no WorldState within {timeout.TotalSeconds:F1} s."); }
        }
    }

    public async Task<ReceivedFrame> FirstResultAsync(CancellationToken ct)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                if (results > 0) return frames.First(f => f.Packet is S2C_GameResult);
                if (failure != null) throw failure;
                wait = changed.Task;
            }
            try { await wait.WaitAsync(timeout, ct); }
            catch (TimeoutException) { throw new CheckFailure($"{Name}: no S2C_GameResult within {timeout.TotalSeconds:F1} s."); }
        }
    }

    public Task MoveAsync(Vector2 direction, CancellationToken ct) => Wire.SendAsync(new C2S_MoveInput
    {
        DirX = direction.X, DirZ = direction.Y, Seq = (uint)Interlocked.Increment(ref sequence)
    }, ct);

    public Task InteractAsync(bool active, CancellationToken ct) => Wire.SendAsync(new C2S_Interact { Active = (byte)(active ? 1 : 0) }, ct);

    public async Task StopAsync()
    {
        stop.Cancel();
        try { await pump.WaitAsync(timeout); } catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await Wire.DisposeAsync();
        stop.Dispose();
    }
}

internal sealed class ScenarioRun(Options options) : IAsyncDisposable
{
    private const float Reach = 0.6f;          // arrival radius; the scenario requires "within 1.0 m"
    private const float KillerKeepOut = 4f;    // survivors path around the standing killer by this radius
    private static readonly TimeSpan HoldContact = TimeSpan.FromSeconds(3); // > 2 s execution grace + 1 s, so a server re-arming in contact is caught (QA F9)
    private readonly Planner planner = new();
    private readonly FixedMap map = new();
    private int exitIndex = -1;   // the exit whose switch the survivors hold (chosen away from the killer)
    private Vector2 exit;
    private readonly CancellationTokenSource failed = new();
    private readonly HashSet<int> killerBits = new();
    private readonly List<string> evidence = new();
    private readonly SightCounts sightCounts = new();
    private Exception? pumpFailure;
    private CheckFailure? inspectionFailure; // a leak or exit-integrity failure found while inspecting a received frame
    private string step = "setup";
    private Actor? killerActor, aActor, bActor;
    private int[] survivorIds = Array.Empty<int>();
    private SightJoin? sight;
    // U3d a3: per-stream tick integrity over the whole scenario (first WorldState after MatchStart to the end of the drain).
    private readonly Dictionary<int, TickSequence> ticks = new();
    // The Tick of the last WorldState before each human's GameResult (checked equal in the leak scan, after the join).
    private List<(string Name, long Tick)> lastTicks = new();

    private Actor Killer => killerActor!;
    private Actor A => aActor!;
    private Actor B => bActor!;
    private IEnumerable<Actor> All => new[] { killerActor, aActor, bActor }.OfType<Actor>();

    public async Task<string> RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, failed.Token);
        linked.CancelAfter(ScenarioCheck.Budget);
        var clock = Stopwatch.StartNew();
        try
        {
            await SetupAsync(linked.Token);
            await SightAsync(linked.Token);
            await HackAsync(linked.Token);
            await SwitchAsync(linked.Token);
            await CaptureAndRetreatAsync(linked.Token);
            await RescueAsync(linked.Token);
            await EscapeAsync(linked.Token);
            await ResultAsync(linked.Token);
            LeakScan();
            RequireSight();
            return $"{string.Join("; ", evidence)}; total {clock.Elapsed.TotalSeconds:F1} s.";
        }
        catch (Exception ex)
        {
            Exception cause = ex is OperationCanceledException && pumpFailure != null ? pumpFailure : ex;
            string reason = cause is not OperationCanceledException ? cause.Message
                : ct.IsCancellationRequested ? "cancelled."
                : linked.IsCancellationRequested ? $"whole-check budget of {ScenarioCheck.Budget.TotalSeconds:F0} s exceeded."
                : $"server did not answer within the {options.Timeout.TotalSeconds:F1} s per-wait deadline.";
            throw new CheckFailure($"[{step}] {reason}" + (evidence.Count > 0 ? $" Completed: {string.Join("; ", evidence)}." : ""));
        }
    }

    private Action<Actor, ReceivedFrame> Inspecting(Action<Actor, ReceivedFrame> inspect) => (actor, frame) =>
    {
        try { inspect(actor, frame); }
        catch (CheckFailure failure)
        {
            Interlocked.CompareExchange(ref inspectionFailure, failure, null);
            throw;
        }
    };

    private void OnPumpFailure(Exception ex)
    {
        Interlocked.CompareExchange(ref pumpFailure, ex, null);
        try { failed.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task SetupAsync(CancellationToken ct)
    {
        killerActor = await Actor.JoinAsync("killer", options, ct);
        Killer.Speed = GameConstants.KillerSpeed;
        Killer.OwnRole = (byte)Role.Killer;
        var lobby = (S2C_LobbyState)(await Killer.Wire.ReadAsync<S2C_LobbyState>(ct)).Packet;
        Check.Require(lobby.Slots.Any(s => s.PlayerId == Killer.Id && s.IsHost != 0),
            "Harness must be the first human/host of a fresh lobby: start a dedicated server with --bots 0 WITHOUT --autostart for H6.");
        await Killer.Wire.SendAsync(new C2S_SetBots { Count = 0 }, ct);
        await Killer.Wire.ReadAsync<S2C_LobbyState>(ct, p => p.Slots.Count(s => s.IsBot != 0) <= 1);
        aActor = await Actor.JoinAsync("A", options, ct);
        bActor = await Actor.JoinAsync("B", options, ct);
        Check.Require(All.Select(x => x.Id).Distinct().Count() == 3, "Three humans did not receive distinct PlayerIds.");
        survivorIds = new[] { A.Id, B.Id };
        sight = new SightJoin(Killer.Id);
        foreach (var actor in All) ticks[actor.Id] = new TickSequence(actor.Name);
        await Killer.Wire.SendAsync(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer }, ct);
        await Killer.Wire.SendAsync(new C2S_SetBots { Count = 0 }, ct);
        await Killer.Wire.ReadAsync<S2C_LobbyState>(ct, p => p.HostRoleChoice == (byte)RoleChoice.Killer &&
            p.Slots.All(s => s.IsBot == 0) && All.All(x => p.Slots.Any(s => s.PlayerId == x.Id)));
        await Killer.Wire.SendAsync(new C2S_StartMatch(), ct);
        var starts = await Task.WhenAll(All.Select(async x => (S2C_MatchStart)(await x.Wire.ReadAsync<S2C_MatchStart>(ct)).Packet));
        Check.Require(starts[0].Role == (byte)Role.Killer && starts[1].Role == (byte)Role.Survivor && starts[2].Role == (byte)Role.Survivor,
            $"MatchStart roles killer/A/B = {starts[0].Role}/{starts[1].Role}/{starts[2].Role}; expected Killer/Survivor/Survivor.");
        Check.Require(starts.All(s => s.Seed == starts[0].Seed), "Clients received different match seeds.");
        Killer.Start(Inspecting(InspectKiller), OnPumpFailure);
        A.Start(Inspecting(InspectSurvivor), OnPumpFailure);
        B.Start(Inspecting(InspectSurvivor), OnPumpFailure);
        GameConstants.TerminalsFor(2, out int required, out int placed);
        foreach (var actor in All)
        {
            var (frame, _) = await actor.NextWorldAsync(0, ct);
            var world = (S2C_WorldState)frame!.Packet;
            Check.Require(world.TerminalsRequired == required && world.Terminals.Count == placed && world.Exits.Count == map.Exits.Count &&
                world.Exits.TrueForAll(e => e.Flags == 0 && e.SwitchProgress == 0) && world.CollapseLeft < 0 && world.HatchVisible == 0,
                $"{actor.Name} first WorldState: TerminalsRequired={world.TerminalsRequired}, terminals={world.Terminals.Count}, " +
                $"{DescribeExits(world)}, collapse={world.CollapseLeft}, hatch={world.HatchVisible}; expected {required} of {placed}, " +
                $"{map.Exits.Count} unpowered closed exits, no collapse, no hatch (2 survivors).");
        }
        evidence.Add($"setup: 3 humans, 0 bots, killer(host) id={Killer.Id}, A id={A.Id}, B id={B.Id}; TerminalsRequired={required} of {placed}");
    }

    private static bool Has(ExitState exit, ExitFlags flag) => (exit.Flags & (byte)flag) != 0;
    private static bool Powered(S2C_WorldState world) => world.Exits.Count > 0 && world.Exits.TrueForAll(e => Has(e, ExitFlags.Powered));
    private static string DescribeExits(S2C_WorldState world) =>
        $"exits=[{string.Join(",", world.Exits.Select(e => $"{e.Id}:f{e.Flags}/{e.SwitchProgress}"))}]";

    // F2 (GDD v2): on every WorldState of every stream, a powered or open exit needs TerminalsRequired terminals at 100 in that
    // frame (H6 never closes a hatch, the only other source of power), and an open exit needs power.
    private static void RequireExitEarned(Actor actor, S2C_WorldState world)
    {
        if (!world.Exits.Exists(e => e.Flags != 0)) return;
        int complete = world.Terminals.Count(t => t.Progress == 100);
        Check.Require(complete >= world.TerminalsRequired,
            $"{DescribeExits(world)} in {actor.Name}'s WorldState tick={world.Tick} with only {complete} of {world.TerminalsRequired} " +
            $"required terminals at Progress 100 (exit powered without terminals).");
        Check.Require(world.Exits.TrueForAll(e => !Has(e, ExitFlags.Open) || Has(e, ExitFlags.Powered)),
            $"{DescribeExits(world)} in {actor.Name}'s WorldState tick={world.Tick}: an open exit without power.");
    }

    private void InspectKiller(Actor actor, ReceivedFrame frame)
    {
        if (frame.Packet is not S2C_WorldState world) return;
        ticks[actor.Id].Add(world.Tick);
        RequireExitEarned(actor, world);
        // Only the killer's own entry is read from the killer stream (survivor entries may be anonymised later).
        var self = actor.Self(frame);
        Check.Require(self.Role == (byte)Role.Killer, $"Killer's own entry has role {self.Role}.");
        lock (killerBits)
        {
            killerBits.Add(KillerLeakScan.Bits(self.X));
            killerBits.Add(KillerLeakScan.Bits(self.Z));
        }
        // Line-of-sight ground truth for this tick; survivor worlds waiting for it are judged now (a leak throws).
        sight!.AddKiller(world);
    }

    private void InspectSurvivor(Actor actor, ReceivedFrame frame)
    {
        // Contract (U2e): a killer entry only with a clear line of sight and exact fields (SightJoin), plus the byte scan
        // below; nothing about other entries.
        int legal = -1;
        if (frame.Packet is S2C_WorldState world)
        {
            ticks[actor.Id].Add(world.Tick);
            RequireExitEarned(actor, world);
            var verdict = sight!.AddSurvivor(actor.Id, world);
            // Waiting for the killer tick: the final LeakScan byte-scans this frame once its entry is judged.
            if (verdict == null && KillerSight.HasKillerEntry(world, Killer.Id)) return;
            legal = verdict?.AllowedEntry ?? -1;
        }
        string? leak;
        lock (killerBits) leak = KillerLeakScan.Excess(frame, killerBits, survivorIds, legalKillerEntry: legal);
        Check.Require(leak == null, $"{actor.Name}: {leak}");
    }

    private static PlayerState? Entry(S2C_WorldState world, int id)
    {
        foreach (var player in world.Players) if (player.Id == id) return player;
        return null;
    }

    private static string Describe(S2C_WorldState world) =>
        $"tick={world.Tick}, {DescribeExits(world)}, terminals=[{string.Join(",", world.Terminals.Select(t => $"{t.Id}:{t.Progress}"))}], " +
        $"players=[{string.Join(",", world.Players.Select(p => $"{p.Id}:r{p.Role}/s{p.Status}/c{p.CaptureCount}/t{p.StatusTimer:F1}@({p.X:F1},{p.Z:F1})"))}]";

    private static Vector2 Position(PlayerState player) => new(player.X, player.Z);

    private async Task<(Vector2 A, Vector2 B, Vector2 Killer)> PositionsAsync(CancellationToken ct)
    {
        async Task<Vector2> Of(Actor actor)
        {
            var (frame, _) = await actor.NextWorldAsync(0, ct);
            return Position(actor.Self(frame!));
        }
        return (await Of(A), await Of(B), await Of(Killer));
    }

    private static async Task<ReceivedFrame> UntilAsync(Actor actor, Func<S2C_WorldState, bool> done, TimeSpan limit, string what,
        CancellationToken ct, Func<S2C_WorldState, string?>? abort = null)
    {
        var clock = Stopwatch.StartNew();
        long seen = 0;
        while (true)
        {
            (var frame, seen) = await actor.NextWorldAsync(seen, ct);
            var world = (S2C_WorldState)frame!.Packet;
            if (done(world)) return frame;
            if (abort?.Invoke(world) is string reason) throw new CheckFailure($"{what}: {reason} ({actor.Name} view: {Describe(world)})");
            Check.Require(clock.Elapsed <= limit,
                $"{what} not observed by {actor.Name} within {limit.TotalSeconds:F1} s; last {Describe(world)}.");
        }
    }

    // Drives one client toward the end of path at ~20 Hz (one input per own WorldState), steering from the
    // client's own observed position. Returns the frame on which `until` (a check of other streams) held, the arrival frame (after a
    // zero input), or null when a GameResult arrived (endOnResult).
    private static async Task<ReceivedFrame?> WalkAsync(Actor actor, string leg, IReadOnlyList<Vector2> path, float reach,
        CancellationToken ct, Func<bool>? until = null, bool endOnResult = false)
    {
        float length = Planner.Length(path);
        var limit = TimeSpan.FromSeconds(length / actor.Speed * 1.5 + 3);
        var clock = Stopwatch.StartNew();
        var goal = path[^1];
        int next = Math.Min(1, path.Count - 1);
        long seen = 0;
        while (true)
        {
            (var frame, seen) = await actor.NextWorldAsync(seen, ct, endOnResult);
            if (frame == null)
            {
                await actor.MoveAsync(Vector2.Zero, ct);
                return null;
            }
            if (until?.Invoke() == true) return frame;
            var self = actor.Self(frame);
            var position = Position(self);
            if (Vector2.Distance(position, goal) <= reach)
            {
                await actor.MoveAsync(Vector2.Zero, ct);
                return frame;
            }
            Check.Require(clock.Elapsed <= limit,
                $"{actor.Name} stuck on leg '{leg}': at ({self.X:F2},{self.Z:F2}), waypoint ({path[next].X:F2},{path[next].Y:F2}), " +
                $"target ({goal.X:F2},{goal.Y:F2}) after {clock.Elapsed.TotalSeconds:F1} s (limit {limit.TotalSeconds:F1} s for {length:F1} m).");
            while (next < path.Count - 1 && Vector2.Distance(position, path[next]) < 0.35f) next++;
            var delta = path[next] - position;

            // A delayed snapshot must not command full speed through a waypoint and oscillate around it.
            float divisor = MathF.Max(delta.Length(), actor.Speed * .75f);
            await actor.MoveAsync(divisor > 1e-4f ? delta / divisor : Vector2.Zero, ct);
        }
    }

    // U3d: positive line-of-sight coverage before the hack. The killer stands at its spawn; B walks to a spot where a wall
    // blocks its line of sight to the killer (the map is open, so this does not happen reliably on its own) while A stays
    // in the open. B blocked while A is not exposes per-recipient leaks; the capture contact later adds close-range sight.
    private async Task SightAsync(CancellationToken ct)
    {
        step = "sight: B behind a wall";
        var (aPos, bPos, killerPos) = await PositionsAsync(ct);
        float MarginTo(Vector2 p) => (float)LineOfSight.Margin(p.X, p.Y, killerPos.X, killerPos.Y);
        var hide = planner.PathToNearest(bPos, p => Vector2.Distance(p, killerPos) >= 8 && MarginTo(p) <= -0.75f,
            new[] { new Circle(killerPos, KillerKeepOut) });
        Check.Require(hide != null, $"No walkable spot for B whose line of sight to the killer at {killerPos} is blocked by a wall.");
        await WalkAsync(B, "B behind a wall", hide!, 0.3f, ct);
        long seen = 0;
        for (int i = 0; i < 5; i++) (_, seen) = await B.NextWorldAsync(seen, ct); // a few blocked ticks to join
        (aPos, bPos, killerPos) = await PositionsAsync(ct);
        var bSight = LineOfSight.Classify(bPos.X, bPos.Y, killerPos.X, killerPos.Y);
        Check.Require(bSight == Sight.Blocked,
            $"B stopped at ({bPos.X:F2},{bPos.Y:F2}) where its line of sight to the killer is {bSight} (margin {MarginTo(bPos):F2} m), not blocked.");
        evidence.Add($"sight: B stands behind a wall at ({bPos.X:F1},{bPos.Y:F1}), line of sight to the killer at ({killerPos.X:F1},{killerPos.Y:F1}) " +
            $"blocked by {-MarginTo(bPos):F2} m; A at ({aPos.X:F1},{aPos.Y:F1}) {LineOfSight.Classify(aPos.X, aPos.Y, killerPos.X, killerPos.Y)}");
    }

    private async Task HackAsync(CancellationToken ct)
    {
        step = "hack";
        int required = A.Latest.TerminalsRequired;
        for (int k = 0; k < required; k++)
        {
            var (aPos, bPos, killerPos) = await PositionsAsync(ct);
            var avoid = new[] { new Circle(killerPos, KillerKeepOut) };
            TerminalState? chosen = null;
            List<Vector2>? pathA = null, pathB = null;
            float best = float.MaxValue;
            foreach (var terminal in A.Latest.Terminals)
            {
                if (terminal.Progress >= 100) continue;
                var target = new Vector2(terminal.X, terminal.Z);
                var pa = planner.PathTo(aPos, target, avoid);
                var pb = planner.PathTo(bPos, target, avoid);
                if (pa == null || pb == null || Math.Max(Planner.Length(pa), Planner.Length(pb)) >= best) continue;
                (chosen, pathA, pathB, best) = (terminal, pa, pb, Math.Max(Planner.Length(pa), Planner.Length(pb)));
            }
            Check.Require(chosen != null, $"No reachable incomplete terminal for A/B: {Describe(A.Latest)}.");
            var t = chosen!.Value;
            step = $"hack terminal {t.Id}";
            await Task.WhenAll(WalkAsync(A, $"A to terminal {t.Id}", pathA!, Reach, ct), WalkAsync(B, $"B to terminal {t.Id}", pathB!, Reach, ct));
            await Task.WhenAll(A.InteractAsync(true, ct), B.InteractAsync(true, ct));
            var clock = Stopwatch.StartNew();
            var samples = new List<byte>();
            await UntilAsync(A, world =>
            {
                int index = world.Terminals.FindIndex(x => x.Id == t.Id);
                if (index < 0) return false;
                byte progress = world.Terminals[index].Progress;
                if (samples.Count == 0 || samples[^1] != progress) samples.Add(progress);
                return progress >= 100;
            }, TimeSpan.FromSeconds(GameConstants.TerminalHackTimeSeconds * 1.5 + 3), $"terminal {t.Id} Progress == 100 while A and B hold Interact", ct);
            await Task.WhenAll(A.InteractAsync(false, ct), B.InteractAsync(false, ct));
            string shown = samples.Count <= 8 ? string.Join(",", samples)
                : $"{string.Join(",", samples.Take(4))},..,{string.Join(",", samples.TakeLast(3))}";
            evidence.Add($"terminal {t.Id}@({t.X:R},{t.Z:R}) co-hacked by A+B: progress {shown} ({samples.Count} values) in {clock.Elapsed.TotalSeconds:F2} s");
        }
        step = "exits powered";
        foreach (var actor in All)
            await UntilAsync(actor, Powered, options.Timeout, "both exits Powered after the required terminals", ct);
        Check.Require(!All.Any(x => x.Latest.Exits.Exists(e => Has(e, ExitFlags.Open))), $"An exit opened without its switch: {Describe(A.Latest)}.");
        evidence.Add($"exits Powered (not open) seen by killer, A and B; {A.SyncSummary}; {B.SyncSummary}");
    }

    // GDD v2 §3: one survivor holds the switch of the exit farthest from the killer for 15 s; the exit opens and the
    // collapse starts. The other survivor stays where it is.
    private async Task SwitchAsync(CancellationToken ct)
    {
        step = "switch";
        var (aPos, bPos, killerPos) = await PositionsAsync(ct);
        var avoid = new[] { new Circle(killerPos, KillerKeepOut) };
        List<Vector2>? path = null;
        Actor? holder = null;
        float far = -1;
        // B holds the switch while A stays at the last terminal, so the capture and rescue happen away from the exit.
        for (int i = 0; i < map.ExitSwitches.Count; i++)
        {
            var point = new Vector2(map.ExitSwitches[i].X, map.ExitSwitches[i].Z);
            if (Vector2.Distance(point, killerPos) <= far) continue;
            var pb = planner.PathTo(bPos, point, avoid);
            var pa = pb == null ? planner.PathTo(aPos, point, avoid) : null;
            if (pa == null && pb == null) continue;
            (exitIndex, far, holder, path) = (i, Vector2.Distance(point, killerPos), pb != null ? B : A, pb ?? pa);
        }
        Check.Require(holder != null, $"No path to any exit switch avoiding the killer at {killerPos}.");
        exit = new Vector2(map.Exits[exitIndex].X, map.Exits[exitIndex].Z);
        step = $"switch {exitIndex} by {holder!.Name}";
        await WalkAsync(holder, $"{holder.Name} to switch {exitIndex}", path!, Reach, ct);
        await holder.InteractAsync(true, ct);
        var clock = Stopwatch.StartNew();
        var samples = new List<byte>();
        var opened = await UntilAsync(holder, world =>
        {
            var state = world.Exits.Find(e => e.Id == exitIndex);
            if (samples.Count == 0 || samples[^1] != state.SwitchProgress) samples.Add(state.SwitchProgress);
            return Has(state, ExitFlags.Open);
        }, TimeSpan.FromSeconds(GameConstants.ExitSwitchSeconds * 1.5 + 3), $"exit {exitIndex} Open while {holder.Name} holds its switch", ct);
        await holder.InteractAsync(false, ct);
        double held = clock.Elapsed.TotalSeconds;
        var world = (S2C_WorldState)opened.Packet;
        Check.Require(held >= GameConstants.ExitSwitchSeconds - 1.5,
            $"exit {exitIndex} opened after only {held:F1} s of holding (switch takes {GameConstants.ExitSwitchSeconds} s).");
        Check.Require(world.CollapseLeft > GameConstants.CollapseSeconds - 1 && world.CollapseLeft <= GameConstants.CollapseSeconds,
            $"collapse not started when exit {exitIndex} opened: CollapseLeft={world.CollapseLeft}.");
        foreach (var actor in All)
            await UntilAsync(actor, w => w.Exits.Exists(e => e.Id == exitIndex && Has(e, ExitFlags.Open)), options.Timeout,
                $"exit {exitIndex} Open", ct);
        string shown = samples.Count <= 8 ? string.Join(",", samples)
            : $"{string.Join(",", samples.Take(4))},..,{string.Join(",", samples.TakeLast(3))}";
        evidence.Add($"switch {exitIndex}: {holder.Name} held it {held:F1} s (progress {shown}); exit {exitIndex}@({exit.X:R},{exit.Y:R}) Open " +
            $"seen by killer, A and B; collapse {world.CollapseLeft:F1} s");
    }

    private static string? Executed(S2C_WorldState world, int id) =>
        Entry(world, id) is { Status: (byte)PlayerStatus.Eliminated or (byte)PlayerStatus.Escaped } p ? $"player {id} has status {p.Status}" : null;

    private async Task CaptureAndRetreatAsync(CancellationToken ct)
    {
        step = "capture: B leaves A";
        var (aPos, bPos, killerPos) = await PositionsAsync(ct);
        var away = planner.PathToNearest(bPos, p => Vector2.Distance(p, aPos) >= 9 && Vector2.Distance(p, killerPos) >= 8 &&
            Planner.SegmentDistance(p, killerPos, aPos) >= 5, new[] { new Circle(killerPos, KillerKeepOut) });
        Check.Require(away != null, $"No walkable spot >= 9 m from A ({aPos}) for B.");
        await WalkAsync(B, "B away from A", away!, 0.4f, ct);
        (aPos, bPos, killerPos) = await PositionsAsync(ct);
        float separation = Vector2.Distance(aPos, bPos);
        Check.Require(separation >= 8, $"B is only {separation:F2} m from A.");

        step = "capture: killer to A";
        var path = planner.PathTo(killerPos, aPos, new[] { new Circle(bPos, KillerKeepOut) });
        Check.Require(path != null, $"No killer path from {killerPos} to A at {aPos} avoiding B.");
        // A's status is read from A's own (survivor) stream, never from the killer stream.
        bool Captured(S2C_WorldState world) => Entry(world, A.Id) is { } p && p.Status != (byte)PlayerStatus.Normal;
        var clock = Stopwatch.StartNew();
        await WalkAsync(Killer, "killer to A", path!, 0.3f, ct, () => Captured(A.Latest));
        // U2b: the capturing contact never executes (leave-and-re-enter). Stop and stay in contact.
        await Killer.MoveAsync(Vector2.Zero, ct);
        if (!Captured(A.Latest))
            await UntilAsync(A, Captured, options.Timeout, "A captured after the killer reached it (stand-still contact)", ct);
        double reached = clock.Elapsed.TotalSeconds;
        var lagging = A.Self(await UntilAsync(A, world => Entry(world, A.Id)?.Status == (byte)PlayerStatus.Lagging, options.Timeout,
            "A Lagging in survivor view", ct, world => Executed(world, A.Id)));
        Check.Require(lagging.CaptureCount == 1 && lagging.StatusTimer > 0,
            $"A after capture (survivor view): status={lagging.Status}, CaptureCount={lagging.CaptureCount}, StatusTimer={lagging.StatusTimer:R}; expected Lagging/1/>0.");

        step = "capture: 3 s contact hold";
        var hold = Stopwatch.StartNew();
        float nearest = float.MaxValue, farthest = 0;
        int held = 0;
        long seenA = 0;
        var heldA = new List<(uint Tick, Vector2 At)>();
        while (hold.Elapsed < HoldContact)
        {
            (var current, seenA) = await A.NextWorldAsync(seenA, ct);
            var a = A.Self(current!);
            Check.Require(a.Status == (byte)PlayerStatus.Lagging,
                $"A executed during the capturing contact (player {A.Id} has status {a.Status}) {hold.Elapsed.TotalSeconds:F2} s after capture; " +
                "execution must require leaving and re-entering the capture radius.");
            heldA.Add((((S2C_WorldState)current!.Packet).Tick, Position(a)));
        }
        // U3d a3: contact is measured between the killer's and A's positions at the SAME tick, so a lagging killer stream (an old
        // killer position) does not break it (QA F3). The killer stream is read until it covers A's last held tick. An A tick the
        // killer never received is not a contact sample; it fails in the leak scan's join ("never delivered to the killer").
        var killerByTick = await KillerPositionsAsync(heldA[^1].Tick, ct);
        foreach (var (tick, at) in heldA)
        {
            if (!killerByTick.TryGetValue(tick, out var k)) continue;
            float distance = Vector2.Distance(k, at);
            Check.Require(distance <= GameConstants.CaptureRadius, $"Killer lost contact during the hold: {distance:F2} m from A at tick={tick}.");
            (nearest, farthest, held) = (Math.Min(nearest, distance), Math.Max(farthest, distance), held + 1);
        }
        Check.Require(held >= heldA.Count / 2, $"Only {held} of A's {heldA.Count} contact-hold ticks reached the killer stream.");

        step = "retreat";
        var killerAt = Position(Killer.LatestSelf);
        var leave = Stopwatch.StartNew();
        long seen = 0;
        while (true)
        {
            (var current, seen) = await Killer.NextWorldAsync(seen, ct);
            killerAt = Position(Killer.Self(current!));
            if (Vector2.Distance(killerAt, aPos) >= 2.5f) break;
            Check.Require(leave.Elapsed < TimeSpan.FromSeconds(2),
                $"Killer could not leave A's capture zone: at {killerAt}, A at {aPos}.");
            var back = killerAt - aPos;
            await Killer.MoveAsync(back.LengthSquared() > 1e-8f ? Vector2.Normalize(back) : Vector2.UnitX, ct);
        }
        var retreatAvoid = new[] { new Circle(aPos, 2.4f), new Circle(bPos, KillerKeepOut) };
        bool Far(Vector2 p) => Vector2.Distance(p, aPos) >= 9 && Vector2.Distance(p, bPos) >= 6 && Planner.SegmentDistance(p, bPos, aPos) >= 5;
        var retreat = planner.PathToNearest(killerAt, p => Far(p) && Planner.SegmentDistance(p, aPos, exit) >= 6, retreatAvoid)
            ?? planner.PathToNearest(killerAt, Far, retreatAvoid);
        Check.Require(retreat != null, $"No retreat point >= 9 m from A at {aPos}.");
        await WalkAsync(Killer, "killer retreat", retreat!, 0.4f, ct);
        (aPos, bPos, killerPos) = await PositionsAsync(ct);
        // B's state from A's (complete) survivor stream: B stands far from the killer, often out of its own line of sight.
        var bState = Entry((S2C_WorldState)(await UntilAsync(A, _ => true, options.Timeout, "B state", ct)).Packet, B.Id)
            ?? throw new CheckFailure($"B is missing from A's WorldState: {Describe(A.Latest)}.");
        Check.Require(bState.Status == (byte)PlayerStatus.Normal && bState.CaptureCount == 0,
            $"B was touched: status={bState.Status}, CaptureCount={bState.CaptureCount}.");
        evidence.Add($"capture: B {separation:F1} m away and still; killer reached A in {reached:F1} s; survivor view A Lagging, CaptureCount=1, " +
            $"StatusTimer={lagging.StatusTimer:F1} s; killer held contact {HoldContact.TotalSeconds:F0} s ({held} A frames, {nearest:F2}..{farthest:F2} m) with A still Lagging (no execution); B Normal/0; killer then left and stands still at ({killerPos.X:F1},{killerPos.Y:F1}), " +
            $"{Vector2.Distance(killerPos, aPos):F1} m from A");
    }

    private async Task RescueAsync(CancellationToken ct)
    {
        step = "rescue";
        var (aPos, bPos, killerPos) = await PositionsAsync(ct);
        var path = planner.PathTo(bPos, aPos, new[] { new Circle(killerPos, KillerKeepOut) });
        Check.Require(path != null, $"No path for B from {bPos} to A at {aPos} avoiding the killer.");
        await WalkAsync(B, "B to A", path!, Reach, ct);
        var clock = Stopwatch.StartNew();
        await UntilAsync(A, world => Entry(world, A.Id) is { Status: (byte)PlayerStatus.Normal, CaptureCount: 1 },
            TimeSpan.FromSeconds(GameConstants.RescueTimeSeconds + 5), "A rescued (Normal, CaptureCount 1) while B stays within 1.0 m", ct,
            world => Executed(world, A.Id));
        double contact = clock.Elapsed.TotalSeconds;
        var bState = Entry(A.Latest, B.Id);
        Check.Require(bState is { Status: (byte)PlayerStatus.Normal, CaptureCount: 0 }, $"B changed during rescue: {Describe(A.Latest)}.");
        float killerToA = Vector2.Distance(Position(Killer.LatestSelf), Position(A.Self(await UntilAsync(A, _ => true, options.Timeout, "A state", ct))));
        Check.Require(killerToA > GameConstants.RescueRadius + 1, $"Killer re-entered A's vicinity during the rescue: {killerToA:F2} m.");
        evidence.Add($"rescue: B within {Reach:F1} m of A, A Normal with CaptureCount=1 after {contact:F2} s, killer still {killerToA:F1} m away");
    }

    private bool HasEscaped(Actor actor) => Entry(actor.Latest, actor.Id)?.Status == (byte)PlayerStatus.Escaped;

    // GDD v2 §1: every survivor escapes on its own; the match ends when the last one is out. B goes first, then A.
    private async Task EscapeAsync(CancellationToken ct)
    {
        var (aPos, bPos, killerPos) = await PositionsAsync(ct);
        var avoid = new[] { new Circle(killerPos, KillerKeepOut) };
        var pathB = planner.PathTo(bPos, exit, avoid);
        Check.Require(pathB != null, $"No path for B to the exit at {exit} avoiding the killer.");
        step = "escape by B";
        await WalkAsync(B, "B to exit", pathB!, Reach, ct, () => HasEscaped(B));
        if (!HasEscaped(B)) await UntilAsync(B, w => Entry(w, B.Id)?.Status == (byte)PlayerStatus.Escaped, options.Timeout, "B Escaped", ct);
        // B's own first Escaped frame carries the escape position and A's status at that tick (a spectator world is complete).
        var mid = (S2C_WorldState)B.Frames().First(f => f.Packet is S2C_WorldState w && Entry(w, B.Id)?.Status == (byte)PlayerStatus.Escaped).Packet;
        Check.Require(Entry(mid, A.Id)?.Status == (byte)PlayerStatus.Normal, $"A is not in play after B escaped: {Describe(mid)}.");
        Check.Require(A.Frames().All(f => f.Packet is not S2C_GameResult), "The match ended when only B had escaped.");
        float bOut = Vector2.Distance(Position(Entry(mid, B.Id)!.Value), exit);
        // The escaping frame itself carries the escape position (no travel after it), so the margin is small.
        Check.Require(bOut <= GameConstants.ExitRadius + 0.25f,
            $"B Escaped {bOut:F2} m from the exit, outside ExitRadius + 0.25 m ({GameConstants.ExitRadius + 0.25f:F2} m).");
        evidence.Add($"escape: B (id={B.Id}) Escaped {bOut:F2} m from the open exit; the match went on with A Normal");
        await EscapeLastAsync(ct);
    }

    private async Task EscapeLastAsync(CancellationToken ct)
    {
        var (aPos, _, killerPos) = await PositionsAsync(ct);
        var avoid = new[] { new Circle(killerPos, KillerKeepOut) };
        var path = planner.PathTo(aPos, exit, avoid);
        Check.Require(path != null, $"No path for A to the exit at {exit} avoiding the killer.");
        var escaper = A;
        step = "escape by A";
        try { await WalkAsync(escaper, $"{escaper.Name} to exit", path!, Reach, ct, endOnResult: true); }
        catch (Exception ex) when (!ct.IsCancellationRequested || pumpFailure != null)
        {
            // A leak in the escaper's last frames (e.g. the killer entry to a spectator) is the cause, not a missing GameResult.
            if (inspectionFailure != null) throw inspectionFailure;
            // A server that ends the match without a GameResult goes silent once the escaper is in range; the
            // last WorldState is from up to two ticks before the escaping tick.
            var last = escaper.Self(escaper.Frames().Last(f => f.Packet is S2C_WorldState));
            float distance = Vector2.Distance(Position(last), exit);
            if (distance > GameConstants.ExitRadius + 2 * escaper.Speed / GameConstants.TickRate) throw;
            throw new CheckFailure($"No S2C_GameResult although {escaper.Name} was {distance:F2} m from the open exit " +
                $"(ExitRadius {GameConstants.ExitRadius} m): {(ex is OperationCanceledException && pumpFailure != null ? pumpFailure : ex).Message}");
        }
        // F1: positive escape evidence from the escaper's own stream. The last self state observed before the
        // GameResult must be Escaped or within ExitRadius + 0.5 m of the exit.
        var result = await escaper.FirstResultAsync(ct);
        var frames = escaper.Frames();
        int resultIndex = frames.IndexOf(result);
        var before = frames.Take(resultIndex).LastOrDefault(f => f.Packet is S2C_WorldState);
        Check.Require(before != null, $"GameResult before escaper reached the exit: {escaper.Name} saw no WorldState before it.");
        var final = escaper.Self(before!);
        float finalDistance = Vector2.Distance(Position(final), exit);
        Check.Require(final.Status == (byte)PlayerStatus.Escaped || finalDistance <= GameConstants.ExitRadius + 0.5f,
            $"GameResult before escaper reached the exit: {escaper.Name}'s last own state was {finalDistance:F2} m from the exit " +
            $"(limit {GameConstants.ExitRadius + 0.5f:F1} m), status={final.Status}.");
        evidence.Add($"escape: {escaper.Name} (id={escaper.Id}) walked to the exit (last own state {finalDistance:F2} m from it, " +
            $"status={final.Status}) before the GameResult");
    }

    // The killer's own position per tick, read until the killer stream has a WorldState with Tick >= tick (bounded per wait).
    private async Task<Dictionary<uint, Vector2>> KillerPositionsAsync(uint tick, CancellationToken ct)
    {
        long seen = 0;
        while (Killer.Latest.Tick < tick) (_, seen) = await Killer.NextWorldAsync(seen, ct);
        var positions = new Dictionary<uint, Vector2>();
        foreach (var frame in Killer.Frames())
            if (frame.Packet is S2C_WorldState world) positions[world.Tick] = Position(Killer.Self(frame));
        return positions;
    }

    private async Task ResultAsync(CancellationToken ct)
    {
        step = "GameResult";
        var firsts = await Task.WhenAll(All.Select(x => x.FirstResultAsync(ct)));
        long drainEnd = firsts.Min(f => f.ReceivedAt) + Stopwatch.Frequency;
        double remaining = ReceivedFrame.SecondsBetween(Stopwatch.GetTimestamp(), drainEnd);
        if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), ct);
        // U3d a3 coverage: keep the killer's pump running (bounded by --timeout) until it covers every survivor tick received.
        var coverage = Stopwatch.StartNew();
        while (sight!.AwaitingKiller && coverage.Elapsed < options.Timeout && pumpFailure == null) await Task.Delay(20, ct);
        foreach (var actor in All) await actor.StopAsync();
        if (pumpFailure != null) throw pumpFailure;
        // Every human gets every tick, so the last WorldState before each GameResult carries the same Tick (checked in LeakScan).
        lastTicks = All.Select(actor =>
        {
            var frames = actor.Frames();
            int result = frames.FindIndex(f => f.Packet is S2C_GameResult);
            var last = frames.Take(result < 0 ? frames.Count : result).LastOrDefault(f => f.Packet is S2C_WorldState);
            return (actor.Name, Tick: last == null ? -1L : ((S2C_WorldState)last.Packet).Tick);
        }).ToList();
        byte[]? reference = null;
        var lobbies = new List<string>();
        foreach (var actor in All)
        {
            var frames = actor.Frames();
            var results = frames.Where(f => f.Packet is S2C_GameResult).ToList();
            Check.Require(results.Count == 1, $"{actor.Name} received {results.Count} S2C_GameResult frames within 1 s; expected exactly one.");
            var result = (S2C_GameResult)results[0].Packet;
            Check.Require(result.WinnerRole == (byte)Role.Survivor && result.Reason == (byte)GameEndReason.AllResolved,
                $"{actor.Name} GameResult winner={result.WinnerRole}, reason={result.Reason}; expected Survivor({(byte)Role.Survivor}), AllResolved({(byte)GameEndReason.AllResolved}).");
            Check.Require(result.EscapedIds.Order().SequenceEqual(survivorIds.Order()) && result.EliminatedIds.Count == 0,
                $"{actor.Name} GameResult EscapedIds=[{string.Join(",", result.EscapedIds)}], EliminatedIds=[{string.Join(",", result.EliminatedIds)}]; " +
                $"expected escaped [{A.Id},{B.Id}] and nobody eliminated.");
            reference ??= results[0].Raw;
            Check.Require(results[0].Raw.AsSpan().SequenceEqual(reference), $"{actor.Name} GameResult differs from the killer's copy.");
            bool lobby = frames.SkipWhile(f => f.Packet is not S2C_GameResult).Any(f => f.Packet is S2C_LobbyState);
            lobbies.Add($"{actor.Name}={(lobby ? "yes" : "no")}");
        }
        evidence.Add($"GameResult x1 per human (1 s drain), identical: winner=Survivor, reason=AllResolved, escaped=[{A.Id},{B.Id}], eliminated=[]; " +
            $"LobbyState followed (evidence only): {string.Join(",", lobbies)}");
    }

    private void LeakScan()
    {
        step = "killer-coordinate leak scan";
        int frames = 0, worlds = 0, coincidences = 0, unknown = 0;
        long bytes = 0;
        // U3d a3: every survivor world joined a killer tick (no tolerance), every stream's ticks increase by 1 or 2, and every
        // stream ends on the same tick; only then does every entry have a final verdict, so the byte scan runs on final verdicts only.
        sight!.Finish();
        TickSequence.Require(ticks.Values.ToArray());
        Check.Require(lastTicks.Count > 0 && lastTicks.All(t => t.Tick == lastTicks[0].Tick),
            $"the last WorldState before the GameResult differs between streams: {string.Join(", ", lastTicks.Select(t => $"{t.Name} tick={t.Tick}"))}; " +
            "every human must receive every tick.");
        foreach (var actor in new[] { A, B })
        {
            foreach (var frame in actor.Frames())
            {
                frames++;
                bytes += frame.Raw.Length;
                int legal = -1;
                if (frame.Packet is S2C_WorldState world)
                {
                    worlds++;
                    // Final judgement with every killer tick: only a Visible or Ambiguous entry's own X/Z offsets are allowed;
                    // sight.Finish() passed above, so every world here joined a killer tick and the verdict is final.
                    var verdict = sight.Judge(actor.Id, world);
                    Check.Require(verdict.Leak == null, $"{actor.Name}: {verdict.Leak}.");
                    sightCounts.Add(verdict.Outcome);
                    legal = verdict.AllowedEntry;
                }
                string? leak = KillerLeakScan.Excess(frame, killerBits, survivorIds, n => coincidences += n, legalKillerEntry: legal);
                Check.Require(leak == null, $"{actor.Name}: {leak}");
            }
            // Skipped unknown-Id frames (not decodable with this Shared): raw bytes at any alignment, any match fails.
            foreach (var frame in actor.Wire.UnknownFramesSince(long.MinValue))
            {
                unknown++;
                string? leak = KillerLeakScan.UnknownLeak(frame, killerBits);
                Check.Require(leak == null, $"{actor.Name}: {leak}");
            }
        }
        evidence.Add($"leak scan: {killerBits.Count} distinct killer X/Z values vs {frames} survivor-bound frames ({bytes} bytes, any alignment): " +
            $"0 excess, {coincidences} exact match(es) with legitimate fields; {unknown} unknown-Id frames raw-scanned, 0 matches; " +
            $"killer entry judged by line of sight in {worlds} survivor WorldStates: {sightCounts}; every survivor tick joined a killer tick " +
            $"({string.Join("; ", ticks.Values)}; last WorldState tick before GameResult {lastTicks[0].Tick} in every stream)");
    }

    // --expect-los (after U2e): the server must actually show the Killer in line of sight and hide it behind walls.
    private void RequireSight()
    {
        if (!options.ExpectLos) return;
        step = "line of sight (--expect-los)";
        Check.Require(sightCounts.Visible >= 1 && sightCounts.BlockedAbsent >= 1,
            $"--expect-los needs at least one legal line-of-sight killer entry and one blocked-and-absent tick in the survivor worlds; got {sightCounts}.");
        evidence.Add($"--expect-los: {sightCounts.Visible} legal visible entries and {sightCounts.BlockedAbsent} blocked-and-absent ticks observed");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var actor in All) await actor.DisposeAsync();
        failed.Dispose();
    }
}
