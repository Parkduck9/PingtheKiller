using PingTheKiller.ServerCore;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Test-local helpers for the independent U2c QA suite.

// Stream replay for the "nothing is derivable from MatchStart.Seed" checks. The server's hidden draws come from
// PrivateRandom (xoshiro256**); an attacker who knows the algorithm and the wire seed would try
// PrivateRandom.FromSeed(f(wireSeed), stream) and look for the server's draws inside it.
internal static class SeedAttack
{
    internal static ulong[] Draw(object stream, int count)
    {
        var random = (PrivateRandom)stream;
        var values = new ulong[count];
        for (int i = 0; i < count; i++) values[i] = random.NextUInt64();
        return values;
    }

    internal static bool Contains(int candidateSeed, int streamIndex, ulong[] observed, int maxOffset)
    {
        var random = PrivateRandom.FromSeed(candidateSeed, streamIndex);
        var window = new ulong[observed.Length];
        for (int i = 0; i < observed.Length; i++) window[i] = random.NextUInt64();
        for (int offset = 0; offset < maxOffset; offset++)
        {
            bool all = true;
            for (int j = 0; j < observed.Length && all; j++) all = window[j] == observed[j];
            if (all) return true;
            Array.Copy(window, 1, window, 0, window.Length - 1);
            window[^1] = random.NextUInt64();
        }
        return false;
    }

    internal static IEnumerable<int> Derived(int wire)
    {
        yield return wire; yield return -wire; yield return ~wire; yield return wire + 1; yield return wire - 1;
        yield return unchecked(wire * 7919 + 17); yield return unchecked(wire * 31 + 7); yield return unchecked(wire ^ 0x5DEECE66);
        yield return unchecked((int)(((long)wire * 0x5DEECE66DL + 0xB) & 0x7fffffff)); yield return unchecked(wire * 1664525 + 1013904223);
        yield return unchecked((int)(wire * 2654435761L));
    }
}

internal static class KillerViewAsserts
{
    // The structural contract of one killer-bound frame: exactly one killer entry (real Id); every other entry is an
    // anonymous Signal-only survivor blip.
    internal static void AssertAnonymous(S2C_WorldState w, int killerId, string context)
    {
        Assert.True(w.Players.Count <= Protocol.MaxWorldEntries, context + ": entry cap exceeded");
        Assert.Equal(1, w.Players.Count(p => p.Role == (byte)Role.Killer));
        foreach (var p in w.Players)
        {
            if (p.Role == (byte)Role.Killer) { Assert.Equal(killerId, p.Id); continue; }
            Assert.True(p.Role == (byte)Role.Survivor && p.Id == 0 && p.Yaw == 0 && p.StatusTimer == 0 && p.CaptureCount == 0 &&
                p.Flags == (byte)PlayerFlags.Signal && (p.Status == (byte)PlayerStatus.Normal || p.Status == (byte)PlayerStatus.Lagging),
                $"{context}: entry not anonymous (id={p.Id} yaw={p.Yaw} timer={p.StatusTimer} cc={p.CaptureCount} flags={p.Flags} status={p.Status})");
        }
        foreach (var t in w.Terminals) Assert.True(t.Progress == 0 || t.Progress == 100, context + ": terminal progress " + t.Progress);
    }
}
