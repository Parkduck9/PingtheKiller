# Brief Q6-survivor-menu — attempt 1: survivor in-match play menu

**Quota fallback:** Codex is out of credits and the Antigravity Gemini Flash sign-in is expired. Per CLAUDE.md a Claude Opus subagent implements and a Claude Sonnet subagent does QA. Put this note at the top of your report.
**Parallel units (other worktrees, same time):** Q1 play-check polish, Q2 Killer edge labels, Q4 v2 sounds. Keep edits to shared files small and in clearly separated blocks so merges stay clean.
**Worktree:** `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\optimistic-ellis-27bdd6` (branch `claude/optimistic-ellis-27bdd6`). Work only here. Do **not** commit, push or merge.

Read first: `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md` (mandatory for UI), `docs/ui/HUD_SPEC.md` §0.5, §0.6, §4.0, §5, §6.6, §12 (Settings).

## Goal

Survivors (third person) get the same in-match play menu the Killer has. Today Esc for a Survivor only releases the cursor and shows the muted prompt `Click to resume mouse look`; there is no way to reach Resume / Settings / Leave match during a match.

## Current code (map)

- `ClientApp.cs:353` `SetMenu(bool open)` — Killer only (`role != Role.Killer` returns). Sends `movement.Final(rig.Yaw)`, `rig.SetCursor(!open)`, `screens.Killer.SetMenu(open)`.
- `ClientApp.cs:387-398` — Esc: Settings open → close it; Survivor → `releasedCursor = true`; Killer → `SetMenu(!menuOpen)`. Left click re-locks for the Survivor. `active = Application.isFocused && !releasedCursor && !menuOpen` gates look, WASD, E, Q and Space.
- `ClientApp.cs:113-115` — Killer menu events wired (Resume → `SetMenu(false)`, LeaveMatch → `Disconnect(...)`).
- `ClientApp.cs:589` `OnApplicationFocus` — focus loss sets `releasedCursor` and sends release zeros.
- `UI/KillerHud.cs:52-63, 140-149, 212` — the Killer menu block (`kmenu`, skill name, effect, Resume / Settings / Leave match stacked 296×40, `MenuHeight = 256`), `SetMenu`, `FocusMenu`, `MenuOpen`.
- `UI/ClientScreens.cs:89-91, 261-273` — Settings host; `SettingsClosed` always calls `Killer.FocusMenu()` during a match.
- `UI/SurvivorHud.cs:41-60, 316-340` — the bottom-centre stack (result line, prompt row `sprompt` 672×40, hold row, sync ring) and `SetPrompt`.
- `World/RoleCamera.cs` — `SetCursor`, `Look(bool active)`.
- `Dev/DevCapture.cs:251-258, 555-556` — `-ptk-menu <s>` opens the menu for 2 s in match 1 and shoots `play-menu.png`; `-ptk-settings` then shoots Settings from the play menu. Today these only work for the Killer because `SetMenu` returns for Survivors.
- Existing source-level tests that pin this code: `Tests/EditMode/QA/CameraMovementQaTests.cs:123` (`SetMenu` text, the `active` expression), `Tests/EditMode/QA/KillerHudQaTests.cs:359-420`, `Tests/EditMode/KillerHudTests.cs`, `SettingsTests.cs`.

## Spec

