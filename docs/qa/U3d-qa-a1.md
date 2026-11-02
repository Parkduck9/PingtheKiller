# U3d QA report, attempt 1

QA agent: Claude Sonnet 5.5 (implementer: Claude Opus, a different model). Branch `claude/u3d-los`, nothing committed. Brief: `docs/briefs/U3d-qa-a1.md`.

New QA tests: `Tools/Harness.Tests/QA/U3d/` (`RefLos.cs`, `U3dProxy.cs`, `U3dGeometryTests.cs`, `U3dJudgeAndStressTests.cs`, `U3dH6Tests.cs`, `U3dH3Tests.cs`, `U3dFormerLeakTests.cs`). Method: the QA reference (`RefLos`) is a different formulation from the harness (exact integer edge-intersection predicate plus ternary-search penetration depth, versus Liang-Barsky). A QA man-in-the-middle (`U3dProxy`) sits in front of the implementer's contract-correct fakes (`ScenarioServer { LineOfSight = true }`, `LobbyServer { KillerEntry = Contract }`), tracks the killer's own stream by Tick, and rewrites or injects survivor-bound frames, with an optional per-connection delay for stream skew. Each broken server is the contract-correct one plus exactly one mutation, so a failure can be attributed to that mutation (the failure evidence is checked, and the mutation is asserted to have been applied).

## 1. Verdict summary

FAIL, one blocker: an entry shown on a survivor tick that the killer's own stream never carried is counted "undecided" and passes. A through-the-wall leak with the real killer coordinates passes both H3 (gate) and H6 with full score (finding F1). Every other required false pass is caught, every required false fail passes, the geometry fuzz has no disagreement, and the regression is green and deterministic.

## 2. The two existing tests that failed after the spec change

The implementer's claim (a single killer entry in a survivor world is contract-legal) is **true** for both. Independent verification:

| Test | Premise checked | Result |
|------|-----------------|--------|
| `QaH3Tests.LeakingServerFailsH3("killer-entry-in-survivor-world")` | QaServer positions are off-map (z about -10), so no wall lies on any segment; fields Flags/StatusTimer/CaptureCount are 0; Id/Role/X/Z real | H3 evidence: 84 of 84 survivor worlds were `legal line-of-sight killer entries`, 0 blocked. Legal. |
| `U3bLeakKillerEntryByIdOnceTests` | geometry captured at the moment of the leak and judged with `RefLos` | A Lagging in contact: distance 1.11 m, reference margin +11.43 m (clear), flags/timer/capture 0. Legal; H6 passed with `1 legal line-of-sight killer entries` (`U3dH6LegalKillerEntryInContactOnceTests`). |

Edits to existing QA files (every one; each keeps proving that a leak is caught, none deletes or loosens an assertion):

