# U4 implementation — attempt 1

Implemented networking, menus/lobby, primitive rendering, interpolation/fading, cameras, movement sequencing, and threat audio under `Client/Assets/Scripts/**`. Added Inter/OFL, URP/Lit materials, startup scene, EditMode tests, and BuildScript. Updated manifest, input handling, and build scenes. Preserved URP configuration; Shared/server/Tools sources untouched; no commit.

Verification:
- `csc.dll @Client/Temp/U4Verification/compile.rsp`: all client/editor/test and Shared sources compiled against installed Unity 6000.3 assemblies, zero errors/warnings.
- `dotnet Client/Temp/U4Verification/Runner.dll`: 35 checks passed, including real TCP fragmentation, periodic ping/pong, queue dispatch, disconnects, and release sequencing.
- Asset/reference audit and `git diff --check`: passed.
- Ignored Client/Temp contains fallback verification scaffolding.

Unity `-batchmode -nographics -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/U4-editmode.xml -logFile -` stalled before import: `Connection to channel LicenseClient-User refused`; `Licensing is not yet initialized`. Terminated it; no further Unity launch/build attempted. Standalone checks do **not** establish Unity compilation, EditMode success, or Windows build success.

Human verification remains: import/build, UI/navigation, camera framing, audio, live server integration. Assumptions: join result 0 succeeds; slot ID 0 is empty. U2a specifies no exact coordinates; MapSource isolates provisional centered geometry/Exit until Server.Core arrives. Terminals follow received state.
