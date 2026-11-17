using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class BotMatchTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void OneHumanAndSevenBotsAlwaysEndWithExactlyOneGameResult(int seed)
    {
        using var f = new CoreFixture(seed: seed);
        var human = f.Join("Human");
        human.Send(new C2S_SetBots { Count = 7 });
        human.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Single(human.All<S2C_MatchStart>());
        bool killerFrames = human.Last<S2C_MatchStart>().Role == (byte)Role.Killer;
        int ticks = 0, worlds = 0;
        S2C_GameResult? result = null;
        while (result == null && ticks <= 18001)
        {
            f.Tick();
            ++ticks;
            human.Drain();
            foreach (var packet in human.Packets)
            {
                if (packet is S2C_GameResult r) result = r;
                if (packet is not S2C_WorldState world) continue;
                ++worlds;
                // Survivor frames carry real, distinct Ids; killer frames carry Id 0 for every survivor-shaped entry.
                var named = world.Players.Where(p => p.Id != 0).ToList();
                Assert.Equal(named.Count, named.Select(p => p.Id).Distinct().Count());
                // A survivor frame may also carry the killer entry with its real Id while in line of sight (U2e).
                if (killerFrames)
                    Assert.All(world.Players, p => Assert.True(p.Role == (byte)Role.Killer || p.Id == 0));
                else
                    Assert.True(world.Players.Count(p => p.Role == (byte)Role.Killer) <= 1);
                Assert.All(world.Players, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Z) && float.IsFinite(p.Yaw) &&
                    float.IsFinite(p.StatusTimer) &&
                    // Q5a: a vaulting body crosses over the rail or Down shutter (never a wall or an obstacle).
                    (f.Server.Map.CanStand(p.X, p.Z) || (f.Server.Map.CanStandStatic(p.X, p.Z, FixedMap.PlayerRadius) && f.Server.VaultingAtForTests(p.X, p.Z)))));
            }
            human.Packets.Clear();
        }
        Assert.NotNull(result);
        Assert.True(ticks <= 18000, "the match must end by the 900 s time limit");
        if (result.WinnerRole == (byte)Role.Survivor) Assert.NotEmpty(result.EscapedIds);
        // GDD v2: fewer than half escaping is still a killer victory.
        else Assert.True(result.EscapedIds.Count < 4);
        // After the end only lobby traffic follows, and no second result.
        f.Tick();
        f.Tick();
        Assert.Empty(human.All<S2C_GameResult>());
        Assert.Empty(human.All<S2C_WorldState>());
        Assert.True(worlds >= ticks);
    }
}