1. `QA/QaServer.cs`: `QaFaults.KillerEntryFlags` (byte, default 0) ORed into the Flags of the killer's entry only when it is sent to a survivor. Default 0 leaves every other test unchanged.
2. `QA/QaH3Tests.cs`: the theory case `killer-entry-in-survivor-world` is now `LeakKillerToSurvivors = true, KillerEntryFlags = Signal` (a naive copy of the killer's Signal state, forbidden because Flags must be 0). Its assertions are unchanged (`AssertFailed`, score 0, gate, "leak" in the evidence). New `LegalKillerEntryInSurvivorWorldPassesH3` asserts that the old, legal entry now passes.
3. `QA/U3b/U3bBrokenFakeTests.cs`: `U3bLeakKillerEntryByIdOnceTests` entry now carries `Flags = Signal`; expected evidence is "Killer 1 leaked to survivor" plus (new) "the contract requires all three 0". Its legal twin is `U3dH6LegalKillerEntryInContactOnceTests`, and a blocked twin is `U3dH6FormerLeakWithBlockedSightStillFailsTests`.

Both edited tests were re-run and pass (they fail H3/H6 as intended); they would fail again if the field check were removed.

## 3. Fake to expected to actual

"Fail" means: the harness check failed with score 0 and evidence naming the cause. Every row passed its test; where actual differs from expected it is marked **MISMATCH**.

### H6 (normal mode, full scenario on the line-of-sight `ScenarioServer` behind the proxy)

| Fake (one mutation of the contract-correct server) | Expected | Actual evidence |
|---|---|---|
| entry sent once while B is behind a wall | fail | fail: `leaked to survivor 3 ... is blocked (penetrates a wall by 0.370 m)` |
| entry sent to B on every blocked tick | fail | fail, same, first blocked tick |
| A sees the killer, B (blocked, same tick) gets the entry too (cross-recipient) | fail | fail: `leaked to survivor 3 ... is blocked` |
| recipient status Escaped, entry present | fail | fail: `the recipient is a spectator (status 2 Escaped)` |
| recipient status Eliminated, entry present | fail | fail: `spectator (status 3 Eliminated)` |
| stale position (previous tick, killer moving) | fail | fail: `0.230 m from the killer's own position ... it equals the killer's position at tick-1 (stale)` |
| X +0.06 m / Z -0.07 m / diagonal 0.057 m | fail | fail: `0.060 / 0.070 / 0.057 m from the killer's own position (epsilon 0.05 m)` |
| X +0.04 m on every entry (inside epsilon) | pass | pass (`U3dH6EntryOffsetInsideEpsilonStillPassesTests`, over 100 entries) |
| Flags = Signal / StatusTimer 0.25 / StatusTimer 1e-30 / CaptureCount 1 | fail | fail: `the contract requires all three 0` (each) |
| Status Lagging in the entry / Yaw +0.5 | fail | fail: `Status=1 ...` / `Yaw=0.5 but the killer's own Yaw is 0` |
| right Id, Role Survivor | fail | fail: `entry Id=1 Role=0; only the real Id 1 with Role Killer may appear` |
| Role Killer under Id 99 | fail | fail: `entry Id=99 Role=1` |
| extra Role-Killer entry under a fresh Id beside the legal one | fail | fail: `2 killer entries in one WorldState` |
| survivor-shaped fresh-Id / Id 0 entry carrying killer X or Z beside the legal entry | fail | fail: `killer coordinate 5 bytes leaked: 4 occurrence(s) ... only 3 legitimate` |
| duplicated entry in the same frame | fail | fail: `2 killer entries in one WorldState` |
| killer X/Z in a SkillEffect (caster 99 and caster = killer) beside the legal entry | fail | fail: `killer coordinate 5 bytes leaked ... S2C_SkillEffect` |
| killer X/Z in an unknown-Id 0x7E frame (aligned, and at odd offset) beside the legal entry | fail | fail: `killer coordinate bytes in unknown packet 0x7E` |
| survivor-shaped leak (no killer-role entry) on a tick the killer never carried | fail | fail: `killer coordinate 5 bytes leaked: 3 occurrence(s) ... only 1 legitimate` (the byte scan catches it) |
| **blocked entry with the real killer coordinates on a tick the killer stream never carried (138 entries)** | **fail** | **PASS, score 10, evidence `... 14 blocked-and-absent ... 138 undecided`. MISMATCH, finding F1** |
| contract-correct server, `--expect-los` off | pass | pass; evidence counts equal the independent tally: visible 2095/2095, blocked-and-absent 152/152, ambiguous 5/5, clear-absent 0/0, undecided 0 |
| contract-correct server, `--expect-los` on | pass | pass |
| survivor streams (A, B) +200 ms, `--expect-los` on | pass | pass, undecided 0 |
| A alone +200 ms | pass | pass |
| killer stream +100 ms and +200 ms | pass | **FAIL for both the line-of-sight server and a server with no entries at all**: `[capture: 3 s contact hold] Killer lost contact during the hold: 1.57 m / 1.80 m from A`. Not line of sight; pre-existing scenario flow (finding F3) |
| `--expect-los`, every blocked-and-absent frame made undecided | fail (needs a blocked-and-absent tick) | fail: `got 2095 legal ..., 0 blocked-and-absent ..., 152 undecided`; same server passes without the flag |

### H3 (gate; `LobbyServer` sandbox match behind the proxy, mutation every 7th tick from tick 30)

| Fake | Expected | Actual |
|---|---|---|
| entry while blocked | fail | fail: `leaked to survivor 2 ... penetrates a wall by 1.000 m` |
| X +0.06 m | fail | fail: `0.060 m from the killer's own position` |
| Flags 1 / StatusTimer 0.1 / CaptureCount 2 | fail | fail: `the contract requires all three 0` |
| spectator recipient (Eliminated) with entry | fail | fail: `the recipient is a spectator (status 3 Eliminated)` |
| entry Id 77 Role Killer / Id 1 Role Survivor / duplicate entry | fail | fail: `entry Id=77 Role=1` / `only the real Id` / `2 killer entries` |
| stopped-survivor leak beside a legal entry (id view, anonymous view, anonymous rounded) | fail | fail: `Silent survivor X=... bytes leaked`, `Silent survivor 2 leaked in the killer view at tick=21` |
| anonymous killer view, contract entries, no leak | pass | pass (`legal line-of-sight killer entries` > 0) |
| streams skewed (killer +200 / survivor +200 / both +200), clear and blocked layouts | pass | pass; 0 to 4 undecided worlds at the stream ends, never a failure |
| killer exactly on corner (18,14), (20,14), (18,32), (20,32), contract server hides it | pass | pass; every world ambiguous (83 or 84 of 83 or 84), 0 visible, 0 blocked |
| killer exactly on corner (18,14), lenient server shows it | pass (band, spec 1) | pass (band) |
| killer 0.03 m outside / 0.03 m inside the corner (server sends the entry when truly clear) | pass | pass, 84 ambiguous each (fields still checked) |
| always-show server, killer 0.03 m inside a wall corner | by design: not judged (spec 1) | pass, `83 ambiguous` (finding F2) |
| always-show server, killer 1 m inside the wall | fail | fail: `penetrates a wall by 1.000 m` |
| **entry through the wall on every 8th survivor tick, those ticks renumbered (8 entries)** | **fail** | **PASS, evidence `54 killer worlds joined ... 74 blocked-and-absent, 8 undecided`. MISMATCH, finding F1** |
| same leak without renumbering | fail | fail at the first such tick (`tick=24`) |

Also green from the implementer's suites (re-run): all six implementer fakes, `H3LineOfSightTests`, the U3, U3b and U3c QA suites (H3 masking under the anonymous killer view is exercised by `U3cH3*`, `U3dH3LegalEntryBesideSilentLeakTests`).

## 4. Line-of-sight fuzz (harness `LineOfSight` against `RefLos`)

| Run | Pairs | Clear | Blocked | Ambiguous | Disagreements |
|-----|-------|-------|---------|-----------|---------------|
| grid (1/4096 m, exact reference; 24 945 zero-length, 13 490 exact touches, band-hugging and lattice points) | 150 000 | 25 774 | 98 280 | 25 946 | contract bool 0, class 0, decided inside the band 0 |
| float (off-grid, random plus band-hugging) | 60 000 | 20 306 | 34 717 | 4 977 | 0 |

Max |margin error| between harness and reference 2.1e-14; `Classify` is symmetric on every pair. Handwritten cases (through a wall, ending inside, touching an edge, touching a corner, flush on a face, parallel 0.03 and 0.06 m off a face, zero length, 0.049 / 0.051 m either side of the band) all agree with the reference. Band pairs reach the judge as `Ambiguous`, never as pass or fail. Sensitivity check: the same comparison against walls shrunk by 0.2 m disagrees on 805 of 20 000 pairs, so "0 disagreements" is evidence and not a lenient comparison.

## 5. Stress counts

| Test | Result |
|------|--------|
| 120 000 contract-correct survivor worlds (40 000 ticks x 3 recipients, spectators, killer hugging walls), joined in random order from 2 threads | visible 27 001/27 001, blocked-absent 49 582/49 582, ambiguous 3 718/3 718, ineligible 39 699/39 699, undecided 0, clear-absent 0 (counts equal the reference exactly) |
| leak judge, 11 leak types, at least 3 000 instances each (blocked, spectator, offset 0.05..5 m, stale, Flags, StatusTimer incl. `float.Epsilon`, CaptureCount, wrong Role or Id, duplicate, Status, Yaw) | 33 507 of 33 507 flagged, 0 false flags over 16 523 legal or blocked-absent controls; the position-independent types are flagged without a killer tick too |
| H6 evidence counts vs independent tally (one real run, 2 252 survivor worlds) | equal in every bucket (see section 3) |
| `dotnet test PingTheKiller.sln` x3 | run 1: Server.Tests 713/713, Harness.Tests 520/520 (308 s); run 2: 713/713, 520/520 (243 s); run 3: 713/713, 520/520 (290 s). No non-deterministic outcome. |
| loaded run (`Harness.Tests` with 32 busy-loop processes on 32 logical cores) | Harness.Tests 520/520 in 280 s (the 3 unloaded runs took 243 to 308 s); every timing-sensitive H6/H3 test still passed; no non-deterministic outcome |

## 6. Findings

### F1 BLOCKER (gate H3) / major (H6): an entry on a tick the killer's stream never carried passes
- **Reproduction:** `U3dH3Gap_LeakOnUnjoinableTicksIsNotDetectedTests.Gap_BlockedLeakOnRenumberedTicksPassesH3`, `U3dH6Gap_LeakOnUnjoinableTicksIsNotDetectedTests.Gap_BlockedLeakWithRenumberedTicksPassesH6`, unit probe `U3dJudgeProbeTests.Gap_AnEntryOnAnUnjoinableTickIsUndecidedAndItsRealCoordinatesAreAllowedByTheScan`. Controls: `TheSameLeakOnJoinableTicksFailsH3`, `SurvivorShapedLeakOnAnUnjoinableTickIsStillCaughtByTheByteScan`.
- **Expected:** the brief says such an entry "must not pass silently".
- **Actual:** `KillerSight.Judge` returns `SightOutcome.Unknown` with `Entry = index` and no leak when there is no killer tick. `SightJoin.Finish()` only counts it; H6's final scan then passes `verdict.Entry` as `legalKillerEntry`, so the entry's X/Z are allowed to hold the real killer coordinates (the unit probe shows the byte scan alone would reject the same frame). H3 has no byte scan of survivor frames, so it is judged by `SightJoin` only. A server that gives a survivor frame a Tick the killer stream never delivers (for example a per-recipient or decimated tick counter, an out-of-band frame) can show the Killer through a wall on every such tick.
- **Evidence:** H6: 138 through-the-wall entries with real killer coordinates, H6 PASS 10/10, `14 blocked-and-absent ... 138 undecided`. H3: 8 entries, H3 PASS (`54 killer worlds joined ... 8 undecided`; H3 still passes because 54 of 62 ticks join, above its 50 minimum).
- **Not a regression of anything correct:** a server with one tick numbering never produces this, and the implementer documented it as limit U3d-1. Under the brief's severity rule it is nevertheless a false pass on a leak, so it blocks the unit.
- **Suggested fix (harness, not product code):** after `Finish()`, count entries on undecided ticks and fail when any lies inside the killer stream's tick range, or when more than a few (for example 3 edge ticks) are undecided; stop passing `verdict.Entry` to the leak scan for an Unknown outcome so the byte scan still sees the coordinates. Edge undecided ticks are legitimate: killer +200 ms gave 3 to 4 undecided worlds at the window end in H3.

### F2 minor, by design: leaks inside the 0.05 m band are not judged
`U3dH3AmbiguousBandIsNotJudgedTests.ByDesign_AlwaysShowingServerWithTheKillerInsideTheBandPassesH3`: an always-show server with the killer 0.03 m inside a wall corner passes H3 (83 ambiguous). The brief's false-pass list asks for this to fail, but spec 1 of `U3d-a1.md` forbids deciding in the band, so the two cannot both hold; spec 1 was followed. The contrast (killer 1 m inside the wall) fails at once. Exposure is a leak limited to segments within 5 cm of a wall.

### F3 minor, pre-existing: H6's capture-hold step cannot tolerate a 100 ms or longer killer-stream lag
`U3dH6KillerStreamSkewTests` (documents it; asserts that any failure is the scenario flow and not a line-of-sight verdict). With the killer stream delayed 100 or 200 ms, the scenario fails at `[capture: 3 s contact hold] Killer lost contact during the hold` for the contract-correct server and, identically, for a server without line-of-sight entries, because the killer steers from an old position. Streams skewed 200 ms on the survivors, or on either stream in H3, pass. Predates U3d; the required "skewed up to 200 ms" is met for everything except the killer stream in H6.

### F4 informational, pre-existing and documented: F4 and U3d-3
A killer coordinate copied into a field the leak scan treats as legitimate (survivor-shaped PlayerState X/Z/Yaw/StatusTimer, TimeLeft, terminals, survivor-cast SkillEffect) is tolerated. Read from `KillerLeakScan.Fields`; not re-tested here, not introduced by U3d.

### F5 informational: StatusTimer is compared bit-exactly
`-0f` in the entry counts as a leak (the implementer's own unit test pins this). A correct server writes `0f`, so there is no false fail; noted only because `-0f == 0f` numerically.

No false fail against a contract-correct server was found: plain and `--expect-los` runs, A or both survivors 200 ms late, the survivor stream or killer stream 200 ms late in H3, the killer exactly at a corner, several survivors with different results in one tick (the cross-recipient and blocked-entry runs only apply because A is visible while B is blocked in the same ticks), and an entry offset 0.04 m all pass.

VERDICT: FAIL (F1 blocker: an entry on a survivor tick the killer stream never carried is only counted as undecided, so a through-the-wall leak passes H3 and H6)
