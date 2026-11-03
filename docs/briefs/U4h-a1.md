# Brief U4h — attempt 1: World art (walls, floor, terminals, Exit, lighting)

Implementer: Claude Opus subagent (Codex is unavailable in this session; CLAUDE.md quota-fallback route). It runs **in parallel with U4g (character art)** in a separate git worktree. Read `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md`, and `docs/ui/HUD_SPEC.md` §0.4, §4.3, §4.7, §6.2 (Terminals, Exit) first. Do **not** commit.

## Goal

Replace the grey primitive world with the imported art, keeping the gameplay geometry exact. U4f split the code: world props now live in `WorldProps.cs`.

## Owned files

- `Client/Assets/Scripts/World/WorldProps.cs`
- `Client/Assets/Scripts/World/SceneLighting.cs`
- New: `Client/Assets/Scripts/World/WorldArt.cs` (a ScriptableObject catalog), `Client/Assets/Resources/WorldArt.asset`, and `Client/Assets/Art/World/**` (materials, prefabs you compose, textures you derive)
- New editor builder: `Client/Assets/Editor/Setup/WorldArtSetup.cs`. It creates the catalog and prefabs reproducibly, idempotent like `UrpSetup.cs`.
- New tests: `Client/Assets/Tests/EditMode/WorldArtTests.cs`
- `docs/qa/U4h-impl-a1.md` and `docs/qa/U4h-screens/`

Do not touch `CharacterViews.cs`, `CharacterLook.cs`, `KillerWorldLayer.cs`, `WorldMaterials.cs`, `ClientApp.cs`, `Resources/WorldLit*.mat` (characters still use them), `Assets/Settings/**`, or `Assets/ThirdParty/**`. Vendor files stay pristine: copy them under `Art/World/` if you need to change them.

## Available art (`Assets/ThirdParty/`)

- `Fries and Seagull/City 03/Models/*.fbx`: low-poly city with embedded materials. Includes buildings (House 1–10), fences, barriers, road pieces, street lamps, signs, cars, trash and dumpsters. Prefabs were not imported; compose your own.
- `Layer Lab/3D Props-CartoonBoxMap`: modular box-map tiles and props in 7 themes (prefabs and a texture atlas).
- `QAtmo/Explosive Device C4 & Defuse Kit/Prefabs`: ExplosiveDeviceC4, SapperKit, Decoder and others.
- `Hovl Studio/Map track markers VFX/Prefabs`: Marker 1–8 (zone, pillar, circle, arrows), as loop and one-shot.
- `Toon Shaders Pro/Shaders`: Toon lit shader and Shader Graph. Using it on world materials is optional.

## Spec

1. **Runtime access.** Load the art only through `Resources.Load<WorldArt>("WorldArt")`. If it is missing, fall back to today's primitives.
2. **Walls must match the gameplay boxes.** `MapGeometry` / line of sight / the camera sphere cast use the analytic `SharedMap.WallBox` AABBs at `MapGeometry.WallHeight` 3 m.
   - The visible wall must fill each box: no gap larger than 0.05 m, and it must not stick out of the box by more than 0.05 m, from the floor to 3 m.
   - No visual may block sight where the box does not (no props inside the walkable area).
   - You may add a cap or trim inside the box footprint, and decoration on top of walls up to about 3.5 m.
   - A good approach: wall meshes or tiled materials with world-space UVs fitted to each box, optionally with city fence or barrier pieces as a facade inside the box volume.
3. **Floor.** The 60 × 60 m arena reads as ground: road and pavement or tiled ground, dark enough that `signal`, `ally` and `danger` stay the brightest saturated colours on screen.
4. **Backdrop.** You may place City buildings, lamps and similar **outside** the 60 × 60 border walls, so the arena has a skyline and does not end in the void. The Killer camera's far plane is 180 m; keep it cheap (static, batched).
5. **Terminals** (radius 1.5 m, the server position is the centre):
   - A device on a plinth or crate, for example ExplosiveDeviceC4 on a box-map crate.
   - Footprint ≤ 1.2 × 1.2 m, height ≤ 1.2 m.
   - It shows three states without numbers, using the existing colour rules (`WorldProps` already computes them): **idle**, **done for Survivors** (`ally`), **done for the Killer** (`text`). Use a panel or screen material on the device. HUD_SPEC §6.2: a done terminal gets no marker, so "its 3D model shows a done state". No blinking, no particles.
6. **Exit** (radius 2.0 m):
   - Keep the flat ring at the exact radius (it is the escape boundary).
   - Add a Hovl zone, circle or pillar marker **only while open**, tinted `ally` for Survivors and `text` for the Killer.
   - Locked or not open: ring only, `muted`. This is state information, not decoration (SLOP).
7. **Lighting** (`SceneLighting`).
   - Keep the script-built lights. Retune the colours and intensities for the new materials so that walls read against the floor, and characters (about mid-grey to teal and red) read against both.
   - Shadows on the key light are allowed if the cost is reasonable at 1080p, with 8 characters and a skyline.
   - The camera clear colour stays as `ClientApp` sets it (`bg #101113`). Distance fog toward that colour is allowed.
8. **Performance.** Static batching or a merged mesh for walls and backdrop. No per-frame allocations in `Accept`.

## Source-text tests

Some QA tests under `Tests/EditMode/QA/**` read the World `.cs` sources and assert on text (for example `CreatePrimitive(`, colour literals, method names). U4f gives them a shared reader over `WorldView.cs`, `CharacterViews.cs`, `WorldProps.cs` and `WorldMaterials.cs`.

- If such a test fails **only** because you legitimately replaced the implementation detail it names, update it so it checks the same **intent** against the new code. Examples: the done-terminal colour rule, and the Exit open colour per role.
- Never weaken an intent: no deleted assertions and no skipped tests.
- List every test you changed in the report, with one line each on its old and new check.
- If U4g changes the same test file in parallel, keep your edit minimal, because the orchestrator merges both.

## Acceptance

- EditMode tests: baseline count + your new ones, all passing (except the known stale U4e QA test). `WorldArtTests` covers:
  - the catalog loads;
  - for every `FixedMap` wall, the renderer bounds of its visual match the box within 0.05 m;
  - there are no colliders;
  - no renderer lies inside the walkable area other than terminals, the Exit and the floor;
  - the fallback works with no catalog;
  - the terminal state material changes.
- `BuildScript.BuildWindows` succeeds.
- Screenshots in `docs/qa/U4h-screens/` at 1280×720 and 1920×1080, using the existing `DevCapture` flags (see `docs/qa/U4e-impl-a1.md` for the recipe):
  - survivor-3p
  - killer-menu or killer-inview (a wide Killer view)
  - a terminal close-up (hacking)
  - exit-open
  - result
- Look at your own screenshots before reporting. Pink materials, floating props, z-fighting, or a wall that does not match the line of sight are a FAIL.

## Report

`docs/qa/U4h-impl-a1.md`, English, ≤ 250 words:
- files changed;
- test counts;
- build result;
- screenshot list;
- what you could not verify;
- any spec deviation, with the reason.
