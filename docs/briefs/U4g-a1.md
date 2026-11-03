# Brief U4g — attempt 1: Character art (Robot Kyle Killer, Rio Survivors, animation)

Implementer: Claude Opus subagent (Codex is unavailable in this session; CLAUDE.md quota-fallback route). It runs **in parallel with U4h (world art)** in a separate git worktree. Read `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md`, and `docs/ui/HUD_SPEC.md` §0.4, §4.10, §6.2, §6.3 first. Do **not** commit.

## Goal

Replace the capsule-and-visor characters with the imported art. U4f split the code: characters now live in `CharacterViews.cs`.
- The **Killer** (seen by Survivors in line of sight) becomes Robot Kyle.
- **Survivors** become Rio, with idle, walk and Lagging animation.
The look must keep every rule the capsules encode today.

## Owned files

- `Client/Assets/Scripts/World/CharacterViews.cs`, `Client/Assets/Scripts/World/CharacterLook.cs`
- `Client/Assets/Scripts/Killer/KillerWorldLayer.cs`. Only the character parts: `Get`, `Part`, `Pose` and the instance pool. Do **not** change blip matching, classification or marker drawing.
- `Client/Assets/Resources/Shaders/PtkDither.shader`, `PtkOutline.shader`, and new shaders under the same folder
- New: `Client/Assets/Scripts/World/CharacterArt.cs` (a ScriptableObject catalog), `Client/Assets/Resources/CharacterArt.asset`, and `Client/Assets/Art/Characters/**` (animator controllers, override materials, avatar masks)
- New editor builder: `Client/Assets/Editor/Setup/CharacterArtSetup.cs`. It creates the catalog and controllers reproducibly, idempotent like `UrpSetup.cs`.
- New tests: `Client/Assets/Tests/EditMode/CharacterArtTests.cs`
- `docs/qa/U4g-impl-a1.md` and `docs/qa/U4g-screens/`

Do not touch `WorldProps.cs`, `WorldMaterials.cs`, `SceneLighting.cs`, `ClientApp.cs`, `Resources/WorldLit*.mat`, or `Assets/ThirdParty/**`. Vendor files stay pristine: make copies or overrides under `Art/Characters/`.

## Spec

1. **Runtime access.** Load the art only through `Resources.Load<CharacterArt>("CharacterArt")`, which holds the prefab, model, controller and material references. If the catalog is missing, fall back to today's capsules so tests and headless runs never break.
2. **Gameplay dimensions are fixed.** `CharacterLook.Height` 1.8 m, radius 0.4 m (`FixedMap.PlayerRadius`), feet at y = 0, facing +Z at yaw 0. Scale each model so its bounds height is 1.8 m (Killer: `KillerBodyScale` total height, 10 % taller, as today). Remove colliders and do not add any. Turn off shadow casting on characters (HUD_SPEC 4.10 forbids a Killer shadow; do the same for Survivors so it stays consistent).
3. **Animation** (Survivor view and Killer view):
   - Humanoid Animator with Idle, Walk and Run from `Kevin Iglesias/Human Animations/Animations/Male` (Rio) or Kyle's own clips (Kyle). Both rigs are Humanoid, so pick whichever looks right.
   - Root motion off. Position stays fully server/interpolation-driven.
   - Speed is derived from the interpolated position delta per frame, smoothed. Idle below 0.5 m/s, walk up to about 4 m/s, run above (Killer 4.6 m/s).
   - Lagging pose: a slowed, stumbling walk or a tilt.
     - Survivor view: keep the Signal-amber tint for other Lagging Survivors.
     - Killer view: HUD_SPEC §6.3 rule 5. Stepped at 8 Hz (`Animator.speed = 0` plus a manual `Update` at 8 Hz, or equivalent).
   - No hacking or rescue animation.
4. **Survivor view (CharacterViews).**
   - Other Survivors: Rio, tinted toward `ally` teal `#3FB8AF` through the base colour, so they still read as team at a glance. Lagging ones are tinted `signal` amber. The alpha from `SnapshotBuffer.Sample` is still applied.
   - Own character: Rio. When `SetLocalLook(dither: true)`, the whole model is drawn with a 40 % screen-door dither. Extend `PtkDither` to sample the base map, or use an equivalent. Facing uses `localFacing`.
   - Killer: Robot Kyle, tinted `danger` red `#E5484D` (one Killer colour per match), fading by alpha over 0.3 s exactly as today. All its materials fade, including the visor-equivalent: use transparent clones while the alpha is below 1, and opaque at 1 to avoid sorting artefacts. Keep `KillerAlpha` / `KillerVisorAlpha` meaningful for `DevCapture`: the visor alpha is the fade of the Kyle head/visor material.
5. **Killer view (KillerWorldLayer).**
   - One shared Rio model and **one shared neutral material** for every blip: no per-player colour (§6.2, §6.3 rule 4).
   - A 1 u `signal` outline on the silhouette. You may extend the inverted-hull `PtkOutline` to work on skinned meshes, or use the Toon Shaders Pro outline renderer feature. If you add a renderer feature, note it, because it touches `Assets/Settings/PC_Renderer.asset`. Then also own that file and tell me in the report.
   - Facing and walk/idle come from matched deltas only, as today.
6. **Performance.** Up to 8 characters. Share materials where the tint is the same (for example `MaterialPropertyBlock`). No per-frame allocations in `Render` or `Pose`.
7. **SLOP.** No glow, no rim light, no particles on characters.

## Source-text tests

Some QA tests under `Tests/EditMode/QA/**` read the World/Killer `.cs` sources and assert on text (for example `CreatePrimitive(`, colour literals, method names). U4f gives them a shared reader over `WorldView.cs`, `CharacterViews.cs`, `WorldProps.cs` and `WorldMaterials.cs`.

- If such a test fails **only** because you legitimately replaced the implementation detail it names, update it so it checks the same **intent** against the new code. For example, "no per-player colour in the Killer view" still holds, checked another way.
- Never weaken an intent: no deleted assertions and no skipped tests.
- List every test you changed in the report, with one line each on its old and new check.
- Behavioural tests must pass unchanged.

## Acceptance

- EditMode tests: baseline count + your new ones, all passing (except the known stale U4e QA test). `CharacterArtTests` covers:
  - the catalog loads;
  - model bounds height is 1.8 ± 0.05 m with feet at 0;
  - shadows are off and there are no colliders;
  - the fallback works with no catalog;
  - the Killer-view material is shared across instances.
- `BuildScript.BuildWindows` succeeds.
- Screenshots in `docs/qa/U4g-screens/` at 1280×720 and 1920×1080, using the existing `DevCapture` flags (see `docs/qa/U4e-impl-a1.md` for the recipe):
  - survivor-3p
  - survivor-behind
  - killer-inview
  - killer-crowd
  - killer-in-sight-injected
  - lagging-stutter
- Look at your own screenshots before reporting. A character that is pink, floating, sunk, T-posed, or facing backwards is a FAIL.

## Report

`docs/qa/U4g-impl-a1.md`, English, ≤ 250 words:
- files changed;
- test counts;
- build result;
- screenshot list;
- what you could not verify;
- any spec deviation, with the reason.
