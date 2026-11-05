# U6g-rtt-probe: orchestrator verdict, attempt 1

Implementer: Codex `gpt-6.1-sol` (effort high). QA: Gemini Flash `gemini-3.8-flash-medium` via agy (`docs/qa/U6g-rtt-probe-qa-a1.md`, VERDICT: PASS, 0 blockers, 0 majors). Branch `claude/sync-rtt-probe`, based on `claude/sync-great-timing` `d598cd6`.

## Gates (run by the orchestrator)

| Gate | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors |
| `dotnet test Server.Tests --filter "Category!=BalanceSweep"` | 1003/1003 |
| QA tests `Category=QaU6g` (written by Flash) | 6/6 |
| `dotnet test Tools/Harness.Tests` | 638/638 (12 m 11 s) |
| Harness ×3, `--threshold 100 --expect-los`, fresh sandbox + normal server each run | 100 / 100 / 100, gates passed |
| Unity EditMode | 1387/1387, including `RttProbeIsEchoedWithoutPumpingAndNeverReachesTheMainThread` |
| Unity BuildWindows | Success |

Run outputs: `docs/qa/U6g-rtt-probe-runs/`.

## Spec judgement

Every design rule and every test item 1–9 in `docs/briefs/U6g-rtt-probe-a1.md` passes. I read the diff myself and agree with the QA table.

- Both ends of the measurement use the server clock. The probe is stamped just before `Send`, the same way `SyncSentAt` is, and the echo uses the receive-thread `ReceivedAt`, the same way a sync press does.
- Nonces come from a separate private stream: stream 4 with the seed hook, CSPRNG otherwise. A nonce is never zero and never within ±1 of the previous one.
- Only one probe is outstanding at a time. The timeout is 1 s and does not change the estimate. Samples are clamped to 0.25 s and smoothed by 1/8. All probe state resets on disconnect.
- Offline servers and bots are never probed.
- The client echoes on the receive thread through the outgoing queue, which matches the path of the needle origin and of the press.
- The 60 ms end-to-end test turns Good into Great. The delayed-echo test shows that the window is shifted by a constant and is not widened.

## Beyond the brief

- The harness clients do not answer probes (the brief made this optional). The harness H6 sync presses therefore still run with `rtt=0` on loopback, and the harness does not exercise the online compensation. That path is covered by `RttProbeTests.RealTcpLoopbackEchoesProduceASmallPositiveServerTimedRtt` and the in-memory end-to-end tests. No live playtest over a real WAN link has been done.
- Above 250 ms RTT the compensation stops at the clamp, so such players are still judged late by `rtt − 0.25 s`. This is accepted by design and keeps the cheat bound small.
- Probes go to sandbox-mode humans too. H1–H5 are unaffected (100/100); the harness skips the probes structurally (`Measurements.Masked`, `KillerLeakScan`) and still checks their bytes exactly.

## Verdict

PASS. Committed on `claude/sync-rtt-probe`. Not pushed, not merged.
