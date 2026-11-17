using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 8 skill icons as data (U9): one list of primitives per skill in a 32 u box (origin top-left, y down), drawn
    // by the HUD skill block (SkillRing, Painter2D), the own-cast flash (SkillCastFlash) and rasterized once at warm-up for
    // the world glyph above a skill effect (World/SkillFx). One source, so the HUD and the world always match.
    public static class SkillGlyph
    {
        public enum Kind : byte { Arc, Dot, Polyline }

        public struct Prim
        {
            public Kind Kind;
            public Vector2 Center;
            public float Radius, From, To; // Arc / Dot: degrees, clockwise in y-down space (Painter2D convention)
            public Vector2[] Points;       // Polyline
        }

        private static readonly Vector2[] NoiseDots = { new Vector2(5, 7), new Vector2(20, 5), new Vector2(13, 16), new Vector2(27, 19), new Vector2(8, 27) };
        private static readonly Dictionary<int, Prim[]> Table = Build();

        public static int Key(bool killerSkill, byte skillId) => (killerSkill ? 100 : 0) + skillId;

        // The glyph of a skill, or an empty array for an unknown id.
        public static Prim[] For(bool killerSkill, byte skillId) => Table.TryGetValue(Key(killerSkill, skillId), out Prim[] p) ? p : System.Array.Empty<Prim>();

        private static Prim Arc(float x, float y, float r, float from, float to) => new Prim { Kind = Kind.Arc, Center = new Vector2(x, y), Radius = r, From = from, To = to };
        private static Prim Dot(float x, float y, float r) => new Prim { Kind = Kind.Dot, Center = new Vector2(x, y), Radius = r };
        private static Prim Line(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            return new Prim { Kind = Kind.Polyline, Points = points };
        }

        private static Dictionary<int, Prim[]> Build()
        {
            var t = new Dictionary<int, Prim[]>();
            // Killer (HUD_SPEC 8): three concentric arcs from a point toward a small dot.
            t[Key(true, SkillIds.PingScanner)] = new[] { Arc(6, 26, 7, -80, -10), Arc(6, 26, 13, -80, -10), Arc(6, 26, 19, -80, -10), Dot(6, 26, 1.5f), Dot(27, 5, 2) };
            // Two right chevrons followed by a vertical stop bar.
            t[Key(true, SkillIds.Overclock)] = new[] { Line(5, 8, 12, 16, 5, 24), Line(13, 8, 20, 16, 13, 24), Line(26, 7, 26, 25) };
            // R1 (11/17) Packet trace: a centre dot inside a dashed ring, short rays bursting out of it.
            var trace = new List<Prim> { Dot(16, 16, 2.5f) };
            for (int i = 0; i < 6; i++) trace.Add(Arc(16, 16, 8, i * 60 + 10, i * 60 + 40));
            for (int i = 0; i < 4; i++)
            {
                float a = (45 + 90 * i) * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                trace.Add(Line(16 + 11 * c, 16 + 11 * s, 16 + 15 * c, 16 + 15 * s));
            }
            t[Key(true, SkillIds.PacketTrace)] = trace.ToArray();
            // A ring with a horizontal bar through it.
            t[Key(true, SkillIds.FirewallBlock)] = new[] { Arc(16, 16, 10, 0, 360), Line(2, 16, 30, 16) };
            // Survivor: a dashed-outline dot.
            var dashed = new Prim[8];
            for (int i = 0; i < 8; i++) dashed[i] = Arc(16, 16, 9, i * 45, i * 45 + 25);
            t[Key(false, SkillIds.DummyPacket)] = dashed;
            // A dot inside a circle, a wave line crossed out.
            t[Key(false, SkillIds.StealthRelay)] = new[] { Arc(10, 16, 7, 0, 360), Dot(10, 16, 2), Line(20, 16, 23, 12, 26, 20, 29, 16), Line(20, 23, 30, 9) };
            // A dot emitting two arcs, a down chevron beside it.
            t[Key(false, SkillIds.TrafficOverload)] = new[] { Dot(6, 16, 2), Arc(6, 16, 6, -45, 45), Arc(6, 16, 11, -45, 45), Line(22, 12, 26, 18, 30, 12) };
            // Two dots joined by a solid line.
            t[Key(false, SkillIds.EmergencyRestore)] = new[] { Dot(6, 16, 3), Dot(26, 16, 3), Line(9, 16, 23, 16) };
            // Five scattered dots.
            var noise = new Prim[NoiseDots.Length];
            for (int i = 0; i < noise.Length; i++) noise[i] = Dot(NoiseDots[i].x, NoiseDots[i].y, 2);
            t[Key(false, SkillIds.WhiteNoise)] = noise;
            return t;
        }

        // Draws the glyph with its 32 u box at `origin`, scaled by `size / 32`, in `color` with `stroke` u lines.
        public static void Paint(Painter2D p, Prim[] glyph, Vector2 origin, float size, Color color, float stroke)
        {
            float k = size / 32f;
            p.strokeColor = color; p.fillColor = color; p.lineWidth = stroke;
            p.lineCap = LineCap.Butt; p.lineJoin = LineJoin.Miter;
            foreach (Prim prim in glyph)
            {
                switch (prim.Kind)
                {
                    case Kind.Arc:
                        p.BeginPath(); p.Arc(origin + prim.Center * k, prim.Radius * k, Angle.Degrees(prim.From), Angle.Degrees(prim.To)); p.Stroke();
                        break;
                    case Kind.Dot:
                        p.BeginPath(); p.Arc(origin + prim.Center * k, prim.Radius * k, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                        break;
                    case Kind.Polyline:
                        p.BeginPath(); p.MoveTo(origin + prim.Points[0] * k);
                        for (int i = 1; i < prim.Points.Length; i++) p.LineTo(origin + prim.Points[i] * k);
                        p.Stroke();
                        break;
                }
            }
        }

        // Coverage mask (0..1, row 0 = bottom, as Texture2D) of the glyph at `size` px with `stroke` u lines (world glyph).
        // Warm-up only: it allocates.
        public static float[] Rasterize(Prim[] glyph, int size, float stroke)
        {
            var mask = new float[size * size];
            float k = size / 32f, half = stroke * k * .5f;
            var segments = new List<Vector4>();
            var dots = new List<Vector3>();
            foreach (Prim prim in glyph)
            {
                switch (prim.Kind)
                {
                    case Kind.Dot: dots.Add(new Vector3(prim.Center.x * k, prim.Center.y * k, prim.Radius * k)); break;
                    case Kind.Polyline:
                        for (int i = 1; i < prim.Points.Length; i++)
                            segments.Add(new Vector4(prim.Points[i - 1].x * k, prim.Points[i - 1].y * k, prim.Points[i].x * k, prim.Points[i].y * k));
                        break;
                    case Kind.Arc:
                        int steps = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(prim.To - prim.From) / 6f));
                        Vector2 last = Polar(prim, prim.From) * 1;
                        for (int i = 1; i <= steps; i++)
                        {
                            Vector2 next = Polar(prim, prim.From + (prim.To - prim.From) * i / steps);
                            segments.Add(new Vector4(last.x * k, last.y * k, next.x * k, next.y * k));
                            last = next;
                        }
                        break;
                }
            }
            // Each primitive only touches the pixels of its own bounding box (warm-up cost).
            foreach (Vector4 s in segments)
            {
                float reach = half + 1;
                Stamp(mask, size, Mathf.Min(s.x, s.z) - reach, Mathf.Min(s.y, s.w) - reach, Mathf.Max(s.x, s.z) + reach, Mathf.Max(s.y, s.w) + reach,
                    q => Mathf.Clamp01(half + .5f - Distance(q, new Vector2(s.x, s.y), new Vector2(s.z, s.w))));
            }
            foreach (Vector3 d in dots)
                Stamp(mask, size, d.x - d.z - 1, d.y - d.z - 1, d.x + d.z + 1, d.y + d.z + 1, q => Mathf.Clamp01(d.z + .5f - Vector2.Distance(q, new Vector2(d.x, d.y))));
            return mask;
        }

        private static void Stamp(float[] mask, int size, float x0, float y0, float x1, float y1, System.Func<Vector2, float> cover)
        {
            int ax = Mathf.Max(0, Mathf.FloorToInt(x0)), ay = Mathf.Max(0, Mathf.FloorToInt(y0));
            int bx = Mathf.Min(size - 1, Mathf.CeilToInt(x1)), by = Mathf.Min(size - 1, Mathf.CeilToInt(y1));
            for (int y = ay; y <= by; y++)
                for (int x = ax; x <= bx; x++)
                {
                    int i = (size - 1 - y) * size + x; // y-down glyph space -> Texture2D rows (row 0 = bottom)
                    float c = cover(new Vector2(x + .5f, y + .5f));
                    if (c > mask[i]) mask[i] = c;
                }
        }

        private static Vector2 Polar(in Prim prim, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return prim.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * prim.Radius;
        }

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + ab * t);
        }
    }

    // The glyph as a UITK element (the own-cast flash). Size = the element's layout size.
    public sealed class SkillGlyphElement : VisualElement
    {
        private SkillGlyph.Prim[] glyph = System.Array.Empty<SkillGlyph.Prim>();
        private Color color = new Color32(232, 230, 225, 255);
        public float Scale { get; set; } = 1;

        public SkillGlyphElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += context =>
            {
                float stroke = Mathf.Max(1, Mathf.Round(Scale)) / Scale; // HUD_SPEC 0.2 stroke snapping, 1 u lines
                SkillGlyph.Paint(context.painter2D, glyph, Vector2.zero, resolvedStyle.width > 0 ? resolvedStyle.width : 32, color, stroke);
            };
        }

        public void Set(bool killerSkill, byte skillId, Color tint)
        {
            glyph = SkillGlyph.For(killerSkill, skillId); color = tint;
            MarkDirtyRepaint();
        }
    }
}
