# QA Review Report: Ping the Killer — Unit U6c-wire (Attempt 1)

**Reviewer:** Gemini 3.8 Flash (acting as Gemini Flash QA)  
**Date:** 2026-11-04  
**Working Directory:** `C:/Unity/MultiPlayGame/PtK-reqa/client`  
**Implementer:** Claude Opus subagent (quota fallback)  

---

## 1. Spec Items Evaluation

| Spec ID | Status | File : Line Evidence | Notes |
|---|---|---|---|
| **S1** | **PASS** | `ClientApp.cs:47,257-267,407-415`<br>`RulesV2Wiring.cs:28-86`<br>`RulesV2WiringTests.cs:29-157` | `SyncCheckTracker` handles `OnCheck`, `OnResult`, `Tick` timeout (Duration + 1 s), single `TryPress` per check, and resets on `MatchStart`, `GameResult`, disconnect, and leaving `Normal`. Sound cues omitted (documented gap). |
| **S2** | **PASS** | `ClientApp.cs:485-493`<br>`RulesV2Wiring.cs:126-130,181-191`<br>`SurvivorHud.cs:269-278` | `SelfState.ActionKind/Progress` drives `HoldActionBar`. Switch prompts (open / locked by killer) and hatch escape prompt appear in reach and follow HUD_SPEC §13.3. |
| **S3** | **PASS** | `ClientApp.cs:481-483`<br>`SurvivorHud.cs:78-79,242-251`<br>`RulesV2Wiring.cs:94-105,118-120` | Objective row shows both exits and collapse clock (personal at 2x rate during Lagging via `SelfStateHolder.CollapseRate`). Info line 3 (`Exit …`) is completely removed. |
| **S4** | **FAIL** | `SurvivorHud.cs:30-84`<br>`RulesV2WiringTests.cs:225-227` | **Not implemented in runtime product code.** Pure helper `TerminalFlagCopy.Regressing` was unit-tested, but `SurvivorHud` has no `V2MarkerLayer`, `ClientApp.cs` never calls `v2.Regressing` for survivors, and survivor terminal markers never display the `Regressing` tag. |
| **S5** | **DEVIATION** | `SurvivorFeedback.cs:66-68,1460-1466`<br>`KillerHudCopy.cs:695-701`<br>`HudCopy.cs:1078-1082` | Execution wording, single-exit lines, and v1 result copy were removed. However, `CaptureWatch.Banner` remains hardcoded to 60 s (`LaggingTimeoutSeconds`) on the second capture where the timer is 30 s. |
| **K1** | **PASS** | `ExecutionGuidance.cs` (deleted)<br>`KillerHudCopy.cs:741-751` (deleted)<br>`KillerWorldLayer.cs:812-821` (removed) | Execution guidance, `KillerHudCopy.PromptLine`, and client-side `ExecutionGraceLeft` references are completely deleted. Touching Lagging survivor does nothing. |
| **K2** | **PASS** | `ClientApp.cs:403-404`<br>`KillerWorldLayer.cs:873-875`<br>`KillerHud.cs:1190-1200`<br>`RulesV2Wiring.cs:194-212` | Killer sends `C2S_Interact` on hold E. Kick and hatch-close prompts appear in reach and are replaced by the hold block during hold actions. |
| **K3** | **DEVIATION** | `KillerWorldLayer.cs:876`<br>`KillerHud.cs:1205`<br>`4-killer-collapse-preview-720.png`<br>`4-killer-collapse-preview-1080.png` | Global collapse clock is wired, but visual urgency fails in screenshots: at 0:23 (< 30 s), the collapse indicator bar remains off-white (`#E8E6E1`) rather than danger red (`#E5484D`). |
| **K4** | **PASS** | `KillerWorldLayer.cs:892-938`<br>`RulesV2Wiring.cs:94-105,115` | Overloaded terminal alerts render on screen and at screen edge via `EdgePlacement`. Both exit markers render with individual status. |
| **R1** | **PASS** | `ClientScreens.cs:1009-1027`<br>`ClientApp.cs:287-291,315-321`<br>`RulesV2Views.cs:1344-1360` | `ResultV2Panel` replaces v1 result body. Supports lobby names, slot ordering, skips killer in rows, handles unlisted players, keeps rematch and back-to-lobby flow, and `-ptk-preview-result` works. |
| **B1** | **DEVIATION** | `SurvivorHud.cs:187-196,203`<br>`7-b1-before-1080.png`<br>`7-b1-after-1080.png` | Threat strip mask stacking from row 0 upwards fixes the clipped text artifact. However, the required acceptance proof of a 1080p shot in the **same framing** failed (shot was taken outdoors in parking lot instead of hallway). |

