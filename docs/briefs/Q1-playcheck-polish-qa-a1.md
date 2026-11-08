# QA brief Q1-playcheck-polish, attempt 1

Quota switch (CLAUDE.md fallback, 2026-11-08): Codex out of credits, agy Gemini Flash sign-in expired. Implementer was Claude Opus; QA is Claude Sonnet (different model). Put this in the report header.

Worktree: `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\relaxed-leavitt-607e3e`. Read `AGENTS.md` §5 (QA rules: QA never edits product code), the implementer brief `docs/briefs/Q1-playcheck-polish-a1.md` (the spec you check against) and the implementer report `docs/qa/Q1-playcheck-polish/impl-a1.md`.

## Tasks
1. **Diff review.** `git diff` (and untracked files via `git status`). For each of spec items 1-5 in the implementer brief: PASS / FAIL with file:line evidence. Hunt real bugs too: e.g. CaptureWatch Reset/reconnect paths, a bot rename that collides or differs between clients, separation that moves authoritative/sent state, popping/jitter of the own avatar or camera, null slots, copy that breaks SLOP §4 or overflows HUD_SPEC widths. Check that no file outside the brief's owned list changed (Q2/Q4/Q6 files especially).
2. **Tests.** Confirm the new EditMode tests cover the acceptance list; add QA tests under `Client/Assets/Tests/EditMode/QA/` for gaps you find (pure-function tests only). If you add tests, run Unity EditMode once (background; wait for it; Unity must not already be running on this project):
   `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/Q1-playcheck-polish/qa-editmode.xml -logFile docs/qa/Q1-playcheck-polish/qa-editmode.log`
   Report pass/total. If you add none, say so and use the implementer's XML for the count (verify it yourself: read the XML's `test-run` totals).
3. **Live screenshots.** Use the exe that BuildWindows produced in this worktree (`Client/Builds/Win64/PingTheKiller.exe` in this worktree unless the report says otherwise; check its timestamp is newer than the source edits). Launching the exe with flags is fine. Run two offline Survivor matches **one after the other or concurrently** (each its own `-ptk-shots` dir), 1280x720 windowed:
   - A: `-screen-width 1280 -screen-height 720 -screen-fullscreen 0 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-dev-hack 240 -ptk-dev-switch -ptk-auto-sync good -ptk-skill-at 8 -ptk-shots docs/qa/Q1-playcheck-polish/shots-a -ptk-quit-after 330 -logFile docs/qa/Q1-playcheck-polish/shots-a.log`
   - B: same plus `-ptk-rescue`, shots to `shots-b`, log `shots-b.log`.
   Use absolute paths for the shots dir and log if relative ones misbehave. Run them in the background and wait for the processes to exit on their own (they quit after N s). Never send keyboard/mouse input. Never kill processes you did not start (Unity Hub PID 38484, any Unity editor for C:\Unity\CsharpPrac).
   Then view the PNGs (Read tool shows images) and the logs:
   - `lobby.png`: bot names contiguous (Bot 1..7, no gap), the player row correct.
   - Terminal / sync-check / hacking shots and the rescue shot: no two bodies standing inside each other. Compare with the old evidence in the MAIN checkout `C:\Unity\MultiPlayGame\PingtheKiller\docs\qa\playcheck-1105\surv\v2-sync-check.png`, `survivor-rescue.png`, `lobby.png`.
   - Survivor info block reads `Terminals n/m`.
   - If an elimination happened (`[client] eliminated` in the log): the reason line/`eliminated-reason.png` matches the captures in the log (count `Captured`/status transitions you can find). If none happened, say so — the unit tests then carry item 1.
   - Logs: no exception/error lines.
   Killer-side `Terminals n/m` is verified by unit test + code review only unless you also run a short Killer match (`-ptk-role killer -ptk-chase 60 -ptk-killer-look nearest`, shots-k, quit-after 90) — do it if time allows.
4. **Clean up:** `git checkout --` Client files whose only change is line endings (`git diff -w --stat` to tell). Do not commit.

## Report
`docs/qa/Q1-playcheck-polish/qa-a1.md` (English): header (quota switch, models), spec table PASS/FAIL, findings (severity blocker/major/minor, repro, expected vs actual, evidence path), test counts, screenshot list with one line each, what was not verified. Final message: verdict line + findings summary (≤ 200 words).
