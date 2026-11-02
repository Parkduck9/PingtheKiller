using System.Diagnostics;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for U2b: input validation (rule 13), sandbox (attempt 2), bot information limits (rule 12),
// and the --bind / --sandbox process options (rule 14).
public sealed class U2bInputSandboxBotTests
{
    // ------------------------------------------------------------------ input validation

    [Fact]
    public void Input_NonFiniteAndExtremeMoveInputFloatsInEveryField_NeverThrowNeverDisconnect_NeverMoveFasterThanSpeed()
    {
        float[] values = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, float.Epsilon, 0f, 1f, -1e30f, 1e30f };
        using var r = new QaRig(1);
        r.Place(r.K, 20, 5);
        r.Place(r.S[0], 30, 5);
        uint seq = 1;
        var peers = new[] { r.K, r.S[0] };
        int combos = 0;
        foreach (var x in values)
        foreach (var z in values)
        foreach (var yaw in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, 90f, 1e30f })
        {
            var p = peers[combos++ % 2];
            r.Place(p, p == r.K ? 20 : 30, 5);
            p.Send(new C2S_MoveInput { Seq = seq++ }); // explicit stop: nothing carried over from the previous combo
            r.Tick();
            float px = Pos(r, p).X, pz = Pos(r, p).Z;
            p.Send(new C2S_MoveInput { DirX = x, DirZ = z, Yaw = yaw, Seq = seq++ });
            r.Tick();
            Assert.False(r.K.Connection.IsClosed || r.S[0].Connection.IsClosed, $"disconnected on ({x},{z},{yaw})");
            var e = Pos(r, p);
            Assert.True(float.IsFinite(e.X) && float.IsFinite(e.Z) && float.IsFinite(e.Yaw), $"non-finite state after ({x},{z},{yaw})");
            float max = (p == r.K ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) / GameConstants.TickRate;
            float step = MathF.Sqrt((e.X - px) * (e.X - px) + (e.Z - pz) * (e.Z - pz));
            Assert.True(step <= max + 0.001f, $"moved {step} > {max} on ({x},{z},{yaw})");
            foreach (var w in r.KW.Players) Assert.True(float.IsFinite(w.X) && float.IsFinite(w.Z) && float.IsFinite(w.Yaw) && float.IsFinite(w.StatusTimer));
            Assert.True(float.IsFinite(r.SW.TimeLeft));
        }
        Assert.True(combos > 500);
        // Seq extremes still do not break anything.
        foreach (uint s in new[] { 0u, uint.MaxValue, 1u, uint.MaxValue - 1, 0x80000000u })
        {
            r.S[0].Send(new C2S_MoveInput { DirX = 1, Seq = s });
            r.Tick();
        }
        Assert.False(r.S[0].Connection.IsClosed);
    }

    private static PlayerState Pos(QaRig r, QaPeer p) => QaRig.Entry(p.World(), p.Id)!.Value;

    [Fact]
    public void Input_OutOfRangeBytesThatSkipTheCodec_InteractUseSkillSetRoleSetBots_AreIgnored_NoThrow_NoDisconnect()
    {
        using var d = new DirectRig(start: false);
        // Lobby: host sends invalid SetRole / SetBots values; the lobby stays sane.
        for (int v = 0; v < 256; v++)
        {
            bool valid = v is 0 or 1 or 255;
            if (valid) continue;
            d.K.Push(new C2S_SetRole { RoleChoice = (byte)v });
        }
        for (int v = 8; v < 256; v++) d.K.Push(new C2S_SetBots { Count = (byte)v });
        d.Tick(2);
        Assert.False(d.K.IsClosed || d.S.IsClosed);
        var lobby = d.K.Last<S2C_LobbyState>();
        Assert.Equal((byte)RoleChoice.Random, lobby.HostRoleChoice);
        Assert.DoesNotContain(lobby.Slots, s => s.IsBot != 0);
        d.K.Push(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        d.K.Push(new C2S_StartMatch());
        d.Tick();
        Assert.Equal((byte)Role.Killer, d.K.Last<S2C_MatchStart>().Role);
        byte skill = d.S.Last<S2C_MatchStart>().SkillId;

        // Interact: only 0 and 1 are meaningful. 2..255 must neither start a hack nor kill the connection.
        Spy.Set(d.P(d.SId), "X", 10f);
        Spy.Set(d.P(d.SId), "Z", 25.6f);
        d.Tick();
        for (int v = 2; v < 256; v++) d.S.Push(new C2S_Interact { Active = (byte)v });
        d.Tick(10);
        Assert.Equal(0, d.S.Last<S2C_WorldState>().Terminals[0].Progress);
        Assert.False(d.S.IsClosed);
        d.S.Push(new C2S_Interact { Active = 1 });
        d.Tick(10);
        Assert.True(d.S.Last<S2C_WorldState>().Terminals[0].Progress > 0);
        d.S.Push(new C2S_Interact { Active = 7 }); // an invalid value must not even release the hold
        d.Tick(5);
        int p1 = d.S.Last<S2C_WorldState>().Terminals[0].Progress;
        d.Tick(10);
        Assert.True(d.S.Last<S2C_WorldState>().Terminals[0].Progress > p1, "invalid Interact value released the hold");

        // Killer Interact is ignored (no hack, no state change).
        d.K.Push(new C2S_Interact { Active = 1 });
        d.Tick(3);
        Assert.False(d.K.IsClosed);

        // UseSkill: all 256 values in one tick produce at most one cast (the assigned id).
        int before = d.S.All<S2C_SkillEffect>().Count;
        for (int v = 0; v < 256; v++) d.S.Push(new C2S_UseSkill { SkillId = (byte)v });
        d.Tick(2);
        Assert.False(d.S.IsClosed);
        // A survivor with Dummy Packet / Stealth / Traffic / Restore (needs ally) / White Noise: restore without Lagging ally fails.
        int casts = d.S.All<S2C_SkillEffect>().Count - before;
        Assert.InRange(casts, 0, 1);
        _ = skill;
    }

    [Fact]
    public void Input_OutOfRangeEnumBytesOverTheRealCodec_AreAWireFormatViolation_ConnectionCloses_Characterisation()
    {
        // Brief rule 1/13 say "ignore the packet, never disconnect for a semantic error". The Shared codec (not touched by U2b)
        // rejects Interact.Active > 1, SetRole outside {0,1,255} and SetBots > 7 as malformed frames, so over the wire the
        // server closes the connection. The implementer documented this for Interact (judgement call 6). Pinned as minor.
        foreach (var make in new Func<IPacket>[]
        {
            () => new C2S_Interact { Active = 2 },
            () => new C2S_SetRole { RoleChoice = 7 },
            () => new C2S_SetBots { Count = 8 }
        })
        {
            using var f = new QaFixture(seed: 3);
            var host = f.Join("H");
            host.Send(make());
            f.Tick(0.05f);
            f.Tick(0.05f);
            Assert.True(host.Connection.IsClosed, make().GetType().Name);
        }
    }

    [Fact]
    public void Input_UseSkillEveryByteOverTheRealCodec_NeverDisconnects()
    {
        using var r = new QaRig(1);
        r.SetSkill(r.K, SkillIds.Overclock);
        for (int v = 0; v < 256; v++) r.K.Send(new C2S_UseSkill { SkillId = (byte)v });
        r.Tick(2);
        Assert.False(r.K.Connection.IsClosed);
        r.K.Drain();
        Assert.Single(r.K.Packets.OfType<S2C_SkillEffect>());
    }

    // ------------------------------------------------------------------ sandbox

    [Fact]
    public void Sandbox_IsOffByDefault_InEveryConstructionPath()
    {
        Assert.False(new ServerOptions().Sandbox);
        Assert.False(new ServerOptions { IsOffline = true, BotCount = 3, AutoStart = true }.Sandbox);
        var server = new GameServer();
        Assert.False(Spy.Server<bool>(server, "_sandbox"));
        server.Dispose();
        var withOptions = new GameServer(new ServerOptions { IsOffline = true });
        Assert.False(Spy.Server<bool>(withOptions, "_sandbox"));
        withOptions.Dispose();
        var on = new GameServer(new ServerOptions { Sandbox = true });
        Assert.True(Spy.Server<bool>(on, "_sandbox"));
        on.Dispose();
    }

    [Fact]
    public void Sandbox_NoCaptureHackingSkillsOrMatchEndBefore600Seconds_ThenExactlyOneTimeUp()
    {
        using var r = new QaRig(2, seed: 21, sandbox: true);
        r.Place(r.S[0], 10, 25.6f);
        r.Place(r.S[1], 56.5f, 30);
        var map = new PingTheKiller.ServerCore.Map.FixedMap();
        int pushed = 0;
        void Spam()
        {
            r.Interact(r.S[0], true);
            r.Interact(r.S[1], true);
            for (byte b = 0; b < 5; b++) { r.Use(r.S[0], b); r.Use(r.S[1], b); }
            for (byte b = 0; b < 4; b++) r.Use(r.K, b);
            r.Place(r.K, 11.0f, 25.6f); // killer sits on top of S0 (capture would trigger instantly under U2b rules)
            pushed++;
        }
        void Check()
        {
            var kw = r.KW;
            var sw = r.SW;
            Assert.All(sw.Players.Where(p => p.Role == (byte)Role.Survivor), p =>
            {
                Assert.Equal((byte)PlayerStatus.Normal, p.Status);
                Assert.Equal(0, p.CaptureCount);
                Assert.Equal(0, p.Flags & ~(byte)PlayerFlags.Signal);
            });
            Assert.All(kw.Terminals, t => Assert.Equal(0, t.Progress));
            Assert.Equal(0, kw.ExitOpen);
            Assert.Equal(1, kw.ExitLocked);
        }
        for (int i = 0; i < 400; i++) { Spam(); r.Tick(); if (i % 40 == 0) Check(); }
        r.Dt = 1f;
        while (r.Elapsed < 596)
        {
            Spam();
            r.Tick();
            Assert.False(r.K.Has<S2C_GameResult>() || r.S[0].Has<S2C_GameResult>(), "match ended early at " + r.Elapsed);
        }
        Check();
        r.Dt = 0.05f;
        while (!r.K.Has<S2C_GameResult>() && r.Elapsed < 700) { Spam(); r.Tick(); }
        Assert.InRange(r.Elapsed, 599.9, 600.12); // the match-start tick also counts as match time
        r.Tick(100);
        r.K.Drain();
        r.S[0].Drain();
        r.S[1].Drain();
        foreach (var p in new[] { r.K, r.S[0], r.S[1] })
        {
            var result = Assert.Single(p.Packets.OfType<S2C_GameResult>());
            Assert.Equal((byte)GameEndReason.TimeUp, result.Reason);
            Assert.Equal((byte)Role.Killer, result.WinnerRole);
            Assert.Empty(p.Packets.OfType<S2C_SkillEffect>());
        }
        Assert.True(pushed > 0);
        _ = map;
    }

    [Fact]
    public void Sandbox_KillerFramesContainOnlyRealSignalSurvivors_NoDecoysNoAfterimage()
    {
        using var f = new QaFixture(bots: 5, seed: 8, sandbox: true);
        var killer = f.Join("K");
        var survivor = f.Join("S");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        var truthSignal = 0;
        for (int i = 0; i < 1200; i++)
        {
            if (i % 40 < 20) survivor.Move(0.6f, 0.8f); else survivor.Move(0, 0);
            f.Tick();
            var kw = killer.World();
            var sw = survivor.World();
            // U2c migration: killer-bound entries are anonymous, so each one is matched to its survivor by position. The
            // sandbox still has no phantoms and no afterimage: the entries are exactly the survivors that are Signal.
            var signalTruth = sw.Players.Where(x => x.Role == (byte)Role.Survivor && (x.Flags & (byte)PlayerFlags.Signal) != 0).ToList();
            Assert.Equal(signalTruth.Count, kw.Players.Count(p => p.Role == (byte)Role.Survivor));
            foreach (var p in kw.Players.Where(p => p.Role == (byte)Role.Survivor))
            {
                Assert.Equal((byte)PlayerFlags.Signal, p.Flags);
                Assert.Equal(0, p.Id);
                var t = Assert.Single(signalTruth, x => x.X == p.X && x.Z == p.Z);
                Assert.Equal(t.X, p.X);
                Assert.Equal(t.Z, p.Z);
                truthSignal++;
            }
            killer.Packets.RemoveAll(x => x is S2C_WorldState && x != kw);
            survivor.Packets.RemoveAll(x => x is S2C_WorldState && x != sw);
        }
        Assert.True(truthSignal > 500);
    }

    // ------------------------------------------------------------------ bots must not use hidden information (rule 12)

    private sealed class BotSet : IDisposable
    {
        internal QaFixture F;
        internal QaPeer Human;
        internal List<object> Bots = new();
        internal object? KillerPlayer;
        internal BotSet(int seed, bool humanKiller, int bots)
        {
            F = new QaFixture(seed: seed);
            Human = F.Join("H");
            Human.Send(new C2S_SetBots { Count = (byte)bots });
            Human.Send(new C2S_SetRole { RoleChoice = (byte)(humanKiller ? RoleChoice.Killer : RoleChoice.Survivor) });
            Human.Send(new C2S_StartMatch());
            F.Tick();
            foreach (var p in Spy.Players(F.Server))
            {
                if (Spy.Get<bool>(p, "IsBot")) Bots.Add(p);
                if ((Role)Spy.Get(p, "Role") == Role.Killer) KillerPlayer = p;
            }
        }
        internal object HumanPlayer => Spy.Player(F.Server, Human.Id);
        internal static (float X, float Z) At(object p) => (Spy.Get<float>(p, "X"), Spy.Get<float>(p, "Z"));
        internal void Move(object p, float x, float z) { Spy.Set(p, "X", x); Spy.Set(p, "Z", z); }
        public void Dispose() => F.Dispose();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Bots_SurvivorBotsNeverSteerByTheKillerPosition_OnlyByThreatLevel(int seed)
    {
        // Run the same seeded match with the (idle) human killer parked at several spots. Whenever two spots expose every
        // bot to the identical ThreatLevel sequence and the identical line-of-sight sequence (U2e/U2d: a survivor also sees
        // the killer while its own sight line is clear; that is all a bot may legitimately use), every survivor bot must
        // have taken exactly the same path. A bot reading the true killer position would diverge.
        var runs = new List<(byte[] Bands, (float X, float Z)[] Track)>();
        // U2d re-target: survivor bots now also see the killer in line of sight, so two in-map spots rarely give identical
        // inputs; two spots beyond the outer walls (ThreatLevel 0, never in sight, far apart) always form a matching pair.
        foreach (var killerSpot in new[] { (2f, 2f), (57f, 3f), (58f, 12f), (30f, 2f), (2f, 14f), (3f, 58f), (58f, 58f), (45f, 5f), (-40f, -40f), (100f, 100f) })
        {
            using var b = new BotSet(seed, humanKiller: true, bots: 7);
            Assert.Equal(7, b.Bots.Count);
            b.Move(b.KillerPlayer!, killerSpot.Item1, killerSpot.Item2);
            var track = new List<(float, float)>();
            var bands = new List<byte>();
            for (int i = 0; i < 600; i++) // U2d re-target: 600 ticks (was 300); bots open with a 2-8 s Silent pause
            {
                b.F.Tick();
                foreach (var bot in b.Bots)
                {
                    var (x, z) = BotSet.At(bot);
                    var k = BotSet.At(b.KillerPlayer!);
                    float d = MathF.Sqrt((x - k.X) * (x - k.X) + (z - k.Z) * (z - k.Z));
                    bands.Add(GameServer.ThreatForDistance(d));
                    // U2d re-target: line of sight is legitimate information too, so it is part of the compared sequence.
                    bands.Add((byte)(b.F.Server.Map.SegmentBlocked(x, z, k.X, k.Z) ? 0 : 1));
                    track.Add((x, z));
                }
                Assert.False(b.Human.Has<S2C_GameResult>());
            }
            runs.Add((bands.ToArray(), track.ToArray()));
        }
        var pair = runs.SelectMany((a, i) => runs.Skip(i + 1).Select(c => (a, c))).FirstOrDefault(p => p.a.Bands.SequenceEqual(p.c.Bands));
        Assert.NotNull(pair.a.Track);
        Assert.Equal(pair.a.Track, pair.c.Track);
        var tracks = new List<(float X, float Z)[]> { pair.a.Track };
        // And the bots really did something (they walk and hack), so equality is not vacuous.
        var distinct = tracks[0].Distinct().Count();
        Assert.True(distinct > 400, "bots barely moved");
    }

    [Fact]
    public void Bots_SurvivorBotStopsAtThreatTwoOrMore_AndOnlyTheBandMatters_NotTheDirection()
    {
        // U2d re-target (bot timing and the U2e sight rule): a survivor bot freezes while ThreatLevel is 2+ and pauses for a
        // while after ThreatLevel rises to 1; it also reacts to a killer in its line of sight, which is legitimate. The killer
        // is therefore re-placed every tick at a fixed distance from the bot, at the first angle from the chosen direction
        // (turning in 5-degree steps) whose sight line to the bot is blocked by a wall. ThreatLevel is constant and the killer
        // is never in sight by construction, so the only thing that differs between two runs is where the killer is.
        List<(float X, float Z)> Run(float dirX, float dirZ, float distance, int ticks)
        {
            using var b = new BotSet(4, humanKiller: true, bots: 1);
            var map = b.F.Server.Map;
            var track = new List<(float, float)>();
            float baseAngle = MathF.Atan2(dirX, dirZ);
            for (int i = 0; i < ticks; i++)
            {
                var (x, z) = BotSet.At(b.Bots[0]);
                (float X, float Z)? hidden = null;
                for (int step = 0; step < 72 && hidden == null; step++)
                {
                    float angle = baseAngle + step * MathF.PI / 36f;
                    float kx = x + MathF.Sin(angle) * distance, kz = z + MathF.Cos(angle) * distance;
                    if (map.SegmentBlocked(x, z, kx, kz)) hidden = (kx, kz);
                }
                Assert.True(hidden.HasValue, $"no hidden spot {distance} m from ({x},{z})");
                b.Move(b.KillerPlayer!, hidden!.Value.X, hidden.Value.Z);
                b.F.Tick();
                track.Add(BotSet.At(b.Bots[0]));
            }
            return track;
        }
        static float Travel(List<(float X, float Z)> t) => MathF.Abs(t[^1].X - t[0].X) + MathF.Abs(t[^1].Z - t[0].Z);
        // ThreatLevel 2 (8 m) from two different directions: frozen for 10 s, identically.
        var a = Run(0, -1, 8, 200);
        var c = Run(1, -0.7f, 8, 200);
        Assert.True(Travel(a) < 0.01f, $"bot moved {Travel(a)} at threat 2");
        Assert.Equal(a, c);
        // Control: with the killer far away (ThreatLevel 0) the same bot walks within 10 s, so the freeze above is not vacuous.
        Assert.True(Travel(Run(0, -1, 40, 200)) > 0.5f, "control bot never moved");
        // ThreatLevel 1 (15 m): identical movement from two directions, and it does move after its pause.
        // U2d re-target: 600 ticks (was 320): the ThreatLevel 1 scare lasts 8 s after the rise plus a 4-10 s calm.
        var d1 = Run(0, -1, 15, 600);
        var d2 = Run(1, -1, 15, 600);
        Assert.Equal(d1, d2);
        Assert.True(Travel(d1) > 0.5f, "bot never moved at threat 1");
    }
    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void Bots_KillerBotIgnoresSilentSurvivors_ChasesWhatItSees(int seed)
    {
        // A human survivor sitting Silent must not influence the bot killer's path, wherever it is.
        var tracks = new List<List<(float, float)>>();
        foreach (var spot in new[] { (3f, 57f), (57f, 57f) })
        {
            using var b = new BotSet(seed, humanKiller: false, bots: 1);
            Assert.NotNull(b.KillerPlayer);
            Assert.True(Spy.Get<bool>(b.KillerPlayer!, "IsBot"));
            // U2c migration: with the new private RNG seeds 2 and 6 give the bot killer Ping Scanner, which by design exposes the NEAREST
            // survivor (Signal or not) and so legitimately reacts to where a silent survivor sits. The invariant protected here is that
            // the bot's own decisions use only the anonymous view, so the skill is neutralised for this comparison.
            Spy.Set(b.KillerPlayer!, "Skill", (byte)255);
            // The scanner may already have fired during BotSet construction and left a forced exposure on the human; clear it.
            foreach (var pl in new[] { b.HumanPlayer, b.Bots[0] }) Spy.Set(pl, "ExposedUntil", double.NegativeInfinity);
            b.Move(b.HumanPlayer, spot.Item1, spot.Item2);
            var track = new List<(float, float)>();
            for (int i = 0; i < 300; i++)
            {
                b.F.Tick();
                track.Add(BotSet.At(b.KillerPlayer!));
                Assert.Equal((byte)PlayerStatus.Normal, (byte)(PlayerStatus)Spy.Get(b.HumanPlayer, "Status"));
            }
            tracks.Add(track);
        }
        Assert.Equal(tracks[0], tracks[1]);
        // Non-vacuity: the bot walks (it also stops for look-around pauses of 1.5-4 s, so well under 300 distinct points is normal).
        Assert.True(tracks[0].Distinct().Count() > 100, $"killer bot barely moved: {tracks[0].Distinct().Count()} distinct positions");

        // Positive control: a moving (visible) survivor is chased.
        using var c = new BotSet(seed, humanKiller: false, bots: 1);
        c.Move(c.HumanPlayer, 30, 55);
        c.F.Tick();
        var start = BotSet.At(c.KillerPlayer!);
        float d0 = Dist(start, (30, 55));
        for (int i = 0; i < 100; i++) { c.Human.Move(0, 0.1f); c.F.Tick(); }
        var near = BotSet.At(c.KillerPlayer!);
        Assert.True(Dist(near, BotSet.At(c.HumanPlayer)) < d0 - 10, "killer bot did not chase a visible Signal");
    }

    private static float Dist((float X, float Z) a, (float X, float Z) b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void Bots_KillerBotUsesPingScannerOnlyWhenNoSurvivorIsVisible()
    {
        using var b = new BotSet(12, humanKiller: false, bots: 1);
        var killer = b.KillerPlayer!;
        b.Move(b.HumanPlayer, 57, 57);
        for (int i = 0; i < 3; i++) { b.Human.Move(0, 1); b.F.Tick(); }
        Spy.Set(killer, "Skill", SkillIds.PingScanner);
        Spy.Set(killer, "SkillReadyAt", Spy.Server<double>(b.F.Server, "_now"));
        int mark = b.Human.Mark();
        for (int i = 0; i < 100; i++) { b.Human.Move(0, 1); b.F.Tick(); } // permanently visible Signal
        Assert.Empty(b.Human.Since<S2C_SkillEffect>(mark));
        int stopped = -1;
        for (int i = 0; i < 60 && stopped < 0; i++)
        {
            b.F.Tick();
            if (b.Human.Since<S2C_SkillEffect>(mark).Any(e => e.SkillId == SkillIds.PingScanner)) stopped = i;
        }
        Assert.True(stopped >= 0, "bot killer never used Ping Scanner although nobody was visible");
    }

    // ------------------------------------------------------------------ rule 14: --bind / --sandbox process options

    private static string? ServerDll()
    {
        string dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string candidate = Path.GetFullPath(Path.Combine(dir, "..", "..", "Server", "debug", "Server.dll"));
        if (File.Exists(candidate)) return candidate;
        candidate = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "Server", "debug", "Server.dll"));
        return File.Exists(candidate) ? candidate : null;
    }

    private static (int exit, string stdout, string stderr) RunServer(string args, bool killAfterListening)
    {
        string? dll = ServerDll();
        Assert.NotNull(dll);
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {args}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        var listening = new ManualResetEventSlim(false);
        proc.OutputDataReceived += (_, e) => { if (e.Data == null) return; lock (stdout) stdout.AppendLine(e.Data); if (e.Data.StartsWith("listening")) listening.Set(); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        try
        {
            if (killAfterListening)
            {
                Assert.True(listening.Wait(20000), "server did not start: " + stdout + stderr);
                proc.Kill(true);
                proc.WaitForExit(5000);
            }
            else if (!proc.WaitForExit(20000)) { proc.Kill(true); Assert.Fail("server did not exit"); }
            proc.WaitForExit();
            return (proc.ExitCode, stdout.ToString(), stderr.ToString());
        }
        finally { if (!proc.HasExited) proc.Kill(true); }
    }

    [Fact]
    public void Process_SandboxIsNotEnabledByDefault_BindDefaultsToAnyAndLoopbackIsHonoured()
    {
        if (ServerDll() == null) return; // solution not built in this layout
        var plain = RunServer("--port 0 --bots 0 --bind 127.0.0.1", true);
        Assert.Contains("listening address=127.0.0.1", plain.stdout);
        Assert.DoesNotContain("sandbox", plain.stdout);
        var any = RunServer("--port 0 --bots 0", true);
        Assert.Contains("listening address=0.0.0.0", any.stdout);
        Assert.DoesNotContain("sandbox", any.stdout);
        var sb = RunServer("--port 0 --bots 0 --bind 127.0.0.1 --sandbox", true);
        Assert.Contains("sandbox", sb.stdout);
    }

    [Theory]
    [InlineData("--bind not-an-ip")]
    [InlineData("--bind")]
    [InlineData("--bind 999.1.1.1")]
    [InlineData("--bind 127.0.0.1 --bots 9")]
    public void Process_InvalidBindExitsWithCodeTwoAndAMessage(string args)
    {
        if (ServerDll() == null) return;
        var result = RunServer(args, false);
        Assert.Equal(2, result.exit);
        Assert.False(string.IsNullOrWhiteSpace(result.stderr));
    }
}
