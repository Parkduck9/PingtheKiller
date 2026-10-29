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
    None, NoGameResult, DuplicateGameResult, EscapedEmpty, EscapedBoth, EscapedOther, WinnerKiller, ReasonTimeUp,
    LeakSkillEffect, LeakKillerEntry, ExitNeverOpens, NoCapture, NoRescue, ExecuteOnContinuedContact, RearmsInContact, PrematureEnd,
    LeakInUnknownPacket
}

// Test-only scripted rules fake for H6, following the U2b contract (docs/briefs/U2b-a1.md) closely enough to
// play the scenario: 20 Hz ticks, FixedMap movement at server speeds with the U2a Signal-hold input expiry,
// hacking, capture/execution, rescue, escape, per-recipient masking and GameResult. It validates the harness,
// not Server.Core. Each H6Fault breaks exactly one behaviour.
internal sealed class ScenarioServer : IAsyncDisposable
{
    private const float Dt = 1f / GameConstants.TickRate;

    private sealed class Player(int id)
    {
        public int Id { get; } = id;
        public ulong Token { get; } = (ulong)(9000 + id);
        public Peer? Peer;
        public Role Role;
        public PlayerStatus Status;
        public float X, Z, Dx, Dz, StatusTimer;
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
            if (unknownFrames && packet is S2C_WorldState) Output.Writer.TryWrite(LobbyServer.UnknownFrame(0x11, 16, 0x11));
        }
    }

    private readonly FixedMap map = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(170));
    private readonly object sync = new();
    private readonly List<Player> players = new();
    private readonly List<Peer> peers = new();
    private readonly List<Task> connections = new();
    private readonly Task accepting, ticking;
    private readonly H6Fault fault;
    private TerminalState[] terminals = Array.Empty<TerminalState>();
    private double[] progress = Array.Empty<double>();
    private byte required, bots, choice = (byte)RoleChoice.Random;
    private bool started, ended, exitOpen, rescued;
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
    // A well-formed unknown-Id frame (0x7E) before every packet and a 0x11 one (future Id) after every world.
    public bool UnknownFrames { get; init; }
    public int AnonymousWorldsSent { get; private set; }
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
            var point = map.SpawnPoints[p.Role == Role.Killer ? 0 : spawn++];
            (p.X, p.Z) = (point.X, point.Z);
            p.Peer?.Send(new S2C_MatchStart { Role = (byte)p.Role, SkillId = 0, Seed = 4242 });
        }
        terminals = map.CreateTerminals(players.Count - 1, out required);
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
    private static float Distance(Player p, float x, float z) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));

    // U2b order: inputs (applied on receipt) -> movement -> statuses/timers -> capture -> rescue -> hacking
    // -> exit/escape -> win check -> broadcast.
    private void Step()
    {
        now += Dt;
        tick++;
        foreach (var p in players)
        {
            if (!Signal(p) || p.Status is PlayerStatus.Escaped or PlayerStatus.Eliminated) { p.Dx = p.Dz = 0; continue; }
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
            var rescuers = Survivors.Where(r => r != target && r.Status == PlayerStatus.Normal &&
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
                progress[i] = Math.Min(1, progress[i] + Dt / GameConstants.TerminalHackTimeSeconds * Math.Min(hackers[i], GameConstants.MaxCoHackMultiplier) + 1e-9);
            terminals[i].Progress = (byte)(progress[i] >= 1 ? 100 : Math.Min(99, (int)(progress[i] * 100)));
        }
        if (terminals.Count(t => t.Progress == 100) >= required && fault != H6Fault.ExitNeverOpens) exitOpen = true;
        var escaped = new List<int>();
        if (exitOpen)
            foreach (var s in Survivors)
                if (s.Status == PlayerStatus.Normal && Distance(s, map.Exit.X, map.Exit.Z) <= GameConstants.ExitRadius)
                {
                    s.Status = PlayerStatus.Escaped;
                    escaped.Add(s.Id);
                }
        // PrematureEnd: a valid SurvivorEscaped result ~1 s after a survivor starts walking after the rescue,
        // long before it reaches the exit.
        foreach (var s in Survivors) s.MovingTicks = rescued && Signal(s) && (s.Dx != 0 || s.Dz != 0) ? s.MovingTicks + 1 : 0;
        if (fault == H6Fault.PrematureEnd && Survivors.FirstOrDefault(s => s.MovingTicks >= 20) is { } early)
        {
            DistanceToExitAtPrematureEnd = Distance(early, map.Exit.X, map.Exit.Z);
            End(Role.Survivor, GameEndReason.SurvivorEscaped, new List<int> { early.Id });
            return;
        }
        if (escaped.Count > 0) End(Role.Survivor, GameEndReason.SurvivorEscaped, escaped);
        else if (Survivors.All(s => s.Status != PlayerStatus.Normal)) End(Role.Killer, GameEndReason.AllSurvivorsDown, escaped);
        else Broadcast();
    }

    private bool Visible(Player s) => s.Status is PlayerStatus.Normal or PlayerStatus.Lagging &&
        (Signal(s) || s.Status == PlayerStatus.Lagging || s.Hacking || s.Rescuing);

    private PlayerState State(Player p) => new()
    {
        Id = p.Id, Role = (byte)p.Role, Status = (byte)p.Status, X = p.X, Z = p.Z,
        Flags = (byte)(((p.Role == Role.Killer ? Signal(p) : Visible(p)) ? PlayerFlags.Signal : PlayerFlags.None) |
            (p.Hacking ? PlayerFlags.Hacking : 0) | (p.Rescuing ? PlayerFlags.Rescuing : 0)),
        StatusTimer = p.Status == PlayerStatus.Lagging ? p.StatusTimer : 0, CaptureCount = p.CaptureCount
    };

    private void Broadcast()
    {
        foreach (var recipient in players)
        {
            if (recipient.Peer == null) continue;
            var world = new S2C_WorldState
            {
                Tick = tick, TimeLeft = (float)(GameConstants.MatchLengthSeconds - now),
                ExitOpen = (byte)(exitOpen ? 1 : 0), TerminalsRequired = required, Terminals = terminals.ToList()
            };
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
            H6Fault.EscapedBoth => Survivors.Select(s => s.Id).ToList(),
            H6Fault.EscapedOther => Survivors.Select(s => s.Id).Where(id => !escaped.Contains(id)).ToList(),
            _ => escaped
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
