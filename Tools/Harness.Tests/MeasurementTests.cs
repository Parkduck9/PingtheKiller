using System.Diagnostics;
using PingTheKiller.Harness;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

public sealed class MeasurementTests
{
    private static ReceivedFrame Frame(uint tick, double seconds, params PlayerState[] players)
    {
        var world = new S2C_WorldState { Tick = tick, Players = players.ToList() };
        return new(world, PacketCodec.Encode(world), (long)(seconds * Stopwatch.Frequency));
    }

    [Fact]
    public void P99UsesNearestRankAndRejectsEmptySamples()
    {
        Assert.Equal(99, Measurements.P99(Enumerable.Range(1, 100).Select(i => (double)i)));
        Assert.Equal(200, Measurements.P99(new double[] { 10, 20, 200 }));
        Assert.Throws<CheckFailure>(() => Measurements.P99(Array.Empty<double>()));
    }

    [Fact]
    public void SyncMeasuresArrivalAndTickPeriodAndRejectsGapOrDuplicate()
    {
        var frames = new[] { Frame(1, 0), Frame(2, .05), Frame(4, .15) };
        var result = Measurements.Sync(frames);
        Assert.Equal(100, result.ArrivalP99Ms, 3);
        Assert.Equal(50, result.TickP99Ms, 3);
        Assert.Throws<CheckFailure>(() => Measurements.Sync(new[] { Frame(1, 0), Frame(2, .05), Frame(5, .1) }));
        Assert.Throws<CheckFailure>(() => Measurements.Sync(new[] { Frame(1, 0), Frame(2, .05), Frame(2, .1) }));
    }

    [Theory]
    [InlineData(.070)]
    [InlineData(.120)]
    public void SyncAllowsArrivalP99Through120MsRegardlessOfInferredTickPeriod(double interval)
    {
        var result = Measurements.Sync(new[] { Frame(1, 0), Frame(2, interval), Frame(3, interval * 2) });
        Assert.Equal(interval * 1000, result.ArrivalP99Ms, 3);
        Assert.True(result.TickP99Ms > 60);
    }

    [Fact]
    public void SyncRejectsArrivalP99Above120Ms()
    {
        var error = Assert.Throws<CheckFailure>(() => Measurements.Sync(new[] { Frame(1, 0), Frame(2, .121), Frame(3, .242) }));
        Assert.Contains("inter-arrival p99", error.Message);
    }

    [Theory]
    [InlineData(1.016)]
    [InlineData(1.15)]
    public void FloodAllowsTimerJitterAndReportsActualRate(double seconds)
    {
        string evidence = Measurements.FloodTiming(500, seconds);
        Assert.Contains($"500 inputs in {seconds:F3} s", evidence);
        Assert.Contains($"achieved rate={500 / seconds:F1}/s", evidence);
        Assert.Throws<CheckFailure>(() => Measurements.FloodTiming(500, 1.151));
    }

    [Fact]
    // U3c: the raw scan no longer looks the survivor up by Id (killer-view entries are anonymous); entries are attributed
    // by tick and position in KillerViewTests. An entry at the exact position is still caught by its bytes.
    public void MaskingDetectsEachCoordinateInAnyRawFrame()
    {
        var stopped = new PlayerState { Id = 73, X = 7.12345f, Z = -8.45678f };
        Measurements.Masked(Frame(1, 0), stopped);
        Assert.Throws<CheckFailure>(() => Measurements.Masked(Frame(1, 0, stopped), stopped));
        Assert.Throws<CheckFailure>(() => Measurements.Masked(Frame(1, 0, stopped with { Id = 0 }), stopped));
        foreach (float coordinate in new[] { stopped.X, stopped.Z })
        {
            var raw = new byte[19];
            Measurements.FloatBytes(coordinate).CopyTo(raw, 7);
            var leak = new ReceivedFrame(new S2C_Pong(), raw, 0);
            Assert.Throws<CheckFailure>(() => Measurements.Masked(leak, stopped));
            Assert.Throws<CheckFailure>(() => Measurements.MaskedBytes(raw, "unknown packet 0x7E", stopped));
        }
    }

    // U3d replaced "the killer is always hidden" with the line-of-sight rule (KillerSight, see LineOfSightTests): without a
    // killer tick an entry is undecided, and any killer entry to a recipient that is not in the world is a leak.
    [Fact]
    public void KillerEntryWithoutLineOfSightGroundTruthIsNeverLegal()
    {
        var absent = (S2C_WorldState)Frame(1, 0, new PlayerState { Id = 73 }).Packet;
        Assert.Null(KillerSight.Judge(absent, 73, 11, null).Leak);
        var present = (S2C_WorldState)Frame(1, 0, new PlayerState { Id = 11, Role = (byte)Role.Killer }).Packet;
        Assert.NotNull(KillerSight.Judge(present, 73, 11, null).Leak);
        Assert.Contains("Killer 11 leaked to survivor 74", KillerSight.Judge(present, 74, 11, null).Leak);
    }

    [Fact]
    public void SpeedUsesServerTicksAndRejectsFastOrNonfiniteMovement()
    {
        ReceivedFrame At(uint tick, float x) => Frame(tick, 0, new PlayerState { Id = 73, X = x });
        Assert.Equal(4, Measurements.MaxSpeed(new[] { At(1, 0), At(2, .2f), At(4, .6f) }, 73, 0, false), 4);
        Assert.Throws<CheckFailure>(() => Measurements.MaxSpeed(new[] { At(1, 0), At(2, 2), At(3, 4) }, 73, 0, false));
        Assert.Throws<CheckFailure>(() => Measurements.MaxSpeed(new[] { At(1, 0), At(2, float.NaN), At(3, 0) }, 73, 0, false));
        Assert.Throws<CheckFailure>(() => Measurements.MaxSpeed(new[] { At(1, 0), At(2, .1f), At(3, .2f) }, 73, 0, true));
        Assert.Throws<CheckFailure>(() => Measurements.MaxSpeed(new[] { Frame(1, 0), Frame(2, 0), Frame(3, 0) }, 73, 0, false));
    }
}
