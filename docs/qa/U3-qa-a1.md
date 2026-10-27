# QA report U3 — attempt 1 (Tools/Harness false-pass hunt)

QA: Claude Sonnet 5.5 (implementer: Codex gpt-6.1-sol). Scope: `Tools/Harness/**` driven only through public entry points (`HarnessRunner.RunAsync`, and the real `Harness.dll` executable for exit codes / report file). No product code touched. New files: `Tools/Harness.Tests/QA/{QaServer,QaHelpers,QaH1Tests,QaH2Tests,QaH3Tests,QaH4Tests,QaH5Tests,QaLivenessTests,QaScorerAndExitCodeTests}.cs` and this report.

`QaServer` is an independent fake (not the Codex `LobbyServer`/`PingServer`): with default settings it behaves like a correct U2a-shaped server (PacketAssembler framing, lobby/match flow, masking, normalized clamping, token reconnect); every knob in `QaFaults` breaks exactly one behaviour. Each check also has a correct-server positive control so a "fail" is not just "everything fails".

## Verdict summary
All 65 broken-fake scenarios made the targeted check FAIL (score 0, gate flag false for H1/H3/H4). All correct-server controls PASS. Silent / refused / mid-match-stalled / chatty servers fail every check within bounded time. No blocker or major findings. 5 minor observations (below).

## Broken fake -> check -> expected -> actual
All "actual" rows are FAIL (score 0/max), i.e. as expected. Evidence is the harness's own text.

