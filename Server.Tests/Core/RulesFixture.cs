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
            if (ok)
            {
                // Arrange legacy rule scenarios in the west open lane, independently of production spawn clusters.
                f.Server.PlaceForTests(killer.Id, 5, 5);
                var positions = new[] { (5f,26f), (10f,26f), (5f,36f), (10f,36f), (44f,50f), (76f,50f), (76f,55f) };
                for (int i=0;i<list.Length;i++) f.Server.PlaceForTests(list[i].Id,positions[i].Item1,positions[i].Item2);
                f.Tick();
                return new RulesMatch(f, killer, list, seed);
            }
            f.Dispose();
        }
        throw new InvalidOperationException("No seed assigns the requested skills.");
    }

    // Walks straight to (x, z) at the given speed, resending input every tick and shortening the final step.
    internal void WalkTo(Peer peer, float x, float z, float speed)
    {
        var route = new List<PingTheKiller.ServerCore.Map.MapPoint>(128);
        var start = peer.Self();
        Assert.True(F.Server.Nav.FindPath(start.X,start.Z,x,z,route), $"no route to {x},{z}");
        int next=1;
        for (int i = 0; i < 4000; i++)
        {
            var self = peer.Self();
            if (MathF.Abs(x-self.X)<.001f && MathF.Abs(z-self.Z)<.001f) break;
            while(next+1<route.Count && F.Server.Nav.LineClear(self.X,self.Z,route[next+1].X,route[next+1].Z)) next++;
            if(next>=route.Count) break;
            float dx=route[next].X-self.X,dz=route[next].Z-self.Z;
            float distance=MathF.Sqrt(dx*dx+dz*dz);
            if(distance<.001f) {next++;continue;}
            float scale=1/MathF.Max(distance,speed/GameConstants.TickRate);
            peer.Move(dx*scale,dz*scale);F.Tick();
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
    internal List<PlayerState> KillerPhantoms() => KillerTruth.Phantoms(F.Server, Killer.Last<S2C_WorldState>());
    internal PlayerState Truth(Peer survivor) => Entry(Survivors[0].Last<S2C_WorldState>(), survivor.Id)!.Value;

    // Ticks an idle (non-routing) team of `hackers` needs for one terminal from 0 %.
    internal static int PassiveTicks(int hackers) =>
        (int)Math.Ceiling(GameConstants.TerminalPassiveSeconds / GameConstants.CoHackRate(hackers) * GameConstants.TickRate - 1e-6);

    internal static float Distance(PlayerState a, float x, float z) => MathF.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

    // Complete three real terminals with two co-hackers (GDD v2, 11/17 R2: 90 s passive alone, 1.8x for two, so 50 s each,
    // nobody routing): both exits get power.
    internal void PowerExitsWithTwoSurvivors()
    {
        var a=Survivors[0]; var b=Survivors[1];
        F.Server.PlaceForTests(Killer.Id,76,76); Tick();
        foreach(int id in new[]{0,2,3})
        {
            var t=F.Server.Map.TerminalPositions[id];
            WalkTo(a,t.X-.5f,t.Z,GameConstants.SurvivorSpeed);
            WalkTo(b,t.X+.5f,t.Z,GameConstants.SurvivorSpeed);
            a.Send(new C2S_Interact {Active=1}); b.Send(new C2S_Interact {Active=1}); Tick(PassiveTicks(2) + 1);
            Assert.Equal(100,Killer.Last<S2C_WorldState>().Terminals[id].Progress);
            a.Send(new C2S_Interact {Active=0}); b.Send(new C2S_Interact {Active=0});
            if(id!=3) Assert.False(Killer.Last<S2C_WorldState>().Powered());
        }
        Assert.True(Killer.Last<S2C_WorldState>().Powered());
        Assert.False(Killer.Last<S2C_WorldState>().AnyOpen());
    }

    // Ticks of a full exit switch hold (GameConstants.ExitSwitchSeconds; B1: 17 s).
    internal static int SwitchTicks => (int)Math.Round(GameConstants.ExitSwitchSeconds * GameConstants.TickRate);

    // Powers the exits, then survivor 0 holds the switch of exit 0 for the full ExitSwitchSeconds: exit 0 (4,4) opens and the
    // collapse starts. Survivor 0 is left at that switch, survivor 1 at terminal 3.
    internal void OpenExitWithTwoSurvivors()
    {
        PowerExitsWithTwoSurvivors();
        var a=Survivors[0]; var point=F.Server.Map.ExitSwitches[0];
        WalkTo(a,point.X,point.Z,GameConstants.SurvivorSpeed);
        a.Send(new C2S_Interact {Active=1}); Tick(SwitchTicks + 1);
        a.Send(new C2S_Interact {Active=0}); Tick();
        Assert.True(Killer.Last<S2C_WorldState>().Open(0));
        Assert.False(Killer.Last<S2C_WorldState>().Open(1));
    }
}