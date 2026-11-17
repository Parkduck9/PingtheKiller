using System;
using System.Collections.Generic;
using System.IO;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PingTheKiller.Editor.Setup
{
    // Q5b editor batch capture (run without -nographics), the HatchShot pattern:
    //   Unity.exe -batchmode -projectPath Client -executeMethod PingTheKiller.Editor.Setup.ChasePropShot.Run -logFile <log>
    // Builds the real map through WorldView plus the ChasePropsLayer, feeds it hand-made snapshots (the shutter bytes and
    // Chase flags a server would send) and renders the game's night lighting at 1280x720 into docs/qa/Q5b-screens:
    // a shutter Up, Down and Broken and a rail from a Survivor framing, a Down shutter from the Killer eye, and a Survivor
    // view of the Killer model stunned beside a Down shutter. These are editor renders of the client code, not live play.
    public static class ChasePropShot
    {
        private const int OwnId = 1, MateId = 2, Width = 1280, Height = 720;

        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "qa", "Q5b-screens"));
            Directory.CreateDirectory(output);
            try
            {
                ChaseProp shutter = WorldChaseProps.Shutters[5], rail = WorldChaseProps.Rails[4];
                Shoot(output, "editor-survivor-shutter-up", shutter, 6, Role.Survivor, ShutterState.Up, null);
                Shoot(output, "editor-survivor-shutter-down", shutter, 6, Role.Survivor, ShutterState.Down, null);
                Shoot(output, "editor-survivor-shutter-broken", shutter, 6, Role.Survivor, ShutterState.Broken, null);
                Shoot(output, "editor-killer-shutter-down", shutter, 5, Role.Killer, ShutterState.Down, null);
                Shoot(output, "editor-survivor-rail", rail, 6, Role.Survivor, ShutterState.Up, null);
                Shoot(output, "editor-survivor-mate-vault", rail, 6, Role.Survivor, ShutterState.Up, (byte)ChaseFlags.Vaulting);
                Shoot(output, "editor-survivor-killer-stunned", shutter, 6, Role.Survivor, ShutterState.Down, (byte)ChaseFlags.Stunned);
                Debug.Log("[ChasePropShot] done: " + output);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[ChasePropShot] failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        // actor: null = nobody at the prop; Vaulting = a Survivor mate mid-vault on the rail; Stunned = the Killer model at the
        // Down shutter, stunned.
        private static void Shoot(string output, string label, ChaseProp prop, float distance, Role role, ShutterState state, byte? actor)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Chase prop shot");
            SceneLighting.Apply(root.transform);
            var cameraObject = new GameObject("Role camera", typeof(Camera)) { tag = "MainCamera" };
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SceneLighting.Background;
            camera.nearClipPlane = ViewPose.Near;
            camera.farClipPlane = FixedMap.Size * 3;
            camera.GetUniversalAdditionalCameraData();
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.aspect = Width / (float)Height;

            var centre = new Vector3(prop.CenterX, 0, prop.CenterZ);
            Vector3 ground = Standpoint(prop, distance, out float yaw);
            var map = new GameObject("Match world");
            map.transform.SetParent(root.transform, false);
            WorldView view = map.AddComponent<WorldView>();
            view.Initialize(OwnId, role);
            ChasePropsLayer chase = map.AddComponent<ChasePropsLayer>();
            chase.Initialize(OwnId, role, true);
            var shutters = new List<byte>();
            for (int i = 0; i < WorldChaseProps.ShutterCount; i++) shutters.Add((byte)(IsShutter(prop, i) ? state : ShutterState.Up));
            float side = prop.Side(ground.x, ground.z) > 0 ? 1 : -1;
            for (int i = 0; i < 5; i++)
            {
                var snapshot = new S2C_WorldState { Tick = (uint)(i + 1), Shutters = shutters };
                snapshot.Players.Add(new PlayerState { Id = OwnId, Role = (byte)role, X = ground.x, Z = ground.z, Yaw = yaw });
                for (byte t = 0; t < SharedMap.Map.TerminalPositions.Count; t++)
                    snapshot.Terminals.Add(new TerminalState { Id = t, X = SharedMap.Map.TerminalPositions[t].X, Z = SharedMap.Map.TerminalPositions[t].Z });
                if (actor == (byte)ChaseFlags.Vaulting)
                    snapshot.Players.Add(new PlayerState { Id = MateId, Role = (byte)Role.Survivor, X = prop.CenterX, Z = prop.CenterZ, Yaw = yaw + 180, Flags = (byte)PlayerFlags.Signal, Chase = (byte)ChaseFlags.Vaulting });
                if (actor == (byte)ChaseFlags.Stunned)
                    snapshot.Players.Add(new PlayerState { Id = 9, Role = (byte)Role.Killer, X = prop.CenterX - prop.CrossX * side * 1.0f, Z = prop.CenterZ - prop.CrossZ * side * 1.0f, Yaw = yaw + 180, Chase = (byte)ChaseFlags.Stunned });
                view.Accept(snapshot, 1 + i * .05);
                chase.Accept(snapshot, 1 + i * .05);
            }
            if (role == Role.Survivor)
            {
                yaw += 24; // turned so the prop is not hidden behind the character
                view.SetLocalLook(yaw, false);
                new SurvivorOrbitRig().Place(camera, yaw, ground, 0);
            }
            else new KillerViewRig().Place(camera, yaw, ground);
            // Mid-action: the vault at the top of its arc (0.4 s of 0.8 s after the flag), the stun 0.5 s in.
            double now = actor == (byte)ChaseFlags.Vaulting ? 1.0 + .4 : 1.5;
            view.Render(now + .3); // characters render 100 ms behind; the snapshots are static, so any time after them works
            chase.Frame(now);
            chase.ApplyPoses(now);
            // Edit mode has no player loop: advance the burst's particles to 0.5 s so the capture shows them.
            if (chase.Burst.Root != null)
                foreach (ParticleSystem system in chase.Burst.Root.GetComponentsInChildren<ParticleSystem>()) system.Simulate(.5f, true, true);
            camera.Render();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string file = Path.Combine(output, $"{label}-{Width}x{Height}.png");
            File.WriteAllBytes(file, image.EncodeToPNG());
            Debug.Log($"[ChasePropShot] {file} prop {prop.Id} at ({centre.x:F1}, {centre.z:F1}) from ({ground.x:F1}, {ground.z:F1}) yaw {yaw:F0} pose {chase.KillerPose}/{chase.PoseOf(MateId)} burst {chase.Burst.Shown}");
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static bool IsShutter(ChaseProp prop, int i)
        {
            ChaseProp s = WorldChaseProps.Shutters[i];
            return s.MinX == prop.MinX && s.MinZ == prop.MinZ && s.MaxX == prop.MaxX && s.MaxZ == prop.MaxZ;
        }

        // A standable point `distance` metres out along the prop's crossing axis (either side, then angled), looking at it.
        private static Vector3 Standpoint(ChaseProp prop, float distance, out float yaw)
        {
            FixedMap map = SharedMap.Map;
            var centre = new Vector3(prop.CenterX, 0, prop.CenterZ);
            foreach (float angle in new[] { 25f, -25f, 155f, -155f, 0f, 180f, 45f, -45f, 135f, -135f })
            {
                float baseAngle = Mathf.Atan2(prop.CrossX, prop.CrossZ) * Mathf.Rad2Deg + angle;
                var ground = centre + new Vector3(Mathf.Sin(baseAngle * Mathf.Deg2Rad), 0, Mathf.Cos(baseAngle * Mathf.Deg2Rad)) * distance;
                if (!map.CanStand(ground.x, ground.z, .8f) || map.SegmentBlocked(ground.x, ground.z, centre.x, centre.z)) continue;
                yaw = Mathf.Atan2(centre.x - ground.x, centre.z - ground.z) * Mathf.Rad2Deg;
                return ground;
            }
            throw new InvalidOperationException("No clear standpoint " + distance + " m from prop " + prop.Id);
        }
    }
}
