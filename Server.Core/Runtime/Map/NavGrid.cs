using System;
using System.Collections.Generic;

namespace PingTheKiller.ServerCore.Map
{
    // 1 m grid over the fixed map. Bots and phantoms steer straight when the line is clear and otherwise follow a
    // breadth-first distance field toward the goal, so no goal behind a wall is unreachable.
    public sealed class NavGrid
    {
        private const int Cells = (int)FixedMap.Size;
        private const int Unreached = int.MaxValue;
        private const int Lookahead = 12;
        private const int MaxCachedFields = 64;
        private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] StepZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly FixedMap _map;
        private readonly bool[] _walkable = new bool[Cells * Cells];
        private readonly Dictionary<int, int[]> _fields = new Dictionary<int, int[]>();
        private readonly int[] _queue = new int[Cells * Cells];
        private readonly int[] _chain = new int[Lookahead];

        public NavGrid(FixedMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            for (int x = 0; x < Cells; x++)
                for (int z = 0; z < Cells; z++)
                    _walkable[x * Cells + z] = map.CanStand(x + 0.5f, z + 0.5f);
            var spots = new List<MapPoint>();
            for (float x = SpotSpacing / 2; x < FixedMap.Size; x += SpotSpacing)
                for (float z = SpotSpacing / 2; z < FixedMap.Size; z += SpotSpacing)
                    if (map.CanStand(x, z) && map.CanStand(x - 0.5f, z) && map.CanStand(x + 0.5f, z) &&
                        map.CanStand(x, z - 0.5f) && map.CanStand(x, z + 0.5f))
                        spots.Add(new MapPoint(x, z));
            Spots = spots.AsReadOnly();
        }

        private const float SpotSpacing = 3f;

        // Standable places on a 3 m lattice with some wall clearance: the refuges and hideouts survivor bots walk to.
        public IReadOnlyList<MapPoint> Spots { get; }

        // True when a body can slide straight from one point to the other without touching a wall.
        public bool LineClear(float x, float z, float tx, float tz)
        {
            float dx = tx - x, dz = tz - z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            int samples = Math.Max(1, (int)Math.Ceiling(length / 0.2f));
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                if (!_map.CanStand(x + dx * t, z + dz * t)) return false;
            }
            return true;
        }

        // Unit direction from (x, z) toward the goal along a walkable route; false when already at the goal.
        public bool Steer(float x, float z, float tx, float tz, out float dirX, out float dirZ)
        {
            float wx = tx, wz = tz;
            if (!LineClear(x, z, tx, tz))
            {
                var field = Field(Cell(tx, tz));
                int current = NearestWalkable(Cell(x, z), field);
                if (current >= 0 && field[current] != Unreached)
                {
                    int count = 0;
                    while (count < Lookahead && field[current] > 0)
                    {
                        int next = Downhill(current, field);
                        if (next < 0) break;
                        _chain[count++] = current = next;
                    }
                    if (count > 0)
                    {
                        int pick = _chain[0];
                        for (int i = count - 1; i >= 0; i--)
                            if (LineClear(x, z, CenterX(_chain[i]), CenterZ(_chain[i]))) { pick = _chain[i]; break; }
                        // The final cell holds the goal itself: aim at the exact goal once its cell is next.
                        bool goalCell = field[pick] == 0;
                        wx = goalCell ? tx : CenterX(pick);
                        wz = goalCell ? tz : CenterZ(pick);
                    }
                }
            }
            float dx = wx - x, dz = wz - z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.0001f) { dirX = dirZ = 0; return false; }
            dirX = dx / length;
            dirZ = dz / length;
            return true;
        }

        private static int Cell(float x, float z)
        {
            int cx = Math.Max(0, Math.Min(Cells - 1, (int)Math.Floor(x)));
            int cz = Math.Max(0, Math.Min(Cells - 1, (int)Math.Floor(z)));
            return cx * Cells + cz;
        }
        private static float CenterX(int cell) => cell / Cells + 0.5f;
        private static float CenterZ(int cell) => cell % Cells + 0.5f;

        private bool CanStep(int cell, int k, out int next)
        {
            int x = cell / Cells + StepX[k], z = cell % Cells + StepZ[k];
            next = x * Cells + z;
            if (x < 0 || z < 0 || x >= Cells || z >= Cells || !_walkable[next]) return false;
            // Diagonals never cut a wall corner.
            if (k >= 4 && (!_walkable[(cell / Cells + StepX[k]) * Cells + cell % Cells] ||
                           !_walkable[(cell / Cells) * Cells + cell % Cells + StepZ[k]])) return false;
            return true;
        }

        private int Downhill(int cell, int[] field)
        {
            int best = -1, bestValue = field[cell];
            for (int k = 0; k < 8; k++)
                if (CanStep(cell, k, out int next) && field[next] < bestValue) { bestValue = field[next]; best = next; }
            return best;
        }

        private int NearestWalkable(int cell, int[] field)
        {
            if (_walkable[cell]) return cell;
            int best = -1, bestValue = Unreached;
            int cx = cell / Cells, cz = cell % Cells;
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(Cells - 1, cx + 1); x++)
                for (int z = Math.Max(0, cz - 1); z <= Math.Min(Cells - 1, cz + 1); z++)
                {
                    int other = x * Cells + z;
                    if (_walkable[other] && field[other] < bestValue) { bestValue = field[other]; best = other; }
                }
            return best;
        }

        // Breadth-first steps to the goal cell; cached per goal cell (the map never changes).
        private int[] Field(int goal)
        {
            if (_fields.TryGetValue(goal, out var cached)) return cached;
            if (_fields.Count >= MaxCachedFields) _fields.Clear();
            var field = new int[Cells * Cells];
            for (int i = 0; i < field.Length; i++) field[i] = Unreached;
            int head = 0, tail = 0;
            field[goal] = 0;
            _queue[tail++] = goal;
            while (head < tail)
            {
                int cell = _queue[head++];
                for (int k = 0; k < 8; k++)
                {
                    if (!CanStep(cell, k, out int next) || field[next] != Unreached) continue;
                    field[next] = field[cell] + 1;
                    _queue[tail++] = next;
                }
            }
            _fields[goal] = field;
            return field;
        }
    }
}
