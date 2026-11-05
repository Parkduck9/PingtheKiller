# U6f-sync-timing QA, attempt 1

Reviewer: Claude Sonnet 5.5 (fallback for Gemini Flash: agy sign-in required)
Code under review: branch `claude/sync-great-timing`, commit e9895aa, `git diff main..HEAD`.
Re-run by the reviewer: `dotnet test Server.Tests --filter "FullyQualifiedName~SyncTiming|FullyQualifiedName~RulesV2"` gave 74/74 passed. No product code was edited.

## 1. Spec conformance

| # | Spec bullet | Result | Evidence |
|---|---|---|---|
| 1 | One monotonic server clock; test hook | PASS | `GameServer.cs` ctor: `Stopwatch` started at construction, `_clock`; `ServerOptions.ClockForTests` is internal. `Run()` keeps its own pacing clock. |
| 2 | `Input.ReceivedAt` stamped on the receive thread | PASS | `Input` struct has `ReceivedAt`. `DrainConnection` enqueues with `_clock()`. The offline link uses the same path. |
| 3 | Record tick start | PASS | `Tick`: `_tickWallStart = _clock()` is the first thing done under the lock, before the drain. It also records `_tickSimStart`, `_tickDt` and `_tickLag`. |
| 4 | Press mapped to simulated time, full RTT | DEVIATION (benign) | `SimulatedArrival` clamps the age to [-lag, 0.25] instead of [0, 0.25]. With lag 0 it is identical. The lower bound is needed so a late tick maps correctly. `Handle` takes `receivedAt`, and only `C2S_SyncPress` uses it. `SyncPress` judges `SimulatedArrival - RttOf`. |
| 5 | Check stamped when it leaves; bot path consistent | DEVIATION (benign, better) | `SimulatedNow()` is `_tickSimStart + clamp(clock - (wallStart - lag), 0, lag + dt)`. It allows for a late tick, where the brief had clamp(…, 0, step). `SyncBotPressAt = SyncSentAt + fraction × Duration`. |
| 6 | Fail limit | PASS | `limit = SyncSentAt + 1.1 + 0.15 + RttOf`. It fires only when `_tickSimStart + _tickLag >= limit - TimeEpsilon`, so presses that arrived before the tick start were handled first. |
| 7 | `Classify` and constants unchanged | PASS | `Classify` is only moved by context in the diff. No constant changed. |
| 8 | Autopilot aims at the great middle; test updated | PASS | `DevAutopilot.cs`: target = `ZoneStart + GreatSize*.5`. `GreatLead` removed. `RulesV2WiringTests` updated (0.6175 boundary). |
| 9 | No protocol change | PASS | The diff has no change under `Shared/`. |

Gates are the orchestrator's. I did not rerun build, harness, Unity or the full test suite.

## 2. Worked timing cases (computed from the code)

Setup: tick scheduled starts T_j = T_0 + j·0.05. Sim start S_j = T_j + c (c constant while no lag). Mapping: sim t is at wall (W − L) + (t − S), where W = `_tickWallStart` and L = `_tickLag`.
Check issued in tick k with in-tick send delay d (L = 0): `SyncSentAt = S_k + d`, left at wall T_k + d.
Press arrives at wall a = T_k + d + f·1.1 (f = needle fraction, RTT 0) and is handled by tick m, the first with W_m = T_m ≥ a.

- **Arrival 0 ms after tick m's start.** age = T_m − a = 0. arrival sim = S_m − 0 = S_k + d + f·1.1. judged = f. Great if f is in the great part.
- **Arrival 25 ms after tick m's scheduled start.** The press waits for tick m+1: age = T_{m+1} − a = 25 ms. arrival sim = S_{m+1} − 0.025 = S_m + 0.025. judged = f. Same result (the 25 ms is just the in-tick offset).
- **Arrival 49 ms after tick m's start.** Handled by tick m+1, age = 1 ms. arrival sim = S_{m+1} − 0.001 = S_m + 0.049. judged = f. Great.
  Result: the judged fraction is f exactly, independent of the offset, because both the send time and the arrival are mapped through the same scheduled grid.
- **Late tick, lateBy = 30 ms.** Check tick: W = T_k + 0.03, L = 0.03, base = T_k. Sent at `S_k + clamp(0.03 + d, 0, 0.08) = S_k + 0.03 + d`, which is wall T_k + 0.03 + d, so it is consistent. Press tick with L': age = T_m − a (it can be negative down to −L'), and arrival sim = S_m + (a − T_m). Mapping is exact. judged = f.
- **RTT = 0.1 s.** A press whose needle reading is f arrives at a = sent + f·1.1 + 0.1. judged = (arrival_sim − 0.1 − sent)/1.1 = f. The Fail limit becomes sent + 1.25 + 0.1. A press at needle 1.25 s arrives exactly at that limit. If the tick starts at or after the limit, the drain still handles the press before the rules run.
- **Bot press.** Press at `SentAt + (ZoneStart + GreatSize/2)·1.1` (or the middle of the rest of the zone). It is judged at exactly that moment, so its fraction is exactly the middle of the part, and it is Great or Good with the old shares. It resolves on the first tick that starts at or after that time, at most one tick (50 ms) after the old resolution tick. Only the moment of the `S2C_SyncResult` can differ, not the verdict. The Great/Good/Fail counts 30/162/22 are the implementer's claim, which I did not rerun on main.

Other checks:

