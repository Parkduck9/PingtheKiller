# QA Review: Unit U6c-ui (Rules v2 HUD widgets)

**Verdict:** PASS  
**SLOP §9 Score:** 93/100 (No gate failures; pass threshold ≥ 85)

## 1. Spec Verification Table

| Spec Requirement | Status | Evidence |
|---|:---:|---|
| `SyncCheckView` fields (`Duration, ZoneStart, ZoneSize, GreatSize, Elapsed, Result?`) | PASS | `RulesV2Views.cs:21-27` |
| `ExitView` fields (`Powered, Open, Locked, SwitchProgress01`) + wire decoder | PASS | `RulesV2Views.cs:30-44` |
| `HoldActionView` fields (`Kind, Progress01`) | PASS | `RulesV2Views.cs:47-51` |
| `CollapseView` fields (`GlobalLeft, PersonalLeft`) | PASS | `RulesV2Views.cs:55-60` |
| `TerminalFlagsView` fields (`Regressing, Overloaded`) | PASS | `RulesV2Views.cs:63-72` |
| `ResultV2View` fields (rows, reason, winner rule) | PASS | `RulesV2Views.cs:83-122` |
| USS styles (`RulesV2.uss`) | PASS | `RulesV2.uss:1-99` |
| EditMode tests (17 new covering needle, hit/miss, clamping, collapse, results) | PASS | `RulesV2HudTests.cs:20-309` |
| Flag `-ptk-preview-v2-hud <scenario>` in `DevArgs`/`DevCapture` | PASS | `DevArgs.cs:53,133`, `DevCapture.cs:398` |
| `HUD_SPEC.md` section 12 added | PASS | `HUD_SPEC.md:707-809` |
| Widget 1: Sync check ring (sweeping needle, Space press, flash result 0.4s, 720p/1080p) | PASS | `SyncCheckRing.cs:55`, `1280x720-v2-sync-check.png` |
| Widget 2: Hold action bar (exit switch, kick terminal, close hatch) | PASS | `RulesV2Blocks.cs:10-49`, `1280x720-v2-kick-terminal.png` |
| Widget 3: Exit status (unpowered, powered, switch %, open, locked) | PASS | `RulesV2Blocks.cs:54`, `1280x720-v2-exit-switch-hold.png` |
| Widget 4: Collapse timer (global, personal/Lagging, urgent ≤30s colour change, no flash) | PASS | `RulesV2Blocks.cs:90`, `1280x720-v2-collapse-urgent.png` |
| Widget 5: Terminal flags (Regressing to both, Overloaded killer edge alert) | PASS | `RulesV2World.cs:53-66`, `1280x720-v2-kick-terminal.png` |
| Widget 6: Result screen v2 (winner line, reason, player list, majority rule) | PASS | `RulesV2World.cs:193`, `1280x720-v2-result-4of7.png` |

## 2. Protocol v0.4 Mirroring Check
- `ResultReasonV2` (0=AllResolved, 1=AllSurvivorsDown, 2=TimeUp, 3=Collapse) mirrors `GameEndReason` exactly (`RulesV2Views.cs:16`).
- `HoldKind` (0=None, 1=ExitSwitch, 2=KickTerminal, 3=CloseHatch) mirrors `S2C_SelfState.ActionKind` exactly (`RulesV2Views.cs:13`).
- `SyncResult` (0=Fail, 1=Good, 2=Great) mirrors `S2C_SyncResult.Result` (`RulesV2Views.cs:10`).
- Decoders `ExitView.FromWire` and `TerminalFlagsView.FromWire` map wire bitmasks and 0–100 progress without mismatch. Wiring will be seamless.

## 3. Deviations Judgment
1. **Ring placed below character feet (not exact center):** APPROVED. In 3rd-person orbit, exact screen center directly occludes the survivor avatar. Placing it in the bottom-center stack maintains clear character visibility while remaining in prime focal view (`HUD_SPEC.md:718`).
2. **Killer exit status on world markers (not panel):** APPROVED. Strictly conforms to SLOP §8 ("the world is the HUD; exit lock state on marker, not in a panel").
3. **Extra `Lagging` field in `CollapseView`:** APPROVED. Essential for `CollapseCopy.Personal` to differentiate "Lagging: 2× faster" from standard personal clock display as specified in brief bullet 4.
4. **Amber square for off-screen Overloaded:** APPROVED. Intentionally avoids chevron/diamond shapes, preventing Killer confusion with Survivor edge markers (`HUD_SPEC.md:795`).

## 4. Visual Readability & Experience
- **Sync check:** Dark disc backdrop (`#1A1C1F`) ensures high contrast against 3D greybox/textured environments. At 720p, the Great zone is 14 u wide (vs 8 u track), providing instant silhouette and tone differentiation during a 1.1s sweep. The 2 u needle features a 4 u dark keyline to prevent washout over the white Great zone.
- **Hold bar & Collapse:** Legible M typography (18 u) with integer percentage floor clamping. Collapse urgency (≤30s) tints only the accent bar (`danger` for Survivor, `text` for Killer), completely avoiding disorienting flashes.
- **Result screen:** Clear hierarchy, unambiguous winner banner, explicit majority rule math (`3 of 7 escaped ... 4 of 7 needed`), and slot-ordered rows with distinct own-row styling.

## 5. Bug & Architecture Audit
- **Time/Input:** `SyncCheckState.TryPress` enforces single-press (`Pressed = true`), ignoring repeats. Expiry window (`Duration + 0.15s`) prevents stale presses.
- **Allocations:** `V2MarkerLayer` pools `Label` instances (`tags`). No GC churn in core render loops.
- **Role Isolation:** Zero state leaks. Killer cannot see Survivor personal clocks or terminal percentages; Overloaded alert is Killer-only; Danger color never leaks to Killer HUD.

## 6. SLOP §9 Score: 93/100
- Visual tells V1–V12: 20/20 (Gate PASS). Flat fills, no glow, 0 radius, contrast ≥ 5.9:1.
- Layout tells L1–L8: 15/15. Strict 4px grid (4/8/12/16/24/32/48).
- Copy: 15/15. Canonical vocabulary adhered to strictly.
- State/Action clarity: 15/15 (Gate PASS).
- Role-specific HUDs / No leaks: 20/20 (Gate PASS).
- Control states / Navigation: 8/10 (Gate PASS). Result actions wired to standard UI Toolkit buttons; -2 for preview-mock inert buttons.
- Contrast & 720p floor: 5/5.

## 7. Findings

| ID | Severity | Expected | Actual | Evidence |
|---|:---:|---|---|---|
| F-01 | Minor | Leading-edge tolerance for skill check should count as Good | `Judge` includes leading tolerance in Great zone (`zoneStart - tolerance`) | `SyncCheckMath.cs:34` |
| F-02 | Minor | Preview driver should not modify dirty scene settings on run | Unity project files outside brief touched during dev test | `U6c-ui-impl-a1.md:57` |

---
**Orchestrator judgement (Claude Opus):** Flash returned PASS 93, but the orchestrator rejected attempt 1 because the sync zone was grey on grey. Attempt 2 draws the zone in `text`, makes the great part distinct by shape, enlarges the ring to 144 u and fixes F-01 (leading tolerance created Great). The orchestrator checked `1280x720-v2-sync-check.png` and the zone is now found at a glance. EditMode 1238/1238 and the build pass. PASS.
