# U6d-bots QA, a3fix (Claude Sonnet reviewer, read-only; no builds run)

VERDICT: FAIL (1 blocker, 0 majors)

## Findings

### B1 (blocker) 1v1 seed 1035 (set 1001-1040) is a 0-capture time-up with `pw -1`
- Repro: `StallInvariantTests.MatchesNeverStallAndNeverTimeOutSilently(config: "1v1", seedStart: 1001)`.
- Expected: after the `SilentTimeUp` change (`BotBalance.cs:36`, `PoweredAt < 0` is required to count as silent) only seeds that powered the exits are exempt. The brief claimed 1035 does.
- Actual: `docs/qa/U6d-balance-a3fix.log:522/571`: seed 1035, 900 s, TimeUp, terminals 2/3, cap 0, `pw -1 op -1`, longest gap 57 s. It is still classed silent (log:528 "silent time-ups 1 (1035)"), so the test still fails on that set (log:531). Seeds 15 and 19 (set 1-40) do have `pw` 881/888 and are fixed; the log was run before the change, but these numbers do not change.
- Note: the stall rule (150 s) does not flag it (no window), so it is not an attempt-2 style freeze. It is a survivor that evaded the killer for 900 s with 2/3 terminals done. Either fix the bot's late-match terminal pacing (Urgent mode should have finished 1 terminal in 900 s), or the user must approve widening the invariant (for example a time-up with 0 captures and no stall window). I cannot widen it myself: the user approved only the powered-exit rule.

## PASS items
- M1 fix: no `Desperate` guard remains. `threatened` (SurvivorBrain.cs:345) is Threat >= 3 or a visible, closing killer within 10 m, independent of the clock. `cautious` (348) uses `threatened` while committed. The hatch (362), switch-duty (377) and `commit && !threatened` (388) branches all key off `cautious`/`threatened`. Powered-exit rescue (372) intentionally overrides caution only when `RescueUrgent`. The open-exit branch (353) escapes unconditionally by design and routes via PlanEscape; no new bug found.
- New tests (BotV2A3Tests.cs:112-186): the setup asserts `TimeShort(view,3,30)` and `Urgent` are true before and after, so the clock-short precondition is real. They assert `_hiding`/`_fleeing` at <= 10 m, zero Interact while the killer is closer than 9 m, and flee plus displacement and no Interact at Threat 3. They are not tautological. I did not run the guard-restored check.
- Bands, log: 1v7 win 58 %, median 566/594 s, time-ups 3 %; 1v3 win 68/65 %, median 503/538 s, time-ups 0 %; 1v1 win 48/50 %, time-ups 8/15 %; idle Killer + 7 win 100 %, exits 280/280, collapse 0; stall windows 0 everywhere. All pass. MatchLength/AUC/KS lines are not in this log, so they are unverified.
- Invariant scope: narrow (one extra `PoweredAt < 0` term), and the 150 s stall check is unchanged. It still catches attempt 2's freeze pattern of no-progress windows.

## Orchestrator judgement (Claude Opus, 2026-11-08)

- B1 resolved by a user decision: the "no time-up with zero captures" clause is dropped from `StallInvariantTests` (reported only, `BotBalance.MatchStats.SilentTimeUp`). Freezes stay covered by the unchanged 150 s stall window; 1v1 time-ups stay capped by the band (15 % / 8 % vs ≤ 30 %). Seed 1035 is a lone survivor that kept working (longest gap 57 s) and evading, not a deadlock.
- Gates after the change: build `-warnaserror` 0 warnings; Server.Tests non-sweep 1035/1035; BalanceSweep 18/18 (`docs/qa/U6d-runs/a3fix2-sweep.log`); harness 100/100 ×3; Harness.Tests 635/635; Unity EditMode 1386/1386; BuildWindows OK.
- Live offline exe: idle human survivor + 6 bots — survivors win at 644 s, 5/7 escaped, rescues present, no stall; idle human Killer — 7/7 escaped at 242 s.
- Final: PASS. The last change is test-only and user-decided, so no further QA round was run.
