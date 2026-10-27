using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // HUD_SPEC 6.2 edge-indicator placement, steps 1-6 (also used by 4.6). Pure. Screen positions are in panel units with
    // the origin top-left and y pointing down; camera space is x right, y up, z forward.
    public static class EdgePlacement
    {
        public const float Inset = 24, BandInset = 56, KeepOutMargin = 8;

        // Step 2: screen direction from the camera-space point. Works behind the camera too (x and y keep their sign);
        // straight behind (x = y = 0) points at the bottom edge. Returned y-up.
        public static Vector2 Direction(Vector3 cameraSpace)
        {
            var d = new Vector2(cameraSpace.x, cameraSpace.y);
            return d.sqrMagnitude < 1e-12f ? new Vector2(0, -1) : d.normalized;
        }

        // Step 3: the screen inset 24 u, with the top at 56 u while the connection band shows.
        public static Rect SafeRect(Vector2 screen, bool connectionBand)
        {
            float top = connectionBand ? BandInset : Inset;
            return Rect.MinMaxRect(Inset, top, Mathf.Max(Inset, screen.x - Inset), Mathf.Max(top, screen.y - Inset));
        }

        // Step 5: the HUD cluster expanded by 8 u.
        public static Rect KeepOut(Rect cluster)
            => Rect.MinMaxRect(cluster.xMin - KeepOutMargin, cluster.yMin - KeepOutMargin, cluster.xMax + KeepOutMargin, cluster.yMax + KeepOutMargin);

        // Steps 4-5. directionUp: step 2's direction (y up). nearestSlide: on the bottom edge slide to whichever side of the
        // keep-out is nearer (Survivor); otherwise slide away from the screen corner (Killer: right on the bottom edge).
        // pointing: the direction the chevron points, y down.
        public static Vector2 Place(Vector2 screen, Vector2 directionUp, Rect safe, Rect keepOut, bool nearestSlide, out Vector2 pointing)
        {
            Vector2 center = screen * .5f;
            pointing = new Vector2(directionUp.x, -directionUp.y);
            if (pointing.sqrMagnitude < 1e-12f) pointing = new Vector2(0, 1);
            pointing.Normalize();
            float t = float.PositiveInfinity;
            if (pointing.x > 1e-6f) t = Mathf.Min(t, (safe.xMax - center.x) / pointing.x);
            else if (pointing.x < -1e-6f) t = Mathf.Min(t, (safe.xMin - center.x) / pointing.x);
            if (pointing.y > 1e-6f) t = Mathf.Min(t, (safe.yMax - center.y) / pointing.y);
            else if (pointing.y < -1e-6f) t = Mathf.Min(t, (safe.yMin - center.y) / pointing.y);
            if (float.IsInfinity(t) || t < 0) t = 0;
            Vector2 p = center + pointing * t;
            p.x = Mathf.Clamp(p.x, safe.xMin, safe.xMax);
            p.y = Mathf.Clamp(p.y, safe.yMin, safe.yMax);
            if (!Inside(keepOut, p)) return p;
            const float edge = .01f;
            bool vertical = Mathf.Abs(p.x - safe.xMin) < edge || Mathf.Abs(p.x - safe.xMax) < edge;
            bool horizontal = Mathf.Abs(p.y - safe.yMin) < edge || Mathf.Abs(p.y - safe.yMax) < edge;
            if (horizontal && (!vertical || Mathf.Abs(p.y - safe.yMax) < edge))
            {
                // On the bottom (or top) edge: slide along x.
                bool right = nearestSlide ? keepOut.xMax - p.x <= p.x - keepOut.xMin : keepOut.center.x < center.x;
                p.x = Mathf.Clamp(right ? keepOut.xMax : keepOut.xMin, safe.xMin, safe.xMax);
                if (Inside(keepOut, p)) p.y = Mathf.Clamp(keepOut.yMin, safe.yMin, safe.yMax);
            }
            else
            {
                // On the left or right edge: slide along y, away from the nearer screen corner.
                bool up = keepOut.center.y > center.y;
                p.y = Mathf.Clamp(up ? keepOut.yMin : keepOut.yMax, safe.yMin, safe.yMax);
                if (Inside(keepOut, p)) p.x = Mathf.Clamp(keepOut.xMax, safe.xMin, safe.xMax);
            }
            return p;
        }

        // Step 6: an on-screen marker inside the keep-out moves to the nearest keep-out edge whose outside stays in the
        // safe rectangle.
        public static Vector2 ClampOutside(Vector2 point, Rect keepOut, Rect safe)
        {
            if (!Inside(keepOut, point)) return point;
            Vector2 best = point; float bestDistance = float.PositiveInfinity;
            Consider(new Vector2(point.x, keepOut.yMin), point, safe, ref best, ref bestDistance);
            Consider(new Vector2(point.x, keepOut.yMax), point, safe, ref best, ref bestDistance);
            Consider(new Vector2(keepOut.xMin, point.y), point, safe, ref best, ref bestDistance);
            Consider(new Vector2(keepOut.xMax, point.y), point, safe, ref best, ref bestDistance);
            return best;
        }

        // Where a tag sits: on the screen-centre side of its chevron.
        public static Vector2 TagAnchor(Vector2 chevron, Vector2 screen, float offset)
        {
            Vector2 toCenter = screen * .5f - chevron;
            return toCenter.sqrMagnitude < 1e-6f ? chevron : chevron + toCenter.normalized * offset;
        }

        private static void Consider(Vector2 candidate, Vector2 point, Rect safe, ref Vector2 best, ref float bestDistance)
        {
            if (candidate.x < safe.xMin || candidate.x > safe.xMax || candidate.y < safe.yMin || candidate.y > safe.yMax) return;
            float distance = (candidate - point).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = candidate; }
        }

        // Strictly inside: a point on the keep-out edge is already clear.
        private static bool Inside(Rect r, Vector2 p) => p.x > r.xMin && p.x < r.xMax && p.y > r.yMin && p.y < r.yMax;
    }
}
