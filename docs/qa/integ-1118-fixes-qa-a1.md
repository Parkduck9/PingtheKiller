# integ-1118 fixes QA a1 (Fable)

Reviewed: 4bab6df on `claude/integ-1118` (diff `327d35f..4bab6df`). Implementer: Claude Sonnet. Reviewer: Fable. No product code edited, nothing committed. Evidence: `docs/qa/integ-1118-fixes-qa-a1/`.

**Verdict: PASS.** Two minor comment errors (N1, N2), no blocker, no major.

Diff scope: three client lines of behaviour (`AttackPromptRange`, `KillerFxState.Line` order, `RulesV2Preview`), tests, comments and docs. `Server.Core` is untouched; `Shared/` changes one comment. No protocol or masking change.

## Findings re-checked

| Finding | Result | Notes |
|---|---|---|
| F1: large-seed 1v7 test | PASS | See below |
| F2: docs carry merged numbers | PASS | GDD v2 §11 and the impl report give 47.5 / 52.5 and the 9480-seed means (41.7 against main 50.6; 1v3 47.9 against 53.0); the 1v3 effect reads about −5 |
| F3: prompt follows `SwingRange` | PASS | See below |
| F4: swing result over Slowed | PASS | See below |
| F5: leftover 15 s | PASS | `RulesV2Preview` reads `ExitSwitchSeconds`; the harness comments say 17 s; comments only in `Tools/` |
| F7: chained-swing tests | PASS | The 20-tick test is renamed and its comment says one press per second; the new 4-tick theory (hit and miss) asserts whole spans and gaps of at least 1 |
| F6 | skipped by the orchestrator's decision | |

### F1: `LargeSeedOneVersusSevenSurvivorWinRateIsOnTarget`

- Seeds 20001–22000, band 38–46 %, `Category=BalanceSweep`.
- **HEAD passes with room:** 41 % in the test; my sweep tool on the same seeds gives 41.5 % (3.5 points above the lower edge, 4.5 below the upper).
- **Main fails it:** my sweep tool built from main 7b23663 gives 49.4 % on these seeds (`sweep-f1-seeds.txt`), 3.4 points over the band. Run in my scratchpad copy, never in the worktree.
- **Deterministic:** my run and the implementer's run print the same row to the last count (resolved 1266, collapse 415, all down 292, time up 27; median 597 s). Each seed is its own match, so the parallel order does not matter; `AllBotMatchesAreDeterministicPerPrivateSeed` still pins the per-seed replay.
- The 40-seed comment is corrected: it now says the 30–55 % band also passes main.

### F3: prompt range exactly `SwingRange`

- `AttackPromptRange = GameConstants.SwingRange`; `Attack()` is inclusive at the reach. The EditMode test asserts the equality and the three boundary values.
- No margin is the right choice: the prompt now never shows at a distance where the swing cannot reach, which was the old fault (3.0 m against 2.95 m).
- It is still advisory: the hit check runs 0.2 s after the press on the server's positions, so a press at the edge on a Survivor walking away can still miss. That is the swing rule, not the prompt.
- The dev driver keeps its own `DevAttackRange = 3f` ("some reach, some miss" by design), so the implementer's live run (4 hits, 17 misses, 10 presses at 2.98–3.00 m) says nothing about the prompt. Not a finding.
- HUD_SPEC 6.1p says `GameConstants.SwingRange`, 2.95 m, no margin.

### F4: 6.1c priority

- Order is now swing result (1 s) > Slowed > own skill > reason.
- Slowed returns: the result window is bounded (`missedEnd` / `hitEnd`, 1 s), and Slowed's own end time is not touched. The new test shows `Missed` at +0.3 s and `Slowed` again after the result's second, and the same for `Hit`.
- Nothing else is starved: own-skill and reason lines were already below `Missed` / `Hit` before this change; only Slowed moved. With chained misses the result shows from 0.55 s to the next rise at 1.05 s, so Slowed still shows about half of each cycle.
- Cost: the slow countdown is hidden for at most 1 s per swing. HUD_SPEC line 948 states this.
- Not seen live by either of us (no swing landed inside a slow in the implementer's run); unit test and code read only.

## Gates on 4bab6df

| Gate | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror --no-incremental` | 0 warnings, 0 errors |
| Server.Tests, `Category!=BalanceSweep` | 1151 passed, 0 failed (56 s) |
| Server.Tests, `Category=BalanceSweep` | 19 passed; 132 s wall, of which the new test is 119 s |
| Unity EditMode, absolute results path | 1619 total: 1617 passed, 0 failed, 2 skipped; the two changed tests pass |

No live run: the diff changes two client constants' worth of behaviour, both covered by tests. After the Unity run `DOTweenSettings.asset` differed in line endings only and was reverted. The tree is clean apart from this report and its folder. Every process I started has exited.

## New minor notes

### N1 (minor, comment): the new test's time
- The comment says "About 45 s" and the impl report says "expect about 45 s alone". On an idle machine it took 119 s here, and the BalanceSweep filter went from 26 s to 132 s. The 45 s figure was my estimate from the Release sweep tool; the test runs the Debug build next to the other sweeps.
- Fix: say about 2 min.

### N2 (minor, comment): the main figure in the test comment
- The comment says "Main scores 50.5 % on these seeds". 50.5 % is main on seeds 30001–32000; on the test's seeds 20001–22000 main scores 49.4 %. The conclusion (main fails the band) holds.

## Unverified

- A swing during Slowed, live.
- The prompt at exactly 2.95 m in a human's hands; bots and the dev driver do not use the prompt.
