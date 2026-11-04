# U6c-ui implementation, attempt 1

quota fallback: Codex out of credits (implementer: Claude Opus subagent)

**Files**
- New `Client/Assets/Scripts/UI/RulesV2/`:
  - `RulesV2Views.cs`: view models.
  - `RulesV2Copy.cs`: pure math and copy.
  - `SyncCheckRing.cs`, `RulesV2Blocks.cs`, `RulesV2World.cs`: widgets.
  - `RulesV2Preview.cs`: the preview driver.
- New `Resources/RulesV2.uss` and `Tests/EditMode/RulesV2HudTests.cs`.
- Edited:
  - `DevArgs.cs`: the `-ptk-preview-v2-hud` flag.
  - `DevCapture.cs`: starts the preview.
  - `HUD_SPEC.md`: new §13.

**View-model API**

| View model | Fields | Widget |
|---|---|---|
| `SyncCheckView` | `CheckId, Duration, ZoneStart, ZoneSize, GreatSize, Elapsed, Result?` | `SyncCheckRing` + `SyncCheckState` (`TryPress`, event `Press`) |
| `ExitView` | `Id, Powered, Open, Locked, SwitchProgress01, LockedLeft`; `FromWire(id, flags, progress)` | `ExitCell`, `V2MarkerLayer.Exit` |
| `HoldActionView` | `Kind, Progress01` | `HoldActionBar` |
| `CollapseView` | `GlobalLeft, PersonalLeft, Lagging` | `CollapseTimer` |
| `TerminalFlagsView` | `FromWire(id, progress, flags)` | `V2MarkerLayer.Regressing` / `.Overloaded` |
| `ResultV2View` | `FromIds(...)` | `ResultV2Panel` |

**Wiring plan (U6c-wire)**
- `S2C_SyncCheck` / `S2C_SyncResult` → `SyncCheckView`. `Press` sends `C2S_SyncPress`.
- WorldState exits → `ExitView.FromWire`, with `LockedLeft` from FirewallBlock.
- SelfState `ActionKind/ActionProgress` → `HoldActionView`.
- World and self `CollapseLeft` plus own Lagging → `CollapseView`.
- `TerminalState.Flags` → `TerminalFlagsView`.
- GameResult plus lobby names → `ResultV2View`.
- Placement is in HUD_SPEC §13.1. The wiring touches `SurvivorHud`, `KillerHud`/`KillerWorldLayer` and `ClientScreens`; info line 3 is removed.

**Tests:** EditMode 1237/1237 passed (17 new), 0 compile errors. Baseline was 1220.

**Build:** `BuildScript.BuildWindows`: Success.

**Screens:** `docs/qa/U6c-ui-screens/`, at 1280x720 and 1920x1080:
- sync-check, plus the sync result;
- exit-switch-hold;
- kick-terminal;
- collapse-urgent;
- result 4/7 and 3/7.

All 14 opened and checked. They were captured with `-ptk-*` flags only.

**SLOP self-score: 92/100, no gate failures.**
- Deductions: the ring track `line` is barely visible (decorative); the preview buttons are inert.

**Known gaps**
- The preview sits over the live v1 HUD, so `Exit Locked` and `Silent` stay visible. The sync-check prompt row is a stand-in.
- Sounds are specified, not built.
- The ring sits under the character's feet, not at the exact screen centre, so the character stays visible.
- Unity rewrote assets outside my scope: `Art/Characters/*`, `DOTweenSettings`, the URP settings and ProjectSettings. Review them before committing.

## Attempt 2

quota fallback: Codex out of credits (implementer: Claude Opus subagent)

**Fixes**
1. **Sync ring readability** (`SyncCheckRing.cs`, `RulesV2.uss`)
   - The ring is now 144 u.
   - Good zone: `text` at the full band width (10 u).
   - Great part: 22 u `text`, standing 6 u out of the band on both sides, so it differs from Good by shape.
   - Track: dark `line`.
   - Needle: 3 u `text` on a 6 u `bg` keyline.
   - No glow, no gradient, no `signal`.
   - Placement is unchanged: 8 u above the prompt row, under the feet. At 720p its top edge is at 67.1 % of the screen height, against the feet at 67 % (default pitch).
2. **F-01** (`RulesV2Copy.cs`): Great is now only [ZoneStart, ZoneStart + GreatSize]. The tolerance widens Good on both edges and never creates Great.
   - New test `ToleranceNeverCreatesGreatOnEitherEdge`.
   - `ToleranceWidensBothZoneEdges` now expects Good at the leading edge. This is my own attempt-1 assertion, changed because the rule changed.
3. **Renumbering:** the HUD_SPEC section is now **§13 "Rules v2"** (13.1–13.7). Every reference was updated in code comments, the USS, the tests and this report. §13.1/§13.2 are updated for the new ring and the judging rule.
4. **Desktop input:** none. Captures used only `-ptk-*` flags.

**Zone width at 720p**, measured on `1280x720-v2-sync-check.png`:
- Zone arc: 223.2°–266.5°, which is ≈ 34 px along the arc. Thickness 7.2 px.
- Great part: 18 px thick, ≈ 10 px long.

**Verification**
- EditMode: 1238/1238 passed, 0 compile errors (`u6c-ui-runs/editmode-a2.xml`).
- `BuildScript.BuildWindows`: Success (`build-a2.log`).
- After each Unity run I reverted the re-serialised files with `git checkout`. Only my owned files remain changed.

**Screens, new:** `1280x720-` and `1920x1080-v2-sync-check.png` and `-v2-sync-check-result.png`. I opened all four.
- The bright zone is found at a glance.
- The thick Great head is distinct.
- The result shows `Great` with the needle frozen in the great part.
- The ring stays clear of the character.

The other v2 screens are unchanged from attempt 1, since those widgets did not change.

**SLOP self-score: 93/100**, no gate failures.
- Remaining risk: while the objective row shows (hacking after power), the ring sits 56 u higher and can touch the feet. This is rare and documented in §13.1.
