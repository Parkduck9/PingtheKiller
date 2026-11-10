# Q4-v2-sounds — QA, attempt 1

Quota/routing: Codex and Gemini Flash unavailable → Claude Opus implemented; Fable QA (user routing 2026-11-09).

Reviewed: `Client/Assets/Scripts/Audio/V2Cues.cs`, `Client/Assets/Scripts/Audio/RulesV2Audio.cs`,
`Client/Assets/Scripts/UI/RulesV2/RulesV2Wiring.cs` (+3), `Client/Assets/Scripts/ClientApp.cs` (+10/-1),
`Client/Assets/Tests/EditMode/V2SoundTests.cs`, against `docs/qa/Q4-v2-sounds-brief.md`, the implementer's report and the
server rules (`Server.Core/Runtime/GameServer.Exits.cs`, `.Terminals.cs`, `.Visibility.cs`, `.Rules.cs`, `KillerBrain.cs`).
QA edited no product code and added no tests. Unity and exe runs used the implementer's LFS-complete scratch copy
(`...\scratchpad\ptk`), whose `Client/Assets/Scripts`, `Client/Assets/Tests` and `Shared/Runtime` are byte-identical to the
worktree (`diff -r --strip-trailing-cr -q`, rc=0). The worktree project was not opened in Unity (orchestrator's instruction);
no line-ending-only changes are in the worktree (`git diff --stat` equals `--ignore-cr-at-eol --stat`).

## Verdict: PASS

No blocker or major finding. Every spec bullet is PASS or an accepted, documented DEVIATION. EditMode 1401/1402 (0 failed,
1 pre-existing ignored), all 16 `V2SoundTests` passed. Five exe runs reproduced 9 of the 11 cues and the two negative rules
(Killer never gets ExitOpen/Capture/Rescue, own capture is silent); each cue line matches one `[offline server]` event.
Still unobserved in the exe: HatchOpen, TerminalKick, the Killer's own-hold HatchClose, and the hold loop (run E never got the exits powered).
Real listening is unverified.

## Server-rule check of the inferences

- `StartCollapse` has exactly two callers: `Switches` (same tick as `_exitOpen[i] = true`, Exits.cs 80-83) and `CloseHatch`
  (Exits.cs 139-145, which also calls `PowerExits("hatch")`). So "collapse starts while no exit is Open" ⇒ hatch closed. Holds.
  One blind spot: tick order is `KillerHolds` → `Switches` (Rules.cs 54-55); a hatch close and a switch completion in the same
  50 ms tick show up as collapse + Open exit, read as exit-driven. Negligible.
- `TerminalFlagsFor(i, killer)` (Terminals.cs 102-108): `Regressing` is set only for `!killer`. The Killer never receives it,
  so the Killer-side TerminalKick must come from the own hold. Holds.
- A completed kick zeroes `KickHeld` and `KickTerminal` (Terminals.cs 149-151); a completed hatch close zeroes `HatchHeld`
  (Terminals.cs 125-128). `SetActionState` (Visibility.cs 244-258) then reports `ActionKind.None` (or `Kick` directly when the
  Killer keeps holding next to a terminal with progress), and `SelfStateDue` sends a SelfState on any ActionKind change
  (Visibility.cs 218). The client handles `next != HoldKind`, so both None and HatchClose→Kick are covered.
- `Hack` (Terminals.cs 44-49) clears `SwitchExit` every tick for non-Normal or non-interacting players: a holder captured
  mid-switch gets ActionKind None at once, so the loop cannot stay stuck on a capture.
- `ReceivedPacket.Time = LinkClock.Now` (Net/IGameLink.cs 12), the same clock `Frame` uses: the hold prediction is consistent.
- `BuildSurvivorView` (Visibility.cs 136-156) sends every Survivor entry with its real `Id` and `Status`; the Killer view
  sends Survivor entries with `Id = 0` (Visibility.cs 63-72). The rules read Ids only in the `!killer` block.

## Spec bullets

| # | Bullet | Result | Evidence |
|---|--------|--------|----------|
| 1 | SyncGreat/Good/Fail, Survivor, first result of the current check | PASS | `SyncCheckTracker.Resolved` raised only where `OnResult` returns true; `OnSync` drops Killer. Run A: 4 `SyncFail` after `sync player=4 ... result=Fail` (own Id 4), no cue for other players' results (lines 565, 660, 684, 693). Impl runs: SyncGood ×8, SyncGreat ×2. Test `SyncResultsMapToTheirCuesThroughTheTrackersResolvedEventOncePerCheck`. |
| 2 | SwitchHold loop: plays while own ActionKind != None, pitch rises with progress, stops within a frame; seamless | PASS (code/EditMode) | `Frame` starts/stops on `HoldKind`; `OnSelf` calls `Frame` so the stop is on the packet; `HoldPitch` ×1.6 over 0..1; loop partials 220/440/662 Hz and 8 Hz tick all complete whole cycles in 0.5 s. Tests `TheHoldLoopWrapsWithoutAStepAndItsPitchRisesWithProgress`, `TheHoldLoopStartsWithTheOwnActionAndStopsWhenItEndsOrTheMatchStops`. Exe: see run E row. |
| 3 | ExitsPowered, both roles, first time | PASS | Run A (Survivor) line 720-722 `exits powered reason=hatch` → cue; run D (Killer) 547-548 `reason=terminals` → cue. |
| 4 | ExitOpen, Survivor only | PASS | Run A 727-728 `exit 1 open` → cue; run D (Killer) 572 `exit 0 open` → no cue. Test `TheKillerClientNeverGetsCaptureRescueExitOpenOrSyncCues`. |
| 5 | CollapseStart, both roles, CollapseLeft < 0 → ≥ 0 | PASS | Run A 721-723 (hatch), run D 573-574 (exit). |
| 6 | HatchOpen, both roles, first HatchVisible 0→1 per match only | PASS (code/EditMode) | `hatchSeen` latch; baseline test covers a late-join hatch. Exe: not observed (own Survivor was a spectator out of sight when the hatch opened in run A; Killer never came within 15 m in B/D). |
| 7 | HatchClose only when actually closed, never on leaving sight | PASS, documented limits | Run A 719-724: `hatch closed` → `HatchClose` in the collapse-start snapshot with no exit Open. Tests `AHatchLeavingSightRangeNeverGivesHatchClose` (6 sight toggles, both roles, plus a half-released Killer hold) and the Killer own-hold test in both packet orders. Limits (also in the impl report): a Survivor hears no close while the collapse already runs; same-tick close+exit-open reads as exit. |
| 8 | Capture: another Survivor Normal→Lagging; own capture not doubled; no Killer capture cue | PASS | Run A: own capture line 246 (`player=4`) → no cue; 10 other captures → 10 cues. Run B/D Killer: 10 captures, 0 cues. Test `TheOwnCaptureGivesNoCaptureCueButTheOwnRescueGivesTheRescueCue`. |
| 9 | Rescue: any Survivor (own included) Lagging→Normal | PASS | Run A 8 rescues → 8 cues; run C 8/8. Own rescue covered by the test above. |
| 10 | TerminalKick: Regressing rising edge (Survivor); Killer own completed kick | PASS (code/EditMode) | Per-terminal edge map; Killer path at `HoldProgress >= 0.9`. Tests `EachEdgeTriggers...` (a second kick on the same terminal is a new edge) and `TheKillersCompletedKickIsTerminalKickAndAnEarlyReleaseIsSilent`. Exe: unreachable offline — `KillerBrain` has no kick logic and a human Killer needs Interact input (not allowed). |
| 11 | Track by Survivor Id on a Survivor client only; the Killer never reads Survivor Ids | PASS | Whole Id block under `if (!killer)`; Killer entries carry Id 0 anyway. |
| 12 | Entry disappears and reappears is not a status edge | PASS | `survivors`/`seen` swap each snapshot; test `TwoCapturesInOneSnapshotGiveOneCueAndAnEntryThatReturnsIsNoStatusEdge`. |
| 13 | First WorldState after MatchStart is the baseline; nothing for already-true state | PASS | `known` flag; test `TheBaselineSnapshotTriggersNothingEvenWhenEveryConditionIsAlreadyTrue`. Run logs: no cue at `match start` in any run. |
| 14 | At most one cue per kind per snapshot; no repeat while true | PASS | `snapshotCues` bitmask + edge maps; `AssertOnce` repeats each snapshot ×4. Exe: no duplicate lines anywhere. |
| 15 | Pure `float[] Build(V2Cue, int rate)` at 44100, mono, fixed seeds, deterministic | PASS | `new Random(31 + cue)`; test builds twice and compares. |
| 16 | Peak ≤ 0.95, start/end at exactly 0, lengths 0.08–1.2 s, seamless hold | PASS | Edge fade 4 ms; test 1 and 2 (wrap step ≤ max inner step, < 0.03). |
| 17 | Character per cue | PASS (by code reading) | Matches the brief's list (two-note up, single tone, low buzz down, hum swell, chord, alarm pulses, clunk up/down, glitch, rising arpeggio, crunch). Listening unverified. |
| 18 | Volume: sources `ignoreListenerVolume == false`, `spatialBlend == 0`, no second Volume01 scaling | PASS | `Source()`; `PlayOneShot(clip)` with no volume scale; test compares clip data at 0.3 and 1.0 and checks `source.volume == 1`. |
| 19 | Logging: one `[audio] v2 cue <Cue>` per played cue; `hold start/stop <Kind>` | PASS | `Flush`, `Frame`, `StopHold`. Exe logs show exactly one line per event. |
| 20 | ClientApp hook minimal (≤ 15 lines), reset on MatchStart/EndMatch | PASS | 9 hook lines + 1 `using` (10 changed lines). `Begin` at MatchStart, `End` in `EndMatch`. See finding F1 about `Disconnect`. |
| 21 | No forbidden files touched | PASS | `git status`: only the owned files plus new `.meta`s and the report. |
| 22 | EditMode all green; BuildWindows succeeds | PASS (scratch copy) | QA EditMode run below. BuildWindows: implementer's `build-lfs.log` and the exe used here; QA did not rebuild. |

## Findings (no blocker, no major)

- **F1 minor — `Disconnect()` does not call `v2Audio.End()`** (`ClientApp.cs` 620-628; `EndMatch` 313 does). Repro: leave a
  match mid-hold or on a dropped link. Expected: the rules go inactive like on EndMatch. Actual: `V2CueRules.Active` stays
  true until the next `Begin`. Effect: none audible — the hold loop stops on the next `Frame(…, false)`, and no WorldState/
  SelfState/SyncResult reaches the rules while `playing == false`. Consistency only.
- **F2 minor (accepted DEVIATION, documented) — Survivor HatchClose coverage.** A hatch closed while the collapse already runs
  (an exit opened first) is silent for Survivors; a close in the same tick as a switch completing reads as exit-driven.
  The server gives Survivors no other observable signal; the brief allowed "whatever the rules make reliable".
- **F3 minor (documented) — Killer own-hold inference from predicted progress.** A kick cancelled (moved/released) in its
  last 10 % (200 ms of 2 s) plays `TerminalKick`. The HatchClose arm additionally needs the hatch to leave the snapshot within
  0.5 s, which walking cannot do (15 m sight vs 4.6 m/s), so a false HatchClose is practically impossible.
- **F4 minor — summed peak of simultaneous cues.** A hatch close yields ExitsPowered + CollapseStart + HatchClose in one
  snapshot (run A 722-724). Each clip is ≤ 0.95 alone; `PlayOneShot` mixes them, so the sum can exceed the per-cue bound and
  is clamped by Unity's output. The brief bounds each cue only; cosmetic.
- **F5 minor (note for the design owner) — Rescue also plays for `emergency restore`** (skill, Rules: `Skills.cs` 119),
  since it is a Lagging→Normal edge (impl run 2 lines 355-359). Matches the spec's literal edge.
- **F6 minor (note) — spectators keep the cues.** After the own Survivor is Eliminated/Escaped it still hears Capture/Rescue/
  ExitOpen/HatchClose (run A after t=162). The brief says "Survivor"; acceptable, not a spec breach.
- **F7 minor — test gaps.** No test for the Killer's direct HatchClose→Kick transition (the server can send it) and none for
  the three-cue hatch-close snapshot. Both paths were read and the latter was observed in run A.

Bug hunt with nothing found: cue at match start (none in 5 runs + baseline test), late join (baseline test), cues after
GameResult (`End()` in `EndMatch`, `playing` gate on every feed), stuck hold loop (server clears `SwitchExit` on capture; `Frame`
stops outside a match; `End` stops), double cue on one event (bitmask + edge maps; no duplicate lines in any log),
NaN (`ActionProgress` NaN → 0; `HoldPitch` NaN → base; `CollapseLeft` NaN compares false), unknown ActionKind byte → None.

## EditMode (scratch copy, QA run)

`Unity.exe -batchmode -projectPath <scratch>\Client -runTests -testPlatform EditMode` → `docs/qa/Q4-v2-sounds-runs/qa-editmode.xml`,
log `qa-editmode.log`: total 1402, **passed 1401, failed 0**, skipped 1 (pre-existing ignored), 25.5 s. `V2SoundTests`: 16/16 passed.

## Exe runs (scratch exe, flags only; logs in `docs/qa/Q4-v2-sounds-runs/`)

| Run | Flags | Cues (count) | Server match | Not seen |
|-----|-------|--------------|--------------|----------|
| A `qa-run-a-survivor-switch.log` | survivor, 7 bots, `-ptk-dev-hack 600 -ptk-dev-switch -ptk-auto-sync miss`, 600 s | Capture 10, Rescue 8, SyncFail 4, ExitsPowered 1, CollapseStart 1, HatchClose 1, ExitOpen 1 | each cue follows its `[offline server]` line (captures/rescues of players ≠ 4; `sync player=4 result=Fail` ×4; 719-721 `hatch closed` / `exits powered reason=hatch` / `collapse start reason=hatch`; 727 `exit 1 open`). Own capture 246 silent; own eliminated at t=162 (never reached a switch). | hold loop, HatchOpen, TerminalKick |
| B `qa-run-b-killer-chase.log` | killer, 7 bots, `-ptk-chase 590 -ptk-killer-look nearest`, 600 s | none | 6 captures, 0 cues (correct); no power/exit/hatch events in 600 s | — |
| C `qa-run-c-survivor-idle.log` | survivor, 7 bots, idle, 600 s | Capture 11, Rescue 8 | all matched; own never captured; 2/5 terminals | — |
| D `qa-run-d-killer-idle.log` | killer, 7 bots, idle, 600 s | ExitsPowered 1, CollapseStart 1 | 547 `exits powered reason=terminals`, 573 `collapse start reason=exit`; 572 `exit 0 open` → **no ExitOpen on the Killer**; captures 579/584/585 → no cue | HatchOpen, Killer HatchClose/TerminalKick (need Interact input) |
| E `qa-run-e-survivor-switch-only.log` | survivor, 7 bots, `-ptk-dev-switch` only (idle until powered), 600 s | Capture 6, Rescue 5 | all matched (one Rescue after 92 `Normal reason=emergency restore`, see F5); exits never powered (1/5 terminals in 600 s), so the switch hold never happened | hold loop (the bot Killer keeps bot hackers below 5 terminals; unreachable without input) |

Cues observed in a real exe across QA + implementer runs: SyncGreat, SyncGood, SyncFail, ExitsPowered, ExitOpen, CollapseStart,
HatchClose, Capture, Rescue (9/11). Unobserved: HatchOpen, TerminalKick; and the Killer's own-hold HatchClose path.

## Unverified

- Real listening (character, loudness balance, click-free wrap) — never verifiable by QA here.
- The hold loop, HatchOpen and TerminalKick in a real exe (EditMode only). The Survivor switch hold needs 5 terminals done while the own Survivor stays alive; in 5 runs of 600 s with a bot Killer that never happened. TerminalKick needs a Killer that kicks: the bot Killer never kicks,
  and a human Killer would need desktop input.
- The Killer's own-hold HatchClose/TerminalKick path under real network latency (prediction tolerance is 10 % of the hold).
- BuildWindows was not re-run by QA (implementer's build used; its sources match the worktree).
