# QA Review: U6c-hatch (Attempt 1)
**Reviewer:** Gemini Flash (Independent QA)  
**Implementer:** Claude Sonnet 5.5 (quota fallback)  
**Date:** 2026-11-04  
**Target Unit:** `U6c-hatch` (escape hatch world visual)

---

## 1. Specification Compliance (H1–H4)

| Spec ID | Requirement Summary | Status | Evidence (`file:line`) |
| :--- | :--- | :--- | :--- |
| **H1** | Procedural low-poly floor hatch (~1.2 m), frame + dark opening/grate, toon materials, no glow, readable from 15 m in 3rd/1st person, no downloaded assets | **PASS** | [HatchView.cs:13-21, 71-106](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L13-L21); 12 screenshots in `docs/qa/U6c-hatch-review/` |
| **H2** | Subtle, non-glowing cue (slow pulse <= 1 Hz), no bloom/glow (SLOP compliant), respect `GameSettings.ReducedMotion` | **PASS** | [HatchView.cs:14, 18-19, 55, 63-69](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L14-L69); [HatchViewTests.cs:117-132](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/HatchViewTests.cs#L117-L132) |
| **H3** | Lifecycle: lazy creation, show/move on `1`, hide on `0`, teardown with world, 0 alloc per snapshot, snap position without lerp | **PASS** | [WorldView.cs:37, 47, 56](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/WorldView.cs#L37-L56); [HatchView.cs:43-61, 121-125](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L43-L125) |
| **H4** | Public API: `bool HatchShown`, `Vector3 HatchPosition` (read-only) on `WorldView`, no `ClientApp` changes | **PASS** | [WorldView.cs:10-12](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/WorldView.cs#L10-L12); git diff clean outside `WorldView.cs` |

---

## 2. Correctness & Code Quality Analysis

- **Lifecycle (Hide / Re-show / Move):**  
  `HatchView.Set()` cleanly branches on `visible`. On hide (`visible == false`), it sets `shown = false` and deactivates the GameObject hierarchy with `root.SetActive(false)`. On re-show, it activates the root and sets `lastX = lastZ = float.NaN`, ensuring that the subsequent coordinate comparison immediately assigns `root.transform.position = new Vector3(x, 0, z)`. Moving while shown updates coordinates instantly with no interpolation.
- **Position Cache:**  
  When `shown == true` and `x == lastX && z == lastZ`, `Set()` returns immediately without writing to `transform.position`. When re-shown after being hidden, resetting `lastX` and `lastZ` to `NaN` ensures the position cache is invalidated and reapplied even if the hatch re-appears at the identical coordinate.
- **Teardown & Material Ownership:**  
  `WorldView.OnDestroy()` calls `hatch?.Dispose()`. `HatchView.Dispose()` destroys `root` (destroying all child primitives). All materials created through `WorldMaterials.Primitive` and `WorldMaterials.MakeMaterial` are tracked in `WorldMaterials.materials` and destroyed when `materials?.Dispose()` executes in `WorldView.OnDestroy()`. There are no lingering scene objects or leaked unmanaged material instances upon match end.
- **Per-Snapshot Allocations:**  
  In steady-state (both while shown and while hidden), `Set()` executes with zero heap allocations. `Vector3` creation is a value-type stack operation. `Render()` updates `Material.color`, which alters the material's properties without heap allocation.
- **`GameSettings` Load Frequency:**  
  Line 55 evaluates `reduced = ReducedMotionOverride ?? GameSettings.Load(new PlayerPrefsStore()).ReducedMotion;` only inside `if (!shown)`. During steady-state snapshots, `GameSettings.Load` is never called. However, it is invoked whenever the hatch transitions from hidden to shown (see Finding 2).
- **Null Safety:**  
  Before the first snapshot arrives, `HatchShown` evaluates to `false`, `HatchPosition` evaluates to `Vector3.zero`, `Render()` early-exits via `if (!shown || rim == null)`, and `Dispose()` is safe against null `root`.
- **Colliders & Physics Side Effects:**  
  All primitives are created via `WorldMaterials.Primitive()`, which immediately strips their `Collider` components (`Object.Destroy(item.GetComponent<Collider>())`). The hatch carries no physics colliders or rigidbodies, preventing interference with character movement or camera collision probes.

---

## 3. Test Quality Evaluation

The test suite in [HatchViewTests.cs](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/HatchViewTests.cs) adds 9 test cases covering all required behaviors:
- `NoHatchBeforeTheFirstSnapshotAndNoObjectForIt`: Rigorously verifies lazy instantiation and object counts for both roles.
- `ShowsOnVisibleAtTheServerPosition`: Validates position snapping and presence of active renderers for both roles.
- `HidesOnTheFirstSnapshotWithoutIt`: Confirms GameObject deactivation and `HatchShown == false`.
- `ReshowsAtANewPositionAndSnapsToIt`: Verifies position cache invalidation and instantaneous position updates.
- `HundredRepeatedSnapshotsCreateNothingNew`: Verifies that repeated snapshots and visibility toggles produce zero new GameObjects.
- `TheRimPulsesAtMostOnceASecondAndStaysStaticUnderReducedMotion`: Tests the <= 1 Hz pulse constraint, smooth multi-frame color easing, and the static color response under reduced motion.
- `NothingOnTheHatchIsEmissiveAndTheOpeningIsTheDarkestPart`: Checks emission keywords across all child renderers. (Note: weak assertion on Opening color noted in Finding 3).

---

## 4. Visual Readability & Style (Screenshot Inspection)

All 12 screenshots in `docs/qa/U6c-hatch-review/` were inspected across 1280x720 and 1920x1080 resolutions:
- **Recognisability:** The raised steel coaming (0.3 m high, 1.2 m square), dark recessed pit, three cross bars, and propped-open hinged lid clearly communicate an escape/utility hatch.
- **Readability at Night:**
  - *Survivor 6 m (`hatch1` & `hatch3`):* Crisp rim contrast against asphalt. The propped lid provides a distinct silhouette.
  - *Survivor 14 m (`hatch1` & `hatch3`):* The pale rim (`RimDim` to `RimBright`) and upright lid remain clearly discernible near maximum sight range (15 m) without looking faint or indistinct.
  - *Killer 10 m (`hatch1` & `hatch3`):* Killer first-person eye height foreshortens the flat base, but the upright lid and rim provide immediate visual cues.
- **SLOP & Style Compliance:**  
  No emission, no bloom, no particle flare, and no neon shader effects. The materials use `PingTheKiller/Toon` flat tone bands matching the game's night lighting palette.
- **Silhouette & Lid Proportions:**  
  The 0.95 m lid leaned back at 80 degrees fits the 1.2 m frame naturally. (Note on fixed yaw described in Finding 5).

---

## 5. Findings

### Finding 1: Redundant `Material` instances created and discarded during `HatchView.Build()`
- **Severity:** Minor
- **Evidence:** [HatchView.cs:85-88, 100-101, 114-119](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L85-L119)
- **Expected:** Primitives sharing an existing material (`rimMaterial`) should not create temporary dummy materials.
- **Actual:** `Rim N`, `Rim S`, `Rim E`, `Rim W`, and `Lid edge` call `materials.Primitive()`, which executes `MakeMaterial(Color.white)` and registers the resulting material in `WorldMaterials.materials`. Immediately after, `Part` and the lid setup overwrite `sharedMaterial = rimMaterial`. As a result, 5 unrendered material instances are created and held in memory until `WorldMaterials.Dispose()` at match teardown.

### Finding 2: `GameSettings.Load` with `new PlayerPrefsStore()` allocated on hide-to-show transitions
- **Severity:** Minor
- **Evidence:** [HatchView.cs:55](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L55)
- **Expected:** Player preferences should be queried once upon initialization or cached, rather than re-parsed from storage during gameplay.
- **Actual:** Every time `HatchVisible` transitions from 0 to 1 (e.g., player weaving across the 15 m sight boundary or around wall corners), `new PlayerPrefsStore()` is instantiated on the heap and `GameSettings.Load()` reads PlayerPrefs.

### Finding 3: Weak assertion in `NothingOnTheHatchIsEmissiveAndTheOpeningIsTheDarkestPart` test
- **Severity:** Minor
- **Evidence:** [HatchViewTests.cs:144](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Tests/EditMode/HatchViewTests.cs#L144)
- **Expected:** The test should inspect the actual instantiated opening renderer's material properties or color on the GameObject.
- **Actual:** `Assert.That(HatchView.Opening.maxColorComponent, Is.LessThan(HatchView.Steel.maxColorComponent))` compares static C# constants rather than querying the scene's `Renderer` materials.

### Finding 4: `WorldView.HatchPosition` returns stale coordinates when `HatchShown == false`
- **Severity:** Minor
- **Evidence:** [WorldView.cs:11](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/WorldView.cs#L11), [HatchView.cs:35](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L35)
- **Expected:** `HatchPosition` should ideally return `Vector3.zero` when the hatch is inactive (`!HatchShown`), consistent with its initial pre-snapshot state.
- **Actual:** `hatch.Position` returns `root.transform.position` whenever `root != null`. After being shown once and then hidden, `HatchPosition` retains the previous coordinates while `HatchShown` is `false`.

### Finding 5: World vs. local positioning assumption in `HatchView.Part()`
- **Severity:** Minor
- **Evidence:** [HatchView.cs:110-111, 117-118](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/Client/Assets/Scripts/World/HatchView.cs#L110-L118)
- **Expected:** Child parts should be parented with `worldPositionStays = false` and have their `localPosition` set explicitly, as is done for `lid` and `lidEdge`.
- **Actual:** `materials.Primitive` assigns `item.transform.position = position` (world position), and `Part` calls `item.transform.SetParent(root.transform, true)`. This implicitly relies on `WorldView`'s transform sitting at `(0, 0, 0)`.

---

VERDICT: PASS
