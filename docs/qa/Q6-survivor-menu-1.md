# Q6-survivor-menu — QA attempt 1

**Quota fallback:** Codex out of credits and the Antigravity Gemini Flash sign-in expired. Claude Opus subagent implemented; this QA was done by a Claude Sonnet subagent (different model from the implementer).

**Verdict: PASS** (no blocker or major findings; 3 minor notes)

## Spec table

| # | Spec | Result | Evidence |
|---|------|--------|----------|
| 1 | One shared `PlayMenu`, Stacked + Row, Killer API/look unchanged | PASS | `UI/PlayMenu.cs` (layouts, events, Focus), `KillerHud.cs` `Menu` + forwarded events, `MenuHeight = StackedHeight` (256). Killer 720p shot identical to `docs/qa/U7a-screens/720/play-menu.png` |
| 2 | Survivor menu in prompt slot, prompt/hold hidden | PASS | `SurvivorHud.cs` `SetPrompt` early return when `MenuOpen`, `ClusterRect` +152; shot shows block directly above meter cluster, 8 u gap, no overlap |
| 3 | Content: skill, effect, line 3 only when true | PASS (line 3 not seen on screen) | `PlayMenuCopy.CaptureNote`; matches `Server.Core/GameServer.Rules.cs:76,115` (2nd capture = 30 s lagging, 3rd eliminates). Unit-tested only (no capture seam) |
| 4 | Pure gate, one zero MoveInput + Interact release, no look/WASD/E/Q/Space | PASS | `PlayMenuGate`; `ClientApp.cs` `active`/`presses` feed look, keys (l.430), interactHeld (l.443), Space/gamepad A, Q. No gamepad axis movement exists, so nothing else leaks |
| 5 | Esc/Start, Settings first, click never re-locks under menu, focus loss unchanged | PASS | `ClientApp.cs` Update block; `releasedCursor && !menuGate.Open`; `OnApplicationFocus` uses `menuGate.Open` |
| 6 | Settings from Survivor menu, close focuses current role menu, comments | PASS | `ClientScreens.SettingsClosed` + `OpenPlayMenu`; `settings-play.png` |
| 7 | Leave = Disconnect; menu reset on leave/disconnect/result/next match | PASS | `ResetMenu()` at match start, `EndMatch`, `Disconnect`; QA source pin; exe log `cursor lock=None visible=True`, clean exit |
| 8 | Focus/states | PASS | Resume outline visible in shot; Tab/arrow order covered by `PlayMenuTests` |
| 9 | Dev seams | PASS | `-ptk-menu 5 -ptk-settings -ptk-menu-leave` ran end to end for Survivor |
| 10 | HUD_SPEC §5 only | PASS | diff touches §5 only |

## Findings

1. **minor, cosmetic:** with no line 3 (the normal case) the Row block shows a blank band (~30 u) between the effect line and the buttons, because buttons are pinned to the bottom. Intended by the spec note ("does not move with line 3"); not a defect.
2. **minor, pre-existing:** Settings "Reduced motion" note overflows the panel to the right (`settings-play.png`). Not part of this unit.
3. **minor, unverified:** line 3 has never been shown on screen (needs a capture seam with CaptureCount >= 1). Logic and copy are unit-tested and consistent with the server rules.

No input-leak, cursor-state, stale-menu, or Esc-in-Settings bug found. Open menu at match end / disconnect / leave / next match is reset on all four paths.

## Tests

- QA tests added: `Client/Assets/Tests/EditMode/QA/PlayMenuQaTests.cs` (6: gate reset/closed frame, CaptureNote boundaries, Killer HUD closes on Begin + stacked sizes, Survivor menu close, ClientApp reset-path source pins, USS).
- Unity EditMode (`docs/qa/q6-runs/qa-editmode.xml/.log`): **total 1404, passed 1404, failed 0**, 0 `error CS`.
- Build (`qa-build.log`): `BuildScript.BuildWindows` succeeded (`Client/Builds/Win64/PingTheKiller.exe`).
- Exe, Survivor 1280x720 offline `-ptk-menu 5 -ptk-settings -ptk-menu-leave`: all shots taken; log: `Leave match pressed (role=Survivor)`, `main menu shown, playing=False cursor lock=None visible=True`, `script complete, quitting`, process exit 0, no exceptions.
- Line endings: `git diff --stat` equals `--ignore-cr-at-eol` (11 files); no ProjectSettings/asset content diffs (status-only staleness from LF/CRLF warnings).

## SLOP §9 score (Survivor play menu)

| Row | Score | Notes |
|-----|-------|-------|
| Copy (exact HUD_SPEC §5 labels, plain, no filler) | 14/15 | line 3 not seen on screen |
| Layout / grid / spacing (672x152, 160x40, gap 8, grows up, no overlap) | 14/15 | blank band when no line 3 |
| Hierarchy / type (M 600 title, S effect) | 10/10 | |
| Color / contrast / borders | 10/10 | |
| Focus / states (Resume outline, hover/press as Killer) | 10/10 | |
| Consistency with Killer menu | 10/10 | |
| 720p readability | 9/10 | small text but legible |
| Motion / no decoration / no slop | 10/10 | |
| Behaviour/edge (leave, settings) | 10/10 | |
| **Total** | **97/100** | gates: none failed |

## Screenshots (all viewed)

`docs/qa/q6-runs/qa-survivor-720p/` (play-menu, settings-play, menu-after-leave, menu, lobby, settings-menu, play-1s, play-4s, survivor-3p); implementer's `survivor-720p/`, `survivor-1080p/` (play-menu viewed), `killer-720p/play-menu.png`; comparison `docs/qa/U7a-screens/720/play-menu.png`. Not re-run at 1080p by QA (implementer's 1080p reviewed).
