using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests.QA.U3b;

// QA-owned rules fake for H6 (independent of the implementer's ScenarioServer, whose fault knob is private).
// Behaviour follows the U2b contract; every Knobs member breaks exactly one thing. Knobs defaults = correct control.
internal sealed class Knobs
{
    // movement
    public float SpeedFactor = 1f;                  // 0.9 = server 10 % slower than spec
    public bool NeverMove;                          // inputs accepted, positions never change
    public int InputDelayTicks;                     // inputs take effect N ticks late (timing-sensitivity probe)
    // liveness
    public int SilentAfterTick = int.MaxValue;      // nothing is sent after this tick (sockets stay open)
    public Func<Snapshot, int?>? ClosePlayerWhen;   // returns a player id whose socket is closed by the server
    // terminals / exit
    public int ForceExitOpenAtTick = -1;            // exit reported open although terminals are not done
    public bool ProgressCappedAt99;                 // terminals never show 100 (but the exit logic still counts them)
    // capture / rescue
    public bool CaptureKeepsNormal;                 // CaptureCount becomes 1 but Status stays Normal
    public byte? CaptureCountOnCapture;             // e.g. 0 or 2 instead of 1
    public bool CaptureExecutes;                    // capture goes straight to Eliminated
    // U2b execution rule: leave-and-re-enter. A captured (Lagging) survivor is disarmed; armed on any tick the killer is
    // outside 1.2 m; executed only on contact while armed and after the grace. ExecutionGraceSeconds 0 / 2 = both server versions.
    public float ExecutionGraceSeconds;
    public bool ExecuteOnContinuedContact;          // OLD literal rule: contact executes at once (broken fake)
    public bool RearmsInContact;                    // re-arms while still in contact: executes 2 ticks after capture (broken fake)
    public float ExecuteAfterContactSeconds;         // execute after this much CONTINUOUS contact (limit probe)
    public float? ExitRadiusOverride;               // escape radius other than the spec 2 m
    public bool ProgressDisplayLagsOneTick;          // terminals show last tick's progress while the exit logic is current
    public bool LaggingTimerZero;                   // StatusTimer reported 0 while Lagging
    public bool RescueDropsCaptureCount;            // rescue returns Normal but CaptureCount 0
    public bool RescueMakesPartnerLagging;          // rescue "completes" by flipping the RESCUER to Lagging (wrong survivor)
    public float HackRateFactor = 1f;               // 20 = terminals complete 20x faster than spec
    public float RescueSeconds = GameConstants.RescueTimeSeconds; // rescue completes after this contact time
    // end of match
    public bool PrematureEnd;                       // SurvivorEscaped sent ~1 s after the escaper starts walking
    public bool EndWithoutAnyWalk;                  // SurvivorEscaped sent the moment the rescue completes
    public Func<int, S2C_GameResult, IReadOnlyList<(S2C_GameResult Result, int DelayTicks)>>? ResultPlan;
    // survivor-bound leaks (called once per survivor-bound WorldState before it is sent)
    public Action<LeakContext>? SurvivorFrame;
}

internal sealed class Snapshot
{
    public uint Tick;
    public bool ALagging, ARescued, ExitOpen;
    public int MaxProgress;
}

internal sealed class LeakContext
{
    public uint Tick;
    public int RecipientId, KillerId;
    public float KillerX, KillerZ;
    public bool ALagging;
    public S2C_WorldState World = null!;
    public Action<IPacket> Send = null!;
    public IReadOnlyList<PlayerState> SurvivorEntries = Array.Empty<PlayerState>();
}

internal sealed class QaRulesServer : IAsyncDisposable
{
    private const float Dt = 1f / GameConstants.TickRate;
    // GDD v2 (U6a): the fake hacks a terminal alone in 10 s (it validates the harness, not the 45 s timing) and uses
    // the v2 co-hack rates, the 17 s exit switch, per-survivor escape and the v0.4 exit states.
    private const float FakeHackSeconds = 10f;

