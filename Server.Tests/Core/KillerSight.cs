using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// U2e contract (GDD §2, 10/30): a survivor-bound WorldState carries the killer entry only while the recipient is Normal or
// Lagging and the segment between the two server positions of that tick touches no wall. Sweep tests use this to keep
// their "no killer data in survivor frames" strength for every blocked frame and to check the exact entry otherwise.
// FixedMap.SegmentBlocked itself is verified independently in LineOfSightTests.
internal static class KillerSight
{
    private static readonly FixedMap Map = new();

    internal static bool Expected(PlayerState self, float killerX, float killerZ) =>
        (self.Status == (byte)PlayerStatus.Normal || self.Status == (byte)PlayerStatus.Lagging) &&
        !Map.SegmentBlocked(self.X, self.Z, killerX, killerZ);

    // The exact contract fields. yaw is null when the ground truth has no facing (bot killer read from PlayerSnapshot).
    internal static void AssertEntry(PlayerState entry, int killerId, float x, float z, float? yaw, string context = "")
    {
        Assert.True(entry.Id == killerId, context + ": killer entry id " + entry.Id);
        Assert.Equal((byte)Role.Killer, entry.Role);
        Assert.Equal((byte)PlayerStatus.Normal, entry.Status);
        Assert.True(Same(entry.X, x) && Same(entry.Z, z), $"{context}: killer entry at ({entry.X:R},{entry.Z:R}), truth ({x:R},{z:R})");
        if (yaw is float y) Assert.True(Same(entry.Yaw, y), $"{context}: killer entry yaw {entry.Yaw:R}, truth {y:R}");
        Assert.Equal((byte)PlayerFlags.None, entry.Flags);
        Assert.Equal(0f, entry.StatusTimer);
        Assert.Equal(0, entry.CaptureCount);
    }

    internal static void AssertEntry(PlayerState entry, PlayerState killerSelf, string context = "") =>
        AssertEntry(entry, killerSelf.Id, killerSelf.X, killerSelf.Z, killerSelf.Yaw, context);

    // Verifies the killer entry of a survivor-bound frame against the same tick's ground truth and returns whether it was
    // (correctly) present.
    internal static bool Verify(S2C_WorldState w, int selfId, int killerId, float killerX, float killerZ, float? killerYaw, string context = "")
    {
        var self = w.Players.Single(p => p.Id == selfId);
        var entries = w.Players.Where(p => p.Role == (byte)Role.Killer || p.Id == killerId).ToList();
        if (!Expected(self, killerX, killerZ))
        {
            Assert.True(entries.Count == 0, context + ": killer entry in a survivor frame without line of sight");
            return false;
        }
        Assert.True(entries.Count == 1, context + ": " + entries.Count + " killer entries in a survivor frame with line of sight");
        AssertEntry(entries[0], killerId, killerX, killerZ, killerYaw, context);
        return true;
    }

    internal static bool Verify(S2C_WorldState w, int selfId, PlayerState killerSelf, string context = "") =>
        Verify(w, selfId, killerSelf.Id, killerSelf.X, killerSelf.Z, killerSelf.Yaw, context);

    // The same frame with the legal killer entry removed, for byte-level checks of everything else.
    internal static S2C_WorldState WithoutKiller(S2C_WorldState w) => new S2C_WorldState
    {
        Tick = w.Tick, TimeLeft = w.TimeLeft, ThreatLevel = w.ThreatLevel, CollapseLeft = w.CollapseLeft, Exits = w.Exits.ToList(),
        HatchVisible = w.HatchVisible, HatchX = w.HatchX, HatchZ = w.HatchZ,
        TerminalsRequired = w.TerminalsRequired, Terminals = w.Terminals.ToList(),
        Players = w.Players.Where(p => p.Role != (byte)Role.Killer).ToList()
    };

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
}
