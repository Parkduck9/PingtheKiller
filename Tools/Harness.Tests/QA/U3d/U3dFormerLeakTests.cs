using PingTheKiller.Harness.Tests.QA.U3b;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests.QA.U3d;

// Independent check of the implementer's claim that the two QA tests which started failing under U3d only because the spec
// changed were sending a contract-legal entry. The geometry is read at the moment of the leak and judged with the QA reference.
public sealed class U3dH6LegalKillerEntryInContactOnceTests
{
    [Fact]
    public async Task TheFormerU3bLeakIsContractLegalAndPassesH6()
    {
        double margin = double.NaN, distance = double.NaN;
        PlayerState entry = default;
        var once = Leaks.Once(c =>
        {
            var self = c.SurvivorEntries.First(p => p.Id == c.RecipientId);
            (margin, distance) = (RefLos.Margin(self.X, self.Z, c.KillerX, c.KillerZ, exactGrid: false), Math.Sqrt(Math.Pow(self.X - c.KillerX, 2) + Math.Pow(self.Z - c.KillerZ, 2)));
            entry = new PlayerState { Id = c.KillerId, Role = (byte)Role.Killer, X = c.KillerX, Z = c.KillerZ };
            c.World.Players.Add(entry);
        });
        await using var server = new QaRulesServer(new Knobs { SurvivorFrame = once });
        var (h6, _) = await U3bRun.H6Async(server, "u3d-former-u3b-leak-is-legal");
        U3dEvidence.Write("former-u3b-leak", $"reference margin={margin:F3} m (clear > 0.05), distance recipient-killer={distance:F2} m, entry flags={entry.Flags} timer={entry.StatusTimer} cc={entry.CaptureCount}");
        Assert.False(double.IsNaN(margin), "premise: the leak hook never fired");
        Assert.True(margin > RefLos.Band, $"the entry was NOT legal: reference margin {margin:F3} m");
        Assert.True(h6.Passed, h6.Evidence);
        Assert.Equal(1, U3dCounts.Parse(h6.Evidence).Visible);
    }
}

// The same former leak with one wrong field, and with blocked sight, must still fail (kept in the U3b file as the replacement).
public sealed class U3dH6FormerLeakWithBlockedSightStillFailsTests
{
    [Fact]
    public async Task EntryShownToTheSurvivorBehindTheWallFails()
    {
        // B (id 3) stands behind a wall in the sight step; the legal-shaped entry goes to it once.
        int sent = 0;
        var knob = new Knobs { SurvivorFrame = c =>
        {
            if (c.RecipientId != 3 || Interlocked.Exchange(ref sent, 1) != 0) return;
            var self = c.SurvivorEntries.First(p => p.Id == c.RecipientId);
            if (RefLos.Margin(self.X, self.Z, c.KillerX, c.KillerZ, exactGrid: false) > -0.3) { Interlocked.Exchange(ref sent, 0); return; }
            c.World.Players.Add(new PlayerState { Id = c.KillerId, Role = (byte)Role.Killer, X = c.KillerX, Z = c.KillerZ });
        } };
        await using var server = new QaRulesServer(knob);
        var (h6, _) = await U3bRun.H6Async(server, "u3d-former-leak-blocked");
        Assert.Equal(1, sent);
        Assert.False(h6.Passed, h6.Evidence);
        Assert.Contains("is blocked", h6.Evidence);
    }
}
