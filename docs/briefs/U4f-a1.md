# Brief U4f — attempt 1: Split WorldView before art integration (no behaviour change)

Implementer: Claude subagent (Codex is unavailable in this session: its auto-approve run was denied by the permission policy; CLAUDE.md quota-fallback route). Read `AGENTS.md` first. Do **not** commit.

## Goal

`Client/Assets/Scripts/World/WorldView.cs` mixes characters (Killer model, Survivor avatars) with world props (floor, walls, terminals, Exit ring). Two art units will run next in parallel (U4g characters, U4h world). Split the file so that they never touch the same file. **Pure refactor: the rendered result, public API and tests must be identical.**

## Owned files

- `Client/Assets/Scripts/World/WorldView.cs` (modify)
- `Client/Assets/Scripts/World/WorldMaterials.cs` (new)
- `Client/Assets/Scripts/World/CharacterViews.cs` (new)
- `Client/Assets/Scripts/World/WorldProps.cs` (new)
- `docs/qa/U4f-impl-a1.md` (report)

## Spec

1. **WorldView** stays the MonoBehaviour that `ClientApp`, `DevCapture` and the tests use. Keep every public/static member with the same signature and semantics: `KillerBodyScale`, `KillerAlpha`, `KillerVisorAlpha`, `KillerDrawable`, `Initialize`, `Accept`, `SetLocalLook`, `Render`, `LocalPosition`. It owns the `SnapshotBuffer`, `localId`, `role` and delegates:
   - `Accept` → `snapshots.Add`, then `characters.Accept(state)`, then `props.Accept(state)`.
   - `Render` → `characters.Render(now, snapshots)` (avatars, then Killer model, as today).
2. **WorldMaterials** (plain C# class, not a MonoBehaviour): today's `MakeMaterial`, `Primitive` and the material list with a `Dispose()` that destroys them. Constructed with the parent `Transform`. Both new classes receive the same instance. After this unit it is **frozen**: U4g/U4h add helpers in their own files.
3. **CharacterViews** (plain C# class): `KillerModel`, `Avatar`, the avatar dictionary, the Killer/Survivor/Signal colours, the dither material, `MakeKillerModel`, the avatar-building part of `Accept`, `SetLocalLook`, `Render` + `RenderKiller`. It exposes what WorldView needs for `KillerAlpha`/`KillerVisorAlpha`.
4. **WorldProps** (plain C# class): floor, walls, terminals (`Terminal`, `MakeTerminal`, the terminal part of `Accept`), the Exit `Ring` and its open/closed colour, plus the Plinth/Panel/Neutral colours. The Survivor team colour used for done terminals and the open Exit: keep one definition (a `public static readonly` on CharacterViews or a small shared constant in WorldMaterials — your choice, state it in the report).
5. `OnDestroy` in WorldView disposes all three (materials, dither material, Exit mesh) exactly as today.
6. Keep comments, which carry HUD_SPEC references: move each with its code. Same naming and comment style as the surrounding code.
7. No other file changes. If a test or caller needs to change, stop and report instead.

## Acceptance

Run both and paste the summary lines in the report:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/unity-editmode.xml -logFile -
"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile -
```
(Check `Client/Assets/Editor/BuildScript.cs` for the exact method name.)
- EditMode: same total and same pass/fail set as before your change. Run once **before** editing to record the baseline. U4e left one known stale QA test failing, so do not count it against you.
- BuildWindows succeeds.
- `git diff --stat` touches only the owned files.

## Report

`docs/qa/U4f-impl-a1.md`, English, ≤ 200 words: the new file responsibilities, baseline vs after test counts, build result, anything that did not split cleanly.
