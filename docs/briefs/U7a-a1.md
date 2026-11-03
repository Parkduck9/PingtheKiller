# Brief U7a — attempt 1: Settings screen (FOV, sensitivity, reduced motion, volume)

**Implementer:** Claude Sonnet subagent. This is a quota fallback because Codex is out of credits.
**QA:** Gemini Flash.
**Parallel units:** U6a, U4h, U6e and U6c-ui run at the same time.

Read first: `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md` (mandatory), `docs/ui/HUD_SPEC.md`.

Do **not** commit.

## Goal

The PLAN lists two things as needed before the Microsoft Store:
- FOV and mouse sensitivity settings;
- a reduced-motion toggle for the Lagging stutter. Today it exists only as the dev flag `-ptk-reduce-stutter`.

Add a Settings screen. It must be reachable from the main menu and from the in-match play menu.

## Settings

All values persist in `PlayerPrefs` under `ptk.settings.*`. Read every value safely: if a stored value is out of range, use the default.

| Setting | Range | Default | Notes |
|---|---|---|---|
| Survivor camera FOV | 60–90° | today's `SurvivorOrbitRig.VerticalFov` | survivor orbit camera |
| Killer camera FOV | 60–100° | today's `KillerViewRig.VerticalFov` | killer first-person camera |
| Mouse sensitivity | 0.25×–3× | 1× | multiplier on `RoleCamera.Sensitivity` |
| Invert Y | on / off | off | |
| Reduced motion | on / off | off | When on, behaves as `-ptk-reduce-stutter` does today. The dev flag still forces it on. |
| Master volume | 0–100 % | 100 % | sets `AudioListener.volume` |

## Owned files

**New**
- `Client/Assets/Scripts/UI/SettingsScreen.cs`
- `Client/Assets/Scripts/Settings/GameSettings.cs`: a plain class that does load, clamp and save, so it is testable.
- `Client/Assets/Tests/EditMode/SettingsTests.cs`. Cover:
  - clamping and defaults;
  - the persistence round-trip;
  - sensitivity and invert-Y applied to the yaw and pitch delta;
  - FOV applied to each rig.
- `docs/qa/U7a-impl-a1.md` and `docs/qa/U7a-screens/`.

**Edit minimally**
- `ClientScreens.cs`: add a "Settings" button to the menu and to the play menu, and host the screen.
- `ClientApp.cs`: apply the settings at start and whenever they change.
- `RoleCamera.cs`, `SurvivorOrbitRig.cs`, `KillerViewRig.cs`: accept FOV, sensitivity and invert values instead of only constants.
- `LaggingStutter.cs`, or wherever the reduce flag is read.
- The existing USS.
- `docs/ui/HUD_SPEC.md`: add a Settings section.

Another unit (U6a) may make small edits to `ClientScreens.cs` (result reasons) and `ClientApp.cs` at the same time. Keep your edits in those two files small and in clearly separated blocks.

## Acceptance

**Tests and build**
- EditMode: all green, 0 compile errors.
  - Run from the worktree root with `-testResults "$PWD/docs/qa/u7a-runs/editmode.xml"`. The full command is in `AGENTS.md` §3.
  - The first run in a fresh worktree imports assets for a long time.
- `BuildScript.BuildWindows` succeeds.

**Screenshots**
- Capture the settings screen opened from the main menu and from the play menu, at 720p and at 1080p.
- If no existing flag can reach the screen, add `-ptk-settings` to `DevArgs` / `DevCapture`.
- Look at every PNG and score it against SLOP §9.

**Input**
- The screen works with keyboard navigation (Tab, arrows, Enter) and with the mouse.

## Report

`docs/qa/U7a-impl-a1.md`, at most 250 words. Start with the header note "quota fallback: Codex out of credits".
