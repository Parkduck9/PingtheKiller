# U3d QA report, attempt 2

QA agent: Claude Sonnet 5.5 (implementer Claude Opus). Input: `docs/briefs/U3d-a2.md`, `docs/qa/U3d-impl-a2.md`, the attempt 2 code in the working tree. Nothing committed. Writes: `Tools/Harness.Tests/QA/U3d/**` and this file.

## 1. F1 fix verification

The three round-1 `Gap_` pins are flipped to must-fail with their exact scenarios, renamed (the name no longer says gap):

| Was | Now | Result |
|-----|-----|--------|
| `U3dJudgeProbeTests.Gap_AnEntryOnAnUnjoinableTickIsUndecidedAndItsRealCoordinatesAreAllowedByTheScan` | `AnEntryOnAnUnjoinableTickFailsAndGetsNoScanAllowance` (Finish throws "never carried"; `AllowedEntry == -1`; byte scan rejects the frame) | pass |
| `U3dH3Gap_...Gap_BlockedLeakOnRenumberedTicksPassesH3` | `U3dH3UnjoinableTickLeakTests.BlockedLeakOnRenumberedTicksFailsH3` (H3 fails, score 0, "never carried") | pass |
| `U3dH6Gap_...Gap_BlockedLeakWithRenumberedTicksPassesH6` | `U3dH6UnjoinableTickLeakTests.BlockedLeakWithRenumberedTicksFailsH6` (138 entries, H6 fails, "never carried") | pass |

