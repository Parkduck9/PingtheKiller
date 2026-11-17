using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

// Q5a attempt 2 (QA finding F2): the bots must use the chase props at a steady rate, not by luck. Seeded all-bot 1v7
// matches run for a fixed time (or to their end); the server log has one line per pull, vault, stun and break.
public sealed class ChasePropsBotUseTests
{
    internal const int Seeds = 12;
    internal const int Seconds = 300;
    // Floors, as an average per match over the fixed window (attempt 1 measured about 0.6 pulls and 0.3 rail vaults
    // per full match; QA saw 0.25 pulls and no rail vault per offline match).
    internal const double MinPullsPerMatch = 1.0;
    internal const double MinRailVaultsPerMatch = 1.0;

    private readonly ITestOutputHelper _output;
    public ChasePropsBotUseTests(ITestOutputHelper output) { _output = output; }

    internal sealed class Use
    {
        internal int Pulls, Downs, Stuns, RailVaults, KillerRailVaults, ShutterVaults, Breaks, Matches;
        public override string ToString() =>
            $"{Matches} matches: pulls {Pulls}, downs {Downs}, stuns {Stuns}, rail vaults {RailVaults} (killer {KillerRailVaults}), " +
            $"shutter vaults {ShutterVaults}, breaks {Breaks}";
    }

    internal static Use Measure(int seeds, int survivors, int seconds)
    {
        // One after another on the test thread: pool threads busy with whole matches starve the real-socket timing
        // tests (RttProbeTests) that run in parallel with this class.
        var bag = new List<Use>();
        for (int seed = 1; seed <= seeds; seed++)
        {
            var use = new Use { Matches = 1 };
            using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = survivors + 1, AutoStart = true });
            bool ended = false;
            server.Log += line =>
            {
                if (line.Contains(" pull by ")) use.Pulls++;
                if (line.StartsWith("shutter ") && line.Contains(" down by ")) use.Downs++;
                if (line.Contains("stun=killer")) use.Stuns++;
                if (line.StartsWith("rail ") && line.Contains(" vault by ")) { use.RailVaults++; if (line.EndsWith("role=Killer")) use.KillerRailVaults++; }
                if (line.StartsWith("shutter ") && line.Contains(" vault by ")) use.ShutterVaults++;
                if (line.Contains(" broken by ")) use.Breaks++;
                if (line.StartsWith("match end")) ended = true;
            };
            for (int t = 0; t < seconds * GameConstants.TickRate && !ended; t++) server.Tick(1f / GameConstants.TickRate);
            bag.Add(use);
        }
        var total = new Use();
        foreach (var u in bag)
        {
            total.Matches += u.Matches; total.Pulls += u.Pulls; total.Downs += u.Downs; total.Stuns += u.Stuns;
            total.RailVaults += u.RailVaults; total.KillerRailVaults += u.KillerRailVaults;
            total.ShutterVaults += u.ShutterVaults; total.Breaks += u.Breaks;
        }
        return total;
    }

    [Fact]
    public void SeededOneVersusSevenBotsPullShuttersAndVaultRailsEveryMatchOnAverage()
    {
        var use = Measure(Seeds, 7, Seconds);
        _output.WriteLine($"{Seconds} s window: {use}");
        Assert.Equal(Seeds, use.Matches);
        Assert.True(use.Pulls >= MinPullsPerMatch * Seeds, $"pulls per match below {MinPullsPerMatch}: {use}");
        Assert.True(use.RailVaults >= MinRailVaultsPerMatch * Seeds, $"rail vaults per match below {MinRailVaultsPerMatch}: {use}");
        Assert.True(use.Downs <= use.Pulls);
    }
}

// Q5a attempt 2 chase plans on hand-built role views.
public sealed class ChasePlanTests
{
    // Rail 5 stands at x 36.5, z 25-28 (vault point 36.5, 26.5).
    [Fact]
    public void AChasedSurvivorRunsToARailItReachesFirstAndVaultsIt()
    {
        var nav = new NavGrid(new FixedMap());
        var tactics = new ChaseTactics();
        var view = new SurvivorBotView { Status = PlayerStatus.Normal, X = 33f, Z = 26.5f, KillerVisible = true, KillerX = 29f, KillerZ = 26.5f, Time = 1 };
        var intent = new BotIntent { DirX = -1 };
        tactics.Survivor(view, nav, ref intent);
        Assert.True(intent.DirX > 0.9f); // toward the rail, away from the killer
        Assert.False(intent.Interact);
        view.X = 35.75f;
        view.KillerX = 32f;
        view.Time = 1.7;
        intent = new BotIntent();
        tactics.Survivor(view, nav, ref intent);
        Assert.True(intent.Interact);
        Assert.Equal(0f, intent.DirX);
    }

    [Fact]
    public void AChasedSurvivorSkipsAPropTheKillerReachesFirst()
    {
        var nav = new NavGrid(new FixedMap());
        var tactics = new ChaseTactics();
        // The killer stands between the survivor and the rail.
        var view = new SurvivorBotView { Status = PlayerStatus.Normal, X = 31f, Z = 26.5f, KillerVisible = true, KillerX = 34f, KillerZ = 26.5f, Time = 1 };
        var intent = new BotIntent { DirX = -1 };
        tactics.Survivor(view, nav, ref intent);
        Assert.Equal(-1f, intent.DirX);
        Assert.False(intent.Interact);
    }

    [Fact]
    public void TheKillerWalksToADownShutterAndBreaksItWhenGoingAroundIsMuchLonger()
    {
        var nav = new NavGrid(new FixedMap());
        nav.SetShutterDown(5, true);
        var tactics = new ChaseTactics();
        var view = new KillerBotView { X = 19.5f, Z = 30.5f, Time = 1 };
        view.Shutters[5] = (byte)ShutterState.Down;
        view.Blips.Add(new KillerBotView.Blip { X = 19.5f, Z = 36f });
        var intent = new BotIntent { DirX = 1 };
        tactics.Killer(view, nav, ref intent);
        Assert.False(intent.Interact);
        Assert.True(intent.DirZ > 0.9f); // to the shutter, not around the block
        view.Z = 32.6f;
        view.Time = 1.5;
        intent = new BotIntent { DirX = 1 };
        tactics.Killer(view, nav, ref intent);
        Assert.True(intent.Interact);
        Assert.Equal(0f, intent.DirX);
        Assert.Equal(0f, intent.DirZ);
    }

    [Fact]
    public void TheKillerVaultsARailOnlyOnceItsTargetLoopsIt()
    {
        var nav = new NavGrid(new FixedMap());
        var tactics = new ChaseTactics();
        var view = new KillerBotView { X = 37.2f, Z = 26.5f };
        float[] targetX = { 35.6f, 37.7f, 35.6f };
        bool pressed = false;
        for (int step = 0; step < targetX.Length; step++)
        {
            view.Time = 1 + step * 0.5;
            view.Blips.Clear();
            view.Blips.Add(new KillerBotView.Blip { X = targetX[step], Z = 26.5f });
            var intent = new BotIntent();
            tactics.Killer(view, nav, ref intent);
            pressed = intent.Interact;
            if (step == 0) Assert.False(pressed); // across once: the killer goes around as usual
        }
        Assert.True(pressed); // the target crossed back and forth: vault after it
    }
}