---

## 2. Findings

### Finding 1 (Blocker): S4 Regressing tag for Survivors is not implemented in product code
- **Severity:** Blocker
- **Evidence:** `Client/Assets/Scripts/UI/SurvivorHud.cs:30-84`, `Client/Assets/Scripts/ClientApp.cs:473-494`, `Client/Assets/Tests/EditMode/RulesV2WiringTests.cs:225-227`
- **Expected:** Per brief S4 and HUD_SPEC §13.6, when `TerminalState.Flags` has `Regressing`, survivor terminal markers show the `Regressing` tag (`Terminal N · X% · Regressing`).
- **Actual:** While the unit test checks the pure method `TerminalFlagCopy.Regressing(flags, Role.Survivor)`, there is zero wiring in product code. `SurvivorHud` does not instantiate or hold a `V2MarkerLayer`, `ClientApp.cs` never projects terminal positions or calls `v2.Regressing` for survivors, and `WorldView.cs` (unmodified) does not render the tag. In an actual match as a Survivor, the Regressing tag is completely invisible.

### Finding 2 (Major): Capture banner hardcodes 60 s for second capture instead of 30 s
- **Severity:** Major
- **Evidence:** `Client/Assets/Scripts/UI/SurvivorFeedback.cs:66-68,126`, `Client/Assets/Scripts/ClientApp.cs:485`
- **Expected:** Per GDD v2 §4, the second capture initiates a 30 s rescue window (`GameConstants.SecondLaggingTimeoutSeconds`). The prompt/banner must reflect 30 s when `CaptureCount == 2`.
- **Actual:** `CaptureWatch.Banner` is defined as a `static readonly string`:
  ```csharp
  public static readonly string Banner = "Lagging. A Survivor within " + Num(GameConstants.RescueRadius) + " m for " + Num(GameConstants.RescueTimeSeconds)
      + " s rescues you " + SurvivorReadout.Dot + " " + Num(GameConstants.LaggingTimeoutSeconds) + " s left";
  ```
  `CaptureWatch.BannerPrompt` always passes this static string. On a second capture, the banner displays `60 s left` while the server counts down from 30 s and `SurvivorReadout.Meter` correctly scales to 30 s, creating an in-game copy conflict.

### Finding 3 (Major): Killer HUD Collapse Timer fails to display visual urgency (< 30 s)
- **Severity:** Major
- **Evidence:** `4-killer-collapse-preview-720.png`, `4-killer-collapse-preview-1080.png`, `Client/Assets/Scripts/UI/KillerHud.cs:1148,1205`
- **Expected:** Per Spec K3 and HUD_SPEC §13.5, the collapse block must show urgency during the last 30 s through color (the indicator bar turns `danger` red `#E5484D`, matching the survivor objective row in `3-objective-collapse-preview-720.png`).
- **Actual:** In both 720p and 1080p preview screenshots (`4-killer-collapse-preview`), the clock shows `Collapse 0:23` (23 s left), but the vertical bar on the left of the block is plain off-white text color (`#E8E6E1`). Urgency styling is not applied.

