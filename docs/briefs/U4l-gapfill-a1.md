# Brief U4l-gapfill: no see-through gaps up close; art fills its boxes (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 100 minutes. Other units are running in parallel.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/u4l-gapfill`, branch `claude/u4l-gapfill`, from main 3675877.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject/Client/Library` into `Client/Library`. Skip any `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
- **Skills:** first read the project skills `unity-graphics` and `unity-performance` under `.claude/skills/`.
- Also read `AGENTS.md` and the previous unit's brief and reports:
  - `docs/briefs/U4k-buildings-a1.md`
  - `docs/qa/U4k-buildings-impl-a1.md`
  - `docs/qa/U4k-buildings-qa-a1.md`

## User report (Korean, translated)

"Run tests and look for places where a gap between things is empty, so you can see the other side. Move left and right, up close. Then scale all those assets up so they fill completely."

## Context

- `Client/Assets/Editor/Setup/WorldArtSetup.cs` generates one art piece per FixedMap box:
  - each wall box: a 3 m podium that fills the box exactly, plus upper storeys set back 1 m from the outer group boundary (U4k);
  - each obstacle box: a car, container or junk prop, at most `MaxObstacleHeight` 1.5 m;
  - the backdrop or skyline, plus the terrace behind the border walls.
- `WorldArtCapture.cs` renders views in batchmode.
- U4k fixed the distant, top-down gaps. **This unit is about close range, at eye level.**
- **Collision is the FixedMap boxes.** Wherever the art does not fill its box, the player sees a gap and also bumps into an invisible wall. Both are bad.

## Spec

### S1 Detect (automated, editor batchmode)

1. Write a detector in `WorldArtCapture.cs` or a new `WorldArtGapScan.cs`. It loads the generated catalog and builds temporary `MeshCollider`s from every art renderer. Then it samples:
   - **eye points:** along every street, alley and plaza edge, at 1.7 m height, 1.0 m and 2.0 m away from each wall box face and each obstacle box face, every 1 m along the face;
   - **at each eye point:** a horizontal fan of rays at -60°…+60° in 5° steps, at heights 0.3, 1.0, 1.7 and 2.6 m, aimed into the facing box.
2. **A see-through hit** is a ray that enters a wall box, or an obstacle box below its height, and either:
   - travels more than 0.25 m inside the box without hitting any art collider; or
   - exits the far side of the box.
3. **Also check:**
   - each **obstacle** box: the art's bounds versus the box, as a fill ratio per axis;
   - each **wall** box from 0 to 3 m: the podium's bounds versus the box;
   - **upper storeys**: inside one building group no through-gaps above the podium when looking along the faces. U4k's shared-edge rule must still hold.
4. **Output:** `docs/qa/U4l-gaps-before.md` and `.json`. List every gap cluster with its box index, face, height band, width and a screenshot.
5. **Screenshots:** render each cluster from its eye point, at 1280x720 with the in-game FOV of 50° vertical. Then render a **left-right sweep** at about 5 positions per street for the 6 worst streets, as the user asked. Put them in `docs/qa/U4l-screens/before/`.

### S2 Fill

1. **Wall boxes:** the podium art must cover the box face fully from 0 to 3 m, with no holes, open doorways, arcades or see-through windows. Where a fitted City model leaves holes, choose one of:
   - scale or stretch it to the box;
   - add a solid backing or infill mesh inside the box;
   - replace it with the generated podium.
   Upper storeys keep the U4k camera rule (`Setback` from the outer group boundary). Inside the group they meet or overlap.
2. **Obstacle boxes:** scale each prop non-uniformly to fill its box footprint to at least 95 % per axis. It must not exceed the box by more than 0.05 m, and its height must stay ≤ 1.5 m. If a model looks wrong when stretched, choose another prop from the same pack or a generated crate or container block that fits the box.
3. **Border walls and skyline:** no see-through to the sky gaps at eye level behind the border walls from inside the arena.
4. **Keep every existing rule:**
   - `WorldArt.Matches(new FixedMap())`;
   - setup idempotent;
   - the floor at or below 0.15 m;
   - nothing in the walkable area;
   - no change to `Server.Core`, `Shared` or any collision.
5. **Re-run the detector and capture.** The goal is 0 see-through hits and fill ≥ 95 % everywhere. If anything is left, explain it with a screenshot. Put the after shots in `docs/qa/U4l-screens/after/`, at the same views as before.

### S3 Performance

Report the triangle and renderer counts before and after, as U4k did. Say why any increase above 25 % is acceptable.

## Tests (EditMode)

- No see-through hit on the current catalog. Run a reduced-density version of the detector as a test, so a future regression fails.
- The obstacle fill ratio is ≥ 0.95 per axis and ≤ box + 0.05.
- The podium covers every wall face from 0 to 3 m.

Keep every existing WorldArt and WorldArtBuilding test green. Change one only if it encoded old fitting tolerances, and list each change.

## Gates

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath Client -executeMethod PingTheKiller.Editor.Setup.WorldArtSetup.Run -logFile <log>
"$U" -batchmode -projectPath Client -executeMethod PingTheKiller.Editor.Setup.WorldArtCapture.Run -ptk-capture-out <dir> -logFile <log>    (no -quit/-nographics, as in U4k)
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-shots <abs dir> -ptk-quit-after 120 -logFile <abs log>
```

- **Pass criteria:**
  - All EditMode tests pass. Main had 1486.
  - BuildWindows succeeds.
  - `dotnet build -warnaserror` stays green.
- **Paths:** logs go in `docs/qa/U4l-runs/`.
- **After each Unity run:** revert files that changed only in line endings.

## Owned files

- `Client/Assets/Editor/Setup/WorldArtSetup.cs`, `WorldArtCapture.cs` and a new `WorldArtGapScan.cs`.
- `Client/Assets/Resources/WorldArt.asset` and the regenerated `Client/Assets/Art/World/**`.
- The WorldArt tests and the test asmdef, if needed.
- `docs/qa/U4l-*`.

Units running in parallel:
- **Q5b** builds shutter and rail art in its own `ChaseProps` setup and folder. Do not touch those.
- **U9** owns `SkillFx`.
- **U8** owns the capture effects.

## Report

Write `docs/qa/U4l-gapfill-impl-a1.md` in English, ≤ 300 words. Cover:
- the detector design;
- the before and after counts (gap clusters, fill ratios);
- what was changed per category;
- the triangle and renderer counts;
- the tests;
- the gates with counts;
- the shot paths;
- what is left;
- the risks.

Also write `docs/qa/U4l-gapfill-commit-message.txt`.