xUnit1031 fixed (`U3dStressTests` is now `async Task` with `await Task.WhenAll`). `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings, 0 errors (checked before the gate and again after the last edit).

New unit tests (`U3dEdgeToleranceUnitTests`, all pass): a tick missing inside the range fails (middle, second, one before last); tolerance is exactly 6 (94 and 206 tolerated against range 100..200, 93 and 207 fail); no killer world at all fails; an unjoined world without an entry is only counted; `Finish()` is idempotent and a late killer tick still joins; only Visible or Ambiguous verdicts carry a scan allowance.

## 2. Hunt for new false passes and false fails

### F6 BLOCKER (gate H3), major (H6): the edge tolerance is per tick number, so a leak hides there without limit
The tolerance accepts any survivor frame whose Tick is within 6 below the killer stream's first tick or 6 above its last, and it does not bound how many frames do so. The killer stream starts at tick 1, so a server that numbers every leaking survivor frame `Tick = 0` (1 below the range) is never failed, for every frame of the match.
- H3: `U3dH3EdgeTickZeroLeakTests.BlockedLeakNumberedTickZeroOnSparseFrames`: 8 through-the-wall killer entries with the real position, Tick 0, H3 passes (`... 8 undecided`). Expected fail, actual PASS. FAILS in the suite.
- H6: `U3dH6EdgeTickZeroPerturbedLeakTests.BlockedLeakNumberedTickZeroWithCoordinatesOff1cm`: 138 through-the-wall entries, Tick 0, X/Z about 2 cm off the killer's position, H6 passes 10/10. Expected fail, actual PASS. FAILS in the suite. (With the exact killer coordinates the H6 byte scan, which no longer allows an undecided entry, still catches it: `U3dH6EdgeTickZeroLeakTests...`, which passes. So in H6 the worst case is a position leak to about one float step of accuracy; in H3, which has no byte scan, it is exact.)
- Worst case: unlimited frames per edge tick, every blocked tick of the match, as soon as the server may choose its tick numbers. At the far edge (last+1..last+6) the server would have to guess the last tick, so tick 0 is the practical hole. Round-1 hole closed for a renumbering far from the range, reopened for a renumbering to the edge.
- Suggested fix: do not trust the tick number for the exemption. Check tick continuity per survivor stream (a world whose Tick is not within 2 of the previous world's on that stream is a renumbering and fails; H2 already requires gaps <= 2), and/or cap undecided entry-bearing worlds to the real edge effect (about 6 per recipient per edge). Keep refusing allowance to undecided entries.

### F7 major (false fail against a contract-correct server): a killer stream that starts 3 ticks late fails H6
`U3dH6KillerStreamStartsLateTests.ContractServerWithTheKillerStreamStarting3TicksLateMustPass`: the correct line-of-sight server, the killer's first 3 WorldStates dropped by the proxy. A sees the killer from tick 1, so its first 3 frames carry a legal entry on ticks the killer never carried (3 below the range, inside the 6 tolerance). `Finish()` tolerates them, but they get no scan allowance and the stationary killer's coordinates are in the killer-bit set, so H6 fails with `killer coordinate 5 bytes leaked: 3 occurrence(s) ... only 1 legitimate field(s)`. The two halves of the fix disagree: a tolerated edge entry is rejected by the scan. FAILS in the suite. Same code path at the end of the match if the killer stream stops earlier than the survivors'. Suggested fix: let tolerated edge entries (only those) pass the scan for their own X/Z once F6's continuity check holds, or re-judge them with the nearest killer tick.

### F8 minor: the 6-tick tolerance is exceeded at 300 ms of killer lag
Observed in the loaded run: `killer entry on tick=90 ... 7 ticks outside its observed range 1..83, beyond the edge tolerance of 6` with the killer stream 300 ms late (undecided counts: 3 to 4 at 200 ms, 4 to 5 at 250 ms, 6 to 7 at 300 ms). H3's own older checks (Silent-survivor byte check on late killer frames, "Survivor remained Signal" when survivors lag) already fail from about 250 to 300 ms, so the tolerance matches the existing skew limit; at 300 ms the new rule can be the one to fail. Consider 8.

### Ordering of the H6 final check and the leak scan
`LeakScan()` calls `sight.Finish()` first, so a leak on an unjoined tick fails with the "never carried" evidence, then `Judge` and the byte scan run with `AllowedEntry`. Streaming: a frame whose entry is still pending is skipped by the byte scan and picked up by the final scan; verified by the 138-frame renumbering run failing with "never carried" at the final scan, and by `AnEntryOnAnUnjoinableTickFailsAndGetsNoScanAllowance`. The only ordering defect found is F7 (the tolerated frame is rejected by the later scan).

### Skew 200 to 350 ms, contract-correct servers (`U3dRound2Tests`)
- H3, either stream +200 ms (clear and blocked layouts): pass in every run.
- H3, killer +250 ms: 4 of 5 runs pass; the failure is the older `Silent survivor X=... bytes leaked`. Not a line-of-sight verdict.
- H3, killer +300/+350 ms or survivor +300/+350 ms: fails in older checks (`Silent survivor X=... bytes leaked`; `Survivor remained Signal after zero input and grace period`); a control server without any killer entries fails the same way in every run (survivor lag 300/350: 4 of 4; killer lag 350: 4 of 4; killer lag 300: 3 of 4). One loaded run failed with the new tolerance message instead (F8).
- H6, survivor streams +250 and +300 ms, and B alone +300 ms: pass. H6 with the killer stream lagging is the F3 scenario-flow limit from round 1 (documented in the README now).

### What did not break
Per-recipient judging, spectator rule, field and Id/Role checks, duplicate, SkillEffect and unknown-frame leaks, the evidence counts and `--expect-los` semantics (round-1 tests, all still green).

## 3. Gate

| Run | Build `-warnaserror --no-incremental` | Server.Tests | Harness.Tests |
|-----|-----|------|------|
| 1 | 0 warnings, 0 errors | 713/713 | 550/553: the three deterministic reds below |
| 2 | | 713/713 | 549/553: the three reds plus `U3dH3SkewBeyond200Tests` killer +250 ms (older H3 timing check, flake) |
| 3 | | 713/713 | 550/553: the three reds |
| loaded (32 busy loops) | | 713/713 | 549/553: the three reds plus the 300 ms skew test (F8 text) |
| final, after making the two flaky skew tests stable (200 ms for the must-pass rows; the 300 ms test accepts the three known causes) | 0 warnings | 713/713 | 550/553: the three reds |

Deterministic reds, expected to flip when F6 and F7 are fixed, all QA tests that fail for a real defect:
1. `U3dH3EdgeTickZeroLeakTests.BlockedLeakNumberedTickZeroOnSparseFrames` (F6, H3)
2. `U3dH6EdgeTickZeroPerturbedLeakTests.BlockedLeakNumberedTickZeroWithCoordinatesOff1cm` (F6, H6)
3. `U3dH6KillerStreamStartsLateTests.ContractServerWithTheKillerStreamStarting3TicksLateMustPass` (F7)

Everything else is green, including the three flipped tests, all round-1 tests, the U3, U3b and U3c suites, the geometry fuzz (210k pairs, 0 disagreements) and the stress counts from attempt 1.

VERDICT: FAIL (F6 blocker: the 6-tick edge tolerance is per tick number, so Tick 0 hides an unlimited through-the-wall leak in H3 and a centimetre-accurate one in H6; F7: a contract-correct server whose killer stream starts 3 ticks late fails H6)
