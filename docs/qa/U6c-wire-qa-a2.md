# QA Review Report: Ping the Killer — Unit U6c-wire (Attempt 2)

**Reviewer:** Gemini 3.8 Flash (acting as Gemini Flash Independent QA)  
**Date:** 2026-11-04  
**Working Directory:** `C:/Unity/MultiPlayGame/PtK-reqa/client`  
**Target Unit:** U6c-wire (Attempt 2)  
**Prior Report:** `docs/qa/U6c-wire-qa-a1.md` (Attempt 1: FAIL)  
**Tests & Build:** EditMode 1347/1347 passed; BuildWindows succeeded.

---

## 1. Fix-List Items Resolution Table

| # | Item | Status | File : Line Evidence | Evaluation & Verification |
|---|---|---|---|---|
| **1** | **S4 Survivor Regressing tag** | **RESOLVED** | `SurvivorHud.cs:39-40,128-163`<br>`ClientApp.cs:428`<br>`RulesV2World.cs:56-63`<br>`HUD_SPEC.md:196`<br>`RulesV2WiringTests.cs:464-498` | **Full runtime wiring verified.** `SurvivorHud` instantiates and adds `V2MarkerLayer`. `ClientApp.cs:428` calls `screens.Survivor.DrawTerminalFlags(lastWorld, rig.Pose)` every frame from the active update loop. It calculates 3D projection at 1.5 m height, enforces strict viewport bounds (`0..1`) and `safe` area containment (on-screen only, zero edge indicators), and clamps positions outside `keepOut` (`SurvivorHud.ClusterRect(screen)` which accounts for the cluster, objective row, prompt/hold row, and sync ring). The tag displays `Terminal N · X% · Regressing` with a 2 u `danger` bar per §13.6. Tested via runtime HUD method and `ClientApp` source assertion. |
| **2** | **Capture banner seconds** | **RESOLVED** | `SurvivorFeedback.cs:68-72,112-116`<br>`SurvivorReadout.cs:149,168,205-209`<br>`HUD_SPEC.md:28,37`<br>`RulesV2WiringTests.cs:501-536` | **Correct for both stages and escalation.** `CaptureWatch.BannerFor(captureCount)` returns `Banner` (60 s) for first capture and `SecondBanner` (30 s) for second capture (`SurvivorReadout.SecondStage`). When the first stage times out while Lagging and the server increments `CaptureCount` to 2, `Observe` detects stage escalation, resets `bannerUntil = now + BannerSeconds`, and triggers `SecondBanner` once more with 30 s. Elimination reason `TimedOut` (`SurvivorFeedback.cs:76`) correctly displays 30 s (`GameConstants.SecondLaggingTimeoutSeconds`). `SurvivorReadout.Meter` scales to 30 s and shows `Captured twice`. Tested thoroughly for both captures and time-out escalation. |
| **3** | **Per-frame allocations** | **RESOLVED** | `RulesV2Wiring.cs:167-197,199-223,240-262`<br>`ClientApp.cs:37,63,246,256`<br>`KillerWorldLayer.cs:41,166,227`<br>`RulesV2World.cs:21,50-75`<br>`RulesV2Views.cs:1535-1548,1580-1598,1628-1646`<br>`RulesV2WiringTests.cs:539-579` | **Allocations and string churn eliminated.** `ExitCache` maintains a reused `ExitView[] views` buffer, refilling it only when a new `S2C_WorldState` arrives and mutating `LockedLeft` in place without heap allocations. `ClientApp` and `KillerWorldLayer` use their own `ExitCache` instances; no calls to `V2Wire.Exits` remain in per-frame rendering. `TextMemo` caches string formatting across `InteractCopy` (`SwitchOpen`, `SwitchLocked`, `Kick`), `V2MarkerLayer` (`Exit`, `Regressing`, `OverloadedTag`), `HoldActionBar`, `ExitCell`, and `CollapseTimer`. Verified by tests checking buffer and string reference equality (`Is.SameAs`). |
| **4** | **Sync check needle readability** | **RESOLVED** | `SyncCheckRing.cs:66,124-131,133-142`<br>`HUD_SPEC.md:159-160`<br>`RulesV2WiringTests.cs:582-590`<br>`a2-1-sync-check-720.png`<br>`a2-1-sync-check-1080.png`<br>`a2-2-sync-result-720.png`<br>`a2-2-sync-result-1080.png` | **Dial readability verified.** The needle is now a prominent 3 u hand from radius 30 (inside track) to radius 80 (8 u past the 72 u disc edge into the stack gap) with a 7 u `bg` keyline (total length 50 u, > 2x Great width 22 u). A 2 u `panel` cut separates the Great head from the Good arc, ensuring the zone reads as two distinct segments on the ring rather than a tapered arrowhead. SLOP rules preserved (no glows or signal amber). Confirmed in recaptured 720p and 1080p shots. |
| **5** | **Shot 5 at 1080p (Killer Overloaded alert)** | **RESOLVED** | `docs/qa/U6c-wire-review/a2/a2-5-killer-overloaded-1080.png` | **Present and verified.** 1920×1080 live capture provided showing `Terminal 6 overloaded · 41 m` with amber chevron/square indicator on the right screen edge. |
| **6** | **B1 proof framing** | **RESOLVED** | `docs/qa/U6c-wire-review/a2/a2-7-b1-hud-crop-before-after-1080.png`<br>`SurvivorHud.cs:226-236`<br>`U6c-wire-impl-a1.md:70` | **Verified via HUD comparison.** `a2-7-b1-hud-crop-before-after-1080.png` provides an exact pixel crop comparison of the threat strip HUD region at 1080p before and after the fix, proving bottom-stacked noise columns eliminate the clipped text illusion. Implementer documented the one-line reason for HUD crop vs 3D framing (stochastic offline spawns and bot Killer without a client seed flag). |

