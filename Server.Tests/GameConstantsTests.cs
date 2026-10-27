using PingTheKiller.Shared.Game;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class GameConstantsTests
{
    [Theory]
    [InlineData(1, 2, 4)]
    [InlineData(2, 2, 4)]
    [InlineData(3, 3, 5)]
    [InlineData(4, 3, 5)]
    [InlineData(5, 3, 5)]
    [InlineData(6, 4, 6)]
    [InlineData(7, 4, 6)]
    public void TerminalCountsMatchGdd(int survivors, int expectedRequired, int expectedPlaced)
    {
        GameConstants.TerminalsFor(survivors, out int required, out int placed);
        Assert.Equal(expectedRequired, required);
        Assert.Equal(expectedPlaced, placed);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public void TerminalCountsRejectInvalidSurvivorCount(int survivors) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GameConstants.TerminalsFor(survivors, out _, out _));

    [Fact]
    public void ConstantsMatchGddNumbers()
    {
        Assert.Equal(20, GameConstants.TickRate);
        Assert.Equal(4.0f, GameConstants.SurvivorSpeed);
        Assert.Equal(4.6f, GameConstants.KillerSpeed);
        Assert.Equal(0.25f, GameConstants.LaggingSpeedMultiplier);
        Assert.Equal(0.15f, GameConstants.SignalHoldSeconds);
        Assert.Equal(1.2f, GameConstants.CaptureRadius);
        Assert.Equal(1.5f, GameConstants.RescueRadius);
        Assert.Equal(3.0f, GameConstants.RescueTimeSeconds);
        Assert.Equal(60f, GameConstants.LaggingTimeoutSeconds);
        Assert.Equal(2f, GameConstants.PostRescueInvulnerabilitySeconds);
        Assert.Equal(1.5f, GameConstants.TerminalRadius);
        Assert.Equal(10f, GameConstants.TerminalHackTimeSeconds);
        Assert.Equal(3, GameConstants.MaxCoHackMultiplier);
        Assert.Equal(2f, GameConstants.ExitRadius);
        Assert.Equal(600f, GameConstants.MatchLengthSeconds);
        Assert.Equal(18f, GameConstants.ThreatLevel1Distance);
        Assert.Equal(12f, GameConstants.ThreatLevel2Distance);
        Assert.Equal(6f, GameConstants.ThreatLevel3Distance);
        Assert.Equal(12f, GameConstants.KillerSkillCooldownSeconds);
        Assert.Equal(15f, GameConstants.SurvivorSkillCooldownSeconds);
    }
}
