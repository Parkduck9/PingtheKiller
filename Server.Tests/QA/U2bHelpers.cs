using System.Buffers.Binary;
using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Test-local helpers for the independent U2b QA suite. They drive the server only through the public API
// (InMemoryTransport + packets). Reflection (Spy) is used ONLY to arrange a scenario (teleport, assign a skill,
// pre-age a timer) and to read ground truth for bot-isolation checks; every asserted behaviour is observed on the wire.

internal static class Spy
{
    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static readonly FieldInfo PlayersField = typeof(GameServer).GetField("_players", All)!;

    internal static object Player(GameServer server, int id)
    {
        foreach (var p in (System.Collections.IEnumerable)PlayersField.GetValue(server)!)
            if ((int)Get(p, "Id") == id) return p;
        throw new InvalidOperationException("no player " + id);
    }
    internal static object Get(object target, string field) =>
        target.GetType().GetField(field, All)!.GetValue(target)!;
    internal static T Get<T>(object target, string field) => (T)Get(target, field);
    internal static void Set(object target, string field, object value)
    {
        var f = target.GetType().GetField(field, All)!;
        f.SetValue(target, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, value) : Convert.ChangeType(value, f.FieldType));
    }
    internal static T Server<T>(GameServer server, string field) => (T)typeof(GameServer).GetField(field, All)!.GetValue(server)!;
    internal static void SetServer(GameServer server, string field, object value)
    {
        var f = typeof(GameServer).GetField(field, All)!;
        f.SetValue(server, Convert.ChangeType(value, f.FieldType));
    }
    internal static IEnumerable<object> Players(GameServer server) =>
        ((System.Collections.IEnumerable)PlayersField.GetValue(server)!).Cast<object>();
    internal static int CountOf(GameServer server, string field) =>
        (int)typeof(System.Collections.ICollection).GetProperty("Count")!.GetValue(typeof(GameServer).GetField(field, All)!.GetValue(server))!;
}

// One human killer + N human survivors, match started, every player's skill neutralised (255 = no skill) and no
// bots, so a rule can be exercised in isolation. Positions are arranged with Place().
internal sealed class QaRig : IDisposable
{
    internal QaFixture F { get; }
    internal QaPeer K { get; }
    internal QaPeer[] S { get; }
    internal GameServer Server => F.Server;
    internal int Ticks;
    internal readonly List<string> Logs = new();

    internal QaRig(int survivors, int seed = 1, bool sandbox = false, int? matchSeed = null, bool csprng = false)
    {
        F = new QaFixture(0, false, seed, sandbox, matchSeed, csprng);
        F.Server.Log += m => { lock (Logs) Logs.Add(m); };
        K = F.Join("K");
        S = new QaPeer[survivors];
        for (int i = 0; i < survivors; i++) S[i] = F.Join("S" + i);
        K.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        K.Send(new C2S_StartMatch());
        F.Tick();
        Assert.Equal((byte)Role.Killer, K.Last<S2C_MatchStart>().Role);
        foreach (var s in S) Assert.Equal((byte)Role.Survivor, s.Last<S2C_MatchStart>().Role);
        SetSkill(K, 255);
        foreach (var s in S) SetSkill(s, 255);
        // Park everybody far apart: killer in the SW corner, survivors at the centre crossroads behind the south blocks.
        Place(K, 5, 5);
        for (int i = 0; i < survivors; i++) Place(S[i], 38 + (i % 4) * 2f, 31 + (i / 4) * 2f);
    }

    // Ends the running match by time-up, then starts the next one with the same lobby (killer role kept) and parks everybody again.
    internal void NextMatch()
    {
        K.Drain();
        int results = K.Packets.Count(p => p is S2C_GameResult);
        SetMatchTime(899.9);
        Tick(3);
        K.Drain();
        Assert.Equal(results + 1, K.Packets.Count(p => p is S2C_GameResult));
        K.Send(new C2S_StartMatch());
        Tick();
        Assert.Equal((byte)Role.Killer, K.Last<S2C_MatchStart>().Role);
        SetSkill(K, 255);
        foreach (var s in S) SetSkill(s, 255);
        Place(K, 5, 5);
        for (int i = 0; i < S.Length; i++) Place(S[i], 38 + (i % 4) * 2f, 31 + (i / 4) * 2f);
    }

