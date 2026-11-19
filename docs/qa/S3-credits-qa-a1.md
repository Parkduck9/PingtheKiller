# S3-credits QA, attempt 1

Reviewer: Claude Fable 5.2 (implementer: Claude Sonnet 5.5). Commit `d4a3045` on `claude/s3-credits`, base `claude/store-prep`.

**Verdict: FAIL** (two SLOP Section 9 gate failures: V10 and keyboard/gamepad navigation). SLOP score 75 / 100.

## What was checked

- Diff `claude/store-prep..d4a3045` read in full (`CreditsScreen.cs`, `CreditsCopy.cs`, `ClientScreens.cs`, `HelpUI.uss`, `DevArgs.cs`, `DevCapture.cs`, tests, docs).
- Unity EditMode, batch mode, absolute `-testResults`: **1627 total, 1625 passed, 0 failed, 2 skipped**. All 8 `CreditsTests` passed. Matches the implementer's report.
- A temporary QA probe test (removed afterwards, not committed) drove the real `ClientScreens` runtime panel (`RuntimePanel`, context Player), forced layout and sent `NavigationMoveEvent`s. Results are quoted under F-01 and F-02.
- Screenshots `docs/qa/S3-credits/menu.png`, `credits-top.png`, `credits-licence.png` (the implementer's; no new shots were taken).
- Player build `Client/Builds/Win64` (built 13:01, commit 13:02): `PingTheKiller_Data/resources.assets` contains the string `SIL OPEN FONT LICENSE`.
- `Client/Assets/Resources/DOTweenSettings.asset` was touched by Unity (line endings only) and reverted. The worktree is clean except this report.

## Findings

### F-01 (blocker, gate): the licence text cannot be scrolled with a gamepad or with the arrow keys
- Repro: open Credits. Focus is on Back. Press d-pad / stick / arrow keys in any direction.
- Expected (brief): the screen works with keyboard, mouse and gamepad, and scrolls. The OFL text must be viewable by the user.
- Actual: directional navigation never leaves Back and never scrolls. Probe log on the runtime panel, content 2035 u in a 700 u view (range 1335 u):
  - `move Down / Up / Left / Right` from Back: `focused=Button 'Back'`, `offset=(0,0)` every time.
  - `move Next` (Tab only): focus goes to the default `ScrollerSlider`; then each `move Down` scrolls 10 u (`offset=(0,10)`, `(0,20)`), so about 134 presses for the full text.
- A gamepad has no Tab, so a gamepad-only player sees only the first 700 u: the credits list and the OFL preamble. Keyboard works only through Tab to an unstyled slider with no visible focus state.
- `HUD_SPEC.md` 1.8 itself says only "the wheel or the scroll bar moves the text".
- Fix direction: scroll the `ScrollView` from `NavigationMoveEvent` Up/Down on the panel (a page or about 120 u per press, key repeat), keep Back focused, and add a test that asserts `scrollOffset` changes.

### F-02 (blocker, gate V10): the scroll bar is the default Unity runtime theme
- Evidence: `credits-top.png` and `credits-licence.png`, x 1288 to 1312: light grey track, near-white dragger and arrow buttons with black triangles. Probe: tracker `RGBA(0.737,0.737,0.737)`, dragger `RGBA(0.906,0.906,0.906)`. No `unity-scroller` rule exists in any project `.uss`.
- Expected: SLOP V10 (default Unity grey sliders) is a gate. The Settings slider was restyled for the same reason (`ClientUI.uss` `.set-slider`).
- This is the first `ScrollView` in the client. Fix direction: style `.cr-scroll .unity-scroller` (track `#1A1C1F`, dragger `#9A9890` or `#E8E6E1`, 1 px `#2C2F33` line, radius 0, hide or restyle the arrow buttons, a focus state).

### F-03 (minor): the Inter copyright line is shown twice in a row
- `CreditsCopy.InterNotice` repeats the copyright and licence sentence that are the first lines of `OFL.txt`. Evidence: `credits-top.png` y 389 and y 410.
- Keep `OFL.txt` verbatim; reduce `InterNotice` to a short label such as `Inter (font)`.

### F-04 (minor): OFL body type is off the type scale
- `.cr-ofl` is 13 u; the scale is 14 / 18 / 28. Use 14 u like `htp-se`.
- Contrast is fine: `#9A9890` on `#101113` is about 6.5:1. At 1280x720 the `s-floor` class raises it to 15 u, so it stays readable.
- The text keeps the file's hard line breaks (about 72 characters) inside a 1240 u column, so it reads as a narrow ragged block. Acceptable for a verbatim licence.

### F-05 (minor): scroll bar position and empty right side
- The view ends at x 1312 because it reuses the 1280 u `howto` frame. The left-aligned frame is the project convention (SLOP Section 8), so the empty right side is not a defect. A narrower text column (about 800 u) with the bar next to it would read better; the bar will also look less stray once F-02 is fixed.

### F-06 (minor, code)
- `ShowCredits` guards `CreditsOpen || Playing` but not `HowToOpen` / `SettingsOpen`, and `ShowHowTo` does not guard `CreditsOpen`. Not reachable from the UI (the menu is hidden), only from code or dev flags.
- `DevCapture.cs`: the `-ptk-howto` comment is now separated from `HowToShot` by the new credits block.

### F-07 (minor, tests)
- `TheContentScrollsAndEndsWithTheOtherNotice` does not test scrolling (child count only), which is why F-01 passed.
- No test for "Credits is disabled while connecting". `EscAndGamepadBClose` uses synthetic events only; gamepad B on hardware stays unverified.
- The hype word list in `TheCopyFollowsTheSlopRules` is not the SLOP Section 4 list (Unleash, Epic, Ultimate, Immersive are missing).
- Otherwise the tests are meaningful: open/close with idempotent close, focus return on the real `ClientScreens`, full OFL text from the resource and present on screen, no publisher / MCP / Newtonsoft, an active `ReleaseGate` test.

## Checked and correct

- Open/close state: the menu is hidden while Credits shows; `Phase` hides Credits on any screen change; `Close` is idempotent, so Esc arriving as both `KeyDownEvent` and `NavigationCancelEvent` is safe.
- Focus returns to the Credits button (test on the real `ClientScreens`); Back has focus on open.
- Input behind the screen: the Join Enter handler is on the hidden address field; the Esc menu, intro card and B-cancel paths in `ClientApp` run only in a match, and `ShowCredits` refuses while `Playing`. Credits is disabled while connecting.
- OFL text: `Resources.Load<TextAsset>("Fonts/OFL")`, a `Resources` asset (in the repository since U4), present in the built player. Not editor-only. 4379 characters, full text.
- Content: NIPAD, Robot Kyle and Rio with no publisher, DOTween notice, no unity-mcp or Newtonsoft.
- `-ptk-credits`: only acts with `-ptk-shots` after the menu shot, ordered after Settings and How to play, then closes. Same exposure as the other dev flags.
- Menu spacing (orchestrator's note): Credits uses `set-open` (margin-top 24 u) like How to play and Settings, so the three are evenly spaced (84 u pitch). The tighter Join / Offline gap predates this unit. Join stays the single dominant action (L3 holds).
- Docs: `CREDITS.md` and `HUD_SPEC.md` 1.8 match the code (button position, frame, 1280 x 700, 13 u, copy source, flag, shot names). 1.8 must be updated when F-01, F-02 and F-04 are fixed.

## SLOP Section 9 score

| Check | Points | Score | Note |
|---|---:|---:|---|
| No V1-V12 | 20 | 8 | **V10 gate failed** (F-02) |
| No L1-L8, one spacing scale | 15 | 12 | F-04, F-05 |
| Copy and vocabulary | 15 | 13 | F-03 |
| Every element: what state, what action | 15 | 15 | |
| Role HUDs, no leak | 20 | 20 | not applicable, no regression |
| States, keyboard and gamepad navigable | 10 | 3 | **gate failed** (F-01) |
| Contrast, readable at 1280x720 | 5 | 4 | 6.5:1; 13 u body |
| **Total** | 100 | **75** | below 85, two gate failures |

## Unverified

- Gamepad B and real pad navigation on hardware (no input may be sent to the desktop). F-01 is from `NavigationMoveEvent`s on the runtime panel in EditMode, with an 800 x 600 panel.
- Mouse wheel and scroll bar drag (the screenshots show the scripted scroll only).
- No new screenshots were taken; the build was not launched.

## Fix list for attempt 2

1. F-01: Up/Down (keys, d-pad, stick) scroll the text while Back keeps focus; test it.
2. F-02: style the scroller to the palette, with a focus state if it stays focusable.
3. F-03, F-04: drop the duplicate copyright line; 14 u body.
4. Optional: F-05 column width, F-06 guards, F-07 tests. Update `HUD_SPEC.md` 1.8 and retake the three shots.
