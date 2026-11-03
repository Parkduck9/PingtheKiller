using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PingTheKiller.McpBridge
{
    /// <summary>Command handlers. Every method runs on the editor main thread.</summary>
    internal static class McpCommands
    {
        public static object Execute(string command, McpArgs args)
        {
            switch (command)
            {
                case "status": return Status();
                case "console":
                    return McpLogBuffer.Read(args.Int("maxEntries", 100), args.String("types"),
                        args.Bool("includeStackTraces"), args.String("contains"), args.Bool("clear"));
                case "compile_errors": return McpCompileMessages.Read(args.Bool("includeWarnings"));
                case "refresh": return Refresh();
                case "tests_run": return McpTestRuns.Start(args);
                case "tests_result": return McpTestRuns.Result(args);
                case "play_mode": return PlayMode(args.String("action"));
                case "screenshot": return Screenshot(args);
                case "execute_menu": return ExecuteMenu(args.String("menuPath"));
                case "find_assets": return FindAssets(args);
                default: throw new McpCommandException($"Unknown command '{command}'.");
            }
        }

        private static object Status()
        {
            Scene scene = SceneManager.GetActiveScene();
            return new Dictionary<string, object>
            {
                { "bridgeVersion", McpBridge.BridgeVersion },
                { "epoch", McpBridge.Epoch },
                { "domainLoadedUtc", McpBridge.DomainLoadedUtc.ToString("O") },
                { "unityVersion", Application.unityVersion },
                { "projectPath", Directory.GetCurrentDirectory() },
                { "isCompiling", EditorApplication.isCompiling },
                { "isUpdating", EditorApplication.isUpdating },
                { "isPlaying", EditorApplication.isPlaying },
                { "isPaused", EditorApplication.isPaused },
                { "isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode },
                { "hasCompileErrors", EditorUtility.scriptCompilationFailed },
                { "compileErrorCount", McpCompileMessages.ErrorCount },
                { "lastCompilationFinishedUtc", McpCompileMessages.LastFinishedUtc },
                { "activeScene", scene.path },
                { "activeSceneDirty", scene.isDirty },
                { "clients", McpBridge.ClientCount },
            };
        }

        private static object Refresh()
        {
            AssetDatabase.Refresh();
            return new Dictionary<string, object>
            {
                { "isCompiling", EditorApplication.isCompiling },
                { "isUpdating", EditorApplication.isUpdating },
            };
        }

        private static object PlayMode(string action)
        {
            switch (action)
            {
                case "enter":
                    if (EditorUtility.scriptCompilationFailed)
                        throw new McpCommandException("Cannot enter Play mode: there are compile errors (see unity_get_compile_errors).");
                    EditorApplication.isPlaying = true;
                    break;
                case "exit":
                    EditorApplication.isPlaying = false;
                    break;
                case "pause":
                    EditorApplication.isPaused = true;
                    break;
                case "resume":
                    EditorApplication.isPaused = false;
                    break;
                default:
                    throw new McpCommandException("action must be enter, exit, pause or resume.");
            }
            return new Dictionary<string, object> { { "requested", action }, { "isPlaying", EditorApplication.isPlaying } };
        }

        private static object ExecuteMenu(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath)) throw new McpCommandException("menuPath is required.");
            if (!EditorApplication.ExecuteMenuItem(menuPath))
                throw new McpCommandException($"Menu item '{menuPath}' does not exist or is disabled right now.");
            return new Dictionary<string, object> { { "executed", menuPath } };
        }

        private static object FindAssets(McpArgs args)
        {
            string filter = args.String("filter");
            string[] folders = args.Strings("folders");
            int limit = Math.Max(1, args.Int("limit", 100));
            foreach (string folder in folders)
                if (!AssetDatabase.IsValidFolder(folder)) throw new McpCommandException($"'{folder}' is not an asset folder.");

            string[] guids = folders.Length > 0 ? AssetDatabase.FindAssets(filter, folders) : AssetDatabase.FindAssets(filter);
            var assets = new List<object>();
            for (int i = 0; i < guids.Length && assets.Count < limit; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                assets.Add(new Dictionary<string, object>
                {
                    { "path", path }, { "guid", guids[i] }, { "type", type != null ? type.Name : "" },
                });
            }
            return new Dictionary<string, object>
            {
                { "total", guids.Length }, { "returned", assets.Count }, { "truncated", guids.Length > assets.Count }, { "assets", assets },
            };
        }

        private static object Screenshot(McpArgs args)
        {
            string path = args.String("path");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new McpCommandException("path must be an absolute file path.");
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(path)) File.Delete(path);

            if (EditorApplication.isPlaying)
            {
                ScreenCapture.CaptureScreenshot(path, Math.Max(1, args.Int("superSize", 1)));
                FocusGameView();
                return new PendingScreenshot(path, EditorApplication.timeSinceStartup + 10.0);
            }
            return RenderCamera(path, args.Int("width"), args.Int("height"));
        }

        private static object RenderCamera(string path, int width, int height)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                foreach (Camera c in Camera.allCameras)
                    if (c.enabled && c.targetTexture == null && (camera == null || c.depth > camera.depth)) camera = c;
            }
            if (camera == null) throw new McpCommandException("No enabled camera in the open scene(s) to render in Edit mode.");

            if (width <= 0 || height <= 0)
            {
                Vector2 size = Handles.GetMainGameViewSize();
                width = Mathf.Max(16, (int)size.x);
                height = Mathf.Max(16, (int)size.y);
            }
            width = Mathf.Clamp(width, 16, 8192);
            height = Mathf.Clamp(height, 16, 8192);

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return new Dictionary<string, object>
            {
                { "path", path }, { "width", width }, { "height", height }, { "mode", "edit-camera-render" },
                { "camera", camera.name }, { "note", "Edit mode render of one camera; Screen Space - Overlay UI is not included." },
            };
        }

        private static void FocusGameView()
        {
            // ScreenCapture writes at the end of the next rendered Game view frame; make sure one renders.
            Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView, false, null, false)?.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private sealed class PendingScreenshot : IPendingOperation
        {
            private readonly string _path;
            private readonly double _deadline;
            private long _lastSize = -1;

            public PendingScreenshot(string path, double deadline)
            {
                _path = path;
                _deadline = deadline;
            }

            public bool Poll(out object result, out string error)
            {
                result = null;
                error = null;
                if (File.Exists(_path))
                {
                    long size = new FileInfo(_path).Length;
                    if (size > 0 && size == _lastSize)
                    {
                        result = new Dictionary<string, object> { { "path", _path }, { "bytes", size }, { "mode", "play-screencapture" } };
                        return true;
                    }
                    _lastSize = size;
                }
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    error = "ScreenCapture did not write the file within 10 s (is the Game view visible and Play mode running?).";
                    return true;
                }
                if (!EditorApplication.isPlaying)
                {
                    error = "Play mode ended before the screenshot was written.";
                    return true;
                }
                FocusGameView();
                return false;
            }
        }
    }
}
