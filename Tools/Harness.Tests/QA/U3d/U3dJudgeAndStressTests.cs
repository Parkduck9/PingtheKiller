using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

// Unit-level probes of KillerSight / SightJoin / KillerLeakScan (fast, no sockets).
public sealed class U3dJudgeProbeTests
{
    private const int KillerId = 1, A = 2, B = 3;
    private static readonly PlayerState Killer = new() { Id = KillerId, Role = (byte)Role.Killer, X = 5.25f, Z = 5.5f, Yaw = 0.7f };
    private static readonly PlayerState Entry = new() { Id = KillerId, Role = (byte)Role.Killer, X = Killer.X, Z = Killer.Z, Yaw = Killer.Yaw };
    private static readonly PlayerState InOpenA = new() { Id = A, X = 5.125f, Z = 26.375f };
    private static readonly PlayerState BehindWallB = new() { Id = B, X = 22.5f, Z = 20.25f };

    private static S2C_WorldState W(uint tick, params PlayerState[] players) => new() { Tick = tick, Players = players.ToList() };

    // Per-recipient judgement in the same tick: A sees the killer, B is behind a wall.
    [Fact]
    public void EachRecipientIsJudgedSeparatelyInTheSameTick()
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(10, Killer));
        Assert.Equal(SightOutcome.Visible, join.AddSurvivor(A, W(10, InOpenA, BehindWallB, Entry))!.Value.Outcome);
        Assert.Equal(SightOutcome.BlockedAbsent, join.AddSurvivor(B, W(10, InOpenA, BehindWallB))!.Value.Outcome);
        var leak = Assert.Throws<CheckFailure>(() => join.AddSurvivor(B, W(10, InOpenA, BehindWallB, Entry)));
        Assert.Contains("leaked to survivor 3", leak.Message);
        Assert.Contains("is blocked", leak.Message);
    }

    [Fact]
    public void ARecipientWhoseOwnEntryIsMissingOrDuplicatedCannotBeLegal()
    {
        var join = new SightJoin(KillerId);
        join.AddKiller(W(10, Killer));
        Assert.Contains("own entry is missing", Assert.Throws<CheckFailure>(() => join.AddSurvivor(A, W(10, BehindWallB, Entry))).Message);
        Assert.Contains("own entry is missing", Assert.Throws<CheckFailure>(() => join.AddSurvivor(A, W(10, InOpenA, InOpenA, Entry))).Message);
    }

    // Round 1 pinned this as a gap (undecided, real coordinates allowed by the scan). Since attempt 2: an entry on a tick the
    // killer never delivered, far outside its range, fails at Finish(); and an undecided verdict grants no scan allowance.
    [Fact]
    public void AnEntryOnAnUnjoinableTickFailsAndGetsNoScanAllowance()
    {
        var world = W(77, BehindWallB, Entry);            // blocked, real killer X/Z; the killer stream has no tick 77
        var join = new SightJoin(KillerId);
        join.AddKiller(W(10, Killer));
        Assert.Null(join.AddSurvivor(B, world));          // pending
        var failure = Assert.Throws<CheckFailure>(() => join.Finish());
        Assert.Contains("never carried", failure.Message);
        var verdict = join.Judge(B, world);
        Assert.Equal(-1, verdict.AllowedEntry);           // no allowance for an undecided entry
        var bits = new HashSet<int> { KillerLeakScan.Bits(Entry.X), KillerLeakScan.Bits(Entry.Z) };
        var frame = new ReceivedFrame(world, PacketCodec.Encode(world), 0);
        Assert.NotNull(KillerLeakScan.Excess(frame, bits, new[] { A, B }, legalKillerEntry: verdict.AllowedEntry));
    }

    // The entry's X/Z allowance is per bit pattern: a copy anywhere else in the frame is still an excess, also when X == Z.
    [Fact]
    public void TheEntryAllowanceNeverCoversAnExtraCopyEvenWhenXEqualsZ()
    {
        var diagonal = new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = 7.3125f, Z = 7.3125f };
        var bits = new HashSet<int> { KillerLeakScan.Bits(7.3125f) };
        var legal = W(1, new PlayerState { Id = A, X = 1, Z = 2 }, diagonal);
        Assert.Null(KillerLeakScan.Excess(new ReceivedFrame(legal, PacketCodec.Encode(legal), 0), bits, new[] { A, B }, legalKillerEntry: 1));
        var extra = W(1, new PlayerState { Id = A, X = 1, Z = 2 }, diagonal, new PlayerState { Id = 50, Z = 7.3125f });
        Assert.Contains("3 occurrence(s)", KillerLeakScan.Excess(new ReceivedFrame(extra, PacketCodec.Encode(extra), 0), bits, new[] { A, B }, legalKillerEntry: 1));
        var skill = new S2C_SkillEffect { CasterId = 50, X = 7.3125f, Z = 0 };
        Assert.Contains("S2C_SkillEffect", KillerLeakScan.Excess(new ReceivedFrame(skill, PacketCodec.Encode(skill), 0), bits, new[] { A, B }, legalKillerEntry: 1));
    }

    // Sensitivity check for the geometry fuzz: a harness test against shrunk walls must disagree with the reference a lot, so
    // "0 mismatches" in the fuzz is evidence and not an artefact of a lenient comparison.
    [Fact]
    public void TheFuzzComparisonWouldCatchAWrongWallSet()
    {
        var map = new FixedMap();
        var shrunk = map.Walls.Select(w => new Wall(w.MinX + 0.2f, w.MinZ + 0.2f, w.MaxX - 0.2f, w.MaxZ - 0.2f)).ToList();
        var random = new Random(3);
        int differing = 0;
        for (int n = 0; n < 20_000; n++)
        {
            double R() => RefLos.Snap(-3 + random.NextDouble() * (FixedMap.Size + 6));
            double ax = R(), az = R(), bx = R(), bz = R();
            // On larger buildings uniform long rays often hit another wall too. Probe the erased face strip directly.
            if ((n & 3) == 0)
            {
                var wall = map.Walls[4 + n % (map.Walls.Count - 4)];
                ax = bx = wall.MinX + .125;
                az = (wall.MinZ + wall.MaxZ) * .5 - .25;
                bz = az + .5;
            }
            if (LineOfSight.Classify(ax, az, bx, bz, shrunk).ToString() != RefLos.Classify(ax, az, bx, bz).ToString()) differing++;
        }
        U3dEvidence.Write("los-fuzz-sensitivity", $"shrunk-wall harness disagrees with the reference on {differing}/20000 pairs");
        Assert.True(differing > 300, $"{differing}");
    }
}

