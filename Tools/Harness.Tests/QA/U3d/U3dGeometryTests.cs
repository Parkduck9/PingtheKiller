using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

internal static class U3dEvidence
{
    // QA_U3D_EVIDENCE_FILE collects one line per scenario for docs/qa/U3d-qa-a1.md.
    public static void Write(string label, string text)
    {
        string? file = Environment.GetEnvironmentVariable("QA_U3D_EVIDENCE_FILE");
        if (file == null) return;
        lock (typeof(U3dEvidence)) File.AppendAllText(file, $"{label}\t{text.ReplaceLineEndings(" ")}\n");
    }
}

// ---- handwritten geometry, each checked against the QA reference as well ----------------------------------------------
public sealed class U3dGeometryCaseTests
{
    // FixedMap walls: boundary (-1,-1,0,61) (60,-1,61,61) (0,-1,60,0) (0,60,60,61); inner (18,14,20,32) (36,28,38,47) (24,42,32,44) (40,14,49,16).
    private static void Check(string name, double ax, double az, double bx, double bz, Sight expected, bool contractBlocked)
    {
        Assert.Equal(expected, LineOfSight.Classify(ax, az, bx, bz));
        Assert.Equal(expected, LineOfSight.Classify(bx, bz, ax, az));                    // symmetric
        Assert.Equal(contractBlocked, LineOfSight.Blocked(ax, az, bx, bz));
        Assert.Equal(contractBlocked, LineOfSight.Blocked(bx, bz, ax, az));
        Assert.Equal(contractBlocked, RefLos.ExactBlocked(ax, az, bx, bz));             // independent exact reference agrees
        var reference = RefLos.Classify(ax, az, bx, bz);
        Assert.Equal(expected.ToString(), reference.ToString());
        Assert.True(Math.Abs(LineOfSight.Margin(ax, az, bx, bz) - RefLos.Margin(ax, az, bx, bz)) < 1e-9, name);
    }

    [Fact]
    public void SegmentThroughAWallIsBlocked() => Check("through", 10, 20, 30, 20, Sight.Blocked, true);

    [Fact]
    public void SegmentThroughTheThinWallFromAboveIsBlocked() => Check("through-thin", 26, 34, 26, 44, Sight.Blocked, true);

    [Fact]
    public void SegmentEndingInsideAWallIsBlocked() => Check("end-inside", 10, 20, 19, 20, Sight.Blocked, true);

    [Fact]
    public void BothEndpointsInsideTheSameWallAreBlocked() => Check("both-inside", 18.5, 20, 19.5, 25, Sight.Blocked, true);

    [Fact]
    public void ASegmentLyingOnWallEdgeIsBlockedByContractAndAmbiguousForTheHarness()
    {
        // along the bottom face z=14 (outside the wall x range too) and along the left face x=18
        Check("flush-bottom-face", 10, 12, 26, 12, Sight.Ambiguous, true);
        Check("flush-left-face", 12, 14, 12, 34, Sight.Ambiguous, true);
        Check("flush-inside-face-span", 18.5, 12, 19.5, 12, Sight.Ambiguous, true);
        Check("flush-boundary-wall", 5, 0, 55, 0, Sight.Ambiguous, true);                 // face of the bottom boundary wall
    }

    [Fact]
    public void ASegmentEndingOnAnEdgeIsBlockedByContractAndAmbiguousForTheHarness()
    {
        Check("end-on-face", 19, 10, 19, 12, Sight.Ambiguous, true);
        Check("end-on-vertical-face", 10, 20, 12, 20, Sight.Ambiguous, true);
    }

    [Fact]
    public void ASegmentTouchingACornerIsBlockedByContractAndAmbiguousForTheHarness()
    {
        Check("through-corner", 11, 13, 13, 11, Sight.Ambiguous, true);                 // diagonal through (18,14) only
        Check("end-on-corner", 10, 20, 12, 12, Sight.Ambiguous, true);
        Check("start-on-corner", 12, 12, 10, 20, Sight.Ambiguous, true);
        Check("corner-of-corner", 24, 12, 26, 10, Sight.Ambiguous, true);
        Check("graze-far-corner", 23, 11, 25, 13, Sight.Ambiguous, true);              // passes exactly through (20,32) only
    }

