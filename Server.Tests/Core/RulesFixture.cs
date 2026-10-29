using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

internal static class PeerExtensions
{
    internal static List<T> All<T>(this Peer peer) where T : IPacket
    {
        peer.Drain();
        return peer.Packets.OfType<T>().ToList();
    }
}

// The killer view is anonymous (Id 0, shuffled). Tests identify entries through the server's ground-truth labels,
// which are index-aligned with the latest killer-bound WorldState.
internal static class KillerTruth
{
    internal static int? Index(GameServer server, S2C_WorldState world, int id, bool phantom = false)
    {
        var sources = server.KillerViewSources;
        Assert.Equal(sources.Count, world.Players.Count);
        for (int i = 0; i < sources.Count; i++)
            if (sources[i].Phantom == phantom && sources[i].Label == id) return i;
        return null;
    }
    internal static PlayerState? Entry(GameServer server, S2C_WorldState world, int id) =>
        Index(server, world, id) is int i ? world.Players[i] : null;
    internal static bool Has(GameServer server, S2C_WorldState world, int id) => Index(server, world, id) != null;
    internal static bool IsAfterimage(GameServer server, S2C_WorldState world, int id) =>
        Index(server, world, id) is int i && server.KillerViewSources[i].Afterimage;
    internal static List<PlayerState> Phantoms(GameServer server, S2C_WorldState world)
    {
        var sources = server.KillerViewSources;
        Assert.Equal(sources.Count, world.Players.Count);
        return world.Players.Where((p, i) => sources[i].Phantom).ToList();
    }
}

// A started match of one human killer and N human survivors (no bots), optionally with required skills.
internal sealed class RulesMatch : IDisposable
{
    internal RulesMatch(CoreFixture f, Peer killer, Peer[] survivors, int seed)
    {
        F = f; Killer = killer; Survivors = survivors; Seed = seed;
    }
    internal CoreFixture F { get; }
    internal Peer Killer { get; }
    internal Peer[] Survivors { get; }
    internal int Seed { get; }
    internal Peer this[int index] => Survivors[index];
    public void Dispose() => F.Dispose();

    internal void Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++) F.Tick();
    }

    // Skill ids are random per match; the search over seeds is deterministic and black-box (MatchStart only).
    internal static RulesMatch Start(int survivors, int killerSkill = -1, params int[] survivorSkills)
    {
        for (int seed = 1; seed < 20000; seed++)
        {
            var f = new CoreFixture(seed: seed);
            var killer = f.Join("Killer");
            var list = new Peer[survivors];
            for (int i = 0; i < survivors; i++) list[i] = f.Join("S" + i);
            killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            killer.Send(new C2S_StartMatch());
            f.Tick();
            bool ok = killer.Last<S2C_MatchStart>().Role == (byte)Role.Killer &&
                (killerSkill < 0 || killer.Last<S2C_MatchStart>().SkillId == killerSkill);
            for (int i = 0; ok && i < survivorSkills.Length; i++)
                ok = survivorSkills[i] < 0 || list[i].Last<S2C_MatchStart>().SkillId == survivorSkills[i];
            if (ok) return new RulesMatch(f, killer, list, seed);
            f.Dispose();
        }
        throw new InvalidOperationException("No seed assigns the requested skills.");
    }

    // Walks straight to (x, z) at the given speed, resending input every tick and shortening the final step.
    internal void WalkTo(Peer peer, float x, float z, float speed)
    {
        for (int i = 0; i < 4000; i++)
        {
            var self = peer.Self();
            float dx = x - self.X, dz = z - self.Z;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance < 0.001f) break;
            float step = speed / GameConstants.TickRate;
            float scale = distance > step ? 1 / distance : 1 / step;
            peer.Move(dx * scale, dz * scale);
            F.Tick();
        }
        peer.Move(0, 0);
        var end = peer.Self();
        Assert.True(MathF.Abs(end.X - x) < 0.01f && MathF.Abs(end.Z - z) < 0.01f,
            $"walk to ({x},{z}) stopped at ({end.X},{end.Z})");
    }

    // One tick of movement in a direction, then an explicit stop so the input hold does not carry it further.
    internal void Step(Peer peer, float x, float z)
    {
        peer.Move(x, z);
        F.Tick();
        peer.Move(0, 0);
    }

    internal void WalkPath(Peer peer, float speed, params (float X, float Z)[] points)
    {
        foreach (var point in points) WalkTo(peer, point.X, point.Z, speed);
    }

    internal static PlayerState? Entry(S2C_WorldState world, int id)
    {
        foreach (var player in world.Players) if (player.Id == id) return player;
        return null;
    }
    internal PlayerState? KillerSees(Peer survivor) => KillerTruth.Entry(F.Server, Killer.Last<S2C_WorldState>(), survivor.Id);
    internal PlayerState? KillerEntry(S2C_WorldState world, int id) => KillerTruth.Entry(F.Server, world, id);
    internal bool KillerSeesAfterimage(Peer survivor) => KillerTruth.IsAfterimage(F.Server, Killer.Last<S2C_WorldState>(), survivor.Id);
    internal List<PlayerState> KillerPhantoms() => KillerTruth.Phantoms(F.Server, Killer.Last<S2C_WorldState>());
    internal PlayerState Truth(Peer survivor) => Entry(Survivors[0].Last<S2C_WorldState>(), survivor.Id)!.Value;

    internal static float Distance(PlayerState a, float x, float z) => MathF.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

    // Both survivors co-hack terminal 0 (10,25) and terminal 2 (49,25); 2 survivors need 2 of 4 terminals.
    internal void OpenExitWithTwoSurvivors()
    {
        var a = Survivors[0];
        var b = Survivors[1];
        WalkTo(a, 9.5f, 25.5f, GameConstants.SurvivorSpeed);
        WalkTo(b, 10.5f, 25.5f, GameConstants.SurvivorSpeed);
        a.Send(new C2S_Interact { Active = 1 });
        b.Send(new C2S_Interact { Active = 1 });
        Tick(101);
        Assert.Equal(100, Killer.Last<S2C_WorldState>().Terminals[0].Progress);
        a.Send(new C2S_Interact { Active = 0 });
        b.Send(new C2S_Interact { Active = 0 });
        var route = new[] { (12f, 34f), (21f, 34f), (30f, 27f) };
        WalkPath(a, GameConstants.SurvivorSpeed, route);
        WalkTo(a, 48f, 25f, GameConstants.SurvivorSpeed);
        WalkPath(b, GameConstants.SurvivorSpeed, route);
        WalkTo(b, 50f, 25.5f, GameConstants.SurvivorSpeed);
        Assert.Equal(0, Killer.Last<S2C_WorldState>().ExitOpen);
        Assert.Equal(1, Killer.Last<S2C_WorldState>().ExitLocked);
        a.Send(new C2S_Interact { Active = 1 });
        b.Send(new C2S_Interact { Active = 1 });
        Tick(101);
        var world = Killer.Last<S2C_WorldState>();
        Assert.Equal(100, world.Terminals[2].Progress);
        Assert.Equal(1, world.ExitOpen);
        Assert.Equal(0, world.ExitLocked);
    }
}
