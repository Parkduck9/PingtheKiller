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
    // the podium mesh; anything higher stands at least Setback inside the box, so the Survivor camera (which can rise above a
    // wall box up to about 0.8 m inside its footprint) never ends up inside a visual; obstacles fill their box and stay below
    // MaxObstacleHeight; nothing stands in the walkable area except the flat ground (at most MaxFloorHeight).
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
            catalog.Walls = new WorldArt.Piece[map.Walls.Count];
            for (int i = 0; i < map.Walls.Count; i++)
            {
                Wall wall = map.Walls[i];
                catalog.Walls[i] = new WorldArt.Piece { Prefab = Building(i, wall), Box = new Vector4(wall.MinX, wall.MinZ, wall.MaxX, wall.MaxZ) };
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
            Debug.Log("[WorldArtSetup] WorldArt catalog ready: " + map.Walls.Count + " buildings, " + map.Obstacles.Count + " obstacles.");
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

        // Fits a model into a footprint with its natural height scaled by the mean footprint scale, capped at top.
        private static Transform FitUpper(GameObject model, Transform parent, float minX, float minZ, float maxX, float maxZ, int yaw, float top, string label)
        {
            var probe = (GameObject)Object.Instantiate(model);
            probe.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var frame = new GameObject("frame").transform;
            Bounds b = BoundsIn(probe, frame);
            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(frame.gameObject);
            float scale = ((maxX - minX) / b.size.x + (maxZ - minZ) / b.size.z) * .5f;
            float height = Mathf.Min(b.size.y * scale, top);
            return Fit(model, parent, new Vector3(minX, 0, minZ), new Vector3(maxX, height, maxZ), yaw, label);
        }

        // ---- generated meshes ----------------------------------------------------------------------------------------------

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Material> materials = new List<Material>();
            private readonly List<List<int>> triangles = new List<List<int>>();

            // a, b, c, d: bottom-left, top-left, top-right, bottom-right seen from the front.
            public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int sub = materials.IndexOf(material);
                if (sub < 0) { materials.Add(material); triangles.Add(new List<int>()); sub = materials.Count - 1; }
                Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                List<int> t = triangles[sub];
                t.Add(start); t.Add(start + 1); t.Add(start + 2);
                t.Add(start); t.Add(start + 2); t.Add(start + 3);
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
        private static void Podium(MeshBuilder mb, float minX, float minZ, float maxX, float maxZ, Style style, System.Random random)
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
            // Parapet on the roof edge, inside the box.
            const float parapet = .18f, parapetTop = Wall + .14f;
            foreach (Face face in Faces(minX, minZ, maxX, maxZ))
            {
                Vector3 inner = face.Origin - face.Out * parapet + face.Right * parapet;
                float length = face.Length - 2 * parapet;
                if (length <= 0) continue;
                mb.Panel(style.Base, face.Origin, face.Right, face.Out, 0, face.Length, Wall, parapetTop, 0);
                mb.Panel(style.Base, inner + Vector3.up * Wall, -face.Right, -face.Out, -length, 0, 0, parapetTop - Wall, 0);
                Vector3 a = face.Origin + Vector3.up * parapetTop, b = a + face.Right * face.Length;
                mb.Quad(style.Base, a, a - face.Out * parapet + face.Right * parapet, b - face.Out * parapet - face.Right * parapet, b);
            }
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

        // Upper storeys of a generated building, standing on the podium roof: window grid, roof and parapet.
        private static void Storeys(MeshBuilder mb, float minX, float minZ, float maxX, float maxZ, int floors, Style style, System.Random random)
        {
            const float storey = 2.9f;
            float top = Wall + floors * storey;
            foreach (Face face in Faces(minX, minZ, maxX, maxZ))
            {
                Vector3 origin = face.Origin + Vector3.up * Wall;
                int windows = Mathf.Max(1, Mathf.FloorToInt((face.Length - .6f) / 2.3f));
                float pitch = (face.Length - .6f) / windows;
                for (int f = 0; f < floors; f++)
                {
                    float y0 = f * storey, sill = y0 + .9f, head = y0 + 2.25f, y1 = y0 + storey;
                    mb.Panel(style.Wall, origin, face.Right, face.Out, 0, face.Length, y0, sill, 0);
                    mb.Panel(style.Wall, origin, face.Right, face.Out, 0, face.Length, head, y1, 0);
                    float u = 0;
                    for (int w = 0; w < windows; w++)
                    {
                        float w0 = .3f + w * pitch + (pitch - 1.1f) * .5f, w1 = w0 + 1.1f;
                        mb.Panel(style.Wall, origin, face.Right, face.Out, u, w0, sill, head, 0);
                        double r = random.NextDouble();
                        Material glass = r < .3 ? Palette.GlassWarm : r < .42 ? Palette.GlassCool : Palette.Glass;
                        Grid(mb, new Face { Origin = origin, Right = face.Right, Out = face.Out, Length = face.Length }, glass, Palette.Frame, w0, w1, sill, head, .06f, 2, 0);
                        mb.Reveal(Palette.Frame, origin, face.Right, face.Out, w0, w1, sill, head, Recess);
                        u = w1;
                    }
                    mb.Panel(style.Wall, origin, face.Right, face.Out, u, face.Length, sill, head, 0);
                }
                mb.Panel(style.Base, origin, face.Right, face.Out, 0, face.Length, floors * storey, floors * storey + .3f, 0);
            }
            mb.Top(Palette.Roof, minX, minZ, maxX, maxZ, top);
            foreach (Face face in Faces(minX, minZ, maxX, maxZ))
            {
                Vector3 inner = face.Origin - face.Out * .2f + face.Right * .2f + Vector3.up * top;
                float length = face.Length - .4f;
                if (length <= 0) continue;
                mb.Panel(style.Base, inner, -face.Right, -face.Out, -length, 0, 0, .3f, 0);
                Vector3 a = face.Origin + Vector3.up * (top + .3f), b = a + face.Right * face.Length;
                mb.Quad(style.Base, a, a - face.Out * .2f + face.Right * .2f, b - face.Out * .2f - face.Right * .2f, b);
            }
        }

        // ---- buildings -----------------------------------------------------------------------------------------------------

        private struct Upper
        {
            public string Model;
            public int Yaw;
            public float Top;
            public int Floors; // generated storeys when Model is null
            public string Roof; // optional model on a generated roof (a billboard)
        }

        // Per FixedMap wall index: the upper part. Border walls (0 to 3) have none; the skyline stands behind them.
        private static Upper UpperFor(int index)
        {
            switch (index)
            {
                case 4: return new Upper { Model = "House 10", Yaw = 0, Top = 10.2f };
                case 5: return new Upper { Model = "House 1", Yaw = 0, Top = 10f };
                case 6: return new Upper { Floors = 1, Roof = "Roadside Billboard 1" };
                case 7: return new Upper { Model = "House 2", Yaw = 180, Top = 8f };
                case 8: return new Upper { Model = "House 10", Yaw = 180, Top = 9.4f };
                case 9: return new Upper { Floors = 1 };
                case 10: return new Upper { Model = "House 3", Yaw = 90, Top = 8f };
                case 11: return new Upper { Model = "House 5", Yaw = 0, Top = 7.5f };
                case 12: return new Upper { Model = "House 8", Yaw = 90, Top = 8.5f };
                case 13: return new Upper { Model = "House 7", Yaw = 0, Top = 10.5f };
                case 14: return new Upper { Model = "House 3", Yaw = 270, Top = 9f };
                case 15: return new Upper { Floors = 1, Roof = "Roadside Billboard 2" };
                default: return new Upper();
            }
        }

        private static GameObject Building(int index, Wall wall)
        {
            var random = new System.Random(7919 * (index + 1));
            float hx = (wall.MaxX - wall.MinX) * .5f, hz = (wall.MaxZ - wall.MinZ) * .5f;
            bool border = index < 4;
            var style = new Style
            {
                Wall = WallMaterial(border ? new Color32(96, 98, 102, 255) : WallColours[index % WallColours.Length]),
                Accent = Lit("Accent " + Hex(AccentColours[index % AccentColours.Length]), AccentColours[index % AccentColours.Length], .1f),
                Service = border
            };
            Color32 wallColour = border ? new Color32(96, 98, 102, 255) : WallColours[index % WallColours.Length];
            style.Base = WallMaterial(new Color32((byte)(wallColour.r * .62f), (byte)(wallColour.g * .62f), (byte)(wallColour.b * .62f), 255));
            var root = new GameObject("Building " + index);
            var podium = new MeshBuilder();
            Podium(podium, -hx, -hz, hx, hz, style, random);
            MeshObject("Podium", root.transform, podium, "Building " + index + " podium");
            Upper upper = UpperFor(index);
            float minX = -hx + Setback, minZ = -hz + Setback, maxX = hx - Setback, maxZ = hz - Setback;
            if (upper.Model != null)
            {
                FitUpper(Model(upper.Model), root.transform, minX, minZ, maxX, maxZ, upper.Yaw, upper.Top, "Upper");
            }
            else if (upper.Floors > 0)
            {
                var storeys = new MeshBuilder();
                var upperStyle = new Style { Wall = style.Wall, Base = style.Base, Accent = style.Accent };
                Storeys(storeys, minX, minZ, maxX, maxZ, upper.Floors, upperStyle, random);
                MeshObject("Upper", root.transform, storeys, "Building " + index + " storeys");
                if (upper.Roof != null)
                {
                    GameObject sign = Model(upper.Roof);
                    float roofTop = Wall + upper.Floors * 2.9f;
                    float width = Mathf.Min(maxX - minX - .6f, 7f);
                    float cx = (minX + maxX) * .5f, cz = (minZ + maxZ) * .5f;
                    FitUpperAt(sign, root.transform, new Vector3(cx - width * .5f, roofTop, cz - .3f), new Vector3(cx + width * .5f, roofTop + width * .55f, cz + .3f));
                }
            }
            return SavePrefab(root, "Building " + index);
        }

        private static void FitUpperAt(GameObject model, Transform parent, Vector3 min, Vector3 max)
        {
            Fit(model, parent, min, max, 0, "Roof sign");
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
            MeshObject("Parking pad", root, pad, "Parking pad " + (alongX ? "x" : "z"), false);
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

        private static GameObject Backdrop()
        {
            var root = new GameObject("Backdrop");
            const float gap = 2.5f, near = -1 - gap, far = FixedMap.Size + 1 + gap;
            int pick = 0;
            // Each side: houses in a row facing the arena; south/north span the corners, west/east fill between them.
            Row(root.transform, ref pick, Vector3.right, Vector3.back, new Vector3(-16, 0, near), 112, 6, 0);
            Row(root.transform, ref pick, Vector3.left, Vector3.forward, new Vector3(FixedMap.Size + 16, 0, far), 112, 6, 180);
            Row(root.transform, ref pick, Vector3.back, Vector3.left, new Vector3(near, 0, FixedMap.Size + 1), FixedMap.Size + 2, 0, 270);
            Row(root.transform, ref pick, Vector3.forward, Vector3.right, new Vector3(far, 0, -1), FixedMap.Size + 2, 0, 90);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return SavePrefab(root, "Backdrop");
        }

        // Lines houses up from start along `along`, their arena-facing side on the line through start, extending toward `away`.
        private static void Row(Transform root, ref int pick, Vector3 along, Vector3 away, Vector3 start, float length, float overshoot, int yaw)
        {
            float cursor = 0;
            while (cursor < length - 4)
            {
                string name = SkylineModels[pick++ % SkylineModels.Length];
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
                if (cursor + width > length + overshoot) { Object.DestroyImmediate(holder.gameObject); cursor += 3; continue; }
                // Move so that the bounds start at the cursor along the row and touch the line on the arena side.
                Vector3 alongMin = Vector3.Dot(along, Vector3.one) > 0 ? b.min : b.max;
                Vector3 nearSide = Vector3.Dot(away, Vector3.one) > 0 ? b.min : b.max;
                Vector3 target = start + along * cursor;
                Vector3 offset = Vector3.Scale(target - alongMin, Abs(along)) + Vector3.Scale(target - nearSide, Abs(away));
                offset.y = -b.min.y;
                holder.localPosition = offset;
                cursor += width + 1.5f;
            }
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
