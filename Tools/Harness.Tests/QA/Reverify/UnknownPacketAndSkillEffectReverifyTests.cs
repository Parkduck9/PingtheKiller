using PingTheKiller.Harness;
using PingTheKiller.Harness.Tests;
using PingTheKiller.Harness.Tests.QA;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.Reverify;

public sealed class KillerSkillEffectLeakH6Tests
{
    [Fact]
    public async Task KillerCoordinatesInSkillEffect_FailsH6()
    {
        // Attack: killer X/Z inside an S2C_SkillEffect sent to a survivor (caster is Killer)
        await using var server = new ScenarioServer(H6Fault.LeakSkillEffect);
        var (h6, _) = await H6.RunAsync(server.Port);

        Assert.False(h6.Passed, $"FALSE PASS: killer coordinates in SkillEffect passed H6: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Contains("S2C_SkillEffect", h6.Evidence);
        Assert.Contains("killer coordinate", h6.Evidence);
    }
}

public sealed class KillerUnknownPacketLeakH6Tests
{
    [Fact]
    public async Task KillerCoordinatesInUnknownPacket_FailsH6()
    {
        // Attack: killer X/Z bytes inside an unknown-Id frame (0x7E) sent to a survivor
        await using var server = new ScenarioServer(H6Fault.LeakInUnknownPacket);
        var (h6, _) = await H6.RunAsync(server.Port);

        Assert.False(h6.Passed, $"FALSE PASS: killer coordinates in unknown frame passed H6: {h6.Evidence}");
        Assert.Equal(0, h6.Score);
        Assert.Contains("killer coordinate bytes in unknown packet 0x7E", h6.Evidence);
    }
}

public sealed class UnknownPacketH3AndControlTests
{
    [Fact]
    public async Task StoppedSurvivorCoordinatesInUnknownPacket_FailsH3()
    {
        // Attack: stopped survivor coordinates embedded in unknown packet 0x7E sent to killer in H3
        await using var server = new LobbyServer
        {
            AnonymousKillerView = true,
            LeakSilentInUnknown = true
        };
        var (report, _) = await Qa.RunAsync(server.Port, new[] { "H3" }, timeoutSeconds: 3);
        var h3 = Qa.Item(report, "H3");

        Assert.False(h3.Passed, $"FALSE PASS: survivor coordinates in unknown packet passed H3: {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains("unknown packet 0x7E", h3.Evidence);
    }

    [Fact]
    public async Task Control_CleanUnknownFramesAndLineOfSight_PassH6()
    {
        // Control: well-formed unknown frames without killer coordinates are skipped and allowed
        await using var server = new ScenarioServer
        {
            LineOfSight = true,
            UnknownFrames = true
        };
        var (h6, _) = await H6.RunAsync(server.Port);

        Assert.True(h6.Passed, $"FALSE FAIL: server with clean unknown frames failed H6: {h6.Evidence}");
        Assert.Equal(10, h6.Score);
        Assert.Contains("skipped unknown packets", h6.Evidence);
    }
}
