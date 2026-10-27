using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3b;

// Direct tests of KillerLeakScan.Excess on hand-built frames.
public sealed class U3bLeakScanUnitTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int A = 2, B = 3;
    private static ReceivedFrame Frame(IPacket p) => new(p, PacketCodec.Encode(p), 0);
    private static HashSet<int> Bits(params float[] values) => values.Select(KillerLeakScan.Bits).ToHashSet();

    // The real frame that made a correct fake fail H6 (QA run U3bHalfSpeed, tick 203): the killer stands at its
    // spawn (5, 5); a survivor's X float is 0x412040A0, whose bytes A0 40 follow the entry's Role=0, Status=0
    // bytes: the four bytes 00 00 A0 40 are the little-endian encoding of 5.0f spread over two fields.
    // No killer coordinate is present in this frame, so a correct scan must not flag it.
    [Fact]
    public void CleanSurvivorFrameIsNotFlaggedWhenFieldBoundariesFormAKillerFloat()
    {
        var world = new S2C_WorldState
        {
            Tick = 203, TimeLeft = 589.85f, TerminalsRequired = 2,
            Terminals = new List<TerminalState> { new() { Id = 0, X = 10, Z = 25, Progress = 100 }, new() { Id = 3, X = 12, Z = 49 } },
            Players = new List<PlayerState>
            {
                new() { Id = A, X = BitConverter.Int32BitsToSingle(0x412040A0), Z = 30.684925f },
                new() { Id = B, X = 10.476913f, Z = 31.179619f }
            }
        };
        string? error = KillerLeakScan.Excess(Frame(world), Bits(5f), new[] { A, B });
        Assert.True(error == null, "false positive on a frame that contains no killer coordinate: " + error);
    }

    // Round killer values (any float with two zero low bytes: spawn 5.0, 10.0, 12.0, ...) have 16 free bits to
    // match against Role/Status (always 00 00) + the survivor X low bytes, so each survivor entry has a 2^-16 chance
    // per round killer value. This measures it on uniformly random positions over the 60 m map.
    [Fact]
    public void RandomCleanFramesAlmostNeverFlagged()
    {
        var rng = new Random(12345);
        int frames = 200_000, flagged = 0;
        var killer = Bits(5f);
        for (int i = 0; i < frames; i++)
        {
            var world = new S2C_WorldState
            {
                Tick = (uint)i, TimeLeft = 600 - i * 0.05f, TerminalsRequired = 2,
                Players = new List<PlayerState>
                {
                    new() { Id = A, X = (float)(rng.NextDouble() * 60), Z = (float)(rng.NextDouble() * 60) },
                    new() { Id = B, X = (float)(rng.NextDouble() * 60), Z = (float)(rng.NextDouble() * 60) }
                }
            };
            if (KillerLeakScan.Excess(Frame(world), killer, new[] { A, B }) != null) flagged++;
        }
        output.WriteLine($"false positives: {flagged}/{frames} clean 2-survivor frames ({flagged * 100.0 / frames:F4} %)");
        // One scenario run sends ~2 x 1000 survivor-bound frames; require < 0.01 % false positives per frame
        // (< 1 in 10 runs would fail H6 spuriously because of the spawn coordinate alone).
        Assert.True(flagged <= frames / 10_000, $"{flagged}/{frames} clean frames flagged ({flagged * 100.0 / frames:F4} %)");
    }
}
