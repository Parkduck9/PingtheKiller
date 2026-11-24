quota fallback: Codex out of credits (implementer: Claude Opus subagent)

# U6c-wire attempt 1: implementation report

**Files:**
- New: `UI/RulesV2/RulesV2Wiring.cs`, `Dev/DevAutopilot.cs`, `Tests/EditMode/RulesV2WiringTests.cs` (21 tests).
- Changed: `ClientApp.cs`, `World/SelfState.cs`, `Killer/KillerWorldLayer.cs`, `KillerHudCopy.cs`, `UI/{KillerHud,SurvivorHud,SurvivorReadout,SurvivorFeedback,HudCopy,ResultCopy,ClientScreens}.cs`, `RulesV2/{RulesV2Views,RulesV2World,SyncCheckRing}.cs`, `Dev/{DevArgs,DevCapture}.cs`, `RulesV2.uss`, `HUD_SPEC.md`.
- Deleted: `Killer/ExecutionGuidance.cs`.

**Spec items:**

| Item | Status |
|---|---|
| S1 | PASS. One press per check; stale and unknown ids ignored; hides at Duration+1 s; result shown 0.4 s; cleared on status change, MatchStart, GameResult and disconnect. |
| S2 | PASS. Hold row from SelfState; switch, locked-switch and hatch prompts. |
| S3 | PASS. Objective row (both exits plus collapse); info line 3 removed. |
| S4 | PASS. Regressing tag on Survivor terminal markers. |
| S5 | PASS. v1 copy removed. |
| K1 | PASS. ExecutionGuidance, PromptLine and `ExecutionGraceLeft` are gone. |
| K2 | PASS. Kick and hatch-close prompts and hold block; Killer now sends Interact. |
| K3 | PASS. Collapse block; urgency shown by bar colour only. |
| K4 | PASS. Overloaded square and edge alert, Regressing tag, both exit markers. |
| R1 | PASS. ResultV2Panel; lobby and rematch buttons in Actions; `-ptk-preview-result` works. |
| B1 | FIXED. Cause: the level-1 threat strip, a flat 2D noise mask sitting flush on the cluster border, read as clipped red text. Each column is now stacked from the bottom. |

**Removed v1 strings:**
- `Executed: …` (2 lines)
- `no rescue within 60 s`
- `Catch every Survivor before one reaches the Exit.`
- `One more opens the Exit.`
- `After the Exit opens`
- `Locks the open Exit for 5 s.`
- `Exit Locked` / `Exit Open` / `Locked N s` (line 3 and Killer tags)
- ` · Exit open/locked` suffix
- v1 headline, outcome and reason sentences
- grace lines (`Wait…`, `Step back…`, `Touch again…`)

**Tests:**
- EditMode: 1340/1340 pass (baseline 1320), 0 compile errors.
- About 25 existing source-text and copy assertions were updated (none deleted). Each now asserts the v2 replacement. Rationale comments carry a `U6c-wire` tag in each file.
- `dotnet test Server.Tests`: 959/963. The 4 failures are `U2dQaSweepTests` balance sweeps, which fail the same way on unmodified `main`.
- BuildWindows: Success.

**Screenshots** (`docs/qa/U6c-wire-screens/`):
- Live: 1, 2, 5 (720p only) and 7.
- Preview: 3, 4, 4b and 6.
- Bots did not power any exit within 480 s, so the objective and collapse states could not be captured live.

**SLOP self-score:** about 84.
- Clean: no glow, no flashing, `danger` used only on bars.
- Deductions: edge distance labels overlap (pre-existing), the preview draws duplicate tags, and the sync needle reads as a heavy arrow.

**Unverified items and risks:**
- `great` aim gave Good; the server judges on its 20 Hz tick.
- The Killer never receives Regressing, and kick prompts show for 0 % terminals (N9).
- No sounds.
- The capture banner still says 60 s.
- Live collapse, hatch and kick are untested.
- Tag hide-on-text-change has no test.

## Attempt 2

| # | Item | Status |
|---|---|---|
| 1 | S4 Survivor Regressing | FIXED. `SurvivorHud` owns a `V2MarkerLayer`. `DrawTerminalFlags(world, pose)` runs every frame from `ClientApp` (`rig.Pose`). It draws tags for on-screen terminals only and clamps them out of the keep-out. Path test plus ClientApp source test. §13.6 updated. |
| 2 | Banner seconds | FIXED. The text follows `SurvivorReadout.SecondStage` (the stage `LaggingTotal` uses): 60 s, then 30 s on the second capture. The banner shows once more when the first stage times out and escalates. Both stages tested. Sweep found no other hard-coded Lagging duration. |
| 3 | Allocations | FIXED. `ExitCache` builds one buffer per WorldState for ClientApp and the Killer layer, and gives the Firewall target. A `TextMemo` keeps prompt, exit, collapse, hold, Regressing and Overloaded texts until their value changes. Tested with buffer and string identity. |
| 4 | Needle | FIXED. The needle is a 3 u hand, radius 30 to 80, on a 7 u `bg` keyline. A 2 u cut separates Great from Good. §13.2 updated. Shots `a2-1` and `a2-2`. |
| 5 | Shot 5 at 1080p | DONE, live (`a2-5`). |
| 6 | B1 same framing | PARTIAL. Same framing is impossible: offline spawns and the bot Killer are random, and the client has no seed flag. `a2-7` crops the identical HUD region before and after the fix. |

Notes:
- F8 (duplicate preview tags) and F10 (edge label overlap) are noted, not fixed.
- EditMode: 1347/1347 pass. `SurvivorKillerRenderingReverifyTests` failed once, depending on random line of sight; it was green on the rerun.
- BuildWindows: Success.
