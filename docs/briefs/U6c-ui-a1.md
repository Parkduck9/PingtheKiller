# Brief U6c-ui — attempt 1: Rules v2 HUD widgets (unwired)

- **Implementer:** Claude Opus subagent. This is the quota fallback, because Codex is out of credits.
- **QA:** Gemini Flash, covering both code and visuals.
- **Parallel units:** U6a, U4h, U6e and U7a run at the same time in other worktrees.

Read these first:
- `AGENTS.md`
- `docs/ui/SLOP_DEFINITION.md` (mandatory)
- `docs/ui/HUD_SPEC.md`
- `docs/GDD-v2.md` §1–§4
- **`docs/briefs/U6a-a1.md`** — it defines the exact protocol v0.4 fields your widgets will show.

Do **not** commit.

## Goal

Build the client widgets for rules v2 now, driven by **view-model structs**. After U6a merges, wiring them to the real packets should be a small step; that is a later unit, U6c-wire.

U6a's packet classes do not exist on your branch yet. **Do not add or edit anything under `Shared/`.**

## Owned files

**New: `Client/Assets/Scripts/UI/RulesV2/**`** — the widgets, plus plain C# view models that mirror the U6a fields:

| View model | Fields |
|---|---|
| `SyncCheckView` | Duration, ZoneStart, ZoneSize, GreatSize, Elapsed, Result? |
| `ExitView` (×2) | Powered, Open, Locked, SwitchProgress01 |
| `HoldActionView` | Kind, Progress01 |
| `CollapseView` | GlobalLeft, PersonalLeft |
| `TerminalFlagsView` | Regressing, Overloaded |
| `ResultV2View` | per-player rows: escaped or eliminated; reason; winner rule "half or more escaped" |

**Other new files**
- The widgets' USS, next to the existing USS files.
- EditMode tests: `Client/Assets/Tests/EditMode/RulesV2HudTests.cs`.

**Existing files you may edit**
- `Client/Assets/Scripts/Dev/DevArgs.cs` and `DevCapture.cs`: add **one** flag, `-ptk-preview-v2-hud <scenario>`. It shows the widgets with fake view-model data over the normal scene, for screenshots only.
- `docs/ui/HUD_SPEC.md`: add a "Rules v2" section with exact layout, sizes, colours and copy, written in the existing style.

**Reports**
- `docs/qa/U6c-ui-impl-a1.md` and `docs/qa/U6c-ui-screens/`.

**Do not edit:** `ClientApp.cs`, `ClientScreens.cs`, `SurvivorHud.cs`, `World/**`, `Killer/**`, `ResultCopy.cs`. Other units change these in parallel. If wiring would need them, describe the wiring in the report instead.

## Widgets

All copy is English and uses the canonical vocabulary from SLOP. Follow SLOP: no glow, no gradients, no template look; keep it minimal and readable.

1. **Sync check ring (survivor)**
   - A ring with a needle that sweeps from 0 to 1 over Duration, a zone arc, and a great sub-arc.
   - Space presses. Expose an event or callback; do not send network messages.
   - After the press, flash the result (Great / Good / Fail) for about 0.4 s.
   - Place it small and at centre screen, without hiding the character. It must work at 720p and 1080p.
2. **Hold action bar** — one component with a label and a progress fill, used for:
   - exit switch (survivor);
   - kick terminal (killer);
   - close hatch (killer).
3. **Exit status** — both exits, for each role: unpowered, powered, switch %, open, locked (firewall).
4. **Collapse timer**
   - Shows the global countdown.
   - Shows the personal clock when it differs, for example "Lagging: 2× faster".
   - In the last 30 s it turns urgent through the colour rule only, with no flashing.
5. **Terminal flags**
   - **Regressing:** shown to both roles.
   - **Overloaded:** a killer-side alert. It may use an edge indicator toward the terminal's world position. Reuse the existing edge-marker helper only through its public API; do not edit `Killer/**`.
6. **Result screen v2**
   - The winner line.
   - The reason: AllResolved, AllSurvivorsDown, TimeUp or Collapse.
   - A per-player list showing escaped or eliminated.
   - One short line explaining "half or more escaped wins".

## Acceptance

**Tests**
- EditMode: all green and 0 compile errors. Run from the worktree root:
  ```
  "/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "$PWD/docs/qa/u6c-ui-runs/editmode.xml" -logFile docs/qa/u6c-ui-runs/editmode.log
  ```
  The first run in a fresh worktree imports assets for a long time.
- New tests must cover:
  - needle position at a given time;
  - zone hit and miss, including the edges;
  - progress clamping;
  - the collapse urgent threshold;
  - result rows and the winner line for 3 of 7 and for 4 of 7 escaped.

**Build**
- `BuildScript.BuildWindows` succeeds.

**Screenshots**
- Capture at 1280×720 and 1920×1080 using `-ptk-preview-v2-hud`:
  - sync-check (zone and great visible)
  - exit-switch-hold
  - kick-terminal (killer)
  - collapse-urgent
  - result-v2
- Look at every PNG yourself and give an honest score against SLOP §9.

## Report

Write `docs/qa/U6c-ui-impl-a1.md`, at most 300 words. Start with the header note "quota fallback: Codex out of credits". Include:
- files changed;
- the view-model API;
- the wiring plan: which U6a field feeds which view model;
- test counts;
- the build result;
- the screenshots;
- your SLOP self-score.
