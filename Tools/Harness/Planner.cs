using System.Numerics;
using PingTheKiller.ServerCore.Map;

namespace PingTheKiller.Harness;

// A point to keep away from (another player, a capture zone). Vector2.Y is world Z.
public readonly record struct Circle(Vector2 Center, float Radius);

// 8-connected grid search over the server's static FixedMap walls (0.5 m cells, PlayerRadius plus a
// safety margin). Reads geometry only; movement itself is always the server's.
public sealed class Planner
{
    public const float Cell = 0.5f, Margin = 0.35f;
    private const int N = (int)(FixedMap.Size / Cell) + 1;
    private readonly IReadOnlyList<Wall> walls;
    private readonly bool[] open = new bool[N * N];

    public Planner() : this(new FixedMap()) { }

    public Planner(FixedMap map)
    {
        walls = map.Walls;
        for (int i = 0; i < open.Length; i++) open[i] = Clear(Pos(i));
    }

    private static Vector2 Pos(int node) => new(node / N * Cell, node % N * Cell);

    public bool Clear(Vector2 p)
    {
        foreach (var wall in walls)
            if (wall.IntersectsCircle(p.X, p.Y, FixedMap.PlayerRadius + Margin)) return false;
        return true;
    }

    public bool Walkable(Vector2 p, IReadOnlyList<Circle> avoid) =>
        Clear(p) && avoid.All(c => Vector2.Distance(p, c.Center) >= c.Radius);

    public List<Vector2>? PathTo(Vector2 from, Vector2 to, IReadOnlyList<Circle> avoid)
    {
        int goal = Nearest(to, avoid);
        if (goal < 0) return null;
        var path = Search(from, node => node == goal, to, avoid);
        path?.Add(to);
        return path == null ? null : Smooth(path, avoid);
    }

    // Cheapest walkable point (by path length) that satisfies goal, e.g. "at least 9 m from A".
    public List<Vector2>? PathToNearest(Vector2 from, Func<Vector2, bool> goal, IReadOnlyList<Circle> avoid)
    {
        var path = Search(from, node => goal(Pos(node)), null, avoid);
        return path == null ? null : Smooth(path, avoid);
    }

    public static float Length(IReadOnlyList<Vector2> path)
    {
        float length = 0;
        for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
        return length;
    }

    public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector2.Distance(p, a + t * ab);
    }

    private bool Open(int node, IReadOnlyList<Circle> avoid)
    {
        if (!open[node]) return false;
        var p = Pos(node);
        foreach (var c in avoid) if (Vector2.Distance(p, c.Center) < c.Radius) return false;
        return true;
    }

    private int Nearest(Vector2 p, IReadOnlyList<Circle> avoid)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        int ci = (int)MathF.Round(p.X / Cell), cj = (int)MathF.Round(p.Y / Cell);
        for (int i = Math.Max(0, ci - 8); i <= Math.Min(N - 1, ci + 8); i++)
            for (int j = Math.Max(0, cj - 8); j <= Math.Min(N - 1, cj + 8); j++)
            {
                int node = i * N + j;
                float distance = Vector2.Distance(p, Pos(node));
                if (distance < bestDistance && Open(node, avoid)) { best = node; bestDistance = distance; }
            }
        return best;
    }

    private List<Vector2>? Search(Vector2 from, Func<int, bool> isGoal, Vector2? target, IReadOnlyList<Circle> avoid)
    {
        int start = Nearest(from, avoid);
        if (start < 0) return null;
        var cost = new float[N * N];
        var parent = new int[N * N];
        Array.Fill(cost, float.MaxValue);
        cost[start] = 0;
        parent[start] = -1;
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(start, 0);
        var closed = new bool[N * N];
        while (queue.TryDequeue(out int node, out _))
        {
            if (closed[node]) continue;
            closed[node] = true;
            if (isGoal(node))
            {
                var nodes = new List<Vector2>();
                for (int n = node; n >= 0; n = parent[n]) nodes.Add(Pos(n));
                nodes.Add(from);
                nodes.Reverse();
                return nodes;
            }
            int i = node / N, j = node % N;
            for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    if ((di == 0 && dj == 0) || i + di < 0 || i + di >= N || j + dj < 0 || j + dj >= N) continue;
                    int next = (i + di) * N + j + dj;
                    if (closed[next] || !Open(next, avoid)) continue;
                    // No corner cutting: a diagonal step needs both orthogonal neighbours open.
                    if (di != 0 && dj != 0 && (!Open((i + di) * N + j, avoid) || !Open(i * N + j + dj, avoid))) continue;
                    float step = cost[node] + (di != 0 && dj != 0 ? Cell * 1.4142135f : Cell);
                    if (step >= cost[next]) continue;
                    cost[next] = step;
                    parent[next] = node;
                    queue.Enqueue(next, step + (target is { } t ? Vector2.Distance(Pos(next), t) : 0));
                }
        }
        return null;
    }

    private bool LineClear(Vector2 a, Vector2 b, IReadOnlyList<Circle> avoid)
    {
        int samples = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 0.1f));
        for (int k = 1; k < samples; k++)
            if (!Walkable(Vector2.Lerp(a, b, k / (float)samples), avoid)) return false;
        return true;
    }

    private List<Vector2> Smooth(List<Vector2> path, IReadOnlyList<Circle> avoid)
    {
        var result = new List<Vector2> { path[0] };
        for (int i = 0; i < path.Count - 1;)
        {
            int j = path.Count - 1;
            while (j > i + 1 && !LineClear(path[i], path[j], avoid)) j--;
            result.Add(path[j]);
            i = j;
        }
        return result;
    }
}
