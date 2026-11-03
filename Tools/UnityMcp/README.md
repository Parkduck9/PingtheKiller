# UnityMcp — Unity Editor MCP for Claude Code

```
Claude Code ──stdio (MCP JSON-RPC)──> Tools/UnityMcp (.NET 10 console)
                                        │ TCP 127.0.0.1:7801, auto-reconnect
                                        ▼
                     Client/Assets/Editor/McpBridge (editor script, main-thread dispatch)
```

Unity reloads its script domain on every script change and when entering Play mode, which restarts
all in-editor code. The MCP session therefore lives in the external process. The bridge closes its
listener before each reload and opens it again via `[InitializeOnLoad]`, and the server reconnects
on its own.

This is a dev tool. The game's "no JSON" wire rule (AGENTS.md) applies to `Shared/` and the game
protocol, not to this control channel.

## Setup

1. Open `Client/` in the Unity editor (6000.3). The bridge compiles into its own Editor-only assembly
   (`PingTheKiller.McpBridge.Editor`), so it still loads when game scripts have compile errors.
2. Start Claude Code in the repo root. `.mcp.json` registers the server as `unity-editor`
   (`dotnet run --project Tools/UnityMcp -c Release`). Approve the project MCP server when Claude Code
   asks. The first start builds the project (a few seconds).
3. `unity_status` should report `"connected": true`.

The Release build output (`artifacts/bin/UnityMcp/release`) is separate from the solution's Debug
build, so `dotnet build PingTheKiller.sln` still works while Claude Code is running the server.
`dotnet build -c Release` of the solution fails while the server runs (the DLL is locked).

Options: `--port N` / `--host H` arguments or the `UNITY_MCP_PORT` environment variable (server side).
The bridge port is fixed at 7801 (`McpBridge.Port`).

## Tools

| Tool | What it does |
|------|--------------|
| `unity_status` | Connection, compiling / importing / play-mode state, domain epoch, compile error count. Reports `connected:false` instead of failing. |
| `unity_read_console` | Console messages captured by the bridge (kept across domain reloads). Filter by type or text; optional `clear`. |
| `unity_get_compile_errors` | Compiler messages (file, line, column) of the latest compilation. |
| `unity_refresh` | `AssetDatabase.Refresh()`, then waits for compile + domain reload + import to settle. Returns `domainReloaded` and compile errors. |
| `unity_run_tests` | Test Runner API run (EditMode default, PlayMode allowed) with name / group / category / assembly filters; returns counts and failures, optional NUnit XML. `saveScenes=true` saves dirty scenes first. |
| `unity_play_mode` | `enter`, `exit`, `pause`, `resume`; waits until the editor settles. |
| `unity_screenshot` | Game view PNG. Play mode: `ScreenCapture` (UI included). Edit mode: off-screen render of the main camera (overlay UI not included). |
| `unity_execute_menu` | `EditorApplication.ExecuteMenuItem(path)`. |
| `unity_find_assets` | `AssetDatabase.FindAssets(filter, folders)` → path, GUID, type. |

Relative file paths (screenshots, test XML) resolve against the server's working directory, which is
the repo root when Claude Code launches it.

## Server ↔ bridge wire format

Each message is `[int32 Length, little-endian][UTF-8 JSON payload]`. `Length` is the payload byte
count and must be 1..8 MiB; anything else closes the connection.

```
request : {"id": 7, "cmd": "status", "args": {}}
response: {"id": 7, "ok": true,  "result": {...}}
          {"id": 7, "ok": false, "error": "message"}
```

Responses carry the request `id`, so calls can overlap. Bridge commands: `status`, `console`,
`compile_errors`, `refresh`, `tests_run`, `tests_result`, `play_mode`, `screenshot`, `execute_menu`,
`find_assets`. Code: `Tools/UnityMcp/BridgeFraming.cs` and `Client/Assets/Editor/McpBridge/McpBridge.cs`
(keep both in sync).

Threading on the Unity side: an accept thread plus a reader and a writer thread per client only frame
and parse. Commands go to a `ConcurrentQueue` that `EditorApplication.update` drains on the main thread
(at most 32 per frame). Multi-frame commands (Play-mode screenshot) are polled each frame.

Calls whose reply can be lost to a domain reload (`refresh`, `play_mode enter`, tests) are finished
server-side: the server polls `status` and treats disconnects as "still reloading" until the editor
has stayed idle for a settle period.

## Timeouts and errors

- Bridge down: a call waits up to 3 s for a connection, then fails with
  "Unity editor not connected … domain reload in progress". It never hangs.
- Connection drops mid-call: the call fails immediately with "connection … was lost".
- Bridge connected but silent: the per-call timeout applies (30 s default; refresh 300 s,
  tests 600 s, menu 120 s; most tools take `timeoutSeconds`).

## Troubleshooting

- **`connected:false` for a long time**: check the editor is open on `Client/`, then run
  *Tools → PtK MCP Bridge → Log Status*. If the port is taken (another editor instance holds 7801),
  the Console shows `Cannot listen on 127.0.0.1:7801` and the bridge retries every 2 s. Use
  *Restart Listener* after freeing the port.
- **Calls time out while connected**: a modal dialog (save prompt, error box) or a long import is
  blocking the main thread. Close the dialog in the editor.
- **`unity_run_tests` refuses with "unsaved changes"**: the Test Runner would pop a modal Save
  dialog. Pass `saveScenes=true` (scenes with a path), or save the untitled scene in the editor.
- **Batchmode**: the bridge is off in `-batchmode` runs (CI, `-runTests`) so it never competes for the
  port. Set `UNITY_MCP_BATCH=1` in the environment to enable it there.
- **CoplayDev `com.coplaydev.unity-mcp`** (in `Client/Packages/manifest.json`) is a separate MCP
  package. It uses port 6400 and the `Window/MCP for Unity` menu, so it does not collide with this
  bridge (7801, `Tools/PtK MCP Bridge`). Both can stay installed.
- **Elevated editor**: Unity 6.3 shows an "Administrator Privileges Detected" dialog when started
  from an elevated shell; the bridge only listens once the editor finishes loading.

## Tests

```
dotnet test Tools/UnityMcp.Tests
```

Covers framing (round trip, partial reads, invalid lengths, truncation), the client (concurrent ids,
no-bridge fast failure, error replies, timeouts, drop mid-call, reconnect after a bridge restart on the
same port, waiting within the grace period) and the editor-state waiter (riding through a reload).
