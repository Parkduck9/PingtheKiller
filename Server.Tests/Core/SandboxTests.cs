using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class SandboxTests
{
    private static (CoreFixture f, Peer killer, Peer a, Peer b) StartThree(bool sandbox, int seed = 1234)
    {
        var f = new CoreFixture(seed: seed, sandbox: sandbox);
        var killer = f.Join("Killer");
        var a = f.Join("A");
        var b = f.Join("B");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal((byte)Role.Killer, killer.Last<S2C_MatchStart>().Role);
        f.Server.PlaceForTests(killer.Id,5,5); f.Server.PlaceForTests(a.Id,5,26); f.Server.PlaceForTests(b.Id,10,26); f.Tick();
        return (f, killer, a, b);
    }

    private static void UseEverySkill(params Peer[] peers)
    {
        foreach (var peer in peers)
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++) peer.Send(new C2S_UseSkill { SkillId = id });
    }

    private static void Walk(CoreFixture f, Peer peer, float x, float z, float speed)
    {
        for (int i = 0; i < 400; i++)
        {
            var self = peer.Self();
            float dx = x - self.X, dz = z - self.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < 0.001f) break;
            float step = speed / GameConstants.TickRate, scale = d > step ? 1 / d : 1 / step;
            peer.Move(dx * scale, dz * scale);
            f.Tick();
        }
        peer.Move(0, 0);
    }

    [Fact]
    public void OptionIsOffByDefaultIncludingTheOfflinePath()
    {
        Assert.False(new ServerOptions().Sandbox);
        Assert.False(new ServerOptions { IsOffline = true }.Sandbox);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SandboxRemovesCaptureHackingAndSkillsWhileTheDefaultKeepsThem(bool sandbox)
    {
        var (f, killer, a, b) = StartThree(sandbox);
        using (f)
        {
            // b spawns at (10,26), 1 m from terminal 0, and holds Interact; everyone spams every skill id.
            b.Send(new C2S_Interact { Active = 1 });
            UseEverySkill(killer, a, b);
            f.Tick(0.05f);
            for (int i = 0; i < 40; i++) { UseEverySkill(killer, a, b); f.Tick(); }
            Walk(f, killer, 5, 25, GameConstants.KillerSpeed); // touches a at (5,26)
            f.Tick();
            var world = b.Last<S2C_WorldState>();
            var truthA = world.Players.Single(p => p.Id == a.Id);
            var truthB = world.Players.Single(p => p.Id == b.Id);
            int effects = killer.All<S2C_SkillEffect>().Count + a.All<S2C_SkillEffect>().Count + b.All<S2C_SkillEffect>().Count;
            if (sandbox)
            {
                Assert.Equal((byte)PlayerStatus.Normal, truthA.Status);
                Assert.Equal(0, truthA.CaptureCount);
                Assert.Equal(0, world.Terminals[0].Progress);
                Assert.Equal(0, truthB.Flags);
                Assert.Equal(0, effects);
                // Only movement makes a survivor visible: no decoys, no afterimage, no forced exposure.
                f.Tick();
                f.Tick();
                f.Tick();
                Assert.DoesNotContain(killer.Last<S2C_WorldState>().Players, p => p.Role == (byte)Role.Survivor);
            }
            else
            {
                Assert.Equal((byte)PlayerStatus.Lagging, truthA.Status);
                Assert.Equal(1, truthA.CaptureCount);
                Assert.True(world.Terminals[0].Progress > 0);
                Assert.True(effects > 0);
            }
        }
    }

    [Fact]
    public void SandboxMatchCannotBeWonAndEndsOnlyByTimeUp()
    {
        var (f, killer, a, b) = StartThree(sandbox: true);
        using (f)
        {
            // Two survivors on the exit cannot escape: no hacking means the exit never opens, and escape is off.
            for (int i = 0; i < 17997; i++) // the StartMatch tick is the first of 18000
            {
                if (i % 20 == 0) UseEverySkill(killer, a, b);
                if (i < 300) { a.Move(1, 0.1f); b.Move(-0.2f, -1); }
                f.Tick();
                if (i % 100 == 0) { killer.Drain(); a.Drain(); b.Drain(); }
                if (i % 1000 == 0) Assert.Empty(a.Packets.OfType<S2C_GameResult>());
            }
            Assert.Empty(killer.All<S2C_GameResult>());
            Assert.False(a.Last<S2C_WorldState>().Powered());
            Assert.All(a.Last<S2C_WorldState>().Players, p => Assert.Equal((byte)PlayerStatus.Normal, p.Status));
            f.Tick();
            var result = Assert.Single(killer.All<S2C_GameResult>());
            Assert.Equal((byte)GameEndReason.TimeUp, result.Reason);
            Assert.Empty(killer.All<S2C_SkillEffect>());
        }
    }

    [Fact]
    public void SandboxBotsNeverCaptureOrEndTheMatchBeforeTheTimeLimit()
    {
        using var f = new CoreFixture(seed: 9, sandbox: true);
        var human = f.Join("Human");
        int humanId = human.Id;
        human.Send(new C2S_SetBots { Count = 7 });
        human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
        human.Send(new C2S_StartMatch());
        f.Tick();
        bool botsMoved = false;
        float firstX = float.NaN;
        for (int i = 0; i < 17998; i++) // the StartMatch tick is the first of 18000
        {
            if (i % 40 < 20) human.Move(1, 0); else human.Move(-1, 0); // a visible target for the killer bot
            f.Tick();
            human.Drain();
            var world = human.Packets.OfType<S2C_WorldState>().Last();
            Assert.All(world.Players, p => Assert.Equal((byte)PlayerStatus.Normal, p.Status));
            var bot = world.Players.First(p => p.Id != humanId);
            if (float.IsNaN(firstX)) firstX = bot.X;
            else if (bot.X != firstX) botsMoved = true;
            Assert.Empty(human.Packets.OfType<S2C_GameResult>());
            Assert.Empty(human.Packets.OfType<S2C_SkillEffect>());
            human.Packets.Clear();
        }
        Assert.True(botsMoved);
        f.Tick();
        Assert.Equal((byte)GameEndReason.TimeUp, Assert.Single(human.All<S2C_GameResult>()).Reason);
    }
}
