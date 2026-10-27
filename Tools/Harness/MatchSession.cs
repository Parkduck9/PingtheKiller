using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

// H2-H5 share one real match: reconnecting does not reset an U2a server to a lobby.
public sealed class MatchSession : IAsyncDisposable
{
    private readonly Options options;
    private uint sequence;
    public WireClient Killer { get; }
    public WireClient Survivor { get; private set; }
    public S2C_JoinResult KillerIdentity { get; }
    public S2C_JoinResult SurvivorIdentity { get; private set; }

    private MatchSession(Options options, WireClient killer, WireClient survivor,
        S2C_JoinResult killerIdentity, S2C_JoinResult survivorIdentity)
    {
        this.options = options;
        Killer = killer;
        Survivor = survivor;
        KillerIdentity = killerIdentity;
        SurvivorIdentity = survivorIdentity;
    }

    public uint NextSequence() => ++sequence;

    public static async Task<S2C_JoinResult> JoinAsync(WireClient client, string nickname, ulong token, CancellationToken ct)
    {
        await client.SendAsync(new C2S_Join { Nickname = nickname, ReconnectToken = token }, ct);
        var join = (S2C_JoinResult)(await client.ReadAsync<S2C_JoinResult>(ct)).Packet;
        Check.Require(join.Result == 0, $"Join rejected: result={join.Result}.");
        Check.Require(join.ReconnectToken != 0, "Server returned a zero reconnect token.");
        return join;
    }

    public static async Task<MatchSession> CreateAsync(Options options, CancellationToken ct)
    {
        WireClient killer = await WireClient.ConnectAsync(options, ct);
        WireClient? survivor = null;
        try
        {
            var killerId = await JoinAsync(killer, "Harness Host", 0, ct);
            var lobby = (S2C_LobbyState)(await killer.ReadAsync<S2C_LobbyState>(ct)).Packet;
            Check.Require(lobby.Slots.Any(s => s.PlayerId == killerId.PlayerId && s.IsHost != 0),
                "Harness must be the first human/host of a fresh lobby; start a new server WITHOUT --autostart.");
            // Free a human slot even with --bots 7. U2a may clamp the request to
            // one bot while only one human is present (minimum two participants).
            await killer.SendAsync(new C2S_SetBots { Count = 0 }, ct);
            await killer.ReadAsync<S2C_LobbyState>(ct, p => p.Slots.Count(s => s.IsBot != 0) <= 1);
            survivor = await WireClient.ConnectAsync(options, ct);
            var survivorId = await JoinAsync(survivor, "Harness Survivor", 0, ct);
            Check.Require(killerId.PlayerId != survivorId.PlayerId, "Two humans received the same PlayerId.");
            await killer.SendAsync(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer }, ct);
            await killer.SendAsync(new C2S_SetBots { Count = 6 }, ct);
            await killer.ReadAsync<S2C_LobbyState>(ct, p => p.HostRoleChoice == (byte)RoleChoice.Killer &&
                p.Slots.Count(s => s.IsBot != 0) == 6 &&
                p.Slots.Any(s => s.IsBot == 0 && s.PlayerId == killerId.PlayerId) &&
                p.Slots.Any(s => s.IsBot == 0 && s.PlayerId == survivorId.PlayerId));
            await killer.SendAsync(new C2S_StartMatch(), ct);
            var hostStart = (S2C_MatchStart)(await killer.ReadAsync<S2C_MatchStart>(ct)).Packet;
            var guestStart = (S2C_MatchStart)(await survivor.ReadAsync<S2C_MatchStart>(ct)).Packet;
            Check.Require(hostStart.Role == (byte)Role.Killer && guestStart.Role == (byte)Role.Survivor,
                "SetRole(Killer) did not assign the harness host Killer and the other human Survivor.");
            Check.Require(hostStart.Seed == guestStart.Seed, "Clients received different match seeds.");
            return new MatchSession(options, killer, survivor, killerId, survivorId);
        }
        catch
        {
            if (survivor != null) await survivor.DisposeAsync();
            await killer.DisposeAsync();
            throw;
        }
    }

    public async Task<S2C_JoinResult> ReconnectSurvivorAsync(CancellationToken ct, bool requireSameIdentity = true)
    {
        var previous = SurvivorIdentity;
        await Survivor.DisposeAsync();
        var replacement = await WireClient.ConnectAsync(options, ct);
        Survivor = replacement;
        var identity = await JoinAsync(replacement, "Harness Survivor", previous.ReconnectToken, ct);
        SurvivorIdentity = identity;
        if (requireSameIdentity)
            Check.Require(identity.PlayerId == previous.PlayerId,
                $"Reconnect changed PlayerId {previous.PlayerId} -> {identity.PlayerId}.");
        return identity;
    }

    public async ValueTask DisposeAsync()
    {
        await Survivor.DisposeAsync();
        await Killer.DisposeAsync();
    }
}
