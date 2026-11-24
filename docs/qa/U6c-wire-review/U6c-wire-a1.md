# Brief U6c-wire — attempt 1: wire the rules v2 HUD to protocol v0.4

- **Implementer:** Claude Opus subagent. This is the quota fallback, because Codex is out of credits.
- **QA:** Gemini Flash via agy, run by the orchestrator. It covers code and visuals.
- **Worktree:** `C:\Unity\MultiPlayGame\PtK-work\u6c-ui`, branch `claude/u6c-wire`, based on main `7f2c746`. The Unity Library is already warm.
- **Parallel unit:** U6c-hatch (worktree `PtK-work/u4h`) owns `World/WorldView.cs`, `World/WorldProps.cs` and the hatch art. **Do not touch those files.**
- Do **not** commit, push or merge.

## Read first

- `AGENTS.md`
- `docs/ui/SLOP_DEFINITION.md` (mandatory)
- `docs/ui/HUD_SPEC.md`, especially §4, §6 and §13 (Rules v2)
- `docs/GDD-v2.md`
- `docs/briefs/U6a-a1.md`, which defines protocol v0.4
- `docs/qa/U6c-ui-impl-a1.md`, which holds the widget API and the wiring plan
- `Client/Assets/Scripts/UI/RulesV2/*`

## Goal

The rules v2 server and protocol v0.4 are on main. The client widgets exist but are not connected. In a real match the client still ignores `S2C_SyncCheck`, `S2C_SyncResult`, the v0.4 `SelfState` and `WorldState` fields, and the v0.4 `GameResult` lists. It still shows v1 copy and the v1 execution prompt.

After this unit, a human player can play rules v2 end to end with correct on-screen information.

## Owned files

You may edit or create:
- `Client/Assets/Scripts/ClientApp.cs`
- `Client/Assets/Scripts/UI/**`, including `RulesV2/**`
- `Client/Assets/Resources/*.uss`
- `Client/Assets/Scripts/Killer/**`
- `Client/Assets/Scripts/Dev/**`
- `Client/Assets/Scripts/World/SelfState.cs`
- `Client/Assets/Tests/EditMode/**`, except `HatchViewTests.cs`, which belongs to U6c-hatch
- `docs/ui/HUD_SPEC.md`
- your reports and screenshots

**Do not edit:**
- `Shared/**`, `Server*/**`, `Tools/**`
- `Client/Assets/Editor/**`
- `World/WorldView.cs`, `World/WorldProps.cs`, `World/WorldArt*.cs`, and any art asset

If you need anything in those files, say so in the report instead.

## Spec

### Survivor

**S1. Sync check**
- On `S2C_SyncCheck`, build a `SyncCheckView`. `Elapsed` is local time since the packet arrived.
- Show `SyncCheckRing` per HUD_SPEC §13.1/§13.2.
- **Space** sends exactly one `C2S_SyncPress { CheckId }` per check. Further presses for the same check do nothing.
- On `S2C_SyncResult` with a matching `CheckId`, show Great / Good / Fail for about 0.4 s, then hide.
- Ignore stale or unknown `CheckId` values.
- If no result arrives by `Duration` + 1 s, hide the ring.
- Clear everything on MatchStart, on GameResult, on disconnect, and when the local status leaves Normal.
- Space must not trigger anything else; it is unused today.

**S2. Hold row**
- Feed `SelfState.ActionKind` / `ActionProgress` into `HoldActionBar` in the prompt row (§13.3).
- Add the interact prompts for the exit switch and the hatch:
  - near a powered exit's switch, the "hold to open" prompt;
  - within `GameConstants.HatchRadius` of a visible hatch (`WorldState.HatchVisible`), the escape prompt.
- Use HUD_SPEC copy and the SLOP vocabulary.
- If HUD_SPEC lacks the exact copy, add it to §13 first, then use it.

**S3. Objective row**
- Show both exits with `ExitView.FromWire` from `WorldState.Exits`, including `LockedLeft` where the data exists.
- Show the collapse clock with `CollapseView`:
  - personal = SelfState `CollapseLeft`;
  - global = WorldState `CollapseLeft`;
  - Lagging comes from the own status.
- Visibility follows §13.1.
- Remove info line 3, the v1 `Exit …` line, as §13.1 says.

**S4. Terminal flags**
- `TerminalState.Flags` Regressing is shown on the survivor terminal markers (§13.6).

**S5. Remove v1 copy**
Remove every survivor-facing v1 string that contradicts GDD v2, for example:
- single-exit wording;
- "touch again" or execution wording;
- v1 result reasons.

List each removed or changed string in the report.

### Killer

