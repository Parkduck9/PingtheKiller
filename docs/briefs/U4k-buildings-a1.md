# Brief U4k-buildings: close the gaps in the city art (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable (separate run, after you).
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/u4k-buildings`, branch `claude/u4k-buildings` (from main 53f4ef9). LFS files are already checked out from the local store.
- Never commit, push or merge. Never download anything. Never send keyboard or mouse input to the desktop. Never kill a process you did not start; leave Unity Hub and any Unity editor of other projects (`C:\Unity\CsharpPrac`, other worktrees) alone.

## Problem (user report: "the buildings look sparse, many gaps are empty")

The orchestrator traced three causes in `Client/Assets/Editor/Setup/WorldArtSetup.cs`:

1. **Stale index table.**
   - `UpperFor(int index)` (about line 661) assigns upper storeys to FixedMap wall indices 4-15. Those indices belong to map v2.
   - Map v2.1 (U6e, 2026-11-04) has 31 walls. The catalog was regenerated, but the table was not updated.
   - So the 15 interior boxes 16-30 are only the 3 m podium, with nothing on top. The surviving 4-15 assignments now land on arbitrary boxes.
   - See `docs/qa/playcheck-1105/killer/play-4s.png`: the flat block in the centre.
2. **Gaps inside multi-box buildings.**
   - The L, U and T buildings are 2-3 touching wall boxes. Each box gets its own upper, set back `Setback` = 1 m from all four box sides.
   - So every shared edge opens a 2 m slot above the podium, and neighbouring boxes get unrelated heights and models.
   - About 12 shared edges, e.g. walls 6/8, 7/8, 23/25, 29/30.
3. **Skyline gaps.**
   - `Backdrop()` / `Row()` (about line 905) leave 1.5 m between skyline houses and skip 3 m when a model does not fit.
   - They also stand 2.5 m behind the 3 m border walls, so the sky shows between them.

## Spec

- **S1 Building groups.**
  - Compute groups from `FixedMap` geometry: boxes that share an edge segment of positive length are in one group. Never use hard-coded indices.
  - A group's upper storeys read as one continuous building. The `Setback` applies only to the group's **outer** boundary, the union of its boxes. On shared interior edges, the uppers meet or overlap, leaving no slot.
  - Keep the camera rule from the file header: anything above `MapGeometry.WallHeight` stays at least `Setback` inside the outer boundary of the union.
  - Heights may step between parts of a group for variety, but adjacent parts must touch.
- **S2 Every interior box gets an upper.**
  - All 27 interior wall boxes (indices 4-30) get an upper part reaching at least 6 m, i.e. 2+ storeys above the podium. Keep it at or below `MaxBuildingHeight` 11 m.
  - Assignment is per group and geometry-keyed. A future map change must make a test fail, not silently drop uppers.
  - Avoid identical models or colours on neighbouring groups. The mix of fitted City 03 house models and generated `Storeys` is your call. Generated storeys are fine where a model cannot fill an L/U footprint cleanly.
- **S3 Continuous skyline.**
  - Seen from inside the arena, no see-through gap wider than 0.3 m between skyline pieces along any side, including the corners.
  - Fill leftover lengths with fitted or generated filler blocks instead of skipping. Close or hide the 2.5 m strip between the border walls and the skyline, for example with a low generated building row or by moving the skyline in.
  - Nothing may enter the walkable area or the border wall boxes' inner side. Shadows stay off for the backdrop.
- **S4 Keep every existing rule.**
  - The podium fills each box exactly from 0 to 3 m.
  - Obstacles stay at or below `MaxObstacleHeight`, the floor at or below 0.15 m, and the walkable area stays clear.
  - `WorldArt.Matches(new FixedMap())` must still be true.
  - Rerunning the setup must still be idempotent.
  - Do not change `Server.Core`, `Shared/` or any gameplay or collision rule. This is visuals only.
- **S5 Performance.**
  - Report the total triangle and renderer count of the catalog (walls + backdrop) before and after.
  - If the triangle count more than doubles, say why it is acceptable, or simplify.
  - Grep the offline exe log for fps or frame time, if the dev log has it.

## Owned files

- `Client/Assets/Editor/Setup/WorldArtSetup.cs`.
- Regenerated `Client/Assets/Art/World/**` and `Client/Assets/Resources/WorldArt.asset`.
- `Client/Assets/Tests/EditMode/WorldArtTests.cs`, plus a new test file if you want one.
- A new `Client/Assets/Editor/Setup/WorldArtCapture.cs`: a batchmode `-executeMethod` that renders the generated catalog to PNGs.
- `docs/qa/U4k-*`.

Touch `Client/Assets/Scripts/World/WorldArt.cs` or `WorldProps.cs` only if you must, and say why in the report.

## Tests (EditMode; add them)

- **Uppers:** every interior wall piece has upper renderers reaching ≥ 6 m and ≤ 11 m.
- **Outer setback:** no upper vertex above 3 m lies closer than `Setback - 0.01` to the outer boundary of its group union.
- **Shared edges:** for every shared edge of every group, samples every 0.5 m along the edge at heights 4 m and 5.5 m lie inside upper geometry. Mesh renderer AABBs per child are acceptable, or use a tighter test.
- **Grouping:** the group computation on the current FixedMap gives the expected groups; list them in the test.
- **Skyline:** on each of the 4 sides, the projected coverage of skyline pieces has no gap > 0.3 m over the arena span.
- Keep every existing `WorldArtTests` test green. Change one only where it encoded the old per-box setback, and say so.

## Visual evidence

1. **Before you change anything,** run your capture method on the current catalog. Do the same after. Save both under `docs/qa/U4k-screens/before/` and `after/`. Capture:
   - an oblique overview like `docs/qa/U4h-screens/editor-overview-720.png`, at 1280x720;
   - a top-down view;
   - three street-level views at about 1.7 m eye height, looking along a street toward a multi-box building and toward the map edge.
2. Build and run the offline exe with `-ptk-shots`, Killer and Survivor, short runs. Use `-ptk-quit-after 90`, otherwise as in the command below. Copy a few of those shots into `after/` as well.

## Gates and commands

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath Client -executeMethod WorldArtSetup.Run -logFile <log>
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-shots <dir> -ptk-quit-after 90 -logFile <log>
```

- Check the real namespace and class names of the `-executeMethod` targets first.
- Run Unity commands with `run_in_background`; they take minutes.
- The worktree has no `Client/Library` yet. You may copy `C:/Unity/MultiPlayGame/PingtheKiller/Client/Library` into it first to save an import; a fresh import also works.
- Put logs under `docs/qa/U4k-runs/`.
- All EditMode tests must pass, and BuildWindows must succeed.
- `dotnet build PingTheKiller.sln -warnaserror` must stay green. It should be untouched.
- After each Unity run, `git status`: revert files that changed only in line endings (`git diff --ignore-cr-at-eol --quiet -- <file>`). Keep your real changes.

## Report

- Write `docs/qa/U4k-buildings-impl-a1.md` in English, ≤ 300 words.
- Cover:
  - the groups found;
  - what each spec item became;
  - the test names;
  - the gate results with counts;
  - the before and after screenshot paths;
  - the triangle and renderer counts;
  - the deviations;
  - what is unverified;
  - the risks.
- Also write `docs/qa/U4k-buildings-commit-message.txt`, without an attribution line.
