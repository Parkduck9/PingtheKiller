# U4h attempt 1 — world art on map v2 (implementer report)

Implementer: Claude Opus subagent (quota fallback: Codex is out of credits). Not committed.

**Changed:** `WorldProps.cs` (city path, `SetTerminalLook`, `SetExitLook(int, ExitLook)`, primitive fallback kept), `SceneLighting.cs` (night: cool key with hard shadows, warm fill, fog to #101113). **New:** `WorldArt.cs`, `Resources/WorldArt.asset`, `Editor/Setup/WorldArtSetup.cs` (menu "Ping the Killer/Setup World Art"), `Art/World/**` (21 MB, mostly LFS FBX), `WorldArtTests.cs`.

**Outside the owned list:**
- `PingTheKiller.Editor.Setup.asmdef` gains a `PingTheKiller.ServerCore` reference, which the builder needs.
- Two QA tests: see below.

**Tests:** EditMode 1237/1237 (17 new `WorldArtTests`). In one full run, `SurvivorKillerRenderingReverifyTests…U2eContract` failed once; it then passed 3/3 alone, so it is flaky and unrelated. **Build:** `BuildWindows` Success.

**QA tests changed (intent kept):**
- `KillerColourRolesQaTests` ×3: they read the colour statement in `Accept`; now they read `SetTerminalLook`/`SetExitLook`, and assert that `Accept` calls them.
- `LightingQaTests`:
  - The no-fog test now checks that fog goes to `Background` #101113 and starts at 25 m or more.
  - The no-shadows test now checks that only the key light casts shadows (hard) and the fill casts none.
  - The terminal-colour test now reads `SetTerminalLook`.

**Screens** (`docs/qa/U4h-screens/`, all opened):
- From the exe: survivor-3p, killer-inview, killer-menu, hacking, result and exit-open-killer at 720 and 1080; alley-1080.
- Editor batch renders: plaza-wide, obstacles, terminal, exit-open, overview (720).

**Not verified:**
- The Hovl marker inside an exe frame. The exit-open event fired while the Killer faced a wall.
- Draw calls (release build).
- Whether static batching engages.

**Deviations:**
- Upper storeys set back 1 m, so the camera, which can rise about 0.8 m into a footprint above 3 m, stays out.
- The Layer Lab assets are unused (fantasy style).
- `Models/Barricade 3.fbx` is an orphan.
