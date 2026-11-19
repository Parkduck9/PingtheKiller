# S3-credits QA, attempt 2

Reviewer: Claude Fable 5.2 (implementer: Claude Sonnet 5.5). Commit `9b36895` on `claude/s3-credits`, re-review of `docs/qa/S3-credits-qa-a1.md`.

**Verdict: PASS.** SLOP score 95 / 100, no gate failures. Three minors remain, none blocking.

## What was checked

- Diff `d4a3045..9b36895` read in full.
- Unity EditMode, batch mode, absolute `-testResults`: 1629 total, 1627 passed, 0 failed, 2 skipped, 0 inconclusive. One of those is my temporary probe, so the committed suite is **1628 total, 1626 passed, 0 failed, 2 skipped**, as reported. All 9 `CreditsTests` passed.
- The same kind of probe as attempt 1 (removed afterwards, not committed): real `ClientScreens`, `RuntimePanel`, forced layout, synthetic `NavigationMoveEvent`, `KeyDownEvent` and `WheelEvent`, events sent to the focused element. View 700 u, content 2035 u, range 1335 u.
- Screenshots `credits-top.png`, `credits-licence.png`, `credits-end.png` (the implementer's).
- `DOTweenSettings.asset` was touched by Unity (line endings only) and reverted. The worktree is clean except this report.

## Attempt 1 findings

| Finding | Status | Evidence |
|---|---|---|
| F-01 scrolling by pad / arrows (gate) | **fixed** | see probe log below |
| F-02 default scroller (gate V10) | **fixed** | tracker `#1A1C1F` with `#2C2F33` 1 px line, dragger `#9A9890`, radius 0, arrow buttons and horizontal bar `display: none`; see M-01 |
| F-03 duplicate copyright line | fixed | label is `Inter (font)`; `OFL.txt` untouched |
| F-04 13 u body | fixed | `cr-ofl` 14 u (15 u under `s-floor`) |
| F-05 column width | fixed | 880 u view, 840 u labels; see M-03 |
| F-06 guards | partly | see M-02 |
| F-07 tests | partly | a real scroll test was added (both clamps, not inconclusive in this run); no test for "disabled while connecting"; the hype list is unchanged |

Probe log for F-01 (focus is `Button 'Back'` on every line unless noted):
- Up at the top: offset 0 (clamped). Down: 72. Left, Right: 72 (no change, focus stays).
- PageDown: 700, then 1328. PageUp: 700, 72, 0 (clamped). The page step is 628 u (view minus one step), so no line is skipped.
- 60 times Down: 1335 = max (clamped). PageDown at the end: 1335. Up: 1263. The scroller value follows the offset (1263).
- Wheel (synthetic, delta -3): 1263 to 1209. The wheel still works.
- Tab: focus moves to the scroller slider; Up there moves about 9 u (the stock slider step); Tab or Shift+Tab returns to Back. Not a trap.
- Cancel: closes, focus on `Button 'Credits'`. Reopen: offset 0, focus on Back.

## Remaining minors

### M-01 (minor, visual): the tracker is 24 u wide, not 12 u, and the dragger is off-centre
- Evidence: probe `scroller width=12.5` but `slider / tracker bound width=23.75` (x 900 to 924), dragger x 903.75 to 912.5. In all three shots the dark track is 24 px wide with the 8 px dragger in its left half, and it sticks out 12 u past the 880 u view.
- Expected: `HUD_SPEC.md` 1.8 says a 12 u scroller.
- Cause: the stock slider keeps its default minimum width. Fix: give `.cr-scroll .unity-scroller__slider` `width: 12px; min-width: 0`.
- Not the default Unity look any more, so V10 holds.

### M-02 (minor, code): Settings is not part of the mutual guard
- Probe: with Credits open, `ShowHowTo()` is refused, but `ShowSettings()` opens (`settings=True credits=True`), and closing Settings then shows the menu under the open Credits panel. `ShowHowTo` also has no `SettingsOpen` guard.
- Not reachable from the UI: the menu's Settings button is hidden while Credits is open, and the play-menu path runs only in a match. The implementer's report and `HUD_SPEC.md` 1.8 ("and the reverse") overstate it.
- Fix: in `ShowSettings`, return when `!Playing && (CreditsOpen || HowToOpen)`.

### M-03 (minor, visual): gap between the prose and the scroller
- The verbatim OFL lines end between x 430 and x 610; the scroller is at x 900. The gap is about 290 to 470 u.
- Judgement: acceptable. The bar is clearly attached to the view (same top and bottom as the text, and the Back button below it is left-aligned with the text), and the credits lines and the DOTween line reach x 660. It reads as a wide right margin, not as a stray element. A 720 u view with 680 u labels would tighten it without rewrapping any current line; optional.

## Checked and correct

- Back keeps focus through all Up / Down / PageUp / PageDown input; Left and Right do nothing.
- `ScrollBy` clamps to `content - viewport`; `Show` resets the offset to 0.
- `-ptk-credits` stages (0 to 4, 6, 7, then 9) end and release the Offline / Join step; `credits-end.png` shows the DOTween notice as the last line.
- Contrast unchanged: `#9A9890` on `#101113`, about 6.5:1.
- `HUD_SPEC.md` 1.8 matches the code except the two points in M-01 and M-02; it lists two shot names and omits `credits-end.png`.

## SLOP Section 9 score

| Check | Points | Score | Note |
|---|---:|---:|---|
| No V1-V12 | 20 | 18 | M-01 |
| No L1-L8, one spacing scale | 15 | 13 | M-03; tracker overflows the view by 12 u |
| Copy and vocabulary | 15 | 15 | |
| Every element: what state, what action | 15 | 15 | |
| Role HUDs, no leak | 20 | 20 | not applicable, no regression |
| States, keyboard and gamepad navigable | 10 | 9 | real pad not tested |
| Contrast, readable at 1280x720 | 5 | 5 | |
| **Total** | 100 | **95** | no gate failure |

## Unverified

- A real gamepad (d-pad, stick repeat, B) and a real mouse wheel or bar drag: no input may be sent to the desktop. The evidence is synthetic events on the runtime panel in EditMode (800 x 600 panel).
- The hover and focus colour of the dragger (`#E8E6E1`) was read from the stylesheet, not seen.
- No new screenshots were taken; the build was not launched.
