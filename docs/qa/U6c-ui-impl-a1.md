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
  - `HUD_SPEC.md`: new §12.

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
- Placement is in HUD_SPEC §12.1. The wiring touches `SurvivorHud`, `KillerHud`/`KillerWorldLayer` and `ClientScreens`; info line 3 is removed.

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