    internal float Dt = 0.05f;
    internal double Elapsed;
    internal void Tick(int n = 1)
    {
        for (int i = 0; i < n; i++) { F.Tick(Dt); Ticks++; Elapsed += Dt; }
    }
    internal object P(QaPeer peer) => Spy.Player(Server, peer.Id);
    internal void SetSkill(QaPeer peer, byte skill) => Spy.Set(P(peer), "Skill", skill);
    public void Dispose() => F.Dispose();
    internal void Place(QaPeer peer, float x, float z)
    {
        var p = P(peer);
        Spy.Set(p, "X", x);
        Spy.Set(p, "Z", z);
    }
    // Arrange a Lagging survivor exactly as a capture would have left it.
    internal void MakeLagging(QaPeer peer)
    {
        var p = P(peer);
        Spy.Set(p, "Status", (byte)PlayerStatus.Lagging);
        Spy.Set(p, "CaptureCount", (byte)1);
        Spy.Set(p, "LaggingUntil", Spy.Server<double>(Server, "_now") + GameConstants.LaggingTimeoutSeconds);
        Spy.Set(p, "Interacting", false);
        Spy.Set(p, "RescueProgress", 0.0);
    }
    internal void SetCaptureCount(QaPeer peer, byte count) => Spy.Set(P(peer), "CaptureCount", count);
    internal void SetMatchTime(double seconds) => Spy.SetServer(Server, "_matchTime", seconds);
    // GDD v2: both exits powered and exit 0 (4,4) open, as if its switch had been held for 15 s.
    internal void OpenExitFlag()
    {
        Spy.SetServer(Server, "_powered", true);
        Spy.Server<bool[]>(Server, "_exitOpen")[0] = true;
    }
    internal bool ExitOpenFlag => Spy.Server<bool[]>(Server, "_exitOpen")[0];
    // GDD v2: both exits powered and the switch of exit 0 at 1 s of progress, so Firewall Block has a target.
    internal void ArmFirewall()
    {
        Spy.SetServer(Server, "_powered", true);
        Spy.Server<double[]>(Server, "_switchSeconds")[0] = 1;
    }
    internal void Use(QaPeer peer, byte skill) => peer.Send(new C2S_UseSkill { SkillId = skill });
    internal void Interact(QaPeer peer, bool on) => peer.Send(new C2S_Interact { Active = (byte)(on ? 1 : 0) });

    internal S2C_WorldState KW => K.World();
    internal S2C_WorldState SW => S[0].World();
    // U2c migration: killer-bound survivor entries carry no Id, so "the killer sees survivor s" now means "there is an
    // anonymous survivor entry at s's true position" (the survivor's own frame is the ground truth). Ambiguous only when
    // a phantom shares the exact position (Dummy Packet on the cast tick), which the callers account for.
    internal PlayerState? KillerSees(QaPeer survivor)
    {
        var self = survivor.Self();
        return KillerAt(KW, self.X, self.Z);
    }
    internal static PlayerState? KillerAt(S2C_WorldState killerWorld, float x, float z, float tol = 1e-3f)
    {
        foreach (var p in killerWorld.Players)
            if (p.Role == (byte)Role.Survivor && MathF.Abs(p.X - x) <= tol && MathF.Abs(p.Z - z) <= tol) return p;
        return null;
    }
    // Every survivor-shaped (non-killer) entry of the killer frame.
    internal IEnumerable<PlayerState> KillerSurvivorEntries => KW.Players.Where(p => p.Role == (byte)Role.Survivor);
    internal PlayerState Truth(QaPeer survivor) => Entry(SW, survivor.Id)!.Value;
    internal PlayerState TruthOf(QaPeer viaSurvivor, QaPeer survivor) => Entry(viaSurvivor.World(), survivor.Id)!.Value;
    internal static PlayerState? Entry(S2C_WorldState w, int id)
    {
        foreach (var p in w.Players) if (p.Id == id) return p;
        return null;
    }
    internal static float Dist(float ax, float az, float bx, float bz) =>
        MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    // Number of consecutive ticks (starting with the next one) for which the condition holds; the tick is simulated.
    internal int CountWhile(Func<bool> cond, int limit = 4000)
    {
        int n = 0;
        while (n < limit) { Tick(); if (!cond()) break; n++; }
        return n;
    }

    // Counts SkillEffect packets received since the mark.
    internal static int Effects(QaPeer p, int mark, byte? skill = null) =>
        p.Since<S2C_SkillEffect>(mark).Count(e => skill == null || e.SkillId == skill);

    // Legitimately open the exit with two survivors: both co-hack terminals 0 and 1 (2 of 4 required).
    internal void OpenExitWithTwo()
    {
        Assert.Equal(2, S.Length);
        foreach (var (tx, tz) in Server.Map.TerminalPositions.Take(3).Select(t => (t.X,t.Z)))
        {
            Place(S[0], tx - 0.5f, tz + 0.5f);
            Place(S[1], tx + 0.5f, tz + 0.5f);
            Interact(S[0], true);
            Interact(S[1], true);
            Tick(Core.RulesMatch.PassiveTicks(2) + 4); // 11/17 (R2): 90 s passive at 1.8x
        }
        Interact(S[0], false);
        Interact(S[1], false);
        Tick();
        Assert.True(KW.Powered());
    }
}

