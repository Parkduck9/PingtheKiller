using System;
using System.IO;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PingTheKiller.Editor.Capture
{
    // Editor batch capture for the U6c-hatch review (run without -nographics):
    //   Unity.exe -batchmode -projectPath Client -executeMethod PingTheKiller.Editor.Capture.HatchShot.Run -logFile <log>
    // It builds the real map art through WorldView (WorldProps + the WorldArt catalog), shows the hatch at two real
    // FixedMap.HatchPoints entries (25, 63) and (66, 38.5) (read from SharedMap.Map, so they follow the server's map) and renders
    // the game's night lighting (SceneLighting) from a Survivor third-person framing at 6 m and 14 m and a Killer first-person
    // framing at 10 m, each at 1280x720 and 1920x1080. The game camera enables no post-processing, so none is applied here.
    public static class HatchShot
    {
        private static readonly int[] HatchIndices = { 1, 3 };
        private static readonly int[][] Sizes = { new[] { 1280, 720 }, new[] { 1920, 1080 } };
        private const int OwnId = 1;
        private const float SurvivorYawOffset = 8;

        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "qa", "U6c-hatch-screens"));
            Directory.CreateDirectory(output);
            try
            {
                foreach (int index in HatchIndices)
                {
                    MapPoint point = SharedMap.Map.HatchPoints[index];
                    var hatch = new Vector3(point.X, 0, point.Z);
                    foreach (int[] size in Sizes)
                    {
                        Shoot(output, "survivor-6m", index, hatch, 6, Role.Survivor, size[0], size[1]);
                        Shoot(output, "survivor-14m", index, hatch, 14, Role.Survivor, size[0], size[1]);
                        Shoot(output, "killer-10m", index, hatch, 10, Role.Killer, size[0], size[1]);
                    }
                }
                Debug.Log("[HatchShot] done: " + output);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[HatchShot] failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Shoot(string output, string label, int index, Vector3 hatch, float distance, Role role, int width, int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Hatch shot");
            SceneLighting.Apply(root.transform);
            var cameraObject = new GameObject("Role camera", typeof(Camera)) { tag = "MainCamera" };
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SceneLighting.Background;
            camera.nearClipPlane = ViewPose.Near;
            camera.farClipPlane = FixedMap.Size * 3;
            camera.GetUniversalAdditionalCameraData();
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.aspect = width / (float)height;

            Vector3 ground = Standpoint(hatch, distance, role, out float yaw);
            var view = new GameObject("Map").AddComponent<WorldView>();
            view.transform.SetParent(root.transform, false);
            view.Initialize(OwnId, role);
            view.Hatch.ReducedMotionOverride = true;
            for (int i = 0; i < 5; i++)
            {
                var state = new S2C_WorldState { Tick = (uint)(i + 1), HatchVisible = 1, HatchX = hatch.x, HatchZ = hatch.z };
                state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)role, X = ground.x, Z = ground.z, Yaw = yaw });
                for (byte t = 0; t < SharedMap.Map.TerminalPositions.Count; t++)
                    state.Terminals.Add(new TerminalState { Id = t, X = SharedMap.Map.TerminalPositions[t].X, Z = SharedMap.Map.TerminalPositions[t].Z });
                view.Accept(state, 1 + i * .05);
            }
            if (role == Role.Survivor)
            {
                // The camera looks 8 degrees past the hatch, as a player rarely centres it exactly, so the character does not stand in front of it.
                yaw += SurvivorYawOffset;
                view.SetLocalLook(yaw, false);
                new SurvivorOrbitRig().Place(camera, yaw, ground, 0);
            }
            else new KillerViewRig().Place(camera, yaw, ground);
            view.Render(1.4);
            camera.Render();
            camera.Render(); // the first render after a material change can still show the fallback shader
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string file = Path.Combine(output, $"{label}-hatch{index}-{width}x{height}.png");
            File.WriteAllBytes(file, image.EncodeToPNG());
            Debug.Log($"[HatchShot] {file} from ({ground.x:F1}, {ground.z:F1}) yaw {yaw:F0}");
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static bool ObstacleBlocked(FixedMap map, Vector3 from, Vector3 to)
        {
            foreach (Obstacle obstacle in map.Obstacles)
                if (MapGeometry.SegmentHitsBox(from.x, from.z, to.x, to.z, obstacle.MinX - .3f, obstacle.MinZ - .3f, obstacle.MaxX + .3f, obstacle.MaxZ + .3f, out _)) return true;
            return false;
        }

        // A standable point `distance` metres from the hatch with a clear line to it (and, for the Survivor, a clear boom behind),
        // looking at the hatch.
        private static Vector3 Standpoint(Vector3 hatch, float distance, Role role, out float yaw)
        {
            FixedMap map = SharedMap.Map;
            for (int pass = 0; pass < 2; pass++)
                for (int step = 0; step < 72; step++)
                {
                    float angle = step * 5 * Mathf.Deg2Rad;
                    var ground = new Vector3(hatch.x + Mathf.Sin(angle) * distance, 0, hatch.z + Mathf.Cos(angle) * distance);
                    if (!map.CanStand(ground.x, ground.z, .8f) || map.SegmentBlocked(ground.x, ground.z, hatch.x, hatch.z)) continue;
                    yaw = Mathf.Atan2(hatch.x - ground.x, hatch.z - ground.z) * Mathf.Rad2Deg;
                    if (pass == 0)
                    {
                        // First choice: no obstacle (car, barrier) between the eye and the hatch either.
                        Vector3 eye = role == Role.Survivor ? ground - new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0, Mathf.Cos(yaw * Mathf.Deg2Rad)) * SurvivorOrbitRig.Boom : ground;
                        if (ObstacleBlocked(map, eye, hatch)) continue;
                    }
                    if (role == Role.Survivor && pass == 0)
                    {
                        // First choice: the whole 4 m boom is clear; second choice accepts a shortened boom.
                        Vector3 back = SurvivorOrbitRig.Back(yaw, SurvivorOrbitRig.DefaultPitch);
                        if (MapGeometry.SphereCast(map, SurvivorOrbitRig.Target(ground), back, SurvivorOrbitRig.Boom, SurvivorOrbitRig.ProbeRadius) < SurvivorOrbitRig.Boom) continue;
                    }
                    return ground;
                }
            throw new InvalidOperationException("No clear standpoint " + distance + " m from the hatch at " + hatch);
        }
    }
}
