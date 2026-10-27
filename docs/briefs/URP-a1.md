# Brief URP — attempt 1: Switch the Unity client to URP

Implementer: Codex (gpt-6-luna). Read `AGENTS.md` first; its rules are binding. Do **not** commit. Do **not** run Unity yourself — the orchestrator runs the batch command below after you finish (the Codex sandbox cannot write Unity's AppData).

## Goal

The Unity client (`Client/`, Unity 6000.3.25f1) currently uses the Built-in render pipeline. Purchased assets (Robot Kyle URP, Toon Shaders Pro for URP) need URP. Add URP and a one-shot editor setup method that creates and assigns the pipeline assets, so a single batchmode run converts the project.

## Owned files (create/modify only these)

- `Client/Packages/manifest.json` — add one dependency only
- `Client/Assets/Editor/Setup/UrpSetup.cs` (new)
- `Client/Assets/Editor/Setup/PingTheKiller.Editor.Setup.asmdef` (new, Editor platform only)
- `docs/qa/URP-impl-a1.md` (your report)

Do not touch `Shared/`, `docs/` (other than your report), or anything else. The working tree has unrelated uncommitted changes in `Shared/` and `docs/` — leave them alone.

## Spec

1. `manifest.json`: add `"com.unity.render-pipelines.universal": "17.3.0"` (the version bundled with 6000.3.25f1). Keep every existing entry, including the `com.coplaydev.unity-mcp` git dependency, and keep the file's formatting (2-space indent, alphabetical keys after the git entry as today).
2. `UrpSetup.cs`, namespace `PingTheKiller.Editor.Setup`, static class `UrpSetup`, public static method `Run()` usable via `-executeMethod PingTheKiller.Editor.Setup.UrpSetup.Run`:
   - Ensure folder `Assets/Settings`.
   - Create (or reuse if already present — the method must be idempotent) `Assets/Settings/PC_Renderer.asset` as a `UniversalRendererData`, rendering path **Forward+**.
   - Create (or reuse) `Assets/Settings/PC_RPAsset.asset` via `UniversalRenderPipelineAsset.Create(rendererData)`; set `supportsCameraDepthTexture = true` (toon outline needs depth), leave other defaults.
   - Assign it to `GraphicsSettings.defaultRenderPipeline` **and** to every quality level (`QualitySettings.SetQualityLevel(i, false)` + `QualitySettings.renderPipeline = asset` for each `i`, then restore the original level).
   - Set `PlayerSettings.colorSpace = ColorSpace.Linear` (currently Gamma).
   - `AssetDatabase.SaveAssets()`; log one summary line with `Debug.Log("[UrpSetup] ...")`.
   - On any exception: `Debug.LogError` and, when `Application.isBatchMode`, `EditorApplication.Exit(1)`.
3. asmdef: name `PingTheKiller.Editor.Setup`, `includePlatforms: ["Editor"]`, references `Unity.RenderPipelines.Universal.Runtime`, `Unity.RenderPipelines.Core.Runtime`. No `UnityEngine` reference issues with Shared (does not reference Shared).
4. Do not create scenes, volume profiles, or post-processing — out of scope.

## Acceptance (orchestrator verifies)

```
"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -quit -projectPath Client -executeMethod PingTheKiller.Editor.Setup.UrpSetup.Run -logFile docs/qa/urp-setup.log
```
- Exit code 0; log contains `[UrpSetup]` and no `error CS`.
- `Client/Assets/Settings/PC_RPAsset.asset` and `PC_Renderer.asset` exist; `GraphicsSettings.asset` `m_CustomRenderPipeline` and all 6 `QualitySettings.asset` `customRenderPipeline` entries reference the RP asset GUID; `m_ActiveColorSpace: 1`.
- Running the method a second time changes nothing (idempotent).

## Report

`docs/qa/URP-impl-a1.md`, English, ≤ 200 words: files changed, what you could verify without Unity, risks (API names you were unsure about in URP 17.3).