1. **One shared menu component.** Create `Client/Assets/Scripts/UI/PlayMenu.cs` holding the menu block (skill name M 600, effect S, optional muted line, then `Resume` primary, `Settings`, `Leave match`) with events `Resume`, `OpenSettings`, `LeaveMatch`, `Open`, `SetOpen(bool)`, `Focus...`. It has two layouts: **Stacked** (Killer, HUD_SPEC §6.6: 320×256, buttons 296×40 stacked, gap 8) and **Row** (Survivor, §5: 672×152 block, buttons 160×40 in one row, gap 8). `KillerHud` hosts a Stacked `PlayMenu` instead of its inline copy; its public API (`Resume`, `OpenSettings`, `LeaveMatch`, `MenuOpen`, `SetMenu`, `FocusMenu`, `MenuHeight`) and the Killer menu's look stay the same (keep the USS classes the Killer tests rely on, or update those tests in the same change). No visual change to the Killer menu.
2. **Survivor menu placement (§5).** Esc / gamepad Start replaces the prompt row with the Row menu, same bottom-centre anchor, growing upward. While it is open the prompt row and the hold row stay hidden (`SetPrompt` must not re-show them each frame — same rule as `KillerHud.SetPrompt`). The meter cluster, objective row, result line and sync ring stay as they are: the match keeps running.
3. **Survivor menu content.** Line 1 the own skill name, line 2 its effect (§8 table / existing copy; read Q1's copy files, never edit them), line 3 muted `Captured once: the next capture eliminates you.` only when it is true for the own Survivor **under the current rules** (check `GDD-v2.md` / HUD_SPEC §4.1 for what `CaptureCount` means now; if the sentence is no longer true, write the correct fact or drop the line and say so in the report). New copy constants go in `PlayMenu.cs`, not in Q1's files.
4. **Input gate.** Put the gate in a small pure class in `PlayMenu.cs` (e.g. `PlayMenuGate`: open state + `Active(focused, cursorReleased)`) so it is testable without a scene. `ClientApp.SetMenu` works for both roles: opening sends one zero `MoveInput` (`movement.Final`) and an Interact release if held, releases the cursor; closing re-locks the cursor (if focused). While open: no mouse look, no WASD, E, Q or Space. `RoleCamera`: add only what the gate test needs (e.g. `Look(bool active, Vector2 counts)`), nothing else.
5. **Esc / Start handling (both roles).** Settings open → Esc closes Settings and focus returns to the menu. Otherwise Esc / Start toggles the menu. `Resume` closes it. The Survivor's Esc no longer just releases the cursor. Focus loss keeps today's behaviour (release + `Click to resume mouse look` hint), but a left click must **not** re-lock the cursor while the menu is open (clicking a menu button would otherwise lock the cursor under the open menu).
6. **Settings from the Survivor menu.** `Settings` opens §12 over the match exactly as for the Killer; closing it focuses the menu of the current role (fix `ClientScreens.SettingsClosed`). Update the `SettingsScreen.cs` / `ClientScreens.cs` comments that say "Killer play menu".
7. **Leave match** for the Survivor = the Killer's path (`Disconnect`, offline → empty notice), back to the main menu with the cursor free. Menu state resets on leave, disconnect, game result and the next match start (`menuOpen = false` and the HUD menu hidden).
8. **Focus / states (§0.5).** Resume focused on open; Tab/arrows: Resume → Settings → Leave match; mouse hover/press states as the Killer buttons. Esc / B = Resume.
9. **Dev seams.** `-ptk-menu` and `-ptk-settings` must work for a Survivor client (`-ptk-offline -ptk-role survivor` or whatever the existing role flag is). Add one flag to prove Leave offline in the exe, e.g. `-ptk-menu-leave`: after the play-menu (and settings) shots, press the menu's real Leave button path and shoot `menu-after-leave.png` once the main menu shows. Editing `Dev/DevArgs.cs`, `Dev/DevCapture.cs` and `DevArgsTests.cs` for this is allowed; say so in the report.
10. **Spec doc.** Update `docs/ui/HUD_SPEC.md` §5 only: add `Settings` (row of three 160×40 buttons, gap 8: 160×3 + 16 = 496 ≤ 648), focus Resume → Settings → Leave match, Esc in Settings returns to the menu, Esc no longer only releases the cursor, the shared component. Do not touch other sections (Q2 edits §6).

## Owned files

New: `Client/Assets/Scripts/UI/PlayMenu.cs`, `Client/Assets/Tests/EditMode/PlayMenuTests.cs`, `docs/qa/Q6-survivor-menu-impl-a1.md`, `docs/qa/q6-runs/` (logs, xml, screenshots).
Edit: `UI/KillerHud.cs`, `UI/SurvivorHud.cs` (menu slot + prompt gate only), `UI/ClientScreens.cs`, `UI/SettingsScreen.cs` (comments / focus only), `World/RoleCamera.cs` (look gate only), `ClientApp.cs` (menu/Esc/gate block only — stay away from bot naming), `Resources/ClientUI.uss` (append a clearly marked survivor-menu block), `Dev/DevArgs.cs`, `Dev/DevCapture.cs`, tests under `Client/Assets/Tests/EditMode/` that pin changed code, `docs/ui/HUD_SPEC.md` §5.
**Do not touch:** `ResultCopy.cs`, `HudCopy.cs`, `LobbyCopy.cs`, `KillerHudCopy.cs`, `RulesV2Copy.cs`, `CharacterViews.cs`, bot naming in `ClientApp.cs` / `OfflineLink.cs`, `Killer/EdgePlacement.cs`, `TagLayout.cs`, `UI/RulesV2/RulesV2Wiring.cs`, `Audio/`, `Shared/`, `Server/`.

## Acceptance

- `PlayMenuTests.cs` covers: gate open → `MovementInput` emits exactly one zero and then nothing while keys are "held" through the gate, `RoleCamera.Look` leaves yaw/pitch unchanged, interact released; gate closed → movement and look work normally; Row layout has the three buttons in order with Resume focused-first and the 672 / 160×40 sizes; Killer Stacked layout unchanged (existing Killer tests still green); Settings-closed focus goes to the open role's menu.
- Unity EditMode all green, 0 `error CS`: `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "<worktree>/docs/qa/q6-runs/editmode.xml" -logFile docs/qa/q6-runs/editmode.log`.
- `BuildScript.BuildWindows` succeeds (`-batchmode -quit -executeMethod BuildScript.BuildWindows`).
- Offline exe run as a Survivor with `-ptk-menu`, `-ptk-settings`, your leave flag and shots at 1280×720 and 1920×1080: `play-menu.png`, the settings-from-play-menu shot, `menu-after-leave.png` in `docs/qa/q6-runs/`. Look at every PNG yourself. Also one Killer `play-menu.png` at 720p to show the Killer menu is unchanged.
- Unity runs are long. A fresh worktree has no `Client/Library`; you may seed it with `robocopy C:\Unity\MultiPlayGame\PingtheKiller\Client\Library <worktree>\Client\Library /E /R:0 /W:0 /NFL /NDL` (read-only source). Run Unity in the background or with a long timeout and check the log for completion.

## Safety rules

- Never send keyboard or mouse input to the desktop (no SendKeys, no input simulation). Launching the exe with flags is fine.
- Do not kill any process you did not start. Unity Hub (PID 38484) and any Unity instance on another project (e.g. `C:\Unity\Number`, `C:\Unity\CsharpPrac`) must be left alone. Do not open a Unity instance on another worktree.
- No downloads.
- After Unity runs, revert Client files whose only change is line endings (`git diff --ignore-cr-at-eol --stat` vs `git diff --stat`), and do not leave `Client/Library` or build output tracked.

## Report

`docs/qa/Q6-survivor-menu-impl-a1.md`, ≤ 250 words, header "quota fallback: Codex out of credits → Claude Opus implements". What changed (files), how verified (commands + counts), what is unverified, risks, any file touched outside the owned list and why.