**K1. Remove the v1 execution prompt**
- Remove `ExecutionGuidance`, `KillerHud.SetPrompt(ExecutionPrompt…)`, and `KillerHudCopy.PromptLine` together with its execution lines.
- Remove any `ExecutionGraceLeft` use on the client. The field stays on the wire, in `Shared/`, and is not yours to change.
- GDD v2 has no execution: touching a Lagging survivor does nothing.

**K2. Hold block**
- The kick-terminal and close-hatch progress comes from `SelfState` (§13.3).
- Add the kick and hatch-close interact prompts when in range, using HUD_SPEC copy.

**K3. Collapse block**
- Show the global clock (§13.5), urgent in the last 30 s through colour only.

**K4. Terminal flags and exits on world markers**
- **Overloaded:** an alert plus an edge indicator. Reuse `EdgePlacement`.
- **Regressing:** on the terminal markers.
- **Exit markers:** unpowered, powered, switch %, open, locked (§13.4).

### Result screen

**R1. Result v2 panel**
- `S2C_GameResult` (WinnerRole, Reason, EscapedIds, EliminatedIds) plus lobby names → `ResultV2View` → `ResultV2Panel`. It replaces the v1 result body in `ClientScreens`.
- Keep the existing return-to-lobby flow (`SetResultLobby`) and the rematch path working.
- `-ptk-preview-result` must still work.
- Remove v1 `ResultCopy` lines that no longer apply.

### Bug

**B1. Red clipped label**
- At 1080p a clipped red label shows above the survivor HUD. See `docs/qa/U4i-screens/a2-exe-after-survivor-3p-1080.png`.
- It is probably the threat widget's label.
- Find the cause, fix it, and prove the fix with a 1080p shot of the same framing.

### Dev staging, for screenshots only

The widgets must be photographed in a **real offline match**. Offline mode runs the real `GameServer` in-process, so use it, not the fake preview.

No dev flag can make a survivor hack today. Add flags in `Dev/**` and register them in `DevArgs`:
- **`-ptk-dev-hack <seconds>`:** a survivor autopilot.
  - It walks to the nearest unfinished terminal through the normal `C2S_MoveInput` path. Use the client's map or geometry helpers to steer around walls.
  - It then holds Interact for the given seconds.
- **`-ptk-auto-sync great|good|miss`:** presses Space at the matching needle position for each sync check.
  - `miss` never presses.
  - It goes through the same code path as a real key press.

Rules for these flags:
- They are dev-only and off by default.
- They send only the packets a human could send.
- **Never** simulate OS input or `SendKeys`.
- If you can stage the exit switch, hatch or killer kick the same way at low cost, do it. Otherwise list them as unstaged gaps.

The old `-ptk-preview-v2-hud` may stay for the result panel and for states you cannot stage live.

## Acceptance

### Tests

**EditMode:** all tests green, with 0 compile errors.
- Baseline on main is 1320.
- Existing source-text QA tests that assert removed v1 code must be updated, not deleted. Explain each one in the report.

Command, run from the worktree root:
```
"/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "$PWD/docs/qa/u6c-wire-runs/editmode.xml" -logFile docs/qa/u6c-wire-runs/editmode.log
```

**New tests must cover:**
- sync check state:
  - one press per check;
  - stale and unknown `CheckId` ignored;
  - timeout hide;
  - cleared on status change and MatchStart;
- the `SelfState` v0.4 fields reaching the hold and collapse view models;
- exit and terminal-flag mapping from a `WorldState`;
- the result v2 rows from a `GameResult` with 3 of 7 and 4 of 7 escaped;
- that no execution copy remains (a source or copy check);
- `DevArgs` parsing of the new flags.

`dotnet test Server.Tests` must stay green; you do not touch the server.

### Build

`BuildScript.BuildWindows` succeeds:
```
-executeMethod BuildScript.BuildWindows
```

### Screenshots

Capture to `docs/qa/U6c-wire-screens/` at both 1280×720 and 1920×1080, from a **live offline match** wherever possible:
1. survivor sync check with the needle mid-sweep;
2. a survivor sync result;
3. the survivor objective row with an exit Powered or Open and the collapse running;
4. killer HUD with the collapse block and the exit world markers;
5. a killer Overloaded alert, if stageable;
6. result v2;
7. the 1080p B1 before/after framing.

**Open every PNG yourself** and score it honestly against SLOP §9.

### Housekeeping

Unity batch runs re-serialise files outside your scope, such as `Art/Characters/*`, URP assets and materials. Revert them with `git checkout -- <file>` so only your owned files remain changed.

## Report

Write `docs/qa/U6c-wire-impl-a1.md`, at most 350 words. Start with the header note "quota fallback: Codex out of credits". Include:
- files changed;
- each spec ID (S1–S5, K1–K4, R1, B1) with a one-line status;
- removed v1 strings;
- test counts;
- the build result;
- the screenshots list, staged live or by preview;
- your SLOP self-score;
- unverified items and risks.
