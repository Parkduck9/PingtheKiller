using System;
using System.Collections.Generic;
using System.IO;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PingTheKiller.Editor.Setup
{
    // U4k: renders the generated WorldArt catalog to PNGs for review (run without -nographics):
    //   Unity.exe -batchmode -projectPath Client -executeMethod PingTheKiller.Editor.Setup.WorldArtCapture.Run
    //             -ptk-capture-out <dir> -logFile <log>
    // The city is placed exactly as WorldProps places it (every wall and obstacle prefab at its FixedMap box centre, the ground
    // and the skyline at the origin) under the game's night lighting (SceneLighting). Views: an oblique overview and a top-down
    // view without fog (to read the geometry), and street-level views at 1.7 m eye height with the game's fog. It also logs the
    // catalog's triangle and renderer counts (walls and backdrop) for the performance note.
    public static class WorldArtCapture
    {
        private const float Eye = 1.7f;

        private struct View
        {
            public string Name;
            public Vector3 Position, Target;
            public float Fov;
            public bool Fog, Orthographic;
            public float OrthoSize;
            public int Width, Height;
        }

        private static readonly View[] Views =
        {
            new View { Name = "overview-720", Position = new Vector3(-22, 58, -22), Target = new Vector3(42, 0, 42), Fov = 50, Width = 1280, Height = 720 },
            new View { Name = "overview-ne-720", Position = new Vector3(102, 58, 102), Target = new Vector3(38, 0, 38), Fov = 50, Width = 1280, Height = 720 },
            new View { Name = "top-down", Position = new Vector3(40, 150, 40), Target = new Vector3(40, 0, 40.001f), Orthographic = true, OrthoSize = 50, Width = 1024, Height = 1024 },
            // South edge street, looking north into the dead end of the south U block (walls 6, 7, 8).
            new View { Name = "street-south-u", Position = new Vector3(31, Eye, 1.5f), Target = new Vector3(31, 6, 21), Fov = 60, Fog = true, Width = 1280, Height = 720 },
            // Into the north L block (walls 23, 24, 25) from the street south of it.
            new View { Name = "street-north-l", Position = new Vector3(25, Eye, 56.5f), Target = new Vector3(27, 6, 72), Fov = 60, Fog = true, Width = 1280, Height = 720 },
            // Along the north edge street toward the north-east corner and the skyline.
            new View { Name = "street-edge-ne", Position = new Vector3(36, Eye, 76.5f), Target = new Vector3(80, 7, 82), Fov = 60, Fog = true, Width = 1280, Height = 720 },
            // Across the west plaza toward the west edge (border wall and skyline).
            new View { Name = "street-edge-w", Position = new Vector3(18, Eye, 49), Target = new Vector3(-4, 6, 40), Fov = 60, Fog = true, Width = 1280, Height = 720 },
            // Along the middle street toward the central T block (walls 13, 14) and the L at walls 16, 17.
            new View { Name = "street-centre", Position = new Vector3(39.5f, Eye, 22), Target = new Vector3(30, 6, 42), Fov = 60, Fog = true, Width = 1280, Height = 720 }
        };

        public static void Run()
        {
            string output = Argument("-ptk-capture-out") ?? Path.Combine(Application.dataPath, "..", "..", "docs", "qa", "U4k-screens", "capture");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            try
            {
                WorldArt art = AssetDatabase.LoadAssetAtPath<WorldArt>("Assets/Resources/WorldArt.asset");
                if (art == null) throw new InvalidOperationException("Assets/Resources/WorldArt.asset is missing");
                LogCounts(art);
                foreach (View view in Views) Shoot(output, art, view);
                Debug.Log("[WorldArtCapture] done: " + output);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[WorldArtCapture] failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        // Triangles and renderers of the wall prefabs and the backdrop, as the catalog stores them.
        public static void Count(WorldArt art, out long wallTriangles, out int wallRenderers, out long backdropTriangles, out int backdropRenderers)
        {
            wallTriangles = 0; wallRenderers = 0;
            foreach (WorldArt.Piece piece in art.Walls) Count(piece.Prefab, ref wallTriangles, ref wallRenderers);
            backdropTriangles = 0; backdropRenderers = 0;
            Count(art.Backdrop, ref backdropTriangles, ref backdropRenderers);
        }

        private static void Count(GameObject prefab, ref long triangles, ref int renderers)
        {
            if (prefab == null) return;
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderers++;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Mesh mesh = filter.sharedMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += mesh.GetIndexCount(sub) / 3;
            }
        }

        private static void LogCounts(WorldArt art)
        {
            Count(art, out long wallTriangles, out int wallRenderers, out long backdropTriangles, out int backdropRenderers);
            Debug.Log($"[WorldArtCapture] counts: walls {wallTriangles} triangles / {wallRenderers} renderers; backdrop {backdropTriangles} triangles / {backdropRenderers} renderers; total {wallTriangles + backdropTriangles} triangles / {wallRenderers + backdropRenderers} renderers");
        }

        private static void Shoot(string output, WorldArt art, View view)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("World art capture");
            SceneLighting.Apply(root.transform);
            RenderSettings.fog = view.Fog;
            Build(art, root.transform);
            var cameraObject = new GameObject("Capture camera", typeof(Camera)) { tag = "MainCamera" };
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SceneLighting.Background;
            camera.nearClipPlane = view.Orthographic ? 1 : ViewPose.Near;
            camera.farClipPlane = FixedMap.Size * 4;
            camera.orthographic = view.Orthographic;
            camera.orthographicSize = view.OrthoSize;
            camera.fieldOfView = view.Fov > 0 ? view.Fov : 60;
            camera.GetUniversalAdditionalCameraData();
            var target = new RenderTexture(view.Width, view.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.aspect = view.Width / (float)view.Height;
            camera.transform.position = view.Position;
            camera.transform.rotation = Quaternion.LookRotation(view.Target - view.Position, view.Orthographic ? Vector3.forward : Vector3.up);
            camera.Render();
            camera.Render(); // the first render after a material change can still show the fallback shader
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(view.Width, view.Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, view.Width, view.Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string file = Path.Combine(output, view.Name + ".png");
            File.WriteAllBytes(file, image.EncodeToPNG());
            Debug.Log("[WorldArtCapture] " + file);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(root);
        }

        // The city as WorldProps.BuildCity places it.
        private static void Build(WorldArt art, Transform parent)
        {
            FixedMap map = SharedMap.Map;
            var placed = new List<(GameObject, Vector3)> { (art.Ground, Vector3.zero), (art.Backdrop, Vector3.zero) };
            for (int i = 0; i < map.Walls.Count && i < art.Walls.Length; i++)
            {
                SharedMap.WallBox(map.Walls[i], out Vector3 center, out _);
                placed.Add((art.Walls[i].Prefab, center));
            }
            for (int i = 0; i < map.Obstacles.Count && i < art.Obstacles.Length; i++)
            {
                Obstacle obstacle = map.Obstacles[i];
                placed.Add((art.Obstacles[i].Prefab, new Vector3((obstacle.MinX + obstacle.MaxX) * .5f, 0, (obstacle.MinZ + obstacle.MaxZ) * .5f)));
            }
            foreach ((GameObject prefab, Vector3 position) in placed)
            {
                if (prefab == null) continue;
                var item = (GameObject)Object.Instantiate(prefab, parent);
                item.transform.localPosition = position;
            }
        }
    }
}
