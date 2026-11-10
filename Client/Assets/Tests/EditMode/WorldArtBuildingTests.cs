using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Editor.Setup;
using PingTheKiller.ServerCore.Map;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.Tests
{
    // U4k: building groups (wall boxes that share an edge read as one building), uppers on every interior box, no slot on a
    // shared edge, and a continuous skyline (brief U4k-buildings-a1).
    public sealed class WorldArtBuildingTests
    {
        private const float MinUpperTop = 6f, MaxBuildingHeight = 11f;
        private static FixedMap Map => SharedMap.Map;
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        private static WorldArt Art()
        {
            WorldArt art = WorldArt.Load();
            Assert.That(art, Is.Not.Null, "Resources/WorldArt.asset (Ping the Killer/Setup World Art)");
            Assert.That(art.Matches(Map), Is.True, "the catalog was built for this map");
            return art;
        }

        private GameObject SpawnWall(WorldArt art, int index)
        {
            Wall wall = Map.Walls[index];
            GameObject item = Object.Instantiate(art.Walls[index].Prefab);
            item.transform.position = new Vector3((wall.MinX + wall.MaxX) * .5f, 0, (wall.MinZ + wall.MaxZ) * .5f);
            created.Add(item);
            return item;
        }

        private static bool Interior(Wall wall) => wall.MinX >= 0 && wall.MinZ >= 0 && wall.MaxX <= FixedMap.Size && wall.MaxZ <= FixedMap.Size;

        // The expected groups of FixedMap v2.1 (31 walls; 0-3 are the border). A map change must update this list, and with
        // it the art (rerun Ping the Killer/Setup World Art), instead of silently dropping uppers.
        private static readonly int[][] ExpectedGroups =
        {
            new[] { 4, 5 }, new[] { 6, 7, 8 }, new[] { 9 }, new[] { 10, 11 }, new[] { 12 }, new[] { 13, 14 }, new[] { 15 },
            new[] { 16, 17 }, new[] { 18, 19 }, new[] { 20, 21 }, new[] { 22 }, new[] { 23, 24, 25 }, new[] { 26, 27 },
            new[] { 28 }, new[] { 29, 30 }
        };

        [Test]
        public void TheGroupsOfTheCurrentMapAreTheExpectedOnes()
        {
            List<int[]> groups = WorldArtSetup.Groups(Map);
            Assert.That(groups.Count, Is.EqualTo(ExpectedGroups.Length), Describe(groups));
            for (int g = 0; g < ExpectedGroups.Length; g++) Assert.That(groups[g], Is.EqualTo(ExpectedGroups[g]), Describe(groups));
            Assert.That(WorldArtSetup.SharedEdges(Map).Count, Is.EqualTo(12), "shared edges: 4/5, 6/8, 7/8, 10/11, 13/14, 16/17, 18/19, 20/21, 23/25, 24/25, 26/27, 29/30");
            var covered = new HashSet<int>();
            foreach (int[] group in groups) foreach (int i in group) covered.Add(i);
            for (int i = 0; i < Map.Walls.Count; i++)
                Assert.That(covered.Contains(i), Is.EqualTo(Interior(Map.Walls[i])), "wall " + i + ": every interior box is in exactly one group, border boxes in none");
        }

        private static string Describe(List<int[]> groups)
        {
            var parts = new List<string>();
            foreach (int[] group in groups) parts.Add("{" + string.Join(",", group) + "}");
            return string.Join(" ", parts);
        }

        [Test]
        public void EveryInteriorWallHasAnUpperReachingSixToElevenMetres()
        {
            WorldArt art = Art();
            for (int i = 0; i < Map.Walls.Count; i++)
            {
                if (!Interior(Map.Walls[i])) continue;
                float top = float.NegativeInfinity;
                int uppers = 0;
                foreach (Renderer renderer in SpawnWall(art, i).GetComponentsInChildren<Renderer>())
                {
                    if (renderer.name == "Podium") continue;
                    uppers++;
                    top = Mathf.Max(top, renderer.bounds.max.y);
                }
                Assert.That(uppers, Is.GreaterThan(0), "wall " + i + " has upper renderers");
                Assert.That(top, Is.GreaterThanOrEqualTo(MinUpperTop), "wall " + i + " reaches 6 m");
                Assert.That(top, Is.LessThanOrEqualTo(MaxBuildingHeight), "wall " + i + " stays at or below 11 m");
            }
        }

        // Above MapGeometry.WallHeight every upper vertex stays at least Setback inside the outline of its group's union.
        [Test]
        public void UpperStoreysStandBackFromTheGroupOutlineSoTheRaisedSurvivorCameraNeverEntersThem()
        {
            WorldArt art = Art();
            int checkedVertices = 0;
            foreach (int[] group in WorldArtSetup.Groups(Map))
            {
                List<Vector4> outline = Outline(group);
                foreach (int i in group)
                    foreach (MeshFilter filter in SpawnWall(art, i).GetComponentsInChildren<MeshFilter>())
                    {
                        if (filter.name == "Podium") continue;
                        Matrix4x4 m = filter.transform.localToWorldMatrix;
                        foreach (Vector3 local in filter.sharedMesh.vertices)
                        {
                            Vector3 p = m.MultiplyPoint3x4(local);
                            if (p.y <= MapGeometry.WallHeight + .01f) continue;
                            checkedVertices++;
                            string name = "group {" + string.Join(",", group) + "} wall " + i + " / " + filter.name + " vertex " + p;
                            Assert.That(InUnion(group, p.x, p.z), Is.True, name + " inside the group");
                            Assert.That(Distance(outline, p.x, p.z), Is.GreaterThanOrEqualTo(WorldArtSetup.Setback - .01f), name);
                        }
                    }
            }
            Assert.That(checkedVertices, Is.GreaterThan(1000));
        }

        // Rays from points beside every shared edge (0.05 m either side, at most 0.5 m apart along the part of the edge at least
        // Setback from the outline, at 4 m and 5.5 m) in +x, -x, +z, -z and up all meet the group's upper geometry: the points are
        // enclosed, so no slot opens over the shared edge.
        [Test]
        public void EverySharedEdgeIsClosedByUpperGeometry()
        {
            WorldArt art = Art();
            var triangles = new Dictionary<int, List<Vector3>>();
            foreach (int[] group in WorldArtSetup.Groups(Map))
            {
                var list = new List<Vector3>();
                foreach (int i in group)
                {
                    foreach (MeshFilter filter in SpawnWall(art, i).GetComponentsInChildren<MeshFilter>())
                        if (filter.name != "Podium") Collect(filter, list);
                    triangles[i] = list;
                }
            }
            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up };
            int samples = 0;
            foreach (WorldArtSetup.SharedEdge edge in WorldArtSetup.SharedEdges(Map))
            {
                List<Vector3> group = triangles[edge.A];
                // Both ends 0.05 m inside the inset part: a point exactly on an outer wall face is neither in nor out.
                float first = edge.Lo + WorldArtSetup.Setback + .05f, last = edge.Hi - WorldArtSetup.Setback - .05f;
                int steps = Mathf.CeilToInt((last - first) / .5f);
                for (int k = 0; k <= steps; k++)
                    foreach (float side in new[] { -.05f, .05f })
                        foreach (float y in new[] { 4f, 5.5f })
                        {
                            float t = steps == 0 ? first : first + (last - first) * k / steps;
                            Vector3 p = edge.AlongX ? new Vector3(t, y, edge.Line + side) : new Vector3(edge.Line + side, y, t);
                            foreach (Vector3 direction in directions)
                                Assert.That(Raycast(group, p, direction), Is.LessThan(float.PositiveInfinity), "edge " + edge + " at " + p + " open toward " + direction);
                            samples++;
                        }
            }
            Assert.That(samples, Is.GreaterThan(100));
        }

        // Seen from inside the arena, the skyline pieces (taller than 5 m) on each side leave no gap wider than 0.3 m: south and
        // north over the arena span plus the corner squares, west and east over the arena span. The strip between the border
        // walls and the skyline is closed by pieces standing within 0.1 m of the border walls over the whole arena span.
        [Test]
        public void TheSkylineHasNoGapOnAnySideAndClosesTheStripBehindTheBorderWalls()
        {
            GameObject backdrop = Object.Instantiate(Art().Backdrop);
            created.Add(backdrop);
            const float lo = -1, hi = FixedMap.Size + 1;
            float corner = WorldArtSetup.BackdropStrip;
            var sides = new List<Vector2>[4];
            var strip = new List<Vector2>[4];
            for (int s = 0; s < 4; s++) { sides[s] = new List<Vector2>(); strip[s] = new List<Vector2>(); }
            foreach (Renderer renderer in backdrop.GetComponentsInChildren<Renderer>())
            {
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), renderer.name + ": no shadows from the backdrop");
                Bounds b = renderer.bounds;
                if (b.max.y < 5) continue;
                // 0 south, 1 east, 2 north, 3 west.
                if (b.max.z <= lo + .001f) { sides[0].Add(new Vector2(b.min.x, b.max.x)); if (b.max.z >= lo - .1f) strip[0].Add(new Vector2(b.min.x, b.max.x)); }
                if (b.min.x >= hi - .001f) { sides[1].Add(new Vector2(b.min.z, b.max.z)); if (b.min.x <= hi + .1f) strip[1].Add(new Vector2(b.min.z, b.max.z)); }
                if (b.min.z >= hi - .001f) { sides[2].Add(new Vector2(b.min.x, b.max.x)); if (b.min.z <= hi + .1f) strip[2].Add(new Vector2(b.min.x, b.max.x)); }
                if (b.max.x <= lo + .001f) { sides[3].Add(new Vector2(b.min.z, b.max.z)); if (b.max.x >= lo - .1f) strip[3].Add(new Vector2(b.min.z, b.max.z)); }
            }
            string[] names = { "south", "east", "north", "west" };
            for (int s = 0; s < 4; s++)
            {
                bool spansCorners = s % 2 == 0;
                AssertCovered(sides[s], spansCorners ? lo - corner : lo, spansCorners ? hi + corner : hi, names[s] + " skyline");
                AssertCovered(strip[s], lo, hi, names[s] + " strip behind the border wall");
            }
        }

        private static void AssertCovered(List<Vector2> intervals, float lo, float hi, string name)
        {
            Assert.That(intervals, Is.Not.Empty, name);
            intervals.Sort((p, q) => p.x.CompareTo(q.x));
            float cursor = lo;
            foreach (Vector2 interval in intervals)
            {
                Assert.That(interval.x - cursor, Is.LessThanOrEqualTo(.3f), name + ": gap from " + cursor + " to " + interval.x);
                cursor = Mathf.Max(cursor, interval.y);
                if (cursor >= hi) break;
            }
            Assert.That(hi - cursor, Is.LessThanOrEqualTo(.3f), name + ": gap from " + cursor + " to " + hi);
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------

        private static bool InUnion(int[] group, float x, float z)
        {
            foreach (int i in group)
            {
                Wall w = Map.Walls[i];
                if (x >= w.MinX - .001f && x <= w.MaxX + .001f && z >= w.MinZ - .001f && z <= w.MaxZ + .001f) return true;
            }
            return false;
        }

        // The outer boundary of a group's union as segments (x0, z0, x1, z1): every box side minus the parts another box of
        // the group touches. Computed here, independent of WorldArtSetup.
        private static List<Vector4> Outline(int[] group)
        {
            var segments = new List<Vector4>();
            foreach (int i in group)
            {
                Wall w = Map.Walls[i];
                // (alongX, line, lo, hi) of the south, east, north and west sides.
                var sides = new[] { (true, w.MinZ, w.MinX, w.MaxX), (false, w.MaxX, w.MinZ, w.MaxZ), (true, w.MaxZ, w.MinX, w.MaxX), (false, w.MinX, w.MinZ, w.MaxZ) };
                foreach ((bool alongX, float line, float lo, float hi) in sides)
                {
                    var cut = new List<Vector2>();
                    foreach (int j in group)
                    {
                        if (j == i) continue;
                        Wall o = Map.Walls[j];
                        bool touches = alongX ? Mathf.Abs(o.MinZ - line) < .001f || Mathf.Abs(o.MaxZ - line) < .001f : Mathf.Abs(o.MinX - line) < .001f || Mathf.Abs(o.MaxX - line) < .001f;
                        if (!touches) continue;
                        float a = Mathf.Max(lo, alongX ? o.MinX : o.MinZ), b = Mathf.Min(hi, alongX ? o.MaxX : o.MaxZ);
                        if (b - a > .001f) cut.Add(new Vector2(a, b));
                    }
                    cut.Sort((p, q) => p.x.CompareTo(q.x));
                    float cursor = lo;
                    foreach (Vector2 c in cut)
                    {
                        if (c.x > cursor) segments.Add(Segment(alongX, line, cursor, c.x));
                        cursor = Mathf.Max(cursor, c.y);
                    }
                    if (hi > cursor) segments.Add(Segment(alongX, line, cursor, hi));
                }
            }
            return segments;
        }

        private static Vector4 Segment(bool alongX, float line, float lo, float hi) => alongX ? new Vector4(lo, line, hi, line) : new Vector4(line, lo, line, hi);

        private static float Distance(List<Vector4> segments, float x, float z)
        {
            float best = float.MaxValue;
            foreach (Vector4 s in segments)
            {
                float dx = Mathf.Max(s.x - x, 0, x - s.z), dz = Mathf.Max(s.y - z, 0, z - s.w);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        private static void Collect(MeshFilter filter, List<Vector3> triangles)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) return;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Matrix4x4 m = filter.transform.localToWorldMatrix;
            foreach (int index in indices) triangles.Add(m.MultiplyPoint3x4(vertices[index]));
        }

        // Nearest hit distance along a unit direction, either side of a triangle (Moller-Trumbore); infinity on a miss.
        private static float Raycast(List<Vector3> triangles, Vector3 origin, Vector3 direction)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 e1 = triangles[i + 1] - triangles[i], e2 = triangles[i + 2] - triangles[i];
                Vector3 p = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                Vector3 s = origin - triangles[i];
                float u = Vector3.Dot(s, p) / det;
                if (u < -1e-5f || u > 1 + 1e-5f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(direction, q) / det;
                if (v < -1e-5f || u + v > 1 + 1e-5f) continue;
                float t = Vector3.Dot(e2, q) / det;
                if (t > 0 && t < best) best = t;
            }
            return best;
        }
    }
}
