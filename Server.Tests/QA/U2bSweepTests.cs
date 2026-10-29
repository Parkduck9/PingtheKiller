using System.Buffers.Binary;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA;

// Long seeded sweeps for U2b: leak detection on every encoded frame, decoy indistinguishability, the decoy Id-tracking
// measurement (QA brief task 4), 600 s stability, thread races and growth. Everything is deterministic except the
// thread-race test, which only asserts "no fault".
public sealed class U2bSweepTests
{
    private readonly ITestOutputHelper _out;
    public U2bSweepTests(ITestOutputHelper output) { _out = output; }

    private static readonly FixedMap Map = new();

    [Fact]
    public void LeakDetector_SelfCheck_FindsAPlantedCoordinatePair_AndNothingInACleanFrame()
    {
        var w = new S2C_WorldState { Tick = 5, TimeLeft = 123.456f };
        w.Players.Add(new PlayerState { Id = 3, Role = 0, X = 17.123f, Z = 22.456f, Yaw = 1.5f, Flags = 1 });
        byte[] clean = PacketCodec.Encode(w);
        Assert.False(ContainsPair(clean, 33.333f, 41.777f));
        Assert.False(QaWire.ContainsFloat(clean, 33.333f));
        w.Players.Add(new PlayerState { Id = 9, Role = 1, X = 33.333f, Z = 41.777f });
        byte[] leaked = PacketCodec.Encode(w);
        Assert.True(ContainsPair(leaked, 33.333f, 41.777f));
        Assert.True(QaWire.ContainsFloat(leaked, 33.333f) && QaWire.ContainsFloat(leaked, 41.777f));
        Assert.True(ContainsPair(clean, 17.123f, 22.456f));
    }

    // ------------------------------------------------------------------ the sweep

    private static (float X, float Z) Pair(PlayerState p) => (p.X, p.Z);
    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
    private static bool ContainsPair(byte[] frame, float x, float z)
    {
        Span<byte> needle = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(needle, x);
        BinaryPrimitives.WriteSingleLittleEndian(needle.Slice(4), z);
        return frame.AsSpan().IndexOf(needle) >= 0;
    }

    // U2c migration. The attempt-2 sweep identified killer-frame entries by Id, tracked Ids across frames (the F3 decoy
    // Id-tracking measurement) and required ascending Ids. All of that is gone with the anonymous killer view. What the sweep
    // protects is unchanged: no true position of a hidden survivor, no dead survivor, no survivor-only field and no private
    // value may reach a killer-bound frame; Signal and Lagging survivors are shown faithfully. Entries are now matched to
    // survivors with the server's ground-truth labels (GameServer.KillerViewSources, index-aligned with the frame), and the
    // byte-level search for hidden positions is unchanged.
    private sealed class Sweep
    {
        internal SweepStats St = new();
        internal byte KillerSkill;
        internal int FramesAligned;
        private readonly Dictionary<int, (float X, float Z, int Until)> _dummies = new();
        private readonly Dictionary<int, (float X, float Z, int Tick)> _lastReal = new();

        internal void ResetMatch() { _dummies.Clear(); _lastReal.Clear(); }

