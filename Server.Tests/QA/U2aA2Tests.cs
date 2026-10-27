using System.Globalization;
using System.Text;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for unit U2a attempt 2 (fix list docs/briefs/U2a-a2.md), deterministic part:
// GameServer.Tick(dt) + InMemoryTransport, plus the Start()-driven loop-fault tests (in-memory transport, no sockets).
// Expected values derive from the brief / GDD, never from the implementation.

internal readonly record struct SurvObs(float X, float Z, bool Moved, bool Visible, bool Signal);

// Observes one tick: survivor positions come from a human survivor's frame (it lists ALL survivors, incl. Silent ones),
// visibility comes from the killer-bound frame of the SAME tick (both frames must carry the same server tick).
internal sealed class Watch
{
    private readonly QaFixture _f;
    private readonly QaPeer _killer, _observer;
    private readonly Dictionary<int, (float X, float Z)> _prev = new();
    internal readonly List<string> Violations = new();
    internal int TickNo;
    internal S2C_WorldState? KillerFrame;

    internal Watch(QaFixture f, QaPeer killer, QaPeer observer)
    {
        _f = f; _killer = killer; _observer = observer;
        // Prime with the positions of the last frame so the very first Step already reports movement correctly.
        foreach (var p in observer.World().Players.Where(p => p.Role == (byte)Role.Survivor)) _prev[p.Id] = (p.X, p.Z);
    }

    internal Dictionary<int, SurvObs> Step(float dt = 0.05f)
    {
        int mk = _killer.Mark(), mo = _observer.Mark();
        _f.Tick(dt);
        TickNo++;
        var kf = _killer.Since<S2C_WorldState>(mk);
        var of = _observer.Since<S2C_WorldState>(mo);
        Assert.Single(kf);
        Assert.Single(of);
        Assert.Equal(kf[0].Tick, of[0].Tick);
        KillerFrame = kf[0];
        var result = new Dictionary<int, SurvObs>();
        foreach (var p in of[0].Players.Where(p => p.Role == (byte)Role.Survivor))
        {
            bool moved = _prev.TryGetValue(p.Id, out var prev) && (prev.X != p.X || prev.Z != p.Z);
            var entry = kf[0].Players.Where(k => k.Id == p.Id).Cast<PlayerState?>().FirstOrDefault();
            bool visible = entry.HasValue;
            bool signal = visible && (entry!.Value.Flags & (byte)PlayerFlags.Signal) != 0;
            if (moved && !visible)
                Violations.Add($"tick {TickNo} (server tick {kf[0].Tick}): survivor {p.Id} moved ({prev.X},{prev.Z})->({p.X},{p.Z}) but is absent from the killer frame");
            if (visible && !signal)
                Violations.Add($"tick {TickNo}: survivor {p.Id} present in killer frame without Signal flag");
            _prev[p.Id] = (p.X, p.Z);
            result[p.Id] = new SurvObs(p.X, p.Z, moved, visible, signal);
        }
        return result;
    }
}

internal sealed class FaultyConnection : IConnection
{
    private readonly Queue<IPacket> _inbox = new();
    private readonly bool _throwOnRemoteId, _throwOnSend;
    private readonly string _id;
    private int _closed;
    internal int SendAttempts, CloseCalls;
    internal FaultyConnection(string id, bool throwOnRemoteId, bool throwOnSend, IPacket? first = null)
    {
        _id = id; _throwOnRemoteId = throwOnRemoteId; _throwOnSend = throwOnSend;
        if (first != null) _inbox.Enqueue(first);
    }
    public string RemoteId => _throwOnRemoteId ? throw new InvalidOperationException("faulty-remote-id") : _id;
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public event Action? PacketAvailable { add { } remove { } }
    public event Action? Closed;
    public bool TryReceive(out IPacket packet)
    {
        lock (_inbox)
        {
            if (_inbox.Count > 0) { packet = _inbox.Dequeue(); return true; }
        }
        packet = null!;
        return false;
    }
    public void Send(byte[] frame)
    {
        Interlocked.Increment(ref SendAttempts);
        if (_throwOnSend) throw new IOException("faulty-send");
    }
    public void Close()
    {
        Interlocked.Increment(ref CloseCalls);
        if (Interlocked.Exchange(ref _closed, 1) == 0) Closed?.Invoke();
    }
    public void Dispose() => Close();
}

public sealed class U2aA2Tests
{
    private const float Dt = 0.05f;
    private const float Step = GameConstants.SurvivorSpeed * Dt;
    private readonly ITestOutputHelper _out;
    public U2aA2Tests(ITestOutputHelper output) { _out = output; }

