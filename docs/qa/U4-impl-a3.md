Implementer: Claude Opus subagent (quota switch from Codex) — attempt 3, different approach: screenshot-verified layout + dev capture mode

## Root cause
A temporary probe in the built player logged `layout` vs `MeasureTextSize`: "Server IP:port" measured 118, layout 118; title measured 211, layout 210. Yoga rounds the measured text width to whole pixels and a wrapping label re-wraps at that rounded width, so the last word drops to a second line that the one-line height does not cover. It also happens at 1920×1080, so scaling is not the cause.

## Changes
- `ClientUI.uss`: all text `white-space: nowrap`; only `.message` wraps, at a fixed 960 px width. Button rows, muted empty slots.
- `ClientScreens.cs`: panel scale = min(w/1920, h/1080) clamped to [0.8, 1] (14 px ≥ 11 px at 720p); `Address` setter.
- `Dev/DevArgs.cs` (pure parser), `Dev/DevCapture.cs` (end-of-frame PNG capture, scripted order); `ClientApp` wiring; scripted W goes through `MovementInput`.
- `MapSource.cs`/`WorldView.cs`: map now mirrors U2a `FixedMap` ([0,60], walls, terminals, exit 58,30). Before this, the killer camera showed only a quarter of the server map.
- Tests: `DevArgsTests` (17), `PanelScaleTests` (6).

## Commands
- Build `BuildScript.BuildWindows`: exit 0, 0 `error CS`.
- EditMode: 170 total, 168 pass. The 2 failures are the known superseded `InputQaTests`. `-testResults` relative paths resolve under `Client/`; the XML was moved to `docs/qa/U4-editmode.xml`.
- Player runs against U2a server (`--bots 6`), killer/survivor 1280×720, killer 1920×1080, `-ptk-quit-after 8`: all 4 shots each.

## Screenshots (`docs/qa/U4-screens/`)
a3-menu-1280, a3-menu-1920, a3-lobby-1280, a3-lobby-1920, a3-play-killer-1280, a3-play-survivor-1280 (Signal), plus a3-play-survivor-silent-1280 and a3-menu-error-1280 (two-line wrapped error).

## Needs a human
Audio beeps, hover/focus/pressed feel, gamepad/keyboard navigation.