    private sealed class Player(int id)
    {
        public int Id { get; } = id;
        public ulong Token { get; } = (ulong)(9500 + id);
        public Peer? Peer;
        public Role Role;
        public PlayerStatus Status;
        public float X, Z, Dx, Dz, StatusTimer;
        public double LastSignal = double.NegativeInfinity, InvulnerableUntil, RescueContact;
        public byte CaptureCount;
        public bool Armed;
        public double ContactSeconds;
        public double GraceUntil;
        public bool Interact, Hacking, Rescuing;
        public uint Seq;
        public Queue<(uint DueTick, float Dx, float Dz)> Pending = new();
    }

    private sealed class Peer(TcpClient tcp)
    {
        public TcpClient Tcp { get; } = tcp;
        public Channel<byte[]> Output { get; } = Channel.CreateUnbounded<byte[]>();
        public Player? Player;
        public void Send(IPacket packet) => Output.Writer.TryWrite(PacketCodec.Encode(packet));
    }

    private readonly FixedMap map = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(335)); // GDD v2 H6 budget is 300 s
    private readonly object sync = new();
    private readonly List<Player> players = new();
    private readonly List<Peer> peers = new();
    private readonly List<Task> connections = new();
    private readonly List<(uint DueTick, Player To, S2C_GameResult Result)> delayed = new();
    private readonly Task accepting, ticking;
    private readonly Knobs knobs;
    private TerminalState[] terminals = Array.Empty<TerminalState>();
    private double[] progress = Array.Empty<double>();
    private byte[] lastShown = Array.Empty<byte>();
    private byte required, bots, choice = (byte)RoleChoice.Random;
    private bool started, ended, powered, exitOpen, rescued;
    private readonly double[] switchSeconds = new double[2];
    private readonly bool[] open = new bool[2];
    private readonly List<int> escapedIds = new();
    private double collapseLeft = -1;
    private int prematureMover;
    private double prematureAt;
    private double now;
    private uint tick;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int Executions { get; private set; }
    public int MatchStarts { get; private set; }
    public double? DistanceToExitAtPrematureEnd { get; private set; }
    public string? PrematureEscaperRole { get; private set; }
    public int ResultsSent { get; private set; }

    public QaRulesServer(Knobs? knobs = null)
    {
        this.knobs = knobs ?? new Knobs();
        listener.Start();
        accepting = AcceptAsync();
        ticking = TickAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var tcp = await listener.AcceptTcpClientAsync(lifetime.Token);
                tcp.NoDelay = true;
                var peer = new Peer(tcp);
                lock (sync)
                {
                    peers.Add(peer);
                    connections.Add(ConnectionAsync(peer));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task ConnectionAsync(Peer peer)
    {
        Task sender = SendAsync(peer);
        try
        {
            var assembler = new PacketAssembler();
            var buffer = new byte[8192];
            while (true)
            {
                int count = await peer.Tcp.GetStream().ReadAsync(buffer, lifetime.Token);
                if (count == 0) return;
                assembler.Append(buffer.AsSpan(0, count));
                while (true)
                {
                    var result = assembler.TryRead(out ushort id, out var payload);
                    if (result == AssembleResult.NeedMoreData) break;
                    if (result == AssembleResult.Malformed) return;
                    IPacket packet = PacketCodec.Decode(id, payload.Span);
                    lock (sync) Handle(peer, packet);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or PacketFormatException or ObjectDisposedException) { }
        finally
        {
            lock (sync)
                if (peer.Player is { } player && ReferenceEquals(player.Peer, peer))
                {
                    player.Peer = null;
                    player.Dx = player.Dz = 0;
                    player.Interact = false;
                }
            peer.Output.Writer.TryComplete();
            peer.Tcp.Dispose();
            await sender;
        }
    }

    private async Task SendAsync(Peer peer)
    {
        try
        {
            await foreach (byte[] bytes in peer.Output.Reader.ReadAllAsync(lifetime.Token))
                await peer.Tcp.GetStream().WriteAsync(bytes, lifetime.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
    }

    private void Handle(Peer peer, IPacket packet)
    {
        switch (packet)
        {
            case C2S_Ping ping:
                peer.Send(new S2C_Pong { Timestamp = ping.Timestamp });
                break;
            case C2S_Join join when peer.Player == null:
                var player = players.Find(p => join.ReconnectToken != 0 && p.Token == join.ReconnectToken);
                if (player == null)
                {
                    if (started || players.Count + bots >= 8) { peer.Send(new S2C_JoinResult { Result = 1 }); break; }
                    player = new Player(players.Count + 1);
                    players.Add(player);
                }
                player.Peer = peer;
                peer.Player = player;
                peer.Send(new S2C_JoinResult { PlayerId = player.Id, ReconnectToken = player.Token });
                if (!started) Lobby();
                break;
            case C2S_SetRole setRole when peer.Player?.Id == 1 && !started:
                choice = setRole.RoleChoice;
                Lobby();
                break;
            case C2S_SetBots setBots when peer.Player?.Id == 1 && !started:
                bots = (byte)Math.Clamp(setBots.Count, Math.Max(0, 2 - players.Count), 8 - players.Count);
                Lobby();
                break;
            case C2S_StartMatch when peer.Player?.Id == 1 && !started:
                if (players.Count != 3 || bots != 0 || choice != (byte)RoleChoice.Killer) break;
                Start();
                break;
            case C2S_MoveInput move when started && !ended && peer.Player is { } mover:
                if (unchecked((int)(move.Seq - mover.Seq)) <= 0) break;
                mover.Seq = move.Seq;
                float x = move.DirX, z = move.DirZ;
                if (!float.IsFinite(x) || !float.IsFinite(z)) x = z = 0;
                double length = Math.Sqrt((double)x * x + (double)z * z);
                if (length > 1) { x = (float)(x / length); z = (float)(z / length); }
                if (knobs.InputDelayTicks > 0) mover.Pending.Enqueue((tick + (uint)knobs.InputDelayTicks, x, z));
                else ApplyInput(mover, x, z);
                if (knobs.PrematureEnd && rescued && prematureMover == 0 && mover.Role == Role.Survivor && (x != 0 || z != 0))
                {
                    prematureMover = mover.Id;
                    prematureAt = now + 1.0;
                    PrematureEscaperRole = mover.Id == 2 ? "A" : "B";
                }
                break;
            case C2S_Interact interact when started && !ended && peer.Player is { Role: Role.Survivor } hacker:
                hacker.Interact = interact.Active == 1 && hacker.Status == PlayerStatus.Normal;
                break;
        }
    }

    private void ApplyInput(Player mover, float x, float z)
    {
        mover.Dx = x;
        mover.Dz = z;
        if (x != 0 || z != 0) mover.LastSignal = now;
    }

    private void Start()
    {
        started = true;
        MatchStarts++;
        int spawn = 1;
        foreach (var p in players)
        {
            p.Role = p.Id == 1 ? Role.Killer : Role.Survivor;
            var point = map.SpawnPoints[p.Role == Role.Killer ? 0 : spawn++];
            (p.X, p.Z) = (point.X, point.Z);
            p.Peer?.Send(new S2C_MatchStart { Role = (byte)p.Role, SkillId = 0, Seed = 4242 });
        }
        terminals = map.CreateTerminals(players.Count - 1, out required);
        progress = new double[terminals.Length];
        lastShown = new byte[terminals.Length];
    }

    private void Lobby()
    {
        var lobby = new S2C_LobbyState { HostRoleChoice = choice };
        int index = 0;
        foreach (var player in players)
            lobby.Slots[index++] = new Slot { PlayerId = player.Id, Nickname = "Human", IsHost = (byte)(player.Id == 1 ? 1 : 0) };
        for (int i = 0; i < bots; i++)
            lobby.Slots[index++] = new Slot { PlayerId = 100 + i, Nickname = "Bot", IsBot = 1 };
        foreach (var player in players) player.Peer?.Send(lobby);
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
                lock (sync)
                {
                    if (!started) continue;
                    if (!ended) Step();
                    FlushDelayed();
                }
        }
        catch (OperationCanceledException) { }
    }

    private bool Signal(Player p) => now - p.LastSignal < GameConstants.SignalHoldSeconds - 1e-7;
    private Player Killer => players[0];
    private IEnumerable<Player> Survivors => players.Skip(1);
    private static float Distance(Player p, float x, float z) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));

    private void Step()
    {
        now += Dt;
        tick++;
        foreach (var p in players)
            while (p.Pending.Count > 0 && p.Pending.Peek().DueTick <= tick)
            {
                var (_, dx, dz) = p.Pending.Dequeue();
                ApplyInput(p, dx, dz);
            }
        foreach (var p in players)
        {
            if (!Signal(p) || p.Status is PlayerStatus.Escaped or PlayerStatus.Eliminated || knobs.NeverMove) { p.Dx = p.Dz = 0; continue; }
            float speed = (p.Role == Role.Killer ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) *
                (p.Status == PlayerStatus.Lagging ? GameConstants.LaggingSpeedMultiplier : 1) * knobs.SpeedFactor;
            var moved = map.Move(p.X, p.Z, p.Dx * speed * Dt, p.Dz * speed * Dt);
            (p.X, p.Z) = (moved.X, moved.Z);
        }
        foreach (var s in Survivors)
            if (s.Status == PlayerStatus.Lagging && (s.StatusTimer -= Dt) <= 0) { s.Status = PlayerStatus.Eliminated; s.StatusTimer = 0; }
        foreach (var s in Survivors)
        {
            bool contact = Distance(s, Killer.X, Killer.Z) <= GameConstants.CaptureRadius;
            if (s.Status == PlayerStatus.Lagging)
            {
                bool execute;
                if (s.Status == PlayerStatus.Lagging && contact) s.ContactSeconds += Dt; else s.ContactSeconds = 0;
                if (knobs.ExecuteAfterContactSeconds > 0) execute = contact && s.ContactSeconds >= knobs.ExecuteAfterContactSeconds; // delayed literal rule
                else if (knobs.ExecuteOnContinuedContact) execute = contact; // old literal rule (broken fake)
                else if (!contact) { s.Armed = true; execute = false; }
                else if (now < s.GraceUntil) { s.Armed = false; execute = false; }
                else if (knobs.RearmsInContact) { execute = s.Armed; s.Armed = true; } // re-arms while still touching (broken fake)
                else execute = s.Armed;
                if (execute) { s.Status = PlayerStatus.Eliminated; s.StatusTimer = 0; Executions++; }
                continue;
            }
            if (!contact) continue;
            if (s.Status == PlayerStatus.Normal && now >= s.InvulnerableUntil)
            {
                if (s.CaptureCount == 0)
                {
                    s.Status = knobs.CaptureKeepsNormal ? PlayerStatus.Normal :
                        knobs.CaptureExecutes ? PlayerStatus.Eliminated : PlayerStatus.Lagging;
                    s.CaptureCount = knobs.CaptureCountOnCapture ?? 1;
                    s.StatusTimer = GameConstants.LaggingTimeoutSeconds;
                    s.RescueContact = 0;
                    s.Armed = false;
                    s.GraceUntil = now + knobs.ExecutionGraceSeconds;
                    if (knobs.CaptureKeepsNormal) s.InvulnerableUntil = now + 100; // do not re-trigger forever
                }
                else s.Status = PlayerStatus.Eliminated;
                s.Interact = false;
            }
        }
        foreach (var s in Survivors) s.Rescuing = false;
        foreach (var target in Survivors.Where(s => s.Status == PlayerStatus.Lagging).ToList())
        {
            var rescuers = Survivors.Where(r => r != target && r.Status == PlayerStatus.Normal &&
                Distance(r, target.X, target.Z) <= GameConstants.RescueRadius).ToList();
            if (rescuers.Count == 0) { target.RescueContact = 0; continue; }
            foreach (var r in rescuers) r.Rescuing = true;
            target.RescueContact += Dt;
            if (target.RescueContact >= knobs.RescueSeconds - 1e-6)
            {
                if (knobs.RescueMakesPartnerLagging)
                {
                    foreach (var r in rescuers) { r.Status = PlayerStatus.Lagging; r.StatusTimer = 60; }
                    target.RescueContact = 0;
                    continue;
                }
                target.Status = PlayerStatus.Normal;
                target.StatusTimer = 0;
                target.RescueContact = 0;
                if (knobs.RescueDropsCaptureCount) target.CaptureCount = 0;
                target.InvulnerableUntil = now + GameConstants.PostRescueInvulnerabilitySeconds;
                rescued = true;
            }
        }
        var hackers = new int[terminals.Length];
        foreach (var s in Survivors)
        {
            s.Hacking = false;
            if (!s.Interact || s.Status != PlayerStatus.Normal) continue;
            int nearest = -1;
            for (int i = 0; i < terminals.Length; i++)
                if (progress[i] < 1 && Distance(s, terminals[i].X, terminals[i].Z) <= GameConstants.TerminalRadius &&
                    (nearest < 0 || Distance(s, terminals[i].X, terminals[i].Z) < Distance(s, terminals[nearest].X, terminals[nearest].Z)))
                    nearest = i;
            if (nearest < 0) continue;
            hackers[nearest]++;
            s.Hacking = true;
        }
        for (int i = 0; i < terminals.Length; i++)
        {
            if (hackers[i] > 0)
                progress[i] = Math.Min(1, progress[i] + Dt * knobs.HackRateFactor / FakeHackSeconds * GameConstants.CoHackRate(hackers[i]) + 1e-9);
            int shown = progress[i] >= 1 ? 100 : Math.Min(99, (int)(progress[i] * 100));
            byte shownByte = (byte)(knobs.ProgressCappedAt99 ? Math.Min(99, shown) : shown);
            terminals[i].Progress = knobs.ProgressDisplayLagsOneTick ? lastShown[i] : shownByte;
            lastShown[i] = shownByte;
        }
        if (progress.Count(p => p >= 1) >= required) powered = true;
        // Forced: exit 0 powered and open without terminals or switch.
        if (knobs.ForceExitOpenAtTick >= 0 && tick >= knobs.ForceExitOpenAtTick) powered = open[0] = true;
        if (collapseLeft > 0) collapseLeft = Math.Max(0, collapseLeft - Dt);
        for (int e = 0; e < open.Length && powered; e++)
        {
            if (open[e]) continue;
            var point = map.ExitSwitches[e];
            if (!Survivors.Any(s => s.Interact && s.Status == PlayerStatus.Normal && !s.Hacking &&
                Distance(s, point.X, point.Z) <= GameConstants.ExitSwitchRadius)) continue;
            switchSeconds[e] += Dt;
            if (switchSeconds[e] >= GameConstants.ExitSwitchSeconds - 1e-6) open[e] = true;
        }
        exitOpen = open.Any(o => o);
        if (exitOpen && collapseLeft < 0) collapseLeft = GameConstants.CollapseSeconds;
        for (int e = 0; e < open.Length; e++)
            if (open[e])
                foreach (var s in Survivors)
                    if (s.Status == PlayerStatus.Normal && Distance(s, map.Exits[e].X, map.Exits[e].Z) <= (knobs.ExitRadiusOverride ?? GameConstants.ExitRadius))
                    {
                        s.Status = PlayerStatus.Escaped;
                        escapedIds.Add(s.Id);
                    }
        if (knobs.EndWithoutAnyWalk && rescued) { End(Role.Survivor, GameEndReason.AllResolved, new List<int> { 2, 3 }); return; }
        if (prematureMover != 0 && now >= prematureAt)
        {
            var mover = players.First(p => p.Id == prematureMover);
            DistanceToExitAtPrematureEnd = map.Exits.Min(e => Distance(mover, e.X, e.Z));
            End(Role.Survivor, GameEndReason.AllResolved, new List<int> { 2, 3 });
            return;
        }
        // GDD v2 §1: the match ends once nobody is Normal or Lagging; survivors win with at least half escaped.
        if (Survivors.All(s => s.Status is PlayerStatus.Escaped or PlayerStatus.Eliminated))
            End(escapedIds.Count * 2 >= Survivors.Count() ? Role.Survivor : Role.Killer,
                escapedIds.Count == 0 ? GameEndReason.AllSurvivorsDown : GameEndReason.AllResolved, escapedIds.Order().ToList());
        else Broadcast();
        MaybeClose();
    }

    private void MaybeClose()
    {
        if (knobs.ClosePlayerWhen == null) return;
        var a = players.Count > 1 ? players[1] : null;
        var snap = new Snapshot
        {
            Tick = tick, ALagging = a?.Status == PlayerStatus.Lagging, ARescued = rescued, ExitOpen = exitOpen,
            MaxProgress = terminals.Length == 0 ? 0 : terminals.Max(t => t.Progress)
        };
        if (knobs.ClosePlayerWhen(snap) is int id && players.Find(p => p.Id == id)?.Peer is { } peer)
        {
            peer.Tcp.Dispose();
            players.Find(p => p.Id == id)!.Peer = null;
        }
    }

    private bool Visible(Player s) => s.Status is PlayerStatus.Normal or PlayerStatus.Lagging &&
        (Signal(s) || s.Status == PlayerStatus.Lagging || s.Hacking || s.Rescuing);

    private PlayerState State(Player p) => new()
    {
        Id = p.Id, Role = (byte)p.Role, Status = (byte)p.Status, X = p.X, Z = p.Z,
        Flags = (byte)(((p.Role == Role.Killer ? Signal(p) : Visible(p)) ? PlayerFlags.Signal : PlayerFlags.None) |
            (p.Hacking ? PlayerFlags.Hacking : 0) | (p.Rescuing ? PlayerFlags.Rescuing : 0)),
        StatusTimer = p.Status == PlayerStatus.Lagging && !knobs.LaggingTimerZero ? p.StatusTimer : 0, CaptureCount = p.CaptureCount
    };

    private void Broadcast()
    {
        if (tick > (uint)knobs.SilentAfterTick) return;
        foreach (var recipient in players)
        {
            if (recipient.Peer == null) continue;
            var world = new S2C_WorldState
            {
                Tick = tick, TimeLeft = (float)(GameConstants.MatchLengthSeconds - now),
                TerminalsRequired = required, Terminals = terminals.ToList(), CollapseLeft = (float)collapseLeft
            };
            for (int e = 0; e < open.Length; e++)
                world.Exits.Add(new ExitState
                {
                    Id = (byte)e, X = map.Exits[e].X, Z = map.Exits[e].Z,
                    Flags = (byte)((powered ? ExitFlags.Powered : 0) | (open[e] ? ExitFlags.Open : 0)),
                    SwitchProgress = (byte)(open[e] ? 100 : Math.Min(99, (int)(switchSeconds[e] / GameConstants.ExitSwitchSeconds * 100)))
                });
            if (recipient.Role == Role.Killer)
            {
                world.Players.Add(State(recipient));
                world.Players.AddRange(Survivors.Where(Visible).Select(State));
            }
            else
            {
                world.ThreatLevel = PingThreat(Distance(recipient, Killer.X, Killer.Z));
                world.Players.AddRange(Survivors.Select(State));
                knobs.SurvivorFrame?.Invoke(new LeakContext
                {
                    Tick = tick, RecipientId = recipient.Id, KillerId = Killer.Id, KillerX = Killer.X, KillerZ = Killer.Z,
                    ALagging = players[1].Status == PlayerStatus.Lagging, World = world, Send = recipient.Peer.Send,
                    SurvivorEntries = world.Players.ToList()
                });
            }
            recipient.Peer.Send(world);
        }
    }

    private static byte PingThreat(float distance) =>
        (byte)(distance <= GameConstants.ThreatLevel3Distance ? 3 : distance <= GameConstants.ThreatLevel2Distance ? 2 :
            distance <= GameConstants.ThreatLevel1Distance ? 1 : 0);

    private void End(Role winner, GameEndReason reason, List<int> escaped)
    {
        ended = true;
        var baseline = new S2C_GameResult { WinnerRole = (byte)winner, Reason = (byte)reason, EscapedIds = escaped };
        foreach (var p in players)
        {
            var plan = knobs.ResultPlan?.Invoke(p.Id, baseline) ?? new[] { (baseline, 0) };
            foreach (var (result, delay) in plan)
            {
                if (delay <= 0) { p.Peer?.Send(result); ResultsSent++; }
                else delayed.Add(((uint)delay, p, result));
            }
        }
        Lobby();
    }

    private uint postEnd;

    // Delayed GameResult copies are due N ticks after the match ended.
    private void FlushDelayed()
    {
        if (!ended) return;
        postEnd++;
        for (int i = delayed.Count - 1; i >= 0; i--)
            if (delayed[i].DueTick <= postEnd)
            {
                delayed[i].To.Peer?.Send(delayed[i].Result);
                ResultsSent++;
                delayed.RemoveAt(i);
            }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        await accepting.WaitAsync(TimeSpan.FromSeconds(3));
        lock (sync) foreach (var peer in peers) peer.Tcp.Dispose();
        await Task.WhenAll(connections.Append(ticking)).WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Dispose();
    }
}
