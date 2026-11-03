# U4i Visual QA Review (Attempt 1) — Gemini 3.8 Flash (High)

- **Reviewer:** Gemini 3.8 Flash (High), independent visual QA.
- **Implementer:** Claude Opus subagent (quota fallback).
- **Target Unit:** U4i — Toon Shading (no outlines, darker night mood).
- **Date:** 2026-11-03.
- **Working Directory:** `C:/Unity/MultiPlayGame/PtK-reqa/client`.

---

## 1. Executive Summary

Unit U4i migrated character and world materials from URP Lit to a custom toon shader (`PingTheKiller/Toon`, adapted from `Toon Shaders Pro/URP/Toon` to fix a vendor albedo-doubling bug). 

The toon shading bands on character models are distinct, clean, and immediately recognizable, and the Killer view's 1 u amber `signal` outline contract is properly maintained without introducing forbidden black inverted-hull geometry outlines or specular glow. 

However, **the update fails the primary aesthetic goal and explicit user feedback**: the city still reads as a bright, cheerful daytime resort rather than a tense, dark stealth night chase due to unadjusted pastel pink, lavender, peach, and baby blue building facades. Furthermore, cast shadows exhibit severe saw-tooth stairstepping artifacts under building awnings, and anomalous ground shadow patches appear in both editor and exe captures.

---

## 2. Brief Requirements Verification Table

| Requirement | Status | Screenshot Evidence | Notes |
|---|:---:|---|---|
| **Toon Shading Bands** (2 world, 2–3 characters) | **PASS** | `exe-after-survivor-3p-720.png`, `after-walls-lit-survivor-720.png` | Stepped bands on survivor vest, trousers, and face are crisp and distinct. World geometry has 2-tier lighting steps. |
| **No Inverted-Hull / Black Outlines** (Hard Rule 1) | **PASS** | `after-plaza-wide-720.png`, `after-alley-720.png` | No mesh-dilation or post-process black outlines were added to characters or buildings. |
| **Killer Amber Signal Outline Preserved** (Hard Rule 1, HUD_SPEC §6) | **PASS** | `exe-after-killer-inview-720.png`, `after-walls-lit-killer-720.png` | 1 u screen-space amber (`#FFB020`) outline renders cleanly around transmitting Survivor silhouettes. |
| **No Glow / Rim Lighting Disabled** (Hard Rule 2, SLOP V3) | **PASS** | `after-terminal-states-720.png`, `after-walls-shadow-survivor-720.png` | Rim lighting alpha is zeroed; specular highlights are off. |
| **Look Contracts Preserved** (Hard Rule 3) | **PASS** | `after-terminal-states-720.png`, `after-exit-open-survivor-720.png`, `after-walls-shadow-killer-720.png` | Shared neutral survivor material in Killer view, terminal screens, exit particle marker, and fog contracts are intact. |
| **Shader Fallback & Exe Execution** (Hard Rule 4) | **PASS** | `exe-after-survivor-3p-720.png`, `exe-after-killer-inview-720.png` | Exe renders toon shading properly without missing/magenta shader artifacts. |
| **No Desktop Input Automation** (Hard Rule 5) | **PASS** | Verified via implementer report & test suites | Captures taken via `-ptk-*` staging flags / EditMode renders. |
| **Darker Night Mood / Tense Atmosphere** (User Feedback) | **FAIL** | `after-plaza-wide-720.png`, `after-plaza-wide-1080.png`, `exe-after-killer-inview-720.png` | City still reads bright and pastel. Pink, lavender, salmon, and baby blue facades dominate. |
| **Clean Shadows & Artifact-Free Surfaces** | **FAIL** | `exe-after-killer-inview-720.png`, `after-walls-lit-killer-720.png`, `after-plaza-wide-720.png` | Severe saw-tooth shadow edges under awnings; disconnected bright grey rectangle and black ground cutout. |
| **Readability** (Survivors vs walls, terminals, exits) | **PASS** | `after-walls-lit-survivor-720.png`, `after-terminal-states-720.png`, `after-exit-open-survivor-720.png` | Silhouette separation, 3 m terminal states (grey/teal/white), and exit portal visibility are all clear. |
| **HUD Contrast over Darker Scene** | **PASS** | `exe-after-survivor-3p-720.png`, `exe-after-killer-inview-720.png` | Cluster fills (`#1A1C1F`), borders (`#2C2F33`), text (`#E8E6E1`), and amber bar (`#FFB020`) exceed 7:1 contrast. |
| **Capture Staging Consistency** | **FAIL** | `exe-after-killer-inview-1080.png`, `exe-after-hacking-720.png` | 1080p killer view staged in different corridor; "hacking" captures depict running/movement with no hacking state. |

---

## 3. SLOP Section 9 Scorecard

Scored against `docs/ui/SLOP_DEFINITION.md` Section 9:

