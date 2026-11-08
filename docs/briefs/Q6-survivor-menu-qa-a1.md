# QA brief Q6-survivor-menu — attempt 1

**Quota fallback:** Codex is out of credits and the Antigravity Gemini Flash sign-in is expired. A Claude Opus subagent implemented; a Claude **Sonnet** subagent does QA (different model from the implementer). Put this note at the top of the report.
**Worktree:** `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\optimistic-ellis-27bdd6`. Uncommitted implementation is in the working tree.

Read: `AGENTS.md` §5 (QA rules: never edit product code), `docs/briefs/Q6-survivor-menu-a1.md` (the implementation brief — its Spec 1–10 and Acceptance are what you check), `docs/qa/Q6-survivor-menu-impl-a1.md` (implementer report), `docs/ui/SLOP_DEFINITION.md` §9, `docs/ui/HUD_SPEC.md` §0.5, §0.6, §5, §6.6.

Changed code: `git diff -- Client/Assets/Scripts Client/Assets/Tests Client/Assets/Resources/ClientUI.uss docs/ui/HUD_SPEC.md` plus untracked `Client/Assets/Scripts/UI/PlayMenu.cs` and `Client/Assets/Tests/EditMode/PlayMenuTests.cs`. Ignore the art/font/png files that `git status` lists as modified (LFS pointers replaced by real content; not part of this unit).

## Tasks

1. **Code review against the spec.** For each Spec bullet 1–10 write PASS / FAIL / DEVIATION with file:line evidence. Hunt for real bugs, especially: input leaking while the menu is open (look, WASD, E, Q, Space, gamepad), cursor state after Resume / Settings / Leave / focus loss / match end, menu left open or visible into the next match or the result screen, Esc while Settings is open, the Killer menu changing in look or behaviour, and anything that sends input the server should not get.
2. **Tests.** Add QA tests under `Client/Assets/Tests/EditMode/QA/` (new file, e.g. `PlayMenuQaTests.cs`) for any gap you find (behavioural, not only source regex). Then run Unity EditMode, all green required:
   `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "<worktree>/docs/qa/q6-runs/qa-editmode.xml" -logFile <worktree>/docs/qa/q6-runs/qa-editmode.log`
   Runs take several minutes; run in the background or with a long timeout and read the log for completion. Report total / passed / failed and 0 `error CS`.
3. **Build + exe.** Run `-batchmode -quit -executeMethod BuildScript.BuildWindows` (log to `docs/qa/q6-runs/qa-build.log`), then launch the built exe yourself offline as a Survivor with `-ptk-menu`, `-ptk-settings`, `-ptk-menu-leave` and shots at 1280×720 into `docs/qa/q6-runs/qa-survivor-720p/` (find the exact flags in `Dev/DevArgs.cs` and the implementer report). Confirm from the player log that Leave returned to the main menu with the cursor free and the process exited cleanly.
4. **Visual review.** Look at every PNG in `docs/qa/q6-runs/` (implementer's and yours: survivor 720p/1080p `play-menu.png`, `settings-play.png`, `menu-after-leave.png`, killer `play-menu.png`). Score the survivor play menu against SLOP §9 (each row with points and gate verdict; threshold ≥ 85 and no gate failure). Check copy against HUD_SPEC §5 exactly (button labels, line 1–3), the 672-wide block in the prompt row slot growing upward, button sizes and spacing, focus outline on Resume, no overlap with the meter cluster, objective row or sync ring, readability at 720p, and that the Killer menu looks as before (compare with `docs/qa/U7a-screens/720/play-menu.png` if present).

## Rules

- Never edit product code (anything outside `Client/Assets/Tests/` and `docs/qa/`).
- Never send keyboard or mouse input to the desktop (no SendKeys or input simulation). Launching the exe with flags is fine.
- Do not kill processes you did not start. Unity Hub PID 38484 and Unity instances on other projects (`C:\Unity\Number`, `C:\Unity\CsharpPrac`) must be left alone.
- No downloads. Do not commit.
- After Unity runs, convert back any Client asset file whose only change is line endings (compare `git diff --ignore-cr-at-eol --stat` with `git diff --stat` for non-binary files).

## Report

`docs/qa/Q6-survivor-menu-1.md`: header with the quota note; verdict PASS / FAIL; per-spec table; findings each with severity (blocker / major / minor), reproduction, expected vs actual, evidence; test counts; build result; SLOP §9 score table; screenshot paths. Return a short summary.
