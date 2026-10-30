using PingTheKiller.ServerCore.Map;

namespace PingTheKiller.Harness.Tests;

// The fakes' own line-of-sight rule (U2e contract): the segment hits a closed, uninflated FixedMap wall AABB, touching
// included. Deliberately written apart from the harness's LineOfSight (slab test, no margins) so the two are not one code path.
internal static class FakeSight
{
    private static readonly IReadOnlyList<Wall> Walls = new FixedMap().Walls;

    public static bool Blocked(float ax, float az, float bx, float bz)
    {
        foreach (var w in Walls)
        {
            double enter = 0, exit = 1;
            if (!Slab(ax, bx - ax, w.MinX, w.MaxX, ref enter, ref exit) || !Slab(az, bz - az, w.MinZ, w.MaxZ, ref enter, ref exit)) continue;
            return true;
        }
        return false;
    }

    private static bool Slab(double start, double delta, double min, double max, ref double enter, ref double exit)
    {
        if (delta == 0) return start >= min && start <= max;
        double t1 = (min - start) / delta, t2 = (max - start) / delta;
        if (t1 > t2) (t1, t2) = (t2, t1);
        enter = Math.Max(enter, t1);
        exit = Math.Min(exit, t2);
        return enter <= exit;
    }
}
