# U3b verdict — PASS (attempt 2 + orchestrator one-line fix)

Implementer: Claude Opus (quota fallback, Codex out of credits; not a failed attempt). QA: Claude Sonnet (`U3b-qa-a1.md` QA-FAIL F1–F3 → fixed in a2; `U3b-qa-a2.md` QA-FAIL F9 → fixed by the orchestrator). Judge: Claude Opus.

**F9 fix (orchestrator, review-time one-liner):** contact hold after capture raised from 2 s to 3 s (= 2 s execution grace + 1 s). Before it, a server that re-arms execution while still in contact passed H6 in 3 of 7 runs with the grace on; after it, it was caught 3 of 3. A correct server never executes during contact, so the longer hold cannot cause a false fail. QA's characterisation test for F9b (execution after 2.5 s of contact passed) is closed by the same change, and the orchestrator flipped it into a must-fail assertion (`ExecutionAfter2Point5sOfContinuousContact_FailsH6`). Evidence strings in two tests changed from "2 s" to "3 s".

## Gate
- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings.
- `dotnet test PingTheKiller.sln` ×3 in a row: Server.Tests 662/662, Harness.Tests 284/284 each run.

## Real-server runs (main `5f83456` = U2b merged, this PC loopback, final build)
Per run: one fresh sandbox server `Server.dll --port P --bots 0 --bind 127.0.0.1 --sandbox` (H1–H5) and one fresh normal server `Server.dll --port P+1 --bots 0 --bind 127.0.0.1` (H6). One harness invocation scores both: `Harness.dll --host 127.0.0.1 --port P --h6-port P+1 --threshold 100`. All servers were stopped after each run.

| Run | H1 | H2 (p99) | H3 | H4 | H5 (rejoin) | H6 | Total | Gates | Exit |
|---|---|---|---|---|---|---|---:|:---:|:---:|
| 1 | 25 | 20 (63.5 ms) | 20 | 15 | 10 (48.1 ms) | 10 | 100 | ✔ | 0 |
| 2 | 25 | 20 (63.4 ms) | 20 | 15 | 10 (47.2 ms) | 10 | 100 | ✔ | 0 |
| 3 | 25 | 20 (63.5 ms) | 20 | 15 | 10 (63.6 ms) | 10 | 100 | ✔ | 0 |

H1–H5 subtotal 90/90 with all gates in every run. A preliminary set of 3 runs on the 2 s-hold build, taken while QA loaded the machine, also scored 100/100 each (H2 p99 73 ms).

## Spec bullets (U3b-a1/a2)
H6 scenario hack → exit → capture → 3 s contact hold without execution → retreat → rescue → escape → GameResult: PASS · leak scan (decode/re-encode equality + field-level comparison + nickname bytes): PASS · per-frame ExitOpen ⇒ terminals: PASS · escape evidence: PASS · `--h6-port` / `--h6-host` and fail-fast: PASS · Server.Core Map-only guard (source + metadata): PASS · passes with the execution grace on and off: PASS · flaky `QaH1Tests.BrokenServerFailsH1` fixed without weakening: PASS · no killer-stream survivor lookup by Id: PASS.

## Accepted limits (README "Known limits")
- F4: killer coordinates laundered through a survivor-shaped field.
- F5: a duplicate GameResult arriving more than 1 s later.
- F6: rule magnitudes (owned by Server.Tests).
- F7, F8: failure text varies; `localhost` and `127.0.0.1` count as different hosts.
- F10: wall-clock leg timeouts fail, bounded, under very heavy CPU load.
- F11: unreachable skill and byte-field corner cases.

## Pending
U3c (U2c anonymous killer view): H3 and H6 must match survivors by tick and position instead of Id, and the decoder must skip unknown server packet Ids (`S2C_SelfState`).