### Finding 4 (Major): Sync Check Ring zone is unreadable and needle is an oversized arrow
- **Severity:** Major (SLOP §9 failure)
- **Evidence:** `1-sync-check-720.png`, `1-sync-check-1080.png`, `2-sync-result-720.png`, `2-sync-result-1080.png`
- **Expected:** Per HUD_SPEC §13.1/§13.2, the sync ring zone must clearly display the valid press arc along the ring with distinguishable Good and Great segments, and a precise needle line sweeping across it.
- **Actual:** The target zone appears as an almost invisible horizontal tick `—` at 3 o'clock with no visible arc width or Great vs Good segment delineation. Conversely, the needle is drawn as a massive, opaque curved wedge spanning ~45° ending in a heavy arrowhead. A player cannot identify where the zone begins/ends or when the needle hits Great.

### Finding 5 (Major): Per-frame heap array allocations in HUD update loops
- **Severity:** Major
- **Evidence:** `Client/Assets/Scripts/UI/RulesV2/RulesV2Wiring.cs:94-105,108-113`, `Client/Assets/Scripts/ClientApp.cs:482`, `Client/Assets/Scripts/Killer/KillerWorldLayer.cs:866,926`
- **Expected:** Zero steady-state allocations in hot paths per `AGENTS.md` and high-performance game loop guidelines.
- **Actual:** `V2Wire.Exits(S2C_WorldState, ...)` allocates `new ExitView[count]` on every call. It is invoked every frame in `ClientApp.UpdateSurvivorHud`, every frame in `KillerWorldLayer.DrawStatic`, and a third time every frame inside `V2Wire.SwitchInProgress` called by `KillerWorldLayer.UpdateHud`. This generates 2-3 array allocations per rendered frame at 60–120 Hz.

### Finding 6 (Minor): Missing 1080p screenshot for Killer Overloaded alert
- **Severity:** Minor
- **Evidence:** `docs/qa/U6c-wire-review/5-killer-overloaded-exitmarker-720.png` (only 720p present)
- **Expected:** Brief Acceptance §Screenshots: "Capture to docs/qa/U6c-wire-screens/ at both 1280×720 and 1920×1080... 5. a killer Overloaded alert, if stageable".
- **Actual:** Implementer provided only the 720p capture and omitted the 1080p capture.

### Finding 7 (Minor): B1 fix proof deviates from required framing
- **Evidence:** `7-b1-before-1080.png` vs `7-b1-after-1080.png`, `b1-original-u4i.png`
- **Expected:** Brief B1: "Find the cause, fix it, and prove the fix with a 1080p shot of the same framing."
- **Actual:** `7-b1-before-1080.png` is framed in an indoor hallway looking down a corridor with `Silent` status. `7-b1-after-1080.png` is framed outdoors in a parking lot between two vehicles while moving in `Signal` status. While the column-stacking logic (`SurvivorHud.Columns`) successfully eliminates the text-like border artifact, the screenshot does not prove the fix in the same camera/environment framing.

### Finding 8 (Minor): Duplicate terminal tags in Killer HUD preview
- **Evidence:** `4b-killer-kick-preview-720.png`, `4b-killer-kick-preview-1080.png`
- **Expected:** Only the active tag is drawn over the terminal marker.
- **Actual:** `Terminal 7 · Regressing` and `Terminal 7` are rendered simultaneously stacked on top of each other.

### Finding 9 (Minor): Missing sound cues for sync check results
- **Evidence:** `Client/Assets/Scripts/ClientApp.cs:263-267`, `U6c-wire-impl-a1.md:56`
- **Expected:** HUD_SPEC §13.2: "`sync_great` / `sync_fail`, one short tone each."
- **Actual:** `ClientApp.cs` dispatches `screens.Survivor.Sync.OnResult` but triggers no audio cues.

