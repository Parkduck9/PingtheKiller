using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class SimulationTests
{
    [Fact]
    public void SurvivorMovesFourMetersInTwentyTicksAndKeepsLatestDirection()
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        float start = survivor.Self().X;
        // A held key is resent every tick (20 Hz); the server applies the latest direction.
        f.Walk(survivor, 1, 0, 20);
        Assert.InRange(survivor.Self().X - start, 3.99f, 4.01f);
    }

    [Fact]
    public void KillerMovesAtItsOwnSpeed()
    {
        using var f = new CoreFixture();
        var (killer, _) = f.StartPair();
        float start = killer.Self().X;
        f.Walk(killer, 1, 0, 20);
        Assert.InRange(killer.Self().X - start, 4.59f, 4.61f);
    }

    [Theory]
    [InlineData(float.NaN, 1)]
    [InlineData(1, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 1)]
    public void NonFiniteDirectionStopsMovement(float x, float z)
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        survivor.Send(new C2S_MoveInput { DirX = 1, Seq = 1 });
        f.Tick();
        var start = survivor.Self();
        survivor.Send(new C2S_MoveInput { DirX = x, DirZ = z, Yaw = float.NaN, Seq = 2 });
        f.Tick();
        Assert.Equal(start.X, survivor.Self().X);
        Assert.Equal(start.Z, survivor.Self().Z);
        Assert.Equal(0, survivor.Self().Yaw);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(float.MaxValue, float.MaxValue)]
    public void OversizedDirectionIsNormalizedWithoutOverflow(float x, float z)
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        var start = survivor.Self();
        survivor.Send(new C2S_MoveInput { DirX = x, DirZ = z, Seq = 1 });
        f.Tick();
        var end = survivor.Self();
        float distance = MathF.Sqrt(MathF.Pow(end.X - start.X, 2) + MathF.Pow(end.Z - start.Z, 2));
        Assert.InRange(distance, 0.1999f, 0.2001f);
    }

    [Fact]
    public void StaleAndDuplicateSequencesCannotReplaceLatestInput()
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        var start = survivor.Self();
        survivor.Send(new C2S_MoveInput { DirX = 1, Seq = 10 });
        survivor.Send(new C2S_MoveInput { DirX = -1, Seq = 9 });
        survivor.Send(new C2S_MoveInput { DirX = -1, Seq = 10 });
        f.Tick();
        Assert.InRange(survivor.Self().X - start.X, 0.1999f, 0.2001f);
    }

    [Fact]
    public void MoreThanSixtyInputsInARollingSecondAreDroppedThenBudgetRecovers()
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        float start = survivor.Self().X;
        for (uint seq = 1; seq <= 60; ++seq)
            survivor.Send(new C2S_MoveInput { DirX = 1, Seq = seq });
        for (uint seq = 61; seq <= 500; ++seq)
            survivor.Send(new C2S_MoveInput { DirX = -1, Seq = seq });
        f.Tick();
        Assert.InRange(survivor.Self().X - start, 0.1999f, 0.2001f);
        for (int i = 0; i < 19; ++i) f.Tick();
        start = survivor.Self().X;
        // Excess sequences were dropped, not accepted as the new last sequence.
        survivor.Send(new C2S_MoveInput { DirX = -1, Seq = 61 });
        f.Tick();
        Assert.InRange(start - survivor.Self().X, 0.1999f, 0.2001f);
    }

    [Fact]
    public void PlayerCannotWalkThroughInteriorOrOuterWalls()
    {
        using var f = new CoreFixture();
        var (_, survivor) = f.StartPair();
        // Survivor spawn (5,26) is aligned with the interior wall x=18..20, z=14..32.
        Assert.Equal(26f, survivor.Self().Z);
        for (int i = 0; i < 160; i++)
        {
            f.Walk(survivor, 1, 0, 1);
            var state = survivor.Self();
            Assert.True(f.Server.Map.CanStand(state.X, state.Z));
        }
        Assert.InRange(survivor.Self().X, 11.4f, 11.61f);
        for (int i = 0; i < 120; i++)
        {
            f.Walk(survivor, -1, 0, 1);
            var state = survivor.Self();
            Assert.True(f.Server.Map.CanStand(state.X, state.Z));
        }
        Assert.InRange(survivor.Self().X, FixedMap.PlayerRadius - 0.001f, FixedMap.PlayerRadius + 0.11f);
    }

    [Fact]
    public void CircleCollisionSlidesAndLargeStepCannotTunnel()
    {
        var map = new FixedMap();
        var slide = map.Move(11.6f, 20, 4, 4);
        Assert.InRange(slide.X, 11.59f, 11.61f);
        Assert.InRange(slide.Z, 23.99f, 24.01f);
        var tunnel = map.Move(5, 20, 50, 0);
        Assert.InRange(tunnel.X, 11.4f, 11.61f);
        Assert.True(map.CanStand(tunnel.X, tunnel.Z));
        Assert.Equal(80, FixedMap.Size);
        Assert.All(map.SpawnPoints, p => Assert.True(map.CanStand(p.X, p.Z)));
        Assert.All(map.TerminalPositions, p => Assert.True(map.CanStand(p.X, p.Z)));
        Assert.True(map.CanStand(map.Exit.X, map.Exit.Z));
    }

    [Fact]
    public void SilentSurvivorAndItsCoordinateBytesAreAbsentFromKillerFrame()
    {
        var (fixture, killer, survivor) = CoreFixture.PairWithoutAfterimage();
        using var f = fixture;
        Assert.False(KillerTruth.Has(f.Server, killer.Last<S2C_WorldState>(), survivor.Id));
        survivor.Send(new C2S_MoveInput { DirX = 0.6f, DirZ = 0.8f, Seq = 1 });
        f.Tick();
        Assert.True(KillerTruth.Has(f.Server, killer.Last<S2C_WorldState>(), survivor.Id));
        survivor.Send(new C2S_MoveInput { Seq = 2 });
        f.Tick();
        Assert.True(KillerTruth.Has(f.Server, killer.Last<S2C_WorldState>(), survivor.Id));
        f.Tick();
        f.Tick();
        var world = killer.Last<S2C_WorldState>();
        Assert.False(KillerTruth.Has(f.Server, world, survivor.Id));
        Assert.Contains(world.Players, p => p.Id == killer.Id);
        byte[] frame = PacketCodec.Encode(world);
        var hidden = survivor.Self();
        Assert.Equal(-1, frame.AsSpan().IndexOf(BitConverter.GetBytes(hidden.X)));
        Assert.Equal(-1, frame.AsSpan().IndexOf(BitConverter.GetBytes(hidden.Z)));
        survivor.Send(new C2S_MoveInput { DirX = 1, Seq = 3 });
        f.Tick();
        Assert.True(KillerTruth.Has(f.Server, killer.Last<S2C_WorldState>(), survivor.Id));
    }

    [Fact]
    public void SurvivorReceivesExactKillerEntryInLineOfSightAndThreatUsesActualDistanceBands()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        // Killer spawns at (5,5), survivor at (5,26): both on the open x=5 line, 21 m apart, so (U2e) the survivor sees
        // the killer in every frame. The blocked case is covered by LineOfSightTests.
        var target = survivor.Self();
        Assert.Equal(0, survivor.Last<S2C_WorldState>().ThreatLevel);
        foreach (var (distance, threat) in new[] { (20f, 0), (15f, 1), (9f, 2), (3f, 3) })
        {
            // The killer walks up the z axis at 20 Hz, scaling its last step so it stops exactly at the distance.
            float goalZ = target.Z - distance;
            for (int i = 0; i < 200 && MathF.Abs(goalZ - killer.Self().Z) > 0.0005f; i++)
            {
                float remaining = goalZ - killer.Self().Z;
                float dir = Math.Clamp(remaining / (GameConstants.KillerSpeed * 0.05f), -1f, 1f);
                killer.Move(0, dir);
                f.Tick();
            }
            killer.Move(0, 0);
            f.Tick();
            var world = survivor.Last<S2C_WorldState>();
            Assert.InRange(target.Z - killer.Self().Z, distance - 0.01f, distance + 0.01f);
            Assert.Equal(threat, world.ThreatLevel);
            Assert.True(KillerSight.Verify(world, survivor.Id, killer.Self()));
        }
        survivor.Drain();
        Assert.All(survivor.Packets.OfType<S2C_WorldState>().Skip(1), w => Assert.Single(w.Players, p => p.Id == killer.Id));
    }
}
