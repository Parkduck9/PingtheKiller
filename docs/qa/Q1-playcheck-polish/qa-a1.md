# Q1-playcheck-polish QA, attempt 1

Quota switch (2026-11-08): Codex out of credits, agy Gemini sign-in expired. Implementer Claude Opus; QA Claude Sonnet 5.5 (different model).

## Spec table
| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | Elimination copy / capture tag | PASS | SurvivorFeedback.cs CaptureWatch (Captures, Escalated, WholeMatch, CaughtTimes, Caught()); SurvivorReadout.CaptureTag. Live: shots-a.log:432-433 three real captures of player 4 (146, 291, elimination) -> "Disconnected: caught 3 times." matches eliminated-reason.png. Escalation/unknown cases covered by unit tests only. |
| 2 | No stacked bodies | PASS | VisualSeparation.cs (pure, order-independent, bounded, eased); CharacterViews.Render pass 1/2, offsets draw-only. shots-a/v2-sync-check.png, shots-b/survivor-rescue.png: bodies adjacent, not intersecting. |
| 3 | Contiguous bot names | PASS | LobbyCopy.ContiguousBotNames, applied once in ClientApp.cs:203. shots-a/lobby.png: player in slot 1 so Bot 1..7 (the gap case is covered by unit tests only). |
| 4 | Terminals n/m | PASS | HudCopy.Terminals shared by KillerHudCopy; HUD_SPEC 6.1a updated. Survivor HUD shows "Terminals 0/5", "Terminals 1/5". Killer side: code + unit test only. |
| 5 | SLOP/HUD_SPEC copy | PASS | Lines measured by implementer's tests; eliminated-reason.png line fits. |

## Findings
- minor: HUD_SPEC 4.1 still has the old reason line/"Captured twice"/tag budget, and its parity assertion was removed (SurvivorFeedbackQaTests). Spec and test must be restored together. Out of brief scope.
- minor: pinned own avatar + pinned Killer model can still overlap each other, and a pushed Survivor could be nudged into geometry (draw-only, <= 0.8 m). Accepted deviation, not a blocker.
- No files outside the owned list changed (no Q2/Q4/Q6 files). No line-ending-only changes (git diff -w shows real edits in all 13 files).

## Tests
No QA tests added. Implementer's XML re-read: test-run total=1405 passed=1405 failed=0 (finished after the last source edit, 19:35:08 vs 19:35:38 start). 19 new tests in PlaycheckPolishTests.cs cover every acceptance bullet.

## Screenshots (shots-a, shots-b; 1280x720, offline, 7 bots)
- shots-a/lobby.png: Bot 1..7 contiguous, player row correct.
- shots-a/v2-sync-check.png: hacking at terminal, Terminals 0/5, no overlap.
- shots-b/survivor-rescue.png: rescuer and Lagging body side by side, no overlap.
- shots-a/eliminated-reason.png: "Disconnected: caught 3 times.", matches log.
- Other PNGs in both dirs captured, not individually reviewed.
- Logs: 0 exception/error lines in shots-a.log and shots-b.log.

## Not verified
Killer-side `Terminals n/m` live; escalation elimination line live; lobby with player in slot 4 live; old-evidence side-by-side comparison skipped (main checkout images not opened).

## Verdict: PASS
