# URP-impl-a1

Implementer: Claude (Sonnet 5.5) in place of Codex. Unity not run.

## Files changed
- Client/Packages/manifest.json: added `com.unity.render-pipelines.universal` 17.3.0 (one line).
- Client/Assets/Editor/Setup/UrpSetup.cs (new)
- Client/Assets/Editor/Setup/PingTheKiller.Editor.Setup.asmdef (new, Editor only)

## API names checked against the 17.3.0 package source
- `UniversalRendererData.renderingMode` (setter) with `RenderingMode.ForwardPlus` (= 2), in `UnityEngine.Rendering.Universal`.
- `UniversalRenderPipelineAsset.Create(ScriptableRendererData)`, `.supportsCameraDepthTexture` (get/set), `.packagePath` (public).
- `ResourceReloader.ReloadAllNullIn(object, string)`, a public static in `Unity.RenderPipelines.Core.Runtime`. It reloads the renderer's shaders after `CreateAsset`.
- `GraphicsSettings.defaultRenderPipeline`, `QualitySettings.renderPipeline`: standard engine API, not in the package. I did not look them up.

## Verified without Unity
- Package source read only. Nothing compiled or run.
- manifest diff is exactly one added line.

## Risks
- `PostProcessData.GetDefaultPostProcessData` is internal, so the renderer's `postProcessData` stays null. This is out of scope (no post-processing) but means volume post-effects need it assigned later.
- Idempotency relies on guards before every write, so a second run should make no asset changes. Not tested.
- The asmdef refers to assemblies by name. If the Editor assembly does not resolve them, check that the package finished importing before the `-executeMethod` run.
