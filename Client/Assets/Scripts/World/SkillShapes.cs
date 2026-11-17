using System.Collections.Generic;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // U9 generated geometry for the skill signatures (warm-up only; everything here allocates). Lines are "cross ribbons":
    // two perpendicular quads per segment, drawn double-sided with the unlit wire material, so a line reads from any angle
    // with a constant world width. No textures, no glow (HUD_SPEC 14.1 World effects).
    public static class SkillShapes
    {
        public const float Width = .06f;

        public static Mesh Wire(string name, List<Vector3[]> lines, float width = Width, bool dashed = false)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Vector3[] line in lines)
                for (int i = 1; i < line.Length; i++)
                {
                    if (dashed && (i & 1) == 0) continue;
                    Segment(line[i - 1], line[i], width * .5f, vertices, triangles);
                }
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Segment(Vector3 a, Vector3 b, float half, List<Vector3> v, List<int> t)
        {
            Vector3 dir = b - a;
            if (dir.sqrMagnitude < 1e-8f) return;
            dir.Normalize();
            Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized;
            Vector3 w = Vector3.Cross(dir, u).normalized;
            Quad(a, b, u * half, v, t);
            Quad(a, b, w * half, v, t);
        }

        private static void Quad(Vector3 a, Vector3 b, Vector3 side, List<Vector3> v, List<int> t)
        {
            int i = v.Count;
            v.Add(a - side); v.Add(a + side); v.Add(b - side); v.Add(b + side);
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i + 2); t.Add(i + 3); t.Add(i + 1);
        }

        public static Vector3[] Circle(float radius, float y, int points)
        {
            var line = new Vector3[points + 1];
            for (int i = 0; i <= points; i++)
            {
                float a = i * Mathf.PI * 2 / points;
                line[i] = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
            }
            return line;
        }

        // Firewall block: a honeycomb wrapped on a cylinder (radius, height) plus its ground ring.
        public static Mesh HexWall(float radius, float height)
        {
            var lines = new List<Vector3[]> { Circle(radius, .03f, 72) };
            int columns = Mathf.Max(6, Mathf.RoundToInt(2 * Mathf.PI * radius / .45f));
            float step = 2 * Mathf.PI * radius / columns;          // 1.5 x hex radius
            float hex = step / 1.5f, rowStep = Mathf.Sqrt(3) * hex;
            int rows = Mathf.Max(1, Mathf.FloorToInt((height - .5f * rowStep) / rowStep));
            for (int c = 0; c < columns; c++)
                for (int r = 0; r < rows; r++)
                {
                    float cu = c * step, cv = rowStep * (r + .5f + ((c & 1) == 1 ? .5f : 0));
                    var hexLine = new Vector3[13];
                    for (int k = 0; k <= 12; k++)
                    {
                        float a = k * Mathf.PI / 6; // 12 points: corners at even k, edge midpoints at odd k (gentle curve)
                        float rad = (k & 1) == 0 ? hex * .92f : hex * .92f * Mathf.Cos(Mathf.PI / 6);
                        float u = cu + Mathf.Cos(a) * rad, vv = cv + Mathf.Sin(a) * rad;
                        float angle = u / radius;
                        hexLine[k] = new Vector3(Mathf.Cos(angle) * radius, vv, Mathf.Sin(angle) * radius);
                    }
                    lines.Add(hexLine);
                }
            return Wire("skill fx hex wall", lines, .045f);
        }

        // Dummy packet: a dashed wire capsule the size of a character (two crossed profiles and three rings).
        public static Mesh WireDouble(float radius, float height)
        {
            var lines = new List<Vector3[]>();
            for (int plane = 0; plane < 2; plane++)
            {
                var profile = new List<Vector3>();
                float bottom = radius, top = height - radius;
                for (int i = 0; i <= 12; i++) { float a = Mathf.PI + i * Mathf.PI / 12; profile.Add(Point(plane, Mathf.Cos(a) * radius, top - Mathf.Sin(a) * radius)); }
                for (int i = 0; i <= 12; i++) { float a = i * Mathf.PI / 12; profile.Add(Point(plane, Mathf.Cos(a) * radius, bottom - Mathf.Sin(a) * radius)); }
                profile.Add(profile[0]);
                lines.Add(Densify(profile.ToArray(), .12f));
            }
            foreach (float y in new[] { .35f, .95f, 1.45f }) lines.Add(Circle(radius, y, 24));
            return Wire("skill fx wire double", lines, .035f, true);
        }

        private static Vector3 Point(int plane, float across, float y) => plane == 0 ? new Vector3(across, y, 0) : new Vector3(0, y, across);

        private static Vector3[] Densify(Vector3[] line, float step)
        {
            var output = new List<Vector3> { line[0] };
            for (int i = 1; i < line.Length; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(line[i - 1], line[i]) / step));
                for (int k = 1; k <= n; k++) output.Add(Vector3.Lerp(line[i - 1], line[i], k / (float)n));
            }
            return output.ToArray();
        }

        // Stealth relay: a wire hemisphere (12 meridians, 3 parallels, the ground ring), so several lines are always in frame
        // from inside it.
        public static Mesh Dome(float radius)
        {
            var lines = new List<Vector3[]> { Circle(radius, .03f, 96) };
            for (int m = 0; m < 12; m++)
            {
                float yaw = m * Mathf.PI * 2 / 12;
                var arc = new Vector3[17];
                for (int i = 0; i <= 16; i++)
                {
                    float a = i * Mathf.PI / 2 / 16;
                    float h = Mathf.Cos(a) * radius;
                    arc[i] = new Vector3(Mathf.Cos(yaw) * h, Mathf.Sin(a) * radius, Mathf.Sin(yaw) * h);
                }
                lines.Add(arc);
            }
            foreach (float lat in new[] { 22.5f, 45f, 67.5f })
                lines.Add(Circle(Mathf.Cos(lat * Mathf.Deg2Rad) * radius, Mathf.Sin(lat * Mathf.Deg2Rad) * radius, 64));
            return Wire("skill fx dome", lines, .07f);
        }

        // Traffic overload: jagged spikes from the caster's chest outwards; `seed` gives the second jitter frame.
        public static Mesh Spikes(float radius, int seed)
        {
            var random = new System.Random(911 + seed);
            var lines = new List<Vector3[]>();
            const int count = 10, kinks = 5;
            for (int s = 0; s < count; s++)
            {
                float yaw = (s + .5f * seed) * Mathf.PI * 2 / count;
                float rise = ((float)random.NextDouble() - .4f) * .6f;
                var dir = new Vector3(Mathf.Cos(yaw), rise, Mathf.Sin(yaw)).normalized;
                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
                var spike = new Vector3[kinks + 1];
                for (int k = 0; k <= kinks; k++)
                {
                    float along = .15f + (radius - .15f) * k / kinks;
                    float off = k == 0 || k == kinks ? 0 : ((k & 1) == 0 ? 1 : -1) * (.08f + (float)random.NextDouble() * .1f);
                    spike[k] = new Vector3(0, .95f, 0) + dir * along + side * off + Vector3.up * off * .5f;
                }
                lines.Add(spike);
            }
            return Wire("skill fx spikes " + seed, lines, .04f);
        }

        // Emergency restore: the beam's six columns (0 .. height) on a small circle.
        public static Mesh BeamColumns(float radius, float height)
        {
            var lines = new List<Vector3[]>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                var foot = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                lines.Add(new[] { foot, foot + Vector3.up * height });
            }
            return Wire("skill fx beam", lines, .05f);
        }

        public static Mesh Ring(string name, float radius, float width = Width) => Wire(name, new List<Vector3[]> { Circle(radius, 0, 32) }, width);

        // White noise: one small static square (outline) in its local XY plane.
        public static Mesh Square(float size)
        {
            float h = size * .5f;
            var line = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(h, h, 0), new Vector3(-h, h, 0), new Vector3(-h, -h, 0) };
            return Wire("skill fx static", new List<Vector3[]> { line }, .035f);
        }

        // Ping scanner (attempt 2, F1): a vertical band on a unit circle from y0 to y1, double-sided. Scaling X/Z sets the
        // radius without changing the band's height, so it stays a readable wall of light in first person.
        public static Mesh Band(float y0, float y1, int segments)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Vector3 pa = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), pb = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                int k = vertices.Count;
                vertices.Add(pa + Vector3.up * y0); vertices.Add(pa + Vector3.up * y1); vertices.Add(pb + Vector3.up * y0); vertices.Add(pb + Vector3.up * y1);
                triangles.Add(k); triangles.Add(k + 1); triangles.Add(k + 2); triangles.Add(k + 2); triangles.Add(k + 1); triangles.Add(k + 3);
            }
            var mesh = new Mesh { name = "skill fx pulse band" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // White noise (attempt 2, F2): a filled unit square as two crossed quads (visible from any side), double-sided.
        public static Mesh FilledCross()
        {
            var mesh = new Mesh { name = "skill fx static" };
            mesh.vertices = new[]
            {
                new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0),
                new Vector3(0, -.5f, -.5f), new Vector3(0, -.5f, .5f), new Vector3(0, .5f, -.5f), new Vector3(0, .5f, .5f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 4, 6, 5, 6, 7, 5 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // Overclock: one streak along +Z, `length` long.
        public static Mesh Streak(float length) => Wire("skill fx streak", new List<Vector3[]> { new[] { Vector3.zero, Vector3.forward * length } }, .025f);

        // The world glyph's quad (1 x 1, centred, facing -Z like the built-in quad).
        public static Mesh GlyphQuad()
        {
            var mesh = new Mesh { name = "skill fx glyph" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();
            return mesh;
        }

        // Deterministic 0..1 hash (White noise blink pattern and streak placement).
        public static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
