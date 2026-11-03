using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UnityMcp;

[McpServerToolType]
public sealed class UnityTools(BridgeClient bridge)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(30);
    private readonly EditorWaiter _waiter = new(bridge);

    [McpServerTool(Name = "unity_status", ReadOnly = true)]
    [Description("Unity editor state: bridge connection, compiling / importing / domain reload, play mode, Unity version, active scene, compile error count. Never fails: reports connected=false while the editor is closed or reloading.")]
    public async Task<CallToolResult> Status(CancellationToken ct)
    {
        try
        {
            var status = await bridge.CallAsync("status", null, TimeSpan.FromSeconds(5), ct);
            return Ok(new JsonObject
            {
                ["connected"] = true,
                ["state"] = EditorWaiter.Describe(status),
                ["endpoint"] = bridge.Endpoint,
                ["connectedSinceUtc"] = bridge.ConnectedSinceUtc?.ToString("O"),
                ["connectCount"] = bridge.ConnectCount,
                ["editor"] = JsonNode.Parse(status.GetRawText()),
            });
        }
        catch (Exception ex) when (ex is BridgeUnavailableException or BridgeTimeoutException)
        {
            return Ok(new JsonObject
            {
                ["connected"] = false,
                ["state"] = ex is BridgeTimeoutException ? "connected but unresponsive (main thread busy)" : "not connected (editor closed, bridge not loaded, or domain reload in progress)",
                ["endpoint"] = bridge.Endpoint,
                ["disconnectedSinceUtc"] = bridge.DisconnectedSinceUtc?.ToString("O"),
                ["connectCount"] = bridge.ConnectCount,
                ["detail"] = ex.Message,
            });
        }
    }

    [McpServerTool(Name = "unity_read_console", ReadOnly = true)]
    [Description("Read recent Unity Console messages captured by the bridge (kept across domain reloads, newest last). Compiler errors are reported by unity_get_compile_errors instead.")]
    public Task<CallToolResult> ReadConsole(
        [Description("Maximum number of entries to return (newest kept).")] int maxEntries = 100,
        [Description("Comma-separated types to include: error, exception, assert, warning, log. Empty = all.")] string types = "",
        [Description("Include stack traces.")] bool includeStackTraces = false,
        [Description("Only entries whose message contains this text (case-insensitive).")] string contains = "",
        [Description("Clear the captured buffer (and the editor Console) after reading.")] bool clear = false,
        CancellationToken ct = default) =>
        Run(() => Forward("console", new JsonObject
        {
            ["maxEntries"] = maxEntries,
            ["types"] = types,
            ["includeStackTraces"] = includeStackTraces,
            ["contains"] = contains,
            ["clear"] = clear,
        }, DefaultCallTimeout, ct));

    [McpServerTool(Name = "unity_get_compile_errors", ReadOnly = true)]
    [Description("Compiler messages from the most recent script compilation (file, line, message), plus whether the editor currently has compile errors.")]
    public Task<CallToolResult> CompileErrors(
        [Description("Also return compiler warnings.")] bool includeWarnings = false,
        CancellationToken ct = default) =>
        Run(() => Forward("compile_errors", new JsonObject { ["includeWarnings"] = includeWarnings }, DefaultCallTimeout, ct));

    [McpServerTool(Name = "unity_refresh")]
    [Description("AssetDatabase.Refresh(), then (by default) wait until compilation, domain reload and imports finish. Returns whether a domain reload happened and the compile errors. Use after editing scripts or assets outside Unity.")]
    public Task<CallToolResult> Refresh(
        [Description("Wait for compile + domain reload to finish before returning.")] bool waitForCompile = true,
        [Description("Overall timeout in seconds.")] int timeoutSeconds = 300,
        CancellationToken ct = default) =>
        Run(async () =>
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds));
            var started = DateTime.UtcNow;
            string? epochBefore = null;
            try
            {
                epochBefore = EditorWaiter.GetString(await bridge.CallAsync("status", null, TimeSpan.FromSeconds(5), ct), "epoch");
            }
            catch (BridgeTimeoutException) { /* busy; refresh will queue behind it */ }

            JsonElement refresh = default;
            try
            {
                refresh = await bridge.CallAsync("refresh", null, timeout, ct);
            }
            catch (BridgeUnavailableException) when (epochBefore != null && waitForCompile)
            {
                // The reply can be lost when the domain reload starts right after Refresh(); keep waiting.
            }
            if (!waitForCompile)
                return Ok(new JsonObject { ["refreshed"] = true, ["bridge"] = Node(refresh) });

            await Task.Delay(500, ct);
            var remaining = timeout - (DateTime.UtcNow - started);
            var status = await _waiter.WaitForStableAsync(_ => true, remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), ct);
            var errors = await bridge.CallAsync("compile_errors", new JsonObject { ["includeWarnings"] = false }, DefaultCallTimeout, ct);
            return Ok(new JsonObject
            {
                ["refreshed"] = true,
                ["domainReloaded"] = epochBefore != null && EditorWaiter.GetString(status, "epoch") != epochBefore,
                ["seconds"] = Math.Round((DateTime.UtcNow - started).TotalSeconds, 1),
                ["state"] = EditorWaiter.Describe(status),
                ["compile"] = Node(errors),
            });
        });

    [McpServerTool(Name = "unity_run_tests")]
    [Description("Run Unity tests through the Test Runner API and return pass/fail counts and failure details. Waits for the run to finish (survives domain reloads). PlayMode runs enter Play mode.")]
    public Task<CallToolResult> RunTests(
        [Description("EditMode or PlayMode.")] string mode = "EditMode",
        [Description("Full test names to run (exact). Empty = all.")] string[]? testNames = null,
        [Description("Group (namespace / fixture) regex filters.")] string[]? groupNames = null,
        [Description("NUnit categories.")] string[]? categoryNames = null,
        [Description("Test assembly names (without .dll).")] string[]? assemblyNames = null,
        [Description("Optional path to also save the NUnit XML results (relative paths resolve against the MCP server's working directory).")] string resultsXmlPath = "",
        [Description("Save open scenes that have unsaved changes first (otherwise a dirty scene makes this fail, because the Test Runner would block on a modal Save dialog).")] bool saveScenes = false,
        [Description("Overall timeout in seconds.")] int timeoutSeconds = 600,
        CancellationToken ct = default) =>
        Run(async () =>
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(10, timeoutSeconds));
            var deadline = DateTime.UtcNow + timeout;
            await _waiter.WaitForStableAsync(_ => true, TimeSpan.FromSeconds(Math.Min(120, timeout.TotalSeconds)), TimeSpan.FromMilliseconds(500), ct);

            var args = new JsonObject
            {
                ["mode"] = mode,
                ["testNames"] = ToArray(testNames),
                ["groupNames"] = ToArray(groupNames),
                ["categoryNames"] = ToArray(categoryNames),
                ["assemblyNames"] = ToArray(assemblyNames),
                ["resultsXmlPath"] = string.IsNullOrWhiteSpace(resultsXmlPath) ? "" : Path.GetFullPath(resultsXmlPath),
                ["saveScenes"] = saveScenes,
            };
            var started = await bridge.CallAsync("tests_run", args, DefaultCallTimeout, ct);
            string runId = EditorWaiter.GetString(started, "runId") ?? throw new BridgeCommandException("tests_run returned no runId.");

            while (true)
            {
                try
                {
                    var result = await bridge.CallAsync("tests_result", new JsonObject { ["runId"] = runId }, DefaultCallTimeout, ct);
                    if (EditorWaiter.GetString(result, "state") == "finished") return Ok(Node(result));
                    if (EditorWaiter.GetString(result, "state") == "unknown")
                        throw new BridgeCommandException($"The bridge lost test run {runId} (editor restarted?). Run the tests again.");
                }
                catch (Exception ex) when (ex is BridgeUnavailableException or BridgeTimeoutException)
                {
                    // Domain reloads (PlayMode tests) and long synchronous tests are expected; keep polling.
                }
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException($"Test run {runId} did not finish within {timeout.TotalSeconds:0} s.");
                await Task.Delay(500, ct);
            }
        });

    [McpServerTool(Name = "unity_play_mode")]
    [Description("Enter or exit Play mode, or pause/resume it, and wait until the editor has settled in the new state.")]
    public Task<CallToolResult> PlayMode(
        [Description("enter, exit, pause or resume.")] string action,
        [Description("Timeout in seconds.")] int timeoutSeconds = 120,
        CancellationToken ct = default) =>
        Run(async () =>
        {
            action = action.Trim().ToLowerInvariant();
            Func<JsonElement, bool> reached = action switch
            {
                "enter" => s => EditorWaiter.GetBool(s, "isPlaying"),
                "exit" => s => !EditorWaiter.GetBool(s, "isPlaying"),
                "pause" => s => EditorWaiter.GetBool(s, "isPaused"),
                "resume" => s => !EditorWaiter.GetBool(s, "isPaused"),
                _ => throw new ArgumentException("action must be enter, exit, pause or resume."),
            };
            var timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds));
            try
            {
                await bridge.CallAsync("play_mode", new JsonObject { ["action"] = action }, DefaultCallTimeout, ct);
            }
            catch (BridgeUnavailableException) when (action == "enter")
            {
                // Entering Play mode reloads the domain; the reply may be lost. The wait below decides.
            }
            var status = await _waiter.WaitForStableAsync(reached, timeout, TimeSpan.FromSeconds(action == "enter" ? 1 : 0.3), ct);
            return Ok(new JsonObject { ["action"] = action, ["state"] = EditorWaiter.Describe(status), ["editor"] = Node(status) });
        });

    [McpServerTool(Name = "unity_screenshot")]
    [Description("Save a PNG of the Game view. In Play mode this is the full Game view (UI included, via ScreenCapture). In Edit mode the main camera is rendered off-screen (Screen Space - Overlay UI is not included).")]
    public Task<CallToolResult> Screenshot(
        [Description("Output .png path. Relative paths resolve against the MCP server's working directory (the repo root when launched by Claude Code).")] string path,
        [Description("Play mode only: resolution multiplier.")] int superSize = 1,
        [Description("Edit mode only: width in pixels (0 = Game view size).")] int width = 0,
        [Description("Edit mode only: height in pixels (0 = Game view size).")] int height = 0,
        [Description("Timeout in seconds.")] int timeoutSeconds = 30,
        CancellationToken ct = default) =>
        Run(() =>
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            return Forward("screenshot", new JsonObject
            {
                ["path"] = full,
                ["superSize"] = superSize,
                ["width"] = width,
                ["height"] = height,
            }, TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)), ct);
        });

    [McpServerTool(Name = "unity_execute_menu", Destructive = true)]
    [Description("Execute a Unity editor menu item by path, e.g. \"Assets/Refresh\" or \"Tools/Setup/Build Scene\". Fails if the item does not exist or is disabled.")]
    public Task<CallToolResult> ExecuteMenu(
        [Description("Menu item path.")] string menuPath,
        [Description("Timeout in seconds (the item runs synchronously on the main thread).")] int timeoutSeconds = 120,
        CancellationToken ct = default) =>
        Run(() => Forward("execute_menu", new JsonObject { ["menuPath"] = menuPath }, TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)), ct));

    [McpServerTool(Name = "unity_find_assets", ReadOnly = true)]
    [Description("AssetDatabase.FindAssets search, e.g. \"t:Prefab Player\", \"t:Scene\", \"l:Character\", \"t:Material glow\". Returns asset paths, GUIDs and main types.")]
    public Task<CallToolResult> FindAssets(
        [Description("FindAssets filter string (name words, t:Type, l:Label, ref:...).")] string filter,
        [Description("Folders to search, e.g. [\"Assets/Art\"]. Empty = whole project including packages.")] string[]? folders = null,
        [Description("Maximum results to return.")] int limit = 100,
        CancellationToken ct = default) =>
        Run(() => Forward("find_assets", new JsonObject
        {
            ["filter"] = filter,
            ["folders"] = ToArray(folders),
            ["limit"] = limit,
        }, DefaultCallTimeout, ct));

    private async Task<CallToolResult> Forward(string command, JsonObject args, TimeSpan timeout, CancellationToken ct) =>
        Ok(Node(await bridge.CallAsync(command, args, timeout, ct)));

    private static async Task<CallToolResult> Run(Func<Task<CallToolResult>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex) when (ex is BridgeUnavailableException or BridgeTimeoutException or BridgeCommandException or TimeoutException or ArgumentException or IOException)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ex.Message }] };
        }
    }

    private static CallToolResult Ok(JsonNode? node) =>
        new() { Content = [new TextContentBlock { Text = node?.ToJsonString(Pretty) ?? "null" }] };

    private static JsonNode? Node(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText());

    private static JsonArray ToArray(string[]? values)
    {
        var array = new JsonArray();
        foreach (var value in values ?? [])
            if (!string.IsNullOrWhiteSpace(value)) array.Add(value);
        return array;
    }
}