### H1 (gate, 25)
| Broken fake | Actual evidence |
|---|---|
| Naive framing (no reassembly, drops partial frames) | FAIL: Pong #4 timestamp mismatch |
| Processes only first frame of every TCP read | FAIL: Pong #1 timestamp mismatch |
| Drop pong #0 / #5000 / barrier (10001st) | FAIL: timestamp mismatch / barrier wait timed out |
| Reorder pongs 100/101, 9998/9999, 9999/barrier | FAIL: Pong #n expected/got mismatch, or barrier missing |
| Alter one timestamp (#0, #4321, #9999, barrier) | FAIL: expected X got X+1 / "Extra or reordered Pong" |
| Duplicate pong #0, #5000 | FAIL: next pong has duplicated (older) timestamp |
| Close connection after 1 / 5000 / 9999 / 10000 pongs | FAIL: "Pong #n missing ... IOException" / disconnect at barrier |

### H3 (gate, 20)
| Broken fake | Actual evidence |
|---|---|
| Killer-bound world keeps silent survivor entry (flag only) | FAIL: "Silent survivor 2 leaked as PlayerState" |
| Entry lingers 1.5 s after the survivor stopped | FAIL: leaked as PlayerState at tick 22 |
| Entry omitted, X+Z float bytes in a TerminalState | FAIL: "X=... bytes leaked in S2C_WorldState" |
| Entry omitted, only X in WorldState.TimeLeft | FAIL: X bytes leaked |
| Entry omitted, only Z in killer's own Yaw | FAIL: Z bytes leaked |
| Entry omitted, X/Z in a separate S2C_SkillEffect frame | FAIL: X bytes leaked in S2C_SkillEffect |
| Survivor-bound world contains the killer | FAIL: "Killer 1 leaked to survivor" |
| Killer can never see a moving survivor (over-masking) | FAIL: Signal baseline timeout |
| Server kicks any client that moves | FAIL: IOException (baseline) |

### H4 (gate, 15)
| Broken fake | Actual evidence |
|---|---|
| Hang (stops answering Ping) / crash after Length -1 | FAIL: timeout / IOException on control Ping |
| Hang / crash after Length int.MaxValue | FAIL: same |
| Keeps abusive socket open for Length 0, 1, -1, 16385, int.MaxValue | FAIL: disconnect wait timed out |
| Hang after unknown packet id | FAIL: control Ping timeout |
| No clamp, direction (100,100) | FAIL: 565.69 m/s > 4.2 |
| Per-axis clamp (diagonal 1.41x) | FAIL: 5.66 m/s > 4.2 |
| Clamp to 1.5x | FAIL: 6.00 m/s > 4.2 |
| Movement applied per input (500/s flood => 25x) | FAIL: 160 m/s > 4.2 |
| Hang / crash on NaN input | FAIL |
| NaN poisons position | FAIL: "Non-finite position" |
| NaN treated as a move | FAIL: "NaN input moved player by 0.2 m" |
| Hang / crash on 500/s flood | FAIL |

### H2 (20)
| Broken fake | Actual evidence |
|---|---|
| Sustained 150 ms world interval | FAIL: p99 162.9 ms > 120 |
| 200 ms stall every 5th world | FAIL: p99 264.6 ms |
| Killer stream only: one 300 ms stall | FAIL: p99 357.6 ms |
| Survivor stream tick gap 3 and 4 | FAIL: "Tick sequence invalid 14 -> 17 / 18" |
| Duplicate tick / tick goes backwards | FAIL: 14 -> 14 / 14 -> 10 |
| (boundary) tick gap of exactly 2 | PASS (spec: only > 2 fails) |

### H5 (10)
| Broken fake | Actual evidence |
|---|---|
| Rejoin returns different PlayerId | FAIL: "Reconnect changed PlayerId 2 -> 3" |
| Position reset / role changed / status changed / capture count reset | FAIL: "Reconnect changed position/role/status/capture count" |
| Join result non-zero / zero token on rejoin | FAIL: "Join rejected: result=2" / "zero reconnect token" |
| Rejoin answered after 3.5 s (wait 2 s) and 5.5 s (wait 9 s, tests the 5 s window) | FAIL: timed out; second stops at ~5 s |
| (independence) broken reconnect + run H4 | H4 PASS, H5 FAIL |

## Bounded-time behaviour (every network wait has a deadline)
| Scenario | Result |
|---|---|
| Accept-then-silent server, `--timeout 0.3`, each of H1..H5 | each FAIL in < 4 s |
| Same, whole default run (H1..H6) | total 0, all 6 fail, < 8 s |
| Connection refused, each of H1..H5 | each FAIL < 4 s |
| Lobby + MatchStart then no WorldState, H2..H5 | each FAIL < 8 s |
| Chatty server (Pong spam only, no WorldState), H2..H5 | each FAIL < 10 s (unrelated traffic does not extend the wait) |
| External cancellation (Ctrl+C path) with 30 s per-wait timeout | fails in < 6 s |
| Real executable vs silent server | exit 1, 6 items, total 0, < 30 s |

## Scorer / JSON / exit code
- All 64 pass/fail combinations of H1..H6: total equals the exact sum of maxima; `gatesPassed` equals H1 && H3 && H4. Maxima sum to 100; H1..H5 all passing = 90.
- Threshold edge: total 90 passes at threshold 90, 89 fails, 91 passes; failed gate with total >= threshold (70/70, and 40/40 via the executable) -> not met, exit 1. Forged items (wrong gate flag, inflated/partial score, unknown id) are rejected by `Scorer.Summarize`.
- JSON (unit and from the real report file): exactly `{ total, gatesPassed, items:[{ id, score, max, gate, passed, evidence }] }`, camelCase, in that order, correct value kinds.
- Executable exit codes verified: 0 (all gates pass, threshold met; full run against correct fake = exactly 90 with H6 not counted), 1 (gate failed with total >= threshold; threshold 61 vs total 60; H6-only even with threshold 0; silent server), 2 (bad `--only`, `--threshold`, unknown option).
- H6: skipped, score 0, passed false, evidence "skipped (needs U2b)", never contacts the server (verified against a closed port), never counted in the total.

## Flakiness
Full `dotnet test Tools/Harness.Tests/Harness.Tests.csproj` three times (TRX compared test-by-test):
| Run | Passed | Failed | Skipped | Total | Time |
|---|---|---|---|---|---|
| 1 | 150 | 0 | 0 | 150 | 48 s |
| 2 | 150 | 0 | 0 | 150 | 48 s |
| 3 | 150 | 0 | 0 | 150 | 48 s |
Zero differing outcomes between runs (40 Codex tests + 110 QA tests). `dotnet build Tools/Harness/Harness.csproj -warnaserror`: 0 warnings, 0 errors. Earlier in this attempt two tests failed in my own fake's teardown (unhandled `InvalidOperationException` after a deliberate crash/close); fixed in the fake, not in any assertion.

## Findings
No blocker, no major. All minor / observations; none changes the verdict.

1. **minor (false-pass, narrow)** H1 does not detect a duplicate of the barrier Pong itself (the last frame it reads). Repro: `QaH1Tests.Gap_DuplicateOfTheBarrierPongItselfIsNotDetected`. Expected: fail; actual: PASS ("10000/10000 ordered ... barrier passed"). A duplicate anywhere in the first 10,000 pongs or at #9999 is caught. Suggest a short post-barrier drain (e.g. 100 ms) before declaring pass.
2. **minor (false-pass, outside spec criterion)** H3 only matches exact little-endian float bytes and only inspects PlayerState Ids. Repro: `QaH3Tests.Gap_RoundedCoordinateLeakOutsidePlayerStateIsNotDetected` (survivor X/Z rounded to 0.1 m inside a TerminalState: PASS). By code inspection (not probed): the "killer never appears in survivor frames" check (`Measurements.KillerHidden`) looks only at WorldState entries, not CasterId in non-world packets. These match the documented "byte-level" criterion; consider widening to a position-proximity check later.
3. **minor (false-fail risk)** H4 requires the abusive socket to be closed after an unknown packet ID, but PLAN.md H4 only requires no crash. A server that ignores unknown IDs and keeps serving would fail the gate. Repro: `QaH4Tests.Observation_HarnessRequiresDisconnectOnUnknownIdEvenThoughPlanOnlyRequiresNoCrash`. Needs an orchestrator decision (spec vs README).
4. **minor (by design, per U3-a2 item 1)** A server that disconnects every client sending a non-zero direction passes H4 alone (defensive disconnect accepted, no movement evidence required). H3's Signal baseline fails it. Repro: `QaH4Tests.Observation_ServerThatKicksEveryMovingClientStillPassesH4Alone` and `ServerThatKicksEveryMovingClientFailsH3`. Gate H3 must therefore always be run together with H4.
5. **minor (observation)** H2 follows the brief literally: p99 over a 60 s run ignores the worst ~1% of intervals (about 12 frames), so a few multi-second stalls shorter than `--timeout` still pass; and the real-time p99 is scheduler-sensitive on a loaded machine (the 2 s correct-fake control was stable 3/3 here). Also H5 fails a rejoin that returns a zero token (stricter than the brief; harmless).

## Unverified
Nothing was run against the real U2a server (not in this tree). Fakes prove the harness can fail and is bounded in time; they do not prove U2a behaviour. The optional `QA_EVIDENCE_FILE` environment variable makes the QA tests append each broken fake's failure evidence to a file.

QA-PASS
