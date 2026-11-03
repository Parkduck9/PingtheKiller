U4g attempt 1 — INCOMPLETE; not committed.

**Changed:** `CharacterViews.cs`, `CharacterLook.cs`, `KillerWorldLayer.cs`, `KillerScene.cs`, `PtkDither.shader`, `PtkOutline.shader`; added `CharacterArt.cs`, `CharacterArtSetup.cs`, `CharacterArtTests.cs`, and `CloseRangeVisibilityTests.cs`, with metadata. Implements art integration, locomotion, stepped Lagging, shared neutral materials, textured double-sided dither, skinned outlines, and both close-range fixes. Marker/edge classification remains separate.

**Verified:** `dotnet build PingTheKiller.sln` passed with workspace output/SDK overrides; `dotnet test Server.Tests` passed 836/836. Standalone Unity Roslyn compilation of runtime, builder, and new tests passed. This does not establish Unity Editor compilation or test success. `git diff --check` passed.

**QA source-text change:** `LightingQaTests.TheMapRendersThroughOneMaterialPathWorldViewMakeMaterial`: previously counted one primitive factory; now checks two factories and explicitly requires the character fallback to use `MakeMaterial`. No behavioural tests changed.

**Blocked:** Unity licensing cannot access AppData configuration; even locally resolved packages were refused (`Registered 0 packages`). Catalog/prefabs/controllers were not generated. EditMode baseline plus 14 new cases, builder idempotence, and `BuildScript.BuildWindows` remain unverified. Runtime therefore retains capsule fallback.

**Screenshots:** none; all requested captures, both resolutions, and the close-range pair remain outstanding. Visual pose, dimensions, skinning, and shaders require verification.

Evidence: `U4g-screens/verification/`. Temporary package overrides restored; vendor files and renderer settings unchanged.

## Attempt 3 (Claude Sonnet, quota fallback: Codex out of credits)

