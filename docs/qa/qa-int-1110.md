# Integration QA 2026-11-10 (Fable, read-only) — claude/qa-integration = main 53f4ef9 + U6d, U6g, Q1, Q2, Q4, Q6, U4k

**Verdict: PASS** for merging all seven into main. 0 blockers, 0 majors, 4 minors. SLOP §9: **98/100**, no gate failures.

## Gates (orchestrator runs, `docs/qa/qa-int-runs/`)
| Gate | Result |
|---|---|
| dotnet build -warnaserror | 0 errors |
| Server.Tests (non-sweep) / BalanceSweep | 1062/1062 / 18/18 |
| Harness scored x3 (threshold 100, --expect-los) / Harness.Tests | 100/100 x3 / 638/638 |
| Unity EditMode / BuildWindows | 1468/1468 / OK |
| Live exe survivor / Killer (idle) | 285 s, 7/7 escaped, 3 cap, 3 rescue / 190 s, 7/7 escaped; 0 errors |

## Merge review
- 62eeb05 `ClientApp.cs` resolution is correct: `EndMatch` keeps `ResetMenu()` and `v2Audio.End()`; Q4 hooks (`Begin`, `OnWorld`, `OnSelf`, `OnSync`, `Frame`) and Q6 wiring (both menus, `PlayMenuGate`) all present.
- Q6 gate x Q4: opening the menu sends the Interact release, so the hold loop stops via `Frame`; cues unaffected. Q6 x Q2: `KillerHud.ClusterRect` counts `PlayMenu.StackedHeight`, `EdgePlacement.KeepOut` uses it. Q6 x U6g: echo answered on the receive thread, no gate. Q1 x U6d: server still names `"Bot " + id` (GameServer.Lobby.cs:135); lobby, fx lines and result rows agree (Bot 4 = Killer). U4k is Editor/prefab/mesh only; no runtime script conflict.
- U6g is inert offline (`UpdateRttProbes` returns when `_offline`); live logs show `rtt=0`; covered only by harness H6 and RttProbeTests. Expected, noted.

## Findings
1. **minor, copy conflict Q1 vs Q6**: `PlayMenuCopy.CapturedTwice` = "Captured twice: the next capture eliminates you." for `CaptureCount >= 2`, but Q1 removed "Captured twice" from the meter tag because `CaptureCount` counts Lagging stages (HUD_SPEC §4.3 11/08 vs §5 Q6). After a first stage runs out, the menu claims two captures. Expected: a count-free line ("Next capture eliminates you."). Evidence: `Client/Assets/Scripts/UI/PlayMenu.cs:35-45`.
2. **minor, visual**: in-view diamond tag "39 m" touches the right-edge chevron glyph with 0 u gap; "38 m"/"39 m" tags touch. Q2's 4 u gap covers edge markers only. Evidence: `live-killer/v2-killer-collapse.png` (~1255,390).
3. **minor, coverage**: the exe window had no focus; every mid-match survivor shot shows "Click to resume mouse look", the human never hacked (128/128 SelfState `action=None`, no sync check for player 4), `-ptk-auto-sync` never fired. No play-menu/Settings shot in the integrated build (Q6 shots are pre-merge).
4. **minor, note**: `Disconnect()` skips `v2Audio.End()`; harmless (`Frame(playing=false)` stops the hold, `Begin` resets rules).

## Logs
No exceptions, missing assets or audio errors (only the engine d3d12 info-queue line). Sequence sane: cues ExitsPowered -> ExitOpen -> CollapseStart, captures before rescues, nothing after match end. Five `skill press sent=True watch=False` at 256-259 s are the dev `-ptk-skill-at` retry while on cooldown (server ignores).

**Balance**: 285 s / 7 of 7 vs sweep 1v7 median 566/594 s (p25 484/501, win 58 %, `U6d-balance-a3fix.log:179,272`). One sample under p25 is not a red flag: the bot Killer spent two chase-capture cycles on a non-fleeing human parked at a terminal (captured twice at 36.6,60.1, both undone by bot rescues), decoy skills were spammed, and the earlier live run was 644 s / 5 of 7. Recommend 2-3 further survivor runs with a focused window and `-ptk-dev-hack` before judging a trend. Idle-Killer 190 s / 7 of 7 matches the sweep's 100 % band.

## Visual (41 PNG)
Menus, lobby (Bot 1..7 contiguous), results clean; role HUDs distinct; Killer reads "Terminals 0/5" like the Survivor (Q1). Edge tags stack without overlap (killer-crowd 4 right, skillfx-s2 4 right, overloaded alert + 2). No U4k holes or sky gaps in any street/facade shot. Bodies keep ground spacing (depth overlap remains, exit-open.png); bent pose = `MotionPose.Injured`, by design. Score: V 19, L 15, copy 15, state 15, role 20, interactive 9 (menu states not visually re-verified), contrast 5.

## Orchestrator judgement (Opus)

PASS. All gates green on the combined tree; I verified finding 1 in PlayMenu.cs:35-41 (real, minor: CaptureCount counts Lagging stages, so "Captured twice" can be wrong). Findings 1-2 are follow-ups; 3 is a coverage gap (unfocused window), 4 is harmless.