---

## 2. Regression & Robustness Audit

1. **Exit Cache Lifecycles across Matches:**
   - In `ClientApp.cs:203`, `exitCache.Reset()` is explicitly invoked upon `S2C_MatchStart`, clearing `source` and `views`.
   - `KillerWorldLayer` is created on a newly instantiated `Match world` GameObject each match and destroyed on match completion (`ClientApp.cs:198,209`), guaranteeing no cross-match cache retention.
   - `ExitCache.For(world)` uses `ReferenceEquals(world, source)`, meaning any new snapshot reference immediately invalidates stale references.

2. **TextMemo Cache Invalidation & Slot Boundaries:**
   - All `TextMemo` keys pack full value state (progress percentages, ceil integer seconds, meter distances, role viewer IDs, and phase flags). Any value update produces a distinct key.
   - Cache misses regenerate strings via `memo.Put(...)`. If a slot were to experience a hash collision, `memo.Hit` checks `keys[slot] == key`, safely treating mismatches as misses rather than serving stale strings.
   - All slot allocations (`V2MarkerLayer`: 34 slots; `InteractCopy`: 20 slots; `HoldActionBar`: 2 slots) strictly bound indices below array limits.

3. **Marker Visibility & Lifecycle Clearing:**
   - In `V2MarkerLayer.cs:33-39`, `Begin()` resets `used = 0`, and `End()` iterates from `used` to `tags.Count`, setting `display = DisplayStyle.None` on all pooled tags not used during the current frame.
   - Tested in `RulesV2WiringTests.cs:482-485`: clearing `TerminalState.Flags` immediately hides the tag and leaves `hud.V2Markers.Tags` empty.

4. **Handling of `null` `lastWorld` Before First Snapshot:**
   - `ClientApp.UpdateSurvivorHud` explicitly guards with `if (lastWorld == null) return;` (`ClientApp.cs:238`).
   - `SurvivorHud.DrawTerminalFlags` null-coalesces `world?.Terminals` (`SurvivorHud.cs:148`). Passing a null `world` safely executes `Begin()`/`End()` and clears tags without throwing (`RulesV2WiringTests.cs:486-487`).
   - `KillerWorldLayer.DrawStatic` guards with `if (last == null) return;` (`KillerWorldLayer.cs:199`), and `UpdateHud` guards interactive prompts with `last == null ? default : ...` (`KillerWorldLayer.cs:172`).

---

## 3. Test Quality Assessment

