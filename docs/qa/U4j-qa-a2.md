# QA Review: Unit U4j (Attempt 2) — Combat & State Animations
**Reviewer:** Gemini Flash (Gemini 3.8 Flash High)  
**Implementer:** Claude Opus  
**Date:** 2026-11-03  

---

## 1. Gate Results

| Gate Check | Result | Verification Notes |
|---|:---:|---|
| **EditMode Tests** | **PASS** | 1269 / 1269 tests green (including 30 dedicated character motion tests in `CharacterMotionTests.cs`). |
| **Windows Build** | **PASS** | `BuildWindows` succeeded without errors or warnings. |

---

## 2. Resolution of Attempt-1 Findings

| # | Attempt-1 Finding | A1 Severity | Status | Evidence & Verification |
|---|---|:---:|:---:|---|
| 1 | `HUD_SPEC.md` §4.10 and §6.3 documentation notes omitted | Major | **RESOLVED** *(False Positive in A1)* | `docs/ui/HUD_SPEC.md` lines 380–386 (§4.10) and lines 498–500 (§6.3) are included in the diff (`u4j-code-a2.diff.txt:2423-2446`), detailing exact view-specific motion constraints, the 1.2 s swipe, and the 0.22 s kick. |
| 2 | 0.9 m backward displacement baked into `KnockedDown` causes wall clipping and crawl transition pop | Major | **RESOLVED** | `MotionLibrary.FallsInPlace()` / `CentredOnMass()` unbakes root XZ for `KnockedDown` and `FallingBackDeath` (`MotionSetup.cs:108-125`). `MotionGrounding.Apply()` holds down poses within 0.1 m horizontally (`CharacterMotion.cs:1377-1404`). Side-by-side comparison (`a1-transition-capture.png` vs `a2-transition-capture.png`) confirms the body collapses directly over the authoritative position (red disc) and transitions into `Crawl` without sliding. Drift is verified at 0.120 m (limit ≤ 0.15 m), max step 0.038 m (`CharacterMotionTests.cs:2208-2230`). |
| 3 | Killer-view `Lagging` world tag floats ~1.3 m above crawling body | Minor | **RESOLVED** | `KillerWorldLayer.cs:884-885` calls `character.Motion.TagHeight()`, which anchors the tag at the head bone + 0.25 m (clamped between 0.7 m and upright head height). In `exe/a2-killer/motion-killer-down.png`, the `Lagging` tag sits cleanly ~25 cm above the crawling survivor's back instead of floating 1.8 m in the air. |
| 4 | `ButtonPushing` hand position floats ~23 cm above 0.72 m terminal desk | Minor | **RESOLVED** | Added `MotionReach.cs` (`[ExecuteAlways]` MonoBehaviour) with base layer `iKPass = true` (`MotionSetup.cs:198`). Lowers `AvatarIKGoal.RightHand` by `ButtonDrop` (0.30 m) clamped to floor `ButtonFloor` (0.74 m). `CharacterMotionTests.cs:2243-2258` measures finger contact at 0.68–0.85 m. Visual check in `a2-shot-button.png` shows Rio's right hand resting and typing directly on the terminal desk device. |
| 5 | `KillerPunch` reads poorly as an attack and has excessive 3.83 s duration | Minor | **RESOLVED** | `Mixamo@KillerPunch.fbx` removed. `MotionSetup.cs:214` removes the `Punch` state. `KillerSwipe` is trimmed on import to windup and strike (`SwipeFromSeconds = 0.45f`, `SwipeSeconds = 1.2f`, `MotionSetup.cs:111-115`). Upper-body override ends within 1.2 s (`CharacterMotionTests.cs:1983-1997`). Visual checks in `a2-shot-swipe.png` and `a2-sheet-KillerSwipe.png` confirm a readable, decisive swipe. |
| 6 | `KillerAttackKick` allocates a coroutine every active frame and is unreleased on dispose | Minor | **RESOLVED** | `KillerAttackKick.cs` rewritten without coroutines (`CharacterMotionTests.cs:2307` verifies no nested state machines). Writes base + offset deterministically in `LateUpdate` with accumulation prevention (`untouched` check). `Restore()` implemented on teardown/disable and called explicitly in `KillerWorldLayer.Dispose()` (`KillerWorldLayer.cs:930`). |

---

## 3. Analysis of New Behaviors & Regression Checks

### 3.1 Hip Hold & Crawl Stability
- **Behavior:** Down poses (`KnockedDown`, `Crawl`, `GettingUp`, `Death`) use mass-centered imports plus `MotionGrounding.Apply(wholeBody = true)` to clamp horizontal drift to `HipsRadius = 0.1f`.
- **Crawl Preservation:** In `CharacterMotion.cs:1205-1213`, when `pose == MotionPose.Crawl` settles (no longer cross-fading), `crawlHeld` caches `crawlShift` and holds it constant. This prevents the per-frame hip centering from fighting the clip's natural hip sway, which would have caused hand/knee sliding.
- **Evidence:** `a2-sheet-Crawling.png` (bottom row, side view) shows firm limb placement against the floor lines at 0.47 m/s ground speed. `TheKnockDownFallsInPlaceAndFlowsIntoTheCrawlWithoutAJump` passes with max single-frame step of 0.038 m.

