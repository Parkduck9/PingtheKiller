using PingTheKiller.Harness;
using PingTheKiller.Harness.Tests;
using PingTheKiller.Harness.Tests.QA;
using PingTheKiller.Harness.Tests.QA.U3d;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.Reverify;

public sealed class AmbiguousBandH3Tests
{
    [Fact]
    public async Task WallPenetrationBeyondBand_FailsH3()
    {
        // Attack: server leaks killer entry when ray penetrates wall by 0.07 m (> 0.05 m band)
        // FixedMap wall corner at (18, 14); (18.07, 14.07) penetrates by 0.07 m
        var (h3, _, _) = await U3dH3Run.RunAsync("penetration-beyond-band",
            new LobbyServer { Layout = SightLayout.Clear },
            custom: tap => U3dH3KillerAtAWallCornerTests.BrainFor(18.07f, 14.07f, lenient: false, always: true)(tap));

        Assert.False(h3.Passed, $"FALSE PASS: killer entry penetrating wall by 0.07 m passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("is blocked", h3.Evidence);
    }

    [Fact]
    public async Task WallPenetrationInsideBand_PassesByDesign_H3()
    {
        // Accepted limit U3d-1 / F2: killer penetrates wall corner by 0.03125 m (<= 0.05 m band)
        // Harness classifies within [-0.05, 0.05] as Ambiguous and does not fail
        var (h3, _, _) = await U3dH3Run.RunAsync("penetration-inside-band",
            new LobbyServer { Layout = SightLayout.Clear },
            custom: tap => U3dH3KillerAtAWallCornerTests.BrainFor(18.03125f, 14.03125f, lenient: false, always: true)(tap));

        Assert.True(h3.Passed, $"FALSE FAIL: ambiguous band penetration failed H3: {h3.Evidence}");
        Assert.Equal(20, h3.Score);
        var counts = U3dCounts.Parse(h3.Evidence);
        Assert.True(counts.Ambiguous > 0, "Expected ambiguous counts in evidence");
        Assert.Equal(0, counts.Visible);
    }
}

public sealed class AmbiguousBandOnlyNearWallsH6Tests
{
    private static Func<SurvCtx, bool> ShowOnlyNearWalls() => c =>
    {
        bool nearWall = Math.Abs(c.Margin) <= LineOfSight.Band;
        if (nearWall && c.EntryIndex < 0 && c.Eligible)
        {
            c.World.Players.Insert(0, c.LegalEntry);
            return true;
        }
        if (!nearWall && c.EntryIndex >= 0)
        {
            c.World.Players.RemoveAt(c.EntryIndex);
            return true;
        }
        return false;
    };

    [Fact]
    public async Task ServerShowingKillerOnlyNearWalls_FailsH6ExpectLos()
    {
        // Attack: server shows killer to survivors ONLY when segment is within 0.05 m of a wall
        // All shown entries are Ambiguous (Visible = 0), so H6 with --expect-los FAILS
        await using var server = new ScenarioServer { LineOfSight = true };
        var tap = new SurvivorTap(ShowOnlyNearWalls());
        await using var proxy = new U3dProxy(server.Port, tap.Rewrite);
        var (h6, _) = await H6.RunAsync(proxy.Port, expectLos: true);

        Assert.False(h6.Passed, $"FALSE PASS: server showing killer only near walls passed --expect-los: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Contains("--expect-los needs at least one legal line-of-sight killer entry", h6.Evidence);
        Assert.Contains("0 legal line-of-sight killer entries", h6.Evidence);
    }

    [Fact]
    public async Task ServerShowingKillerOnlyNearWalls_PassesWithoutExpectLos_ByDesign()
    {
        // Contrast: without --expect-los, the harness accepts servers without line-of-sight support
        // (U3d-2: clear-and-absent accepted; ambiguous entries tolerated)
        await using var server = new ScenarioServer { LineOfSight = true };
        var tap = new SurvivorTap(ShowOnlyNearWalls());
        await using var proxy = new U3dProxy(server.Port, tap.Rewrite);
        var (h6, _) = await H6.RunAsync(proxy.Port, expectLos: false);

        Assert.True(h6.Passed, $"FALSE FAIL: server without --expect-los failed H6: {h6.Evidence}");
        Assert.Equal(10, h6.Score);
    }
}

public sealed class StrictContractExpectLosH6Tests
{
    [Fact]
    public async Task Control_StrictContractServer_PassesH6ExpectLos()
    {
        // Control: strictly contract-correct server passes H6 with --expect-los
        await using var server = new ScenarioServer { LineOfSight = true };
        var (h6, _) = await H6.RunAsync(server.Port, expectLos: true);

        Assert.True(h6.Passed, $"FALSE FAIL: contract-correct server failed --expect-los: {h6.Evidence}");
        Assert.Equal(10, h6.Score);
        Assert.Contains("--expect-los:", h6.Evidence);
    }
}
