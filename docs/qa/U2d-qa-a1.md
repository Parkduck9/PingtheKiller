# QA Report: Unit U2d (Server Bot Balance) — Attempt 1

- **QA Model:** Gemini 3.8 Flash (Medium via agy)
- **Commit:** `42946a2` ("WIP U2d attempt 1 for QA")
- **Base Commit:** `48abe4a`
- **Date:** 2026-11-02
- **Test Runs & Counts:**
  - `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 errors, 0 warnings
  - Flake Check Run 1: `dotnet test Server.Tests` — 816/816 passed (0 failed, 32 s)
  - Flake Check Run 2: `dotnet test Server.Tests` — 816/816 passed (0 failed, 29 s)
  - Flake Check Run 3: `dotnet test Server.Tests` — 816/816 passed (0 failed, 30 s)
  - Solution Test: `dotnet test PingTheKiller.sln` — Server.Tests 816/816 passed (30 s), Harness.Tests 616/616 passed (6 m 42 s), Total 1432/1432 passed (0 failed)

---

## 1. Independent Seed Sweep

An independent sweep was executed across 40 fresh seeds (`1001..1040`) per configuration using an independent driver (`PingTheKiller.Tests.QA.U2d.U2dQaSweepTests`), completely isolated from the implementer's seeds (`1..40`).

### Comparison Table: Implementer (Seeds 1..40) vs QA Independent (Seeds 1001..1040)

| Config | Source | Survivor Win | Median (p25/p75) | End Reasons | Terminals Done | Capt / Resc / Exec | Timeouts | Matches with >= 1 Rescue | Target Status |
|---|---|---|---|---|---|---|---|---|---|
| **1v7** | Implementer | 53% | 401 s (317/446) | escaped 21, down 17, time 2 | 3.13 | 6.40 / 0.65 / 4.83 | 5% | 53% | PASS |
| | **QA (1001..1040)** | **43%** | **409 s (313/504)** | **down 19, escaped 17, time 4** | **3.10** | **6.63 / 0.75 / 5.08** | **10%** | **53%** | **PASS** (target: win 35–65%, med 180–480 s, timeout <= 15%, rescue >= 30%) |
| **1v3** | Implementer | 48% | 409 s (327/538) | escaped 19, down 16, time 5 | 2.30 | 2.70 / 0.58 / 1.30 | 13% | 53% | PASS |
| | **QA (1001..1040)** | **53%** | **389 s (339/485)** | **escaped 21, down 15, time 4** | **2.18** | **2.45 / 0.43 / 1.35** | **10%** | **38%** | **PASS** (target: win 25–75%, med 120–480 s) |
| **1v1** | Implementer | 33% | 310 s (203/410) | down 25, escaped 13, time 2 | 1.10 | 0.63 / 0.00 / 0.00 | 5% | 0% | PASS |
| | **QA (1001..1040)** | **25%** | **299 s (187/365)** | **down 26, escaped 10, time 4** | **0.95** | **0.65 / 0.00 / 0.00** | **10%** | **0%** | **PASS** (target: win 20–80%, med 90–480 s) |
| **Idle + 7** | Implementer | 100% | 144 s (136/156) | escaped 40 | 4.00 | 0 / 0 / 0 | 0% | 0% | PASS |
| | **QA (1001..1040)** | **100%** | **148 s (136/155)** | **escaped 40** | **4.00** | **0 / 0 / 0** | **0%** | **0%** | **PASS** (target: win >= 90%, med 120–360 s) |

All brief §3 balance targets were successfully met on independent, unseen seeds. No seed overfitting was observed.

---

## 2. Unfair-Information Hunt & Implementer Test Verdict

### 2.1 Code Audit & Adversarial Probing
- **Survivor Bot Knowledge:**
  - `SurvivorBotView` is populated from `_survivorView` and `FixedMap`.
  - Killer position (`KillerX`, `KillerZ`, `KillerYaw`) is populated **only** when `KillerInSightOf(bot)` is true (unblocked 2D segment between bot and killer on `FixedMap`). When blocked, `KillerVisible` is false and killer coordinates are strictly `(0, 0, 0)`.
  - `SurvivorBrain.Remember` refreshes `_killerSeenAt` and `_killerX/Z/Yaw` only if `view.KillerVisible` is true. Out-of-sight positions are never refreshed.
  - Threat level is received only as a discrete band `0..3`. No continuous distance calculations or reverse-engineering of killer location are performed.
  - Teammate radio check: Survivor A seeing the killer does not leak coordinates to Survivor B behind a wall (`U2dQaFairnessTests.SurvivorBot_DoesNotReceiveKillerPositionThroughTeammatesLineOfSight` verified).
- **Killer Bot Knowledge:**
  - `KillerBotView` receives anonymous `Blip` structs containing only `(X, Z, Lagging)`. Survivor IDs, flags, player status timers, and capture counts are completely omitted.
  - Silent survivors are omitted from `_killerView` by `BuildKillerView` (unless legitimate afterimage). Moving silent survivors across the map causes zero divergence in killer intent (`U2dQaFairnessTests.KillerBot_DoesNotReactToSilentSurvivorPositionOrMovement` verified).
  - Terminal progress: `KillerBotView.Terminal` exposes only `bool Done` (`progress >= 100`). Incomplete terminal progress (e.g., 5% vs 95%) is inaccessible to `KillerBrain`.
  - Phantoms vs Real: Phantoms and real signal survivors are indistinguishable blips in `KillerBotView`.
- **Architectural Isolation:**
  - `BotBrain`, `SurvivorBrain`, and `KillerBrain` have zero references to `GameServer` or `Player`. All dependencies are passed via query objects, `NavGrid`, `FixedMap`, and `Random`.

### 2.2 Verdict on Implementer's Fairness Test (`BotFairInformationTests`)
- **Design Evaluation:** PASS.
- The implementer's `BotFairInformationTests` runs lock-step parallel matches with `GameServer.BotProbeForTests`. Right before view population, hidden state (silent survivor positions, incomplete terminal progress, killer position while blocked) is randomized, and restored right after the brain update. If any brain reads hidden state, lock-step match state diverges.
- Negative controls (`Scramble.LegitKillerView`, `Scramble.LegitThreat`) scramble legitimate fields and verify that the test reliably detects divergence.
- Reflection checks confirm brain classes contain no references to server internals.
- **Verdict:** The implementer's test is rigorous and would reliably catch regressions where hidden state leaks into bot decisions.

---

## 3. Must-Not-Regress & Gate Verification

| Check | Verdict | Evidence |
|---|---|---|
| **U2c/U2e Leak & Anonymity Tests** | **PASS** | `DecoyAndLeakTests.LeakSweep_BlockedSurvivorFramesCarryNoKillerCoordinates...` (24 seeds) and `U2bSweepTests.LeakSweep_EveryEncodedFrame...` (14 seeds) both pass with 0 violations. |
| **Phantom Movement & Indistinguishability** | **PASS** | Phantoms inherit constants from `SurvivorBrain` (`BotStopDistance = SurvivorBrain.HackStopDistance = 1.4 m`, burst/pause timings). KS gate passes (p = 0.017 >= 0.01), AUC is 0.805 (within the 0.85 ceiling). |
| **3/4-Terminal Stall Fix** | **PASS** | `MatchLengthTests.RouteReachesGoalsBehindWalls` and `MedianMatchLengthReport` pass. No stall detected across 160 independent sweep matches. |
| **Sandbox Mode (`--sandbox`)** | **PASS** | `--sandbox` executes `UpdateSandboxBot` path only. No hacking, no captures, no match end before 600 s. `Process_SandboxIsNotEnabledByDefault...` passes. |
| **Harness Runs (PLAN §5)** | **PASS** | Re-checked `docs/qa/U2d-harness-run1.json`, `run2.json`, `run3.json`: All 3 runs scored 100/100, passed all gates (H1, H3, H4, H6) with `--expect-los`. |

---

## 4. Rules Untouched & Program Interface

| Item | Verdict | Details |
|---|---|---|
| **GDD §3–§5 Rules Untouched** | **PASS** | All constants in `GameConstants` and `GameServer` were verified by `U2dQaSweepTests.RulesUntouched_GameConstantsMatchGddSections3To5`: SurvivorSpeed = 4.0 m/s, KillerSpeed = 4.6 m/s, CaptureRadius = 1.2 m, RescueRadius = 1.5 m, TerminalRadius = 1.5 m, ExitRadius = 2.0 m, MatchLength = 600 s, LaggingTimeout = 60 s, ExecutionGrace = 2 s, RescueTime = 3 s, PostRescueInvulnerability = 2 s, Cooldowns = 12 s / 15 s, Terminal requirements = (1..2->2/4, 3..5->3/5, 6..7->4/6). No product rules changed. |
| **All-Bot Path & Test Hooks** | **DEVIATION (Benign)** | Brief §2 requested a test hook on `ServerOptions` never set by console or offline paths. The implementer did not add a new boolean flag; instead, all-bot matches are run by setting `AutoStart = true` and `BotCount = N + 1` with no client connection. In `Server/Program.cs`, `--autostart` requires `--bots >= 2`, but the process always creates `TcpTransport`, starts it, and waits on `shutdown.Wait()`. In `Client/OfflineLink.cs`, `OfflineLink` always sends `C2S_Join { Nickname = "Player" }`. Thus, a headless all-bot match without humans is only possible via in-process instantiation in tests. `Seed` and `BotProbeForTests` remain internal/test-only. |

---

## 5. Beyond the Brief

| Check | Verdict | Details |
|---|---|---|
| **Determinism per Seed** | **PASS** | Seed 1005 and seed 1020 executed twice in `U2dQaSweepTests.Determinism_SameSeedProducesBitIdenticalMatch`; produced bit-identical tick counts, winner, end reason, and event tallies. |
| **No `System.Random` in `Server.Core`** | **PASS** | All random generators in `Server.Core` derive from `PrivateRandom` (xoshiro256** initialized via CSPRNG or SplitMix64 test seeds). Zero instances of `new System.Random()` exist in `Server.Core`. |
| **Threading Model** | **PASS** | `GameServer.Tick` and state mutation occur exclusively on the dedicated game-loop thread under `_tickGate`. Sockets run asynchronously off-thread and communicate via thread-safe queues. |
| **Per-Tick Allocations** | **PASS** | Flat allocations verified by `U2bSweepTests.Growth_U2bRulesMatch_AllocationPerTickIsFlat...` over 12,000 ticks. |
| **Re-Targeted Tests Strength** | **PASS** | Audit of re-targeted tests: <br>1. `U2bSweepTests.LeakSweep_EveryEncodedFrame` oracle changes: <br> - `straddlesShownEntries`: Scans only `EntryBytes(kw)` containing strictly the encoded bytes of legitimately shown entries (killer, phantoms, afterimages, signal survivors). Because silent survivors are excluded from `kw.Players`, 4-byte boundaries across fields in `EntryBytes(kw)` contain only shown data. It prevents false positives from float bit coincidences and cannot hide a real leak.<br> - Match-end afterimage exemption: Applies only when `(resultK || resultS)` is true, killer has `PacketAfterimage`, and the survivor was seen within 22 ticks (1.1 s) at that exact coordinate. Permitted by GDD §4.<br>2. `MatchLengthTests`: Relaxed idle floor from 180 s to 120–360 s (matching U2d §3 specs); max bot timeouts <= 2 of 15. Appropriate.<br>3. `SkillTests`: Dummy stop distance updated to 1.4 m (`SurvivorBrain.HackStopDistance`). Appropriate.<br>4. `DecoyAndLeakTests`: Seed sweep extended up to 24 seeds to ensure all 4 killer skills are drawn. Appropriate.<br>5. `U2bInputSandboxBotTests`: Includes line of sight in compared input sequence and hidden angles. Appropriate. |

---

## 6. Findings

- **[minor] deviation: All-bot test configuration reuses `AutoStart` and `BotCount` without a dedicated `ServerOptions` flag.**
  - *Reproduction:* Setting `AutoStart = true` and `BotCount = survivors + 1` without connecting any human client creates an all-bot match.
  - *Expected vs Actual:* Brief §2 suggested a dedicated test hook on `ServerOptions` never set by console or offline paths. The implementer reused existing options.
  - *Impact:* Benign. Neither `Server/Program.cs` nor `OfflineLink.cs` can enter a headless all-bot state without a human player.

---

VERDICT: PASS