// A connection that hands decoded packet objects straight to the server (bypasses the codec's range checks) so that
// semantically invalid values reach the rules, and records what the server sends back.
internal sealed class QaDirect : IConnection
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<IPacket> _incoming = new();
    private readonly List<IPacket> _sent = new();
    private int _closed;
    public string RemoteId { get; } = "qadirect:" + Guid.NewGuid().ToString("N");
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public event Action? PacketAvailable;
    public event Action? Closed;
    public bool TryReceive(out IPacket packet)
    {
        bool ok = _incoming.TryDequeue(out var next);
        packet = next!;
        return ok;
    }
    public void Push(IPacket packet) { _incoming.Enqueue(packet); PacketAvailable?.Invoke(); }
    public void Send(byte[] frame)
    {
        var packet = PacketCodec.Decode(BitConverter.ToUInt16(frame, 4), frame.AsSpan(6));
        lock (_sent) _sent.Add(packet);
    }
    internal List<T> All<T>() where T : IPacket { lock (_sent) return _sent.OfType<T>().ToList(); }
    internal T Last<T>() where T : IPacket => All<T>().Last();
    public void Close() { if (Interlocked.Exchange(ref _closed, 1) == 0) Closed?.Invoke(); }
    public void Dispose() => Close();
}

// Two direct connections (host killer + one survivor) in a started match; no codec in the path.
internal sealed class DirectRig : IDisposable
{
    internal GameServer Server { get; }
    internal QaDirect K { get; } = new();
    internal QaDirect S { get; } = new();
    internal int KId, SId;
    internal uint Seq = 1;
    internal DirectRig(int seed = 5, bool start = true)
    {
        Server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed });
        Server.AddConnection(K);
        K.Push(new C2S_Join { Nickname = "K" });
        Server.Tick(0.05f);
        Server.AddConnection(S);
        S.Push(new C2S_Join { Nickname = "S" });
        Server.Tick(0.05f);
        KId = K.Last<S2C_JoinResult>().PlayerId;
        SId = S.Last<S2C_JoinResult>().PlayerId;
        if (!start) return;
        K.Push(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        K.Push(new C2S_StartMatch());
        Server.Tick(0.05f);
    }
    internal void Tick(int n = 1) { for (int i = 0; i < n; i++) Server.Tick(0.05f); }
    internal object P(int id) => Spy.Player(Server, id);
    internal PlayerState Entry(QaDirect c, int id) => c.Last<S2C_WorldState>().Players.Single(p => p.Id == id);
    public void Dispose() { K.Dispose(); S.Dispose(); Server.Dispose(); }
}

internal static class QaWire
{
    internal static bool ContainsFloat(byte[] frame, float value)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(needle, value);
        return frame.AsSpan().IndexOf(needle) >= 0;
    }

    // Floats a packet legitimately carries that are NOT positions of the entities being protected.
    internal static HashSet<int> NonPositionFloats(IPacket packet, int skipEntityId = int.MinValue)
    {
        var set = new HashSet<int>();
        void Add(float v) => set.Add(BitConverter.SingleToInt32Bits(v));
        if (packet is S2C_WorldState w)
        {
            Add(w.TimeLeft);
            foreach (var t in w.Terminals) { Add(t.X); Add(t.Z); }
            foreach (var p in w.Players) { Add(p.StatusTimer); if (p.Role == (byte)Role.Killer) { Add(p.X); Add(p.Z); } }
        }
        else if (packet is S2C_SkillEffect e) { Add(e.Duration); }
        return set;
    }

    // Reads everything waiting on the connection without storing it (long sweeps must not grow a packet list).
    internal static List<IPacket> Take(QaPeer peer)
    {
        var list = new List<IPacket>();
        while (peer.Connection.TryReceive(out var packet)) list.Add(packet);
        return list;
    }
}

// Observed counters for the long seeded sweeps.
internal sealed class SweepStats
{
    internal int Matches, KillerFrames, SurvivorFrames, AbsentChecks, DecoyEntries, AfterimageEntries, RealSignalEntries;
    internal int ByteCoincidences;
    internal readonly HashSet<byte> KillerSkills = new(), SurvivorSkillsCast = new(), KillerEffectsSeen = new();
    internal readonly Dictionary<string, int> Results = new();
    internal readonly List<string> Violations = new();
    internal void Violation(string text) { if (Violations.Count < 25) Violations.Add(text); }
}
