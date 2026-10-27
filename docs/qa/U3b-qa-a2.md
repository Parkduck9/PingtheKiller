# U3b QA report — round 2 (attempt 2)

QA agent: Claude Sonnet (implementer: Claude Opus). Same write limits as round 1: `Tools/Harness.Tests/QA/U3b/**` and this file. No product code and no existing test was edited, and no assertion was weakened.

## Verdict: FAIL

F1, F2 and F3 from round 1 are fixed. One new major false pass was found (F9), and its test is left red.

## 1. My fake updated to the U2b armed rule

`QaRulesServer` now implements leave-and-re-enter, mirroring `GameServer.Rules.cs`:
- Capture disarms the survivor.
- Any tick with the killer outside 1.2 m arms it.
- Execution needs contact while armed and after the grace.
- `ExecutionGraceSeconds` is the switch (0 or 2).

Broken-fake knobs:
- `ExecuteOnContinuedContact` (the old literal rule).
- `RearmsInContact`.
- `ExecuteAfterContactSeconds` (probe).
- `ExitRadiusOverride` (probe).
- `ProgressDisplayLagsOneTick` (probe).

All attempt-1 tests were re-run unchanged against attempt 2, apart from two items:
- The input-delay tests keep their "H6 passes iff nobody was executed" assertion. With the armed rule they now pass at delays of 1, 2 and 3 ticks (rule independent of input latency).
- The concurrent-control test alternates grace 0 and 2.

The 25 attempt-1 failures caused by the old literal rule are green. Correct controls pass with grace 0 and with grace 2.

| Fake | Expected | Actual |
|---|---|---|
| Correct, grace 0 and grace 2 (plus 90 % speed, 12 concurrent runs alternating grace) | PASS | PASS; evidence shows `held contact 2 s ... A still Lagging` |
| Old literal rule (contact executes), grace 0 and 2 | FAIL | FAIL, `[capture: 2 s contact hold] A executed during the capturing contact` |
| Re-arms while in contact, grace 0 | FAIL | FAIL, same step |
| **Re-arms while in contact, grace 2** | FAIL | **FALSE PASS (intermittent), see F9** |
| Executes after 1.0 s or 1.9 s of continuous contact | FAIL | FAIL |
| Executes after 2.5 s of continuous contact | limit | PASS (the hold is 2 s, F9b) |
| Exit open one frame before the second terminal shows 100 | FAIL | FAIL, `ExitOpen=1 in A's WorldState ... only 1 of 2 ... (exit opened without terminals)` |
| Escape at 2.6 m | FAIL | FAIL, `GameResult before escaper reached the exit` |
| Escape at 2.2 m | limit | PASS (tolerance is 0.5 m) |

## 2. Round 1 findings re-verified

- **F1 (premature result):** `U3bResultBeforeAnyoneReachesExitTests` fails correctly: `[escape by B] GameResult before escaper reached the exit`. Fixed.
- **F2 (exit without terminals):** `U3bExitOpensWithoutTerminalsTests` fails at 5 s: `ExitOpen=1 in killer's WorldState ... exit opened without terminals`. Fixed.
- **F3 (leak-scan false positive):** the clean-frame repro test now passes. Fixed.
- All other leak, GameResult, rescue and liveness fakes still fail H6 with evidence naming the step.
- The KnownLimit tests for F4–F6 (laundering, late duplicate result, rule magnitudes) still characterise a pass and are unchanged.

## 3. New findings

| # | Severity | Finding |
|---|---|---|
| F9 | major (false pass, intermittent) | The contact hold lasts 2 s, which is exactly the grace. A server that re-arms while still in contact never executes inside the hold, because the grace disarms it until about 2.0 s after capture and it would execute only about 2.1 s after. The killer leaves at about 2.0 s, so the bug passes H6 in the configuration that ships. Whether H6 catches it is a race: the test `U3bRearmsInContactWithGraceTests` was caught in 3 of 7 runs and missed in 4. With grace 0 it is caught every time. Fix: hold at least grace + 1 s (3 s). A correct server never executes during contact, so a longer hold is safe. |
| F9b | info | The same hold length lets a server that executes only after 2.5 s of continuous contact pass (characterised: `KnownLimit_ExecutionAfter2Point5sOfContinuousContact_StillPassesH6`). |
| F10 | info | Leg and hold timeouts are wall-clock. When the host starves the server below about two thirds of real-time ticks, H6 fails with `stuck on leg` or `[setup] server did not answer within 3.0 s`, bounded, no hang. With 40 or 96 busy processes the whole suite fails this way, including existing H2 tests. With 24 busy processes plus 12 concurrent H6 runs it is clean. Deriving limits from the wire Tick delta would remove it. |
| F11 | info | Structural scan limits, both unreachable in H6 (no skills, no bots): a 5 s SkillEffect `Duration` equals a spawn coordinate of 5.0 and is flagged (clean frame, false positive, characterised). Four consecutive byte fields (ThreatLevel, ExitOpen, ExitLocked, TerminalsRequired) can carry a float and the scan does not see it (characterised). |

Structural scan probes that came out clean:
- 60,006 clean frames of every server-to-client packet type with 120 round killer values: 0 flagged.
- Round-trip edge cases: NaN payload, -0, infinities, 48-byte, empty, surrogate-pair and Korean nicknames. None is reported as unexplained bytes.
- Killer bits inside a nickname are detected at offsets 0, 1, 44 and after a multibyte character.
- Killer bits in Pong halves, JoinResult token halves, GameResult ids, MatchStart seed, Tick, and in the Id, X, Z, Yaw or StatusTimer of an unknown entry, plus Duration, CasterId and X in a SkillEffect: all detected.
- Raw/decoded mismatch or trailing bytes: reported as `unexplained bytes`.

## 4. Counts

- `dotnet test PingTheKiller.sln` x3: Server.Tests 485/485 each time. Harness.Tests 284/284, 283/284 and 283/284; the single red test is F9 (`U3bRearmsInContactWithGraceTests`). No other test fails, and all outputs outside QA/U3b are green.
- Loaded run (24 busy processes, 12 concurrent H6 runs, 18 Server.Tests loops): Harness.Tests 284/284, Server.Tests 18/18, 12/12 concurrent controls.
- Stress at 40 and 96 busy processes: see F10.

## 5. Not verified

H6 against the real U2b server.

QA-FAIL
