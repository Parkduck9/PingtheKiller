# U6g-rtt-probe QA, attempt 1

Reviewer: Gemini Flash (Antigravity QA)
Code under review: branch `claude/sync-rtt-probe` (worktree `C:/Unity/MultiPlayGame/PtK-work/u6g`, implementation uncommitted).
Pre-existing gate results reported by implementer:
- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings, 0 errors
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: 1003/1003 passed
- `dotnet test Tools/Harness.Tests`: 638/638 passed
- Unity EditMode, BuildWindows and harness x3: pending, run by orchestrator

Per strict QA tool rules, no shell commands or tests were executed by the reviewer.

---

## 1. Design Rules & Test Conformance

| # | Spec Item | Result | Evidence |
|---|---|---|---|
| R1 | Who is probed: online human only, lobby & match; offline/unjoined/bots skipped; RttOf bot returns 0 | PASS | `GameServer.Rtt.cs:18-22`, `GameServer.Terminals.cs:123` |
| R2 | Sending: inside Tick, at most 1 outstanding, clock >= nextProbeAt, nonce from stream 4/CSPRNG (nonzero, non-sequential), probeSentAt right before Send, nextProbeAt = probeSentAt + 1.0 | PASS | `GameServer.Rtt.cs:23-42`, `GameServer.cs:101,109,272` |
| R3 | Echo: input queue, handled in Tick with ReceivedAt, matching nonce & pending required, sample clamped [0, 0.25], clears pending | PASS | `GameServer.cs:153,200`, `GameServer.Rtt.cs:49-60` |
| R4 | Timeout: probe older than 1.0 s cleared without sample, estimate retained, next probe on schedule, stale echo ignored | PASS | `GameServer.Rtt.cs:26-27,53-54` |
| R5 | Smoothing: first sample sets RttSeconds, subsequent samples use RttSmoothingFactor = 1/8, clamped [0, 0.25] | PASS | `GameServer.Rtt.cs:11,55-58` |
| R6 | Reset: disconnect resets RttSeconds, pending probe, samples, and schedules; reconnect starts fresh on next tick | PASS | `GameServer.cs:184`, `GameServer.Rtt.cs:62-69`, `GameServer.Lobby.cs:27` |
| R7 | Log: appends ` rtt={RttOf(player)*1000:0}` to sync check result log line | PASS | `GameServer.Terminals.cs:129` |
| R8 | SetRttForTests: kept, comment updated to reflect override behavior | PASS | `GameServer.Terminals.cs:138` |
| T1 | Serialization round trip, golden bytes, short (<4B) and trailing (>4B) payload rejection | PASS | `Server.Tests/Core/RttProtocolTests.cs:9-32` |
| T2 | Joined human online receives probe on first tick; 80 ms echo sets 0.080; 160 ms echo smooths to 0.090 | PASS | `Server.Tests/Core/RttProbeTests.cs:36-53` |
| T3 | Echo at 0.6 s clamps sample and estimate to MaxRttSeconds (0.25 s) | PASS | `Server.Tests/Core/RttProbeTests.cs:67-83` |
| T4 | Adversarial cannot shrink: wrong nonce, echo before probe, guessed nonce + 1, duplicate, older probe echo rejected | PASS | `Server.Tests/Core/RttProbeTests.cs:85-114` |
| T5a | Adversarial delay: 0.5 s delay converges to 0.25 s clamp; true needle moment judged early; window width unchanged | PASS | `Server.Tests/Core/RttProbeTests.cs:383-397` |
| T5b | Adversarial drop: never echoes keeps estimate at 0; no probe flood; outstanding probe <= 1 | PASS | `Server.Tests/Core/RttProbeTests.cs:116-139` |
| T5c | Answering then stopping keeps last smoothed estimate | PASS | `Server.Tests/Core/RttProbeTests.cs:153-166` |
| T6 | Bots never probed and RttOf stays 0; offline server sends no probes and ignores echoes | PASS | `Server.Tests/Core/RttProbeTests.cs:168-200` |
| T7 | Disconnect and live reconnect resets estimate and outstanding probe; fresh probe on next tick | PASS | `Server.Tests/Core/RttProbeTests.cs:202-231` |
| T8 | End-to-end: 60 ms symmetric latency gets Great with probes, Good without probes (resolving U6f finding 1) | PASS | `Server.Tests/Core/RttProbeTests.cs:368-381` |
| T9 | Real TCP loopback: raw client joins, echoes probes, yields 0 < RttSeconds < 0.05 | PASS | `Server.Tests/Core/RttProbeTests.cs:400-430` |

---

## 2. Security & Timing Analysis

1. **Security & Cheating Immunity**:
   - **Nonce Unpredictability**: Nonces are generated from `_rttRandom` (private stream index 4 or CSPRNG), completely decoupled from gameplay, phantoms, decoys, or sync streams (`GameServer.cs:101,109`). Nonces are 32-bit values drawn from a 256-bit Xoshiro state. Guessing is infeasible ($1 / 2^{32}$).
   - **Cross-Connection & Replay Protection**: `ReceiveRttEcho` strictly inspects `player.RttProbeNonce` for the player bound to `connection.RemoteId`. Echoes referencing another player's probe or an old probe from a prior connection session are silently dropped.
   - **Artificially Delayed Echoes**: An abuser delaying echoes can only inflate `RttSeconds` up to `MaxRttSeconds` (0.25 s). Because the server shifts the judged window earlier by that exact constant offset, pressing at the true visual needle position causes early judgment (Good or Fail). If the player compensates by delaying their press by the offset, the window width remains exactly identical to that of a fair player with 0.25 s RTT.
   - **Burst Flooding**: Multiple echo packets in a single frame/tick are safely handled: the first matching echo clears `RttProbePending`, rendering all subsequent echoes inactive.