| Category | Max Points | Awarded | Notes | Gate Check |
|---|:---:|:---:|---|:---:|
| **No V1–V12 Visual Tells** | 20 | 20 | No gradients, no glassmorphism, no outer glows, no template rounded cards, no generic emoji. | **PASS** (V1, V2, V4, V8, V10 green) |
| **No L1–L8 Layout Tells** | 15 | 15 | Strict bottom-center (Survivor) and bottom-left (Killer) clusters. Spacing follows 4 px scale. | **PASS** |
| **Copy Follows §4 & Fixed Vocabulary** | 15 | 15 | Exact fixed terms: "Signal", "Silent", "Visible while you move.", "Exit Locked". | **PASS** |
| **State & Action Information Density** | 15 | 15 | Every HUD element maps directly to actionable game rules. | **PASS** |
| **Role-Specific HUDs & No State Leaks** | 20 | 20 | 3P orbit HUD for Survivor; 1P HUD with anonymous blips & signal outline for Killer. | **PASS** |
| **Interactive States & Navigation** | 10 | 10 | Keyboard/gamepad navigable; normal/focused states supported. | **PASS** |
| **Text Contrast (≥ 4.5:1 at 720p)** | 5 | 5 | Text is crisp `#E8E6E1` on `#1A1C1F`, easily exceeding 4.5:1 against the 3D scene. | **PASS** |
| **Total Score** | **100** | **100** | **Pass threshold: ≥ 85/100 and no gate failures.** | **PASS** |

*Note: While the UI/HUD elements achieve a passing SLOP §9 score, the visual 3D scene shading and materials evaluated below fail the brief's aesthetic and quality requirements.*

---

## 4. Visual Evaluation

### (a) Toon Shading Visibility
- **Characters:** The toon effect is distinctly visible. Comparing `exe-before-survivor-3p-720.png` with `exe-after-survivor-3p-720.png`, the survivor model transitions from smooth Lambertian shading to crisp, multi-tier discrete bands across the shoulders, back vest, trousers, and face.
- **World Art:** World geometry (`after-alley-720.png`, `after-walls-lit-killer-720.png`) exhibits two-band stepped shading. However, large flat facades without surface trim or normal variation can appear uniformly flat because the single directional light casts an all-or-nothing illumination across entire planar surfaces.

### (b) Night Mood vs. Cheerful Pastel Surfaces
- **Assessment:** The environment **still reads too bright and cheerful**, directly failing the core user feedback ("the game looked too bright").
- While the implementer decreased ambient sky luminance (.30 → .23) and key light intensity (1.0 → .9), the underlying albedo textures from the asset pack remain bright daytime pastels. Combined with a high toon light band tint (`.52/.52/.56`), these surfaces maintain high saturation and luminance:
  - **Pastel Pink / Lavender Facades:** In `after-plaza-wide-720.png` and `after-plaza-wide-1080.png`, the prominent 4-story residential building on the left is pastel pink/lavender (`#D2A8C0` to `#C898B5`), looking like a dollhouse under daylight.
  - **Peach / Salmon Brick:** The adjacent building in `after-plaza-wide-720.png` is warm salmon peach (`#D68E7C`).
  - **Cream / Sand Storefronts:** The commercial lower facades in `after-plaza-wide-720.png` and `exe-after-killer-inview-720.png` are light warm cream (`#DDD3B8`).
  - **Baby Blue / Periwinkle Facades:** In `exe-after-killer-inview-720.png`, the upper floor of the right building is saturated light blue (`#8CA2C9`) with white decorative cornices.
  - **Mint Green Facades:** In `after-walls-shadow-survivor-720.png`, the background building is pale lime/mint (`#A5C085`).
- The game does not evoke a tense, dark nocturnal stealth chase; it feels like an early evening stroll in a candy-colored cartoon town.

### (c) Readability
- **Survivors vs. Walls:** In survivor 3P view (`after-walls-lit-survivor-720.png`, `after-walls-shadow-survivor-720.png`), the dark vest and green/teal clothing separate clearly from both cream walls and dark storefront glass.
- **Killer View & Amber Signal Outline:** In `exe-after-killer-inview-720.png` and `after-walls-lit-killer-720.png`, the neutral grey survivor with the 1 u amber `#FFB020` outline is sharp, unmistakable, and visible at long range.
- **Terminal States at 3 m:** In `after-terminal-states-720.png` and `after-terminal-states-1080.png`, the three states (unhacked dark grey, active teal, completed white) are immediately distinguishable.
- **Exits:** In `after-exit-open-survivor-720.png` (teal ring) and `after-exit-open-killer-720.png` (white ring), the exit portal is prominent and readable.

### (d) Visual Defects, Jagged Shadows & Artifacts
- **Awning Saw-Tooth Shadow:** In `exe-after-killer-inview-720.png` and `exe-after-killer-inview-1080.png`, the shadow cast by the scalloped awning on the right building onto the blue wall exhibits extreme, razor-sharp saw-tooth stairstepping. Because `Cast-shadow edge` was configured with an overly narrow transition threshold (`.30–.34`, delta 0.04), directional shadow map aliasing is drastically magnified into jarring jagged teeth.
- **Disconnected Bright Grey Rectangle on Ground:** In `exe-after-killer-inview-720.png` (bottom-left asphalt, above the HUD cluster), there is an isolated bright grey rectangular patch (`#767A83`) sitting disconnected on the dark asphalt.
- **Harsh Black Ground Cutout:** In `after-plaza-wide-720.png` and `after-plaza-wide-1080.png`, behind the white car, there is a sharp, pitch-black rectangular shadow cutout (`#141618`) on the plaza tiles that appears as an unlit geometric hole.
- **Outlines & Rim Light:** Hard rules strictly obeyed — no black inverted-hull geometry outlines, no rim light glow, and no missing pink shaders.

