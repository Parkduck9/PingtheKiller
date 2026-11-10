using System;
using System.Collections.Generic;
using System.IO;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PingTheKiller.Editor.Setup
{
    // Builds the WorldArt catalog for the current FixedMap: graded copies of the City 03 materials, readable copies of the
    // City 03 models (runtime static batching needs readable meshes; the vendor files stay untouched), generated podium,
    // storey and ground meshes, and one prefab per wall box, obstacle box, the ground, the skyline, the terminal and the Exit
    // marker. Idempotent: rerunning overwrites the same assets in place.
    //
    // Fitting rules (HUD_SPEC 6.2 / brief U4h): every wall box is filled exactly from the floor to MapGeometry.WallHeight by
    // the podium mesh; anything higher stands at least Setback inside the outline of its building group (U4k: wall boxes that
    // share an edge form one building, so the Setback applies to the union's outer boundary only), so the Survivor camera
    // (which can rise above a wall box up to about 0.8 m inside its footprint) never ends up inside a visual; obstacles fill
    // their box and stay below MaxObstacleHeight; nothing stands in the walkable area except the flat ground (at most
    // MaxFloorHeight).
    public static class WorldArtSetup
    {
        private const string Root = "Assets/Art/World";
        private const string Catalog = "Assets/Resources/WorldArt.asset";
        private const string CitySource = "Assets/ThirdParty/Fries and Seagull/City 03/Models/";
        private const string DeviceSource = "Assets/ThirdParty/QAtmo/Explosive Device C4 & Defuse Kit/Prefabs/";
        private const string MarkerSource = "Assets/ThirdParty/Hovl Studio/Map track markers VFX/Prefabs/Marker 3 Zone Loop.prefab";
        public const float Setback = 1f;
        public const float Recess = .04f;
        public const float Pavement = .03f;
        public const float MaxFloorHeight = .15f;
        public const float MaxObstacleHeight = 1.5f;
        public const float MaxBuildingHeight = 11f;
        private const float Wall = MapGeometry.WallHeight;

        private static readonly Dictionary<string, Material> graded = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

        [MenuItem("Ping the Killer/Setup World Art")]
        public static void Run()
        {
            foreach (string folder in new[] { Root, Root + "/Materials", Root + "/Models", Root + "/Meshes", Root + "/Prefabs" })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            graded.Clear(); made.Clear(); models.Clear();
            var map = new FixedMap();
            var catalog = AssetDatabase.LoadAssetAtPath<WorldArt>(Catalog);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WorldArt>();
                AssetDatabase.CreateAsset(catalog, Catalog);
            }
            List<SharedEdge> edges = SharedEdges(map);
            Dictionary<int, GroupPlan> plans = PlanGroups(map);
            catalog.Walls = new WorldArt.Piece[map.Walls.Count];
            for (int i = 0; i < map.Walls.Count; i++)
            {
                Wall wall = map.Walls[i];
                plans.TryGetValue(i, out GroupPlan plan);
                catalog.Walls[i] = new WorldArt.Piece { Prefab = Building(i, wall, plan, edges), Box = new Vector4(wall.MinX, wall.MinZ, wall.MaxX, wall.MaxZ) };
            }
            catalog.Obstacles = new WorldArt.Piece[map.Obstacles.Count];
            for (int i = 0; i < map.Obstacles.Count; i++)
            {
                Obstacle obstacle = map.Obstacles[i];
                catalog.Obstacles[i] = new WorldArt.Piece { Prefab = ObstaclePiece(i, obstacle), Box = new Vector4(obstacle.MinX, obstacle.MinZ, obstacle.MaxX, obstacle.MaxZ) };
            }
            catalog.Ground = Ground(map);
            catalog.Backdrop = Backdrop();
            catalog.Terminal = Terminal();
            catalog.ExitMarker = ExitMarker();
            Templates();
            ConvertLeftovers();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            WorldArtCapture.Count(catalog, out long wallTriangles, out int wallRenderers, out long backdropTriangles, out int backdropRenderers);
            Debug.Log("[WorldArtSetup] WorldArt catalog ready: " + map.Walls.Count + " buildings in " + Groups(map).Count + " groups, " + map.Obstacles.Count
                + " obstacles; walls " + wallTriangles + " triangles / " + wallRenderers + " renderers, backdrop " + backdropTriangles + " triangles / " + backdropRenderers + " renderers.");
        }

        // ---- palette -------------------------------------------------------------------------------------------------------

        private static class Palette
        {
            public static Material Asphalt => Lit("Asphalt", new Color32(27, 28, 31, 255), .25f, ToonShading.Kind.Ground);
            public static Material Paving => Lit("Paving", new Color32(47, 47, 49, 255), .1f, ToonShading.Kind.Ground);
            public static Material PlazaA => Lit("Plaza A", new Color32(54, 52, 50, 255), .1f, ToonShading.Kind.Ground);
            public static Material PlazaB => Lit("Plaza B", new Color32(48, 47, 46, 255), .1f, ToonShading.Kind.Ground);
            public static Material Kerb => Lit("Kerb", new Color32(78, 78, 80, 255), .1f, ToonShading.Kind.Ground);
            public static Material Marking => Lit("Marking", new Color32(118, 116, 108, 255), .1f, ToonShading.Kind.Ground);
            public static Material Roof => Lit("Roof", new Color32(42, 43, 46, 255), .05f);
            public static Material Frame => Lit("Frame", new Color32(34, 36, 39, 255), .3f);
            public static Material Glass => Lit("Glass dark", new Color32(22, 26, 32, 255), .75f);
            // Lit shop windows and signs are unlit colours: they read as light sources at night without any light or glow.
            public static Material GlassWarm => Unlit("Glass lit warm", new Color32(124, 117, 104, 255));
            public static Material GlassCool => Unlit("Glass lit cool", new Color32(100, 112, 126, 255));
            public static Material SignWarm => Unlit("Sign warm", new Color32(150, 144, 132, 255));
            public static Material SignCool => Unlit("Sign cool", new Color32(124, 136, 150, 255));
            public static Material Shutter => Lit("Shutter", new Color32(92, 96, 101, 255), .35f);
            public static Material Door => Lit("Door", new Color32(58, 50, 44, 255), .2f);
            public static Material Steel => Lit("Steel", new Color32(66, 70, 77, 255), .45f);
            public static Material SteelDark => Lit("Steel dark", new Color32(40, 43, 48, 255), .45f);
        }

        // Building wall colours: muted, mid value (walls read against the dark ground), never close to signal, ally or danger.
        private static readonly Color32[] WallColours =
        {
            new Color32(176, 158, 134, 255), new Color32(150, 106, 94, 255), new Color32(140, 145, 150, 255),
            new Color32(112, 126, 146, 255), new Color32(138, 142, 112, 255), new Color32(188, 178, 158, 255),
            new Color32(106, 112, 122, 255), new Color32(166, 138, 118, 255)
        };
        private static readonly Color32[] AccentColours =
        {
            new Color32(52, 54, 58, 255), new Color32(84, 66, 58, 255), new Color32(60, 68, 82, 255), new Color32(72, 80, 68, 255)
        };

        // Opaque world material. U4i: toon (two bands, no rim, no specular) when the toon shader is available, else URP Lit.
        private static Material Lit(string name, Color color, float smoothness, ToonShading.Kind kind = ToonShading.Kind.World)
        {
            if (made.TryGetValue(name, out Material cached)) return cached;
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader toon = ToonShading.Find();
            Shader shader = toon != null ? toon : Shader.Find("Universal Render Pipeline/Lit");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader)
            {
                material.shader = shader;
                material.shaderKeywords = new string[0];
            }
            // Attempt 2: the night grade is applied here, to the palette colour, so material names and GUIDs never change.
            material.SetColor("_BaseColor", kind == ToonShading.Kind.Character ? color : ToonShading.NightGrade(color));
            if (toon != null) ToonShading.Configure(material, kind);
            else
            {
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", 0);
                material.SetFloat("_EnvironmentReflections", 0);
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            made[name] = material;
            return material;
        }

        // Materials no current piece uses (earlier map versions, the orphan Barricade 3 model) still get the toon look, so no
        // URP Lit material is left under Art/World.
        private static void ConvertLeftovers()
        {
            if (ToonShading.Find() == null) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null || material.shader.name != "Universal Render Pipeline/Lit") continue;
                Lit(Path.GetFileNameWithoutExtension(path), material.GetColor("_BaseColor"), 0);
            }
        }

        // The runtime templates (WorldMaterials.NewMaterial): opaque and fade. Being in Resources, they also put the toon
        // shader with the world variant into the player build.
        private static void Templates()
        {
            Shader toon = ToonShading.Find();
            if (toon == null) { Debug.LogWarning("[WorldArtSetup] Toon shader missing; runtime materials stay URP Lit."); return; }
            foreach (bool fade in new[] { false, true })
            {
                string path = "Assets/Resources/" + (fade ? ToonShading.FadeTemplate : ToonShading.Template) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(toon);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = toon;
                material.shaderKeywords = new string[0];
                material.SetColor("_BaseColor", Color.white);
                ToonShading.Configure(material, ToonShading.Kind.World);
                material.SetFloat("_Surface", fade ? 1 : 0);
                material.SetFloat("_SrcBlend", (float)(fade ? BlendMode.SrcAlpha : BlendMode.One));
                material.SetFloat("_DstBlend", (float)(fade ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                material.SetFloat("_ZWrite", fade ? 0 : 1);
                material.renderQueue = fade ? (int)RenderQueue.Transparent : -1;
                EditorUtility.SetDirty(material);
            }
        }

        private static Material Unlit(string name, Color color)
        {
            if (made.TryGetValue(name, out Material cached)) return cached;
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            made[name] = material;
            return material;
        }

        private static Material WallMaterial(Color32 color) => Lit("Wall " + Hex(color), color, .08f);

        // City 03 colours, graded toward the dusk palette: saturation capped so the game colours stay the most saturated
        // ones on screen, value lowered a little.
        public static Color Grade(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Min(s * .6f, .32f), Mathf.Min(v * .82f, .78f));
        }

        private static Material Graded(Material source)
        {
            Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            Color32 target = Grade(color);
            string key = Hex(target);
            if (!graded.TryGetValue(key, out Material material))
                graded[key] = material = Lit("City " + key, target, Mathf.Min(source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : .1f, .3f));
            return material;
        }

        private static string Hex(Color32 c) => c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        // ---- City 03 model copies ------------------------------------------------------------------------------------------

        private static GameObject Model(string name)
        {
            if (models.TryGetValue(name, out GameObject cached)) return cached;
            string source = CitySource + name + ".fbx", target = Root + "/Models/" + name + ".fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null) throw new InvalidOperationException("Missing world source: " + source);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(target) == null && !AssetDatabase.CopyAsset(source, target))
                throw new InvalidOperationException("Could not copy " + source);
            var importer = (ModelImporter)AssetImporter.GetAtPath(target);
            bool changed = false;
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; changed = true; }
            if (importer.importCameras || importer.importLights) { importer.importCameras = false; importer.importLights = false; changed = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = false; changed = true; }
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> remaps = importer.GetExternalObjectMap();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(source))
            {
                if (!(asset is Material material)) continue;
                Material target2 = Graded(material);
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name);
                if (remaps.TryGetValue(id, out Object current) && current == target2) continue;
                importer.AddRemap(id, target2);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
            return models[name] = AssetDatabase.LoadAssetAtPath<GameObject>(target);
        }

        // Bounds of every mesh under item, in the space of frame.
        private static Bounds BoundsIn(GameObject item, Transform frame)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 m = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds local = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? local.min.x : local.max.x, (c & 2) == 0 ? local.min.y : local.max.y, (c & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        // Places a model (yaw in 90 degree steps) so that its mesh bounds fill [min, max] in parent space exactly.
        private static Transform Fit(GameObject model, Transform parent, Vector3 min, Vector3 max, int yaw, string label)
        {
            var holder = new GameObject(label).transform;
            holder.SetParent(parent, false);
            var item = (GameObject)Object.Instantiate(model, holder);
            item.name = model.name;
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            item.transform.localScale = Vector3.one;
            Bounds b = BoundsIn(item, holder);
            Vector3 size = max - min;
            holder.localScale = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
            holder.localPosition = min - Vector3.Scale(b.min, holder.localScale);
            foreach (Collider collider in item.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return holder;
        }

        // Fits a model into a footprint: the yaw (0 or 90, plus 180 when flip) whose footprint aspect is closest to the area's,
        // its natural height scaled by the mean footprint scale, kept between MinModelTop and top.
        private static Transform FitUpper(GameObject model, Transform parent, Area area, float top, bool flip, string label)
        {
            int best = 0;
            float bestScore = float.MaxValue, height = 0;
            foreach (int yaw in new[] { 0, 90 })
            {
                var probe = (GameObject)Object.Instantiate(model);
                probe.transform.rotation = Quaternion.Euler(0, yaw, 0);
                var frame = new GameObject("frame").transform;
                Bounds b = BoundsIn(probe, frame);
                Object.DestroyImmediate(probe);
                Object.DestroyImmediate(frame.gameObject);
                float score = Mathf.Abs(Mathf.Log(b.size.x / b.size.z * area.Depth / area.Width));
                if (score >= bestScore) continue;
                bestScore = score;
                best = yaw;
                float scale = (area.Width / b.size.x + area.Depth / b.size.z) * .5f;
                height = Mathf.Clamp(b.size.y * scale, MinModelTop, top);
            }
            return Fit(model, parent, new Vector3(area.MinX, 0, area.MinZ), new Vector3(area.MaxX, height, area.MaxZ), best + (flip ? 180 : 0), label);
        }

        // ---- generated meshes ----------------------------------------------------------------------------------------------

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Material> materials = new List<Material>();
            private readonly List<List<int>> triangles = new List<List<int>>();
            private readonly List<Vector3> polygon = new List<Vector3>(8), scratch = new List<Vector3>(8);
            private const float ClipEps = 1e-4f;

            // U4k: when set, every quad is clipped to this footprint (a vertical prism), so one group-wide building can be
            // split exactly along the wall boxes it spans: each box's prefab keeps only the part inside its own box.
            public bool HasClip;
            public Area Clip;

            private int Sub(Material material)
            {
                int sub = materials.IndexOf(material);
                if (sub < 0) { materials.Add(material); triangles.Add(new List<int>()); sub = materials.Count - 1; }
                return sub;
            }

            // a, b, c, d: bottom-left, top-left, top-right, bottom-right seen from the front.
            public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
                if (HasClip)
                {
                    polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c); polygon.Add(d);
                    int clipped = ClipPolygon(normal);
                    if (clipped == 0) return;
                    if (clipped == 2) { Polygon(material, normal); return; }
                }
                int sub = Sub(material);
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                List<int> t = triangles[sub];
                t.Add(start); t.Add(start + 1); t.Add(start + 2);
                t.Add(start); t.Add(start + 2); t.Add(start + 3);
            }

            // The clipped polygon as a triangle fan (the winding of the source quad is kept).
            private void Polygon(Material material, Vector3 normal)
            {
                List<int> t = triangles[Sub(material)];
                int start = vertices.Count;
                foreach (Vector3 p in polygon) { vertices.Add(p); normals.Add(normal); uvs.Add(Vector2.zero); }
                for (int i = 1; i + 1 < polygon.Count; i++) { t.Add(start); t.Add(start + i); t.Add(start + i + 1); }
            }

            // Sutherland-Hodgman against the four sides of Clip. 0 = nothing left, 1 = untouched, 2 = clipped (in polygon).
            // A polygon lying on a clip line belongs to this side only when it faces out of it, i.e. it bounds a solid on
            // this side; so a face on a shared box edge is kept exactly once.
            private int ClipPolygon(Vector3 normal)
            {
                bool changed = false;
                for (int plane = 0; plane < 4; plane++)
                {
                    bool alongX = plane < 2;
                    float value = plane == 0 ? Clip.MinX : plane == 1 ? Clip.MaxX : plane == 2 ? Clip.MinZ : Clip.MaxZ;
                    float sign = plane % 2 == 0 ? 1 : -1; // inside: sign * (coordinate - value) >= 0
                    bool allOn = true, allIn = true, allOut = true;
                    foreach (Vector3 p in polygon)
                    {
                        float d = sign * ((alongX ? p.x : p.z) - value);
                        if (Mathf.Abs(d) > ClipEps) allOn = false;
                        if (d < -ClipEps) allIn = false;
                        if (d > ClipEps) allOut = false;
                    }
                    if (allOn)
                    {
                        if ((alongX ? normal.x : normal.z) * -sign > .5f) continue;
                        return 0;
                    }
                    if (allIn) continue;
                    if (allOut) return 0;
                    scratch.Clear();
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        Vector3 p = polygon[i], q = polygon[(i + 1) % polygon.Count];
                        float dp = sign * ((alongX ? p.x : p.z) - value), dq = sign * ((alongX ? q.x : q.z) - value);
                        if (dp >= 0) scratch.Add(p);
                        if ((dp >= 0) != (dq >= 0)) scratch.Add(p + (q - p) * (dp / (dp - dq)));
                    }
                    polygon.Clear();
                    polygon.AddRange(scratch);
                    changed = true;
                    if (polygon.Count < 3) return 0;
                }
                if (!changed) return 1;
                Vector3 area = Vector3.zero;
                for (int i = 1; i + 1 < polygon.Count; i++) area += Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
                return area.magnitude < 1e-6f ? 0 : 2;
            }

            // A rectangle on a face plane: origin = bottom-left corner of the face seen from outside, right along the face,
            // out = outward normal; u along right, v up, depth > 0 is behind the face plane (a recess), < 0 in front of it.
            public void Panel(Material material, Vector3 origin, Vector3 right, Vector3 out_, float u0, float u1, float v0, float v1, float depth)
            {
                Vector3 back = -out_ * depth;
                Quad(material, origin + right * u0 + Vector3.up * v0 + back, origin + right * u0 + Vector3.up * v1 + back,
                    origin + right * u1 + Vector3.up * v1 + back, origin + right * u1 + Vector3.up * v0 + back);
            }

            // The four side faces of a recess (or a protrusion when depth < 0) around [u0,u1] x [v0,v1].
            public void Reveal(Material material, Vector3 origin, Vector3 right, Vector3 out_, float u0, float u1, float v0, float v1, float depth)
            {
                Vector3 back = -out_ * depth, up = Vector3.up;
                Vector3 p00 = origin + right * u0 + up * v0, p01 = origin + right * u0 + up * v1, p11 = origin + right * u1 + up * v1, p10 = origin + right * u1 + up * v0;
                if (depth > 0)
                {
                    Quad(material, p00, p00 + back, p01 + back, p01); // left wall faces +right
                    Quad(material, p10 + back, p10, p11, p11 + back); // right wall faces -right
                    Quad(material, p00 + back, p00, p10, p10 + back); // floor faces up
                    Quad(material, p01, p01 + back, p11 + back, p11); // ceiling faces down
                }
                else
                {
                    Quad(material, p00 + back, p00, p01, p01 + back);
                    Quad(material, p10, p10 + back, p11 + back, p11);
                    Quad(material, p00, p00 + back, p10 + back, p10);
                    Quad(material, p01 + back, p01, p11, p11 + back);
                }
            }

            // Horizontal rectangle facing up at height y.
            public void Top(Material material, float minX, float minZ, float maxX, float maxZ, float y)
            {
                Quad(material, new Vector3(minX, y, minZ), new Vector3(minX, y, maxZ), new Vector3(maxX, y, maxZ), new Vector3(maxX, y, minZ));
            }

            // Closed box (no bottom).
            public void Box(Material material, Vector3 min, Vector3 max, Material top = null)
            {
                Top(top ?? material, min.x, min.z, max.x, max.z, max.y);
                foreach (Face face in Faces(min.x, min.z, max.x, max.z))
                    Panel(material, face.Origin + Vector3.up * min.y, face.Right, face.Out, 0, face.Length, 0, max.y - min.y, 0);
            }

            public bool Empty => vertices.Count == 0;

            public Mesh Build(string name, out Material[] used)
            {
                var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = materials.Count;
                for (int i = 0; i < materials.Count; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                used = materials.ToArray();
                return mesh;
            }
        }

        private struct Face
        {
            public Vector3 Origin, Right, Out;
            public float Length;
        }

        // The four vertical faces of a box, each with its bottom-left corner seen from outside.
        private static IEnumerable<Face> Faces(float minX, float minZ, float maxX, float maxZ)
        {
            yield return new Face { Origin = new Vector3(minX, 0, minZ), Right = Vector3.right, Out = Vector3.back, Length = maxX - minX };
            yield return new Face { Origin = new Vector3(maxX, 0, minZ), Right = Vector3.forward, Out = Vector3.right, Length = maxZ - minZ };
            yield return new Face { Origin = new Vector3(maxX, 0, maxZ), Right = Vector3.left, Out = Vector3.forward, Length = maxX - minX };
            yield return new Face { Origin = new Vector3(minX, 0, maxZ), Right = Vector3.back, Out = Vector3.left, Length = maxZ - minZ };
        }

        private static Mesh SaveMesh(string name, Mesh mesh)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        private static GameObject MeshObject(string label, Transform parent, MeshBuilder builder, string asset, bool castShadows = true)
        {
            Mesh mesh = SaveMesh(asset, builder.Build(asset, out Material[] used));
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = used;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return item;
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---- facades -------------------------------------------------------------------------------------------------------

        private enum Bay { ShopLit, ShopCool, ShopDark, Door, Shutter, Blank }

        private sealed class Style
        {
            public Material Wall, Base, Accent;
            public bool Service; // border walls: shutters, doors and few windows
        }

        // Ground floor (0 to Wall): base, shop fronts recessed by Recess, a fascia band and a slim cornice (Recess out).
        // shared[k]: the spans (u along face k) this box shares with another box of its group; no parapet runs there, so
        // the podium roofs of one building join without a ridge.
        private static void Podium(MeshBuilder mb, float minX, float minZ, float maxX, float maxZ, Style style, System.Random random, List<Vector2>[] shared)
        {
            const float baseTop = .35f, openTop = 2.25f, fasciaTop = 2.78f, corner = .45f, pilaster = .22f;
            foreach (Face face in Faces(minX, minZ, maxX, maxZ))
            {
                float length = face.Length;
                int bays = length < 2.4f ? 0 : Mathf.Max(1, Mathf.FloorToInt((length - 2 * corner) / (style.Service ? 3.6f : 3.1f)));
                float bayWidth = bays > 0 ? (length - 2 * corner) / bays : 0;
                // Wall and base between the openings.
                float u = 0;
                for (int b = 0; b < bays; b++)
                {
                    float o0 = corner + b * bayWidth + pilaster;
                    mb.Panel(style.Wall, face.Origin, face.Right, face.Out, u, o0, baseTop, openTop, 0);
                    mb.Panel(style.Base, face.Origin, face.Right, face.Out, u, o0, 0, baseTop, 0);
                    u = corner + (b + 1) * bayWidth - pilaster;
                }
                mb.Panel(style.Wall, face.Origin, face.Right, face.Out, u, length, baseTop, openTop, 0);
                mb.Panel(style.Base, face.Origin, face.Right, face.Out, u, length, 0, baseTop, 0);
                for (int b = 0; b < bays; b++)
                {
                    float u0 = corner + b * bayWidth + pilaster, u1 = corner + (b + 1) * bayWidth - pilaster;
                    Bay kind = PickBay(style, random);
                    OpeningBay(mb, face, kind, style, u0, u1, baseTop, openTop);
                    if (kind == Bay.ShopLit || kind == Bay.ShopCool)
                        if (random.NextDouble() < .45) SignBox(mb, face, u0 + (u1 - u0) * .18f, u1 - (u1 - u0) * .18f, openTop + .1f, fasciaTop - .1f, random);
                }
                mb.Panel(style.Accent, face.Origin, face.Right, face.Out, 0, length, openTop, fasciaTop, 0);
                // Cornice: a slab Recess in front of the face from fasciaTop to the wall top.
                mb.Panel(style.Base, face.Origin, face.Right, face.Out, 0, length, fasciaTop, Wall, -Recess);
                mb.Reveal(style.Base, face.Origin, face.Right, face.Out, 0, length, fasciaTop, Wall, -Recess);
            }
            mb.Top(Palette.Roof, minX, minZ, maxX, maxZ, Wall);
            // Parapet on the outer roof edge, inside the box.
            const float parapet = .18f, parapetTop = Wall + .14f;
            int k = -1;
            foreach (Face face in Faces(minX, minZ, maxX, maxZ))
            {
                k++;
                if (face.Length - 2 * parapet <= 0) continue;
                foreach (Vector2 span in Outer(face.Length, shared?[k]))
                {
                    float u0 = span.x, u1 = span.y;
                    bool startCorner = u0 < Eps, endCorner = u1 > face.Length - Eps;
                    float i0 = startCorner ? parapet : u0, i1 = endCorner ? face.Length - parapet : u1;
                    mb.Panel(style.Base, face.Origin, face.Right, face.Out, u0, u1, Wall, parapetTop, 0);
                    if (i1 <= i0) continue;
                    Vector3 inner = face.Origin - face.Out * parapet + Vector3.up * Wall;
                    mb.Panel(style.Base, inner, -face.Right, -face.Out, -i1, -i0, 0, parapetTop - Wall, 0);
                    Vector3 a = face.Origin + Vector3.up * parapetTop + face.Right * u0, b = face.Origin + Vector3.up * parapetTop + face.Right * u1;
                    mb.Quad(style.Base, a, a - face.Out * parapet + face.Right * (i0 - u0), b - face.Out * parapet - face.Right * (u1 - i1), b);
                    // Square end caps where the parapet stops at a shared span.
                    if (!startCorner) Cap(mb, style.Base, face, u0, parapet, parapetTop, false);
                    if (!endCorner) Cap(mb, style.Base, face, u1, parapet, parapetTop, true);
                }
            }
        }

        private static void Cap(MeshBuilder mb, Material material, Face face, float u, float thickness, float top, bool facingRight)
        {
            Vector3 p0 = face.Origin + face.Right * u + Vector3.up * Wall, p1 = p0 + Vector3.up * (top - Wall);
            Vector3 p2 = p1 - face.Out * thickness, p3 = p0 - face.Out * thickness;
            if (facingRight) mb.Quad(material, p0, p1, p2, p3);
            else mb.Quad(material, p3, p2, p1, p0);
        }

        // [0, length] minus the given spans.
        private static List<Vector2> Outer(float length, List<Vector2> spans)
        {
            var result = new List<Vector2>();
            float cursor = 0;
            if (spans != null)
            {
                var sorted = new List<Vector2>(spans);
                sorted.Sort((p, q) => p.x.CompareTo(q.x));
                foreach (Vector2 span in sorted)
                {
                    if (span.x > cursor + Eps) result.Add(new Vector2(cursor, span.x));
                    cursor = Mathf.Max(cursor, span.y);
                }
            }
            if (length > cursor + Eps) result.Add(new Vector2(cursor, length));
            return result;
        }

        private static Bay PickBay(Style style, System.Random random)
        {
            double r = random.NextDouble();
            if (style.Service) return r < .45 ? Bay.Shutter : r < .65 ? Bay.Door : r < .78 ? Bay.ShopDark : r < .86 ? Bay.ShopLit : Bay.Blank;
            return r < .3 ? Bay.ShopLit : r < .5 ? Bay.ShopCool : r < .72 ? Bay.ShopDark : r < .84 ? Bay.Door : r < .93 ? Bay.Shutter : Bay.Blank;
        }

        private static void OpeningBay(MeshBuilder mb, Face face, Bay kind, Style style, float u0, float u1, float baseTop, float openTop)
        {
            const float frame = .07f;
            switch (kind)
            {
                case Bay.Blank:
                    mb.Panel(style.Wall, face.Origin, face.Right, face.Out, u0, u1, baseTop, openTop, 0);
                    mb.Panel(style.Base, face.Origin, face.Right, face.Out, u0, u1, 0, baseTop, 0);
                    return;
                case Bay.Shutter:
                case Bay.Door:
                {
                    Material leaf = kind == Bay.Shutter ? Palette.Shutter : Palette.Door;
                    float d0 = u0, d1 = u1;
                    if (kind == Bay.Door)
                    {
                        float mid = (u0 + u1) * .5f, half = Mathf.Min(.65f, (u1 - u0) * .5f);
                        d0 = mid - half; d1 = mid + half;
                        mb.Panel(style.Wall, face.Origin, face.Right, face.Out, u0, d0, baseTop, openTop, 0);
                        mb.Panel(style.Wall, face.Origin, face.Right, face.Out, d1, u1, baseTop, openTop, 0);
                        mb.Panel(style.Base, face.Origin, face.Right, face.Out, u0, d0, 0, baseTop, 0);
                        mb.Panel(style.Base, face.Origin, face.Right, face.Out, d1, u1, 0, baseTop, 0);
                    }
                    Grid(mb, face, leaf, Palette.Frame, d0, d1, 0, openTop, frame, kind == Bay.Shutter ? 0 : 1, kind == Bay.Shutter ? 6 : 1);
                    mb.Reveal(Palette.Frame, face.Origin, face.Right, face.Out, d0, d1, 0, openTop, Recess);
                    return;
                }
                default:
                {
                    Material glass = kind == Bay.ShopLit ? Palette.GlassWarm : kind == Bay.ShopCool ? Palette.GlassCool : Palette.Glass;
                    mb.Panel(style.Base, face.Origin, face.Right, face.Out, u0, u1, 0, baseTop, 0);
                    int columns = Mathf.Max(1, Mathf.RoundToInt((u1 - u0) / 1.2f));
                    Grid(mb, face, glass, Palette.Frame, u0, u1, baseTop, openTop, frame, columns, 2);
                    mb.Reveal(Palette.Frame, face.Origin, face.Right, face.Out, u0, u1, baseTop, openTop, Recess);
                    return;
                }
            }
        }

        // A framed opening recessed by Recess: panes split into columns (and rows; rows = 2 puts a transom near the top,
        // more rows give the horizontal slats of a shutter), frame strips between them. Panes and frames tile the opening.
        private static void Grid(MeshBuilder mb, Face face, Material pane, Material frameMaterial, float u0, float u1, float v0, float v1, float frame, int columns, int rows)
        {
            var us = new List<float> { u0 };
            if (columns == 0)
            {
                us.Add(u1);
            }
            else
            {
                float paneWidth = (u1 - u0 - (columns + 1) * frame) / columns;
                float u = u0;
                for (int c = 0; c < columns; c++) { u += frame; us.Add(u); u += paneWidth; us.Add(u); }
                us.Add(u1);
            }
            var vs = new List<float> { v0 };
            if (rows == 2)
            {
                float transom = v1 - .32f;
                vs.Add(v0 + frame); vs.Add(transom); vs.Add(transom + frame); vs.Add(v1 - frame); vs.Add(v1);
            }
            else if (rows > 2)
            {
                float slat = (v1 - v0) / rows;
                for (int r = 1; r < rows; r++) { vs.Add(v0 + r * slat - .02f); vs.Add(v0 + r * slat + .02f); }
                vs.Add(v1);
            }
            else
            {
                vs.Add(v0 + frame); vs.Add(v1 - frame); vs.Add(v1);
            }
            for (int c = 0; c + 1 < us.Count; c++)
                for (int r = 0; r + 1 < vs.Count; r++)
                {
                    bool frameColumn = columns > 0 && c % 2 == 0, frameRow = r % 2 == 0;
                    if (columns == 0) frameColumn = false;
                    Material material = frameColumn || frameRow ? frameMaterial : pane;
                    if (rows > 2) material = r % 2 == 1 ? frameMaterial : pane;
                    mb.Panel(material, face.Origin, face.Right, face.Out, us[c], us[c + 1], vs[r], vs[r + 1], Recess);
                }
        }

        // A lit sign box on the fascia, standing Recess in front of the face.
        private static void SignBox(MeshBuilder mb, Face face, float u0, float u1, float v0, float v1, System.Random random)
        {
            Material sign = random.NextDouble() < .5 ? Palette.SignWarm : Palette.SignCool;
            mb.Panel(sign, face.Origin, face.Right, face.Out, u0, u1, v0, v1, -Recess);
            mb.Reveal(Palette.Frame, face.Origin, face.Right, face.Out, u0, u1, v0, v1, -Recess);
        }

        // Window rhythm of a generated building; each group gets one, so neighbouring blocks do not repeat the same facade.
        private sealed class Facade
        {
            public float WindowWidth, Pitch, Lit;
            public int Columns;
            public bool Band; // an accent string course at every floor line
        }

        private static readonly Facade[] Facades =
        {
            new Facade { WindowWidth = 1.1f, Pitch = 2.3f, Lit = .3f, Columns = 2 },
            new Facade { WindowWidth = 1.6f, Pitch = 3f, Lit = .25f, Columns = 3, Band = true },
            new Facade { WindowWidth = .8f, Pitch = 1.7f, Lit = .35f, Columns = 1 }
        };

        // Upper storeys of a generated building, standing on the podium roof (Wall): window grid, roof and parapet.
        // faces[k] (Faces order): 0 = none (hidden by an at least as tall neighbouring wing), 1 = plain wall, 2 = windows.
        // parapets[k]: whether the roof edge on face k gets a parapet (not where the roof meets an equal or taller wing).
        private static void Storeys(MeshBuilder mb, Area a, int floors, float storey, Style style, Facade facade, System.Random random, byte[] faces, bool[] parapets)
        {
            float height = floors * storey, top = Wall + height;
            int k = -1;
            foreach (Face face in Faces(a.MinX, a.MinZ, a.MaxX, a.MaxZ))
            {
                k++;
                if (faces[k] == 0) continue;
                Vector3 origin = face.Origin + Vector3.up * Wall;
                var raised = new Face { Origin = origin, Right = face.Right, Out = face.Out, Length = face.Length };
                if (faces[k] == 1 || face.Length < facade.WindowWidth + .8f)
                {
                    mb.Panel(style.Wall, origin, face.Right, face.Out, 0, face.Length, 0, height, 0);
                }
                else
                {
                    int windows = Mathf.Max(1, Mathf.FloorToInt((face.Length - .6f) / facade.Pitch));
                    float pitch = (face.Length - .6f) / windows;
                    for (int f = 0; f < floors; f++)
                    {
                        float y0 = f * storey, sill = y0 + .9f, head = y0 + Mathf.Min(2.25f, storey - .25f), y1 = y0 + storey;
                        float band = facade.Band && f > 0 ? .14f : 0;
                        if (band > 0) mb.Panel(style.Accent, origin, face.Right, face.Out, 0, face.Length, y0, y0 + band, 0);
                        mb.Panel(style.Wall, origin, face.Right, face.Out, 0, face.Length, y0 + band, sill, 0);
                        mb.Panel(style.Wall, origin, face.Right, face.Out, 0, face.Length, head, y1, 0);
                        float u = 0;
                        for (int w = 0; w < windows; w++)
                        {
                            float w0 = .3f + w * pitch + (pitch - facade.WindowWidth) * .5f, w1 = w0 + facade.WindowWidth;
                            mb.Panel(style.Wall, origin, face.Right, face.Out, u, w0, sill, head, 0);
                            double r = random.NextDouble();
                            Material glass = r < facade.Lit ? Palette.GlassWarm : r < facade.Lit + .12 ? Palette.GlassCool : Palette.Glass;
                            Grid(mb, raised, glass, Palette.Frame, w0, w1, sill, head, .06f, facade.Columns, 0);
                            mb.Reveal(Palette.Frame, origin, face.Right, face.Out, w0, w1, sill, head, Recess);
                            u = w1;
                        }
                        mb.Panel(style.Wall, origin, face.Right, face.Out, u, face.Length, sill, head, 0);
                    }
                }
                mb.Panel(style.Base, origin, face.Right, face.Out, 0, face.Length, height, height + .3f, 0);
            }
            mb.Top(Palette.Roof, a.MinX, a.MinZ, a.MaxX, a.MaxZ, top);
            k = -1;
            foreach (Face face in Faces(a.MinX, a.MinZ, a.MaxX, a.MaxZ))
            {
                k++;
                if (!parapets[k]) continue;
                Vector3 inner = face.Origin - face.Out * .2f + face.Right * .2f + Vector3.up * top;
                float length = face.Length - .4f;
                if (length <= 0) continue;
                mb.Panel(style.Base, inner, -face.Right, -face.Out, -length, 0, 0, .3f, 0);
                Vector3 p = face.Origin + Vector3.up * (top + .3f), q = p + face.Right * face.Length;
                mb.Quad(style.Base, p, p - face.Out * .2f + face.Right * .2f, q - face.Out * .2f - face.Right * .2f, q);
            }
        }

        // Low roof furniture on a generated roof: a stair house (two-storey wings only) and air-conditioning units. Generated
        // quads, so the box clip splits them like the walls; everything stays below MaxBuildingHeight.
        private static void RoofProps(MeshBuilder mb, Area a, float roof, int floors, Style style, System.Random random)
        {
            float room = MaxBuildingHeight - .05f - roof;
            int count = a.Width * a.Depth > 30 ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                bool stair = floors <= 2 && i == 0 && random.NextDouble() < .6;
                float sx = stair ? 2.2f : 1.2f, sz = stair ? 1.8f : .8f, sy = Mathf.Min(stair ? 2.2f : .7f, room);
                if (random.NextDouble() < .5) { float swap = sx; sx = sz; sz = swap; }
                double px = random.NextDouble(), pz = random.NextDouble();
                if (sy < .2f || sx > a.Width - 1.2f || sz > a.Depth - 1.2f) continue;
                float x0 = a.MinX + .6f + (float)px * (a.Width - 1.2f - sx), z0 = a.MinZ + .6f + (float)pz * (a.Depth - 1.2f - sz);
                if (stair) mb.Box(style.Base, new Vector3(x0, roof, z0), new Vector3(x0 + sx, roof + sy, z0 + sz), Palette.Roof);
                else mb.Box(Palette.Steel, new Vector3(x0, roof, z0), new Vector3(x0 + sx, roof + sy, z0 + sz), Palette.SteelDark);
            }
        }

        // ---- building groups (U4k) -----------------------------------------------------------------------------------------

        public const float Eps = .001f;
        // Storey height of the generated uppers: two storeys reach 8 m (8.3 m with the parapet), three 10.5 m (10.8 m).
        public const float Storey = 2.5f;
        // Fitted house models on single-box buildings: at least this tall (well above two storeys over the podium).
        public const float MinModelTop = 7f;

        // An axis-aligned footprint on the ground plane.
        public struct Area
        {
            public float MinX, MinZ, MaxX, MaxZ;
            public Area(float minX, float minZ, float maxX, float maxZ) { MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; }
            public static Area Of(Wall wall) => new Area(wall.MinX, wall.MinZ, wall.MaxX, wall.MaxZ);
            public float Width => MaxX - MinX;
            public float Depth => MaxZ - MinZ;
            public Area Inset(float d) => new Area(MinX + d, MinZ + d, MaxX - d, MaxZ - d);
            public Area Offset(float dx, float dz) => new Area(MinX + dx, MinZ + dz, MaxX + dx, MaxZ + dz);
            public bool Overlaps(Area o) => Mathf.Min(MaxX, o.MaxX) - Mathf.Max(MinX, o.MinX) > Eps && Mathf.Min(MaxZ, o.MaxZ) - Mathf.Max(MinZ, o.MinZ) > Eps;
            public bool Contains(float x, float z) => x > MinX && x < MaxX && z > MinZ && z < MaxZ;
            public bool Encloses(Area o) => o.MinX >= MinX - Eps && o.MaxX <= MaxX + Eps && o.MinZ >= MinZ - Eps && o.MaxZ <= MaxZ + Eps;
            public override string ToString() => $"({MinX:0.##}, {MinZ:0.##})-({MaxX:0.##}, {MaxZ:0.##})";
        }

        // A segment of positive length on which two interior wall boxes touch. AlongX: the edge runs along x at z = Line,
        // else along z at x = Line; it covers [Lo, Hi] along its direction. A < B.
        public struct SharedEdge
        {
            public int A, B;
            public bool AlongX;
            public float Line, Lo, Hi;
            public override string ToString() => $"{A}/{B} {(AlongX ? "z" : "x")}={Line:0.##} [{Lo:0.##}, {Hi:0.##}]";
        }

        // Border walls reach outside the arena square; they get no upper (the skyline stands behind them).
        public static bool IsBorder(Wall wall) => wall.MinX < 0 || wall.MinZ < 0 || wall.MaxX > FixedMap.Size || wall.MaxZ > FixedMap.Size;

        public static List<SharedEdge> SharedEdges(FixedMap map)
        {
            var edges = new List<SharedEdge>();
            for (int a = 0; a < map.Walls.Count; a++)
            {
                if (IsBorder(map.Walls[a])) continue;
                for (int b = a + 1; b < map.Walls.Count; b++)
                {
                    if (IsBorder(map.Walls[b])) continue;
                    Wall p = map.Walls[a], q = map.Walls[b];
                    if (Area.Of(p).Overlaps(Area.Of(q))) throw new InvalidOperationException("Wall boxes " + a + " and " + b + " overlap; the building groups assume touching boxes only.");
                    if (Mathf.Abs(p.MaxZ - q.MinZ) < Eps) AddEdge(edges, a, b, true, p.MaxZ, p.MinX, p.MaxX, q.MinX, q.MaxX);
                    if (Mathf.Abs(p.MinZ - q.MaxZ) < Eps) AddEdge(edges, a, b, true, p.MinZ, p.MinX, p.MaxX, q.MinX, q.MaxX);
                    if (Mathf.Abs(p.MaxX - q.MinX) < Eps) AddEdge(edges, a, b, false, p.MaxX, p.MinZ, p.MaxZ, q.MinZ, q.MaxZ);
                    if (Mathf.Abs(p.MinX - q.MaxX) < Eps) AddEdge(edges, a, b, false, p.MinX, p.MinZ, p.MaxZ, q.MinZ, q.MaxZ);
                }
            }
            return edges;
        }

        private static void AddEdge(List<SharedEdge> edges, int a, int b, bool alongX, float line, float p0, float p1, float q0, float q1)
        {
            float lo = Mathf.Max(p0, q0), hi = Mathf.Min(p1, q1);
            if (hi - lo > Eps) edges.Add(new SharedEdge { A = a, B = b, AlongX = alongX, Line = line, Lo = lo, Hi = hi });
        }

        // Building groups: interior wall boxes connected through shared edges, each sorted, ordered by their first box.
        public static List<int[]> Groups(FixedMap map)
        {
            int n = map.Walls.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            foreach (SharedEdge edge in SharedEdges(map)) parent[FindRoot(parent, edge.A)] = FindRoot(parent, edge.B);
            var lists = new List<List<int>>();
            var byRoot = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (IsBorder(map.Walls[i])) continue;
                int root = FindRoot(parent, i);
                if (!byRoot.TryGetValue(root, out List<int> list)) { byRoot[root] = list = new List<int>(); lists.Add(list); }
                list.Add(i);
            }
            var groups = new List<int[]>();
            foreach (List<int> list in lists) groups.Add(list.ToArray());
            return groups;
        }

        private static int FindRoot(int[] parent, int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }

        // Sides in Faces order: 0 south (z = MinZ), 1 east (x = MaxX), 2 north (z = MaxZ), 3 west (x = MinX).
        private static bool SideAlongX(int side) => side % 2 == 0;
        private static float SideLine(Area a, int side) => side == 0 ? a.MinZ : side == 1 ? a.MaxX : side == 2 ? a.MaxZ : a.MinX;
        private static float SideLo(Area a, int side) => SideAlongX(side) ? a.MinX : a.MinZ;
        private static float SideHi(Area a, int side) => SideAlongX(side) ? a.MaxX : a.MaxZ;

        // True when the intervals together cover [lo, hi].
        private static bool Covers(List<Vector2> intervals, float lo, float hi)
        {
            var sorted = new List<Vector2>(intervals);
            sorted.Sort((p, q) => p.x.CompareTo(q.x));
            float cursor = lo;
            foreach (Vector2 interval in sorted)
            {
                if (interval.x > cursor + Eps) return false;
                cursor = Mathf.Max(cursor, interval.y);
            }
            return cursor >= hi - Eps;
        }

        // True when the boxes together cover the whole rectangle.
        public static bool Covered(Area r, List<Area> boxes)
        {
            var xs = new List<float> { r.MinX, r.MaxX };
            var zs = new List<float> { r.MinZ, r.MaxZ };
            foreach (Area b in boxes)
            {
                if (b.MinX > r.MinX && b.MinX < r.MaxX) xs.Add(b.MinX);
                if (b.MaxX > r.MinX && b.MaxX < r.MaxX) xs.Add(b.MaxX);
                if (b.MinZ > r.MinZ && b.MinZ < r.MaxZ) zs.Add(b.MinZ);
                if (b.MaxZ > r.MinZ && b.MaxZ < r.MaxZ) zs.Add(b.MaxZ);
            }
            xs.Sort(); zs.Sort();
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < zs.Count; j++)
                {
                    if (xs[i + 1] - xs[i] <= Eps || zs[j + 1] - zs[j] <= Eps) continue;
                    float x = (xs[i] + xs[i + 1]) * .5f, z = (zs[j] + zs[j + 1]) * .5f;
                    bool inside = false;
                    foreach (Area b in boxes) inside |= b.Contains(x, z);
                    if (!inside) return false;
                }
            return true;
        }

        // The upper wings of a group: each box inset by Setback, extended by Setback across every side it shares completely
        // with the group (so it meets the neighbour's inset edge; of two boxes sharing a whole side each, the lower index
        // extends). The wings tile the group outline inset by Setback (in the square metric); any part of it they miss is
        // added as extra wings. Each wing, grown by Setback, must lie inside the union of the group's boxes.
        public static List<Area> WingAreas(FixedMap map, int[] group)
        {
            List<SharedEdge> edges = SharedEdges(map);
            var boxes = new List<Area>();
            foreach (int i in group) boxes.Add(Area.Of(map.Walls[i]));
            var wings = new List<Area>();
            foreach (int i in group)
            {
                Area box = Area.Of(map.Walls[i]), wing = box.Inset(Setback);
                for (int side = 0; side < 4; side++)
                {
                    if (!FullyShared(map, edges, i, side, out List<int> neighbours)) continue;
                    bool extend = true;
                    foreach (int other in neighbours)
                        if (other < i && FullyShared(map, edges, other, (side + 2) % 4, out _)) extend = false;
                    if (!extend) continue;
                    if (side == 0) wing.MinZ = box.MinZ - Setback;
                    else if (side == 1) wing.MaxX = box.MaxX + Setback;
                    else if (side == 2) wing.MaxZ = box.MaxZ + Setback;
                    else wing.MinX = box.MinX - Setback;
                }
                wings.Add(wing);
            }
            var xs = new List<float>();
            var zs = new List<float>();
            foreach (Area b in boxes)
            {
                xs.Add(b.MinX - Setback); xs.Add(b.MinX + Setback); xs.Add(b.MaxX - Setback); xs.Add(b.MaxX + Setback);
                zs.Add(b.MinZ - Setback); zs.Add(b.MinZ + Setback); zs.Add(b.MaxZ - Setback); zs.Add(b.MaxZ + Setback);
            }
            foreach (Area w in wings) { xs.Add(w.MinX); xs.Add(w.MaxX); zs.Add(w.MinZ); zs.Add(w.MaxZ); }
            xs.Sort(); zs.Sort();
            int added = 0;
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < zs.Count; j++)
                {
                    var cell = new Area(xs[i], zs[j], xs[i + 1], zs[j + 1]);
                    if (cell.Width <= Eps || cell.Depth <= Eps || !Covered(cell.Inset(-Setback), boxes)) continue;
                    float x = (cell.MinX + cell.MaxX) * .5f, z = (cell.MinZ + cell.MaxZ) * .5f;
                    bool inWing = false;
                    foreach (Area w in wings) inWing |= w.Contains(x, z);
                    if (!inWing) { wings.Add(cell); added++; }
                }
            if (added > 0) Debug.LogWarning("[WorldArtSetup] group " + string.Join(",", group) + ": " + added + " filler wing cells");
            foreach (Area wing in wings)
                if (!Covered(wing.Inset(-Setback), boxes)) throw new InvalidOperationException("Wing " + wing + " of group " + string.Join(",", group) + " comes closer than Setback to the group outline.");
            return wings;
        }

        // Whether side `side` of box `index` lies entirely on edges shared with other boxes; neighbours = those boxes.
        private static bool FullyShared(FixedMap map, List<SharedEdge> edges, int index, int side, out List<int> neighbours)
        {
            Area box = Area.Of(map.Walls[index]);
            neighbours = new List<int>();
            var intervals = new List<Vector2>();
            foreach (SharedEdge edge in edges)
            {
                if (edge.A != index && edge.B != index) continue;
                if (edge.AlongX != SideAlongX(side) || Mathf.Abs(edge.Line - SideLine(box, side)) > Eps) continue;
                intervals.Add(new Vector2(edge.Lo, edge.Hi));
                neighbours.Add(edge.A == index ? edge.B : edge.A);
            }
            return intervals.Count > 0 && Covers(intervals, SideLo(box, side), SideHi(box, side));
        }

        private sealed class WingPlan
        {
            public Area Area;
            public int Floors, Seed;
            public readonly byte[] Faces = new byte[4];
            public readonly bool[] Parapets = new bool[4];
            public int BillboardBox = -1;
            public string Billboard;
        }

        private sealed class GroupPlan
        {
            public int Ordinal;
            public int[] Members;
            public Color32 Colour, Accent;
            public Facade Facade;
            public string Model; // single-box groups: a fitted City 03 house
            public float ModelTop;
            public readonly List<WingPlan> Wings = new List<WingPlan>();
        }

        // Fitted on single-box buildings; neighbouring groups never get the same one.
        private static readonly string[] SingleModels = { "House 10", "House 3", "House 7", "House 1", "House 8", "House 5", "House 2" };
        private const float NeighbourGap = 10f; // groups whose outlines are closer than this face each other across a street

        // Geometry-keyed plan for every interior box: its group, the group's colour (never the colour of a neighbouring
        // group), facade, and either a fitted model (single box) or generated wings with stepped heights.
        private static Dictionary<int, GroupPlan> PlanGroups(FixedMap map)
        {
            List<int[]> groups = Groups(map);
            var plans = new List<GroupPlan>();
            var byBox = new Dictionary<int, GroupPlan>();
            var usedModels = new HashSet<string>();
            for (int g = 0; g < groups.Count; g++)
            {
                var near = new List<GroupPlan>();
                for (int h = 0; h < g; h++)
                    if (Gap(map, groups[g], groups[h]) < NeighbourGap) near.Add(plans[h]);
                var plan = new GroupPlan
                {
                    Ordinal = g, Members = groups[g], Facade = Facades[g % Facades.Length], Accent = AccentColours[g % AccentColours.Length],
                    Colour = WallColours[g * 3 % WallColours.Length] // kept only when every colour is taken nearby
                };
                for (int k = 0; k < WallColours.Length; k++)
                {
                    Color32 colour = WallColours[(g * 3 + k) % WallColours.Length];
                    if (near.Exists(p => Same(p.Colour, colour))) continue;
                    plan.Colour = colour;
                    break;
                }
                if (groups[g].Length == 1)
                {
                    string pick = null;
                    for (int pass = 0; pass < 2 && pick == null; pass++)
                        for (int k = 0; k < SingleModels.Length && pick == null; k++)
                        {
                            string model = SingleModels[(g + k) % SingleModels.Length];
                            if (near.Exists(p => p.Model == model) || (pass == 0 && usedModels.Contains(model))) continue;
                            pick = model;
                        }
                    plan.Model = pick ?? SingleModels[g % SingleModels.Length];
                    usedModels.Add(plan.Model);
                    plan.ModelTop = 9f + g % 3 * .8f;
                }
                else PlanWings(map, plan);
                plans.Add(plan);
                foreach (int i in groups[g]) byBox[i] = plan;
            }
            return byBox;
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        // Smallest distance between the boxes of two groups.
        private static float Gap(FixedMap map, int[] a, int[] b)
        {
            float best = float.MaxValue;
            foreach (int i in a)
                foreach (int j in b)
                {
                    Wall p = map.Walls[i], q = map.Walls[j];
                    float dx = Mathf.Max(0, Mathf.Max(p.MinX - q.MaxX, q.MinX - p.MaxX)), dz = Mathf.Max(0, Mathf.Max(p.MinZ - q.MaxZ, q.MinZ - p.MaxZ));
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                }
            return best;
        }

        private static void PlanWings(FixedMap map, GroupPlan plan)
        {
            List<Area> areas = WingAreas(map, plan.Members);
            for (int k = 0; k < areas.Count; k++)
                plan.Wings.Add(new WingPlan { Area = areas[k], Floors = (plan.Ordinal + k) % 2 == 0 ? 3 : 2, Seed = 7919 * (plan.Ordinal + 1) + 104729 * (k + 1) });
            foreach (WingPlan wing in plan.Wings)
                for (int side = 0; side < 4; side++)
                {
                    // A face is hidden when wings at least as tall cover it completely (the roofs then meet without a parapet).
                    var cover = new List<Vector2>();
                    float lo = SideLo(wing.Area, side), hi = SideHi(wing.Area, side), line = SideLine(wing.Area, side);
                    foreach (WingPlan other in plan.Wings)
                    {
                        if (other == wing || other.Floors < wing.Floors) continue;
                        if (Mathf.Abs(SideLine(other.Area, (side + 2) % 4) - line) > Eps) continue;
                        float a = Mathf.Max(lo, SideLo(other.Area, side)), b = Mathf.Min(hi, SideHi(other.Area, side));
                        if (b - a > Eps) cover.Add(new Vector2(a, b));
                    }
                    bool hidden = cover.Count > 0 && Covers(cover, lo, hi);
                    wing.Faces[side] = (byte)(hidden ? 0 : 2);
                    wing.Parapets[side] = !hidden;
                }
            // Every third group carries a roof billboard on a two-storey wing that lies inside one box.
            if (plan.Ordinal % 3 != 1) return;
            foreach (WingPlan wing in plan.Wings)
            {
                if (wing.Floors != 2) continue;
                Area sign = BillboardFootprint(wing.Area);
                foreach (int i in plan.Members)
                    if (Area.Of(map.Walls[i]).Encloses(sign)) { wing.BillboardBox = i; break; }
                if (wing.BillboardBox < 0) continue;
                wing.Billboard = plan.Ordinal % 2 == 0 ? "Roadside Billboard 1" : "Roadside Billboard 2";
                return;
            }
        }

        private static Area BillboardFootprint(Area a)
        {
            bool alongX = a.Width >= a.Depth;
            float width = Mathf.Min((alongX ? a.Width : a.Depth) - 1.2f, 5f);
            float mx = (a.MinX + a.MaxX) * .5f, mz = (a.MinZ + a.MaxZ) * .5f;
            return alongX ? new Area(mx - width * .5f, mz - .3f, mx + width * .5f, mz + .3f) : new Area(mx - .3f, mz - width * .5f, mx + .3f, mz + width * .5f);
        }

        // ---- buildings -----------------------------------------------------------------------------------------------------

        private static GameObject Building(int index, Wall wall, GroupPlan plan, List<SharedEdge> edges)
        {
            var random = new System.Random(7919 * (index + 1));
            float hx = (wall.MaxX - wall.MinX) * .5f, hz = (wall.MaxZ - wall.MinZ) * .5f;
            float cx = (wall.MinX + wall.MaxX) * .5f, cz = (wall.MinZ + wall.MaxZ) * .5f;
            bool border = plan == null;
            Color32 wallColour = border ? new Color32(96, 98, 102, 255) : plan.Colour;
            Color32 accent = border ? AccentColours[index % AccentColours.Length] : plan.Accent;
            var style = new Style
            {
                Wall = WallMaterial(wallColour),
                Accent = Lit("Accent " + Hex(accent), accent, .1f),
                Base = WallMaterial(new Color32((byte)(wallColour.r * .62f), (byte)(wallColour.g * .62f), (byte)(wallColour.b * .62f), 255)),
                Service = border
            };
            var root = new GameObject("Building " + index);
            var podium = new MeshBuilder();
            Podium(podium, -hx, -hz, hx, hz, style, random, SharedSpans(wall, index, edges));
            MeshObject("Podium", root.transform, podium, "Building " + index + " podium");
            if (border) return SavePrefab(root, "Building " + index);
            var box = new Area(-hx, -hz, hx, hz);
            if (plan.Model != null)
            {
                FitUpper(Model(plan.Model), root.transform, box.Inset(Setback), plan.ModelTop, plan.Ordinal % 2 == 1, "Upper");
                return SavePrefab(root, "Building " + index);
            }
            var upperStyle = new Style { Wall = style.Wall, Base = style.Base, Accent = style.Accent };
            var storeys = new MeshBuilder { HasClip = true, Clip = box };
            foreach (WingPlan wing in plan.Wings)
            {
                Area local = wing.Area.Offset(-cx, -cz);
                if (!local.Overlaps(box)) continue;
                // The same seed in every box the wing spans: each box rebuilds the identical wing and keeps its own part.
                var wingRandom = new System.Random(wing.Seed);
                Storeys(storeys, local, wing.Floors, Storey, upperStyle, plan.Facade, wingRandom, wing.Faces, wing.Parapets);
                RoofProps(storeys, local, Wall + wing.Floors * Storey, wing.Floors, upperStyle, wingRandom);
                if (wing.BillboardBox == index) Billboard(root.transform, wing, local);
            }
            if (!storeys.Empty) MeshObject("Upper", root.transform, storeys, "Building " + index + " storeys");
            return SavePrefab(root, "Building " + index);
        }

        // The spans (u along each face, Faces order) this box shares with other boxes of its group.
        private static List<Vector2>[] SharedSpans(Wall wall, int index, List<SharedEdge> edges)
        {
            var spans = new List<Vector2>[4];
            for (int k = 0; k < 4; k++) spans[k] = new List<Vector2>();
            Area a = Area.Of(wall);
            foreach (SharedEdge edge in edges)
            {
                if (edge.A != index && edge.B != index) continue;
                for (int side = 0; side < 4; side++)
                {
                    if (SideAlongX(side) != edge.AlongX || Mathf.Abs(SideLine(a, side) - edge.Line) > Eps) continue;
                    switch (side)
                    {
                        case 0: spans[0].Add(new Vector2(edge.Lo - a.MinX, edge.Hi - a.MinX)); break;
                        case 1: spans[1].Add(new Vector2(edge.Lo - a.MinZ, edge.Hi - a.MinZ)); break;
                        case 2: spans[2].Add(new Vector2(a.MaxX - edge.Hi, a.MaxX - edge.Lo)); break;
                        default: spans[3].Add(new Vector2(a.MaxZ - edge.Hi, a.MaxZ - edge.Lo)); break;
                    }
                }
            }
            return spans;
        }

        private static void Billboard(Transform root, WingPlan wing, Area local)
        {
            float roof = Wall + wing.Floors * Storey;
            Area sign = BillboardFootprint(local);
            bool alongX = sign.Width >= sign.Depth;
            float width = alongX ? sign.Width : sign.Depth;
            float height = Mathf.Min(width * .55f, MaxBuildingHeight - .1f - roof);
            Fit(Model(wing.Billboard), root, new Vector3(sign.MinX, roof, sign.MinZ), new Vector3(sign.MaxX, roof + height, sign.MaxZ), alongX ? 0 : 90, "Roof sign");
        }

        // ---- obstacles -----------------------------------------------------------------------------------------------------

        private static GameObject ObstaclePiece(int index, Obstacle obstacle)
        {
            float hx = (obstacle.MaxX - obstacle.MinX) * .5f, hz = (obstacle.MaxZ - obstacle.MinZ) * .5f;
            var root = new GameObject("Obstacle " + index);
            if (hx > hz + .5f || hz > hx + .5f) ParkedCar(root.transform, hx, hz, index);
            else
                switch (index % 3)
                {
                    case 0: Dumpsters(root.transform, hx, hz); break;
                    case 1: Crates(root.transform, hx, hz, index); break;
                    default: WorksPen(root.transform, hx, hz); break;
                }
            return SavePrefab(root, "Obstacle " + index);
        }

        private static void Dumpsters(Transform root, float hx, float hz)
        {
            GameObject dumpster = Model("Dumpster 2");
            Fit(dumpster, root, new Vector3(-hx, 0, -hz), new Vector3(hx, 1.3f, 0), 0, "Dumpster A");
            Fit(dumpster, root, new Vector3(-hx, 0, 0), new Vector3(hx, 1.3f, hz), 180, "Dumpster B");
        }

        private static void Crates(Transform root, float hx, float hz, int index)
        {
            GameObject box = Model("Box 1");
            float[] heights = { .95f, .82f, 1.0f, .88f };
            for (int i = 0; i < 4; i++)
            {
                float x0 = i % 2 == 0 ? -hx : 0, z0 = i < 2 ? -hz : 0;
                Fit(box, root, new Vector3(x0, 0, z0), new Vector3(x0 + hx, heights[(i + index) % 4], z0 + hz), 90 * ((i + index) % 4), "Crate " + i);
            }
            float under = heights[(2 + index) % 4];
            Fit(Model("Box 2"), root, new Vector3(-hx * .9f, under, .1f), new Vector3(-.05f, under + .42f, hz * .85f), 0, "Crate top");
        }

        private static void WorksPen(Transform root, float hx, float hz)
        {
            GameObject fence = Model("Road Fence 1");
            const float t = .19f, h = 1f;
            Fit(fence, root, new Vector3(-hx, 0, -hz), new Vector3(-hx + t, h, hz), 0, "Fence W");
            Fit(fence, root, new Vector3(hx - t, 0, -hz), new Vector3(hx, h, hz), 0, "Fence E");
            Fit(fence, root, new Vector3(-hx + t, 0, -hz), new Vector3(hx - t, h, -hz + t), 90, "Fence S");
            Fit(fence, root, new Vector3(-hx + t, 0, hz - t), new Vector3(hx - t, h, hz), 90, "Fence N");
            Fit(Model("Manhole Covers 1"), root, new Vector3(.05f, 0, -.2f), new Vector3(.75f, .08f, .6f), 0, "Opened cover");
        }

        private static void ParkedCar(Transform root, float hx, float hz, int index)
        {
            bool alongX = hx > hz;
            var pad = new MeshBuilder();
            pad.Box(Palette.Kerb, new Vector3(-hx, 0, -hz), new Vector3(hx, .08f, hz), Palette.Paving);
            MeshObject("Parking pad", root, pad, "Parking pad " + index, false); // one mesh per obstacle: pad sizes differ
            string model = index % 2 == 0 ? "Red Car" : "Gray Car";
            int yaw = model == "Red Car" ? (alongX ? 0 : 90) : (alongX ? 90 : 0);
            float length = (alongX ? hx : hz) * 2 - .12f;
            var probe = (GameObject)Object.Instantiate(Model(model));
            probe.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var frame = new GameObject("frame").transform;
            Bounds b = BoundsIn(probe, frame);
            Object.DestroyImmediate(probe); Object.DestroyImmediate(frame.gameObject);
            float scale = Mathf.Min(length / (alongX ? b.size.x : b.size.z), (MaxObstacleHeight - .1f) / b.size.y);
            Vector3 size = b.size * scale;
            Fit(Model(model), root, new Vector3(-size.x * .5f, .08f, -size.z * .5f), new Vector3(size.x * .5f, .08f + size.y, size.z * .5f), yaw, "Car");
        }

        // ---- ground --------------------------------------------------------------------------------------------------------

        private enum Cell : byte { Road, Pavement, PlazaA, PlazaB, Building }

        private static GameObject Ground(FixedMap map)
        {
            const float step = .5f, min = -40, max = FixedMap.Size + 40;
            int n = Mathf.RoundToInt((max - min) / step);
            var cells = new Cell[n, n];
            for (int ix = 0; ix < n; ix++)
                for (int iz = 0; iz < n; iz++)
                {
                    float x = min + (ix + .5f) * step, z = min + (iz + .5f) * step;
                    cells[ix, iz] = Classify(map, x, z, ix, iz);
                }
            var mb = new MeshBuilder();
            // Surfaces: one quad per run of equal cells in a row.
            for (int iz = 0; iz < n; iz++)
            {
                int start = 0;
                for (int ix = 1; ix <= n; ix++)
                {
                    if (ix < n && cells[ix, iz] == cells[start, iz]) continue;
                    Cell kind = cells[start, iz];
                    if (kind != Cell.Building)
                    {
                        float y = kind == Cell.Road ? 0 : Pavement;
                        mb.Top(Surface(kind), min + start * step, min + iz * step, min + ix * step, min + (iz + 1) * step, y);
                    }
                    start = ix;
                }
            }
            // Kerbs: vertical faces where raised paving meets the road.
            for (int ix = 0; ix < n; ix++)
                for (int iz = 0; iz < n; iz++)
                {
                    if (cells[ix, iz] != Cell.Road) continue;
                    float x0 = min + ix * step, z0 = min + iz * step, x1 = x0 + step, z1 = z0 + step;
                    if (ix > 0 && Raised(cells[ix - 1, iz])) Kerb(mb, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1));
                    if (ix + 1 < n && Raised(cells[ix + 1, iz])) Kerb(mb, new Vector3(x1, 0, z1), new Vector3(x1, 0, z0));
                    if (iz > 0 && Raised(cells[ix, iz - 1])) Kerb(mb, new Vector3(x1, 0, z0), new Vector3(x0, 0, z0));
                    if (iz + 1 < n && Raised(cells[ix, iz + 1])) Kerb(mb, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1));
                }
            Markings(mb, map);
            var root = new GameObject("Ground");
            MeshObject("Surface", root.transform, mb, "Ground", false);
            return SavePrefab(root, "Ground");
        }

        private static Material Surface(Cell kind)
        {
            switch (kind)
            {
                case Cell.Pavement: return Palette.Paving;
                case Cell.PlazaA: return Palette.PlazaA;
                case Cell.PlazaB: return Palette.PlazaB;
                default: return Palette.Asphalt;
            }
        }

        private static bool Raised(Cell kind) => kind == Cell.Pavement || kind == Cell.PlazaA || kind == Cell.PlazaB;

        // The kerb face between a road cell and raised paving: a, b along the edge so the face looks toward the road.
        private static void Kerb(MeshBuilder mb, Vector3 a, Vector3 b)
        {
            mb.Quad(Palette.Kerb, a, a + Vector3.up * Pavement, b + Vector3.up * Pavement, b);
        }

        private static Cell Classify(FixedMap map, float x, float z, int ix, int iz)
        {
            foreach (Wall wall in map.Walls)
                if (x > wall.MinX && x < wall.MaxX && z > wall.MinZ && z < wall.MaxZ) return Cell.Building;
            foreach (Wall plaza in map.Plazas)
                if (x > plaza.MinX && x < plaza.MaxX && z > plaza.MinZ && z < plaza.MaxZ)
                    return ((ix / 2) + (iz / 2)) % 2 == 0 ? Cell.PlazaA : Cell.PlazaB;
            if (x < 0 || z < 0 || x > FixedMap.Size || z > FixedMap.Size) return Cell.Pavement;
            float nearest = float.MaxValue;
            foreach (Wall wall in map.Walls)
            {
                float dx = Mathf.Max(wall.MinX - x, 0, x - wall.MaxX), dz = Mathf.Max(wall.MinZ - z, 0, z - wall.MaxZ);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
            }
            foreach (MapPoint exit in map.Exits)
                if ((x - exit.X) * (x - exit.X) + (z - exit.Z) * (z - exit.Z) < 2.6f * 2.6f) return Cell.Road;
            return nearest <= 2f ? Cell.Pavement : Cell.Road;
        }

        // Flat road paint, 0.01 m above the surface: dashed centre lines on the open streets.
        private static void Markings(MeshBuilder mb, FixedMap map)
        {
            const float y = .01f, w = .07f;
            for (float z = 9; z < 74; z += 4) Dash(mb, map, 6 - w, z, 6 + w, z + 2, y);
            for (float z = 4; z < 66; z += 4) Dash(mb, map, 73 - w, z, 73 + w, z + 2, y);
            for (float x = 9; x < 41; x += 4) Dash(mb, map, x, 6 - w, x + 2, 6 + w, y);
            // Zebra crossings at the plaza mouths.
            Zebra(mb, map, 40.5f, 29.2f, 55.5f, 31.6f);
            Zebra(mb, map, 40.5f, 44.4f, 55.5f, 46.8f);
        }

        private static void Dash(MeshBuilder mb, FixedMap map, float x0, float z0, float x1, float z1, float y)
        {
            if (!map.CanStand((x0 + x1) * .5f, (z0 + z1) * .5f, .1f)) return;
            mb.Top(Palette.Marking, x0, z0, x1, z1, y);
        }

        private static void Zebra(MeshBuilder mb, FixedMap map, float x0, float z0, float x1, float z1)
        {
            for (float x = x0; x + .5f <= x1; x += 1f)
                Dash(mb, map, x, z0, x + .5f, z1, .01f);
        }

        // ---- skyline -------------------------------------------------------------------------------------------------------

        private static readonly string[] SkylineModels = { "House 8", "House 10", "House 2", "House 3", "House 7", "House 4", "House 5", "House 10", "House 8", "House 6", "House 2", "House 3", "House 1", "House 7" };
        // The skyline ring outside the border walls (outer faces at -1 and Size + 1): a continuous row of low generated
        // buildings fills the strip from BackdropClearance to BackdropStrip outside them, all the way round; the house models
        // stand shoulder to shoulder on the strip's outer line, and any leftover length gets a generated filler block.
        public const float BackdropClearance = .05f, BackdropStrip = 2.5f;

        private static GameObject Backdrop()
        {
            var root = new GameObject("Backdrop");
            const float lo = -1 - BackdropStrip, hi = FixedMap.Size + 1 + BackdropStrip;
            const float inLo = -1 - BackdropClearance, inHi = FixedMap.Size + 1 + BackdropClearance;
            // South and north rows span the corners, west and east fill between them.
            Terrace(root.transform, "S", new Area(lo, lo, hi, inLo), Vector3.forward, 11);
            Terrace(root.transform, "N", new Area(lo, inHi, hi, hi), Vector3.back, 23);
            Terrace(root.transform, "W", new Area(lo, inLo, inLo, inHi), Vector3.right, 37);
            Terrace(root.transform, "E", new Area(inHi, inLo, hi, inHi), Vector3.left, 41);
            int pick = 0;
            Row(root.transform, ref pick, "S", Vector3.right, Vector3.back, new Vector3(-16, 0, lo), 112, 6, 0);
            Row(root.transform, ref pick, "N", Vector3.left, Vector3.forward, new Vector3(FixedMap.Size + 16, 0, hi), 112, 6, 180);
            Row(root.transform, ref pick, "W", Vector3.back, Vector3.left, new Vector3(lo, 0, hi), hi - lo, 0, 270);
            Row(root.transform, ref pick, "E", Vector3.forward, Vector3.right, new Vector3(hi, 0, lo), hi - lo, 0, 90);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return SavePrefab(root, "Backdrop");
        }

        // Index (Faces order) of the face whose outward normal is `outward`.
        private static int FaceIndex(Vector3 outward) => outward.z < -.5f ? 0 : outward.x > .5f ? 1 : outward.z > .5f ? 2 : 3;

        // The front row: segments of 7 to 12 m, one or two storeys above a plain base, windows on the arena side only.
        private static void Terrace(Transform root, string side, Area strip, Vector3 toArena, int seed)
        {
            var random = new System.Random(seed);
            bool alongX = strip.Width > strip.Depth;
            float start = alongX ? strip.MinX : strip.MinZ, end = alongX ? strip.MaxX : strip.MaxZ;
            var cuts = new List<float> { start };
            float cursor = start;
            while (end - cursor > 16) { cursor += 7 + (float)random.NextDouble() * 5; cuts.Add(cursor); }
            if (end - cursor > 12) cuts.Add((cursor + end) * .5f);
            cuts.Add(end);
            int front = FaceIndex(toArena);
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                Area piece = alongX ? new Area(cuts[i], strip.MinZ, cuts[i + 1], strip.MaxZ) : new Area(strip.MinX, cuts[i], strip.MaxX, cuts[i + 1]);
                Color32 colour = WallColours[(seed + i * 3) % WallColours.Length];
                var style = new Style
                {
                    Wall = WallMaterial(colour),
                    Base = WallMaterial(new Color32((byte)(colour.r * .62f), (byte)(colour.g * .62f), (byte)(colour.b * .62f), 255)),
                    Accent = Lit("Accent " + Hex(AccentColours[i % AccentColours.Length]), AccentColours[i % AccentColours.Length], .1f)
                };
                var mb = new MeshBuilder();
                foreach (Face face in Faces(piece.MinX, piece.MinZ, piece.MaxX, piece.MaxZ))
                    mb.Panel(style.Base, face.Origin, face.Right, face.Out, 0, face.Length, 0, Wall, 0);
                var faces = new byte[] { 1, 1, 1, 1 };
                faces[front] = 2;
                int floors = random.NextDouble() < .35 ? 2 : 1;
                Storeys(mb, piece, floors, 2.9f, style, Facades[i % Facades.Length], random, faces, new[] { true, true, true, true });
                MeshObject("Terrace " + side + " " + i, root, mb, "Backdrop terrace " + side + " " + i, false);
            }
        }

        // The back row: houses from start along `along`, shoulder to shoulder, their arena-facing side on the line through
        // start, extending toward `away`. A house that would overrun the row (plus overshoot) is skipped for the next one that
        // fits; when none fits, a generated filler block takes the exact remaining length.
        private static void Row(Transform root, ref int pick, string side, Vector3 along, Vector3 away, Vector3 start, float length, float overshoot, int yaw)
        {
            float cursor = 0;
            int fillers = 0;
            while (cursor < length - .05f)
            {
                bool placed = false;
                for (int attempt = 0; attempt < SkylineModels.Length && !placed; attempt++)
                {
                    string name = SkylineModels[pick % SkylineModels.Length];
                    pick++;
                    GameObject model = Model(name);
                    var holder = new GameObject(name).transform;
                    holder.SetParent(root, false);
                    var item = (GameObject)Object.Instantiate(model, holder);
                    item.name = name;
                    item.transform.localRotation = Quaternion.Euler(0, yaw + (pick % 2) * 180, 0);
                    float scale = .9f + (pick % 3) * .08f;
                    item.transform.localScale = Vector3.one * scale;
                    Bounds b = BoundsIn(item, root);
                    float width = Mathf.Abs(Vector3.Dot(b.size, along));
                    if (cursor + width > length + overshoot) { Object.DestroyImmediate(holder.gameObject); continue; }
                    // Move so that the bounds start at the cursor along the row and touch the line on the arena side.
                    Vector3 alongMin = Vector3.Dot(along, Vector3.one) > 0 ? b.min : b.max;
                    Vector3 nearSide = Vector3.Dot(away, Vector3.one) > 0 ? b.min : b.max;
                    Vector3 target = start + along * cursor;
                    Vector3 offset = Vector3.Scale(target - alongMin, Abs(along)) + Vector3.Scale(target - nearSide, Abs(away));
                    offset.y = -b.min.y;
                    holder.localPosition = offset;
                    cursor += width;
                    placed = true;
                }
                if (placed) continue;
                Filler(root, side + " " + fillers++, along, away, start + along * cursor, length - cursor);
                cursor = length;
            }
        }

        // A generated three-storey block, 7 m deep, windows toward the arena.
        private static void Filler(Transform root, string label, Vector3 along, Vector3 away, Vector3 from, float length)
        {
            Vector3 to = from + along * length, back = from + away * 7;
            var area = new Area(Mathf.Min(from.x, Mathf.Min(to.x, back.x)), Mathf.Min(from.z, Mathf.Min(to.z, back.z)),
                Mathf.Max(from.x, Mathf.Max(to.x, back.x)), Mathf.Max(from.z, Mathf.Max(to.z, back.z)));
            Color32 colour = WallColours[(label.Length * 5 + (int)length) % WallColours.Length];
            var style = new Style
            {
                Wall = WallMaterial(colour),
                Base = WallMaterial(new Color32((byte)(colour.r * .62f), (byte)(colour.g * .62f), (byte)(colour.b * .62f), 255)),
                Accent = Lit("Accent " + Hex(AccentColours[0]), AccentColours[0], .1f)
            };
            var mb = new MeshBuilder();
            foreach (Face face in Faces(area.MinX, area.MinZ, area.MaxX, area.MaxZ))
                mb.Panel(style.Base, face.Origin, face.Right, face.Out, 0, face.Length, 0, Wall, 0);
            var faces = new byte[] { 1, 1, 1, 1 };
            faces[FaceIndex(-away)] = 2;
            Storeys(mb, area, 3, 2.9f, style, Facades[0], new System.Random(Seed(label)), faces, new[] { true, true, true, true });
            MeshObject("Filler " + label, root, mb, "Backdrop filler " + label, false);
        }

        // A stable seed from a label (string.GetHashCode is not stable across runs).
        private static int Seed(string label)
        {
            int seed = 17;
            foreach (char c in label) seed = seed * 31 + c;
            return seed & 0x7fffffff;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ---- terminal and Exit marker --------------------------------------------------------------------------------------

        // A street utility cabinet with a decoder wired to a charge on top and a sapper kit at its foot. 1.2 x 1.2 x 1.05 m.
        private static GameObject Terminal()
        {
            var root = new GameObject("Terminal");
            var cabinet = new MeshBuilder();
            const float back = .52f, front = -.28f, top = .72f;
            cabinet.Box(Palette.Steel, new Vector3(-.55f, 0, front), new Vector3(.55f, top, back), Palette.SteelDark);
            cabinet.Box(Palette.SteelDark, new Vector3(-.58f, 0, front - .03f), new Vector3(.58f, .1f, back + .03f));
            cabinet.Panel(Palette.SteelDark, new Vector3(-.55f, 0, front), Vector3.right, Vector3.back, .08f, .52f, .18f, .62f, -.012f);
            cabinet.Panel(Palette.SteelDark, new Vector3(-.55f, 0, front), Vector3.right, Vector3.back, .58f, 1.02f, .18f, .62f, -.012f);
            MeshObject("Cabinet", root.transform, cabinet, "Terminal cabinet");
            var screen = new MeshBuilder();
            screen.Box(Palette.Frame, new Vector3(-.5f, top, front + .05f), new Vector3(.08f, top + .04f, back - .05f), Palette.Frame);
            MeshObject("Screen frame", root.transform, screen, "Terminal screen frame");
            var panel = new MeshBuilder();
            panel.Top(Palette.Steel, -.46f, front + .09f, .04f, back - .09f, top + .045f);
            GameObject screenPanel = MeshObject(WorldArt.ScreenName, root.transform, panel, "Terminal screen");
            screenPanel.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Device(root.transform, "ExplosiveDeviceC4", new Vector3(.32f, top, .26f), Quaternion.Euler(90, 90, 0), 1.25f);
            Device(root.transform, "Decoder", new Vector3(.3f, top, -.12f), Quaternion.Euler(90, 0, 0), 1.6f);
            Device(root.transform, "SapperKit", new Vector3(.2f, 0, front - .03f - .17f), Quaternion.Euler(0, 180, 0), 1f);
            return SavePrefab(root, "Terminal");
        }

        private static void Device(Transform root, string name, Vector3 position, Quaternion rotation, float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DeviceSource + name + ".prefab");
            if (prefab == null) throw new InvalidOperationException("Missing device: " + name);
            var item = (GameObject)Object.Instantiate(prefab, root);
            item.name = name;
            item.transform.localRotation = rotation;
            item.transform.localScale = Vector3.one * scale;
            item.transform.localPosition = Vector3.zero;
            Bounds b = BoundsIn(item, root);
            item.transform.localPosition = position - new Vector3(b.center.x, b.min.y, b.center.z);
            // Copies of the vendor materials: the pipeline's material validation must never rewrite the vendor files.
            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                Material[] used = renderer.sharedMaterials;
                for (int i = 0; i < used.Length; i++) used[i] = DeviceMaterial(used[i]);
                renderer.sharedMaterials = used;
            }
            foreach (Collider collider in item.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (MonoBehaviour behaviour in item.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
            foreach (Rigidbody body in item.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        }

        private static Material DeviceMaterial(Material source)
        {
            if (source == null) return null;
            string path = Root + "/Materials/Device " + source.name + ".mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(source);
                AssetDatabase.CreateAsset(copy, path);
            }
            // U4i: the copies are toon too; base map, colour and normal map come from the vendor material.
            Shader toon = ToonShading.Find();
            if (toon != null && copy.shader != toon)
            {
                copy.shader = toon;
                copy.shaderKeywords = new string[0];
                if (source.HasProperty("_BaseMap")) copy.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                if (source.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                if (source.HasProperty("_BumpMap")) copy.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
            }
            if (toon != null) ToonShading.Configure(copy, ToonShading.Kind.World);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        // The Hovl zone marker, scaled so its ring matches the Exit radius; colour is set per role at runtime.
        private static GameObject ExitMarker()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MarkerSource);
            if (prefab == null) throw new InvalidOperationException("Missing marker: " + MarkerSource);
            var root = new GameObject("Exit marker");
            var item = (GameObject)Object.Instantiate(prefab, root.transform);
            item.name = prefab.name;
            item.transform.localPosition = Vector3.zero;
            foreach (ParticleSystem system in item.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = true; // a steady marker while the Exit is open; WorldProps starts and clears it
                main.playOnAwake = false;
            }
            // The vendor material multiplies the particle colour by an HDR emission of 18, which burns the role tint to
            // white. A copy with a low emission keeps the tint readable (and the vendor file untouched).
            foreach (ParticleSystemRenderer renderer in item.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material[] used = renderer.sharedMaterials;
                for (int i = 0; i < used.Length; i++) used[i] = MarkerMaterial(used[i]);
                renderer.sharedMaterials = used;
                if (renderer.trailMaterial != null) renderer.trailMaterial = MarkerMaterial(renderer.trailMaterial);
            }
            item.transform.localScale = Vector3.one * MarkerScale;
            return SavePrefab(root, "Exit marker");
        }

        public const float MarkerEmission = 1.4f;

        private static Material MarkerMaterial(Material source)
        {
            if (source == null) return null;
            if (source.name.StartsWith("Exit marker ")) return source;
            string path = Root + "/Materials/Exit marker " + source.name + ".mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(source);
                AssetDatabase.CreateAsset(copy, path);
            }
            if (copy.HasProperty("_Emission")) copy.SetFloat("_Emission", MarkerEmission);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        public const float MarkerScale = 2.0f / 2.13f;
    }
}