        // Processes one tick's worth of packets for both humans. Returns true when a GameResult was seen.
        internal bool Tick(int tick, List<IPacket> kp, List<IPacket> sp, int kId, int sId, ref bool resultK, ref bool resultS,
            IReadOnlyList<GameServer.KillerViewSource> src)
        {
            string ctx = " [tick " + tick + "]";
            var kw = kp.OfType<S2C_WorldState>().LastOrDefault();
            var sw = sp.OfType<S2C_WorldState>().LastOrDefault();
            foreach (var p in kp.OfType<S2C_GameResult>()) { if (resultK) St.Violation("second GameResult for killer" + ctx); resultK = true; }
            foreach (var p in sp.OfType<S2C_GameResult>()) { if (resultS) St.Violation("second GameResult for survivor" + ctx); resultS = true; }
            if (kw == null || sw == null) return resultK || resultS;
            if (kw.Tick != sw.Tick) St.Violation("frames from different ticks" + ctx);
            St.KillerFrames++;
            St.SurvivorFrames++;
            var kself = kw.Players.FirstOrDefault(p => p.Id == kId);
            if (kself.Id != kId) { St.Violation("killer missing from its own frame" + ctx); return resultK || resultS; }

            // ---- dummy cast bookkeeping, from survivor-bound effects (survivors see all survivor skills)
            foreach (var e in sp.OfType<S2C_SkillEffect>())
            {
                if (e.CasterId == kId)
                {
                    bool zero = e.X == 0 && e.Z == 0;
                    bool exit = e.X == Map.Exit.X && e.Z == Map.Exit.Z;
                    if (!zero && !exit) St.Violation($"survivor-bound killer effect {e.SkillId} carries ({e.X},{e.Z})" + ctx);
                }
                else if (e.SkillId == SkillIds.DummyPacket) { _dummies[e.CasterId] = (e.X, e.Z, tick + 85); /* 3 s dummy + 1 s afterimage tail + slack */ St.SurvivorSkillsCast.Add(e.SkillId); }
                else St.SurvivorSkillsCast.Add(e.SkillId);
            }
            foreach (var e in kp.OfType<S2C_SkillEffect>())
            {
                St.KillerEffectsSeen.Add(e.SkillId);
                if (e.CasterId != 0 && e.CasterId != kId) St.Violation($"killer-bound SkillEffect carries survivor id {e.CasterId}" + ctx);
            }
            foreach (var id in _dummies.Where(d => d.Value.Until < tick).Select(d => d.Key).ToList()) _dummies.Remove(id);

            // ---- ThreatLevel comes from the true killer distance (the only thing a survivor may learn about the killer)
            var me = sw.Players.FirstOrDefault(p => p.Id == sId);
            if (me.Id == sId)
            {
                float d = QaRig.Dist(me.X, me.Z, kself.X, kself.Z);
                bool boundary = Math.Abs(d - 18f) < 1e-3f || Math.Abs(d - 12f) < 1e-3f || Math.Abs(d - 6f) < 1e-3f;
                if (!boundary && sw.ThreatLevel != GameServer.ThreatForDistance(d)) St.Violation($"ThreatLevel {sw.ThreatLevel} != band of distance {d}" + ctx);
            }
            // ---- A. survivor-bound frames never carry the killer
            var legit = new HashSet<int>();
            foreach (var p in sw.Players) { legit.Add(Bits(p.X)); legit.Add(Bits(p.Z)); legit.Add(Bits(p.Yaw)); legit.Add(Bits(p.StatusTimer)); }
            foreach (var t in sw.Terminals) { legit.Add(Bits(t.X)); legit.Add(Bits(t.Z)); }
            legit.Add(Bits(sw.TimeLeft)); legit.Add(Bits(Map.Exit.X)); legit.Add(Bits(Map.Exit.Z));
            foreach (var pkt in sp)
            {
                byte[] bytes = PacketCodec.Encode(pkt);
                if (ContainsPair(bytes, kself.X, kself.Z)) St.Violation("killer (X,Z) pair in survivor-bound " + pkt.GetType().Name + ctx);
                foreach (float f in new[] { kself.X, kself.Z })
                {
                    if (!QaWire.ContainsFloat(bytes, f)) continue;
                    if (legit.Contains(Bits(f)) || pkt is S2C_SkillEffect se && (Bits(se.Duration) == Bits(f) || se.X == f)) { St.ByteCoincidences++; continue; }
                    St.Violation("killer coordinate " + f + " bytes in survivor-bound " + pkt.GetType().Name + ctx);
                }
                if (pkt is S2C_WorldState w)
                {
                    if (w.Players.Any(p => p.Role == (byte)Role.Killer || p.Id == kId)) St.Violation("killer entry in survivor frame" + ctx);
                    if (w.Players.Count(p => p.Role == (byte)Role.Survivor) != sw.Players.Count) St.Violation("survivor frame is not complete" + ctx);
                }
            }

            // ---- B1. killer-bound frame structure: anonymous, signal-only, capped, quantised terminals
            int killerEntries = 0;
            foreach (var p in kw.Players)
            {
                if (p.Role == (byte)Role.Killer) { killerEntries++; if (p.Id != kId) St.Violation("killer entry with a foreign id" + ctx); continue; }
                if (p.Role != (byte)Role.Survivor || p.Id != 0 || p.Yaw != 0 || p.StatusTimer != 0 || p.CaptureCount != 0 ||
                    p.Flags != (byte)PlayerFlags.Signal || (p.Status != (byte)PlayerStatus.Normal && p.Status != (byte)PlayerStatus.Lagging))
                    St.Violation($"killer-bound survivor entry is not anonymous/Signal-only: id={p.Id} yaw={p.Yaw} timer={p.StatusTimer} cc={p.CaptureCount} flags={p.Flags} status={p.Status}" + ctx);
            }
            if (killerEntries != 1) St.Violation("killer frame has " + killerEntries + " killer entries" + ctx);
            if (kw.Players.Count > Protocol.MaxWorldEntries) St.Violation("killer frame exceeds the entry cap" + ctx);
            foreach (var t in kw.Terminals) if (t.Progress != 0 && t.Progress != 100) St.Violation("killer-bound terminal progress " + t.Progress + ctx);

            var killerBytes = kp.Select(PacketCodec.Encode).ToList();
            var kLegit = new HashSet<int> { Bits(kw.TimeLeft) };
            foreach (var t in kw.Terminals) { kLegit.Add(Bits(t.X)); kLegit.Add(Bits(t.Z)); }
            foreach (var p in kw.Players) { kLegit.Add(Bits(p.StatusTimer)); kLegit.Add(Bits(p.Yaw)); }
            kLegit.Add(Bits(kself.X)); kLegit.Add(Bits(kself.Z)); kLegit.Add(Bits(Map.Exit.X)); kLegit.Add(Bits(Map.Exit.Z));
            foreach (var e in kp.OfType<S2C_SkillEffect>()) { kLegit.Add(Bits(e.X)); kLegit.Add(Bits(e.Z)); kLegit.Add(Bits(e.Duration)); }
            // U2c: the killer's own S2C_SelfState floats (cooldown 5.0 s equals the spawn x coordinate 5.0 bit for bit)
            foreach (var s in kp.OfType<S2C_SelfState>()) { kLegit.Add(Bits(s.SkillCooldown)); kLegit.Add(Bits(s.RescueProgress)); kLegit.Add(Bits(s.InvulnerableLeft)); kLegit.Add(Bits(s.ExecutionGraceLeft)); }

            // ---- B2. ground-truth alignment (index-aligned labels; the match-end tick clears them after the last frame)
            bool aligned = src.Count == kw.Players.Count;
            if (!aligned && !(resultK || resultS)) St.Violation($"killer view labels out of step ({src.Count} vs {kw.Players.Count})" + ctx);
            var shownReal = new Dictionary<int, (PlayerState E, bool After)>();
            if (aligned)
            {
                FramesAligned++;
                for (int i = 0; i < src.Count; i++)
                {
                    var e = kw.Players[i];
                    var s = src[i];
                    if (e.Role == (byte)Role.Killer) { if (s.Phantom || s.Label != kId) St.Violation("killer entry mislabelled" + ctx); continue; }
                    if (s.Afterimage && KillerSkill != SkillIds.PacketAfterimage) St.Violation("afterimage entry although the killer has no Packet Afterimage" + ctx);
                    if (s.Phantom)
                    {
                        St.DecoyEntries++;
                        if (s.Afterimage) St.AfterimageEntries++;
                        if (e.Status != (byte)PlayerStatus.Normal) St.Violation("phantom with Status " + e.Status + ctx);
                        if (!Map.CanStand(e.X, e.Z)) St.Violation($"phantom inside a wall at ({e.X},{e.Z})" + ctx);
                    }
                    else if (!shownReal.TryAdd(s.Label, (e, s.Afterimage))) St.Violation("real survivor " + s.Label + " shown twice" + ctx);
                }
            }
            foreach (var t in sw.Players)
            {
                if (t.Role != (byte)Role.Survivor) continue;
                bool truthSignal = (t.Flags & (byte)PlayerFlags.Signal) != 0;
                bool shown = shownReal.TryGetValue(t.Id, out var sh);
                if (t.Status == (byte)PlayerStatus.Eliminated || t.Status == (byte)PlayerStatus.Escaped)
                {
                    if (aligned && shown) St.Violation($"dead/escaped survivor {t.Id} present in killer frame" + ctx);
                    continue;
                }
                if (t.Status == (byte)PlayerStatus.Lagging)
                {
                    if (aligned && (!shown || sh.After || sh.E.Status != (byte)PlayerStatus.Lagging || sh.E.X != t.X || sh.E.Z != t.Z))
                        St.Violation($"Lagging survivor {t.Id} not shown faithfully to the killer" + ctx);
                    else { St.RealSignalEntries++; _lastReal[t.Id] = (t.X, t.Z, tick); }
                    continue;
                }
                if (truthSignal)
                {
                    if (aligned && (!shown || sh.After || sh.E.Status != 0 || sh.E.X != t.X || sh.E.Z != t.Z))
                        St.Violation($"Signal survivor {t.Id} not shown faithfully (shown={shown})" + ctx);
                    else { St.RealSignalEntries++; _lastReal[t.Id] = (t.X, t.Z, tick); }
                    continue;
                }
                // ---- absent (Silent) survivor
                St.AbsentChecks++;
                bool isAfter = aligned && shown;
                if (isAfter)
                {
                    St.AfterimageEntries++;
                    if (!sh.After) St.Violation($"Silent survivor {t.Id} shown without being an afterimage" + ctx);
                    // an afterimage sits at the last position the killer legitimately saw, for at most 1 s
                    else if (!_lastReal.TryGetValue(t.Id, out var lr) || lr.X != sh.E.X || lr.Z != sh.E.Z || tick - lr.Tick > 22)
                        St.Violation($"afterimage of {t.Id} is not at the last seen position or lasts > 1 s" + ctx);
                }
                // the true position of an absent survivor must not be anywhere in the killer-bound frames
                bool dummyExempt = _dummies.TryGetValue(t.Id, out var dm) && dm.X == t.X && dm.Z == t.Z;       // the Dummy is born at the caster
                bool afterExempt = isAfter && sh.E.X == t.X && sh.E.Z == t.Z;                                  // stationary since the last Signal
                if (dummyExempt || afterExempt) continue;
                foreach (var b in killerBytes)
                {
                    if (ContainsPair(b, t.X, t.Z))
                    {
                        var carriers = new List<string>();
                        for (int q = 0; q < kw.Players.Count; q++)
                            if (kw.Players[q].X == t.X && kw.Players[q].Z == t.Z)
                                carriers.Add(aligned ? $"label={src[q].Label} phantom={src[q].Phantom} afterimage={src[q].Afterimage} role={kw.Players[q].Role}" : "unaligned");
                        St.Violation($"true (X,Z) of silent survivor {t.Id} in a killer-bound frame; carriers: {string.Join(";", carriers)}; dummy={(_dummies.TryGetValue(t.Id, out var dq) ? dq.X + "," + dq.Z + " until " + dq.Until : "none")}" + ctx);
                    }
                    foreach (float f in new[] { t.X, t.Z })
                        if (QaWire.ContainsFloat(b, f))
                        {
                            // a single coordinate legitimately repeats when the caster slid along a wall after casting, an afterimage
                            // keeps one axis, or spawn points share an axis value
                            bool sameAxisAsOwnDecoy = (_dummies.TryGetValue(t.Id, out var dd) && (Bits(dd.X) == Bits(f) || Bits(dd.Z) == Bits(f))) ||
                                (isAfter && (Bits(sh.E.X) == Bits(f) || Bits(sh.E.Z) == Bits(f)));
                            bool otherEntryHasIt = kw.Players.Any(o => Bits(o.X) == Bits(f) || Bits(o.Z) == Bits(f));
                            if (kLegit.Contains(Bits(f)) || sameAxisAsOwnDecoy || otherEntryHasIt) St.ByteCoincidences++;
                            else St.Violation($"coordinate {f} of silent survivor {t.Id} found in a killer-bound frame; truthPos=({t.X},{t.Z})" + ctx);
                        }
                }
            }
            return resultK || resultS;
        }
    }
    private sealed class Driver
    {
        private readonly Random _rng;
        private int _killerTarget, _survivorTarget, _restUntil;
        internal Driver(int seed) { _rng = new Random(seed); _killerTarget = _rng.Next(6); _survivorTarget = _rng.Next(6); }

