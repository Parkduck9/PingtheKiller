using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness.Tests;

internal enum H6Fault
{
    None, NoGameResult, DuplicateGameResult, EscapedEmpty, EscapedLastOnly, EscapedOther, WinnerKiller, ReasonTimeUp,
    LeakSkillEffect, LeakKillerEntry, ExitNeverOpens, NoCapture, NoRescue, ExecuteOnContinuedContact, RearmsInContact, PrematureEnd,
    LeakInUnknownPacket,
    // U3d broken line-of-sight variants (each implies the LineOfSight mode): the entry while blocked, to a spectator, to
    // every survivor when any one has sight, with the previous tick's position, with non-zero Flags/StatusTimer/CaptureCount,
    // and killer X/Z in a SkillEffect while the entry is legally visible.
    LosWhileBlocked, LosToSpectator, LosCrossRecipient, LosStalePosition, LosNonZeroFields, LosSkillEffect,
    // U3d a2 (QA F1): every 8th tick the killer gets no WorldState while every eligible survivor gets the entry, sight or not.
    LosUnjoinableTick
}

// Test-only scripted rules fake for H6, following the U2b contract (docs/briefs/U2b-a1.md) closely enough to
// play the scenario: 20 Hz ticks, FixedMap movement at server speeds with the U2a Signal-hold input expiry,
// hacking, capture/execution, rescue, escape, per-recipient masking and GameResult. It validates the harness,
// not Server.Core. Each H6Fault breaks exactly one behaviour.
internal sealed class ScenarioServer : IAsyncDisposable
{
    private const float Dt = 1f / GameConstants.TickRate;
    // The fake hacks a terminal alone in 10 s instead of 45 s: it validates the harness, not the rules
    // timings, and keeps each H6 run short. Switches use the real 15 s (H6 checks the hold time).
    private const float FakeHackSeconds = 10f;

    private sealed class Player(int id)
    {
        public int Id { get; } = id;
        public ulong Token { get; } = (ulong)(9000 + id);
        public Peer? Peer;
        public Role Role;
        public PlayerStatus Status;
        public float X, Z, Dx, Dz, StatusTimer, Yaw, PrevX, PrevZ;
        public double LastSignal = double.NegativeInfinity, InvulnerableUntil, RescueContact, ExecutionGraceUntil;
        public bool ExecutionArmed;
        public int MovingTicks;
        public byte CaptureCount;
        public bool Interact, Hacking, Rescuing;
        public uint Seq;
    }

    private sealed class Peer(TcpClient tcp, bool unknownFrames)
    {
        public TcpClient Tcp { get; } = tcp;
        public Channel<byte[]> Output { get; } = Channel.CreateUnbounded<byte[]>();
        public Player? Player;
        public void Send(IPacket packet)
        {
            if (unknownFrames) Output.Writer.TryWrite(LobbyServer.UnknownFrame(0x7E, 12, 0x7E));
            Output.Writer.TryWrite(PacketCodec.Encode(packet));
            if (unknownFrames && packet is S2C_WorldState) Output.Writer.TryWrite(LobbyServer.UnknownFrame(0x21, 16, 0x21));
        }
    }

