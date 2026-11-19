# S2-strip QA report (attempt 1) — Fable

Reviewed: commit `5c89455` on `claude/s2-strip` (implementer: Claude Opus 5.5). Reviewer: Claude Fable 5.2.
Tools: Windows PowerShell 5.1, `makeappx.exe` (SDK 10.0.26100.0), Unity 6000.3.25f1 in batch mode only (one dev build, step 6). No install, no certificate, no system setting touched; the user's running Unity editor was not touched (this worktree had no `Temp\UnityLockfile`).

**Verdict: PASS** — no blocker, no major; 3 minor notes, none needs a change before commit.

Not tested: `BuildScript.VerifyReleaseManaged` (Unity-side stand-alone check; the implementer's `verify-fake.log` line 383 shows it rejecting), `build-release.ps1` end to end (it would rebuild the release; I read it only), install, Store certification.

## Checks

| # | Check | Result |
|---|---|---|
| 1 | Filter scope | PASS. `ReleaseAssemblyFilter.Active` is a static bool, default false, set true only in `BuildWindowsRelease` and reset in `finally`, so an exception in `Build()` cannot leave it on. A domain reload resets statics to false, which fails safe: the filter would be off and `AssertNoEditorTooling` then rejects the output. `BuildWindows` never sets it. No other `IFilterBuildAssemblies` in the project. Confirmed by the dev build in row 6. |
| 2 | Release output | PASS. `Client/Builds/StoreWin64/PingTheKiller_Data/Managed`: 132 DLLs, none matching `MCPForUnity*` or `Newtonsoft*`. `ScriptingAssemblies.json` and `RuntimeInitializeOnLoads.json`: no MCP/Newtonsoft entry. No remaining Managed DLL contains the strings `Newtonsoft` or `MCPForUnity` (no dangling assembly reference). `globalgamemanagers`, `level0`, `sharedassets0.assets`: no hit. `globalgamemanagers.assets`: 7 MonoScript records (see N1). |
| 3 | Runtime | PASS. Exe from the unpacked `Packaging/out` package (SHA-256 `54D8C1DB...4D4D2E`, 67,967,985 bytes; 0 forbidden files, 132 Managed DLLs), `-ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-quit-after 60`: exit 0, 240 log lines. No match for Exception, TypeLoad, "could not be loaded", FileNotFound, "referenced script", missing, Newtonsoft, MCP. Only unrelated hit: `d3d12: failed to query info queue interface` (normal on a release player). |
| 4 | Pack guard | PASS. Temp copy of the release build: clean copy packs (exit 0). With a 3-byte dummy in `Managed`: `Newtonsoft.Json.dll`, `newtonsoft.json.DLL`, `MCPForUnity.Runtime.dll`, `MCPForUnity.Editor.Extra.dll` each give exit 1, "RELEASE BUILD REJECTED: ... found PingTheKiller_Data\Managed\<name>", no msix written, no `%TEMP%\ptk-msix-*` left. A dummy at the build root is also rejected (the scan is recursive). |
| 5 | Package files | PASS. `git diff e9eaaf2 5c89455 -- Client/Packages Client/ProjectSettings` is empty. `manifest.json` still lists `com.coplaydev.unity-mcp`. Still unchanged after my dev build. |
| 6 | Dev build | PASS (run). Unity batch `-executeMethod BuildScript.BuildWindows`: exit 0, log `Build Finished, Result: Success` and `[U4] Windows build succeeded`, no `[S2] Release build: excluded` line. `Builds/Win64/.../Managed`: 134 DLLs including `MCPForUnity.Runtime.dll` (30,208 bytes) and `Newtonsoft.Json.dll` (691,712 bytes). The release folder was not touched. |
| 7 | Docs | PASS. `MSIX_PACKAGING.md` §0 step 1 and the new "Editor tooling is stripped" section match the code and scripts. `THIRD_PARTY.md` rows match: log lines 2921, 2939, 9395 say what is cited; `release-managed.txt` has 132 lines and no forbidden name; newtonsoft-json is 3.2.2, depth 1, and only unity-mcp depends on it. |

## Notes

**N1 (minor, no action) — 7 MCP MonoScript records remain in `globalgamemanagers.assets`.**
Names: `UnityObjectIdCompat`, `UnityFindObjectsCompat`, `UnityCompatShims`, `UnityPhysicsCompat`, `ScreenshotUtility`, `UnityAssembliesCompat` (`MCPForUnity.Runtime.Helpers`) and one converter (`MCPForUnity.Runtime.Serialization`). They cannot cause a runtime error or a "missing script" warning: that warning is raised when an object in shipped content points at a script that cannot be loaded, and nothing does. Evidence: none of the package's Runtime script GUIDs appears in any `.unity`, `.prefab`, `.asset`, `.mat` or `.controller` under `Client/Assets` or `ProjectSettings`; the built scene `Client.unity` has no MonoBehaviour at all; the only MonoBehaviour in the package runtime (`ScreenshotCapturer`) is created by MCP code at run time; the 60 s run log is clean. The records are class names only.

**N2 (minor, no action) — a package-name list in `resources.assets` contains the string `com.unity.nuget.newtonsoft-json@3.2.2`.**
It is a text list of package ids, not code and not an assembly reference. Mentioned only so a later string search does not raise a false alarm.

**N3 (minor, wording) — `THIRD_PARTY.md` says "stripped by build-release.ps1".**
The strip is done by `BuildScript.BuildWindowsRelease`; the script only runs it and re-checks. Running the Unity method directly strips as well, so the statement is safe, just imprecise.

## State left behind
- `Packaging/out/PingTheKiller_1.0.0.0_x64.msix` untouched (hash unchanged).
- `Packaging\layout` was rewritten by my clean-copy pack run with the same build content and placeholder identity.
- The dev build rewrote line endings in 18 asset/settings files (zero content diff); reverted with `git checkout`. `Client/Builds/Win64` (created by my build) deleted. Unpack, dummy and temp folders deleted. Worktree is clean apart from this report.
