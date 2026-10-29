using PingTheKiller.Harness;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

// U3c: killer-view entries are attributed by tick and position (KillerView/TickJoin), never by Id.
public sealed class KillerViewTests
{
    private const int KillerId = 1;
    private static readonly PlayerState Own = new() { Id = KillerId, Role = (byte)Role.Killer, X = 30.5f, Z = 4.25f, Flags = 1 };
    private static readonly PlayerState Stopped = S(2, 14.72031f, -10.19577f);
    private static readonly PlayerState Bot = S(10, 14.72031f + 0.0137f, -10.19577f + 0.2f);

    private static PlayerState S(int id, float x, float z) => new() { Id = id, X = x, Z = z, Flags = 1 };
    private static S2C_WorldState W(uint tick, params PlayerState[] players) => new() { Tick = tick, Players = players.ToList() };

    [Fact]
    public void EntriesAreAttributedByPositionWhateverIdTheyCarry()
    {
        // Anonymous (Id 0), borrowed (the stopped survivor's real Id on the bot's position) and real Id: all go to the bot.
        var world = W(5, Own, Bot with { Id = 0 }, Bot with { Id = Stopped.Id }, Bot);
        var result = KillerView.Attribute(world, KillerId, new[] { Stopped, Bot });
        Assert.Equal(3, result.Count); // the killer's own entry (real Id + Role Killer) is not attributed
        Assert.All(result, a => Assert.True(a.Explained && a.Nearest.SequenceEqual(new[] { Bot.Id }) && !a.ExactTie && a.Distance == 0));
        // An entry with the killer's Id but Role Survivor is not the killer's own entry.
        Assert.Single(KillerView.Attribute(W(5, Own, Bot with { Id = KillerId }), KillerId, new[] { Stopped, Bot }));
    }

    [Theory]
    [InlineData(0f, 0f)]          // exact (anonymous)
    [InlineData(0.03f, 0f)]       // offset
    [InlineData(-0.0003f, 0.004f)] // rounded-like
    public void TheStoppedSurvivorIsFoundAtExactRoundedOrOffsetPositionsEvenWithABotNearby(float dx, float dz)
    {
        var entry = Stopped with { Id = 0, X = Stopped.X + dx, Z = Stopped.Z + dz };
        var a = Assert.Single(KillerView.Attribute(W(5, entry), KillerId, new[] { Bot, Stopped }));
        Assert.True(a.Explained);
        Assert.Equal(new[] { Stopped.Id }, a.Nearest);
        Assert.False(a.ExactTie);
    }

    [Fact]
    public void OnlyBitIdenticalPositionsAreAmbiguous()
    {
        var onTop = Bot with { X = Stopped.X, Z = Stopped.Z };
        var tie = Assert.Single(KillerView.Attribute(W(5, Stopped with { Id = 0 }), KillerId, new[] { Stopped, onTop }));
        Assert.True(tie.ExactTie);
        Assert.Equal(new[] { Stopped.Id, Bot.Id }, tie.Nearest);
        // Equidistant but different positions: both are nearest, and it is NOT an exact tie (the stopped one is not excused).
        var split = Assert.Single(KillerView.Attribute(W(5, S(0, 0f, 0f)), KillerId, new[] { S(2, 0.01f, 0f), S(10, -0.01f, 0f) }));
        Assert.Equal(new[] { 2, 10 }, split.Nearest);
        Assert.False(split.ExactTie);
    }

    [Fact]
    public void FarNonFiniteOrUnmatchedEntriesAreUnexplained()
    {
        Assert.False(Assert.Single(KillerView.Attribute(W(5, S(0, Stopped.X + 0.051f, Stopped.Z)), KillerId, new[] { Stopped })).Explained);
        Assert.False(Assert.Single(KillerView.Attribute(W(5, S(0, float.NaN, 1f)), KillerId, new[] { Stopped })).Explained);
        Assert.False(Assert.Single(KillerView.Attribute(W(5, Stopped), KillerId, Array.Empty<PlayerState>())).Explained);
        Assert.True(Assert.Single(KillerView.Attribute(W(5, S(0, Stopped.X + 0.049f, Stopped.Z)), KillerId, new[] { Stopped })).Explained);
    }

    [Fact]
    public void TickJoinJoinsEitherArrivalOrderAndSkipsTicksWithoutTruth()
    {
        var joined = new List<uint>();
        var join = new TickJoin(KillerId, (world, _, _) => joined.Add(world.Tick));
        join.AddKiller(W(5, Own, Bot with { Id = 0 }));   // killer first
        join.AddTruth(W(4, Stopped, Bot));
        Assert.Empty(joined);
        Assert.True(join.AwaitingTruth);
        join.AddTruth(W(5, Stopped, Bot));
        join.AddTruth(W(6, Stopped, Bot));               // survivor first
        join.AddKiller(W(6, Own));
        join.AddKiller(W(7, Own));                       // never gets truth
        Assert.True(join.AwaitingTruth);
        join.AddTruth(W(8, Stopped, Bot));
        Assert.False(join.AwaitingTruth);
        Assert.Equal(new uint[] { 5, 6 }, joined);
        Assert.Equal((2, 1, 1), (join.Joined, join.Entries, join.Skipped));
    }

