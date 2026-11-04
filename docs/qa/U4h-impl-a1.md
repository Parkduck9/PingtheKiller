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

## Attempt 2 (Claude Opus, quota fallback): fixes for the Gemini Flash QA FAIL

**1. Exit marker never played (major): fixed.**
- `SetExitLook(Open)` now activates the marker, tints each particle system and calls `Play`.
- `SetExitLook(Closed)` now calls `Stop(…, StopEmittingAndClear)` and then deactivates the marker.
- The builder now sets `loop = true` and `playOnAwake = false` on the prefab copy, so no vendor setting is relied on.
- **Found while rendering:** the Hovl material has `_Emission` 18, which turned the role tint white. The marker now uses its own copy, `Art/World/Materials/Exit marker MarkersAtlas1bcg.mat`, with emission 1.4. The tint now reads as ally teal or `text`, and the vendor file is untouched.
- **New and extended tests:**
  - `TheExitMarkerPrefabLoopsAndNeverPlaysOnItsOwn` (new).
  - `TheExitShowsItsMarkerOnlyWhileOpenTintedByRole` is extended to cover playing while open, emitting after `Simulate`, stopped with 0 particles when closed, and playing again on reopening.

**2. No terminal close-up (major): fixed.**
- `editor-terminal-states-720.png` shows idle, done for Survivors (teal) and done for the Killer (`text`) at about 3 m, with Rio for scale.
- `editor-exit-open-survivor-720.png` and `editor-exit-open-killer-720.png` show the open exit, with particles simulated for 1.5 s.
- The first editor render showed the device materials pink. A warm-up render removed it, and the player frame `survivor-3p-a2-1080.png` shows the devices correctly.

**3. Index guard (minor): fixed.**
- `SetExitLook` and `ExitLookOf` ignore an out-of-range index, and `SetTerminalLook` ignores a missing terminal. Each logs one warning and does not throw.
- New test: `AnExitIndexOutOfRangeIsIgnored`.

**Also:** device materials are now copies under `Art/World/Materials`, so `ThirdParty` stays clean.

**Verified:**
- EditMode: 1239/1239.
- `BuildWindows`: Success.
- A short player run with flags only logged no exceptions.

**Not done here:** reverting the re-serialisation noise files. My rules forbid `git checkout`, so they are left for the orchestrator. `git diff` shows no content change in them.
