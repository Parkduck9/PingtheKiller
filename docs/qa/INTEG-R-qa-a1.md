# INTEG-R QA report, attempt 1 (Fable; implementer Claude Opus)

Branch `claude/integ-1117b` at 83a6c9e (base main 2213343). Review only: no product code edited, nothing committed.
Evidence: `docs/qa/INTEG-R-qa/` (harness logs, EditMode xml/log, live logs and shots).

**Verdict: PASS.** No blocker, no major. Seven minors and three notes. SLOP Section 9: about 90/100, no gate failure.

## 1. Merge correctness: PASS

- Read `b639213` (R3) and `b7f041c` (R1) with `git show --cc`, and diffed every line each unit added (`2213343..1dc6259`, `..b15c68f`, `..092e73b`) against HEAD. Every line missing from HEAD is an intended change: the `restore` to `rescue` rename, the retired sync check, the passive afterimage, R1's `KillerEffect` replacing R3's stub, the swing numbers, the R1 M1/M4 edits.
- Kept from each side: R2 route reset (`Route.Reset`, `routeKeys.Reset`, `ClearRoute` on status change and on `LockTerminal`), R3 intro card, ally markers and hints, R1 `Swing()` in place of `Capture()`, R1 PlayMenu copy, both bot intents (`HackKey`, `Attack`), ScenarioCheck with route counters and the attack sequence (harness H6 swings and routes, see gates).
- Clamps: `ActionKind` 7, `PlayerFlags` 63, `ChaseFlags` 31, `TerminalFlags` 7, `RouteDirection` 3. Exhaustive range tests exist (`ProtocolV02Tests` rows for Flags 0-63 and Chase 0-31, `QaU6aProtocolTests.WorldStateTerminalFlagsAboveSevenAreRejected`, `ProtocolV08Tests`).
- No conflict markers; no reference left to `SyncLockBar`, `C2S_SyncPress` or `PacketAfterimage`.
- `KillerViewTests.AfterimageTailIsTwentyTicks...` was deleted by R1 with the feature. Correct.

## 2. Architecture rules: PASS

- Killer view: `AddKillerEntry` still writes only Status, position and `Flags = Signal`; a Silent Survivor adds no entry. `GettingUp` is set only in the Survivor view (`ExplicitRescueTests.TheGettingUpFlagIsOnTheWireToSurvivorsAndNeverInTheKillerView`). Harness H3 and H6 leak scans pass three times.
- `SkillChoice`: `LobbyFor(recipient)` sends `0xFF` for every slot but the recipient's own; every lobby send goes through `BroadcastLobby` (`KillerSwingTests.EachLobbyRecipientReceivesOnlyItsOwnSkillChoice`).
- New inputs are bounded: `C2S_Attack` stale Seq dropped, 5 per second, Killer only, match only; `C2S_ChooseSkill` validated by the reader, lobby only, 4 per second; `C2S_HackInput` direction clamped, stale or unknown route dropped, 12 per second, only while hacking that route's terminal.
- Get-up: the move direction is zeroed and does not make the Survivor Signal; `SetInteract` and the skill are refused. `SetStatus` clears `Interacting`, so a hold from before the capture cannot hack during the get-up.
- By design (R1 brief S4), Firewall block tells the Killer which terminal has the most progress, and its refusal tells it that none has any. The Killer otherwise sees terminal progress only as 0 or 100. Recorded here so it is a known choice, not a finding.

## 3. Cross-unit behaviour: PASS

| Rule | Code | Test |
|---|---|---|
| E priority: rescue, chase prop, exit switch, terminal | `Rescue()` runs before `Hack()`; `StartChase` returns when `NearestLaggingAlly != null`; `Hack()` skips `Rescuing` and takes a switch before a terminal | `IntegRCrossUnitTests.AnEPressNearALaggingAlly...`, `ExplicitRescueTests.RescueWinsTheEHoldOverATerminal...`, `...RescueWinsAFreshPressOverAShutterInReach` |
| Get-up blocks movement, hacking, routing, skills | `GettingUp()` in Simulation, `SetInteract`, Skills; no hack so no route | `IntegRCrossUnitTests.AGettingUpSurvivor...`, `ExplicitRescueTests` (4 tests) |
| Swing respects invulnerability and get-up | `SwingTarget` skips `Active(InvulnerableUntil)`; 1.5 s get-up is inside the 2 s protection | `IntegRCrossUnitTests.TheSwingCannotHit...`, `KillerSwingTests.InvulnerableAndLagging...` |
| Locked terminal refuses hacking and clears the route | `Hack()` skips a locked nearest terminal; `LockTerminal` and `UpdateRoutes` clear the route; `HackInput` needs `Hacking` | `IntegRCrossUnitTests.AFirewallLockedTerminal...` |
| No hit through a lowered shutter | `SwingBlocked`: walls plus every `Down` shutter box | `KillerSwingTests.AWallOrADownShutterBetweenThemMakesItAMiss`; live, see section 8 |