- **No Weakened Assertions:** Existing test refactors across `CopyTests.cs`, `KillerHudTests.cs`, `KillerHudQaTests.cs`, and `ActionInputQaTests.cs` maintain rigorous checks against updated v2 contracts rather than deleting assertions.
- **Genuine Execution Path Coverage:** `RulesV2WiringTests.cs:464-490` tests `SurvivorHud.DrawTerminalFlags` with real viewport transformations (`LookingNorthFrom(0, 0)`), asserting that only the in-frustum regressing terminal displays the tag, while behind, off-screen, and non-regressing terminals are excluded.
- **ClientApp Wiring Verification:** `RulesV2WiringTests.cs:492-498` parses `ClientApp.cs` source to verify that `screens.Survivor.DrawTerminalFlags(lastWorld, rig.Pose)` is continuously called from the main update loop.
- **Zero Heap Churn Verification:** `TheExitBufferIsBuiltOncePerWorldStateAndReused` and `PerFrameTextsAreTheSameStringWhileTheValueIsUnchanged` assert instance reference identity (`Is.SameAs`) for reused arrays and memoized strings across frames.

---

## 4. SLOP Definition §9 Evaluation

Scored strictly against `docs/ui/SLOP_DEFINITION.md` Section 9:

| Category | Max | Score | Deductions & Notes |
|---|---:|---:|---|
| **Visual tells (V1–V12)** | 20 | 20 | **No gates tripped.** No gradients, no glows, no emojis, no glassmorphism. Needle hand and 2 u cut between Great and Good segments give clean dial geometry. Signal amber `#FFB020` is used exclusively for active transmitting elements. |
| **Layout tells (L1–L8)** | 15 | 14 | 4 px spacing scale strictly observed. Survivor bottom-center cluster and stack maintain consistent 8 u gaps. -1 for pre-existing clamped edge marker text overlap on Killer screen edge (out of scope, noted in brief). |
| **Copy & Voice (Section 4)** | 15 | 15 | Fixed vocabulary strictly maintained (`Killer`, `Survivor`, `Signal`, `Silent`, `Lagging`, `Eliminated`, `Escaped`, `Rescue`, `Terminal`, `hack`, `Exit`, `Locked`, `Collapse`). Capture banner correctly shows 60 s for stage 1 and 30 s for stage 2 and timeout escalation. |
| **State & Action (what state, what action)** | 15 | 15 | **Gate pass.** Sync check ring cleanly differentiates hand sweep and target zone; interact prompts clearly communicate hold requirements (`Hold [E] to hack Terminal 3`, `Hold [E] to kick Terminal 3`); hold bars clearly communicate action progress. |
| **Role-specific HUD & Information Leaks (Section 6)** | 20 | 20 | **Gate pass.** No hidden information leaked to Killer. Killer collapse timer bar correctly follows §13.5 (`muted` → `text` urgency). S4 Survivor Regressing tag properly rendered via `SurvivorHud.V2Markers` with 2 u `danger` bar. |
| **Interactive states & Navigation** | 10 | 10 | **Gate pass.** Buttons retain normal/hover/pressed/disabled/focused states; Space and Gamepad A bound for sync check. |
| **Contrast & 720p Readability** | 5 | 5 | Text contrast complies with ≥ 4.5:1. Redesigned sync check ring hand and zone cut are crisply readable at 1280×720 (`a2-1-sync-check-720.png`). |
| **TOTAL** | **100** | **99** | **PASS (Threshold ≥ 85 / 100 and no gate failures)** |

---

## 5. New Findings

No blocker or major findings were introduced by attempt 2.

### Observation 1 (Minor / Pre-existing): Killer HUD Edge Label Collision
- **Severity:** Minor (Accepted as-is in fix list F10)
- **Evidence:** `docs/qa/U6c-wire-review/a2/a2-5-killer-overloaded-1080.png`
- **Description:** Multiple off-screen indicators clamped to the right screen edge display overlapping distance labels (`42 m`, `41 m`). This is pre-existing edge indicator behavior from earlier units and outside U6c-wire scope.

### Observation 2 (Minor / Pre-existing): Preview Mode Duplicate Terminal Tag
- **Severity:** Minor (Accepted as-is in fix list F8)
- **Evidence:** `docs/qa/U6c-wire-review/4b-killer-kick-preview-720.png`
- **Description:** Synthetic dev preview mode renders stacked terminal labels simultaneously. Confirmed absent in live match rendering.

---

VERDICT: PASS