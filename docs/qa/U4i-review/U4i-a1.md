# Brief U4i — attempt 1: Toon shading (no outlines)

- **Implementer:** Claude Opus subagent. This is the quota fallback, because Codex is out of credits.
- **QA:** Gemini Flash `-high`, visual review.
- **Base:** branch `claude/u4h-world-art` (bcbb4bb). Work on `claude/u4i-toon` in `C:\Unity\MultiPlayGame\PtK-work\u4i`.
- **Parallel units:** U6a and U6e run at the same time in other worktrees.

Read first:
- `AGENTS.md`
- `docs/ui/SLOP_DEFINITION.md` (mandatory)
- `docs/ui/HUD_SPEC.md` §0 palette, §4.6 and §6
- `docs/qa/U4h-impl-a1.md`

Do **not** commit.

## Goal

The user chose **toon shading only, with no black outlines** (decision 2026-11-03).

`Toon Shaders Pro` is already imported but unused (`Client/Assets/ThirdParty/Toon Shaders Pro`, shader `Toon Shaders Pro/URP/Toon`). Every character and world material still uses URP Lit.

Move characters and world art to the Toon shader. The game should read as a clean, stylised night city that still matches the SLOP palette and the U4h night lighting.

## Hard rules

1. **No outline passes.** Do not use `HullOutlines`, `Outline` or `MaskObject` from the package. Do not add an inverted-hull pass or a renderer feature.
   - The only outline in the game stays `PtkOutline`: the Killer view's `signal` outline on transmitting Survivors (HUD_SPEC §6).
   - That outline must stay as visible as it is today.
2. **No glow.** Set rim lighting to off (`_RimColor` alpha 0 or its threshold out of range) and keep specular off or very low. SLOP V3: no glow, no gradients.
3. **Keep every existing look contract unchanged.**
   - `CharacterArt`'s opaque, fade and dither variants, including the camera-near dither from U4g.
   - The Killer view's single shared neutral Survivor material (HUD_SPEC §6, N1).
   - `WorldProps.SetTerminalLook` and `SetExitLook` colours.
   - The Hovl exit marker, which keeps its own particle material.
   - The primitive fallback when art is missing.
   - Fog.
4. **Fallback.** If `Shader.Find("Toon Shaders Pro/URP/Toon")` returns null, fall back to URP Lit, exactly as today. Add the shader to the build:
   - add it to `Always Included Shaders`, or reference it from a material under `Resources/`;
   - confirm that the **built exe** renders toon and not pink or Lit.
5. **No desktop input automation**, ever: no SendKeys, PowerShell input simulation, AutoHotkey or window focusing. Earlier keystrokes leaked into the user's other windows.
   - Screenshots come only from the `-ptk-*` DevCapture flags or EditMode/editor renders.
   - If a shot cannot be staged that way, skip it and report the gap.

## Look targets

- **Bands.** 2 bands for world art; 2–3 bands for characters (`_UseSecondThreshold`). Keep the shadow tint cool and dark, but never pure black, so the walls still read at night.
- **Ground.** Keep it Lit, or use toon with a soft threshold, so the ground does not band visibly in fog.
- **Readability matches U4h or improves:**
  - Survivors versus walls in both roles.
  - Terminal states at 3 m.
  - Exits.
- **Colour.** Tune `_LightTint`, `_MiddleTint` and `_ShadowTint` against `SceneLighting`. Do not change the lights unless the bands do not read; if you do change them, give the reason in the report.

## Owned files

- `Client/Assets/Scripts/World/WorldMaterials.cs`, `CharacterLook.cs` and `CharacterArt.cs`: the material and shader choice only.
- `Client/Assets/Scripts/Killer/KillerWorldLayer.cs`: line 70 fallback only.
- `Client/Assets/Editor/Setup/WorldArtSetup.cs` and `CharacterArtSetup.cs`: create or convert materials to Toon.
  - Rerun both builders so the assets and `Resources/*.asset` are regenerated.
- The `.mat` files under `Client/Assets/Art/**` and `Resources/WorldLit*.mat`.
- `Client/Assets/Shaders/PtkDither.shader` (or wherever it lives): only if the dither variant must match toon banding. Otherwise leave it.
- `ProjectSettings/GraphicsSettings.asset`: Always Included Shaders.
- New: `Client/Assets/Tests/EditMode/ToonShadingTests.cs`.
- `docs/ui/HUD_SPEC.md`: one short "Shading" note under the world-art section.
- Reports: `docs/qa/U4i-impl-a1.md` and `docs/qa/U4i-screens/`.

## Acceptance

**Tests and build**
- EditMode all green with 0 compile errors.
  - Command: `AGENTS.md` §3, with `-testResults "$PWD/docs/qa/u4i-runs/editmode.xml"`.
  - The first import in a fresh worktree is long.
- New tests:
  - every character and world material uses the Toon shader, except the documented exceptions (ground if Lit, the exit marker, the dither variant);
  - no material uses any package outline shader;
  - rim is off;
  - the Lit fallback is used when the shader is missing;
  - the Killer-view Survivor material is still shared and neutral.
- `BuildScript.BuildWindows` succeeds.

**Screenshots** (look at each PNG)
- Use the U4h shots, before and after, at 720p and 1080p:
  - survivor-3p
  - killer-inview, which must show the `signal` outline
  - hacking
  - alley
  - plaza-wide
  - terminal close-up
  - exit-open
- At least survivor-3p and killer-inview must come **from the exe**.
- Self-score each shot against SLOP §9.

**Revert before you finish:** the re-serialized files that Unity touches but you did not intend to change (DOTween, URP, ProjectSettings other than GraphicsSettings). Use `git checkout --`.

## Report

`docs/qa/U4i-impl-a1.md`, at most 250 words. Start with the header note "quota fallback: Codex out of credits". Include:
- files changed;
- the shader settings chosen (tints, thresholds);
- the exceptions;
- tests;
- the build result;
- the screenshot list;
- the SLOP self-score;
- what is unverified.
