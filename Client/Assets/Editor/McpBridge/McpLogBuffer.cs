using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace PingTheKiller.McpBridge
{
    /// <summary>
    /// Ring buffer of Console messages fed by Application.logMessageReceivedThreaded. It is written to
    /// SessionState before a domain reload and restored after, so logs from just before a reload survive.
    /// </summary>
    internal static class McpLogBuffer
    {
        private const string SessionKey = "PtK.McpBridge.Logs";
        private const int Capacity = 1000;
        private const int MaxStackChars = 4000;
        private const int MaxMessageChars = 8000;

        private static readonly object Gate = new object();
        private static readonly LinkedList<Entry> Entries = new LinkedList<Entry>();
        private static long _sequence;

        private struct Entry
        {
            public long Seq;
            public string TimeUtc;
            public string Type;
            public string Message;
            public string Stack;
        }

        public static void Install()
        {
            Restore();
            Application.logMessageReceivedThreaded += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            var entry = new Entry
            {
                TimeUtc = DateTime.UtcNow.ToString("O"),
                Type = TypeName(type),
                Message = Truncate(message, MaxMessageChars),
                Stack = Truncate(stackTrace, MaxStackChars),
            };
            lock (Gate)
            {
                entry.Seq = ++_sequence;
                Entries.AddLast(entry);
                while (Entries.Count > Capacity) Entries.RemoveFirst();
            }
        }

        public static object Read(int maxEntries, string types, bool includeStackTraces, string contains, bool clear)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string t in (types ?? "").Split(','))
                if (t.Trim().Length > 0) allowed.Add(t.Trim());

            var picked = new List<object>();
            int total;
            lock (Gate)
            {
                total = Entries.Count;
                for (var node = Entries.Last; node != null && picked.Count < Math.Max(1, maxEntries); node = node.Previous)
                {
                    Entry e = node.Value;
                    if (allowed.Count > 0 && !allowed.Contains(e.Type)) continue;
                    if (!string.IsNullOrEmpty(contains) && e.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var item = new Dictionary<string, object>
                    {
                        { "seq", e.Seq }, { "timeUtc", e.TimeUtc }, { "type", e.Type }, { "message", e.Message },
                    };
                    if (includeStackTraces && !string.IsNullOrEmpty(e.Stack)) item["stackTrace"] = e.Stack;
                    picked.Add(item);
                }
                if (clear) Entries.Clear();
            }
            picked.Reverse();
            if (clear) ClearEditorConsole();
            return new Dictionary<string, object>
            {
                { "returned", picked.Count }, { "buffered", total }, { "cleared", clear }, { "entries", picked },
            };
        }

        public static void Persist()
        {
            var list = new List<object>();
            lock (Gate)
            {
                foreach (Entry e in Entries)
                    list.Add(new Dictionary<string, object>
                    {
                        { "seq", e.Seq }, { "t", e.TimeUtc }, { "y", e.Type }, { "m", e.Message }, { "s", e.Stack },
                    });
            }
            SessionState.SetString(SessionKey, McpJson.Write(list));
        }

        private static void Restore()
        {
            string json = SessionState.GetString(SessionKey, "");
            if (json.Length == 0) return;
            try
            {
                if (!(McpJson.Parse(json) is List<object> list)) return;
                lock (Gate)
                {
                    foreach (object o in list)
                    {
                        if (!(o is Dictionary<string, object> d)) continue;
                        var e = new Entry
                        {
                            Seq = d.TryGetValue("seq", out object s) && s is double sd ? (long)sd : 0,
                            TimeUtc = d.TryGetValue("t", out object t) ? t as string : "",
                            Type = d.TryGetValue("y", out object y) ? y as string : "log",
                            Message = d.TryGetValue("m", out object m) ? m as string ?? "" : "",
                            Stack = d.TryGetValue("s", out object st) ? st as string : "",
                        };
                        Entries.AddLast(e);
                        _sequence = Math.Max(_sequence, e.Seq);
                    }
                }
            }
            catch (FormatException)
            {
                // Corrupt state from an older bridge version; start empty.
            }
            SessionState.EraseString(SessionKey);
        }

        private static void ClearEditorConsole()
        {
            try
            {
                Type logEntries = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            catch (Exception)
            {
                // Internal API moved; the bridge buffer is still cleared.
            }
        }

        private static string TypeName(LogType type)
        {
            switch (type)
            {
                case LogType.Error: return "error";
                case LogType.Exception: return "exception";
                case LogType.Assert: return "assert";
                case LogType.Warning: return "warning";
                default: return "log";
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max) + "...";
    }

    /// <summary>
    /// Compiler messages of the latest compilation, captured from CompilationPipeline and kept in
    /// SessionState (a successful compile is followed by a domain reload that would otherwise lose them).
    /// </summary>
    internal static class McpCompileMessages
    {
        private const string SessionKey = "PtK.McpBridge.Compile";
        private static readonly List<Dictionary<string, object>> Messages = new List<Dictionary<string, object>>();
        private static string _startedUtc = "";
        private static string _finishedUtc = "";

        public static string LastFinishedUtc => _finishedUtc;

        public static void Install()
        {
            Restore();
            CompilationPipeline.compilationStarted += _ =>
            {
                Messages.Clear();
                _startedUtc = DateTime.UtcNow.ToString("O");
                _finishedUtc = "";
                Persist();
            };
            CompilationPipeline.assemblyCompilationFinished += (assemblyPath, messages) =>
            {
                foreach (CompilerMessage m in messages)
                {
                    Messages.Add(new Dictionary<string, object>
                    {
                        { "type", m.type == CompilerMessageType.Error ? "error" : "warning" },
                        { "file", m.file }, { "line", m.line }, { "column", m.column },
                        { "message", m.message }, { "assembly", System.IO.Path.GetFileName(assemblyPath) },
                    });
                }
                Persist();
            };
            CompilationPipeline.compilationFinished += _ =>
            {
                _finishedUtc = DateTime.UtcNow.ToString("O");
                Persist();
            };
        }

        public static int ErrorCount
        {
            get
            {
                int n = 0;
                foreach (var m in Messages) if ((string)m["type"] == "error") n++;
                return n;
            }
        }

        public static object Read(bool includeWarnings)
        {
            var list = new List<object>();
            foreach (var m in Messages)
                if (includeWarnings || (string)m["type"] == "error") list.Add(m);
            bool failed = EditorUtility.scriptCompilationFailed;
            var result = new Dictionary<string, object>
            {
                { "hasCompileErrors", failed },
                { "errorCount", ErrorCount },
                { "compilationStartedUtc", _startedUtc },
                { "compilationFinishedUtc", _finishedUtc },
                { "messages", list },
            };
            if (failed && ErrorCount == 0)
                result["note"] = "The editor reports compile errors from a compilation before the bridge loaded. Call unity_refresh after touching a script, or read the Console.";
            return result;
        }

        private static void Persist() =>
            SessionState.SetString(SessionKey, McpJson.Write(new Dictionary<string, object>
            {
                { "started", _startedUtc }, { "finished", _finishedUtc }, { "messages", Messages },
            }));

        private static void Restore()
        {
            string json = SessionState.GetString(SessionKey, "");
            if (json.Length == 0) return;
            try
            {
                if (!(McpJson.Parse(json) is Dictionary<string, object> root)) return;
                _startedUtc = root.TryGetValue("started", out object s) ? s as string ?? "" : "";
                _finishedUtc = root.TryGetValue("finished", out object f) ? f as string ?? "" : "";
                if (root.TryGetValue("messages", out object m) && m is List<object> list)
                    foreach (object o in list)
                        if (o is Dictionary<string, object> d) Messages.Add(d);
            }
            catch (FormatException)
            {
                // Ignore corrupt state.
            }
        }
    }
}
