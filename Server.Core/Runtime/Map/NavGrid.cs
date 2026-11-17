using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore.Map
{
    // Static 0.5 m occupancy, shared by every bot. Scratch buffers belong to the single game-loop thread.
    public sealed class NavGrid
    {
        public const float CellSize = 0.5f;
        public const float Clearance = FixedMap.PlayerRadius + 0.05f;
        public const int Cells = (int)(FixedMap.Size / CellSize);
        public const int NodeCount = Cells * Cells;
        private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] StepZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private readonly FixedMap _map;
        private readonly bool[] _walkable = new bool[NodeCount];
        private readonly Wall[] _inflated;
        private readonly int[] _stamp = new int[NodeCount], _closed = new int[NodeCount];
        private readonly int[] _cost = new int[NodeCount], _parent = new int[NodeCount];
        private readonly int[] _heap = new int[NodeCount], _heapIndex = new int[NodeCount];
        private readonly int[] _priority = new int[NodeCount], _chain = new int[NodeCount];
        private readonly List<MapPoint> _steerPath = new List<MapPoint>(128);
        private int _generation, _heapCount;

        public NavGrid(FixedMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            _map = map;
            var rails = WorldChaseProps.Rails;
            _inflated = new Wall[map.Walls.Count + map.Obstacles.Count + rails.Count];
            int k = 0;
            foreach (var w in map.Walls) _inflated[k++] = Inflate(w.MinX, w.MinZ, w.MaxX, w.MaxZ);
            foreach (var o in map.Obstacles) _inflated[k++] = Inflate(o.MinX, o.MinZ, o.MaxX, o.MaxZ);
            // GDD v2 section 8: a rail is a static blocker; FindPath may cross it through a vault link (see below).
            foreach (var r in rails) _inflated[k++] = Inflate(r.MinX, r.MinZ, r.MaxX, r.MaxZ);
            var shutters = WorldChaseProps.Shutters;
            _shutterBoxes = new Wall[shutters.Count];
            _shutterDown = new bool[shutters.Count];
            for (int i = 0; i < shutters.Count; i++)
                _shutterBoxes[i] = Inflate(shutters[i].MinX, shutters[i].MinZ, shutters[i].MaxX, shutters[i].MaxZ);
            for (int i = 0; i < NodeCount; i++) _walkable[i] = PointClear(Center(i));
            _portalA = new int[rails.Count];
            _portalB = new int[rails.Count];
            for (int i = 0; i < rails.Count; i++)
            {
                float reach = rails[i].HalfThickness + Clearance + PortalMargin;
                _portalA[i] = Cell(rails[i].CenterX - rails[i].CrossX * reach, rails[i].CenterZ - rails[i].CrossZ * reach);
                _portalB[i] = Cell(rails[i].CenterX + rails[i].CrossX * reach, rails[i].CenterZ + rails[i].CrossZ * reach);
            }
            var spots = new List<MapPoint>();
            for (float x = 1.5f; x < FixedMap.Size; x += 3)
                for (float z = 1.5f; z < FixedMap.Size; z += 3)
                    if (map.CanStand(x, z, Clearance + .5f)) spots.Add(new MapPoint(x, z));
            Spots = spots.AsReadOnly();
        }

        // Down shutters (dynamic; the game loop calls SetShutterDown on every state change) and the rail vault links.
        private readonly Wall[] _shutterBoxes;
        private readonly bool[] _shutterDown;
        private readonly int[] _portalA, _portalB;
        private const float PortalMargin = 0.3f;

        public void SetShutterDown(int id, bool down)
        {
            if (_shutterDown[id] == down) return;
            _shutterDown[id] = down;
            var box = _shutterBoxes[id];
            int x0 = Math.Max(0, (int)(box.MinX / CellSize) - 1), x1 = Math.Min(Cells - 1, (int)(box.MaxX / CellSize) + 1);
            int z0 = Math.Max(0, (int)(box.MinZ / CellSize) - 1), z1 = Math.Min(Cells - 1, (int)(box.MaxZ / CellSize) + 1);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    _walkable[x * Cells + z] = PointClear(Center(x * Cells + z));
        }

        public void ResetShutters()
        {
            for (int i = 0; i < _shutterDown.Length; i++) SetShutterDown(i, false);
        }

        public bool ShutterDown(int id) => _shutterDown[id];

        private static Wall Inflate(float x, float z, float xx, float zz) =>
            new Wall(x - Clearance, z - Clearance, xx + Clearance, zz + Clearance);
        public IReadOnlyList<MapPoint> Spots { get; }
        public bool IsWalkable(int cell) => cell >= 0 && cell < NodeCount && _walkable[cell];
        public static MapPoint Center(int cell) => new MapPoint((cell / Cells + .5f) * CellSize, (cell % Cells + .5f) * CellSize);
        public static int Cell(float x, float z) =>
            Math.Max(0, Math.Min(Cells - 1, (int)(x / CellSize))) * Cells + Math.Max(0, Math.Min(Cells - 1, (int)(z / CellSize)));
        private bool PointClear(MapPoint p)
        {
            for (int i = 0; i < _inflated.Length; i++)
            {
                var w = _inflated[i];
                if (p.X >= w.MinX && p.X <= w.MaxX && p.Z >= w.MinZ && p.Z <= w.MaxZ) return false;
            }
            for (int i = 0; i < _shutterBoxes.Length; i++)
            {
                if (!_shutterDown[i]) continue;
                var w = _shutterBoxes[i];
                if (p.X >= w.MinX && p.X <= w.MaxX && p.Z >= w.MinZ && p.Z <= w.MaxZ) return false;
            }
            return p.X >= Clearance && p.X <= FixedMap.Size - Clearance && p.Z >= Clearance && p.Z <= FixedMap.Size - Clearance;
        }

        public bool LineClear(float x, float z, float tx, float tz)
        {
            for (int i = 0; i < _inflated.Length; i++)
                if (_inflated[i].IntersectsSegment(x, z, tx, tz)) return false;
            for (int i = 0; i < _shutterBoxes.Length; i++)
                if (_shutterDown[i] && _shutterBoxes[i].IntersectsSegment(x, z, tx, tz)) return false;
            return true;
        }

        // Collision sliding or a human takeover may leave a body inside the extra 5 cm navigation margin.
        // Permit only the initial segment to leave boxes that contain the start, validating the actual body throughout.
        private bool StartLineClear(float x, float z, float tx, float tz)
        {
            bool escape = false;
            for (int i = 0; i < _inflated.Length; i++)
            {
                var w = _inflated[i];
                if (!w.IntersectsSegment(x,z,tx,tz)) continue;
                if (x < w.MinX || x > w.MaxX || z < w.MinZ || z > w.MaxZ) return false;
                escape = true;
            }
            for (int i = 0; i < _shutterBoxes.Length; i++)
            {
                if (!_shutterDown[i]) continue;
                var w = _shutterBoxes[i];
                if (!w.IntersectsSegment(x, z, tx, tz)) continue;
                if (x < w.MinX || x > w.MaxX || z < w.MinZ || z > w.MaxZ) return false;
                escape = true;
            }
            if (!escape) return true;
            int samples = Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(tx-x),Math.Abs(tz-z))/.025f));
            for (int i=0;i<=samples;i++)
                if (!_map.CanStand(x+(tx-x)*i/samples,z+(tz-z)*i/samples)) return false;
            return true;
        }
        // Both orthogonal neighbours must be free for a diagonal step.
        public bool CanStep(int cell, int direction, out int next)
        {
            int cx = cell / Cells, cz = cell % Cells;
            int x = cx + StepX[direction], z = cz + StepZ[direction];
            next = x * Cells + z;
            if (x < 0 || z < 0 || x >= Cells || z >= Cells || !_walkable[next]) return false;
            return direction < 4 || (_walkable[x * Cells + cz] && _walkable[cx * Cells + z]);
        }

        private int Nearest(float x, float z, bool requireConnection)
        {
            int cell = Cell(x, z), best = -1;
            float distance = float.MaxValue;
            int cx = cell / Cells, cz = cell % Cells;
            for (int ix = Math.Max(0, cx - 8); ix <= Math.Min(Cells - 1, cx + 8); ix++)
                for (int iz = Math.Max(0, cz - 8); iz <= Math.Min(Cells - 1, cz + 8); iz++)
                {
                    int n = ix * Cells + iz;
                    if (!_walkable[n]) continue;
                    var p = Center(n);
                    float d = (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);
                    if (d < distance && (!requireConnection || StartLineClear(x, z, p.X, p.Z))) { best = n; distance = d; }
                }
            return best;
        }

        // Octile distance is admissible for costs 1000 / 1414. A heap with decrease-key bounds scratch storage.
        private static int Heuristic(int a, int b)
        {
            int dx = Math.Abs(a / Cells - b / Cells), dz = Math.Abs(a % Cells - b % Cells);
            return 1000 * Math.Max(dx, dz) + 414 * Math.Min(dx, dz);
        }
        private bool Before(int a, int b) => _priority[a] < _priority[b] || (_priority[a] == _priority[b] && _cost[a] > _cost[b]);
        private void PushUp(int cell, int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2, other = _heap[parent];
                if (!Before(cell, other)) break;
                _heap[index] = other; _heapIndex[other] = index; index = parent;
            }
            _heap[index] = cell; _heapIndex[cell] = index;
        }
        private int Pop()
        {
            int result = _heap[0], cell = _heap[--_heapCount], index = 0;
            while (index * 2 + 1 < _heapCount)
            {
                int child = index * 2 + 1;
                if (child + 1 < _heapCount && Before(_heap[child + 1], _heap[child])) child++;
                int other = _heap[child];
                if (!Before(other, cell)) break;
                _heap[index] = other; _heapIndex[other] = index; index = child;
            }
            if (_heapCount > 0) { _heap[index] = cell; _heapIndex[cell] = index; }
            return result;
        }

        // Caller retains its list (capacity 128 is ample for this map). No allocations after list warm-up.
        // Includes the exact start and, when standable, the exact goal. Invalid goals snap to a nearby static cell.
        // vaultCostMeters >= 0 adds the rail vault links (portal to portal across each rail's vault point) at the crossing
        // length plus that many metres; a negative value (phantoms, default) never crosses a rail.
        public bool FindPath(float x, float z, float tx, float tz, List<MapPoint> path) => FindPath(x, z, tx, tz, path, -1);

        public bool FindPath(float x, float z, float tx, float tz, List<MapPoint> path, float vaultCostMeters)
        {
            path.Clear();
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) || float.IsNaN(tx) || float.IsInfinity(tx) || float.IsNaN(tz) || float.IsInfinity(tz)) return false;
            bool exactGoal = PointClear(new MapPoint(tx, tz));
            if (exactGoal && LineClear(x, z, tx, tz))
            { path.Add(new MapPoint(x, z)); path.Add(new MapPoint(tx, tz)); return true; }
            int start = Nearest(x, z, true), goal = Nearest(tx, tz, exactGoal);
            if (start < 0 || goal < 0) return false;
            if (++_generation == int.MaxValue)
            { Array.Clear(_stamp, 0, NodeCount); Array.Clear(_closed, 0, NodeCount); _generation = 1; }
            _heapCount = 0;
            _stamp[start] = _generation; _cost[start] = 0; _parent[start] = -1;
            _priority[start] = Heuristic(start, goal); PushUp(start, _heapCount++);
            while (_heapCount > 0)
            {
                int cell = Pop();
                _closed[cell] = _generation;
                if (cell == goal)
                {
                    int count = 0;
                    for (int n = goal; n >= 0; n = _parent[n]) _chain[count++] = n;
                    path.Add(new MapPoint(x, z));
                    float ax = x, az = z;
                    // String pulling over the reconstructed route; all shortcuts test inflated closed boxes.
                    for (int index = count - 1; index >= 0;)
                    {
                        int pick = 0;
                        while (pick < index && !(path.Count == 1 ? StartLineClear(ax, az, Center(_chain[pick]).X, Center(_chain[pick]).Z) : LineClear(ax, az, Center(_chain[pick]).X, Center(_chain[pick]).Z))) pick++;
                        var p = Center(_chain[pick]); path.Add(p); ax = p.X; az = p.Z; index = pick - 1;
                    }
                    if (exactGoal) path.Add(new MapPoint(tx, tz));
                    return true;
                }
                int links = vaultCostMeters >= 0 ? _portalA.Length : 0;
                for (int k = 0; k < 8 + links; k++)
                {
                    int next, step;
                    if (k < 8)
                    {
                        if (!CanStep(cell, k, out next) || _closed[next] == _generation) continue;
                        step = k < 4 ? 1000 : 1414;
                    }
                    else
                    {
                        int r = k - 8;
                        next = cell == _portalA[r] ? _portalB[r] : cell == _portalB[r] ? _portalA[r] : -1;
                        if (next < 0 || !_walkable[next] || _closed[next] == _generation) continue;
                        var a = Center(cell);
                        var b = Center(next);
                        double crossing = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
                        step = (int)Math.Ceiling((crossing + vaultCostMeters) * 1000 / CellSize);
                    }
                    int cost = _cost[cell] + step;
                    bool fresh = _stamp[next] != _generation;
                    if (!fresh && cost >= _cost[next]) continue;
                    _cost[next] = cost; _parent[next] = cell; _priority[next] = cost + Heuristic(next, goal);
                    if (fresh) { _stamp[next] = _generation; PushUp(next, _heapCount++); }
                    else PushUp(next, _heapIndex[next]);
                }
            }
            return false;
        }

        // Compatibility for static-map phantom steering. Brains retain their own paths below.
        public bool Steer(float x, float z, float tx, float tz, out float dx, out float dz)
        {
            dx = dz = 0;
            if (!FindPath(x, z, tx, tz, _steerPath)) return false;
            for (int i = 1; i < _steerPath.Count; i++)
            {
                float vx = _steerPath[i].X - x, vz = _steerPath[i].Z - z;
                float length = (float)Math.Sqrt(vx * vx + vz * vz);
                if (length < .05f) continue;
                dx = vx / length; dz = vz / length; return true;
            }
            return false;
        }
    }
}