### (e) HUD Contrast
- Excellent. Across all exe captures (`exe-after-survivor-3p-720.png`, `exe-after-killer-inview-720.png`), the dark panel background (`#1A1C1F`) and white text (`#E8E6E1`) provide crisp legibility over the darkened 3D scene, with contrast ratios exceeding 7:1.

---

## 5. Detailed Findings

### Finding 1: Building facades remain bright daytime pastels, failing the dark night mood
- **Severity:** MAJOR
- **Screenshot Evidence:** `after-plaza-wide-720.png`, `after-plaza-wide-1080.png`, `exe-after-killer-inview-720.png`, `after-walls-shadow-survivor-720.png`
- **Expected:** A dark, tense night chase atmosphere with muted, desaturated night tones (slate, charcoal, cool dark stone, navy, weathered dark brick).
- **Actual:** Facades remain cheerful, saturated pastels (lavender/pink `#D2A8C0`, baby blue `#8CA2C9`, salmon `#D68E7C`, pale cream `#DDD3B8`, mint green `#A5C085`). The scene reads like daytime or candy-pop lighting rather than a stealth thriller.

### Finding 2: Severe saw-tooth shadow stairstepping under building awnings
- **Severity:** MAJOR
- **Screenshot Evidence:** `exe-after-killer-inview-720.png`, `exe-after-killer-inview-1080.png`, `after-walls-lit-killer-720.png`
- **Expected:** Smooth or softly antialiased toon shadow edges without jarring geometric stair-step artifacts.
- **Actual:** The cast shadow transition band (`.30–.34`) is too narrow, severely amplifying shadow map texel grid edges into violent saw-tooth teeth under the right building's awning and along street shadow boundaries.

### Finding 3: Ground shadow anomalies (isolated bright rectangle and black cutout)
- **Severity:** MAJOR
- **Screenshot Evidence:** `exe-after-killer-inview-720.png` (bottom-left street), `after-plaza-wide-720.png` (plaza tiles behind car)
- **Expected:** Coherent, continuous ground shading and road markings matching scene occlusion.
- **Actual:** In `exe-after-killer-inview-720.png`, an isolated bright grey rectangle sits floating on the asphalt. In `after-plaza-wide-720.png`, an artificial rectangular pitch-black hole cuts into the ground plane.

### Finding 4: Mismatched 1080p exe staging and missing hacking state in captures
- **Severity:** MINOR
- **Screenshot Evidence:** `exe-after-killer-inview-1080.png`, `exe-after-survivor-3p-1080.png`, `exe-after-hacking-720.png`, `exe-after-hacking-1080.png`
- **Expected:** Consistent camera positions between before/after captures; "hacking" screenshots demonstrating active terminal hacking (hold E, progress bar, hacking prompt).
- **Actual:** 1080p killer view and 3P survivor shots were taken in entirely different locations than the baseline. "Hacking" captures merely show normal walking ("Visible while you move.") with no terminal interaction present.

---

## 6. Concrete List of Fixes for Attempt 2

1. **Desaturate & Darken World Facade Albedos:**
   - In `Client/Assets/Art/` materials or via `WorldArtSetup.cs`, tint or desaturate base facade albedos away from pastel pink, lavender, salmon, and baby blue toward muted, nocturnal tones (dark slate grey, cool deep navy, charcoal, muted weathered stucco).
   - Adjust `_LightTint` on world materials from `.52/.52/.56` down to a darker, cooler nocturnal range (e.g., `.35/.38/.45`), and deepen `_ShadowTint` so illuminated surfaces do not flare brightly at night.
2. **Widen Cast-Shadow Transition to Eliminate Saw-Tooth Aliasing:**
   - Soften the cast-shadow step threshold in `PtkToon.shader` / `WorldMaterials.cs` from `.30–.34` to a slightly wider window (e.g. `.25–.40` or `.28–.42`) so shadow map boundaries do not discretize into harsh jagged stair-steps under awnings and building cornices.
3. **Fix Ground Material & Shadow Mapping Discontinuities:**
   - Verify ground plane receiver settings and crosswalk material blending to remove the disconnected bright grey rectangle in `exe-after-killer-inview-720.png`.
   - Inspect geometry/shadow receivers on plaza tiles behind the car in `after-plaza-wide-720.png` to fix the rectangular black clipping hole.
4. **Re-stage Exe Captures Accurately:**
   - Ensure 1080p exe frames match the exact camera staging of the 720p / baseline shots.
   - Stage an actual terminal hacking interaction for `exe-*-hacking-*` showing the progress bar and `Hacking Terminal N` prompt.

---

VERDICT: FAIL (0 blockers, 3 majors)
