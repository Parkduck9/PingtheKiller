using PingTheKiller.ServerCore.Map;

namespace PingTheKiller.Harness;

public enum Sight { Clear, Blocked, Ambiguous }

// U3d: the harness's own line-of-sight test (never a server helper). Contract: the 2D segment from the recipient to the
// killer must intersect no FixedMap wall; walls are closed AABBs, not inflated, so touching an edge or a corner blocks.
// Margin is signed: the segment's clearance from the nearest wall when it misses every wall (> 0), or minus its deepest
// penetration into a wall (<= 0; a touch is 0). Within Band of the boundary (|margin| <= 0.05 m) the harness does not decide.
public static class LineOfSight
{
    public const double Band = 0.05;
    public static readonly IReadOnlyList<Wall> MapWalls = new FixedMap().Walls;

    public static Sight Classify(double ax, double az, double bx, double bz, IReadOnlyList<Wall>? walls = null)
    {
        double margin = Margin(ax, az, bx, bz, walls);
        return margin > Band ? Sight.Clear : margin < -Band ? Sight.Blocked : Sight.Ambiguous;
    }

    // The contract decision without a band (touching blocks).
    public static bool Blocked(double ax, double az, double bx, double bz, IReadOnlyList<Wall>? walls = null)
    {
        foreach (var wall in walls ?? MapWalls)
            if (Clip(wall, ax, az, bx, bz, out _, out _)) return true;
        return false;
    }

    public static double Margin(double ax, double az, double bx, double bz, IReadOnlyList<Wall>? walls = null)
    {
        double margin = double.PositiveInfinity;
        foreach (var wall in walls ?? MapWalls) margin = Math.Min(margin, Margin(wall, ax, az, bx, bz));
        return margin;
    }

    public static double Margin(Wall wall, double ax, double az, double bx, double bz)
    {
        if (Clip(wall, ax, az, bx, bz, out double t0, out double t1)) return -Depth(wall, ax, az, bx, bz, t0, t1);
        // Disjoint convex sets: the minimum is at a segment endpoint (to the box) or a box corner (to the segment).
        double d = Math.Min(PointToBox(wall, ax, az), PointToBox(wall, bx, bz));
        foreach (var (cx, cz) in new[] { (wall.MinX, wall.MinZ), (wall.MinX, wall.MaxZ), (wall.MaxX, wall.MinZ), (wall.MaxX, wall.MaxZ) })
            d = Math.Min(d, PointToSegment(cx, cz, ax, az, bx, bz));
        return d;
    }

    // Liang-Barsky against the closed box: the parameter interval [t0, t1] of the segment inside it (boundary included).
    private static bool Clip(Wall w, double ax, double az, double bx, double bz, out double t0, out double t1)
    {
        double lo = 0, hi = 1, dx = bx - ax, dz = bz - az;
        bool Edge(double p, double q) // p * t <= q
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > hi) return false; if (r > lo) lo = r; }
            else { if (r < lo) return false; if (r < hi) hi = r; }
            return true;
        }
        bool hit = Edge(-dx, ax - w.MinX) && Edge(dx, w.MaxX - ax) && Edge(-dz, az - w.MinZ) && Edge(dz, w.MaxZ - az);
        (t0, t1) = (lo, hi);
        return hit;
    }

    // Deepest point of the segment inside the box: max over [t0, t1] of the distance to the nearest face. That distance is the
    // minimum of four linear functions of t (concave), so its maximum is at an end of the interval or where two of them cross.
    private static double Depth(Wall w, double ax, double az, double bx, double bz, double t0, double t1)
    {
        double dx = bx - ax, dz = bz - az;
        var lines = new (double C, double S)[] { (ax - w.MinX, dx), (w.MaxX - ax, -dx), (az - w.MinZ, dz), (w.MaxZ - az, -dz) };
        double Inside(double t)
        {
            double m = double.PositiveInfinity;
            foreach (var (c, s) in lines) m = Math.Min(m, c + s * t);
            return m;
        }
        double best = Math.Max(Inside(t0), Inside(t1));
        for (int i = 0; i < lines.Length; i++)
            for (int j = i + 1; j < lines.Length; j++)
            {
                if (lines[i].S == lines[j].S) continue;
                double t = (lines[j].C - lines[i].C) / (lines[i].S - lines[j].S);
                if (t > t0 && t < t1) best = Math.Max(best, Inside(t));
            }
        return Math.Max(0, best);
    }

    private static double PointToBox(Wall w, double x, double z)
    {
        double dx = Math.Max(Math.Max(w.MinX - x, 0), x - w.MaxX);
        double dz = Math.Max(Math.Max(w.MinZ - z, 0), z - w.MaxZ);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static double PointToSegment(double px, double pz, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az, length = dx * dx + dz * dz;
        double t = length == 0 ? 0 : Math.Clamp(((px - ax) * dx + (pz - az) * dz) / length, 0, 1);
        double ex = ax + t * dx - px, ez = az + t * dz - pz;
        return Math.Sqrt(ex * ex + ez * ez);
    }
}