    [Fact]
    public void AParallelSegmentJustOffAFaceFollowsTheBand()
    {
        // parallel to the face z=14, 0.03 m / 0.06 m outside
        Check("parallel-0.03", 10, 11.96875, 30, 11.96875, Sight.Ambiguous, false);   // 0.03125 m
        Check("parallel-0.06", 10, 11.9375, 30, 11.9375, Sight.Clear, false);         // 0.0625 m
        Check("parallel-0.03-inside", 10, 12.03125, 30, 12.03125, Sight.Ambiguous, true);
        Check("parallel-0.06-inside", 10, 12.0625, 30, 12.0625, Sight.Blocked, true);
    }

    [Fact]
    public void ZeroLengthSegmentsAreJudgedAsPoints()
    {
        Check("point-open", 5, 5, 5, 5, Sight.Clear, false);
        Check("point-inside-wall", 19, 20, 19, 20, Sight.Blocked, true);
        Check("point-on-face", 12, 20, 12, 20, Sight.Ambiguous, true);
        Check("point-on-corner", 12, 12, 12, 12, Sight.Ambiguous, true);
        Check("point-0.03-off", 11.96875, 20, 11.96875, 20, Sight.Ambiguous, false);
        Check("point-0.0625-off", 11.9375, 20, 11.9375, 20, Sight.Clear, false);
    }

    [Fact]
    public void SurvivorStandingOnTheKillerIsClearInTheOpenAndNeverCrashes()
    {
        var killer = new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 30.5f, Z = 8.25f };
        var me = new PlayerState { Id = 2, X = 30.5f, Z = 8.25f };
        var world = new S2C_WorldState { Tick = 5, Players = { me, killer with { Flags = 0 } } };
        var verdict = KillerSight.Judge(world, 2, 1, killer);
        Assert.Null(verdict.Leak);
        Assert.Equal(SightOutcome.Visible, verdict.Outcome);
    }

    [Fact]
    public void SegmentsTheBandOnEitherSideAreAmbiguousNeverPassOrFail()
    {
        // Distance 0.049 / 0.051 on the Clear side and penetration 0.049 / 0.051 on the Blocked side (non-grid values on purpose).
        Assert.Equal(Sight.Ambiguous, LineOfSight.Classify(10, 12 - 0.049, 30, 12 - 0.049));
        Assert.Equal(Sight.Clear, LineOfSight.Classify(10, 12 - 0.051, 30, 12 - 0.051));
        Assert.Equal(Sight.Ambiguous, LineOfSight.Classify(10, 12 + 0.049, 30, 12 + 0.049));
        Assert.Equal(Sight.Blocked, LineOfSight.Classify(10, 12 + 0.051, 30, 12 + 0.051));
    }

    // Band pairs reach the judge as Ambiguous: counted, no verdict either way, whether the entry is present or absent.
    [Fact]
    public void BandPairsAreCountedAsAmbiguousByTheJudgeEntryPresentOrAbsent()
    {
        var killer = new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 10, Z = 11.96875f };
        var entry = killer with { Flags = 0 };
        var me = new PlayerState { Id = 2, X = 30, Z = 11.96875f };
        Assert.Equal(SightOutcome.Ambiguous, KillerSight.Judge(new S2C_WorldState { Tick = 3, Players = { me } }, 2, 1, killer).Outcome);
        var with = KillerSight.Judge(new S2C_WorldState { Tick = 3, Players = { me, entry } }, 2, 1, killer);
        Assert.Equal(SightOutcome.Ambiguous, with.Outcome);
        Assert.Null(with.Leak);
        var counts = new SightCounts();
        counts.Add(with.Outcome);
        Assert.Equal(0, counts.Visible + counts.BlockedAbsent);
        Assert.Equal(1, counts[SightOutcome.Ambiguous]);
    }
}