    private readonly FixedMap map = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(330)); // GDD v2 H6 budget is 300 s
    private readonly object sync = new();
    private readonly List<Player> players = new();
    private readonly List<Peer> peers = new();
    private readonly List<Task> connections = new();
    private readonly Task accepting, ticking;
    private readonly H6Fault fault;
    private TerminalState[] terminals = Array.Empty<TerminalState>();
    private double[] progress = Array.Empty<double>();
    private byte required, bots, choice = (byte)RoleChoice.Random;
    private bool started, ended, powered, rescued;
    private readonly double[] switchSeconds = new double[2];
    private readonly bool[] exitOpen = new bool[2];
    private double collapseLeft = -1;
    private readonly List<int> escapedIds = new();
    private double now;
    private uint tick;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int MatchStarts { get; private set; }
    public bool UnexpectedFlow { get; private set; }
    public int Executions { get; private set; }
    public int GameResultsSent { get; private set; }
    // Future U2b: a Lagging survivor cannot be executed during its first 2 s after capture.
    public bool ExecutionGrace { get; init; }
    public float? DistanceToExitAtPrematureEnd { get; private set; }
    // U2c killer view: survivor entries with Id 0, Yaw/CaptureCount/StatusTimer 0, Signal-only flags, shuffled per
    // snapshot; terminal Progress quantised to 0 or 100. The killer's own entry keeps its real Id.
    public bool AnonymousKillerView { get; init; }
    // A well-formed unknown-Id frame (0x7E) before every packet and a 0x21 one (future Id) after every world.
    public bool UnknownFrames { get; init; }
    public int AnonymousWorldsSent { get; private set; }
    // U3d/U2e contract: a survivor-bound world carries the killer entry (real Id/Role/X/Z/Yaw/Status, Flags/StatusTimer/
    // CaptureCount 0) only for a Normal/Lagging recipient whose segment to the killer hits no wall (FakeSight). Players get a
    // Yaw from their move direction in this mode.
    public bool LineOfSight { get; init; }
    // The killer, A and every terminal stand in the wall-free rectangle at the exit's corner (OpenRoad), so A, who also walks
    // only inside it, never loses sight of the killer; B still starts at its spawn point.
    public bool OpenRoad { get; init; }
    public int KillerEntriesSent { get; private set; }
    public int BlockedWorldsSent { get; private set; }
    private bool Los => LineOfSight || fault >= H6Fault.LosWhileBlocked;
    private readonly Random shuffle = new(4243);

    private void Execute(Player s)
    {
        s.Status = PlayerStatus.Eliminated;
        s.StatusTimer = 0;
        Executions++;
    }

    public ScenarioServer(H6Fault fault = H6Fault.None)
    {
        this.fault = fault;
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
                var peer = new Peer(tcp, UnknownFrames);
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
                if (players.Count != 3 || bots != 0 || choice != (byte)RoleChoice.Killer)
                {
                    UnexpectedFlow = true;
                    break;
                }
                Start();
                break;
            case C2S_MoveInput move when started && !ended && peer.Player is { } mover:
                if (unchecked((int)(move.Seq - mover.Seq)) <= 0) break;
                mover.Seq = move.Seq;
                float x = move.DirX, z = move.DirZ;
                if (!float.IsFinite(x) || !float.IsFinite(z)) x = z = 0;
                double length = Math.Sqrt((double)x * x + (double)z * z);
                if (length > 1) { x = (float)(x / length); z = (float)(z / length); }
                mover.Dx = x;
                mover.Dz = z;
                if (x != 0 || z != 0) mover.LastSignal = now;
                break;
            case C2S_Interact interact when started && !ended && peer.Player is { Role: Role.Survivor } hacker:
                hacker.Interact = interact.Active == 1 && hacker.Status == PlayerStatus.Normal;
                break;
        }
    }

    private void Start()
    {
        started = true;
        MatchStarts++;
        int spawn = 1;
        foreach (var p in players)
        {
            p.Role = p.Id == 1 ? Role.Killer : Role.Survivor;
            // Keep A initially visible and B occluded so single-frame and per-recipient leak mutations remain non-vacuous.
            // A stands 10 m east of the killer spawn, on the open street towards terminal 4.
            var point = p.Role == Role.Killer ? map.SpawnPoints[0] : spawn++ == 1 ? new MapPoint(map.SpawnPoints[0].X + 10, map.SpawnPoints[0].Z + .5f) : map.SpawnPoints[2];
            if (OpenRoad && p.Role == Role.Killer) point = RoadPoint(.5f, .5f);
            else if (OpenRoad && spawn == 2) point = RoadPoint(.5f, .33f);
            (p.X, p.Z) = (point.X, point.Z);
            p.Peer?.Send(new S2C_MatchStart { Role = (byte)p.Role, SkillId = 0, Seed = 4242 });
        }
        terminals = map.CreateTerminals(players.Count - 1, out required);
        if (OpenRoad)
            for (int i = 0; i < terminals.Length; i++)
            {
                var spot = RoadPoint(i % 2 == 0 ? .25f : .75f, .15f + .05f * i);
                (terminals[i].X, terminals[i].Z) = (spot.X, spot.Z);
            }
        progress = new double[terminals.Length];
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
                    if (started && !ended) Step();
        }
        catch (OperationCanceledException) { }
    }

    private bool Signal(Player p) => now - p.LastSignal < GameConstants.SignalHoldSeconds - 1e-7;
    private Player Killer => players[0];
    private IEnumerable<Player> Survivors => players.Skip(1);
    // A point of the wall-free rectangle at the exit's corner, as fractions of its size: from x = 0 to the nearest building face
    // in the south half, and from z = 0 to the first building reaching into that band. Walls are the only line-of-sight blockers.
    private MapPoint RoadPoint(float fx, float fz)
    {
        float east = map.Walls.Skip(4).Where(w => w.MinZ < FixedMap.Size / 2).Min(w => w.MinX);
        float north = map.Walls.Skip(4).Where(w => w.MinX < east).Min(w => w.MinZ);
        return new MapPoint(east * fx, north * fz);
    }

    private static float Distance(Player p, float x, float z) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));

    // U2b order: inputs (applied on receipt) -> movement -> statuses/timers -> capture -> rescue -> hacking
    // -> exit/escape -> win check -> broadcast.
    private void Step()
    {
        now += Dt;
        tick++;
        foreach (var p in players)
        {
            (p.PrevX, p.PrevZ) = (p.X, p.Z);
            if (!Signal(p) || p.Status is PlayerStatus.Escaped or PlayerStatus.Eliminated) { p.Dx = p.Dz = 0; continue; }
            if (Los && (p.Dx != 0 || p.Dz != 0)) p.Yaw = MathF.Atan2(p.Dx, p.Dz);
            float speed = (p.Role == Role.Killer ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) *
                (p.Status == PlayerStatus.Lagging ? GameConstants.LaggingSpeedMultiplier : 1);
            var moved = map.Move(p.X, p.Z, p.Dx * speed * Dt, p.Dz * speed * Dt);
            (p.X, p.Z) = (moved.X, moved.Z);
        }
        foreach (var s in Survivors)
            if (s.Status == PlayerStatus.Lagging && (s.StatusTimer -= Dt) <= 0) { s.Status = PlayerStatus.Eliminated; s.StatusTimer = 0; }
        if (fault != H6Fault.NoCapture)
            foreach (var s in Survivors)
            {
                bool contact = Distance(s, Killer.X, Killer.Z) <= GameConstants.CaptureRadius;
                if (s.Status == PlayerStatus.Lagging)
                {
                    // U2b (GameServer.Rules.cs): armed only on a tick outside the radius; the capturing contact never
                    // executes; a touch during the grace is spent. The faults restore broken variants.
                    if (fault == H6Fault.ExecuteOnContinuedContact) { if (contact) Execute(s); }
                    else if (fault == H6Fault.RearmsInContact)
                    {
                        if (contact && s.ExecutionArmed) Execute(s);
                        else s.ExecutionArmed = true;
                    }
                    else if (!contact) s.ExecutionArmed = true;
                    else if (now < s.ExecutionGraceUntil) s.ExecutionArmed = false;
                    else if (s.ExecutionArmed) Execute(s);
                    continue;
                }
                if (!contact) continue;
                if (s.Status == PlayerStatus.Normal && now >= s.InvulnerableUntil)
                {
                    if (s.CaptureCount == 0)
                    {
                        s.Status = PlayerStatus.Lagging;
                        s.CaptureCount = 1;
                        s.StatusTimer = GameConstants.LaggingTimeoutSeconds;
                        s.RescueContact = 0;
                        s.ExecutionArmed = false;
                        s.ExecutionGraceUntil = now + (ExecutionGrace ? 2 : 0);
                    }
                    else s.Status = PlayerStatus.Eliminated;
                    s.Interact = false;
                }
            }
        foreach (var s in Survivors) s.Rescuing = false;
        foreach (var target in Survivors.Where(s => s.Status == PlayerStatus.Lagging).ToList())
        {
            var rescuers = Survivors.Where(r => r != target && r.Status == PlayerStatus.Normal && r.Interact &&
                Distance(r, target.X, target.Z) <= GameConstants.RescueRadius).ToList();
            if (rescuers.Count == 0) { target.RescueContact = 0; continue; }
            foreach (var r in rescuers) r.Rescuing = true;
            target.RescueContact += Dt;
            if (fault != H6Fault.NoRescue && target.RescueContact >= GameConstants.RescueTimeSeconds - 1e-6)
            {
                target.Status = PlayerStatus.Normal;
                target.StatusTimer = 0;
                target.RescueContact = 0;
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
                progress[i] = Math.Min(1, progress[i] + Dt / FakeHackSeconds * GameConstants.CoHackRate(hackers[i]) + 1e-9);
            terminals[i].Progress = (byte)(progress[i] >= 1 ? 100 : Math.Min(99, (int)(progress[i] * 100)));
        }
        if (terminals.Count(t => t.Progress == 100) >= required && fault != H6Fault.ExitNeverOpens) powered = true;
        // GDD v2 §3: a Normal survivor holding Interact at a powered switch fills it in 15 s (holders do not stack).
        if (collapseLeft > 0) collapseLeft = Math.Max(0, collapseLeft - Dt);
        for (int e = 0; e < exitOpen.Length && powered; e++)
        {
            if (exitOpen[e]) continue;
            var point = map.ExitSwitches[e];
            if (!Survivors.Any(s => s.Interact && s.Status == PlayerStatus.Normal && !s.Hacking &&
                Distance(s, point.X, point.Z) <= GameConstants.ExitSwitchRadius)) continue;
            switchSeconds[e] += Dt;
            if (switchSeconds[e] < GameConstants.ExitSwitchSeconds - 1e-6) continue;
            exitOpen[e] = true;
            if (collapseLeft < 0) collapseLeft = GameConstants.CollapseSeconds;
        }
        for (int e = 0; e < exitOpen.Length; e++)
            if (exitOpen[e])
                foreach (var s in Survivors)
                    if (s.Status == PlayerStatus.Normal && Distance(s, map.Exits[e].X, map.Exits[e].Z) <= GameConstants.ExitRadius)
                    {
                        s.Status = PlayerStatus.Escaped;
                        escapedIds.Add(s.Id);
                    }
        // PrematureEnd: a valid SurvivorEscaped result ~1 s after a survivor starts walking after the rescue,
        // long before it reaches the exit.
        foreach (var s in Survivors) s.MovingTicks = rescued && Signal(s) && (s.Dx != 0 || s.Dz != 0) ? s.MovingTicks + 1 : 0;
        if (fault == H6Fault.PrematureEnd && Survivors.FirstOrDefault(s => s.MovingTicks >= 20) is { } early)
        {
            DistanceToExitAtPrematureEnd = Distance(early, map.Exit.X, map.Exit.Z);
            End(Role.Survivor, GameEndReason.AllResolved, new List<int> { early.Id });
            return;
        }
        // GDD v2 §1: the match ends once nobody is Normal or Lagging; survivors win with at least half escaped.
        if (Survivors.All(s => s.Status is PlayerStatus.Escaped or PlayerStatus.Eliminated))
        {
            if (fault == H6Fault.LosToSpectator) Broadcast(); // one more world in which the escapers are spectators
            int count = Survivors.Count();
            End(escapedIds.Count * 2 >= count ? Role.Survivor : Role.Killer,
                escapedIds.Count == 0 ? GameEndReason.AllSurvivorsDown : GameEndReason.AllResolved, escapedIds);
        }
        else Broadcast();
    }

    private bool Visible(Player s) => s.Status is PlayerStatus.Normal or PlayerStatus.Lagging &&
        (Signal(s) || s.Status == PlayerStatus.Lagging || s.Hacking || s.Rescuing);

    private PlayerState State(Player p) => new()
    {
        Id = p.Id, Role = (byte)p.Role, Status = (byte)p.Status, X = p.X, Z = p.Z,
        Flags = (byte)(((p.Role == Role.Killer ? Signal(p) : Visible(p)) ? PlayerFlags.Signal : PlayerFlags.None) |
            (p.Hacking ? PlayerFlags.Hacking : 0) | (p.Rescuing ? PlayerFlags.Rescuing : 0)),
        StatusTimer = p.Status == PlayerStatus.Lagging ? p.StatusTimer : 0, CaptureCount = p.CaptureCount, Yaw = p.Yaw
    };

    private bool Sees(Player recipient) => recipient.Status is PlayerStatus.Normal or PlayerStatus.Lagging &&
        !FakeSight.Blocked(recipient.X, recipient.Z, Killer.X, Killer.Z);

    private PlayerState? KillerEntryFor(Player recipient)
    {
        bool sees = Sees(recipient);
        if (!sees && recipient.Status is PlayerStatus.Normal or PlayerStatus.Lagging) BlockedWorldsSent++;
        bool show = fault switch
        {
            H6Fault.LosWhileBlocked => recipient.Status is PlayerStatus.Normal or PlayerStatus.Lagging,
            H6Fault.LosUnjoinableTick when tick % 8 == 0 => recipient.Status is PlayerStatus.Normal or PlayerStatus.Lagging,
            H6Fault.LosCrossRecipient => Survivors.Any(Sees),
            H6Fault.LosToSpectator => sees || recipient.Status is PlayerStatus.Escaped or PlayerStatus.Eliminated,
            _ => sees
        };
        if (!show) return null;
        var entry = new PlayerState
        {
            Id = Killer.Id, Role = (byte)Role.Killer, Status = (byte)Killer.Status, X = Killer.X, Z = Killer.Z, Yaw = Killer.Yaw
        };
        if (fault == H6Fault.LosStalePosition) (entry.X, entry.Z) = (Killer.PrevX, Killer.PrevZ);
        if (fault == H6Fault.LosNonZeroFields)
            switch (tick % 3)
            {
                case 0: entry.Flags = (byte)PlayerFlags.Signal; break;
                case 1: entry.StatusTimer = 1.5f; break;
                default: entry.CaptureCount = 1; break;
            }
        KillerEntriesSent++;
        return entry;
    }

    private void Broadcast()
    {
        foreach (var recipient in players)
        {
            if (recipient.Peer == null) continue;
            if (fault == H6Fault.LosUnjoinableTick && tick % 8 == 0 && recipient.Role == Role.Killer) continue;
            var world = new S2C_WorldState
            {
                Tick = tick, TimeLeft = (float)(GameConstants.MatchLengthSeconds - now),
                TerminalsRequired = required, Terminals = terminals.ToList(), CollapseLeft = (float)collapseLeft
            };
            for (int e = 0; e < exitOpen.Length; e++)
                world.Exits.Add(new ExitState
                {
                    Id = (byte)e, X = map.Exits[e].X, Z = map.Exits[e].Z,
                    Flags = (byte)((powered ? ExitFlags.Powered : 0) | (exitOpen[e] ? ExitFlags.Open : 0)),
                    SwitchProgress = (byte)(exitOpen[e] ? 100 : Math.Min(99, (int)(switchSeconds[e] / GameConstants.ExitSwitchSeconds * 100)))
                });
            if (recipient.Role == Role.Killer)
            {
                world.Players.Add(State(recipient));
                world.Players.AddRange(Survivors.Where(Visible).Select(State));
                if (AnonymousKillerView) Anonymise(world);
            }
            else
            {
                world.ThreatLevel = PingThreat(Distance(recipient, Killer.X, Killer.Z));
                if (fault == H6Fault.LeakKillerEntry) world.Players.Add(State(Killer));
                world.Players.AddRange(Survivors.Select(State));
                if (Los && KillerEntryFor(recipient) is { } entry)
                {
                    world.Players.Insert(0, entry);
                    if (fault == H6Fault.LosSkillEffect && tick % 10 == 0)
                        recipient.Peer.Send(new S2C_SkillEffect { CasterId = Killer.Id, SkillId = 1, X = Killer.X, Z = Killer.Z, Duration = 3 });
                }
                if (fault == H6Fault.LeakSkillEffect && tick % 20 == 0)
                    recipient.Peer.Send(new S2C_SkillEffect { CasterId = Killer.Id, SkillId = 1, X = Killer.X, Z = Killer.Z, Duration = 3 });
                if (fault == H6Fault.LeakInUnknownPacket && tick % 20 == 0)
                {
                    var payload = new byte[12];
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(1), Killer.X);
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(6), Killer.Z);
                    recipient.Peer.Output.Writer.TryWrite(LobbyServer.UnknownFrame(0x7E, 12, 0x5A, payload));
                }
            }
            recipient.Peer.Send(world);
        }
    }

    private void Anonymise(S2C_WorldState world)
    {
        AnonymousWorldsSent++;
        world.Terminals = world.Terminals.Select(t => t with { Progress = (byte)(t.Progress == 100 ? 100 : 0) }).ToList();
        for (int i = 0; i < world.Players.Count; i++)
        {
            var p = world.Players[i];
            if (p.Role == (byte)Role.Killer) continue;
            world.Players[i] = p with { Id = 0, Yaw = 0, CaptureCount = 0, StatusTimer = 0, Flags = (byte)(p.Flags & (byte)PlayerFlags.Signal) };
        }
        for (int i = world.Players.Count - 1; i > 0; i--)
        {
            int j = shuffle.Next(i + 1);
            (world.Players[i], world.Players[j]) = (world.Players[j], world.Players[i]);
        }
    }

    private static byte PingThreat(float distance) =>
        (byte)(distance <= GameConstants.ThreatLevel3Distance ? 3 : distance <= GameConstants.ThreatLevel2Distance ? 2 :
            distance <= GameConstants.ThreatLevel1Distance ? 1 : 0);

    private void End(Role winner, GameEndReason reason, List<int> escaped)
    {
        ended = true;
        if (fault == H6Fault.NoGameResult) return;
        var ids = fault switch
        {
            H6Fault.EscapedEmpty => new List<int>(),
            H6Fault.EscapedLastOnly => escaped.Skip(escaped.Count - 1).ToList(),
            H6Fault.EscapedOther => escaped.Select((id, i) => i == 0 ? Killer.Id : id).ToList(),
            _ => escaped.Order().ToList()
        };
        var result = new S2C_GameResult
        {
            WinnerRole = (byte)(fault == H6Fault.WinnerKiller ? Role.Killer : winner),
            Reason = (byte)(fault == H6Fault.ReasonTimeUp ? GameEndReason.TimeUp : reason), EscapedIds = ids
        };
        foreach (var p in players)
        {
            p.Peer?.Send(result);
            GameResultsSent++;
            if (fault == H6Fault.DuplicateGameResult && p.Role == Role.Killer) p.Peer?.Send(result);
        }
        Lobby();
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
