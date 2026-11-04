# QA Review: post-merge botfreeze — Gemini 3.8 Flash

## Evaluation Table (a)–(e)

| Category | Status | Evidence (File:Line) |
| :--- | :---: | :--- |
| **(a) Information** | **PASS** | `Server.Core/Runtime/SurvivorBrain.cs:132-163`: `Remember()` accesses solely `SurvivorBotView` properties (`view.Threat`, `view.Time`, `view.Team`, `view.KillerVisible`, `view.KillerX`, `view.KillerZ`, `view.KillerYaw`). No server-authoritative positions or hidden state are used when `KillerVisible` is false. Sighted positions update only on clear sightlines. |
| **(b) Correctness** | **FAIL** | `Server.Core/Runtime/SurvivorBrain.cs:134, 244-256, 495-496`: State bug in `_campNoted` reset; suicide walk-in into stationary killer at terminal on pass 1; unseen creeping killer in ThreatLevel 3 ignored after 20 s. |
| **(c) Determinism** | **PASS** | `Server.Core/Runtime/SurvivorBrain.cs:40-41, 75-76, 134, 154-158, 233, 245, 495-496`: Timing relies strictly on `view.Time`; no `DateTime`, `Stopwatch`, or wall-clock calls. No unseeded RNG or extra `Random` instances introduced. |
| **(d) Test Validity** | **PASS** | `Server.Tests/Core/SurvivorCampTests.cs:95-170`: Tests drive the brain through real `FixedMap` and `SurvivorBotView`. On previous code, tests fail because `sim.RunUntilHacking` times out with bot frozen indefinitely at `ThreatLevel >= 2`. |
| **(e) Phantom Risk** | **PASS** | `docs/qa/merge-1104-review/botfreeze-impl.md:24`, `Server.Core/Runtime/SurvivorBrain.cs:276-282`: KS median-speed p = 0.0166 meets gate criterion (>= 0.01), but represents a severe drop from 0.38 due to reduced standstill time in ThreatLevel 2+. |

---

## Findings

### Finding 1 (Major): `_campNoted` is not reset on sighted killer movement, disabling terminal avoidance on subsequent camp cycles
- **File / Lines**: `Server.Core/Runtime/SurvivorBrain.cs:134`, `157-158`, `245-256`
- **Description**:
  `_campNoted` is reset to `false` exclusively when `view.Threat > _lastThreat` (line 134). When a killer in ThreatLevel 2 moves in sight, `_killerMovedAt` is updated to `view.Time` (line 158), which correctly reactivates `acute = true` for 20 s. However, `_campNoted` is **not** reset when `_killerMovedAt` updates. When those 20 s elapse without the killer moving again, `acute` becomes `false`, but `!_campNoted` evaluates to `false` at line 245. The bot fails to set `_avoidTerminal` and does not release `_terminal = -1` on subsequent stale threat cycles.
- **Reproduction**:
  1. Position bot at an incomplete terminal with a stationary killer at ThreatLevel 2 (e.g., 9 m).
  2. Advance time 21 s: threat goes stale, `_campNoted` becomes `true`, and terminal is avoided for 20 s.
  3. Killer moves in line of sight (> 0.5 m shift), resetting `_killerMovedAt` and making the situation acute again for 20 s. ThreatLevel remains 2 (`view.Threat > _lastThreat` does not fire).
  4. Killer stops and remains idle for 21 s: situation goes stale again (`acute = false`).
  5. Check `_campNoted` and `_avoidTerminal`: `_avoidTerminal` is not set because `_campNoted` stayed `true`.
- **Expected**: Any new acute episode (whether from threat rise or sighted movement) resets `_campNoted = false` so that subsequent stale phases trigger terminal avoidance.
- **Actual**: `_campNoted` stays `true` indefinitely within the same ThreatLevel, permanently disabling the 20 s terminal leave logic after the first cycle.

### Finding 2 (Major): Bot walks directly into killer capture radius on ChooseTerminal fallback pass
- **File / Lines**: `Server.Core/Runtime/SurvivorBrain.cs:244`, `494-496`
- **Description**:
  In `ChooseTerminal`, pass 1 allows picking a terminal near a killer if `t - _killerMovedAt > CampSeconds` (line 496). When `acute` is false, `danger` at line 244 also evaluates to `false` even if the killer is in plain sight and within point-blank range (`killerDistance <= GameConstants.ThreatLevel2Distance && acute` requires `acute`). The bot selects the terminal and walks straight to `HackStopDistance` (1.4 m), with no proximity check or collision avoidance against the killer. If the killer is camping at or directly along the path to the terminal, the bot walks straight into the killer's capture radius.
- **Reproduction**:
  1. Set up an idle killer stationed directly at the terminal (e.g., distance 0.5 m from terminal centre).
  2. Provide only this terminal as incomplete.
  3. Let 20 s elapse so `acute` expires.
  4. Bot selects the terminal on fallback pass 1 and plans a direct route to `(terminal.X, terminal.Z, 1.4f)`.
- **Expected**: Even when attempting to hack near a camping killer, the bot should avoid paths that cross within killer capture distance (`GameConstants.CaptureRadius`).
- **Actual**: Once `acute` is false, stationary killer proximity is completely ignored, and the bot walks into the killer.

### Finding 3 (Minor): Creeping killer within ThreatLevel 3 unseen is ignored after CampSeconds
- **File / Lines**: `Server.Core/Runtime/SurvivorBrain.cs:134`, `233`, `242-244`
- **Description**:
  Because ThreatLevel 3 is the maximum threat band, `view.Threat > _lastThreat` can never trigger while the killer remains in ThreatLevel 3. If an unseen killer enters ThreatLevel 3 (e.g. 5.5 m behind a wall), `_threatRoseAt` triggers once. If 20 s pass without line of sight, `acute` becomes `false`. If the killer creeps closer from 5.5 m to 1.5 m unseen, `view.Threat` remains 3, so `acute` remains `false`. ThreatLevel 3 is treated as non-danger (`danger = false`).
- **Reproduction**:
  1. Killer approaches unseen to 5.5 m (ThreatLevel 3).
  2. Wait 21 s: `acute` expires.
  3. Killer creeps from 5.5 m to 1.5 m behind cover over 10 s.
- **Expected**: ThreatLevel 3 represents imminent lethal proximity and should not decay to complete non-danger without confirmed sighting or distance retreat.
- **Actual**: Bot resumes full hacking/walking routine at ThreatLevel 3 point-blank range because the threat did not rise above 3.

### Finding 4 (Minor): Pacing divergence creates statistical risk at the Indistinguishability gate
- **File / Lines**: `Server.Core/Runtime/SurvivorBrain.cs:276-282`, `docs/qa/merge-1104-review/botfreeze-impl.md:24`
- **Description**:
  Median-speed Kolmogorov-Smirnov p dropped from 0.38 to 0.0166, which is barely above the gating threshold of 0.01. At line 280, when acute danger expires, the bot resets `_pauseUntil = t` and starts walking immediately with zero pause. This increases moving time and decreases standstill time under ThreatLevel 2+ relative to phantom pacing.
- **Reproduction**: Run `Tools/Harness` indistinguishability evaluation across full seed batches.
- **Expected**: KS p-value maintains comfortable margin above 0.05.
- **Actual**: KS p-value degraded 20x to 0.0166, near the rejection boundary.

---

VERDICT: FAIL (0 blockers, 2 majors)