// Stress: 120 000 survivor-bound worlds with a contract-correct "server" computed by the QA reference, joined in a random
// order from two threads; then 11 leak types x thousands of instances through the judge.
public sealed class U3dStressTests
{
    private const int KillerId = 1;

    private static float G(Random r, double lo, double hi) => RefLos.Snap(lo + r.NextDouble() * (hi - lo));

    private static PlayerState KillerAt(uint tick, Random r, float x, float z) => new() { Id = KillerId, Role = (byte)Role.Killer, X = x, Z = z, Yaw = (float)(r.NextDouble() * 6 - 3) };

    [Fact]
    public async Task ContractServerWorldsJoinedInRandomOrderGiveExactlyTheReferenceCounts()
    {
        var random = new Random(1030);
        const int Ticks = 40_000;
        var kills = new PlayerState[Ticks + 1];
        var worlds = new List<(int Recipient, S2C_WorldState World)>();
        int expVisible = 0, expBlocked = 0, expAmbiguous = 0, expSpectator = 0;
        for (uint t = 1; t <= Ticks; t++)
        {
            // killer: mostly random open positions, 15 % hugging a wall face / corner
            float kx = G(random, -2, 62), kz = G(random, -2, 62);
            if (random.Next(7) == 0) { var w = RefLos.Boxes[random.Next(RefLos.Boxes.Length)]; kx = RefLos.Snap(w.MinX + random.Next(0, 3) * 0.015625 * (random.Next(2) * 2 - 1)); kz = RefLos.Snap(w.MinZ + random.NextDouble() * (w.MaxZ - w.MinZ)); }
            var killer = KillerAt(t, random, kx, kz);
            kills[t] = killer;
            var all = new List<PlayerState>();
            for (int s = 0; s < 3; s++)
            {
                byte status = random.Next(6) switch { 0 => (byte)PlayerStatus.Lagging, 1 => (byte)PlayerStatus.Escaped, 2 => (byte)PlayerStatus.Eliminated, _ => (byte)PlayerStatus.Normal };
                all.Add(new PlayerState { Id = 2 + s, Status = status, X = G(random, -2, 62), Z = G(random, -2, 62) });
            }
            foreach (var self in all)
            {
                var world = new S2C_WorldState { Tick = t };
                world.Players.AddRange(all.OrderBy(_ => random.Next()));
                bool eligible = self.Status is 0 or 1;
                bool blockedExact = RefLos.ExactBlocked(self.X, self.Z, killer.X, killer.Z);
                double margin = RefLos.Margin(self.X, self.Z, killer.X, killer.Z);
                bool entry = eligible && !blockedExact;
                if (entry)
                {
                    var e = new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = killer.X, Z = killer.Z, Yaw = killer.Yaw };
                    if (random.Next(3) == 0) { e.X += (float)(random.NextDouble() * 0.08 - 0.04) / 2; e.Z += (float)(random.NextDouble() * 0.08 - 0.04) / 2; } // <= 0.028 m
                    world.Players.Insert(random.Next(world.Players.Count + 1), e);
                }
                if (!eligible) expSpectator++;
                else if (Math.Abs(margin) <= RefLos.Band) expAmbiguous++;
                else if (entry) expVisible++;
                else expBlocked++;
                worlds.Add((self.Id, world));
            }
        }
        // Feed in random order from two threads (killer ticks and survivor worlds interleaved arbitrarily).
        var join = new SightJoin(KillerId);
        var events = new List<(bool Killer, int Index)>();
        for (int t = 1; t <= Ticks; t++) events.Add((true, t));
        for (int i = 0; i < worlds.Count; i++) events.Add((false, i));
        events = events.OrderBy(_ => random.Next()).ToList();
        int half = events.Count / 2;
        Exception? failure = null;
        void Feed(IEnumerable<(bool Killer, int Index)> part)
        {
            try
            {
                foreach (var (isKiller, index) in part)
                {
                    if (isKiller) { var w = new S2C_WorldState { Tick = (uint)index }; w.Players.Add(kills[index]); join.AddKiller(w); }
                    else join.AddSurvivor(worlds[index].Recipient, worlds[index].World);
                }
            }
            catch (Exception ex) { failure ??= ex; }
        }
        var t1 = Task.Run(() => Feed(events.Take(half)));
        var t2 = Task.Run(() => Feed(events.Skip(half)));
        await Task.WhenAll(t1, t2);
        Assert.Null(failure);
        join.Finish();
        var c = join.Counts;
        string summary = $"stress {worlds.Count} worlds / {Ticks} ticks, random order, 2 threads: visible={c.Visible} (expected {expVisible}), blocked-absent={c.BlockedAbsent} ({expBlocked}), " +
            $"ambiguous={c[SightOutcome.Ambiguous]} ({expAmbiguous}), ineligible={c[SightOutcome.Ineligible]} ({expSpectator}), unknown={c[SightOutcome.Unknown]}, clear-absent={c[SightOutcome.ClearAbsent]}";
        U3dEvidence.Write("stress-join", summary);
        Assert.Equal(expVisible, c.Visible);
        Assert.Equal(expBlocked, c.BlockedAbsent);
        Assert.Equal(expAmbiguous, c[SightOutcome.Ambiguous]);
        Assert.Equal(expSpectator, c[SightOutcome.Ineligible]);
        Assert.Equal(0, c[SightOutcome.Unknown]);
        Assert.Equal(0, c[SightOutcome.ClearAbsent]);
        Assert.Equal(worlds.Count, c.Total);
    }

    private enum Leak { Blocked, Spectator, Offset, Stale, Flags, StatusTimer, CaptureCount, WrongRoleOrId, Duplicate, Status, Yaw }

    [Fact]
    public void EveryLeakTypeIsFlaggedAndNoLegalWorldIsFlagged()
    {
        var random = new Random(4242);
        var byType = Enum.GetValues<Leak>().ToDictionary(l => l, _ => (Injected: 0, Flagged: 0));
        int legalWorlds = 0, falseFlags = 0, attempts = 0;
        var misses = new List<string>();
        while (byType.Values.Min(v => v.Injected) < 3_000 && attempts++ < 3_000_000)
        {
            var killer = KillerAt(1, random, G(random, 0, 60), G(random, 0, 60));
            var previous = killer with { X = RefLos.Snap(killer.X + 0.15 + random.NextDouble()), Z = killer.Z };
            var self = new PlayerState { Id = 2, X = G(random, 0, 60), Z = G(random, 0, 60) };
            double margin = RefLos.Margin(self.X, self.Z, killer.X, killer.Z);
            var legalEntry = new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = killer.X, Z = killer.Z, Yaw = killer.Yaw };
            var mate = new PlayerState { Id = 3, X = G(random, 0, 60), Z = G(random, 0, 60) };

            S2C_WorldState Build(PlayerState selfState, params PlayerState[] extra)
            {
                var w = new S2C_WorldState { Tick = 1 };
                w.Players.Add(mate);
                w.Players.Add(selfState);
                w.Players.AddRange(extra);
                return w;
            }

            // control: a legal entry (clear) must never be flagged; blocked-and-absent either
            if (margin > 0.3 && random.Next(4) == 0)
            {
                legalWorlds++;
                if (KillerSight.Judge(Build(self, legalEntry), 2, KillerId, killer, previous).Leak != null) { falseFlags++; misses.Add("legal flagged"); }
            }
            if (margin < -0.3 && random.Next(4) == 0)
            {
                legalWorlds++;
                if (KillerSight.Judge(Build(self), 2, KillerId, killer, previous).Leak != null) { falseFlags++; misses.Add("blocked-absent flagged"); }
            }

            var type = (Leak)random.Next(11);
            if (type == Leak.Blocked ? margin >= -0.3 : margin <= 0.3) continue;
            S2C_WorldState world;
            switch (type)
            {
                case Leak.Blocked: world = Build(self, legalEntry); break;
                case Leak.Spectator: world = Build(self with { Status = (byte)(random.Next(2) == 0 ? PlayerStatus.Escaped : PlayerStatus.Eliminated) }, legalEntry); break;
                case Leak.Offset:
                {
                    double angle = random.NextDouble() * Math.PI * 2, d = 0.0501 + random.NextDouble() * 5;
                    world = Build(self, legalEntry with { X = legalEntry.X + (float)(Math.Cos(angle) * d), Z = legalEntry.Z + (float)(Math.Sin(angle) * d) });
                    break;
                }
                case Leak.Stale: world = Build(self, legalEntry with { X = previous.X, Z = previous.Z }); break;
                case Leak.Flags: world = Build(self, legalEntry with { Flags = (byte)random.Next(1, 32) }); break;
                case Leak.StatusTimer: world = Build(self, legalEntry with { StatusTimer = random.Next(3) == 0 ? float.Epsilon : (float)(random.NextDouble() * 60 + 0.001) }); break;
                case Leak.CaptureCount: world = Build(self, legalEntry with { CaptureCount = (byte)random.Next(1, 4) }); break;
                case Leak.WrongRoleOrId:
                    world = random.Next(2) == 0 ? Build(self, legalEntry with { Id = 50 + random.Next(100) }) : Build(self, legalEntry with { Role = (byte)Role.Survivor });
                    break;
                case Leak.Duplicate: world = Build(self, legalEntry, legalEntry); break;
                case Leak.Status: world = Build(self, legalEntry with { Status = (byte)random.Next(1, 4) }); break;
                default: world = Build(self, legalEntry with { Yaw = legalEntry.Yaw + 0.02f + (float)(random.NextDouble() * 2) }); break;
            }
            var (injected, flagged) = byType[type];
            bool flag = KillerSight.Judge(world, 2, KillerId, killer, previous).Leak != null;
            byType[type] = (injected + 1, flagged + (flag ? 1 : 0));
            if (!flag && misses.Count < 10) misses.Add($"{type} not flagged: margin={margin:F2}");
            // Position-independent leaks must also be flagged when the killer's tick is unknown.
            if (type is Leak.Spectator or Leak.Flags or Leak.StatusTimer or Leak.CaptureCount or Leak.WrongRoleOrId or Leak.Duplicate)
                if (KillerSight.Judge(world, 2, KillerId, null).Leak == null && misses.Count < 10) misses.Add($"{type} not flagged without a killer tick");
        }
        string summary = $"leak judge stress: legal worlds={legalWorlds} falseFlags={falseFlags}; " + string.Join(", ", byType.Select(kv => $"{kv.Key}={kv.Value.Flagged}/{kv.Value.Injected}"));
        U3dEvidence.Write("stress-leaks", summary);
        Assert.Empty(misses);
        Assert.Equal(0, falseFlags);
        foreach (var (type, (injected, flagged)) in byType) Assert.True(injected >= 3_000 && flagged == injected, $"{type}: {flagged}/{injected}");
    }
}
