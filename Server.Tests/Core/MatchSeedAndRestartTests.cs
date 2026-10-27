using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class MatchSeedAndRestartTests
{
    // One human survivor (host) and seven bots, one of them the killer; returns the wire seed and every survivor
    // position the human saw over 200 ticks plus the bots' skill effects and its own skill (killer index, skills, bot choices).
    private static (int wireSeed, List<(int, float, float)> path) BotPaths(int seed, int? matchSeed, bool csprng)
    {
        using var f = new CoreFixture(seed: seed, matchSeed: matchSeed, csprng: csprng);
        var human = f.Join("Human");
        int id = human.Id;
        human.Send(new C2S_SetBots { Count = 7 });
        human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
        human.Send(new C2S_StartMatch());
        f.Tick();
        int wire = human.Last<S2C_MatchStart>().Seed;
        var path = new List<(int, float, float)> { (human.Last<S2C_MatchStart>().SkillId, 0, 0) };
        for (int i = 0; i < 200; i++)
        {
            f.Tick();
            foreach (var p in human.Last<S2C_WorldState>().Players)
                if (p.Id != id) path.Add((p.Id, p.X, p.Z));
        }
        foreach (var effect in human.All<S2C_SkillEffect>()) path.Add((effect.CasterId * 10 + effect.SkillId, effect.X, effect.Z));
        return (wire, path);
    }

    [Fact]
    public void EqualWireSeedsDoNotGiveEqualBotPaths()
    {
        var a = BotPaths(0, matchSeed: 5, csprng: true);
        var b = BotPaths(0, matchSeed: 5, csprng: true);
        Assert.Equal(5, a.wireSeed);
        Assert.Equal(a.wireSeed, b.wireSeed);
        Assert.NotEqual(a.path, b.path);
    }

    [Fact]
    public void InjectedPrivateSeedIsDeterministicAndNotTheWireSeed()
    {
        var a = BotPaths(11, matchSeed: 1, csprng: false);
        var b = BotPaths(11, matchSeed: 2, csprng: false);
        Assert.Equal(a.path, b.path);
        Assert.NotEqual(a.wireSeed, b.wireSeed);
        var c = BotPaths(11, matchSeed: null, csprng: false);
        Assert.Equal(a.path, c.path);
        Assert.NotEqual(11, c.wireSeed);
    }

    // Killer + two human survivors; survivor 0 must hold White Noise. Returns the decoy positions of its first cast.
    private static List<(float, float)>? WhiteNoise(int seed, int matchSeed, bool csprng)
    {
        using var f = new CoreFixture(seed: seed, matchSeed: matchSeed, csprng: csprng);
        var killer = f.Join("K");
        var a = f.Join("A");
        f.Join("B");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        if (a.Last<S2C_MatchStart>().SkillId != SkillIds.WhiteNoise) return null;
        Assert.Equal(matchSeed, a.Last<S2C_MatchStart>().Seed);
        f.Tick();
        a.Send(new C2S_UseSkill { SkillId = SkillIds.WhiteNoise });
        f.Tick();
        var decoys = killer.Last<S2C_WorldState>().Players.Where(p => p.Role == (byte)Role.Survivor).Select(p => (p.X, p.Z)).ToList();
        Assert.Equal(2, decoys.Count);
        return decoys.OrderBy(p => p.X).ToList();
    }

    private static List<(float, float)> FirstWhiteNoise(Func<int, List<(float, float)>?> run)
    {
        for (int attempt = 0; attempt < 400; attempt++)
            if (run(attempt) is { } decoys) return decoys;
        throw new InvalidOperationException("White Noise was never assigned.");
    }

    [Fact]
    public void EqualWireSeedsDoNotGiveEqualDecoyPositionsButAnInjectedPrivateSeedDoes()
    {
        var a = FirstWhiteNoise(_ => WhiteNoise(0, 77, csprng: true));
        var b = FirstWhiteNoise(_ => WhiteNoise(0, 77, csprng: true));
        Assert.NotEqual(a, b);
        int seed = Enumerable.Range(1, 400).First(s => WhiteNoise(s, 1, csprng: false) != null);
        Assert.Equal(WhiteNoise(seed, 1, csprng: false), WhiteNoise(seed, 2, csprng: false));
    }

    [Fact]
    public void ASecondMatchAcceptsSequenceNumbersRestartingAtOne()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        survivor.NextSeq = 500;
        f.Walk(survivor, 0, 1, 10);
        survivor.Move(0, 0);
        // The killer walks up x = 5 into the only survivor: AllSurvivorsDown ends match 1.
        for (int i = 0; i < 400 && !killer.All<S2C_GameResult>().Any(); i++)
        {
            var self = killer.Self();
            var target = survivor.Self();
            killer.Move(target.X - self.X, target.Z - self.Z);
            f.Tick();
        }
        Assert.Single(killer.All<S2C_GameResult>());
        killer.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal(2, survivor.All<S2C_MatchStart>().Count);
        // Both clients restart their sequence at 1, below everything the server accepted in match 1.
        survivor.NextSeq = 1;
        killer.NextSeq = 1;
        float survivorX = survivor.Self().X, killerX = killer.Self().X;
        for (int i = 0; i < 10; i++)
        {
            survivor.Move(1, 0);
            killer.Move(1, 0);
            f.Tick();
        }
        Assert.InRange(survivor.Self().X - survivorX, 1.99f, 2.01f);
        Assert.InRange(killer.Self().X - killerX, 2.29f, 2.31f);
    }
}
