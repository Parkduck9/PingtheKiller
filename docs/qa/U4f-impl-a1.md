# U4f implementation report, attempt 1 (Claude subagent)

Status: BLOCKED on spec point 7. The split is done and compiles, but 17 EditMode tests fail.

Files
- WorldView.cs: MonoBehaviour, owns SnapshotBuffer/localId/role, delegates Accept/Render/SetLocalLook; public API unchanged.
- WorldMaterials.cs: MakeMaterial, Primitive, material list, Dispose; holds the shared Survivor colour (single definition).
- CharacterViews.cs: avatars, Killer model, dither, Killer/Signal colours, Render + RenderKiller, KillerAlpha/VisorAlpha, KillerBodyScale/KillerDrawable (WorldView forwards).
- WorldProps.cs: floor, walls, terminals, Exit ring, Plinth/PanelIdle/Neutral colours.

Results
- EditMode baseline: 1187 total, 1187 passed, 0 failed.
- EditMode after: 1187 total, 1170 passed, 17 failed.
- BuildWindows: "Build Finished, Result: Success."

Cause of the 17 failures: they read `World/WorldView.cs` as text (QaPaths.ReadScript / Code) and assert on method bodies (`Accept(`, `MakeTerminal(`, `MakeKillerModel(`, colour literals, `new Material(`, `CreatePrimitive(`). Any split moves that text. Tests were not edited; they need to read the new files (KillerAnonymityQa, KillerColourRolesQa, LightingQa, MapQa, KillerAnonymityReverify, SurvivorKillerRenderingReverify, RulesReadoutQa, SurvivorFeedbackQa, SurvivorFeedbackTests).

Side effect: Unity batch runs reserialized DefaultVolumeProfile.asset, PC_RPAsset.asset and UniversalRenderPipelineGlobalSettings.asset (outside owned files); new .meta files were generated.

## Attempt 2 (authorised test repoint)

Result: EditMode 1187 total, 1187 passed, 0 failed (baseline 1187/1187). BuildWindows not re-run (passed in attempt 1).

Repointed the 17 source-text tests (9 files) with no assertion changed, loosened or removed:
- New helper `Tests/EditMode/QA/WorldSources.cs` (ReadAll over the 4 files, file names). The non-QA `SurvivorFeedbackTests` is a separate asmdef, so it has its own private `WorldFiles` list.
- Whole-view checks (absence, counts, colour literals, MakeMaterial path, shared map, Exit rule) read all four files.
- Avatar `Accept` break/guard checks and RenderKiller/MakeKillerModel read CharacterViews; terminal/Exit `Accept` checks read WorldProps.
- The Killer-entry reader whitelist now names CharacterViews.cs in place of WorldView.cs (the file that holds that read).
