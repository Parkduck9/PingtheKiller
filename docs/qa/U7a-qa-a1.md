# U7a QA Review Report — Settings Screen

**Implementer:** Claude Sonnet | **Reviewer:** Independent QA (Gemini Flash) | **Verdict:** PASS

## 1. Spec Verification

| Item | Requirement / Range | Stored Key | Code Evidence | Verdict |
|---|---|---|---|---|
| Survivor FOV | 60–90°, default 50° vert (79.2° horiz at 16:9) | `ptk.settings.survivorFov` | `GameSettings.cs:34,38,70`, `RoleCamera.cs:59`, `SurvivorOrbitRig.cs:20,38` | PASS |
| Killer FOV | 60–100°, default 58.7° vert (90° horiz at 16:9) | `ptk.settings.killerFov` | `GameSettings.cs:34,38,71`, `RoleCamera.cs:59`, `KillerViewRig.cs:11,22` | PASS |
| Mouse Sensitivity | 0.25×–3×, default 1×, 0.05 steps | `ptk.settings.sensitivity` | `GameSettings.cs:35,39,76`, `RoleCamera.cs:50`, `SettingsScreen.cs:46,57` | PASS |
| Invert Y | On/Off, default Off, inverts pitch delta only | `ptk.settings.invertY` | `GameSettings.cs:35,56,77`, `RoleCamera.cs:50-52` | PASS |
| Reduced Motion | On/Off, default Off, `-ptk-reduce-stutter` overrides | `ptk.settings.reducedMotion` | `GameSettings.cs:36,57`, `LaggingStutter.cs:23,29,61`, `ClientApp.cs:109,128` | PASS |
| Master Volume | 0–100%, default 100%, 5% steps, sets `AudioListener` | `ptk.settings.volume` | `GameSettings.cs:36,40,68`, `ClientApp.cs:127`, `SettingsScreen.cs:50,58` | PASS |
| Clamping & Fallback | Stored missing/NaN/out-of-range uses default | All keys | `GameSettings.cs:53-67,114-118` | PASS |
| Menu Integration | Reachable from main menu & in-match play menu | UI buttons | `ClientScreens.cs:146,260-271`, `KillerHud.cs:50,130` | PASS (Note F-01) |
| Input & Navigation | Keyboard (Tab/Arrows/Enter/Esc) & Mouse | UI Toolkit | `SettingsScreen.cs:43-64`, `SettingsTests.cs:311-358` | PASS |

## 2. Deviations & Judgement

- **FOV Interpreted as 16:9 Horizontal:** Sound and necessary. Existing vertical FOVs (50° and 58.7°) conflicted with the brief's 60° minimum. Converting horizontal 16:9 baseline angles to vertical angles preserves existing framing and guarantees Hor+ behavior across aspect ratios (`GameSettings.cs:109-112`).
- **Survivor In-Match Settings:** Acceptable limitation. The Survivor play menu (`HUD_SPEC.md` §5) is unbuilt. Settings is reachable from Main Menu and Killer Play Menu (`HUD_SPEC.md:709`).
- **Tab Order:** DOM hierarchy strictly matches tab order; arrow and submit navigation verified in `SettingsTests.cs:311-358`.

## 3. Code Inspection Beyond Brief

- **PlayerPrefs:** Flushed only on `Done`, Esc (outside match), or quit (`SettingsScreen.cs:82`, `ClientApp.cs:130`). Never flushed per frame.
- **Camera/Role Transitions:** `RoleCamera.Place()` passes converted FOVs every frame (`RoleCamera.cs:59`). Sensitivity is applied exactly once in `RoleCamera.Look()`.

## 4. Visual Review & SLOP Score (SLOP §9)

Tested all 8 screenshots (`720/` and `1080/` for `menu`, `settings-menu`, `play-menu`, `settings-play`):
- **Visuals (20/20):** Flat palette (`#101113`, `#1A1C1F`, `#2C2F33`, `#E8E6E1`, `#9A9890`). Custom dragger/tracker eliminates Unity template styling. No gradients or glows.
- **Layout (15/15):** Left-aligned 640 u panel; top-right placement during play preserves center screen and Killer HUD visibility.
- **Copy (15/15):** Strict adherence to vocabulary. Tabular numeric values with proper symbols (`°`, `×`, `%`).
- **Interaction & States (25/25):** Distinct focus/hover styles on buttons and custom sliders. `Done` is visually dominant primary.
- **Contrast & Resolution (5/5):** Text contrast ≥ 5.9:1. Clean scaling at 720p/1080p.
- **Total Score:** **100 / 100** | **Gate Failures:** 0

## 5. Findings

- **F-01 (Minor):** Survivor cannot access Settings during a match.
  - *Reproduction:* Start match as Survivor, press Esc (cursor releases, but no menu opens).
  - *Expected:* Settings accessible during play for both roles.
  - *Actual:* Survivor in-match menu is not yet built in client.
  - *Evidence:* `ClientApp.cs:330-335`, `HUD_SPEC.md:709`.

---
**Orchestrator note (Claude Opus):** All 8 screenshots exist. An earlier note here said one was missing; that was a truncated listing. Follow-up: the Survivor in-match menu (F-01) belongs to a later HUD unit.