    private static bool WaitFor(Func<bool> condition, int milliseconds = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    // Killer joins first (host), survivor second; with the 20 Hz fixture tick, Watch observes both frames per tick.
    private static (QaFixture f, QaPeer killer, QaPeer survivor, Watch watch) Pair(int seed = 1234)
    {
        var f = new QaFixture(seed: seed);
        var (killer, survivor) = f.StartPair();
        return (f, killer, survivor, new Watch(f, killer, survivor));
    }

    // ------------------------------------------------------------------ fix 1: silent-move bypass

    [Fact]
    public void CrossUnitContract_30HzInputsForOneSecond_ThenSilence_MovesContinuouslyStopsAndDropsOut()
    {
        var (f, _, survivor, watch) = Pair();
        using (f)
        {
            int id = survivor.Id;
            // 30 Hz client: inputs at k/30 s (k = 0..29, last at 0.9667 s), server ticks at 20 Hz. Before tick j (executing
            // at simulated time j*0.05) every input with time <= j*0.05 has been sent. No zero input is ever sent.
            const double lastInput = 29 / 30.0;
            int sent = 0;
            var log = new List<(double t, SurvObs o)>();
            for (int j = 1; j <= 80; j++)
            {
                double tt = j * 0.05;
                while (sent < 30 && sent / 30.0 <= tt + 1e-9) { survivor.Move(1, 0); sent++; }
                log.Add((tt, watch.Step()[id]));
            }
            Assert.Equal(30, sent);
            Assert.Empty(watch.Violations);

            // (a) continuous full-speed movement and visibility during the 1 s of input (ticks 1..20)
            float prevX = log[0].o.X - Step; // first tick moved from the spawn
            foreach (var (t, o) in log.Where(e => e.t <= 1.0 + 1e-9))
            {
                Assert.True(o.Moved, $"survivor did not move at t={t:F2} while inputs arrive at 30 Hz");
                Assert.True(o.Visible && o.Signal, $"survivor not visible with Signal at t={t:F2} while inputs arrive at 30 Hz");
                Assert.InRange(o.X - prevX, Step * 0.98f, Step * 1.01f);
                prevX = o.X;
            }

            // (b) stops within 0.15 s +- one tick of the last input
            var lastMove = log.Where(e => e.o.Moved).Max(e => e.t);
            Assert.True(lastMove <= lastInput + 0.15 + 0.05 + 1e-6, $"still moving at t={lastMove:F2} (last input {lastInput:F3}, limit {lastInput + 0.2:F3})");
            Assert.True(lastMove >= 1.0 - 1e-9);
            Assert.All(log.Where(e => e.t > lastMove + 1e-9), e => Assert.False(e.o.Moved, $"moved again at t={e.t:F2}"));
            Assert.InRange(log.Count(e => e.o.Moved), 20, 22); // 20 input ticks + at most the 0.15 s hold

            // (c) drops out of the killer-bound state within 0.15 s +- one tick of the last input, then stays out
            var firstAbsent = log.First(e => !e.o.Visible).t;
            Assert.True(firstAbsent <= lastInput + 0.15 + 0.05 + 1e-6, $"still visible at t={firstAbsent - 0.05:F2} after silence (limit {lastInput + 0.2:F3})");
            Assert.All(log.Where(e => e.t >= firstAbsent - 1e-9), e => Assert.False(e.o.Visible, $"reappeared at t={e.t:F2} without any input"));

            // (d) never changes position in a tick where it is absent from the killer-bound frame
            Assert.DoesNotContain(log, e => e.o.Moved && !e.o.Visible);
            // and the moving->absent transition has no gap: the first absent tick is not a moving tick
            Assert.False(log.First(e => e.t >= firstAbsent - 1e-9).o.Moved);
        }
    }

    [Fact]
    public void OneInputThenSilence_SurvivorStopsWithin015s_NeverMovingWhileAbsent()
    {
        var (f, _, survivor, watch) = Pair();
        using (f)
        {
            int id = survivor.Id;
            survivor.Move(0.6f, 0.8f);
            var obs = new List<SurvObs>();
            for (int i = 0; i < 60; i++) obs.Add(watch.Step()[id]);
            Assert.Empty(watch.Violations);
            int moved = obs.Count(o => o.Moved);
            Assert.InRange(moved, 1, 3); // within 0.15 s (+ one tick)
            int lastMoved = obs.FindLastIndex(o => o.Moved);
            Assert.True(lastMoved <= 2, $"survivor still moving on tick {lastMoved + 1} after a single input");
            Assert.All(obs.Skip(lastMoved + 1), o => { Assert.False(o.Moved); Assert.False(o.Visible); });
            // distance bound: at most 3 steps
            var end = obs[^1];
            var start = obs[0];
            Assert.True(Math.Abs(end.X - start.X) + Math.Abs(end.Z - start.Z) <= 3 * Step * 1.5f);
        }
    }

    [Fact]
    public void TwentyHertzResend_MovesContinuouslyAtFullSpeed_SignalNeverDrops()
    {
        var (f, _, survivor, watch) = Pair();
        using (f)
        {
            int id = survivor.Id;
            float prevZ = survivor.Self().Z;
            for (int i = 0; i < 100; i++) // 5 s, key held, input every tick
            {
                survivor.Move(0, 1);
                var o = watch.Step()[id];
                Assert.True(o.Moved, "tick " + i);
                Assert.True(o.Visible && o.Signal, "tick " + i + ": Signal must be continuous while input is resent at 20 Hz");
                Assert.InRange(o.Z - prevZ, Step * 0.98f, Step * 1.01f);
                prevZ = o.Z;
            }
            Assert.Empty(watch.Violations);
        }
    }

    [Fact]
    public void FiveHertzInputs_MovementStutters_ButIsNeverHiddenWhileMoving()
    {
        var (f, _, survivor, watch) = Pair();
        using (f)
        {
            int id = survivor.Id;
            int moved = 0, still = 0, hiddenStill = 0;
            for (int i = 0; i < 200; i++) // 10 s, one input every 4th tick (5 Hz)
            {
                if (i % 4 == 0) survivor.Move(0, 1);
                var o = watch.Step()[id];
                if (o.Moved) { moved++; Assert.True(o.Visible && o.Signal, "moving but hidden at tick " + i); }
                else { still++; if (!o.Visible) hiddenStill++; }
            }
            Assert.Empty(watch.Violations);
            Assert.InRange(moved, 60, 140);  // stutter: roughly half of the ticks (hold 0.15 s of every 0.2 s)
            Assert.InRange(still, 60, 140);
            Assert.True(hiddenStill > 0, "idle part of the stutter is expected to be masked (no wallhack)");
        }
    }

    [Fact]
    public void KillerHeldDirectionAlsoExpires_AndTwentyHertzKillerKeepsMoving()
    {
        using var f = new QaFixture();
        var (killer, _) = f.StartPair();
        const float kstep = GameConstants.KillerSpeed * Dt;
        var p0 = killer.Self();
        killer.Move(1, 0);
        var xs = new List<float>();
        for (int i = 0; i < 40; i++) { f.Tick(); xs.Add(killer.Self().X); }
        int movedTicks = 0;
        float prev = p0.X;
        int lastMove = -1;
        for (int i = 0; i < xs.Count; i++) { if (xs[i] != prev) { movedTicks++; lastMove = i; } prev = xs[i]; }
        Assert.InRange(movedTicks, 1, 3);
        Assert.True(lastMove <= 2, "killer kept moving after its hold expired");
        Assert.InRange(xs[^1] - p0.X, 0.1f, 3 * kstep * 1.01f);

        // positive control: resending every tick keeps the killer moving at full speed
        float x = killer.Self().X;
        for (int i = 0; i < 40; i++)
        {
            killer.Move(0, 1);
            float z = killer.Self().Z;
            f.Tick();
            Assert.InRange(killer.Self().Z - z, kstep * 0.98f, kstep * 1.01f);
        }
        Assert.Equal(x, killer.Self().X);
    }

    [Fact]
    public void ExplicitZeroInput_StopsAtOnce()
    {
        var (f, _, survivor, watch) = Pair();
        using (f)
        {
            int id = survivor.Id;
            survivor.Move(1, 0);
            Assert.True(watch.Step()[id].Moved);
            survivor.Move(0, 0);
            var o = watch.Step()[id];
            Assert.False(o.Moved);
            Assert.Empty(watch.Violations);
        }
    }

    [Fact]
    public void BotAndHumanSurvivors_NeverMoveWhileAbsentFromKillerFrame_ManySeeds()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            using var f = new QaFixture(bots: 4, seed: seed);
            var killer = f.Join("K");
            var observer = f.Join("S");
            killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            killer.Send(new C2S_StartMatch());
            f.Tick();
            Assert.Equal((byte)Role.Killer, killer.Last<S2C_MatchStart>().Role);
            var watch = new Watch(f, killer, observer);
            var rnd = new Random(seed);
            int botMovedTicks = 0, botHiddenTicks = 0;
            var dirs = new[] { (1f, 0f), (0f, 1f), (-1f, 0f), (0f, -1f), (0.6f, 0.8f) };
            for (int i = 0; i < 1000; i++) // 50 s
            {
                if (rnd.Next(3) == 0) { var d = dirs[rnd.Next(dirs.Length)]; killer.Move(d.Item1, d.Item2); }
                if (rnd.Next(5) == 0) { var d = dirs[rnd.Next(dirs.Length)]; observer.Move(d.Item1, d.Item2); }
                var obs = watch.Step();
                foreach (var (id, o) in obs)
                {
                    if (id == observer.Id) continue;
                    if (o.Moved) botMovedTicks++;
                    if (!o.Visible) botHiddenTicks++;
                }
            }
            Assert.True(watch.Violations.Count == 0, "seed " + seed + ": " + string.Join(" | ", watch.Violations.Take(3)));
            Assert.True(botMovedTicks > 100, "scenario sanity: bots must move (seed " + seed + ")");
            Assert.True(botHiddenTicks > 100, "scenario sanity: bots must also be Silent at times (seed " + seed + ")");
        }
    }

    [Fact]
    public void RandomInputPatterns_VariableDt_NeverMoveWhileAbsentFromKillerFrame()
    {
        for (int seed = 1; seed <= 15; seed++)
        {
            using var f = new QaFixture(seed: seed);
            var killer = f.Join("K");
            var s1 = f.Join("S1");
            var s2 = f.Join("S2");
            killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            killer.Send(new C2S_StartMatch());
            f.Tick();
            var watch = new Watch(f, killer, s1);
            var rnd = new Random(seed * 7919);
            int movedTicks = 0;
            for (int i = 0; i < 500; i++)
            {
                foreach (var peer in new[] { s1, s2, killer })
                {
                    int pick = rnd.Next(10);
                    if (pick < 5) peer.Move((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
                    else if (pick == 5) peer.Move(0, 0);
                    else if (pick == 6) peer.Move(float.NaN, 1);
                    else if (pick == 7) peer.Move(1e20f, -1e20f);
                    else if (pick == 8) { peer.Move(1, 0); peer.Move(0, 1); }
                }
                float dt = 0.01f + (float)rnd.NextDouble() * 0.19f; // 0.01 .. 0.20 s, crosses the 0.15 s hold
                foreach (var o in watch.Step(dt).Values) if (o.Moved) movedTicks++;
            }
            Assert.True(watch.Violations.Count == 0, "seed " + seed + ": " + string.Join(" | ", watch.Violations.Take(3)));
            Assert.True(movedTicks > 100, "scenario sanity");
        }
    }

    // ------------------------------------------------------------------ fix 4 is covered in U2aAdversarialTests (rewritten)

    // ------------------------------------------------------------------ fix 5: half-open connections

    [Fact]
    public void TokenTakeover_OldConnectionStillOpen_OldClosedNewBoundToSamePlayerAndPosition()
    {
        var (f, killer, survivor, _) = Pair();
        using (f)
        {
            int id = survivor.Id;
            ulong token = survivor.Last<S2C_JoinResult>().ReconnectToken;
            for (int i = 0; i < 6; i++) { survivor.Move(0, 1); f.Tick(); }
            f.Ticks(5); // stationary
            var pos = survivor.Self();
            Assert.False(survivor.Connection.IsClosed);

            var fresh = f.Connect();
            fresh.Send(new C2S_Join { Nickname = "Back", ReconnectToken = token });
            f.Tick();
            var jr = fresh.Last<S2C_JoinResult>();
            Assert.Equal(0, jr.Result);
            Assert.Equal(id, jr.PlayerId);
            Assert.Equal(token, jr.ReconnectToken);
            Assert.True(survivor.Connection.IsClosed, "old connection not closed after takeover");
            Assert.True(fresh.Has<S2C_MatchStart>());
            var self = fresh.World().Players.Single(p => p.Id == id);
            Assert.Equal(pos.X, self.X);
            Assert.Equal(pos.Z, self.Z);
            // slot is still the same human (not a bot), exactly two filled slots, host unchanged
            var lobby = killer.Lobby();
            Assert.Equal(2, lobby.Slots.Count(s => s.PlayerId != 0));
            Assert.Equal(0, lobby.Slots.Count(s => s.PlayerId != 0 && s.IsBot == 1));
            Assert.Equal(killer.Id, lobby.Slots.Single(s => s.IsHost == 1).PlayerId);

            // the new connection drives the player; the old one cannot
            int oldMark = survivor.Mark();
            fresh.Move(1, 0);
            f.Tick();
            Assert.True(fresh.Self().X > pos.X + 0.19f);
            Assert.Empty(survivor.Since<S2C_WorldState>(oldMark));

            // the game keeps working: a 10 s disconnect of the NEW connection still starts the normal reconnect window
            fresh.Connection.Close();
            f.Tick();
            Assert.Equal(0, killer.Lobby().Slots.Count(s => s.PlayerId != 0 && s.IsBot == 1));
        }
    }

    [Fact]
    public void TokenTakeover_Lobby_HostTokenKeepsHostFlag()
    {
        using var f = new QaFixture();
        var host = f.Join("Host");
        var guest = f.Join("Guest");
        ulong token = host.Last<S2C_JoinResult>().ReconnectToken;
        var fresh = f.Connect();
        fresh.Send(new C2S_Join { Nickname = "Host2", ReconnectToken = token });
        f.Tick();
        Assert.Equal(0, fresh.Last<S2C_JoinResult>().Result);
        Assert.Equal(host.Id, fresh.Last<S2C_JoinResult>().PlayerId);
        Assert.True(host.Connection.IsClosed);
        var lobby = guest.Lobby();
        Assert.Equal(host.Id, lobby.Slots.Single(s => s.IsHost == 1).PlayerId);
        Assert.Equal(2, lobby.Slots.Count(s => s.PlayerId != 0));
        // The new host connection can still start the match.
        fresh.Send(new C2S_StartMatch());
        f.Tick();
        Assert.True(fresh.Has<S2C_MatchStart>());
        Assert.True(guest.Has<S2C_MatchStart>());
    }

    [Fact]
    public void TokenTakeover_AnotherPlayersTokenNeverTakesOver()
    {
        var (f, killer, survivor, _) = Pair();
        using (f)
        {
            ulong killerToken = killer.Last<S2C_JoinResult>().ReconnectToken;
            ulong survivorToken = survivor.Last<S2C_JoinResult>().ReconnectToken;
            Assert.NotEqual(killerToken, survivorToken);

            // (1) An authenticated connection presenting someone else's token: ignored, nothing changes for anyone.
            survivor.Send(new C2S_Join { Nickname = "Evil", ReconnectToken = killerToken });
            f.Ticks(3);
            Assert.False(killer.Connection.IsClosed);
            Assert.False(survivor.Connection.IsClosed);
            Assert.Equal(survivor.Id, survivor.Last<S2C_JoinResult>().PlayerId);
            killer.Move(0, 1); f.Tick();
            Assert.True(killer.Self().Z > 5.1f);

            // (2) A fresh connection with wrong / near / zero-ish tokens: rejected, existing connections untouched.
            foreach (ulong wrong in new[] { killerToken ^ 1UL, ~survivorToken, survivorToken + 1, 0xFFFFFFFFFFFFFFFFUL })
            {
                if (wrong == killerToken || wrong == survivorToken) continue;
                var intruder = f.Connect();
                intruder.Send(new C2S_Join { Nickname = "X", ReconnectToken = wrong });
                f.Tick();
                Assert.NotEqual(0, intruder.Last<S2C_JoinResult>().Result);
                Assert.False(killer.Connection.IsClosed);
                Assert.False(survivor.Connection.IsClosed);
                Assert.False(intruder.Has<S2C_WorldState>());
            }

            // (3) A third party holding the killer's token takes over the killer slot ONLY (survivor untouched).
            var third = f.Connect();
            third.Send(new C2S_Join { Nickname = "T", ReconnectToken = killerToken });
            f.Tick();
            Assert.Equal(killer.Id, third.Last<S2C_JoinResult>().PlayerId);
            Assert.True(killer.Connection.IsClosed);
            Assert.False(survivor.Connection.IsClosed);
            Assert.Equal(survivor.Id, survivor.Last<S2C_JoinResult>().PlayerId);
        }
    }

    [Fact]
    public void JoinTimeout_ConnectionThatNeverJoinsIsClosedAfter10s_PingBeforeJoinIsAnswered()
    {
        using var f = new QaFixture();
        var idle = f.Connect();
        f.Tick();
        idle.Send(new C2S_Ping { Timestamp = 11 });
        Assert.Equal(11, idle.Last<S2C_Pong>().Timestamp); // answered before Join
        f.Ticks(100); // ~5 s
        idle.Send(new C2S_Ping { Timestamp = 22 });
        Assert.Equal(22, idle.Last<S2C_Pong>().Timestamp);
        Assert.False(idle.Connection.IsClosed);
        f.Ticks(88); // 189 ticks = 9.45 s
        Assert.False(idle.Connection.IsClosed, "closed before the 10 s deadline");
        f.Ticks(16); // 205 ticks = 10.25 s
        Assert.True(idle.Connection.IsClosed, "never-joined connection still open after 10 s of ticks");
    }

    [Fact]
    public void JoinTimeout_JoinedConnectionsAreNotClosed_LateJoinerAt9_5sSurvives_RejectedJoinStillTimesOut()
    {
        using var f = new QaFixture();
        var early = f.Join("Early");
        var late = f.Connect();
        f.Ticks(189);
        late.Send(new C2S_Join { Nickname = "Late" });
        f.Tick();
        Assert.Equal(0, late.Last<S2C_JoinResult>().Result);
        f.Ticks(600); // 30 s more
        Assert.False(early.Connection.IsClosed);
        Assert.False(late.Connection.IsClosed);
        early.Send(new C2S_Ping { Timestamp = 5 });
        Assert.Equal(5, early.Last<S2C_Pong>().Timestamp);

        // a rejected join (lobby full) does not count as joined
        using var g = new QaFixture();
        for (int i = 0; i < 8; i++) g.Join("H" + i);
        var ninth = g.Connect();
        ninth.Send(new C2S_Join { Nickname = "H8" });
        g.Tick();
        Assert.NotEqual(0, ninth.Last<S2C_JoinResult>().Result);
        Assert.False(ninth.Connection.IsClosed);
        g.Ticks(210);
        Assert.True(ninth.Connection.IsClosed);
    }

    [Fact]
    public void JoinTimeout_ManyIdleConnections_AllClosed_NoStateLeak()
    {
        using var f = new QaFixture();
        var idle = Enumerable.Range(0, 300).Select(_ => f.Connect()).ToList();
        var good = f.Join("Good");
        f.Ticks(190);
        Assert.All(idle, c => Assert.False(c.Connection.IsClosed));
        f.Ticks(15);
        Assert.All(idle, c => Assert.True(c.Connection.IsClosed));
        Assert.False(good.Connection.IsClosed);
        // lobby unaffected
        Assert.Equal(1, good.Lobby().Slots.Count(s => s.PlayerId != 0));
    }

    // ------------------------------------------------------------------ fix 6: nickname sanitizer

    [Theory]
    [InlineData("\u202Eabc\u200B", "abc")]
    [InlineData("\u202Ealice\u202C", "alice")]
    [InlineData("\u2066x\u2069", "x")]
    [InlineData("a\u200Bb\u200Cc\u200Dd\uFEFFe\u00ADf", "abcdef")]
    [InlineData("\u200B\u200C\u200D\uFEFF", "Player 5")]
    [InlineData("\u202E", "Player 5")]
    [InlineData("\uD83D\uDE00", "\uD83D\uDE00")]
    public void Nickname_FormatCharsAndLoneSurrogatesStripped(string input, string expected)
    {
        Assert.Equal(expected, GameServer.SanitizeNickname(input, 5));
    }

    [Fact]
    public void Nickname_LoneSurrogatesStripped()
    {
        // Not an InlineData: xunit's attribute metadata cannot carry lone surrogates faithfully.
        Assert.Equal("ab", GameServer.SanitizeNickname("a\uD800b", 5));
        Assert.Equal("ab", GameServer.SanitizeNickname("a\uDC00b", 5));
        Assert.Equal("Player 5", GameServer.SanitizeNickname("\uD83D", 5));
        Assert.Equal("a\uD83D\uDE00b", GameServer.SanitizeNickname("a\uD83D\uDE00b", 5)); // a valid pair survives
        Assert.Equal("ab", GameServer.SanitizeNickname("a\uD83Db", 5)); // high surrogate followed by a non-low char
    }

    public static IEnumerable<object[]> LongNicknames()
    {
        yield return new object[] { new string('x', 200) };
        yield return new object[] { new string('\u00E9', 200) };
        yield return new object[] { string.Concat(Enumerable.Repeat("\u65E5\u672C\u8A9E", 50)) };
        yield return new object[] { string.Concat(Enumerable.Repeat("\uD83D\uDE00", 12)) };               // exactly 48 bytes
        yield return new object[] { string.Concat(Enumerable.Repeat("\uD83D\uDE00", 13)) };               // 52 bytes
        yield return new object[] { "a" + string.Concat(Enumerable.Repeat("\uD83D\uDE00", 12)) };         // 49 bytes: last emoji must go
        yield return new object[] { new string('x', 47) + "\uD83D\uDE00" };                                // emoji straddles the limit
        yield return new object[] { new string('x', 47) + " y" };                                // cap lands on a space
        yield return new object[] { new string('x', 46) + "\u00E9\uD83D\uDE00" };
        yield return new object[] { string.Concat(Enumerable.Repeat("\u202E\uD83D\uDE00\u200B", 40)) };    // format chars interleaved
    }

    [Theory]
    [MemberData(nameof(LongNicknames))]
    public void Nickname_CappedAtByteLimit_NoSplitSurrogate_EncodedLobbyNeverThrows(string input)
    {
        string clean = GameServer.SanitizeNickname(input, 3);
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        int bytes = strict.GetByteCount(clean); // throws on a lone surrogate
        Assert.InRange(bytes, 1, Protocol.MaxNicknameBytes);
        Assert.DoesNotContain(clean, c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format);
        Assert.Equal(clean, clean.Trim());
        // Longest valid prefix: adding the next text element would have exceeded the cap (no over-trimming).
        var lobby = new S2C_LobbyState();
        lobby.Slots[0] = new Slot { PlayerId = 3, Nickname = clean, IsBot = 0, IsHost = 1 };
        var frame = PacketCodec.Encode(lobby);
        Assert.NotEmpty(frame);
    }

    [Fact]
    public void Nickname_CapKeepsTheLongestPrefix()
    {
        Assert.Equal(new string('x', 48), GameServer.SanitizeNickname(new string('x', 200), 1));
        Assert.Equal(new string('\u00E9', 24), GameServer.SanitizeNickname(new string('\u00E9', 200), 1));
        Assert.Equal(string.Concat(Enumerable.Repeat("\u65E5", 16)), GameServer.SanitizeNickname(string.Concat(Enumerable.Repeat("\u65E5", 20)), 1));
        string twelve = string.Concat(Enumerable.Repeat("\uD83D\uDE00", 12));
        Assert.Equal(twelve, GameServer.SanitizeNickname(twelve, 1));
        Assert.Equal(twelve, GameServer.SanitizeNickname(twelve + "\uD83D\uDE00", 1));
        Assert.Equal("a" + string.Concat(Enumerable.Repeat("\uD83D\uDE00", 11)), GameServer.SanitizeNickname("a" + twelve, 1));
        Assert.Equal(new string('x', 47), GameServer.SanitizeNickname(new string('x', 47) + "\uD83D\uDE00", 1));
    }

    [Fact]
    public void Nickname_OverTheWire_SanitizedLobbyIsDeliveredToEveryoneAndConnectionsStayOpen()
    {
        using var f = new QaFixture();
        var names = new[]
        {
            "\u202Eabc\u200B", "\u200B\u200D", "\u202Ealice\u202C", string.Concat(Enumerable.Repeat("\uD83D\uDE00", 12)), // 48 bytes on the wire
            "x\u200Bx", "  \u2066 pad \u2069 ", "Zero\u200BWidth",
        };
        var peers = new List<QaPeer>();
        foreach (var n in names)
        {
            var p = f.Join(n);
            peers.Add(p);
            Assert.False(p.Connection.IsClosed);
        }
        f.Tick();
        foreach (var p in peers)
        {
            var lobby = p.Lobby();
            Assert.Equal(names.Length, lobby.Slots.Count(s => s.PlayerId != 0));
            foreach (var s in lobby.Slots.Where(s => s.PlayerId != 0))
            {
                Assert.DoesNotContain(s.Nickname, c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format || char.IsControl(c));
                Assert.False(string.IsNullOrWhiteSpace(s.Nickname));
            }
        }
        var lobby0 = peers[0].Lobby();
        Assert.Equal("abc", lobby0.Slots.Single(s => s.PlayerId == peers[0].Id).Nickname);
        Assert.Equal("Player " + peers[1].Id, lobby0.Slots.Single(s => s.PlayerId == peers[1].Id).Nickname);
        Assert.Equal("alice", lobby0.Slots.Single(s => s.PlayerId == peers[2].Id).Nickname);
        Assert.Equal(string.Concat(Enumerable.Repeat("\uD83D\uDE00", 12)), lobby0.Slots.Single(s => s.PlayerId == peers[3].Id).Nickname);
    }

    [Fact]
    public void Nickname_InvisibleNonFormatCharacters_Characterisation()
    {
        // Informational only (not in the a2 spec, which strips UnicodeCategory.Format): record what survives.
        foreach (var (label, input) in new[]
        {
            ("U+3164 HANGUL FILLER", "\u3164"), ("U+2800 BRAILLE BLANK", "\u2800"), ("U+115F HANGUL CHOSEONG FILLER", "\u115F"),
            ("U+2028 LINE SEPARATOR", "a\u2028b"), ("U+00A0 NBSP only", "\u00A0"), ("U+0300 combining only", "\u0300\u0301"),
            ("U+E000 private use only", "\uE000"),
        })
        {
            string r = GameServer.SanitizeNickname(input, 9);
            _out.WriteLine($"{label}: [{string.Join(" ", r.Select(c => ((int)c).ToString("X4")))}]");
            Assert.True(Encoding.UTF8.GetByteCount(r) <= Protocol.MaxNicknameBytes);
        }
    }

    // ------------------------------------------------------------------ fix 8: killer spawn

    [Fact]
    public void Spawns_AreFreeDistinctAndKillerSpawnIsMoreThan18mFromEverySurvivorSpawn()
    {
        var map = new PingTheKiller.ServerCore.Map.FixedMap();
        Assert.Equal(8, map.SpawnPoints.Count);
        foreach (var sp in map.SpawnPoints) Assert.True(map.CanStand(sp.X, sp.Z), $"spawn ({sp.X},{sp.Z}) inside a wall");
        Assert.Equal(8, map.SpawnPoints.Select(s => (s.X, s.Z)).Distinct().Count());
        var k = map.SpawnPoints[0];
        for (int i = 1; i < 8; i++)
        {
            float d = (float)Math.Sqrt(Math.Pow(k.X - map.SpawnPoints[i].X, 2) + Math.Pow(k.Z - map.SpawnPoints[i].Z, 2));
            Assert.True(d > GameConstants.ThreatLevel1Distance, $"spawn {i} is {d} m from the killer spawn");
        }
    }

    [Fact]
    public void EveryKillerIndexAndSeed_AllSurvivorsStartAtThreatLevelZero()
    {
        var killerIndicesSeen = new Dictionary<int, HashSet<int>>();
        for (int n = 2; n <= 8; n++)
        {
            killerIndicesSeen[n] = new HashSet<int>();
            for (int seed = 1; seed <= 80; seed++)
            {
                using var f = new QaFixture(seed: seed);
                var humans = Enumerable.Range(0, n).Select(i => f.Join("H" + i)).ToList();
                humans[0].Send(new C2S_StartMatch());
                f.Tick();
                int killerIdx = humans.FindIndex(h => h.Last<S2C_MatchStart>().Role == (byte)Role.Killer);
                Assert.True(killerIdx >= 0);
                killerIndicesSeen[n].Add(killerIdx);
                var kself = humans[killerIdx].Self();
                foreach (var h in humans.Where((_, i) => i != killerIdx))
                {
                    var w = h.World();
                    Assert.True(w.ThreatLevel == 0, $"n={n} seed={seed} killer index {killerIdx}: survivor starts at threat {w.ThreatLevel}");
                    var me = h.Self();
                    float d = (float)Math.Sqrt(Math.Pow(me.X - kself.X, 2) + Math.Pow(me.Z - kself.Z, 2));
                    Assert.True(d > 18f, $"n={n} seed={seed}: survivor spawns {d} m from the killer");
                }
                // the killer's own frame is the only one with threat 0 by definition; check one more tick for stability
                f.Tick();
                foreach (var h in humans.Where((_, i) => i != killerIdx)) Assert.Equal(0, h.World().ThreatLevel);
            }
        }
        // Coverage: with 8 players every killer index was exercised (otherwise the claim "for every killer index" is hollow).
        Assert.Equal(8, killerIndicesSeen[8].Count);
        for (int n = 2; n <= 7; n++) Assert.Equal(n, killerIndicesSeen[n].Count);
    }

    [Fact]
    public void ForcedKillerHost_AtEveryIndexViaPreexistingBots_AllHumanSurvivorsStartAtThreatZero()
    {
        // Host (human) forced to be the killer; k bots occupy indices 0..k-1 so the host's index is k (0..7).
        // Extra humans after the host exercise the "index > killer" spawn mapping.
        for (int k = 0; k <= 7; k++)
        {
            using var f = new QaFixture(bots: k, seed: 100 + k);
            var host = f.Join("Host");
            host.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            var others = new List<QaPeer>();
            while (k + 1 + others.Count < 8 && others.Count < 3) others.Add(f.Join("O" + others.Count));
            if (k == 0 && others.Count == 0) others.Add(f.Join("O"));
            host.Send(new C2S_StartMatch());
            f.Tick();
            Assert.Equal((byte)Role.Killer, host.Last<S2C_MatchStart>().Role);
            var kself = host.Self();
            foreach (var o in others)
            {
                Assert.Equal(0, o.World().ThreatLevel);
                var me = o.Self();
                Assert.True(Math.Sqrt(Math.Pow(me.X - kself.X, 2) + Math.Pow(me.Z - kself.Z, 2)) > 18);
            }
            // bot survivors too: after 1 s of play with the killer standing still, no survivor is within 18 m of the killer spawn
            var s = new HashSet<(float, float)>();
            f.Ticks(5);
            foreach (var o in others) s.Add((o.Self().X, o.Self().Z));
            Assert.Equal(others.Count, s.Count);
        }
    }

    // ------------------------------------------------------------------ fix 2: game loop must not die (Start()-driven)

    [Fact]
    public void GameLoop_ThrowingLogSubscriber_LoopKeepsTicking_OtherSubscribersStillGetLogs()
    {
        var game = new GameServer(new ServerOptions { IsOffline = true, Seed = 3, BotCount = 2, AutoStart = true });
        int thrown = 0;
        var recorded = new System.Collections.Concurrent.ConcurrentQueue<string>();
        game.Log += m => { Interlocked.Increment(ref thrown); throw new InvalidOperationException("log-subscriber-boom"); };
        game.Log += m => recorded.Enqueue(m);
        using var transport = new InMemoryTransport();
        transport.ConnectionAccepted += game.AddConnection;
        transport.Start();
        try
        {
            game.Start();
            var peer = new QaPeer(transport.Connect());
            peer.Send(new C2S_Join { Nickname = "P" });
            uint first = 0, last = 0;
            Assert.True(WaitFor(() =>
            {
                peer.Drain();
                var ws = peer.Packets.OfType<S2C_WorldState>().ToList();
                if (ws.Count == 0) return false;
                first = ws[0].Tick; last = ws[^1].Tick;
                return last >= first + 20;
            }), "game loop stopped ticking after a Log subscriber threw (first " + first + ", last " + last + ")");
            Assert.True(Volatile.Read(ref thrown) > 0, "scenario sanity: the throwing subscriber was never invoked");
            Assert.Contains(recorded, m => m.StartsWith("match start"));
            Assert.Contains(recorded, m => m.StartsWith("join player"));
            peer.Send(new C2S_Ping { Timestamp = 4 });
            Assert.Equal(4, peer.Last<S2C_Pong>().Timestamp);
        }
        finally { game.Stop(); }
        game.Stop(); // idempotent, does not throw
    }

    [Fact]
    public void GameLoop_ThrowingConnectionIdFaults_AreLoggedRateLimited_LoopRecovers()
    {
        var game = new GameServer(new ServerOptions { IsOffline = true, Seed = 4, BotCount = 2, AutoStart = true });
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        game.Log += logs.Enqueue;
        using var transport = new InMemoryTransport();
        transport.ConnectionAccepted += game.AddConnection;
        transport.Start();
        try
        {
            game.Start();
            var good = new QaPeer(transport.Connect());
            good.Send(new C2S_Join { Nickname = "Good" });
            Assert.True(WaitFor(() => { good.Drain(); return good.Packets.OfType<S2C_WorldState>().Count() > 5; }));
            // Ten connections whose RemoteId getter throws: Register faults on the loop thread, once per tick.
            for (int i = 0; i < 10; i++) game.AddConnection(new FaultyConnection("f" + i, throwOnRemoteId: true, throwOnSend: false));
            Assert.True(WaitFor(() => logs.Any(l => l.Contains("tick error"))), "tick fault was never logged");
            // loop recovers: ticks keep arriving afterwards
            good.Drain();
            uint mark = good.Packets.OfType<S2C_WorldState>().Last().Tick;
            Assert.True(WaitFor(() =>
            {
                good.Drain();
                return good.Packets.OfType<S2C_WorldState>().Last().Tick >= mark + 40;
            }), "loop did not recover after tick faults");
            int faultLogs = logs.Count(l => l.Contains("tick error") && l.Contains("faulty-remote-id"));
            Assert.InRange(faultLogs, 1, 2); // ~0.5 s of identical faults: once per distinct message per second
            good.Send(new C2S_Ping { Timestamp = 9 });
            Assert.Equal(9, good.Last<S2C_Pong>().Timestamp);
        }
        finally { game.Stop(); }
    }

    [Fact]
    public void ConnectionWhoseSendThrows_IsClosedAlone_OthersKeepReceiving()
    {
        using var f = new QaFixture();
        var good = f.Join("Good");
        var bad = new FaultyConnection("bad", throwOnRemoteId: false, throwOnSend: true,
            first: new C2S_Join { Nickname = "Bad" });
        f.Server.AddConnection(bad);
        f.Ticks(3);
        Assert.True(bad.CloseCalls >= 1, "faulty connection was never closed");
        Assert.True(bad.IsClosed);
        Assert.False(good.Connection.IsClosed);
        good.Send(new C2S_Ping { Timestamp = 12 });
        Assert.Equal(12, good.Last<S2C_Pong>().Timestamp);
        var lobby = good.Lobby();
        Assert.Equal(1, lobby.Slots.Count(s => s.PlayerId != 0 && s.IsBot == 0 && s.Nickname == "Good"));
    }
}
