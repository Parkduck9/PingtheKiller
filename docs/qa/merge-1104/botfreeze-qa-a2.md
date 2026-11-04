# QA Review: post-merge botfreeze (Attempt 2) — Gemini 3.8 Flash

## Gate Results (Implementer-Measured)
- `dotnet build -warnaserror`: 0 warnings, 0 errors
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: 955/955 passed (0 regressions)
- `dotnet test Tools/Harness.Tests`: 635/635 passed
- U2c gated AUC: 0.855 (pass threshold >= 0.75)
- Indistinguishability KS median-speed p: 0.0166 (pass threshold >= 0.01; tracked for unit U6d)

---

## Findings Resolution Table

| Finding | Status | Evidence (File:Line) | Analysis |
| :--- | :---: | :--- | :--- |
| **Finding 1 (Major): `_campNoted` reset on movement** | **RESOLVED** | `Server.Core/Runtime/SurvivorBrain.cs:248` | `_campNoted` is now reset in `Think()` via `if (acute) _campNoted = false;`. Because `acute` evaluates both `t - _threatRoseAt <= CampSeconds` and `t - _killerMovedAt <= CampSeconds` (`:235`), any acute event (threat rise, sighted shift > 0.5 m, or re-sighting after > 3 s) re-arms `_campNoted`. When the situation turns stale again, `else if (view.Threat >= ThreatFleeLevel && !_campNoted)` cleanly triggers terminal avoidance and unassigns the terminal. Verified by `ASecondCampPhaseLeavesTheTerminalAgain` (`SurvivorCampTests.cs:181-209`). |
| **Finding 2 (Major): Fallback pass killer collision** | **RESOLVED** | `Server.Core/Runtime/SurvivorBrain.cs:43, 487, 502` | `KillerAtTerminalRadius` (`CaptureRadius + HackStopDistance + 1f` = 3.6 m) blocks terminals within lethal proximity on fallback pass 1 (`:502`) whenever `killerKnown` is true (`:487`). The bot no longer selects a terminal directly occupied or camped by the killer at point-blank range. Verified by `AKillerRememberedAtTheTerminalIsNeverWalkedInto` (`SurvivorCampTests.cs:217-228`). |

---

## Evaluation of Attempt 2 Implementation

### 1. Removal of `_campNoted` reset from `Remember`
- **Assessment: SOUND**
- `_threatRoseAt` is updated in `Remember` (`:136`). In `Think`, `acute` checks `t - _threatRoseAt <= CampSeconds` (`:235`).
- Moving `if (acute) _campNoted = false;` to `Think` (`:248`) unifies all acute triggers (both threat level elevation and sighted killer repositioning) under a single state transition point.
- Because `_thinkAt` is scheduled at intervals of 0.175 s–0.84 s (well within `CampSeconds = 20 s`), `acute` is guaranteed to be observed while true following any threat rise, cleanly resetting `_campNoted = false`.

### 2. `KillerAtTerminalRadius` when killer was never seen
- **Assessment: SOUND / CHEAT-FREE**
- Guarded by `bool killerKnown = !double.IsNegativeInfinity(_killerSeenAt);` at `:487`.
- `_killerSeenAt` is initialized to `double.NegativeInfinity` (`:75`) and updated exclusively when `view.KillerVisible` is true (`:154, 161`).
- When the killer has never been seen, `killerKnown` is `false`, preventing false-positive blocking of terminals near the default uninitialized coordinates `(0, 0)`.
- Respects Category (a) Information: the bot never uses un-sighted or wallhacked killer coordinates.

### 3. Terminal blocking and freeze risk
- **Assessment: SOUND**
- On fallback pass 1, `:502` blocks a terminal only if `killerKnown` is true and distance is within 3.6 m.
- On normal pass 0, terminals are guarded by `danger && Distance < TerminalDangerRadius` (`:500`). Once the killer has been out of sight for `TerminalDangerMemorySeconds` (12 s), `danger` evaluates to `false`.
- If a killer was seen near a terminal and moved away unseen, pass 0 re-opens after 12 s (and after `_avoidUntil` expires at 20 s), allowing the bot to investigate and resume work.
- If all incomplete terminals are camped in plain sight at point-blank range (<= 3.6 m), the bot returns `_terminal = -1` and safely alternates between waiting and repositioning to hideouts (`:300-314`, `PickHideout`) rather than walking into capture radius or freezing in place.

### 4. Determinism
- **Assessment: PASS**
- All timing comparisons use `view.Time` (`t`). No wall-clock APIs (`DateTime`, `Stopwatch`) are used.
- Random score perturbation on candidate terminals (`:505`) and hideout selection use the seeded `System.Random` passed into `Update`.
- Execution is strictly deterministic across runs.

---

## Test Verification

1. **`ASecondCampPhaseLeavesTheTerminalAgain` (`SurvivorCampTests.cs:181-209`)**:
   - Tests that after working a camped terminal through phase 1, a second camp phase (triggered by killer movement at constant ThreatLevel 2) causes the bot to abandon the terminal once stale and redirect to newly workable T1.
   - **Attempt-1 failure**: On attempt 1, `_campNoted` was not reset by killer movement, causing the bot to remain at T4 indefinitely across the 90 s window (`atOther` remained false).
   - **Attempt-2 status**: Passes across all seeds (1, 2, 3). Tests the exact mechanism of Finding 1.

2. **`AKillerRememberedAtTheTerminalIsNeverWalkedInto` (`SurvivorCampTests.cs:217-228`)**:
   - Tests that an idle killer stationed 1 m in front of the sole remaining incomplete terminal in sight does not get walked into on fallback pass 1.
   - **Attempt-1 failure**: On attempt 1, fallback pass 1 picked T4 after 20 s, and the bot walked to distance 0.4 m and hacked T4 at t ≈ 55–59 s, failing both assertions.
   - **Attempt-2 status**: Passes across all seeds (1, 2, 3), maintaining distance > 2.2 m and 0 hacking frames over 120 s. Tests the exact mechanism of Finding 2.

---

## Tracked Known Risks (Unit U6d)
Per orchestrator ruling, the following findings from Attempt 1 remain accepted as tracked risks for Unit U6d and are not blockers/majors for this unit:
- **Finding 3 (Creeping unseen inside ThreatLevel 3)**: Unchanged in Attempt 2; behavior is preserved under U6d scope.
- **Finding 4 (KS median-speed margin)**: KS median-speed p = 0.0166 meets the gating threshold (>= 0.01); tracked for pacing polish under U6d.

---

## New Findings
None. Attempt 2 introduces no new blockers or major issues.

VERDICT: PASS
> Orchestrator note (Opus): the "pass threshold >= 0.75" for U2c above is wrong; the gate is gated AUC < 0.86 (0.855 measured). Verdict unaffected.
