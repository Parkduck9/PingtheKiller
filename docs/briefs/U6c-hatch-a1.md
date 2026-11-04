# Brief U6c-hatch — attempt 1: hatch world visual

- **Implementer:** Claude Sonnet subagent. This is the quota fallback, because Codex is out of credits.
- **QA:** Gemini Flash via agy, run by the orchestrator.
- **Worktree:** `C:\Unity\MultiPlayGame\PtK-work\u4h`, branch `claude/u6c-hatch`, based on main `7f2c746`. The Unity Library is already warm.
- **Parallel unit:** U6c-wire, in worktree `PtK-work/u6c-ui`, owns `ClientApp.cs`, `UI/**`, `Killer/**`, `Dev/**`, `World/SelfState.cs` and the HUD prompts, including the survivor "escape through the hatch" prompt. **Do not touch those files.**
- Do **not** commit, push or merge.

## Read first

- `AGENTS.md`
- `docs/GDD-v2.md`, the hatch section
- `docs/briefs/U6a-a1.md` R6 (Hatch)
- `Shared/Runtime/Packets/ServerPackets.cs`, `S2C_WorldState` (`HatchVisible`, `HatchX`, `HatchZ`)
- `Client/Assets/Scripts/World/WorldView.cs`, `WorldProps.cs`, `WorldMaterials.cs`, `WorldArt.cs`
- `docs/ui/SLOP_DEFINITION.md`, the world-readability parts

## Goal

The server sends the hatch position to a recipient only while the hatch is open **and** that recipient can see it: within 15 m and with no wall in the way. Today the client draws nothing for it.

Draw the hatch in the world for **both roles** whenever `WorldState.HatchVisible == 1`, at (`HatchX`, `HatchZ`). Hide it as soon as a snapshot arrives with `HatchVisible == 0`.

## Owned files

You may edit or create:
- `Client/Assets/Scripts/World/WorldView.cs`, the hatch part only: keep the diff small
- `Client/Assets/Scripts/World/WorldProps.cs`, if the prop belongs there
- a new `Client/Assets/Scripts/World/HatchView.cs` (or a similar name)
- `Client/Assets/Editor/Capture/HatchShot.cs`, new, for screenshots
- `Client/Assets/Tests/EditMode/HatchViewTests.cs`, new
- your reports and screenshots

**Do not edit:**
- `Shared/**`, `Server*/**`
- any U6c-wire file listed above
- `Editor/Setup/WorldArtSetup.cs`
- existing art assets

## Spec

**H1. Hatch object**
- A procedural, low-poly floor hatch about 1.2 m across: a frame, plus a dark opening or grate.
- Use the existing world materials so it matches the U4i toon-at-night look. Nothing may glow.
- It must read clearly at night from 15 m in the survivor third-person camera and in the killer first-person camera.
- No downloaded assets.

**H2. Readability cue**
- One subtle, non-glowing cue so a survivor notices it, for example a slow pulse of a light-coloured rim, at most 1 Hz.
- It must follow SLOP: no glow, no bloom-only cue.
- Respect `GameSettings` reduce-motion if such a setting exists. If it exists, a static rim is fine.

**H3. Lifecycle**
- Create the object lazily.
- Show it and move it on `HatchVisible == 1`.
- Hide it on `0`.
- Destroy it with the match world; WorldView already handles that teardown.
- Do not allocate per snapshot.
- Snap to the position; do not interpolate it.

**H4. Public API**
- Expose `bool HatchShown` and `Vector3 HatchPosition` (read-only) on `WorldView`, for tests.
- Do not add anything that needs `ClientApp` changes.

## Acceptance

**Tests**
- EditMode: all green and 0 compile errors. The main baseline is 1320.
- New tests in `HatchViewTests.cs` cover:
  - show on 1;
  - the position;
  - hide on 0;
  - re-show at a new position;
  - no hatch before the first snapshot;
  - no new GameObject across 100 repeated snapshots.
- Command, run from the worktree root:
  ```
  "/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "$PWD/docs/qa/u6c-hatch-runs/editmode.xml" -logFile docs/qa/u6c-hatch-runs/editmode.log
  ```

**Build**
- `-executeMethod BuildScript.BuildWindows` succeeds.

**Screenshots**
- Write an editor batch capture, `HatchShot.Run`, run through `-executeMethod` **without** `-nographics`. It must:
  1. build the real map art the way the game does;
  2. place the hatch at two real `FixedMap.HatchPoints` entries. Read them from `Server.Core`'s FixedMap values, or copy the numbers into the capture with a comment; do not reference Server.Core from the client;
  3. render with the game's night lighting and post-processing from:
     - a survivor third-person framing at about 6 m and at about 14 m;
     - a killer first-person framing at about 10 m;
  4. capture each at 1280×720 and 1920×1080.
- Save the shots to `docs/qa/U6c-hatch-screens/`.
- **Open every PNG yourself.**
- Never send desktop input or `SendKeys`.

**Housekeeping**
- Unity batch runs re-serialise files outside your scope. Revert them with `git checkout -- <file>`.

## Report

Write `docs/qa/U6c-hatch-impl-a1.md`, at most 250 words. Start with the header note "quota fallback: Codex out of credits". Include:
- files changed;
- the API;
- test counts;
- the build result;
- the screenshots;
- an honest readability self-assessment;
- risks.
