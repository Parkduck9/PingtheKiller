# U3d QA report, round 3 (attempt 3: tick coverage, no tolerance)

QA: Claude Sonnet 5.5 (implementer: Claude Opus). Branch `claude/u3d-los`, working tree uncommitted.
Read: `docs/briefs/U3d-a3.md`, `docs/qa/U3d-impl-a3.md`, `Tools/Harness/{KillerSight,MatchChecks,ScenarioCheck,WireClient}.cs`.
QA wrote only under `Tools/Harness.Tests/QA/**` and this file. No assertion was weakened, skipped or deleted.

## 1. Gate

| Step | Result |
|------|--------|
| `dotnet build PingTheKiller.sln -warnaserror --no-incremental` | 0 warnings, 0 errors |
| `dotnet test PingTheKiller.sln` run 1 | Server.Tests 713/713, Harness.Tests 616/616 |
| run 2 | 713/713, 616/616 |
| run 3 | 713/713, 616/616 |
| loaded run (32 busy loops, self-terminating) | 713/713, 616/616 (Harness.Tests 5 m 28 s) |

Before QA's round 3 edits the suite had exactly the 13 reds the implementer listed (557/570); that matched.

## 2. Re-classification of the 13 red tests

Each change follows from the clarified contract (a correct server sends every WorldState tick to every human) or a limit that a3 closed.

| Old test (red) | Now | Reason |
|----|----|----|
| `U3dH6KillerStreamStartsLateTests.ContractServerWithTheKillerStreamStarting3TicksLateMustPass` | `ServerWithholdingTheFirst3TicksFromTheKillerFailsH6`: must FAIL, evidence contains `never delivered to the killer`, premise: >= 1 tick withheld | Contract clarification. Under it, F7's "correct server" was a defective one. |
| `U3dEdgeToleranceUnitTests` x4 (`TheEdgeToleranceIsExactlySixTicks` x2 rows, `WorstCase_UnboundedFramesOnAnEdgeTickAreToleratedAndCounted`, `AnUnjoinedWorldWithoutAnEntryIsOnlyCounted`) | `U3dCoverageRuleUnitTests` (tolerance removed on purpose, F6 and F7) | New rule tests: an unjoined world fails at ticks 0, 94, 99, 206, 207, 1,000,000 and uint.MaxValue, with and without an entry; 1000 frames on Tick 0 all fail; no killer world at all fails; an entry-less unjoined world fails; a late killer tick joins; a seeded tick joins. `TickSequence`: gap 1 and 2 legal; gap 3, duplicate, out-of-order, `812 -> 0` renumbering, `812,813,1000000` and a late frame all fail; the first violation is reported. |
| `U3dH6ExpectLosNeedsBlockedAndAbsentTests` x2 | Same two tests, fake rebuilt (`U3dMut.BlockedWorldsBecomeSpectatorWorlds`) | The old fake moved ticks off the killer's stream, which is now a contract violation. The new fake leaves tick delivery untouched and turns the recipient's own entry Eliminated in every blocked-and-absent world, so those worlds classify Ineligible. Intent unchanged: `--expect-los` fails with `Visible > 0`, `Blocked == 0`; the same server passes without the flag. Both pass. |
| U3c `Gap_OffsetLeakDisguisedAsADuplicateOfTheKillersOwnEntryIsNotDetected` x2 | `OffsetLeakDisguisedAsADuplicateOfTheKillersOwnEntryFailsH3`: must FAIL with `no unique own entry` | Limit closed by a3 (the killer world has no unique own entry, so there is no ground truth). Stronger behaviour. |
| U3c `Limit_StreamsMoreThanTheSignalMarginLateFailH3` x4 | `StreamsHalfASecondLateStillPassH3`: must PASS at 500 ms, killer and survivor, id and anonymous | Pre-existing limit closed by coverage-by-tick. |
| (a2) `U3dH3SkewBeyond200Tests.FromThreeHundredMsAnyFailureIsAPreExistingTimingCheck` x4 | `ContractServerPassesFromThreeHundredMs`: must PASS | Brief item 5 and F8. It was green, but it accepted a failure; it now requires a pass. |

## 3. New hole hunt (`U3dRound3Tests.cs`, all green)

- **Duplicate tick carrying a leak (H6, H3):** clean-then-leaky and leaky-then-clean twins are caught (the leak is judged at once, or the tick sequence fails). A clean duplicate alone fails: `tick sequence violated ... 40 -> 40`.
- **Out of order:** two swapped frames fail (`41 -> 40`). Swapped frames carrying a leak fail.
- **First frames:** an offset entry on ticks 1..3 is caught.
- **After GameResult:** a survivor frame with a killer entry fails in each form:
  - Tick+1: `never delivered to the killer`, bounded by `--timeout`, no hang.
  - The same tick: `tick sequence violated`.
  - Rewound to tick 1: leak judged.
- **Killer stream stops:** at tick 150 (the scenario fails fast, no hang), 25 ticks before the end (fails, bounded), and mid-H3 capture at ticks 60 and 75 (fails). It never passes and never hangs.
- **Killer stream holes:** a hole of 1 tick (survivors still got it) fails with `never delivered`. A hole of 5 fails. A duplicate killer tick fails.
- **Retention (64 worlds), H3:** a correct server with the killer or survivor stream 1000 ms late passes, on clear and blocked layouts. Retention is sufficient.
- **Skew in H6:** a correct server passes with the killer, A or survivors late by 250 and 300 ms (a2 tests, still green), and the killer or A late by 500 ms.

## 4. Limits found (documented, not blockers)

- **L1 (pre-existing, not LOS):** H6 at B +500 ms fails a scenario step (`[rescue] ... not observed within 8.0 s`), and at +1000 ms a movement step (`B stuck on leg`). The controls with a server that has no LOS entries fail identically (pinned by two `Limit_` tests). The requirement is 300 ms and passes.
- **L2 (minor, contrived):** killer worlds seeded from the retained buffer are not covered by `TickSequence`. A server that sent the killer a bogus earlier duplicate of a tick would shift H3's ground truth for that tick. The same server is caught by H6 whenever the killer stream is whole. Not exploited by any practical server.
- **L3 (by design, from a1/a2):** F2 (±0.05 m band) and F4 (tolerated legitimate fields) are unchanged. Killer ticks that a survivor stream never delivers are still only skipped (pre-existing U3c).

## 5. Findings status

F1 (unknown tick) and F6 (Tick 0) are closed by construction: every unjoined world fails, and ticks must rise by 1 or 2. F7 (inconsistent scan) is closed: Finish() runs before the scan, with no undecided state. F8 (300 ms) is closed: it passes at 300 ms on any stream, and at 500 ms on H3. F3 (H6 contact hold with killer lag) is closed: the killer at +500 ms passes. No open blocker or major.

VERDICT: PASS