// ---- fuzz: the harness LineOfSight against the QA reference over FixedMap ---------------------------------------------
public sealed class U3dGeometryFuzzTests
{
    private static (double X, double Z) Point(Random random, int mode)
    {
        var walls = RefLos.Boxes;
        switch (mode)
        {
            case 0: return (RefLos.Snap(-3 + random.NextDouble() * 66), RefLos.Snap(-3 + random.NextDouble() * 66));
            case 1: // within 0.2 m of a random wall boundary
            {
                var w = walls[random.Next(walls.Length)];
                double x = w.MinX - 0.2 + random.NextDouble() * (w.MaxX - w.MinX + 0.4);
                double z = w.MinZ - 0.2 + random.NextDouble() * (w.MaxZ - w.MinZ + 0.4);
                return (RefLos.Snap(x), RefLos.Snap(z));
            }
            case 2: // exact wall corner or a point on an edge line (touching cases)
            {
                var w = walls[random.Next(walls.Length)];
                double x = random.Next(3) switch { 0 => w.MinX, 1 => w.MaxX, _ => RefLos.Snap(w.MinX + random.NextDouble() * (w.MaxX - w.MinX)) };
                double z = random.Next(3) switch { 0 => w.MinZ, 1 => w.MaxZ, _ => RefLos.Snap(w.MinZ + random.NextDouble() * (w.MaxZ - w.MinZ)) };
                return (x, z);
            }
            case 3: // integer / half-integer lattice: many exactly aligned and touching segments
                return (random.Next(-6, 127) / 2.0, random.Next(-6, 127) / 2.0);
            default: // a hair (1/64 .. 1/16 m) outside or inside a wall face: the ambiguous band itself
            {
                var w = walls[random.Next(walls.Length)];
                double d = (random.Next(1, 5) / 64.0) * (random.Next(2) == 0 ? 1 : -1);
                bool vertical = random.Next(2) == 0;
                double along = RefLos.Snap((vertical ? w.MinZ : w.MinX) + random.NextDouble() * (vertical ? w.MaxZ - w.MinZ : w.MaxX - w.MinX));
                return vertical ? (random.Next(2) == 0 ? w.MinX + d : w.MaxX + d, along) : (along, random.Next(2) == 0 ? w.MinZ + d : w.MaxZ + d);
            }
        }
    }

    [Fact]
    public void GridFuzzOver150kPairsAgreesWithTheExactReferenceOutsideTheAmbiguousBand()
    {
        var random = new Random(20261030);
        const int Pairs = 150_000;
        int clear = 0, blocked = 0, ambiguous = 0, touching = 0, zeroLength = 0, contractMismatch = 0, classMismatch = 0, overDecisive = 0;
        double maxMarginError = 0;
        var examples = new List<string>();
        for (int n = 0; n < Pairs; n++)
        {
            var a = Point(random, random.Next(5));
            (double X, double Z) b = random.Next(10) switch
            {
                0 => a,                                   // zero length
                1 => Point(random, 3),
                2 => (a.X + (random.Next(3) - 1) * random.Next(1, 8), a.Z), // horizontal, often flush against a face
                3 => (a.X, a.Z + (random.Next(3) - 1) * random.Next(1, 8)),
                _ => Point(random, random.Next(5))
            };
            if (a == b) zeroLength++;
            bool exact = RefLos.ExactBlocked(a.X, a.Z, b.X, b.Z);
            double margin = RefLos.Margin(a.X, a.Z, b.X, b.Z);
            var expected = margin > RefLos.Band + 1e-6 ? RefClass.Clear : margin < -RefLos.Band - 1e-6 ? RefClass.Blocked : RefClass.Ambiguous;
            bool onBoundary = Math.Abs(Math.Abs(margin) - RefLos.Band) <= 1e-6;
            var actual = LineOfSight.Classify(a.X, a.Z, b.X, b.Z);
            if (margin == 0 && exact) touching++;
            if (LineOfSight.Blocked(a.X, a.Z, b.X, b.Z) != exact)
            {
                contractMismatch++;
                if (examples.Count < 5) examples.Add($"contract ({a.X},{a.Z})-({b.X},{b.Z}) harness={LineOfSight.Blocked(a.X, a.Z, b.X, b.Z)} ref={exact}");
            }
            maxMarginError = Math.Max(maxMarginError, Math.Abs(LineOfSight.Margin(a.X, a.Z, b.X, b.Z) - margin));
            if (LineOfSight.Classify(b.X, b.Z, a.X, a.Z) != actual) { classMismatch++; examples.Add($"asymmetric ({a.X},{a.Z})-({b.X},{b.Z})"); }
            switch (actual) { case Sight.Clear: clear++; break; case Sight.Blocked: blocked++; break; default: ambiguous++; break; }
            if (onBoundary) continue; // exactly at the band edge either answer is acceptable
            if (expected.ToString() != actual.ToString())
            {
                if (actual != Sight.Ambiguous && expected == RefClass.Ambiguous) overDecisive++;
                else classMismatch++;
                if (examples.Count < 8) examples.Add($"class ({a.X},{a.Z})-({b.X},{b.Z}) harness={actual} ref={expected} margin={margin:R}");
            }
        }
        string summary = $"grid fuzz {Pairs} pairs: clear={clear} blocked={blocked} ambiguous={ambiguous} (exact touches={touching}, zero-length={zeroLength}); " +
            $"contract mismatches={contractMismatch}, class mismatches={classMismatch}, decided-inside-band={overDecisive}, max |margin error|={maxMarginError:E2}";
        U3dEvidence.Write("los-fuzz-grid", summary);
        Assert.True(contractMismatch == 0 && classMismatch == 0 && overDecisive == 0 && maxMarginError < 1e-6,
            summary + " | " + string.Join(" | ", examples));
        Assert.True(clear > 10_000 && blocked > 10_000 && ambiguous > 2_000 && touching > 500, summary); // the fuzz really covers each class
    }

