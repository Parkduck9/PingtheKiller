quota fallback: Codex out of credits (implemented by Claude Sonnet 5.5)

# U6c-hatch implementation, attempt 1

**Files changed**
- `Client/Assets/Scripts/World/HatchView.cs` (new): the procedural hatch.
- `Client/Assets/Scripts/World/WorldView.cs` (small diff): creates the `HatchView`, calls `Set` in `Accept`, `Render` in `Render`, disposes it in `OnDestroy`.
- `Client/Assets/Tests/EditMode/HatchViewTests.cs` (new, 9 cases).
- `Client/Assets/Editor/Capture/HatchShot.cs` and `PingTheKiller.Editor.Capture.asmdef` (new).

**API**
- `WorldView.HatchShown` (bool) and `WorldView.HatchPosition` (Vector3), both read-only.
- `WorldView.Hatch` exposes the view for tests: `Built`, `Root`, `RimColor`, `ReducedMotionOverride`.
- No `ClientApp` change. Reduced motion is read from the persisted `GameSettings` each time the hatch goes from hidden to shown.

**Design**
- Steel coaming, 1.2 m across and 0.3 m tall, with a near-black opening, three grate bars and an open lid leaning back.
- All parts use `WorldMaterials` (toon). Nothing is emissive.
- Light rim, one shared material. It eases between two flat tones at 0.8 Hz, and is static under Reduced motion.
- Built lazily on the first `HatchVisible == 1`. Hidden with `SetActive(false)`. A snapshot only writes the position when it changed. Position snaps with no interpolation. Zero allocation per snapshot.

**Tests**
- EditMode: 1329 total, 1329 passed, 0 `error CS`. The baseline was 1320, plus 9 new.
- An earlier full run had one failure in `SurvivorKillerRenderingReverifyTests.SurvivorRole_OfflineLink_ReceivedKillerEntry...`. It passed on the next run, so I treat it as an existing flaky random-offline-match test that this unit does not touch. I did not rerun it three times.

**Build**
- `BuildScript.BuildWindows` finished with "Build Finished, Result: Success".

**Screenshots**
- `docs/qa/U6c-hatch-screens/`: 12 PNGs, opened and checked one by one.
- Hatch points are FixedMap indices 1 (25, 63) and 3 (66, 38.5). Index 0 has no clear 14 m standpoint.
- Framings are survivor 6 m, survivor 14 m and killer 10 m, at 1280x720 and 1920x1080.
- The game camera uses no post-processing, so none is applied.

**Readability self-assessment**
- It reads clearly at 6 m and 10 m: pale rim and lid edge against the dark road.
- At 14 m it is small (about 60 px wide at 1280) but still the lightest object in the alley.
- Seen end-on, the lid is only a sliver. The rim and coaming still carry the shape.
- A flat hatch seen from the killer's eye height foreshorens, so the lid does the work there.

**Risks**
- Hatch yaw is fixed, so the lid looks different from each side.
- The 0.8 Hz pulse is hard to judge from still images.
- The Killer first-person view can place the hatch behind a wall corner, but the server's line-of-sight rule normally prevents that.
- `Destroy` in edit-mode tests logs an error. The tests ignore it, as the other `WorldView` tests do.
- Unity re-serialised many unrelated asset files. I reverted them with `git checkout`, so only `WorldView.cs` is left modified.