### Finding 10 (Minor): Edge distance indicator label overlap in Killer view
- **Evidence:** `4b-killer-kick-preview-720.png`, `5-killer-overloaded-exitmarker-720.png`
- **Expected:** Clamped edge markers should avoid overlapping each other.
- **Actual:** Right screen edge displays overlapping text labels (`35 m`, `30 m`, `41 m`, `36 m`, `43 m`) directly colliding with indicator chevrons.

---

## 3. SLOP Definition §9 Evaluation

Scored strictly against `docs/ui/SLOP_DEFINITION.md` Section 9:

| Category | Max | Score | Deductions & Notes |
|---|---:|---:|---|
| **Visual tells (V1–V12)** | 20 | 16 | No gradients, glows, or emojis (gates pass). -4 for sync check needle reading as an oversized heavy arrow and zone appearing as an ambiguous tick line. |
| **Layout tells (L1–L8)** | 15 | 10 | 4px scale followed. -5 for severe edge indicator overlap on right screen border and duplicate stacked terminal labels in Killer view. |
| **Copy & Voice (Section 4)** | 15 | 12 | Clean fixed vocabulary. -3 for the second-capture banner advertising 60 s instead of 30 s. |
| **State & Action (what state, what action)** | 15 | 14 | Gate pass. Interact prompts cleanly tell the player what E does. -1 for unreadable sync check zone preventing action timing. |
| **Role-specific HUD & Information Leaks (Section 6)** | 20 | 16 | Gate pass. Killer view leaks no hidden survivor info. -4 for Killer collapse block failing to display danger urgency (< 30 s) and complete absence of S4 survivor terminal flag display. |
| **Interactive states & Navigation** | 10 | 10 | Gate pass. Buttons have proper states; gamepad A and keyboard Space properly bound. |
| **Contrast & 720p Readability** | 5 | 4 | Text contrast is good. -1 because sync check target zone is barely visible at 720p. |
| **TOTAL** | **100** | **82** | **FAIL (Threshold is ≥ 85 / 100 and no gate failures)** |

---

## 4. Brief/Server Mismatch Assessment

1. **Killer does not receive Regressing:**
   - In `GameServer.Terminals.cs:106`, the server explicitly strips `TerminalFlags.Regressing` for the Killer (`if (!killer && _regressing[i])`).
   - The implementer wired `v2.Regressing` in `KillerWorldLayer.cs:920` according to Brief K4, but accurately noted in `U6c-wire-impl-a1.md` that the server will never send this flag. This is a design/spec mismatch on the server side; the client implementation is defensively correct.
2. **Killer terminal progress is 0 or 100 only:**
   - On the server, only terminals with `_progress > TimeEpsilon` and `!_regressing` can be kicked (`GameServer.Terminals.cs:139`).
   - Because Killer client only receives `Progress` as 0 or 100, the client offers `Hold [E] to kick Terminal N` for all unfinished terminals within 1.5 m. The implementer adhered to HUD_SPEC §13.3 by providing the hint `Stand still · hacked Terminals only` so the player understands the server will gate the kick. Handled appropriately given protocol v0.4 constraints.

---

## 5. Test Quality Assessment

- **Updated Legacy Tests:** Existing assertions in `CopyTests.cs`, `KillerHudTests.cs`, `KillerHudQaTests.cs`, and `SurvivorFeedbackQaTests.cs` were properly refactored to check v2 replacements rather than simply deleted or weakened.
- **Assertion Blind Spot:** `RulesV2WiringTests.cs:225-227` asserts `TerminalFlagCopy.Regressing`, creating a false sense of test coverage for S4 while the actual UI wiring in `SurvivorHud`/`ClientApp` was completely omitted.
- **Autopilot & Dev Coverage:** Dev flags (`-ptk-dev-hack`, `-ptk-auto-sync`, `-ptk-dev-switch`) and pure helper logic (`HackPilot.SyncDue`, `SurvivorHud.Columns`) have solid, verified unit test coverage.

---

VERDICT: FAIL (1 blockers, 4 majors)