        internal void Killer(QaPeer k, S2C_WorldState? kw, int kId, byte skill, int tick)
        {
            if (kw == null) return;
            var self = kw.Players.First(p => p.Id == kId);
            float tx = Map.TerminalPositions[_killerTarget].X, tz = Map.TerminalPositions[_killerTarget].Z, best = float.MaxValue;
            foreach (var p in kw.Players)
            {
                if (p.Id == kId || p.Status != 0) continue;
                float d = QaRig.Dist(self.X, self.Z, p.X, p.Z);
                if (d < best) { best = d; tx = p.X; tz = p.Z; }
            }
            if (best == float.MaxValue && (QaRig.Dist(self.X, self.Z, tx, tz) < 1 || tick % 200 == 0)) _killerTarget = _rng.Next(Map.TerminalPositions.Count);
            float dx = tx - self.X, dz = tz - self.Z, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len > 0.01f) k.Move(dx / len, dz / len, MathF.Atan2(dx, dz) * 180f / MathF.PI);
            if (tick % 5 == 0) k.Send(new C2S_UseSkill { SkillId = skill });
            if (tick % 41 == 0) k.Send(new C2S_UseSkill { SkillId = (byte)_rng.Next(256) });
        }

        internal void Survivor(QaPeer s, S2C_WorldState? sw, int sId, byte skill, int tick)
        {
            if (sw == null) return;
            var self = sw.Players.First(p => p.Id == sId);
            if (self.Status == (byte)PlayerStatus.Eliminated || self.Status == (byte)PlayerStatus.Escaped) return;
            if (tick % 7 == 0) s.Send(new C2S_UseSkill { SkillId = skill });
            if (tick < _restUntil || sw.ThreatLevel >= 2 && _rng.Next(3) != 0) { s.Move(0, 0); return; }
            if (_rng.Next(300) == 0) _restUntil = tick + 20 + _rng.Next(60);
            float tx, tz;
            if (sw.ExitOpen == 1 && self.Status == 0) { tx = Map.Exit.X; tz = Map.Exit.Z; }
            else
            {
                var lag = sw.Players.Where(p => p.Id != sId && p.Status == (byte)PlayerStatus.Lagging).OrderBy(p => QaRig.Dist(self.X, self.Z, p.X, p.Z)).FirstOrDefault();
                if (lag.Id != 0 && self.Status == 0) { tx = lag.X; tz = lag.Z; }
                else
                {
                    var t = sw.Terminals.Where(t => t.Progress < 100).OrderBy(t => QaRig.Dist(self.X, self.Z, t.X, t.Z)).Cast<TerminalState?>().FirstOrDefault();
                    if (t == null) { tx = Map.Exit.X; tz = Map.Exit.Z; }
                    else { tx = t.Value.X; tz = t.Value.Z; }
                }
            }
            float dx = tx - self.X, dz = tz - self.Z, len = MathF.Sqrt(dx * dx + dz * dz);
            s.Send(new C2S_Interact { Active = (byte)(len < 1.3f && self.Status == 0 ? 1 : 0) });
            if (len > 1.0f) s.Move(dx / len, dz / len, MathF.Atan2(dx, dz) * 180f / MathF.PI);
            else s.Move(0, 0);
        }
    }

    // One fixture: K (host, killer) + S (human survivor) + 6 bots; up to `matches` consecutive matches.
    private static void RunSweep(int seed, int matches, int maxTicksPerMatch, Sweep sw, List<string> logs)
    {
        using var f = new QaFixture(seed: seed);
        f.Server.Log += m => { lock (logs) logs.Add(m); };
        var killer = f.Join("K");
        var survivor = f.Join("S");
        int kId = killer.Id, sId = survivor.Id;
        killer.Send(new C2S_SetBots { Count = 6 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        var drv = new Driver(seed);
        int tick = 0;
        for (int m = 0; m < matches; m++)
        {
            sw.ResetMatch();
            killer.Send(new C2S_StartMatch());
            f.Tick();
            var kp = QaWire.Take(killer);
            var sp = QaWire.Take(survivor);
            var ks = kp.OfType<S2C_MatchStart>().Last();
            var ss = sp.OfType<S2C_MatchStart>().Last();
            Assert.Equal((byte)Role.Killer, ks.Role);
            sw.St.KillerSkills.Add(ks.SkillId);
            sw.St.Matches++;
            bool rk = false, rs = false;
            tick++;
            sw.KillerSkill = ks.SkillId;
            sw.Tick(tick, kp, sp, kId, sId, ref rk, ref rs, f.Server.KillerViewSources.ToList()); // the start tick already carries WorldState and bot skill casts
            S2C_WorldState? lastK = kp.OfType<S2C_WorldState>().LastOrDefault(), lastS = sp.OfType<S2C_WorldState>().LastOrDefault();
            bool ended = false;
            for (int i = 0; i < maxTicksPerMatch && !ended; i++)
            {
                drv.Killer(killer, lastK, kId, ks.SkillId, i);
                drv.Survivor(survivor, lastS, sId, ss.SkillId, i);
                f.Tick();
                tick++;
                kp = QaWire.Take(killer);
                sp = QaWire.Take(survivor);
                lastK = kp.OfType<S2C_WorldState>().LastOrDefault() ?? lastK;
                lastS = sp.OfType<S2C_WorldState>().LastOrDefault() ?? lastS;
                ended = sw.Tick(tick, kp, sp, kId, sId, ref rk, ref rs, f.Server.KillerViewSources.ToList());
            }
            if (!ended) { sw.St.Violation("match did not end within " + maxTicksPerMatch + " ticks (seed " + seed + ")"); return; }
            string reason = "";
            // drain the tail: result must be exactly one per human; afterwards the lobby is re-broadcast and no WorldState follows
            for (int i = 0; i < 5; i++)
            {
                f.Tick();
                kp = QaWire.Take(killer);
                sp = QaWire.Take(survivor);
                if (kp.OfType<S2C_WorldState>().Any() || sp.OfType<S2C_WorldState>().Any()) sw.St.Violation("WorldState after GameResult");
                if (kp.OfType<S2C_GameResult>().Any() || sp.OfType<S2C_GameResult>().Any()) sw.St.Violation("second GameResult");
            }
            if (!(rk && rs)) sw.St.Violation("a human did not receive the GameResult");
            sw.St.Results[reason + "match"] = sw.St.Results.GetValueOrDefault(reason + "match") + 1;
        }
    }

    [Fact]
    public void LeakSweep_EveryEncodedFrame_AllSkillCombinations_SeveralSeeds()
    {
        var sweep = new Sweep();
        var logs = new List<string>();
        for (int seed = 1; seed <= 14; seed++) RunSweep(seed, 3, 12100, sweep, logs);
        var st = sweep.St;
        _out.WriteLine($"matches={st.Matches} killerFrames={st.KillerFrames} absentChecks={st.AbsentChecks} decoyEntries={st.DecoyEntries} afterimage={st.AfterimageEntries} realSignal={st.RealSignalEntries} byteCoincidences={st.ByteCoincidences}");
        _out.WriteLine("killer skills " + string.Join(",", st.KillerSkills.OrderBy(x => x)) + " survivor effects seen " + string.Join(",", st.SurvivorSkillsCast.OrderBy(x => x)) + " killer effects " + string.Join(",", st.KillerEffectsSeen.OrderBy(x => x)));
        foreach (var v in st.Violations.Take(25)) _out.WriteLine("VIOLATION " + v);
        Assert.True(st.Violations.Count == 0, string.Join(" | ", st.Violations.Take(6)));
        Assert.DoesNotContain(logs, l => l.StartsWith("tick error") || l.StartsWith("send error"));
        Assert.True(st.AbsentChecks > 15000, "absent checks " + st.AbsentChecks);
        Assert.True(st.DecoyEntries > 150, "decoy entries " + st.DecoyEntries);
        Assert.True(st.AfterimageEntries > 20, "afterimage entries " + st.AfterimageEntries);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, st.KillerSkills.OrderBy(x => x));
        Assert.Superset(new HashSet<byte> { SkillIds.DummyPacket, SkillIds.StealthRelay, SkillIds.TrafficOverload, SkillIds.EmergencyRestore, SkillIds.WhiteNoise }, st.SurvivorSkillsCast.ToHashSet());
    }

    // ------------------------------------------------------------------ 600 s stability, 1 human + 7 bots, 10 seeds

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stability_OneHumanSevenBots_TenSeeds_EndsExactlyOnce_NoNaN_NoFault(bool humanIsKiller)
    {
        var reasons = new Dictionary<string, int>();
        for (int seed = 1; seed <= 10; seed++)
        {
            var logs = new List<string>();
            using var f = new QaFixture(seed: seed * 31);
            f.Server.Log += m => { lock (logs) logs.Add(m); };
            var human = f.Join("H");
            human.Send(new C2S_SetBots { Count = 7 });
            human.Send(new C2S_SetRole { RoleChoice = (byte)(humanIsKiller ? RoleChoice.Killer : RoleChoice.Survivor) });
            human.Send(new C2S_StartMatch());
            f.Tick();
            int hid = human.Id;
            int results = 0, ticks = 0;
            float lastTimeLeft = float.MaxValue;
            S2C_GameResult? result = null;
            var rnd = new Random(seed);
            while (ticks < 12100 && results == 0)
            {
                if (ticks % 3 == 0 && !humanIsKiller) human.Move((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
                f.Tick();
                ticks++;
                foreach (var p in QaWire.Take(human))
                {
                    if (p is S2C_GameResult gr) { results++; result = gr; }
                    if (p is S2C_WorldState w)
                    {
                        Assert.True(float.IsFinite(w.TimeLeft) && w.TimeLeft >= 0 && w.TimeLeft <= lastTimeLeft + 1e-3f);
                        lastTimeLeft = w.TimeLeft;
                        Assert.All(w.Players, e => Assert.True(float.IsFinite(e.X) && float.IsFinite(e.Z) && float.IsFinite(e.Yaw) && float.IsFinite(e.StatusTimer) &&
                            e.X > -0.01f && e.X < 60.01f && e.Z > -0.01f && e.Z < 60.01f, $"bad entry {e.Id} ({e.X},{e.Z}) tick {ticks}"));
                        Assert.All(w.Terminals, t => Assert.InRange((int)t.Progress, 0, 100));
                    }
                }
            }
            Assert.Equal(1, results);
            for (int i = 0; i < 40; i++)
            {
                f.Tick();
                Assert.DoesNotContain(QaWire.Take(human), p => p is S2C_GameResult || p is S2C_WorldState);
            }
            Assert.DoesNotContain(logs, l => l.StartsWith("tick error") || l.StartsWith("send error"));
            string key = (GameEndReason)result!.Reason + "/" + (Role)result.WinnerRole;
            reasons[key] = reasons.GetValueOrDefault(key) + 1;
        }
        _out.WriteLine(string.Join(", ", reasons.Select(kv => kv.Key + "=" + kv.Value)));
    }

    [Fact]
    public void Stability_BotsOnlyMatchWithAnIdleHumanSurvivor_AlwaysTerminates_AcrossManySeeds()
    {
        // The idle human never moves, so it never hacks: bots must still finish (open the exit / get captured) or time out.
        for (int seed = 100; seed < 112; seed++)
        {
            using var f = new QaFixture(seed: seed);
            var human = f.Join("H");
            human.Send(new C2S_SetBots { Count = 7 });
            human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
            human.Send(new C2S_StartMatch());
            f.Tick();
            int results = 0;
            for (int i = 0; i < 12100 && results == 0; i++)
            {
                f.Tick();
                results += QaWire.Take(human).OfType<S2C_GameResult>().Count();
            }
            Assert.Equal(1, results);
        }
    }

    // ------------------------------------------------------------------ growth / bounded collections

    [Fact]
    public void Growth_CollectionsStayBounded_AndPerTickAllocationDoesNotGrowWithMatchLength()
    {
        // Sandbox gives a full 600 s match (12000 ticks) with 7 bots and two humans moving.
        using var f = new QaFixture(bots: 5, seed: 77, sandbox: true);
        var killer = f.Join("K");
        var survivor = f.Join("S");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        var rnd = new Random(5);
        long Window(int ticks)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ticks; i++)
            {
                if (i % 3 == 0) { killer.Move((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1); survivor.Move((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1); }
                f.Tick();
                QaWire.Take(killer);
                QaWire.Take(survivor);
            }
            return (GC.GetAllocatedBytesForCurrentThread() - before) / ticks;
        }
        Window(300); // warm-up
        long early = Window(1000);
        for (int i = 0; i < 9000; i++) { if (i % 3 == 0) killer.Move(1, 0); f.Tick(); QaWire.Take(killer); QaWire.Take(survivor); }
        long late = Window(1000);
        _out.WriteLine($"bytes/tick early={early} late={late}");
        Assert.True(late < early * 1.25 + 2048, $"allocation per tick grew: {early} -> {late}");
        foreach (var field in new[] { "_decoys", "_skillRequests", "_faultLogs", "_expiredScratch", "_escaped" })
            Assert.InRange(Spy.CountOf(f.Server, field), 0, 16);
        Assert.Equal(0, Spy.CountOf(f.Server, "_inputs"));
        Assert.InRange(Spy.CountOf(f.Server, "_connections"), 2, 2);
        foreach (var p in Spy.Players(f.Server))
            Assert.InRange(((System.Collections.ICollection)Spy.Get(p, "InputTimes")).Count, 0, 60);
    }

    [Fact]
    public void Growth_U2bRulesMatch_AllocationPerTickIsFlatAndCollectionsDrainAfterTheMatch()
    {
        using var f = new QaFixture(seed: 6);
        var killer = f.Join("K");
        var survivor = f.Join("S");
        killer.Send(new C2S_SetBots { Count = 6 });
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        var drv = new Driver(6);
        int kId = killer.Id, sId = survivor.Id; // Id/Last drain the connection into Packets
        killer.Drain();
        survivor.Drain();
        var lastK = killer.Packets.OfType<S2C_WorldState>().LastOrDefault();
        var lastS = survivor.Packets.OfType<S2C_WorldState>().LastOrDefault();
        byte ks = killer.Packets.OfType<S2C_MatchStart>().Last().SkillId, ss = survivor.Packets.OfType<S2C_MatchStart>().Last().SkillId;
        killer.Packets.Clear();
        survivor.Packets.Clear();
        var perWindow = new List<long>();
        long acc = 0;
        int n = 0;
        bool ended = false;
        for (int i = 0; i < 12000 && !ended; i++)
        {
            drv.Killer(killer, lastK, kId, ks, i);
            drv.Survivor(survivor, lastS, sId, ss, i);
            long b = GC.GetAllocatedBytesForCurrentThread();
            f.Tick();
            acc += GC.GetAllocatedBytesForCurrentThread() - b;
            var kp = QaWire.Take(killer);
            var sp = QaWire.Take(survivor);
            lastK = kp.OfType<S2C_WorldState>().LastOrDefault() ?? lastK;
            lastS = sp.OfType<S2C_WorldState>().LastOrDefault() ?? lastS;
            ended = kp.OfType<S2C_GameResult>().Any();
            if (++n % 250 == 0) { perWindow.Add(acc / 250); acc = 0; }
        }
        _out.WriteLine("bytes/tick per 250-tick window: " + string.Join(" ", perWindow) + " ticks=" + n);
        if (perWindow.Count >= 4)
            Assert.True(perWindow.Skip(perWindow.Count / 2).Average() < perWindow.Take(perWindow.Count / 2).Average() * 1.3 + 4096, "allocation grows with match length");
        f.Tick();
        foreach (var field in new[] { "_decoys", "_skillRequests", "_escaped" }) Assert.InRange(Spy.CountOf(f.Server, field), 0, 16);
    }


    // ------------------------------------------------------------------ the public match seed vs hidden randomness

    // U2c migration. The attempt-3 attack replayed System.Random(wireSeed) the way the old server consumed its stream and
    // compared predicted White Noise points with the observed decoys. The server now draws from xoshiro256** streams
    // (PrivateRandom), White Noise points are staggered walkers and nothing is shown by Id, so a point-by-point prediction no
    // longer exists. The invariant the three tests protected is "no hidden server draw is a function of MatchStart.Seed";
    // it is now tested one level lower and stronger: the server's actual random streams are searched for, at any offset,
    // inside streams generated from the wire seed (and simple functions of it) by the server's own generator.
    [Fact]
    public void Seed_AttackControl_WhenTheHookMakesPrivateSeedEqualTheWireSeed_TheStreamReplayStillFindsEveryStream()
    {
        // Positive control: proves the replay code is correct. Only the test hooks (private seed == wire seed) recreate the
        // old coupling; the production path cannot be configured this way.
        using var r = new QaRig(3, seed: 424242, matchSeed: 424242);
        Assert.Equal(424242, r.K.Last<S2C_MatchStart>().Seed);
        r.Tick(5);
        string[] fields = { "_random", "_decoyRandom", "_shuffleRandom" };
        for (int stream = 0; stream < 3; stream++)
        {
            var observed = SeedAttack.Draw(Spy.Server<object>(r.Server, fields[stream]), 8);
            Assert.True(SeedAttack.Contains(424242, stream, observed, 20000), "replay must find " + fields[stream]);
            // ... and only with the right stream index
            Assert.False(SeedAttack.Contains(424242, stream + 3, observed, 20000));
        }
    }

    [Fact]
    public void Seed_FixedInAttempt3_ReplayingTheWireSeedNeverFindsAnyPrivateStream_ManyServers()
    {
        // Production seeding (no hooks except the cosmetic wire seed, fixed here so the attacker's input is known).
        int checks = 0;
        for (int i = 0; i < 12; i++)
        {
            int wire = 424242 + i * 7919;
            using var r = new QaRig(3, matchSeed: wire, csprng: true);
            Assert.Equal(wire, r.K.Last<S2C_MatchStart>().Seed);
            r.Tick(5);
            foreach (string field in new[] { "_random", "_decoyRandom", "_shuffleRandom" })
            {
                var observed = SeedAttack.Draw(Spy.Server<object>(r.Server, field), 8);
                foreach (int candidate in SeedAttack.Derived(wire))
                    for (int stream = 0; stream < 6; stream++)
                    {
                        Assert.False(SeedAttack.Contains(candidate, stream, observed, 4000), $"{field} is a replay of the wire seed {wire} (candidate {candidate}, stream {stream})");
                        checks++;
                    }
            }
        }
        _out.WriteLine(checks + " stream/candidate pairs checked, no hit");
    }

    [Fact]
    public void Seed_FixedInAttempt3_NoPrivateStreamIsAReplayOfTheWireSeedOnSecondAndThirdMatch()
    {
        // Same search in match 2 and 3 of one server (the old code reseeded from the same constant seed every match).
        using var r = new QaRig(3, matchSeed: 31337, csprng: true);
        for (int match = 1; match <= 3; match++)
        {
            if (match > 1) r.NextMatch();
            Assert.Equal(31337, r.K.Last<S2C_MatchStart>().Seed);
            r.Tick(5);
            foreach (string field in new[] { "_random", "_decoyRandom", "_shuffleRandom" })
            {
                var observed = SeedAttack.Draw(Spy.Server<object>(r.Server, field), 8);
                foreach (int candidate in SeedAttack.Derived(31337))
                    for (int stream = 0; stream < 6; stream++)
                        Assert.False(SeedAttack.Contains(candidate, stream, observed, 4000), $"{field} replays the wire seed in match {match}");
            }
        }
    }
    [Theory]
    [InlineData(SkillIds.DummyPacket)]
    [InlineData(SkillIds.StealthRelay)]
    [InlineData(SkillIds.TrafficOverload)]
    public void SurvivorSkills_CooldownIsFifteenSeconds(byte skill)
    {
        using var r = new QaRig(1);
        r.SetSkill(r.S[0], skill);
        r.Place(r.S[0], 30, 30);
        r.S[0].Drain();
        int Count() { r.S[0].Drain(); return r.S[0].Packets.OfType<S2C_SkillEffect>().Count(); }
        r.Use(r.S[0], skill);
        r.Tick();
        int c0 = r.Ticks, e0 = Count();
        Assert.Equal(1, e0);
        int gap = -1;
        for (int i = 0; i < 400 && gap < 0; i++)
        {
            r.Use(r.S[0], skill);
            r.Tick();
            if (Count() > e0) gap = r.Ticks - c0;
        }
        Assert.InRange(gap, 299, 301);
    }

    // ------------------------------------------------------------------ disconnects during a match

    [Fact]
    public void Disconnect_HumanKillerLeaves_BotTakesOverAfterTenSeconds_MatchContinuesWithoutFault()
    {
        using var r = new QaRig(2, seed: 9);
        r.Place(r.S[0], 30, 10);
        r.Place(r.S[1], 50, 50);
        r.K.Connection.Close();
        r.Tick(150); // inside the 10 s grace: the killer is idle
        Assert.False(r.S[0].Has<S2C_GameResult>());
        var killer = r.P(r.K);
        Assert.False(Spy.Get<bool>(killer, "IsBot"));
        r.Tick(80);
        Assert.True(Spy.Get<bool>(killer, "IsBot"));
        float x0 = Spy.Get<float>(killer, "X"), z0 = Spy.Get<float>(killer, "Z");
        r.Tick(100);
        Assert.True(Math.Abs(Spy.Get<float>(killer, "X") - x0) + Math.Abs(Spy.Get<float>(killer, "Z") - z0) > 3f, "the replacement killer bot does not move");
        Assert.DoesNotContain(r.Logs, l => l.StartsWith("tick error"));
        Assert.True(r.SW.Players.Count == 2); // survivor frames keep flowing with only survivors
    }

    [Fact]
    public void Disconnect_SurvivorLeavesMidHack_ProgressKept_ReconnectWithTokenResumesOnlyAfterANewInteract()
    {
        using var r = new QaRig(2, seed: 4);
        // U2c migration: the killer view quantises progress, and S0 (whose frames r.SW would read) disconnects; S1 is the observer.
        ulong token = r.S[0].Last<S2C_JoinResult>().ReconnectToken;
        int id = r.S[0].Id;
        r.Place(r.S[0], 10, 25.6f);
        r.Interact(r.S[0], true);
        r.Tick(40);
        int progress = r.S[1].World().Terminals[0].Progress;
        Assert.InRange(progress, 19, 21);
        r.S[0].Connection.Close();
        r.Tick(20);
        Assert.Equal(progress, r.S[1].World().Terminals[0].Progress); // nothing hacks without its owner
        var back = r.F.Connect();
        back.Send(new C2S_Join { Nickname = "S0", ReconnectToken = token });
        r.Tick();
        Assert.Equal(id, back.Last<S2C_JoinResult>().PlayerId);
        Assert.True(back.Has<S2C_MatchStart>());
        r.Tick(20);
        Assert.Equal(progress, r.S[1].World().Terminals[0].Progress); // the hold did not survive the disconnect
        back.Send(new C2S_Interact { Active = 1 });
        r.Tick(20);
        Assert.InRange(r.S[1].World().Terminals[0].Progress - progress, 9, 11);
    }
    // ------------------------------------------------------------------ thread races between the loop thread and transport callbacks

    [Fact]
    public void Race_LoopThreadVersusConcurrentClientsJoinSpamAndDisconnect_NoFaultNoDeadlock()
    {
        var logs = new List<string>();
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = 99, BotCount = 2, AutoStart = true });
        server.Log += m => { lock (logs) logs.Add(m); };
        using var transport = new InMemoryTransportAdapter(server);
        server.Start();
        var errors = new List<Exception>();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var threads = new List<Thread>();
        for (int t = 0; t < 6; t++)
        {
            int seed = t;
            var th = new Thread(() =>
            {
                var rnd = new Random(seed);
                try
                {
                    while (DateTime.UtcNow < deadline)
                    {
                        var c = transport.Connect();
                        var peer = new QaPeer(c);
                        if (rnd.Next(4) != 0) peer.Send(new C2S_Join { Nickname = "T" + seed, ReconnectToken = rnd.Next(5) == 0 ? (ulong)rnd.Next(1, 100) : 0 });
                        int steps = rnd.Next(5, 120);
                        for (int i = 0; i < steps && !c.IsClosed; i++)
                        {
                            switch (rnd.Next(9))
                            {
                                case 0: peer.Send(new C2S_Ping { Timestamp = i }); break;
                                case 1: peer.Send(new C2S_Interact { Active = (byte)rnd.Next(2) }); break;
                                case 2: peer.Send(new C2S_UseSkill { SkillId = (byte)rnd.Next(256) }); break;
                                case 3: peer.Send(new C2S_SetRole { RoleChoice = (byte)(rnd.Next(2) == 0 ? 255 : rnd.Next(2)) }); break;
                                case 4: peer.Send(new C2S_SetBots { Count = (byte)rnd.Next(8) }); break;
                                case 5: peer.Send(new C2S_StartMatch()); break;
                                default: peer.Send(new C2S_MoveInput { DirX = (float)rnd.NextDouble() - 0.5f, DirZ = float.NaN * 0 + (float)rnd.NextDouble() - 0.5f, Seq = (uint)i }); break;
                            }
                            if (i % 8 == 0) peer.Drain();
                            if (i % 16 == 0) Thread.Yield();
                        }
                        if (rnd.Next(3) != 0) c.Close();
                        peer.Packets.Clear();
                    }
                }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            }) { IsBackground = true };
            threads.Add(th);
            th.Start();
        }
        foreach (var th in threads) Assert.True(th.Join(15000), "client thread stuck");
        var stop = new Thread(server.Stop) { IsBackground = true };
        stop.Start();
        Assert.True(stop.Join(10000), "Stop() deadlocked");
        Assert.True(errors.Count == 0, string.Join(" | ", errors.Select(e => e.ToString())));
        Assert.DoesNotContain(logs, l => l.StartsWith("tick error"));
    }

    private sealed class InMemoryTransportAdapter : IDisposable
    {
        private readonly InMemoryTransport _t = new();
        internal InMemoryTransportAdapter(GameServer s) { _t.ConnectionAccepted += s.AddConnection; _t.Start(); }
        internal IConnection Connect() => _t.Connect();
        public void Dispose() => _t.Dispose();
    }
}