## 4. Protocol v0.8: PASS

`docs/PROTOCOL.md` matches the code: ids 0x0017, 0x0018, 0x0019; 0x0011-0x0013 retired and unknown to the codec; `Swinging 16`, `Locked 4`, `GettingUp 32`, `Rescue 7`; SelfState 29 bytes with the four route bytes; the lobby slot `SkillChoice` byte and its masking. `ProtocolV08Tests` has five golden frames, the version test (reads the doc's first line) and the retired-id test. Two doc nits: finding 5.

## 5. Copy and onboarding: PASS with minors

- One rescue term in prompts, hints, How to play, the downed line and the menu notes. Exception: finding 4.
- The controls legends match the real bindings in `ClientApp.cs` (WASD, hold E, Q, Esc, left click or right trigger, WASD and arrows while routing).
- Orphan confirmed: finding 1.
- **The red marks in `survivor/r3-getting-up.png` are not a defect.** They are the existing threat meter strip (`SurvivorHud.threatStrip`, `.sthreat-strip`, 8 u high at the top right of the meter), drawn at threat level 1 or 2. The log shows `threat visual level=1` at t=160.65 and the shot at t=161.28. `ThreatVisual` is unchanged from main. It reads as debris only because the `Protected 2.0 s` tag sits right under it; the same strip is in my `r3-rescue-prompt.png`.

## 6. Balance: targets met; "clearly stronger" only partly shown

Survivor win %, re-run by me:

| Setup | main, seeds 1-40 / 1001-1040 | HEAD, same seeds | main, 240 seeds 5001-5240 | HEAD, 5001-5240 | HEAD, 3001-3240 |
|---|---|---|---|---|---|
| 1v7 | 63 / 53 | **50 / 53** | 56 | 50 | 50 |
| 1v3 | 55 / 60 | **53 / 48** | 66 | 55 | 50 |
| 1v1 | 40 / 40 | **33 / 43** | 41 | 38 | 28 |

- The reported numbers are confirmed exactly (50/53, 53/48, 33/43; wide sample 50/50/28 on the implementer's seeds 3001-3240). Medians 602 / 574 s. All inside the targets, none on an edge. `Category=BalanceSweep`: 18/18.
- Against main the Killer is clearly stronger at 1v3 (66 to 55 over 240 seeds; captures per match 2.85 to 3.54).
- At 1v7 the gain is small: 56 to 50 over 240 fresh seeds (about 1.3 standard errors), the second fixed set is unchanged at 53, and captures per match are flat (9.90 to 9.99). At 1v1 it is flat on my seeds. See note A.
- 40-seed blocks swing between 40 % and 58 % at 1v7, so the two fixed sets alone cannot show a 5-point change.
- `SwingRange` is 2.75 m: +6 % on R1's 2.6 m (inside the INTEG lever), +25 % on the R1 brief's 2.2 m.

## 7. Gates (all re-run on 83a6c9e)

| Gate | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors |
| Server.Tests `Category!=BalanceSweep` | 1147/1147, six full runs (one idle, two during the Unity EditMode run, three under full CPU load) |
| Server.Tests `Category=BalanceSweep` | 18/18 |
| Harness 3x, fresh servers, 7795 / 7796, `--threshold 100 --expect-los` | 100/100, 100/100, 100/100 (`INTEG-R-qa/harness-{1,2,3}.{log,json}`) |
| Unity EditMode, absolute `-testResults` | 1603 passed, 0 failed, 2 skipped (both `[Explicit]` diagnostics in `U10MateHackQaTests`) |
| Harness.Tests | not run (no change suspected in `Tools/` beyond ScenarioCheck, which the harness exercises) |

- After the Unity run one file changed only in line endings (`Client/Assets/Resources/DOTweenSettings.asset`); reverted. The tree is clean except `docs/qa/INTEG-R-qa/` and this report.
- **The two failing tests.** I could not reproduce a failure in six full runs plus eight targeted runs of the two tests below. The implementer's own evidence names them, though: `docs/qa/INTEG-R/tuned-server-tests.log` (18:36) shows
  - `RttProbeTests.RealTcpLoopbackEchoesProduceASmallPositiveServerTimedRtt`: `loopback RTT 0.057 s` against a 0.05 s limit. A wall-clock test, older than this unit, already flagged in `Q3-skill-reject-qa-a1.md`. It also failed in `r3merge-server-tests.log` and `r1merge-server-tests.log`.
  - `KillerSwingTests.TheBotKillerSwingsOnlyAtANormalBlipCloseInFrontAndAimsAtIt` (line 531): a stale distance during the balance tuning, fixed in 605252a one minute later. Deterministic; passes now.
  - Whether the unlogged run after the Unity build failed these same two is not provable. The first is the likely repeat offender.

## 8. Live runs (1280x720, offline, 7 bots, 300 s each, no input sent)

The exe was used. All `.cs` sources are older than the built `PingTheKiller.Client.dll` (19:21:36). Nineteen asset files carry 19:21:46-19:22:00 timestamps from the line-ending revert after the build; their content equals HEAD, and the two later commits are docs only. See note C.

- **Survivor, `-ptk-rescue`** (`live/survivor-rescue.log`, 22 shots, 0 exceptions). Both missing shots captured:
  - `r3-rescue-prompt.png`: `Hold [E] to rescue Bot 6 — you will be Signal`, readable, no tag over the own head.
  - `r3-restoring.png`: `Rescuing Bot 6 — you are Signal` with the ally bar at about a third.
  - `r3-ally-tag.png`: `Bot 6 · Lagging · 42 m` plus the hint `Bot 6 is Lagging · hold [E] next to them to rescue`.
- **Killer, `-ptk-chase 290`** (`live/killer-chase.log`, 27 shots, 0 exceptions). 21 swings: server 5 hit, 15 miss, 1 interrupted by a shutter stun; the client showed 5 hits and 16 `Missed` (the interrupt reads as a miss). The eliminating hit was reached and shown (`r1-killer-elim-hit.png`). The Killer hint showed at t=8.87.
  - **Lowered shutter captured:** `chase-shutter-down.png`. At t=235.70 the Killer pressed at a blip 2.97 m away; Survivor 5 pulled shutter 2 down during the wind-up; the server logged `result=miss`. The shot shows the Survivor just behind the 1.2 m panel (its top bar is at the bottom edge of the frame) with `Click  Attack` still up: finding 3.

Shots I looked at: implementer's `killer/play-1s`, `killer/lobby`, `killer/r1-killer-miss`, `survivor/play-1s`, `survivor/r3-getting-up` (cropped 5x), `survivor/r3-restoring`, `survivor/howto-survivor`, `survivor/howto-killer`, `survivor-lobby/lobby`; mine `survivor-rescue/r3-rescue-prompt`, `r3-restoring`, `r3-ally-tag`, `killer-chase/play-1s`, `chase-shutter-down` (cropped), `r1-killer-elim-hit`.

## Findings

1. **minor, copy/layout.** The Killer intro card's controls legend wraps and leaves `Esc Menu` alone on line 2.
   - Repro: start as Killer, look at the card in the first 7 s. Evidence: `INTEG-R/killer/play-1s.png`, `INTEG-R-qa/live/killer-chase/play-1s.png`.
   - Expected: no lone tail (the brief fixed the same fault in the lobby picker). The Survivor legend wraps evenly.
2. **minor, behaviour.** Esc does not dismiss the intro card.
   - `ClientApp.cs` ~455-470: Esc first opens the play menu, then `intro.Visible(now, menuGate.Open)` is false, so `Dismiss()` never runs. Closing the menu inside the 7 s brings the card back.
   - Expected (brief item 5, #4): Esc ends the card. Found by reading; not exercised live (no input allowed). `IntroCardState.DismissInput` is tested, the ordering in `ClientApp` is not.
3. **minor, prompt.** `Click  Attack` shows for any Normal blip within 3 m, also when a lowered shutter or a wall makes the swing miss.
   - Repro and evidence: section 8, `chase-shutter-down.png`, log t=235.70-236.62 (the prompt turns into `Hold [E] to force the Shutter` only inside the break radius).
   - Expected: the prompt offers an action that can land. `KillerWorldLayer.cs:250`, `KillerHudCopy.Attack`.
4. **minor, copy.** The Emergency restore result lines still use the verb: `Restored N · no longer Lagging`, `N restored you`, `N restored N` (`SkillFeedback.cs` 330-337, HUD_SPEC 14.2.8), and `ally` remains in the downed line and the How to play page. GDD section 10 keeps only the skill name as an exception. R3 QA minor 2 is therefore half done.
5. **minor, docs.** `docs/PROTOCOL.md`, S2C_SkillRejected: reason 7 is still described as `Passive (Packet afterimage)` and reason 6 as `(Firewall block)` only. Reason 7 is never sent now; reason 6 now means no switch in progress and no terminal with progress.
6. **minor, report accuracy.** The impl report says the two failing test names were not captured; they are in its own `tuned-server-tests.log` (section 7). The RTT loopback test's 0.05 s limit will keep failing gates under load.
7. **minor, tests.** `ProtocolV08Tests.TheNewValuesAreTheClampEdges` says it rejects one past each of five maxima but asserts two (ActionKind 8, Direction 4). The other three are covered elsewhere, so coverage is intact; the comment overstates.

## Notes for the orchestrator

- **A. Balance versus the user's wish.** The numeric targets are met. The 1v7 band (45-55 %) leaves little room below main's 56 %, and the result sits mid-band at 50 %. If "clearly stronger" must show at 1v7 in bot numbers, the target band has to move down; that is a user decision, not an implementer fault.
- **B. Carried over, not in the INTEG scope:** R1 M2 and M3 (late `Missed`, `LateHit` only logged).
- **C. Exe freshness** rests on content, not on timestamps (section 8). A strict mtime check would call it stale.
