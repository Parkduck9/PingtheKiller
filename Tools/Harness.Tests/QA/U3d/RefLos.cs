using PingTheKiller.ServerCore.Map;

namespace PingTheKiller.Harness.Tests.QA.U3d;

public enum RefClass { Clear, Blocked, Ambiguous }

// QA-owned reference line-of-sight (U3d). Deliberately a different formulation from the harness' Liang-Barsky clip:
//  * contract decision (closed, uninflated AABB, touching blocks): endpoint inside the box OR the segment meets one of the four
//    edges, decided with exact integer orientation tests on a 1/4096 m grid (callers pass grid points);
//  * signed margin: deepest penetration (ternary search of a concave function) when the segment enters a wall, else the minimum
//    edge-to-segment distance (four point-to-segment distances per edge pair).
internal static class RefLos
{
    public const double Band = 0.05;
    public const long Scale = 4096;

    internal readonly record struct Box(double MinX, double MinZ, double MaxX, double MaxZ);

    public static readonly Box[] Boxes = new FixedMap().Walls.Select(w => new Box(w.MinX, w.MinZ, w.MaxX, w.MaxZ)).ToArray();

    // ---- exact integer predicate (inputs must be multiples of 1/Scale) -------------------------------------------------
    private static long Q(double v)
    {
        double s = v * Scale;
        long r = (long)Math.Round(s);
        if (Math.Abs(s - r) > 1e-9) throw new ArgumentException($"{v} is not a multiple of 1/{Scale}");
        return r;
    }

    private static long Cross(long ax, long az, long bx, long bz, long cx, long cz) => (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
    private static int Sign(long v) => v > 0 ? 1 : v < 0 ? -1 : 0;

    private static bool OnSegment(long ax, long az, long bx, long bz, long px, long pz) =>
        Cross(ax, az, bx, bz, px, pz) == 0 && px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx) && pz >= Math.Min(az, bz) && pz <= Math.Max(az, bz);

    private static bool SegmentsMeet(long ax, long az, long bx, long bz, long cx, long cz, long dx, long dz)
    {
        int d1 = Sign(Cross(cx, cz, dx, dz, ax, az)), d2 = Sign(Cross(cx, cz, dx, dz, bx, bz));
        int d3 = Sign(Cross(ax, az, bx, bz, cx, cz)), d4 = Sign(Cross(ax, az, bx, bz, dx, dz));
        if (d1 * d2 < 0 && d3 * d4 < 0) return true;
        return OnSegment(cx, cz, dx, dz, ax, az) || OnSegment(cx, cz, dx, dz, bx, bz) ||
               OnSegment(ax, az, bx, bz, cx, cz) || OnSegment(ax, az, bx, bz, dx, dz);
    }

    // Exact contract decision for one wall.
    public static bool Hits(Box w, double ax, double az, double bx, double bz)
    {
        long x0 = Q(ax), z0 = Q(az), x1 = Q(bx), z1 = Q(bz);
        long minX = Q(w.MinX), minZ = Q(w.MinZ), maxX = Q(w.MaxX), maxZ = Q(w.MaxZ);
        bool Inside(long x, long z) => x >= minX && x <= maxX && z >= minZ && z <= maxZ;
        if (Inside(x0, z0) || Inside(x1, z1)) return true;
        return SegmentsMeet(x0, z0, x1, z1, minX, minZ, maxX, minZ) || SegmentsMeet(x0, z0, x1, z1, maxX, minZ, maxX, maxZ) ||
               SegmentsMeet(x0, z0, x1, z1, maxX, maxZ, minX, maxZ) || SegmentsMeet(x0, z0, x1, z1, minX, maxZ, minX, minZ);
    }

    public static bool ExactBlocked(double ax, double az, double bx, double bz) => Boxes.Any(w => Hits(w, ax, az, bx, bz));

    // ---- double-precision signed margin --------------------------------------------------------------------------------
    private static double PointSegment(double px, double pz, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
        double t = len == 0 ? 0 : Math.Clamp(((px - ax) * dx + (pz - az) * dz) / len, 0, 1);
        return Math.Sqrt(Math.Pow(ax + t * dx - px, 2) + Math.Pow(az + t * dz - pz, 2));
    }

    // Distance between two disjoint segments: the minimum is at one of the four endpoints.
    private static double SegmentSegment(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz) =>
        Math.Min(Math.Min(PointSegment(ax, az, cx, cz, dx, dz), PointSegment(bx, bz, cx, cz, dx, dz)),
                 Math.Min(PointSegment(cx, cz, ax, az, bx, bz), PointSegment(dx, dz, ax, az, bx, bz)));

    private static double Depth(Box w, double ax, double az, double bx, double bz)
    {
        double G(double t)
        {
            double x = ax + (bx - ax) * t, z = az + (bz - az) * t;
            return Math.Min(Math.Min(x - w.MinX, w.MaxX - x), Math.Min(z - w.MinZ, w.MaxZ - z));
        }
        double lo = 0, hi = 1;
        for (int i = 0; i < 200; i++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            if (G(m1) < G(m2)) lo = m1; else hi = m2;
        }
        return Math.Max(0, G((lo + hi) / 2));
    }

    // Signed margin over all walls: -(deepest penetration) when blocked, otherwise the clearance from the nearest wall.
    public static double Margin(double ax, double az, double bx, double bz, bool exactGrid = true)
    {
        double deepest = -1, nearest = double.PositiveInfinity;
        foreach (var w in Boxes)
        {
            bool hit = exactGrid ? Hits(w, ax, az, bx, bz) : HitsDouble(w, ax, az, bx, bz);
            if (hit) deepest = Math.Max(deepest, Depth(w, ax, az, bx, bz));
            else
            {
                double d = double.PositiveInfinity;
                var corners = new[] { (w.MinX, w.MinZ), (w.MaxX, w.MinZ), (w.MaxX, w.MaxZ), (w.MinX, w.MaxZ) };
                for (int i = 0; i < 4; i++)
                {
                    var (cx, cz) = corners[i];
                    var (ex, ez) = corners[(i + 1) % 4];
                    d = Math.Min(d, SegmentSegment(ax, az, bx, bz, cx, cz, ex, ez));
                }
                nearest = Math.Min(nearest, d);
            }
        }
        return deepest >= 0 ? -deepest : nearest;
    }

    // Float-input variant for off-grid fuzz: decide "meets" by the concave penetration function (strictly > 0 means the
    // segment enters the interior) or an edge touch within 1e-12. Only used where the result is far from the band anyway.
    private static bool HitsDouble(Box w, double ax, double az, double bx, double bz)
    {
        if (Depth(w, ax, az, bx, bz) > 0) return true;
        double d = double.PositiveInfinity;
        var corners = new[] { (w.MinX, w.MinZ), (w.MaxX, w.MinZ), (w.MaxX, w.MaxZ), (w.MinX, w.MaxZ) };
        for (int i = 0; i < 4; i++)
        {
            var (cx, cz) = corners[i];
            var (ex, ez) = corners[(i + 1) % 4];
            d = Math.Min(d, SegmentSegment(ax, az, bx, bz, cx, cz, ex, ez));
        }
        return d < 1e-12;
    }

    public static RefClass Classify(double ax, double az, double bx, double bz, bool exactGrid = true)
    {
        double m = Margin(ax, az, bx, bz, exactGrid);
        return m > Band ? RefClass.Clear : m < -Band ? RefClass.Blocked : RefClass.Ambiguous;
    }

    // Snaps a coordinate to the 1/Scale grid (exactly representable as a float inside the map region).
    public static float Snap(double v) => (float)(Math.Round(v * Scale) / Scale);
}