- **Are `SimulatedNow` and `SimulatedArrival` on the same scale?** Yes. Both use base (W − L) for the same tick: `SimulatedNow` computes `S + (clock − (W−L))`, and `SimulatedArrival` computes `S − ((W−L) − r)`, which is `S + (r − (W−L))`.
- **Can `SyncSentAt` exceed the tick's end?** Yes, by up to the lag L (cap `lag + dt` from S). Example: L = 45 ms and d = 7 ms gives S + 52 ms, which is past S + dt. This is consistent with the wall time (the next tick is also late), so it is not a defect.
- **Can a press be judged before the check was sent?** Only if a client guesses the next sequential `CheckId` and presses early. It is ignored before the check is pending (the drain precedes the issue), or gets a negative fraction and Fail afterwards. It only hurts the cheater.
- **Can the Fail limit fire before a legal late press is handled?** Not in the design (drain before rules, limit tested against the tick start). There is a microsecond race: the receive thread takes `_clock()` and then enqueues, so a press stamped before the tick start could be enqueued after the drain. The window is negligible.
- **Bot path shares:** unchanged by construction.

## 3. Threading

- A `Stopwatch` that is started once and never mutated can be read from several threads. `Elapsed` only reads the start timestamp and the static `QueryPerformanceCounter`. Safe.
- `_clock` is a readonly delegate. `ClockForTests` is read only in the constructor.
- `_tickWallStart`, `_tickLag`, `_tickSimStart` and `_tickDt` are written inside `Tick` under `_tickGate`. They are read only by `SimulatedNow`, `SimulatedArrival`, `UpdateSyncChecks` and `IssueSyncCheck`, which run inside `Tick`. Nothing reads them outside the gate. Safe.

## 4. Cheating and abuse

- The 0.25 s clamp always dates a stale press later than its true age, never earlier. A client cannot create the delay; only a server stall of more than 0.25 s causes it. Not exploitable.
- The server timestamps by arrival, so a client cannot claim an earlier moment. It can only choose when to send. An aimbot that reads `ZoneStart` from the packet can already time a perfect Great (existing property, unchanged).
- `RttSeconds` is never set in production, so it is 0. A client cannot gain Great from it, because the server subtracts nothing. The effect is the opposite for honest remote players (see finding 1).
- The sequential `CheckId` is guessable, but a premature press only gives that player a Fail.

## 5. Tests

- The new tests drive the real `GameServer.Tick(dt, lateBy)` and a real `C2S_SyncPress` through the in-memory connection (`Peer.Send`), so they exercise `DrainConnection`, the `ReceivedAt` stamping, `Handle`, `SyncPress`, `UpdateSyncChecks` and `IssueSyncCheck`. The only shortcut is the injected clock (the stated hook) and the explicit `lateBy` argument to `Tick`. The expected fractions come from `At()` = sent wall + f·Duration + rtt, computed independently of the server's mapping. They are not tautological. The sweep also asserts that the arrival really lands at the intended in-tick offset.
- The edge tests use ±0.55 ms around each edge with three send delays (0, 21, 49 ms), and the clamp test checks both the clamp and the unclamped alternative (which would be Good).
- Changed old assertions: `PressAt` (RTT = arrival − target) is justified by the full-RTT rule. The unanswered-check test moves from 26..28 to 28..30 ticks (1.1 + 0.15 + 0.2 = 1.45 s = 29 ticks), justified. The wiring test boundary at 0.6175 matches the new autopilot target. None is weakened.
- Gaps: no test sets `lateBy` together with a nonzero RTT, and none covers a bot press that resolves in a late tick. The bot test is statistical (±0.12 and ±0.1 bounds), so it would not catch a small share shift. No test pins the stall plus backlog-drop case (finding 2).

## 6. Live evidence

`live-great-sync-lines.txt`: the local player is 4. Checks 1, 2, 4, 5, 6, 7 and 8 are all Great. That is 7/7. Their press needles are each inside the great part (zone .58 with a press at .606, .71/.733, .54/.558, .55/.572, .63/.654, .72/.741 and .50/.520; the great part is .035 wide). The press overshoot past the great middle is 0 to 8.5 ms in needle time, so the margin (17.5 ms to the end of the great part) is real but not large.
`live-good-sync-lines.txt`: the local player is 4. Checks 1, 2, 3, 4, 5, 6, 8 and 10 are all Good. That is 8/8, with the needle at 0.0025 to 0.0075 of fraction beyond the mid-zone target. Checks 9 and others belong to bots. The claims 7/7 and 8/8 are supported. The sample is small, but the result is consistent with the maths above.

## 7. Findings

1. **Minor (accepted by the brief, follow-up needed): Great is unreachable for online players with RTT above about 20 to 35 ms.** `RttSeconds` is never set in production, so the judge subtracts 0 and a remote press arrives rtt late. Expected: a press made at the needle's great part is Great. Actual online: judged f + rtt/1.1. With 60 ms RTT that is 0.055, past the end of the 0.035 great part, so Good. The server needs an RTT probe. Offline and localhost are correct. The implementer disclosed this.
2. **Minor: after a stall longer than 1 s, a pending check is judged on a skewed scale.** `Run` drops its backlog, so simulated time falls behind the wall clock, and a check issued before the drop is judged against a different base than a press after it. The press is judged earlier than real (Good or Fail, never an unearned Great), and the effect is limited to a stall with a pending check.
3. **Minor: `SyncSentAt` may exceed the end of its tick (up to the lag).** It is consistent with wall time, so it is not a defect, but the comment in the brief ("clamp to step") no longer holds. The implementer's comment documents this.
4. **Minor: test gaps.** See section 5. The shares of bot sync results are checked with wide statistical bounds.
5. **Minor: microsecond race.** The press stamp is taken before the enqueue, so a press stamped before a tick but enqueued after its drain is handled one tick later. The result is still judged on its stamp, so only the Fail-limit corner could differ. Negligible.

No blockers and no majors found.

VERDICT: PASS