    [Fact]
    public void TickJoinFailsAnUnexplainedEntryAndNamesAStaleSnapshot()
    {
        var join = new TickJoin(KillerId, (_, _, _) => { });
        join.AddTruth(W(3, Stopped, Bot));
        var error = Assert.Throws<CheckFailure>(() => join.AddKiller(W(3, Own, S(0, 3.21f, -4.56f))));
        Assert.Contains("unexplained killer-view entry at tick=3", error.Message);
        Assert.Contains("leak", error.Message);
        // A one-tick-stale position of a moving survivor is also unexplained; the diagnostic names the matching tick.
        var mover = S(4, 1.5f, 2.5f);
        join.AddTruth(W(9, mover));
        join.AddTruth(W(10, mover with { X = 1.7f }));
        error = Assert.Throws<CheckFailure>(() => join.AddKiller(W(10, mover with { Id = 0 })));
        Assert.Contains("survivor 4's position at tick=9", error.Message);
    }
}

// Stand-in for U2c's S2C_SelfState (protocol v0.3, not in this Shared yet): only the recipient's own timers.
internal sealed class FakeSelfState : IPacket
{
    public float SkillCooldown, RescueProgress, InvulnerableLeft;
    public PacketId Id => (PacketId)0x10;
    public void Write(PacketWriter w)
    {
        w.WriteSingle(SkillCooldown);
        w.WriteSingle(RescueProgress);
        w.WriteSingle(InvulnerableLeft);
    }
}

public sealed class UnknownPacketScanTests
{
    private const float KillerX = 12.345678f, KillerZ = 40.98765f;
    private static readonly HashSet<int> Killer = new() { KillerLeakScan.Bits(KillerX), KillerLeakScan.Bits(KillerZ) };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(14)]
    public void UnknownFrameRawScanFindsKillerBitsAtAnyAlignment(int offset)
    {
        var raw = LobbyServer.UnknownFrame(0x7E, 12, 0x7E);
        Measurements.FloatBytes(offset % 2 == 0 ? KillerX : KillerZ).CopyTo(raw, offset);
        string? error = KillerLeakScan.UnknownLeak(new UnknownFrame(0x7E, raw, 0), Killer);
        Assert.NotNull(error);
        Assert.Contains("killer coordinate bytes in unknown packet 0x7E (update Shared)", error);
        Assert.Null(KillerLeakScan.UnknownLeak(new UnknownFrame(0x7E, LobbyServer.UnknownFrame(0x7E, 12, 0x7E), 0), Killer));
    }

    [Fact]
    public void KnownIdsAreExactlyTheSharedPacketIds()
    {
        foreach (PacketId id in Enum.GetValues<PacketId>()) Assert.True(WireClient.IsKnownId((ushort)id));
        foreach (ushort id in new ushort[] { 0, 0x7E, 0xFFFF }) Assert.False(WireClient.IsKnownId(id));
        // 0x10 is the expected Id of U2c's S2C_SelfState: unknown until Shared defines it, then known and decoded automatically.
        if (KillerLeakScan.SelfStateType == null) Assert.False(WireClient.IsKnownId(0x10));
    }

    [Fact]
    public void SkipCountsAreReportedPerIdBetweenSnapshots()
    {
        var tally = new UnknownPackets();
        tally.Add(0x7E);
        var before = tally.Snapshot();
        Assert.Null(UnknownPackets.Describe(before, tally.Snapshot()));
        tally.Add(0x7E);
        tally.Add(0x10);
        tally.Add(0x7E);
        Assert.Equal("skipped unknown packets: id=0x10 ×1, id=0x7E ×2", UnknownPackets.Describe(before, tally.Snapshot()));
    }

    [Fact]
    public void SelfStateSeamTreatsOwnTimerFloatsAsCoincidencesButStillRejectsExtraBytes()
    {
        var packet = new FakeSelfState { SkillCooldown = KillerX, RescueProgress = 0.5f, InvulnerableLeft = KillerZ };
        var frame = new ReceivedFrame(packet, PacketCodec.Encode(packet), 0);
        int coincidences = 0;
        Assert.Null(KillerLeakScan.Excess(frame, Killer, new[] { 2, 3 }, n => coincidences += n, typeof(FakeSelfState)));
        Assert.Equal(2, coincidences);
        // Without the seam (a type that is not the SelfState packet) the same frame is scanned conservatively and fails.
        Assert.NotNull(KillerLeakScan.Excess(frame, Killer, new[] { 2, 3 }));
        var padded = PacketCodec.Encode(packet).Concat(new byte[] { 0 }).ToArray();
        Assert.Contains("unexplained bytes", KillerLeakScan.Excess(new ReceivedFrame(packet, padded, 0), Killer, new[] { 2, 3 },
            selfStateType: typeof(FakeSelfState)));
    }

    [Fact]
    public void RealSelfStateIsTreatedAsOwnTimersOnceSharedHasIt()
    {
        if (KillerLeakScan.SelfStateType is not { } type) return; // before U2c merges, 0x10 is an unknown Id (see above)
        var packet = (IPacket)Activator.CreateInstance(type)!;
        foreach (var field in type.GetFields().Where(f => f.FieldType == typeof(float))) field.SetValue(packet, KillerX);
        Assert.Null(KillerLeakScan.Excess(new ReceivedFrame(packet, PacketCodec.Encode(packet), 0), Killer, new[] { 2, 3 }));
    }
}
