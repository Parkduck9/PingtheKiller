using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PingTheKiller.Editor.Setup
{
    // U4l: finds the places where the city art does not fill its FixedMap box at close range, at eye level. The collision is
    // the box, so every such place is both a see-through gap and an invisible wall. The scan places the catalog as WorldProps
    // does, gives every art renderer a temporary MeshCollider, and casts horizontal rays from eye points beside every wall and
    // obstacle face into the boxes:
    //   eye points: 1 m and 2 m out from each face, every 1 m along it, where a player could stand;
    //   rays: yaw -60..+60 degrees around the inward normal in 5 degree steps, at heights 0.3, 1.0, 1.7 and 2.6 m.
    // A see-through hit is a ray that enters a wall box (below MapGeometry.WallHeight) or an obstacle box (below
    // MapGeometry.ObstacleHeight) and then travels more than InsideLimit inside it, or leaves it, without hitting art. Rays that
    // only graze a box corner (less than MinChord inside it) or meet the entered face at more than 60 degrees from its normal
    // (the eye fan's own limit) are ignored. It also measures how well each obstacle prop and each
    // podium fills its box, looks through every building group above the podium, and looks over the border walls for sky
    // between the skyline pieces.
    //   Unity.exe -batchmode -projectPath Client -executeMethod PingTheKiller.Editor.Setup.WorldArtGapScan.Run
    //             -ptk-gaps-label before|after [-ptk-gaps-views <views.txt of an earlier run>] -logFile <log>
    // writes docs/qa/U4l-gaps-<label>.md / .json and the shots to docs/qa/U4l-screens/<label>/ (run without -nographics).
    public static class WorldArtGapScan
    {
        public const float InsideLimit = .25f, MinChord = .05f, Eye = 1.7f, Fov = 50;
        // Sky gaps count up to this pitch above the horizon (eye level); misses between roof tops higher up are notches.
        public const float EyeLevelPitch = 12;
        public static readonly float MaxGrazing = Mathf.Cos(60.5f * Mathf.Deg2Rad);
        public static readonly float[] RayHeights = { .3f, 1f, 1.7f, 2.6f };
        private static readonly string[] SideNames = { "S", "E", "N", "W" };

        [Serializable]
        public sealed class Hit
        {
            public string Kind; // "wall" or "obstacle"
            public int Box, Face;
            public float Height, Along, Inside;
            public bool Exited;
            public Vector3 Eye, Entry;
        }

        [Serializable]
        public sealed class Cluster
        {
            public string Kind;
            public int Box;
            public string Face;
            public float Height, From, To, Width, WorstInside;
            public int Rays;
            public Vector3 Eye, Target;
            public string Shot;
        }

        [Serializable]
        public sealed class Fill
        {
            public string Kind;
            public int Box;
            public float X, Z, Y, Over, Top; // fill ratio per axis (Y against the box height), largest overshoot, art top
        }

        [Serializable]
        public sealed class Report
        {
            public string Label;
            public int EyePoints, Rays, SeeThrough, UpperRays, UpperGaps, SkyRays, SkyGaps, SkyNotches;
            public float MinObstacleFill, MinPodiumFill, MaxObstacleOver;
            public long WallTriangles, BackdropTriangles, ObstacleTriangles;
            public int WallRenderers, BackdropRenderers, ObstacleRenderers;
            public List<Cluster> Clusters = new List<Cluster>();
            public List<Fill> Obstacles = new List<Fill>();
            public List<Fill> Podiums = new List<Fill>();
            public List<string> Upper = new List<string>();
            public List<string> Sky = new List<string>();
            [NonSerialized] public List<Hit> Hits = new List<Hit>();
        }

        // Scan density: the batch run uses the brief's full density, the EditMode test a reduced one.
        public sealed class Density
        {
            public float Step = 1, YawStep = 5, YawMax = 60;
            public float[] Distances = { 1, 2 };
            public float[] Heights = RayHeights;
            public float UpperStep = .5f, SkyStep = 4;
            public static Density Full => new Density();
            public static Density Reduced => new Density { Step = 2, YawStep = 15, Distances = new[] { 1.5f }, UpperStep = 1.5f, SkyStep = 12 };
        }

        private struct BoxRef
        {
            public string Kind;
            public int Index;
            public float MinX, MinZ, MaxX, MaxZ, Height;
        }

        private static List<BoxRef> Boxes(FixedMap map)
        {
            var boxes = new List<BoxRef>();
            for (int i = 0; i < map.Walls.Count; i++)
            {
                Wall w = map.Walls[i];
                boxes.Add(new BoxRef { Kind = "wall", Index = i, MinX = w.MinX, MinZ = w.MinZ, MaxX = w.MaxX, MaxZ = w.MaxZ, Height = MapGeometry.WallHeight });
            }
            for (int i = 0; i < map.Obstacles.Count; i++)
            {
                Obstacle o = map.Obstacles[i];
                boxes.Add(new BoxRef { Kind = "obstacle", Index = i, MinX = o.MinX, MinZ = o.MinZ, MaxX = o.MaxX, MaxZ = o.MaxZ, Height = MapGeometry.ObstacleHeight });
            }
            return boxes;
        }

        // ---- the city with colliders ---------------------------------------------------------------------------------------

        // The city as WorldProps places it, under one root, with a MeshCollider on every art mesh (the ground excluded).
        public static GameObject BuildCity(WorldArt art, bool backdrop, out HashSet<Collider> colliders)
        {
            FixedMap map = SharedMap.Map;
            var root = new GameObject("U4l gap scan city");
            colliders = new HashSet<Collider>();
            var placed = new List<(GameObject, Vector3, bool)> { (art.Ground, Vector3.zero, false) };
            if (backdrop) placed.Add((art.Backdrop, Vector3.zero, true));
            for (int i = 0; i < map.Walls.Count && i < art.Walls.Length; i++)
            {
                SharedMap.WallBox(map.Walls[i], out Vector3 center, out _);
                placed.Add((art.Walls[i].Prefab, center, true));
            }
            for (int i = 0; i < map.Obstacles.Count && i < art.Obstacles.Length; i++)
            {
                Obstacle o = map.Obstacles[i];
                placed.Add((art.Obstacles[i].Prefab, new Vector3((o.MinX + o.MaxX) * .5f, 0, (o.MinZ + o.MaxZ) * .5f), true));
            }
            foreach ((GameObject prefab, Vector3 position, bool solid) in placed)
            {
                if (prefab == null) continue;
                var item = (GameObject)Object.Instantiate(prefab, root.transform);
                item.name = prefab.name;
                item.transform.localPosition = position;
                if (!solid) continue;
                foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.sharedMesh.vertexCount < 3) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    colliders.Add(collider);
                }
            }
            Physics.SyncTransforms();
            return root;
        }

        private static readonly RaycastHit[] buffer = new RaycastHit[256];

        // Distance to the nearest art collider along the ray, or infinity.
        private static float Cast(Vector3 origin, Vector3 direction, float range, HashSet<Collider> colliders)
        {
            int n = Physics.RaycastNonAlloc(origin, direction, buffer, range, ~0, QueryTriggerInteraction.Collide);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
                if (colliders.Contains(buffer[i].collider) && buffer[i].distance < best) best = buffer[i].distance;
            return best;
        }

        // Entry and exit distances of a horizontal ray (2D) through a box; false when it misses.
        private static bool Slab(float ox, float oz, float dx, float dz, float minX, float minZ, float maxX, float maxZ, out float enter, out float exit)
        {
            enter = 0; exit = float.PositiveInfinity;
            return Axis(ox, dx, minX, maxX, ref enter, ref exit) && Axis(oz, dz, minZ, maxZ, ref enter, ref exit);
        }

        private static bool Axis(float o, float d, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(d) < 1e-7f) return o > min && o < max;
            float t1 = (min - o) / d, t2 = (max - o) / d;
            if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
            enter = Mathf.Max(enter, t1);
            exit = Mathf.Min(exit, t2);
            return enter < exit;
        }

        // ---- the scan ------------------------------------------------------------------------------------------------------

        public static Report Scan(WorldArt art, Density density, bool sky)
        {
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            GameObject city = BuildCity(art, sky, out HashSet<Collider> colliders);
            try
            {
                var report = new Report();
                FixedMap map = SharedMap.Map;
                SeeThrough(map, density, colliders, report);
                Clusters(report);
                Fills(map, city, report);
                Uppers(map, density, colliders, report);
                if (sky) Sky(map, density, colliders, report);
                return report;
            }
            finally
            {
                Object.DestroyImmediate(city);
                Physics.queriesHitBackfaces = backfaces;
                Physics.SyncTransforms();
            }
        }

        private static IEnumerable<(Vector3 origin, Vector3 right, Vector3 outward, float length, int side)> Faces(BoxRef b)
        {
            yield return (new Vector3(b.MinX, 0, b.MinZ), Vector3.right, Vector3.back, b.MaxX - b.MinX, 0);
            yield return (new Vector3(b.MaxX, 0, b.MinZ), Vector3.forward, Vector3.right, b.MaxZ - b.MinZ, 1);
            yield return (new Vector3(b.MaxX, 0, b.MaxZ), Vector3.left, Vector3.forward, b.MaxX - b.MinX, 2);
            yield return (new Vector3(b.MinX, 0, b.MaxZ), Vector3.back, Vector3.left, b.MaxZ - b.MinZ, 3);
        }

        private static void SeeThrough(FixedMap map, Density density, HashSet<Collider> colliders, Report report)
        {
            List<BoxRef> boxes = Boxes(map);
            foreach (BoxRef box in boxes)
                foreach (var face in Faces(box))
                    foreach (float distance in density.Distances)
                        for (float u = .5f; u < face.length; u += density.Step)
                        {
                            Vector3 eye = face.origin + face.right * u + face.outward * distance;
                            if (!map.CanStand(eye.x, eye.z, .2f)) continue;
                            report.EyePoints++;
                            for (float yaw = -density.YawMax; yaw <= density.YawMax + .01f; yaw += density.YawStep)
                            {
                                Vector3 direction = Quaternion.Euler(0, yaw, 0) * -face.outward;
                                foreach (float height in density.Heights)
                                {
                                    report.Rays++;
                                    Hit hit = Ray(boxes, new Vector3(eye.x, height, eye.z), direction, colliders);
                                    if (hit == null) continue;
                                    hit.Eye = new Vector3(eye.x, Eye, eye.z);
                                    report.Hits.Add(hit);
                                }
                            }
                        }
            report.SeeThrough = report.Hits.Count;
        }

        // The first box the ray enters (within 30 m, among the boxes taller than the ray), and whether art stops it in time.
        private static Hit Ray(List<BoxRef> boxes, Vector3 origin, Vector3 direction, HashSet<Collider> colliders)
        {
            float bestEnter = 30, bestExit = 0;
            int best = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                BoxRef b = boxes[i];
                if (origin.y >= b.Height) continue;
                if (!Slab(origin.x, origin.z, direction.x, direction.z, b.MinX, b.MinZ, b.MaxX, b.MaxZ, out float enter, out float exit)) continue;
                if (enter < 0 || enter >= bestEnter) continue;
                bestEnter = enter; bestExit = exit; best = i;
            }
            if (best < 0 || bestExit - bestEnter < MinChord) return null;
            // The same incidence limit as the eye fan: a ray meeting the entered face at more than 60 degrees from its normal
            // only skims it (into a 4 cm window recess it then "travels" half a metre), so it is not counted.
            BoxRef entered = boxes[best];
            int face = SideOf(entered, origin + direction * bestEnter);
            float incidence = Mathf.Abs(face % 2 == 0 ? direction.z : direction.x);
            if (incidence < MaxGrazing) return null;
            float art = Cast(origin, direction, bestExit + 1, colliders);
            if (art <= bestEnter + InsideLimit) return null;
            BoxRef box = boxes[best];
            Vector3 entry = origin + direction * bestEnter;
            int side = SideOf(box, entry);
            return new Hit
            {
                Kind = box.Kind, Box = box.Index, Face = side, Height = origin.y, Entry = entry,
                Along = side % 2 == 0 ? entry.x : entry.z,
                Inside = Mathf.Min(art, bestExit) - bestEnter, Exited = art > bestExit
            };
        }

        private static int SideOf(BoxRef b, Vector3 p)
        {
            float s = Mathf.Abs(p.z - b.MinZ), e = Mathf.Abs(p.x - b.MaxX), n = Mathf.Abs(p.z - b.MaxZ), w = Mathf.Abs(p.x - b.MinX);
            float m = Mathf.Min(Mathf.Min(s, e), Mathf.Min(n, w));
            return m == s ? 0 : m == e ? 1 : m == n ? 2 : 3;
        }

        // Hits on one face, in one height band, whose entry points lie within 1 m of each other along the face.
        private static void Clusters(Report report)
        {
            var groups = new Dictionary<string, List<Hit>>();
            foreach (Hit hit in report.Hits)
            {
                string key = hit.Kind + "|" + hit.Box + "|" + hit.Face + "|" + hit.Height.ToString("0.0", CultureInfo.InvariantCulture);
                if (!groups.TryGetValue(key, out List<Hit> list)) groups[key] = list = new List<Hit>();
                list.Add(hit);
            }
            foreach (List<Hit> list in groups.Values)
            {
                list.Sort((a, b) => a.Along.CompareTo(b.Along));
                int start = 0;
                for (int i = 1; i <= list.Count; i++)
                {
                    if (i < list.Count && list[i].Along - list[i - 1].Along <= 1f) continue;
                    report.Clusters.Add(MakeCluster(list, start, i));
                    start = i;
                }
            }
            report.Clusters.Sort((a, b) => b.Rays != a.Rays ? b.Rays.CompareTo(a.Rays) : string.CompareOrdinal(a.Kind + a.Box + a.Face, b.Kind + b.Box + b.Face));
        }

        private static Cluster MakeCluster(List<Hit> list, int start, int end)
        {
            Hit worst = list[start];
            Vector3 target = Vector3.zero;
            for (int i = start; i < end; i++)
            {
                if (list[i].Inside > worst.Inside) worst = list[i];
                target += list[i].Entry;
            }
            target /= end - start;
            Hit first = list[start];
            return new Cluster
            {
                Kind = first.Kind, Box = first.Box, Face = SideNames[first.Face], Height = first.Height,
                From = list[start].Along, To = list[end - 1].Along, Width = list[end - 1].Along - list[start].Along,
                Rays = end - start, WorstInside = worst.Inside, Eye = worst.Eye, Target = new Vector3(target.x, first.Height, target.z)
            };
        }

        // ---- fill ratios ---------------------------------------------------------------------------------------------------

        private static void Fills(FixedMap map, GameObject city, Report report)
        {
            report.MinObstacleFill = report.MinPodiumFill = 1;
            var items = new List<Transform>();
            foreach (Transform t in city.transform) items.Add(t);
            int child = items.Count - map.Walls.Count - map.Obstacles.Count; // after the ground (and the backdrop)
            for (int i = 0; i < map.Walls.Count; i++)
            {
                Wall w = map.Walls[i];
                Transform podium = items[child + i].Find("Podium");
                if (podium == null) { report.MinPodiumFill = 0; continue; }
                Fill fill = Measure("wall", i, podium.GetComponent<Renderer>().bounds, w.MinX, w.MinZ, w.MaxX, w.MaxZ, MapGeometry.WallHeight);
                report.Podiums.Add(fill);
                report.MinPodiumFill = Mathf.Min(report.MinPodiumFill, Mathf.Min(fill.X, Mathf.Min(fill.Z, fill.Y)));
            }
            child += map.Walls.Count;
            for (int i = 0; i < map.Obstacles.Count; i++)
            {
                Obstacle o = map.Obstacles[i];
                Bounds b = new Bounds();
                bool any = false;
                foreach (Renderer r in items[child + i].GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                Fill fill = Measure("obstacle", i, b, o.MinX, o.MinZ, o.MaxX, o.MaxZ, MapGeometry.ObstacleHeight);
                report.Obstacles.Add(fill);
                report.MinObstacleFill = Mathf.Min(report.MinObstacleFill, Mathf.Min(fill.X, Mathf.Min(fill.Z, fill.Y)));
                report.MaxObstacleOver = Mathf.Max(report.MaxObstacleOver, fill.Over);
            }
        }

        private static Fill Measure(string kind, int index, Bounds b, float minX, float minZ, float maxX, float maxZ, float height)
        {
            float fx = (Mathf.Min(b.max.x, maxX) - Mathf.Max(b.min.x, minX)) / (maxX - minX);
            float fz = (Mathf.Min(b.max.z, maxZ) - Mathf.Max(b.min.z, minZ)) / (maxZ - minZ);
            float fy = (Mathf.Min(b.max.y, height) - Mathf.Max(b.min.y, 0)) / height;
            float over = Mathf.Max(Mathf.Max(minX - b.min.x, b.max.x - maxX), Mathf.Max(minZ - b.min.z, b.max.z - maxZ));
            return new Fill { Kind = kind, Box = index, X = fx, Z = fz, Y = fy, Over = Mathf.Max(0, over), Top = b.max.y };
        }

        // ---- upper storeys -------------------------------------------------------------------------------------------------

        // Above the podium: rays straight into every outer face of every building group, at 4 m and 5.5 m, wherever the upper
        // part stands behind the face (the face point moved in by Setback + 0.1 m lies in the inset group outline). Each ray
        // must hit art before it leaves the group's union: otherwise the building has a through-gap.
        private static void Uppers(FixedMap map, Density density, HashSet<Collider> colliders, Report report)
        {
            foreach (int[] group in WorldArtSetup.Groups(map))
            {
                var areas = new List<WorldArtSetup.Area>();
                foreach (int i in group) areas.Add(WorldArtSetup.Area.Of(map.Walls[i]));
                foreach (int i in group)
                {
                    Wall w = map.Walls[i];
                    var box = new BoxRef { Kind = "wall", Index = i, MinX = w.MinX, MinZ = w.MinZ, MaxX = w.MaxX, MaxZ = w.MaxZ, Height = 11 };
                    foreach (var face in Faces(box))
                        for (float u = density.UpperStep * .5f; u < face.length; u += density.UpperStep)
                        {
                            Vector3 onFace = face.origin + face.right * u;
                            if (Inside(areas, onFace + face.outward * .05f)) continue; // a shared edge, not an outer face
                            Vector3 probe = onFace - face.outward * (WorldArtSetup.Setback + .1f);
                            if (!InsideInset(areas, probe, WorldArtSetup.Setback)) continue;
                            foreach (float y in new[] { 4f, 5.5f })
                            {
                                report.UpperRays++;
                                Vector3 from = onFace + face.outward * .5f + Vector3.up * y;
                                float art = Cast(from, -face.outward, 40, colliders);
                                float through = ExitDistance(areas, from, -face.outward);
                                if (art <= through) continue;
                                report.UpperGaps++;
                                report.Upper.Add($"wall {i} face {SideNames[face.side]} at ({onFace.x:0.0}, {y:0.0}, {onFace.z:0.0}): art at {(float.IsInfinity(art) ? "none" : (art - .5f).ToString("0.00", CultureInfo.InvariantCulture))} m, group exit at {through - .5f:0.00} m");
                            }
                        }
                }
            }
        }

        private static bool Inside(List<WorldArtSetup.Area> areas, Vector3 p)
        {
            foreach (WorldArtSetup.Area a in areas) if (p.x > a.MinX && p.x < a.MaxX && p.z > a.MinZ && p.z < a.MaxZ) return true;
            return false;
        }

        // p lies at least d inside the union (all eight neighbours at distance d are inside it).
        private static bool InsideInset(List<WorldArtSetup.Area> areas, Vector3 p, float d)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (!Inside(areas, p + new Vector3(dx * d * .999f, 0, dz * d * .999f))) return false;
            return true;
        }

        private static float ExitDistance(List<WorldArtSetup.Area> areas, Vector3 from, Vector3 direction)
        {
            float t = 0;
            bool entered = false;
            for (; t < 60; t += .05f)
            {
                bool inside = Inside(areas, from + direction * t);
                if (inside) entered = true;
                else if (entered) return t;
            }
            return t;
        }

        // ---- sky over the border walls -------------------------------------------------------------------------------------

        // From eye points 4, 10 and 20 m in from each border wall, rays toward it (yaw -60..+60, pitch 0..20 degrees in 1
        // degree steps). In each yaw column, a ray that clears the border wall and misses all art while a higher ray of the
        // same column hits skyline art (beyond the border walls) is sky seen through a gap between the skyline pieces when it
        // is at most EyeLevelPitch above the horizon; higher up it is a notch in the roofline, listed but not a gap.
        private static void Sky(FixedMap map, Density density, HashSet<Collider> colliders, Report report)
        {
            for (int i = 0; i < map.Walls.Count; i++)
            {
                if (!WorldArtSetup.IsBorder(map.Walls[i])) continue;
                Wall w = map.Walls[i];
                var box = new BoxRef { Kind = "wall", Index = i, MinX = w.MinX, MinZ = w.MinZ, MaxX = w.MaxX, MaxZ = w.MaxZ, Height = 3 };
                foreach (var face in Faces(box))
                {
                    Vector3 mid = face.origin + face.right * face.length * .5f + face.outward;
                    if (mid.x < 0 || mid.z < 0 || mid.x > FixedMap.Size || mid.z > FixedMap.Size) continue; // the outer face
                    for (float u = 2; u < face.length - 2; u += density.SkyStep)
                        foreach (float distance in new[] { 4f, 10f, 20f })
                        {
                            Vector3 eye = face.origin + face.right * u + face.outward * distance + Vector3.up * Eye;
                            if (!map.CanStand(eye.x, eye.z, .2f)) continue;
                            for (float yaw = -density.YawMax; yaw <= density.YawMax + .01f; yaw += density.YawStep * 2)
                            {
                                Vector3 flat = Quaternion.Euler(0, yaw, 0) * -face.outward;
                                bool hitAbove = false;
                                float missPitch = -1, notch = -1;
                                for (float pitch = 20; pitch >= 0; pitch -= 1)
                                {
                                    Vector3 direction = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
                                    report.SkyRays++;
                                    float distance2 = Cast(eye, direction, 200, colliders);
                                    if (!float.IsInfinity(distance2))
                                    {
                                        // Only skyline art (beyond the border walls) counts as "above"; an interior building in
                                        // front blocks the column without saying anything about the skyline.
                                        Vector3 p = eye + direction * distance2;
                                        if (p.x < -1 || p.z < -1 || p.x > FixedMap.Size + 1 || p.z > FixedMap.Size + 1) hitAbove = true;
                                        continue;
                                    }
                                    if (!hitAbove) continue;
                                    if (pitch > EyeLevelPitch) { if (notch < 0) notch = pitch; continue; }
                                    missPitch = pitch;
                                    break;
                                }
                                if (missPitch < 0 && notch < 0) continue;
                                if (missPitch < 0) { report.SkyNotches++; report.Sky.Add($"(notch above {EyeLevelPitch:0} deg) border wall {i}: eye ({eye.x:0.0}, {eye.z:0.0}) yaw {yaw:0} pitch {notch:0}"); continue; }
                                report.SkyGaps++;
                                report.Sky.Add($"border wall {i}: eye ({eye.x:0.0}, {eye.z:0.0}) yaw {yaw:0} pitch {missPitch:0}");
                            }
                        }
                }
            }
        }

        // ---- batch run, report and shots -----------------------------------------------------------------------------------

        public static void Run()
        {
            try
            {
                string label = Argument("-ptk-gaps-label") ?? "before";
                string docs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "qa"));
                string shots = Path.Combine(docs, "U4l-screens", label);
                Directory.CreateDirectory(shots);
                WorldArt art = AssetDatabase.LoadAssetAtPath<WorldArt>("Assets/Resources/WorldArt.asset");
                if (art == null) throw new InvalidOperationException("Assets/Resources/WorldArt.asset is missing");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Report report = Scan(art, Density.Full, true);
                report.Label = label;
                WorldArtCapture.Count(art, out report.WallTriangles, out report.WallRenderers, out report.BackdropTriangles, out report.BackdropRenderers);
                foreach (WorldArt.Piece piece in art.Obstacles) CountPrefab(piece.Prefab, ref report.ObstacleTriangles, ref report.ObstacleRenderers);
                var views = new List<(string name, Vector3 eye, Vector3 target)>();
                string earlier = Argument("-ptk-gaps-views");
                if (earlier != null && File.Exists(earlier)) views.AddRange(ReadViews(earlier));
                var known = new HashSet<string>();
                foreach (var v in views) known.Add(v.name);
                int shot = 0;
                foreach (Cluster cluster in report.Clusters)
                {
                    if (shot >= 40) break; // the 40 largest clusters get a shot; the rest are listed
                    cluster.Shot = $"cluster-{cluster.Kind}{cluster.Box}-{cluster.Face}-{cluster.Height:0.0}-{cluster.From:0.0}".Replace(',', '.');
                    if (!known.Contains(cluster.Shot)) { views.Add((cluster.Shot, cluster.Eye, cluster.Target)); known.Add(cluster.Shot); }
                    shot++;
                }
                if (earlier == null) views.AddRange(Sweeps(report));
                WriteViews(Path.Combine(shots, "views.txt"), views);
                Render(art, shots, views);
                Write(report, docs, label, shots);
                Debug.Log($"[WorldArtGapScan] {label}: {report.SeeThrough} see-through rays in {report.Clusters.Count} clusters ({report.Rays} rays from {report.EyePoints} eye points); obstacle fill min {report.MinObstacleFill:0.000}, over max {report.MaxObstacleOver:0.000}; podium fill min {report.MinPodiumFill:0.000}; upper gaps {report.UpperGaps}/{report.UpperRays}; sky gaps {report.SkyGaps} (notches {report.SkyNotches})/{report.SkyRays}; walls {report.WallTriangles} tris / {report.WallRenderers} renderers, backdrop {report.BackdropTriangles} / {report.BackdropRenderers}, obstacles {report.ObstacleTriangles} / {report.ObstacleRenderers}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[WorldArtGapScan] failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static void CountPrefab(GameObject prefab, ref long triangles, ref int renderers)
        {
            if (prefab == null) return;
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderers++;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) triangles += filter.sharedMesh.GetIndexCount(sub) / 3;
            }
        }

        // The left-right sweep: the six faces with the most see-through rays, five eye points 2 m out along each, looking
        // straight at the face.
        private static List<(string, Vector3, Vector3)> Sweeps(Report report)
        {
            var counts = new Dictionary<string, int>();
            foreach (Hit hit in report.Hits)
            {
                string key = hit.Kind + "|" + hit.Box + "|" + hit.Face;
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
            }
            var keys = new List<string>(counts.Keys);
            keys.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : string.CompareOrdinal(a, b));
            var views = new List<(string, Vector3, Vector3)>();
            List<BoxRef> boxes = Boxes(SharedMap.Map);
            for (int k = 0; k < keys.Count && k < 6; k++)
            {
                string[] parts = keys[k].Split('|');
                int index = int.Parse(parts[1]), side = int.Parse(parts[2]);
                BoxRef box = boxes.Find(b => b.Kind == parts[0] && b.Index == index);
                foreach (var face in Faces(box))
                {
                    if (face.side != side) continue;
                    float lookY = box.Kind == "obstacle" ? .6f : 1.5f;
                    for (int p = 0; p < 5; p++)
                    {
                        float u = -1.5f + (face.length + 3) * p / 4f;
                        Vector3 eye = face.origin + face.right * u + face.outward * 2 + Vector3.up * Eye;
                        Vector3 target = face.origin + face.right * Mathf.Clamp(u, 0, face.length) - face.outward * .5f + Vector3.up * lookY;
                        views.Add(($"sweep-{k + 1}-{parts[0]}{index}-{SideNames[side]}-{p + 1}", eye, target));
                    }
                }
            }
            return views;
        }

        private static void WriteViews(string file, List<(string name, Vector3 eye, Vector3 target)> views)
        {
            var text = new StringBuilder();
            foreach (var v in views)
                text.AppendLine(string.Join(" ", v.name, F(v.eye.x), F(v.eye.y), F(v.eye.z), F(v.target.x), F(v.target.y), F(v.target.z)));
            File.WriteAllText(file, text.ToString());
        }

        private static IEnumerable<(string, Vector3, Vector3)> ReadViews(string file)
        {
            foreach (string line in File.ReadAllLines(file))
            {
                string[] p = line.Split(' ');
                if (p.Length != 7) continue;
                yield return (p[0], new Vector3(P(p[1]), P(p[2]), P(p[3])), new Vector3(P(p[4]), P(p[5]), P(p[6])));
            }
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static void Render(WorldArt art, string folder, List<(string name, Vector3 eye, Vector3 target)> views)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("World art gap shots");
            SceneLighting.Apply(root.transform);
            RenderSettings.fog = true;
            GameObject city = BuildCity(art, true, out _);
            city.transform.SetParent(root.transform, false);
            var cameraObject = new GameObject("Gap camera", typeof(Camera)) { tag = "MainCamera" };
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SceneLighting.Background;
            camera.nearClipPlane = ViewPose.Near;
            camera.farClipPlane = FixedMap.Size * 4;
            camera.fieldOfView = Fov;
            camera.GetUniversalAdditionalCameraData();
            const int width = 1280, height = 720;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            foreach (var view in views)
            {
                camera.transform.position = view.eye;
                camera.transform.rotation = Quaternion.LookRotation(view.target - view.eye, Vector3.up);
                camera.Render();
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(folder, view.name + ".png"), image.EncodeToPNG());
            }
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(root);
        }

        private static void Write(Report report, string docs, string label, string shots)
        {
            File.WriteAllText(Path.Combine(docs, "U4l-gaps-" + label + ".json"), JsonUtility.ToJson(report, true));
            var md = new StringBuilder();
            md.AppendLine($"# U4l gap scan: {label}");
            md.AppendLine();
            md.AppendLine("Generated by `WorldArtGapScan.Run` (see the class header for the method). Heights: rays at 0.3 / 1.0 / 1.7 / 2.6 m; wall boxes count up to 3 m, obstacle boxes up to `MapGeometry.ObstacleHeight` 1.2 m.");
            md.AppendLine();
            md.AppendLine($"- Eye points: {report.EyePoints}, rays: {report.Rays}");
            md.AppendLine($"- **See-through rays: {report.SeeThrough} in {report.Clusters.Count} clusters**");
            md.AppendLine($"- Obstacle fill (min over x, z, height/1.2 m): {report.MinObstacleFill:0.000}; largest overshoot {report.MaxObstacleOver:0.000} m");
            md.AppendLine($"- Podium fill (min over x, z, 0-3 m): {report.MinPodiumFill:0.000}");
            md.AppendLine($"- Upper storeys, rays through each group above the podium: {report.UpperGaps} gaps / {report.UpperRays} rays");
            md.AppendLine($"- Sky over the border walls (rays at pitch 0-20 deg; gaps count up to {EyeLevelPitch:0} deg): {report.SkyGaps} gaps, {report.SkyNotches} roofline notches higher up / {report.SkyRays} rays");
            md.AppendLine($"- Counts: walls {report.WallTriangles} triangles / {report.WallRenderers} renderers; backdrop {report.BackdropTriangles} / {report.BackdropRenderers}; obstacles {report.ObstacleTriangles} / {report.ObstacleRenderers}");
            md.AppendLine($"- Shots: `docs/qa/U4l-screens/{label}/` (1280x720, 50 deg vertical FOV, game fog and night lighting; `views.txt` lists every camera)");
            md.AppendLine();
            md.AppendLine("## Clusters");
            md.AppendLine();
            md.AppendLine("| Box | Face | Height (m) | Along from-to (m) | Width (m) | Rays | Worst inside (m) | Shot |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (Cluster c in report.Clusters)
                md.AppendLine($"| {c.Kind} {c.Box} | {c.Face} | {c.Height:0.0} | {c.From:0.0}-{c.To:0.0} | {c.Width:0.0} | {c.Rays} | {c.WorstInside:0.00} | {(c.Shot != null ? "`" + c.Shot + ".png`" : "-")} |");
            md.AppendLine();
            md.AppendLine("## See-through rays on wall boxes");
            md.AppendLine();
            int walls = 0;
            foreach (Hit h in report.Hits)
            {
                if (h.Kind != "wall") continue;
                if (walls++ < 40) md.AppendLine($"- wall {h.Box} {SideNames[h.Face]}: eye ({h.Eye.x:0.00}, {h.Eye.z:0.00}), height {h.Height:0.0}, entry ({h.Entry.x:0.00}, {h.Entry.z:0.00}), {h.Inside:0.00} m inside{(h.Exited ? ", exits" : "")}");
            }
            if (walls == 0) md.AppendLine("None.");
            md.AppendLine();
            md.AppendLine("## Obstacle fill");
            md.AppendLine();
            md.AppendLine("| Obstacle | x | z | height/1.2 | top (m) | overshoot (m) |");
            md.AppendLine("|---|---|---|---|---|---|");
            foreach (Fill f in report.Obstacles) md.AppendLine($"| {f.Box} | {f.X:0.000} | {f.Z:0.000} | {f.Y:0.000} | {f.Top:0.00} | {f.Over:0.000} |");
            md.AppendLine();
            md.AppendLine("## Podiums below 1.0 fill");
            md.AppendLine();
            int low = 0;
            foreach (Fill f in report.Podiums)
                if (Mathf.Min(f.X, Mathf.Min(f.Z, f.Y)) < .999f) { md.AppendLine($"- wall {f.Box}: x {f.X:0.000}, z {f.Z:0.000}, 0-3 m {f.Y:0.000}"); low++; }
            if (low == 0) md.AppendLine("None: every podium fills its box from 0 to 3 m.");
            md.AppendLine();
            md.AppendLine("## Upper storey gaps");
            md.AppendLine();
            if (report.Upper.Count == 0) md.AppendLine("None.");
            for (int i = 0; i < report.Upper.Count && i < 60; i++) md.AppendLine("- " + report.Upper[i]);
            md.AppendLine();
            md.AppendLine("## Sky gaps over the border walls");
            md.AppendLine();
            if (report.Sky.Count == 0) md.AppendLine("None.");
            for (int i = 0; i < report.Sky.Count && i < 60; i++) md.AppendLine("- " + report.Sky[i]);
            File.WriteAllText(Path.Combine(docs, "U4l-gaps-" + label + ".md"), md.ToString());
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