2. **Timing Correctness**:
   - `RttProbeSentAt` is stamped using `_clock()` immediately before `Send` is called (`GameServer.Rtt.cs:39`).
   - `ReceivedAt` is stamped on the socket receive thread upon frame arrival (`GameServer.cs:153`).
   - `SimulatedArrival(receivedAt) - RttOf(player)` maps the press to simulated time on the exact same monotonic time grid as `SyncSentAt = SimulatedNow()`. For a symmetric path of round-trip latency $r$, the arrival is delayed by $r$, and subtracting `RttOf` cancels $r$ with zero residual error.
   - Late ticks (`_tickLag`) cancel cleanly because both `SimulatedNow()` and `SimulatedArrival()` compute offsets against `_tickWallStart - _tickLag`.

3. **Client Architecture (`NetClient.cs`)**:
   - In `NetClient.ReceiveLoop`, incoming `S2C_RttProbe` packets trigger `Send(new C2S_RttEcho { Nonce = probe.Nonce })` directly on the background receive thread and `continue` (`NetClient.cs:99-104`).
   - Probes are excluded from `incoming` and never reach `NetClient.Pump()` or the Unity main thread, preventing main-thread frame pacing spikes from skewing network measurements.
   - `NetClient.Send` uses `ConcurrentQueue` and thread-safe atomic counter increments, guaranteeing thread safety when invoked from the receive loop.

4. **Harness Robustness**:
   - `Measurements.Masked` validates that `S2C_RttProbe` frames match the byte-exact codec output (`SequenceEqual`), ensuring coordinate bytes cannot leak into probe frames (`Measurements.cs:77-83`).
   - `KillerLeakScan` recognizes `S2C_RttProbe` as having no coordinate fields (`KillerLeakScan.cs:108-110`). H3 verification remains fully intact.

---

## 3. Findings

1. **Minor: Microsecond boundary at exact timeout (`elapsed == 1.0 s`)**
   - **Severity**: Minor (Edge-case behavior)
   - **Reproduction**: An echo arrives where `receivedAt - probeSentAt == 1.00000000`.
   - **Expected vs Actual**: The condition `if (elapsed >= RttProbeTimeoutSeconds) return;` evaluates to true, dropping the echo without recording a sample.
   - **Evidence**: `Server.Core/Runtime/GameServer.Rtt.cs:54`.
   - **Impact**: Completely benign; any sample at or beyond 1.0 s is already at the timeout threshold. On the next tick, a replacement probe is sent on schedule.

2. **Minor: Test gaps in implementer's suite (now covered by QA tests)**
   - **Severity**: Minor (Test coverage)
   - **Reproduction**: The implementer's test suite did not test cross-player echo injection (Player B sending Player A's nonce), replayed nonces after live reconnection, bursts of invalid nonces before valid ones, or late-tick sync judging under nonzero RTT.
   - **Evidence**: `Server.Tests/Core/RttProbeTests.cs`.
   - **Remediation**: Added 6 tests in `Server.Tests/QA/U6g/U6gRttProbeQaTests.cs` explicitly verifying these cases.

No blockers and no majors found.

---

## 4. Tests Written by QA

File created: `Server.Tests/QA/U6g/U6gRttProbeQaTests.cs`
Namespace: `PingTheKiller.Tests.QA.U6g`
Category Trait: `[Trait("Category", "QaU6g")]`

1. `PlayerACannotEchoPlayerBNonceCrossConnection`: Asserts that Player B sending Player A's nonce over Player B's connection produces 0 samples and leaves Player A's probe pending.
2. `ReplayedOldNonceAfterReconnectIsIgnoredAndPendingProbeIsReset`: Asserts that an old nonce from before reconnection is rejected after reconnecting, and only the fresh probe produces a sample.
3. `EchoExactlyAtTimeoutEdgeIsRejectedWhereasJustBeforeTimeoutIsAccepted`: Asserts that an echo arriving at elapsed = 1.000 s is rejected with 0 samples, while elapsed = 0.999 s is accepted and clamped to 0.25 s.
4. `BurstOfInvalidEchoesPrecedingLegitimateEchoDoesNotCorruptSampleOrDropConnection`: Floods 100 invalid nonces followed by a legitimate echo and duplicate echoes in a single burst; verifies socket remains open and exactly 1 sample is recorded.
5. `LateTickWithNonzeroRttJudgesPressAccurately`: Tests terminal hack sync check with 60 ms RTT handled by a tick with 35 ms lag (`lateBy`), verifying verdict resolves to `SyncResult.Great`.
6. `BackwardsClockArrivalClampsSampleToZeroWithoutUnderflow`: Simulates jitter where arrival is timestamped before sent time (-10 ms), verifying sample clamps cleanly to 0.0 s without underflow or crash.

*Note: Per strict QA tool rules, these tests were NOT run by the QA reviewer.*

---

VERDICT: PASS
