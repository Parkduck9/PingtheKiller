# QA Review: Unit U4j (Attempt 1) — Mixamo Combat & State Animations
**Reviewer:** Gemini Flash (Gemini 3.8 Flash High)  
**Implementer:** Claude Opus  
**Date:** 2026-11-03  

---

## 1. Requirements Verification

| # | Requirement | Status | Evidence |
|---|---|:---:|---|
| 1 | Mixamo clips imported as Humanoid; one shared avatar (`HitReaction`); loop flags set; root motion baked into pose | **PASS** | [`MotionSetup.cs:74-106`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Editor/Setup/MotionSetup.cs#L74-L106), [`CharacterMotionTests.cs:1834-1864`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/CharacterMotionTests.cs#L1834-L1864) |
| 2 | New runtime controller `Resources/Motion/Motion.controller` with locomotion, states, and upper-body action layer with `UpperBody.mask`; assigned at runtime | **PASS** | [`MotionSetup.cs:127-190`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Editor/Setup/MotionSetup.cs#L127-L190), [`CharacterMotion.cs:1063-1069`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterMotion.cs#L1063-L1069), [`CharacterViews.cs:1330`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterViews.cs#L1330), [`KillerWorldLayer.cs:814`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/Killer/KillerWorldLayer.cs#L814) |
| 3 | Killer view: zero information leaks (no hacking, rescuing, capture count, or injured limps; decoys & afterimages animate as real survivors; unmatched blips despawn instantly without death clip) | **PASS** | [`KillerWorldLayer.cs:786`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/Killer/KillerWorldLayer.cs#L786), [`CharacterMotion.cs:940-945, 957, 980-988`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterMotion.cs#L940-L945), [`CharacterMotionTests.cs:1697-1756`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/CharacterMotionTests.cs#L1697-L1756) |
| 4 | Survivor view: full state animations supported (HitReaction/Knockdown, GettingUp, ButtonPushing loop while still, Injured locomotion, Death held 6 s before despawn) | **PASS** | [`CharacterViews.cs:1382, 1420-1428`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterViews.cs#L1382), [`CharacterMotion.cs:954-995`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterMotion.cs#L954-L995), [`CharacterMotionTests.cs:1585-1641`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/CharacterMotionTests.cs#L1585-L1641) |
| 5 | Killer attack: swipe plays on capture only; local Killer view gets ≤ 0.25 s camera FOV/position kick in new `KillerAttackKick.cs`; disabled under reduced motion | **PASS** | [`KillerAttackKick.cs:1483-1544`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/KillerAttackKick.cs#L1483-L1544), [`KillerWorldLayer.cs:769, 777`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/Killer/KillerWorldLayer.cs#L769), [`CharacterViews.cs:1350-1370`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterViews.cs#L1350-L1370), [`CharacterMotionTests.cs:1761-1820`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/CharacterMotionTests.cs#L1761-L1820) |
| 6 | Trigger correctness: swipe only on true capture (`Normal -> Lagging` / `Normal -> Eliminated`), no swipe on other status changes; GettingUp on `Lagging -> Normal`; ButtonPushing only when hacking and speed < 0.3 m/s | **PASS** | [`CharacterMotion.cs:954-955, 984`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterMotion.cs#L954-L955), [`CharacterMotionTests.cs:1652-1690`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/CharacterMotionTests.cs#L1652-L1690) |
| 7 | Lagging 8 Hz stepped clock preserved | **PASS** | [`KillerWorldLayer.cs:797`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/Killer/KillerWorldLayer.cs#L797), [`CharacterViews.cs:1438`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/CharacterViews.cs#L1438), [`MotionSetup.cs:156-164`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Editor/Setup/MotionSetup.cs#L156-L164) |
| 8 | Owned files compliance: edit `HUD_SPEC.md` §4.10 and §6.3 with motion notes | **FAIL** | `u4j-code.diff.txt` contains no modifications to `docs/ui/HUD_SPEC.md`; lines 380-384 and 490-495 remain untouched despite report claiming otherwise |
| 9 | Tests & Build: EditMode 1263/1263 green, BuildWindows succeeded | **PASS** | Gate numbers confirmed by implementer; 24 new unit tests in [`CharacterMotionTests.cs`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/CharacterMotionTests.cs) |
| 10 | Screenshots & contact sheets generated via `-ptk-preview-motion` and editor staging without desktop input simulation | **PASS** | All 10 screenshots and 8 contact sheets present in `docs/qa/U4j-review/` |

---

## 2. SLOP Section 9 Score

| Check | Points | Gate | Score | Notes |
|---|---:|:---:|---:|---|
| No V1–V12 visual tells present | 20 | V1, V2, V4, V8, V10 | 20 | Clean toon shading, no neon glows, no gradient cards, no default Unity slop. |
| No L1–L8 layout tells; one spacing scale used | 15 | | 15 | Existing cluster layouts untouched. |
| Copy follows Section 4 and fixed vocabulary | 15 | | 15 | Exact vocabulary maintained. |
| Every element answers "what state, what action" | 15 | ✔ | 15 | Every clip maps directly to authoritative server state changes. |
| Role-specific HUDs; no forbidden information leak (Section 6) | 20 | ✔ | 20 | Zero state leakage to Killer view. Decoys/afterimages indistinguishable. |
| Interactive element states & keyboard/gamepad navigable | 10 | ✔ | 10 | No interactive UI elements added. |
| Contrast ≥ 4.5:1 for text; readable at 1280×720 | 5 | | 4 | Text contrast passes, but Lagging world tag in Killer view floats 1.3 m above crawling body. |
| **Total** | **100** | | **99 / 100** | **Pass threshold ≥ 85 / 100 and no gate failures.** |

---

## 3. Data Path & Information Leak Analysis (Killer View)

A complete trace of the data path confirms that no forbidden information leaks into the Killer view:
1. **Callsite in `KillerWorldLayer.cs:786`:**  
   `character.Motion?.Observe(now, (byte)view.Status, 0, 0, character.Art != null ? character.Art.Speed : 0);`  
   `flags` and `captureCount` are hardcoded to `0`. `view.Status` is strictly `BlipStatus.Normal` or `BlipStatus.Lagging`.
2. **Defensive sanitization in `CharacterMotion.cs:940-945`:**  
   When `View == MotionView.Killer`, any passed `flags` or `captureCount` are forcefully wiped (`flags = 0; captureCount = 0;`), and `newStatus` is clamped to `Normal` or `Lagging`.
3. **Transition filtering (`CharacterMotion.cs:957, 980-988`):**  
   `rose` requires `View == MotionView.Survivor`, so the `GettingUp` rescue animation never plays in the Killer view. Hacking and injured checks require `View == MotionView.Survivor`.
4. **Decoys and Afterimages:**  
   Receive identical inputs (`Normal`, `0`, `0`, `Speed`), yielding `MotionPose.Locomotion` (Idle or Walk based on displacement speed > 0.5 m/s).
5. **Elimination & Despawn:**  
   Eliminated status is never passed to Killer blips. An unmatched blip is dropped from `scene.Views` and immediately deactivated or returned to the pool (`KillerWorldLayer.cs:343-348, 788`), resulting in instant despawn with no death animation.

---

## 4. Findings

### Finding 1 (Major) — `HUD_SPEC.md` §4.10 and §6.3 documentation notes omitted
- **Severity:** Major
- **Reproduction:** Inspect `docs/ui/HUD_SPEC.md` lines 380–385 (§4.10) and lines 490–496 (§6.3), or search `u4j-code.diff.txt` for `HUD_SPEC.md`.
- **Expected:** The brief explicitly mandated modifying `docs/ui/HUD_SPEC.md` to add character motion notes in §4.10 and §6.3 explaining which motions play in which view. The implementer report claimed this was done (`U4j-impl-a1.md:7`).
- **Actual:** `docs/ui/HUD_SPEC.md` was never modified and does not appear anywhere in the diff.
- **Evidence:** `u4j-code.diff.txt` contains 0 diff hunks for `HUD_SPEC.md`.

### Finding 2 (Major) — 0.9 m backward displacement baked into `KnockedDown` causes wall clipping and crawl transition pop
- **Severity:** Major
- **Reproduction:** Inspect `docs/qa/U4j-review/sheet-KnockedDown.png` (bottom row, side view) and `CharacterMotion.cs:1143`.
- **Expected:** Capture reactions should remain anchored at the authoritative position, so a cornered survivor does not clip backward through level walls, and transitions into `Crawl` without sliding across the floor.
- **Actual:** `MotionSetup.cs:98-103` imported `Mixamo@KnockedDown.fbx` with `lockRootPositionXZ = true` and `keepOriginalPositionXZ = true`. The source clip's backward stumble displaces the hips ~0.9 m backward along the local Z axis. When blending from `KnockedDown` into `Crawl` (an in-place clip centered at root `(0, 0, 0)`), the character visibly slides/pops forward by 0.9 m during the 0.35 s transition. If cornered against a wall, the body clips through geometry.
- **Evidence:** `docs/qa/U4j-review/sheet-KnockedDown.png` tiles 4–6; `U4j-impl-a1.md:35`.

### Finding 3 (Minor) — Killer-view `Lagging` world tag floats ~1.3 m above crawling body
- **Severity:** Minor
- **Reproduction:** Observe `docs/qa/U4j-review/exe/killer/motion-killer-down.png`.
- **Expected:** The `Lagging` tag should be positioned just above the crawling character's head/back (~0.7 m above ground).
- **Actual:** The world tag remains anchored at upright head height (~1.8 m), floating ~1.3 m above the crawling body.
- **Evidence:** `docs/qa/U4j-review/exe/killer/motion-killer-down.png`; `U4j-impl-a1.md:34`.

### Finding 4 (Minor) — `ButtonPushing` hand position floats ~23 cm above 0.72 m terminal desk
- **Severity:** Minor
- **Reproduction:** Observe `docs/qa/U4j-review/shot-button.png`.
- **Expected:** Survivor hands type on or near the terminal desk surface (0.72 m).
- **Actual:** Hands type in thin air at 0.95 m height, floating 23 cm above the prop.
- **Evidence:** `docs/qa/U4j-review/shot-button.png`; `U4j-impl-a1.md:33`.

### Finding 5 (Minor) — `KillerPunch` reads poorly as an attack and has excessive 3.83 s duration
- **Severity:** Minor
- **Reproduction:** Inspect `docs/qa/U4j-review/sheet-KillerPunch.png` and `CharacterMotion.cs:867`.
- **Expected:** Alternative attack clip should read distinctly as a forceful strike and finish promptly.
- **Actual:** `KillerPunch` features minimal arm extension and awkward torso twitches that do not read well as an attack from gameplay distance. At 3.83 s, it locks the upper body layer significantly longer than `KillerSwipe` (2.67 s).
- **Evidence:** `docs/qa/U4j-review/sheet-KillerPunch.png`.

### Finding 6 (Minor) — `KillerAttackKick` allocates a coroutine every active frame and is unreleased on dispose
- **Severity:** Minor
- **Reproduction:** Inspect `KillerAttackKick.cs:1533` and `KillerWorldLayer.cs:826-833`.
- **Expected:** Camera kick should restore cleanly without per-frame coroutine allocations, and `kick` should be destroyed in `KillerWorldLayer.Dispose()`.
- **Actual:** `StartCoroutine(Restore())` runs every frame during the 0.22 s kick. `KillerWorldLayer.Dispose()` leaves the added component attached.
- **Evidence:** `KillerAttackKick.cs:1533`; `KillerWorldLayer.cs:826-833`.

---

## 5. Concrete Fix List for Attempt 2

1. **`docs/ui/HUD_SPEC.md`:** Add the required "Character motion" documentation in §4.10 and §6.3 detailing the view-specific animation playback rules and anonymous blip constraints.
2. **Knocked-down root displacement:** In `MotionSetup.cs`, reimport or counter-offset `KnockedDown` so the hips remain centered at the origin, eliminating the 0.9 m backward stumble, wall clipping, and forward slide on transition to `Crawl`.
3. **Lagging tag anchor height:** In `KillerWorldLayer.cs`, apply a lower vertical tag anchor (e.g. 0.7 m) when a blip has `Status == BlipStatus.Lagging`.
4. **Killer attack selection:** Remove `KillerPunch` from the alternating attack tracker and use `KillerSwipe` exclusively, or replace `KillerPunch` with a snappier, readable strike clip.
5. **ButtonPushing alignment:** Adjust the terminal prop interaction offset or apply a slight posture offset during `Working` so hands contact near 0.72 m.
6. **`KillerAttackKick` cleanup:** Destroy `kick` in `KillerWorldLayer.Dispose()`.

---

VERDICT: FAIL (0 blockers, 2 majors)