    [Fact]
    public void FloatFuzzOver60kOffGridPairsAgreesOutsideTheAmbiguousBand()
    {
        var random = new Random(77);
        const int Pairs = 60_000;
        int clear = 0, blocked = 0, ambiguous = 0, mismatches = 0;
        double maxMarginError = 0;
        var examples = new List<string>();
        for (int n = 0; n < Pairs; n++)
        {
            float R() => (float)(-3 + random.NextDouble() * 66);
            float ax = R(), az = R(), bx = R(), bz = R();
            if (n % 3 == 0) { var w = RefLos.Boxes[random.Next(RefLos.Boxes.Length)]; az = (float)(w.MinZ + (random.NextDouble() - 0.5) * 0.3); bz = (float)(az + (random.NextDouble() - 0.5) * 0.1); }
            double margin = RefLos.Margin(ax, az, bx, bz, exactGrid: false);
            var actual = LineOfSight.Classify(ax, az, bx, bz);
            maxMarginError = Math.Max(maxMarginError, Math.Abs(LineOfSight.Margin(ax, az, bx, bz) - margin));
            switch (actual) { case Sight.Clear: clear++; break; case Sight.Blocked: blocked++; break; default: ambiguous++; break; }
            if (Math.Abs(Math.Abs(margin) - RefLos.Band) <= 1e-6) continue;
            var expected = margin > RefLos.Band ? Sight.Clear : margin < -RefLos.Band ? Sight.Blocked : Sight.Ambiguous;
            if (expected != actual)
            {
                mismatches++;
                if (examples.Count < 8) examples.Add($"({ax:R},{az:R})-({bx:R},{bz:R}) harness={actual} ref={expected} margin={margin:R}");
            }
        }
        string summary = $"float fuzz {Pairs} pairs: clear={clear} blocked={blocked} ambiguous={ambiguous}; mismatches={mismatches}, max |margin error|={maxMarginError:E2}";
        U3dEvidence.Write("los-fuzz-float", summary);
        Assert.True(mismatches == 0 && maxMarginError < 1e-6, summary + " | " + string.Join(" | ", examples));
        Assert.True(clear > 5_000 && blocked > 5_000 && ambiguous > 500, summary);
    }

    [Fact]
    public void ClassifyAgreesWithTheContractBoolWheneverItDecides()
    {
        // Independent of the reference: a Clear classification must never be contract-blocked and a Blocked one always.
        var random = new Random(5);
        for (int n = 0; n < 50_000; n++)
        {
            float R() => (float)(-3 + random.NextDouble() * 66);
            float ax = R(), az = R(), bx = R(), bz = R();
            var s = LineOfSight.Classify(ax, az, bx, bz);
            if (s == Sight.Clear) Assert.False(LineOfSight.Blocked(ax, az, bx, bz));
            if (s == Sight.Blocked) Assert.True(LineOfSight.Blocked(ax, az, bx, bz));
        }
    }
}