Design unchanged. All 9 failures were in `CloseRangeVisibilityTests.cs` and all were wrong tests, not product bugs; the product behaviour (dither within or at 0.3 m of another character's bounds, none above) was confirmed correct. No product or Editor-setup code changed.

| Failure | Cause | Change (test only) |
|---|---|---|
| `CameraInsideAnotherCharacterUsesDitherAndKeepsItVisible` (True/False), `CameraInsideFallbackCharacterUsesDitherAndKeepsItVisible` (True/False), `CameraMarginChangesEveryMaterialAndPreservesKillerFade(..., 0.29f)` (4 cases) | The EditMode scene already contains a "Main Camera". `Camera.main` (used by `CharacterViews`) resolved to that camera, not the test's camera tagged `MainCamera`, so `NearCamera` was evaluated from the wrong position and no dither was applied. Confirmed with a temporary diagnostic (removed): shader loads and is supported, `Camera.main` was "Main Camera". The `.31f` cases passed only because they expected no dither. | `CheckCloseRange` untags any other `MainCamera` cameras for the duration of the test, asserts `Camera.main` is the test camera, and restores the tags in `finally`. All assertions are unchanged. |
| `ProximityIncludesThePointThreeMetreMargin(0.31f, false)` | The test derived its bounds from `Renderer.bounds` of a never-rendered skinned mesh. Those are stale and smaller than the true bounds, so a 0.31 m gap measured from them was inside the real 0.3 m margin. The sibling test already uses current transforms for this reason. | The test now uses the existing `BoundsFromLocalCorners(root.transform)` helper (current transforms and local bounds). Same 0.29 true / 0.31 false intent and assertion. |

Only `Client/Assets/Tests/EditMode/CloseRangeVisibilityTests.cs` was edited; no assertion weakened, no case removed.

**EditMode (full run, `docs/qa/u4g-runs/editmode3.xml`):** total 1215, passed 1215, failed 0. The `CloseRangeVisibilityTests` fixture alone: 19/19.

**Windows build:** `BuildScript.BuildWindows` succeeded ("Build Finished, Result: Success"), output `Client/Builds/Win64/PingTheKiller.exe`, log `docs/qa/u4g-runs/build.log`.

**Side effect to note:** running the build and tests made Unity rewrite `Client/Assets/Settings/PC_RPAsset.asset`, `Client/Assets/UniversalRenderPipelineGlobalSettings.asset`, `Client/ProjectSettings/GraphicsSettings.asset` and `Client/ProjectSettings/ProjectSettings.asset` (outside the brief's owned list; Unity-generated, I did not revert them because I may not change git state). Review the diff before committing and drop them if unintended.

## Attempt 3b (Claude Sonnet, quota fallback; screenshots and final gate)

**Product change since 3:** `ClientApp.cs` now tags the "Role camera" `MainCamera` (orchestrator fix, kept). No other product code changed in this attempt.

**EditMode (full run, `docs/qa/u4g-runs/editmode4.xml`):** total 1215, passed 1215, failed 0.
**Build:** `BuildScript.BuildWindows` succeeded ("Build Finished, Result: Success", `docs/qa/u4g-runs/build2.log`); `Assembly-CSharp.dll` rebuilt this attempt.

**Captures:** built exe, offline bots, `-screen-fullscreen 0`, 1280x720 and 1920x1080. Event shots come from `-ptk-shots`. Close-range shots could not be staged with any flag (no flag places a character near the camera, and I did not edit DevCapture), so I grabbed timed frames of a normal offline run with an external `PrintWindow` loop (a script outside the repo, no product change) and picked the frames where a character is near the camera. These are marked "timed grab" below. All files are in `docs/qa/U4g-screens/`; I opened every one.

| File (`-720` and `-1080` both unless noted) | What I see | Result |
|---|---|---|
| survivor-3p | Rio (art model, not a capsule) from behind, teal tint, feet on the floor, upright, facing away from the camera, HUD correct | PASS |
| survivor-behind | Rio facing the camera (correct orbit 180), teal; a red Robot Kyle Killer and a teal ally in the distance, all upright and grounded | PASS |
| killer-inview | Killer view: neutral grey Rio, orange Signal outline, label "31 m"; small because the survivor is 31 m away | PASS |
| killer-crowd | Five or six grey outlined Rio models clustered, neutral, not pink | PASS |
| killer-in-sight (not "-injected") | `-ptk-fake-killer` cannot run offline: the offline server already sends a real Killer entry and the injector refuses a second one. The natural frame (`threat-3`) shows the red Robot Kyle running 5 to 10 m away beside the teal Rio, vignette on | PASS (natural, not injected) |
| lagging-stutter | Lagging own Rio with ghost frames (stepped stutter), red Robot Kyle with trailing ghosts, red vignette; allies in the distance | PASS |
| close-range-survivor-720 (timed grab) | The Killer (red Robot Kyle) 1.5 m beside the Lagging survivor, drawn and slightly translucent, not gone | PASS |
| close-range-survivor-ally-720 (timed grab) | An ally overlapping the own character during a rescue; both drawn, own character dithered | PASS (partial: the camera is not inside the ally) |
| close-range-survivor-1080 (timed grab) | The Killer fills the right third of the frame at about 1 m, red, ghost frames, visible not vanished | PASS |
| close-range-killer-1080 (timed grab) | Killer view: a survivor about 0.8 m ahead, head and shoulders on screen, neutral grey with an orange outline, shoulder strap visible. Before the fix this model would be disabled | PASS |
| close-range-killer-720-midrange (timed grab) | Survivor about 5 m ahead, neutral grey, upright. A true 1 m frame at 720 was not captured in 3 tries | not a close shot; 720 close-range-killer not obtained |

Checks across all shots: no capsules, no pink or missing-shader materials, no floating or sunk feet, no T-poses, no backwards-facing characters. Tints are right: ally teal, Killer red in the survivor view, neutral grey in the Killer view. The own character in the survivor view is not tinted differently from allies beyond the teal.

**Not obtained or limited:** `killer-in-sight-injected` (see above); a 720 `close-range-killer` at about 1 m; a camera physically inside an ally (the boom is 3 to 4 m behind, so only the Killer or an ally overlapping the own character shows up). The EditMode `CloseRangeVisibilityTests` (19 cases, passing) cover those cases numerically.

**Settings files (`PC_RPAsset.asset`, `UniversalRenderPipelineGlobalSettings.asset`, `GraphicsSettings.asset`, `ProjectSettings.asset`, `ShaderGraphSettings.asset`):** `git diff` shows no content change for any of them now. Byte-compare with `git show HEAD:<file>` is identical for all five (and for `DOTweenSettings.asset`). `git status` still lists them as modified only because of a stale stat cache and the CRLF/LF warning; I ran `git update-index --really-refresh` once to confirm, which only refreshes the cache. So these files are re-serialisation noise that Unity rewrote and then rewrote back; nothing in them is needed for the art. The art needs only the Resources shaders (`PtkDither`, `PtkOutline`) and `CharacterArt.asset`, which load by `Resources`. The only real settings-adjacent edit is `PingTheKiller.Editor.Setup.asmdef` (adds the `PingTheKiller.Client` reference for `CharacterArtSetup`), which is needed.

**Housekeeping:** `docs/qa/u4g-runs/` holds logs and XML (17 MB, includes `shots/*.log`); drop it before commit if not wanted. Per-run PNG folders were deleted; only `docs/qa/U4g-screens/` keeps images.