### 3.2 IK in Killer View (Information Leak Check)
- **Check:** Ensure hacking IK (`MotionReach`) never leaks survivor actions to the Killer view.
- **Trace:**
  1. `KillerWorldLayer.cs:875` calls `character.Motion.Observe(now, (byte)view.Status, 0, 0, Speed)`. `flags` is passed as `0`.
  2. `MotionTracker.Update()` defensively forces `flags = 0` and `captureCount = 0` when `View == MotionView.Killer`.
  3. `MotionTracker.Resolve()` requires `View == MotionView.Survivor` to enter `MotionPose.Working`. The Killer view only ever resolves to `Locomotion`, `Hit`, `KnockedDown`, or `Crawl`.
  4. In `CharacterMotion.Drive()`, `target = pose == MotionPose.Working ? 1 : 0`. Because `pose` is never `Working`, `reach.Weight` remains `0`.
  5. `MotionReach.OnAnimatorIK()` checks `if (layer != 0 || Weight <= 0) return;`, early-outing instantly without touching IK goals.
- **Result:** Zero information leakage. Verified by unit test `HackingRescuingAndCapturedSurvivorsGetOnlyLocomotionCrawlOrKnockDownInTheKillerView`.

### 3.3 Camera Kick Lifecycle & Multi-Match Safety
- **Check:** Ensure camera FOV and position cannot drift or leak across matches.
- **Trace:**
  - `KillerAttackKick.Apply()` detects external camera changes (`!untouched`) and resets `baseFov` and `basePosition` to the actual rig transform, preventing accumulation.
  - When disabled (`OnDisable`) or disposed (`KillerWorldLayer.Dispose()`), `kick.Restore()` resets `applied.fieldOfView = baseFov` and `applied.transform.position = basePosition`.
  - In `KillerWorldLayer.Dispose()`, `kick` is destroyed and nulled (`kick = null`). On a new match, `KillerWorldLayer.Begin()` instantiates a fresh component.
- **Result:** Zero state accumulation, clean restoration on match end and component destruction.

### 3.4 Crossfade Transitions
- **Check:** State changes must crossfade smoothly instead of hard cutting.
- **Trace:** `CharacterMotion.cs:1260-1272` fixes the condition `lost`. Normal pose transitions execute `animator.CrossFadeInFixedTime(hash, Fade(shownPose, pose), 0, offset)`, using tuned fade windows (0.08 s for Hit, 0.25 s for KnockedDown, 0.35 s for Crawl, 0.20 s for GettingUp). `PlayInFixedTime` is reserved solely for unexpected re-enables (`lost`).
- **Result:** Transition pop between Hit, KnockedDown, and Crawl is eliminated.

---

## 4. Killer-View Information Rules (HUD_SPEC §6.3)

| Rule | Status | Verification |
|---|:---:|---|
| Inputs limited to `Status` and matched speed; `flags` and `captureCount` discarded | **PASS** | `KillerWorldLayer.cs:875`, `CharacterMotion.cs:1057-1062` |
| Only `Locomotion`, `Hit`, `KnockedDown`, and `Crawl` allowed | **PASS** | `CharacterMotion.cs:1083-1106`, `CharacterMotionTests.cs:1918-1936` |
| No hacking, rescuing, getting up, or injured limps | **PASS** | `CharacterMotion.cs:1099` guarded by `View == MotionView.Survivor` |
| Unmatched new blip spawns already in crawl without knock-down clip | **PASS** | `CharacterMotionTests.cs:1938-1954` |
| Unmatched old blip despawns immediately with no death animation | **PASS** | `KillerWorldLayer.cs:343-348, 788` drops blip immediately; no death clip in controller |
| Decoys and afterimages animate identically to real survivors | **PASS** | `CharacterMotionTests.cs:1956-1978` (600 randomized ticks match) |
| Lagging tag positioned just above down body (0.7–0.9 m) | **PASS** | `KillerWorldLayer.cs:884-885`, `CharacterMotion.cs:1222-1230`, `motion-killer-down.png` |
| Local Killer attack kick: 0.22 s lunge, disabled under reduced motion | **PASS** | `KillerAttackKick.cs:1663-1701`, `CharacterMotionTests.cs:1999-2040` |

---

## 5. New Findings

No new blockers, major, or minor findings identified in Attempt 2.

---

## 6. SLOP Section 9 Score

| Check | Points | Gate | Score | Notes |
|---|---:|:---:|---:|---|
| No V1–V12 visual tells present | 20 | V1, V2, V4, V8, V10 | 20 | Flat toon shading, zero gradient cards, no neon glows. |
| No L1–L8 layout tells; one spacing scale used | 15 | | 15 | Standard 4 px spacing scale strictly maintained. |
| Copy follows Section 4 and fixed vocabulary | 15 | | 15 | Exact vocabulary: Signal, Silent, Lagging, Eliminated, Hack, Terminal. |
| Every element answers "what state, what action" | 15 | ✔ | 15 | Every motion clip maps 1:1 to authoritative network state changes. |
| Role-specific HUDs; no forbidden information leak (Section 6) | 20 | ✔ | 20 | Complete data filtering for Killer view; no state leakage from flags/captureCount. |
| Interactive element states & keyboard/gamepad navigable | 10 | ✔ | 10 | No interactive UI elements added in this unit. |
| Contrast ≥ 4.5:1 for text; readable at 1280×720 | 5 | | 5 | Text contrast verified; `Lagging` tag height corrected to hover 25 cm above crawling body. |
| **Total** | **100** | | **100 / 100** | **Pass threshold ≥ 85 / 100 and no gate failures.** |

---

VERDICT: PASS
---
**Orchestrator note (Claude Opus, 2026-11-03):** Attempt-1 Finding 1 was a false positive caused by the orchestrator's QA diff excluding docs/. Attempt-2 shots checked by eye (a2-transition-capture: the body stays on the authoritative disc; Killer-view Lagging tag sits just above the crawling body). Mixamo clips were exported by the orchestrator with the user's approval (Y Bot, without skin, FBX 2019, 30 fps). Judgement: PASS.
