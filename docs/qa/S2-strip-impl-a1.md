# S2-strip implementation, attempt 1 (Claude Opus, 2026-11-19)

**Approach.** No manifest edit. `BuildWindowsRelease` enables an `IFilterBuildAssemblies` filter that drops `MCPForUnity*.dll` and `Newtonsoft.Json.dll` from the player, then throws `RELEASE BUILD REJECTED` if either is in `Managed`. `Packaging/build-release.ps1` runs the Unity batch build, fails if `manifest.json`/`packages-lock.json` change, re-checks the folder, and with `-Pack` calls `pack-msix.ps1`, which also refuses such a folder (shared `release-guard.ps1`). Why not remove the package: it is a git package, there is no global git cache (`%LOCALAPPDATA%\Unity\cache` = npm, packages, upm), so restoring it would need GitHub. Newtonsoft is depth 1, required only by unity-mcp. No project code references either (grep: only `ArchitectureTests.cs` forbids `Newtonsoft`).

**Evidence** (`docs/qa/S2-strip/`):
- `build-release.log` 2921, 2939 (excluded), 9395 (success); `release-managed.txt`: 132 DLLs, none forbidden.
- Package files' SHA256 identical before/after; `git diff --stat` shows only owned files.
- Guard: dummy DLLs → Unity exit 1 (`verify-fake.log` 383); `pack-msix.ps1` exit 1, no msix. Clean folder passes.
- Pack: 0 errors, 64.8 MB. Unpacked: 0 forbidden DLLs. Offline run exit 0, 0 exceptions, no missing-assembly lines.
- `dotnet build -warnaserror`: 0 warnings.

**Unverified/risks.** Dev `BuildWindows` not rebuilt (filter off by flag). 7 MCP MonoScript name records remain (no code).
