using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if PTK_MCP_TEST_FRAMEWORK
using UnityEditor.TestTools.TestRunner.Api;
#endif

namespace PingTheKiller.McpBridge
{
    /// <summary>
    /// Starts Test Runner API runs and records their results in SessionState, so a run that spans a
    /// domain reload (PlayMode) still reports back. Only the latest run is kept.
    /// </summary>
    internal static class McpTestRuns
    {
        private const string PendingKey = "PtK.McpBridge.TestRun.Pending";
        private const string ResultKey = "PtK.McpBridge.TestRun.Result";
        private const string XmlPathKey = "PtK.McpBridge.TestRun.XmlPath";
        private const int MaxFailures = 100;
        private const int MaxTextChars = 3000;

#if PTK_MCP_TEST_FRAMEWORK
        public static void Install() => TestRunnerApi.RegisterTestCallback(new Callbacks());

        public static object Start(McpArgs args)
        {
            if (EditorApplication.isCompiling) throw new McpCommandException("Scripts are compiling; wait for unity_refresh to finish first.");
            if (EditorUtility.scriptCompilationFailed) throw new McpCommandException("Cannot run tests while there are compile errors (see unity_get_compile_errors).");
            bool saveScenes = args.Bool("saveScenes");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                // The Test Runner would otherwise block the main thread with a modal "save scene?" dialog.
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                if (saveScenes && scene.path.Length > 0 && EditorSceneManager.SaveScene(scene)) continue;
                throw new McpCommandException(scene.path.Length == 0
                    ? $"Open scene '{scene.name}' is untitled and has unsaved changes, so the Test Runner would show a modal Save dialog. Save it to a path in the editor or open another scene first."
                    : $"Open scene '{scene.path}' has unsaved changes, so the Test Runner would show a modal Save dialog. Pass saveScenes=true to save it first, or discard the changes.");
            }
            if (SessionState.GetString(PendingKey, "").Length > 0)
                throw new McpCommandException("A test run started through the bridge is still active; wait for it (unity_run_tests polls it) or restart the editor.");

            string modeName = args.String("mode", "EditMode");
            TestMode mode;
            if (string.Equals(modeName, "EditMode", StringComparison.OrdinalIgnoreCase)) mode = TestMode.EditMode;
            else if (string.Equals(modeName, "PlayMode", StringComparison.OrdinalIgnoreCase)) mode = TestMode.PlayMode;
            else throw new McpCommandException("mode must be EditMode or PlayMode.");

            var filter = new Filter
            {
                testMode = mode,
                testNames = NullIfEmpty(args.Strings("testNames")),
                groupNames = NullIfEmpty(args.Strings("groupNames")),
                categoryNames = NullIfEmpty(args.Strings("categoryNames")),
                assemblyNames = NullIfEmpty(args.Strings("assemblyNames")),
            };

            string runId = Guid.NewGuid().ToString("N");
            SessionState.SetString(PendingKey, runId);
            SessionState.SetString(XmlPathKey, args.String("resultsXmlPath"));
            SessionState.EraseString(ResultKey);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                api.Execute(new ExecutionSettings(filter));
            }
            catch
            {
                SessionState.EraseString(PendingKey);
                throw;
            }
            return new Dictionary<string, object> { { "runId", runId }, { "mode", mode.ToString() } };
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                string runId = SessionState.GetString(PendingKey, "");
                if (runId.Length == 0) return; // a run started from the Test Runner window
                SessionState.EraseString(PendingKey);

                string xmlPath = SessionState.GetString(XmlPathKey, "");
                string xmlError = null;
                if (xmlPath.Length > 0)
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(xmlPath));
                        TestRunnerApi.SaveResultToFile(result, xmlPath);
                    }
                    catch (Exception ex)
                    {
                        xmlError = ex.Message;
                    }
                }

                var failures = new List<object>();
                CollectFailures(result, failures);
                var summary = new Dictionary<string, object>
                {
                    { "runId", runId },
                    { "state", "finished" },
                    { "result", result.TestStatus.ToString() },
                    { "passed", result.PassCount },
                    { "failed", result.FailCount },
                    { "skipped", result.SkipCount },
                    { "inconclusive", result.InconclusiveCount },
                    { "durationSeconds", Math.Round(result.Duration, 3) },
                    { "failures", failures },
                };
                if (xmlPath.Length > 0) summary["resultsXmlPath"] = xmlPath;
                if (xmlError != null) summary["resultsXmlError"] = xmlError;
                SessionState.SetString(ResultKey, McpJson.Write(summary));
            }

            private static void CollectFailures(ITestResultAdaptor node, List<object> failures)
            {
                if (failures.Count >= MaxFailures) return;
                if (node.HasChildren)
                {
                    foreach (var child in node.Children) CollectFailures(child, failures);
                    return;
                }
                if (node.TestStatus != TestStatus.Failed) return;
                failures.Add(new Dictionary<string, object>
                {
                    { "name", node.FullName },
                    { "message", Truncate(node.Message) },
                    { "stackTrace", Truncate(node.StackTrace) },
                });
            }
        }

        private static string[] NullIfEmpty(string[] values) => values.Length == 0 ? null : values;

        private static string Truncate(string s) =>
            string.IsNullOrEmpty(s) || s.Length <= MaxTextChars ? s ?? "" : s.Substring(0, MaxTextChars) + "...";
#else
        public static void Install() { }

        public static object Start(McpArgs args) =>
            throw new McpCommandException("com.unity.test-framework is not installed in this project.");
#endif

        public static object Result(McpArgs args)
        {
            string runId = args.String("runId");
            string json = SessionState.GetString(ResultKey, "");
            if (json.Length > 0)
            {
                object parsed = McpJson.Parse(json);
                if (parsed is Dictionary<string, object> d && d.TryGetValue("runId", out object id) && (string)id == runId)
                    return d;
            }
            if (SessionState.GetString(PendingKey, "") == runId)
                return new Dictionary<string, object> { { "runId", runId }, { "state", "running" } };
            return new Dictionary<string, object> { { "runId", runId }, { "state", "unknown" } };
        }
    }
}
