# U6d-bots QA review, attempt 2

Reviewer: Claude Sonnet (fallback for Gemini Flash, auth expired)
Unit: U6d-bots (bot AI v2), attempt 2. Implementers: Codex gpt-6.1-sol, then Claude Opus (quota switch).
Method: static reading of the review pack in `docs/qa/U6d-review-a2/` plus read-only lookups in `PtK-work/u4h`. No builds or tests were run by me. Line numbers refer to `src/*.cs.txt` in the pack (`SB` = SurvivorBrain, `KB` = KillerBrain). Log lines refer to `balance-sweep.log.txt` (`log`).

## 1. Spec conformance (brief bullets 1-14)

| # | Spec | Status | Evidence |
|---|---|---|---|
| 1 | Survivors spread, cap 2, progress then distance, no 1-worker cap or ramp | PASS | SB:54 (`TerminalCap = 2`), SB:604-611 (Occupants, lower Id wins), SB:629-646 (score `-Progress*200 + dist`), test `EveryNormalSurvivorWorks...` |
| 2 | Survivors hide: cover, stop, Silent; resume when calm; keep camp rule | DEVIATION (declared, accepted, see 3b) | SB:253-307; hide-in-place beyond 18 m (SB:281-298), Threat 2 unseen freezes (SB:246) |
| 3 | Rescue split, timer-aware, gates relax | PASS | SB:576-602 (nearest Normal only, `arrival` vs `StatusTimer`, `urgent` relaxes gate), SB:268-273; tests `RescueUsesArrivalHoldAndUrgency`, `OnlyNearestEligible...` |
| 4 | End game: switch duty, everyone to an open exit, rescue only if close and collapse allows, hatch kept | PASS (with defect M1) | SB:176-187, SB:196-215, SB:456-492, SB:663-671 (Lagging ally drains at 2x), SB:188-194 |
| 5 | Skills: Traffic Overload when chased, Stealth Relay while hacking at Threat >= 1 | PASS | SB:720-722, SB:736-738; test `SurvivorUsesChaseAndHackSkills` |
| 6 | Killer predictive chase, max 1.5 s, then search | PASS | KB:146-167 (velocity from anonymous blips), KB:75-81, KB:94-100, KB:104; test `PredictiveAim...` |
| 7 | Killer patrol ranking by recent Signal then distance; 2 s kick; skip Lagging | PASS | KB:228-236, KB:184-209, KB:70, KB:136; tests `PatrolRanks...`, `KillerHoldsKick...`, `KillerDoesNotKick...` |
| 8 | Killer end game: guard the exit with highest SwitchProgress; Firewall; close hatch | PASS (see m1: open exit still wins the guard choice) | KB:221-224, KB:237, KB:257-263, KB:323-330, KB:82-93 |
| 9 | Killer skills Ping/Overclock on chases | PASS | KB:313-322 |
| 10 | Phantom pacing follows survivor pacing | PASS | `GameServer.Bots.cs` consts reference `SurvivorBrain.*` (code.diff 19-24); white-noise mix 2-4 (code.diff 47-56); AUC and KS below |
| 11 | Information fairness | PASS | section 2 |
| 12 | Restored bounds | PASS | MatchLength <= 4 (measured 4 of 15, exactly at limit); AUC gated 0.716 < 0.85; KS lowest p 0.59 >= 0.02 (tests.diff 96, 115, 233) |
| 13 | Balance bands gated (win, median, time-ups) incl. idle-killer exit share | PASS | tests.diff 37-40, 51-60, 268-271; section 4 |
| 14 | Do not touch rules, protocol, Shared, client, sync judge | PASS with note | Only `BotViews.cs` (`CollapseLeft`), `GameServer.Bots.cs`, `GameServer.Phantoms.cs` touched. The Phantoms edit is outside the owned-file list and the impl report does not mention it (m5). |

## 2. Fairness

Fields read by `SurvivorBrain`: Time, SelfId, X, Z, Speed, Status, Skill, SkillReady, Threat, CollapseLeft, KillerVisible/X/Z/Yaw, Team (Id, Status, Flags of self, X, Z, StatusTimer), Terminals (Progress, X, Z), Exits (Powered, Open, Progress, X, Z, SwitchX, SwitchZ), Hatch. All are in WorldState or SelfState for a human survivor. `CollapseLeft` is built from the bot's own timer (`GameServer.Bots.cs` code.diff 32), matching SelfState. `KillerInSightOf` is plain line of sight (`GameServer.Visibility.cs:152`), the same as the human's view.

Fields read by `KillerBrain`: Time, X, Z, Yaw, Speed, Skill, SkillReady, Blips (X, Z, Lagging), Terminals (X, Z, Done), TerminalCount, Exits (Powered, Open, Progress, SwitchX, SwitchZ), Hatch. No terminal progress.

Terminal progress in the Killer brain: none. Patrol ranking (KB:228-236) uses `Done`, distance and `_signalAt` (blips within 6 m, 60 s). The kick (KB:184-209) uses `Done`, `_kickedAt` and blips. PASS.

Pre-existing, not new in this unit: the Killer's difficulty knobs scale on `TerminalCount` and use random miss chances. A human has no such handicap. Not flagged.

## 3. Fix list F1-F7 and attempt-1 findings

| Fix | Status | Evidence and covering test |
|---|---|---|
| F1 Killer never without destination | PASS (code), symptom persists, see M1 | KB:239-254 (sweep fallback, Signal bonus, skip underfoot), KB:264-269 (far spot when nothing left). Tests `KillerKeepsPatrollingWhenAllWorkWasRecentlyVisitedOrDone` (travel >= 15 m, still <= 4.5 s), `KillerSweepRevisits...`, `KillerDoesNotKickATerminalAgain...`. The guard and powered-exit path is not covered. |
| F2 visible still Killer no longer freezes survivors | PASS | SB:165, SB:253-267, SB:618-620 (`visibleCamp`: avoid terminals in the camper's sight). Test `VisibleCampReleasesOccludedWorkWithinFortySecondsAndMovementRestoresHiding` asserts every hack frame is occluded and that movement restores hiding. |
| F3 speed clamp | PASS | SB:666-667 (also SB:586, 659). Test `EscapeRescueClampsNonPositiveSpeed` (0 and -4). |
| F4 stickiness +15 | PASS | SB:642-644. Test `TerminalStickinessRequiresAtLeastFifteenMoreProgress` (45 stays, 55 and 60 move). |
| F5 rescue in the end game | PASS (code), residual signature, see m2 | Cause: switch duty returned before rescue (SB:196-203 now rescues first), and the camp gate (SB:589). Test `PoweredSwitchHolderRescuesReachableAllyBeforeOpeningExit`. |
| F6 balance margins | PASS | 1v7 win 58/55 % (>= 40), 1v1 win 25/35 % (>= 20). |
| F7 detour around a visible Killer | PASS | SB:362-376 (`EscapeExit` penalises exits whose path passes within `ThreatLevel3Distance`), SB:379-419 (clearance `CaptureRadius+2`, ring waypoints, falls back to going anyway). Tests `EscapeDetoursAround...` (both variants, never within `CaptureRadius+1`) and `CampingKillerDoesNotSuppress...`. |

Attempt-1 findings: F1 major (code fixed, but see M1); finding 2 (unguarded speed division) fixed; finding 3 (fragile win rates) improved; finding 4 (no stickiness) fixed.

### 3b. Declared deviations

- **Hide in place beyond 18 m (SB:281-298).** Reasonable. The bot is Silent and a Silent survivor is omitted from the Killer's view, so there is no fairness issue. It switches to cover once the Killer comes within `ThreatLevel1Distance` (SB:281). It does mean the bot starts moving, and so becomes visible, at exactly the moment the Killer is 18 m away. Acceptable under spec 2.
- **Unseen Threat 2 freezes, flee only at Threat 3 (SB:246).** Reasonable: no direction is known and a still bot is Silent.
- **Can a bot freeze for a long time? Camp release path traced.**
  - Idle Killer in view: `_killerMovedAt` is set at first sighting (SB:148). `Acute` (SB:165) turns false after 20 s, so `danger` (SB:253) is false. The calm timer ends 2-4 s later (SB:276). Then `_hiding` clears (SB:308), the bot picks a terminal the Killer cannot see (SB:618-620), or waits 25-50 s at a hideout (SB:331-341). Release takes about 20-24 s. Fine.
  - Unseen Killer standing at Threat 2: `_threatRoseAt` goes stale after 20 s. Same release. Fine.
  - Still frozen for minutes: a Killer that stays visible and keeps moving, or an unseen Killer that oscillates across the Threat 1/2 boundary (each rise resets `_threatRoseAt`, SB:134). This is defensible behaviour, but it feeds M1.
  - Gap: if every unfinished terminal is in an idle Killer's line of sight, the bot never hacks (SB:620 applies on both passes) and loops through hideouts (see m3).
- **`ReconnectAndBotTests` wait 20 s to 60 s (tests.diff 119-134).** Justified: the bot sees the idle killer, hides in place, and is released after about 20 s. A human Killer standing still will see no survivor for up to a minute. Minor.
- **Replaced test `KillerFallbackPrefersRecentSignal...`.** The replacement `KillerSweepRevisits...` is stronger: it covers the Signal bonus, the oldest-first order and the underfoot skip.

### 3c. Behaviour bugs

- **M1 (below)**: lone-survivor switch deadlock and 1v1 stalemates.
- **Killer kick loop**: none. `_kickedAt` plus `Revisit` (KB:172-173, KB:184, KB:208) prevents re-kicks. The blip-in-sight branch (KB:186-200) leaves without setting `_kickedAt`, which is bounded by `_visitedAt`.
- **Killer with no destination**: none found in static reading. The `TerminalCount == 0` case is covered by the far-spot branch (KB:264-269).
- **Thrash**: the Interact cancel at SB:124-128 resets `_thinkAt` every tick, which re-thinks each tick (CPU only). `ChooseTerminal` then drops the full terminal. Not harmful.
- **Rescue dead end**: with an open exit, rescue is limited to `arrive < 4` s (SB:669). This is by spec ("if close"). Far Lagging allies are left to the collapse.

## 4. Balance evidence (`balance-sweep.log.txt`)

| Config (set) | Win | Median | Time-ups | Band | Verdict |
|---|---|---|---|---|---|
| 1v7 (1-40) | 58 % (log 168) | 553 s | 3 % | win 35-65, median 540-660, time-ups <= 10 | PASS. Median 13 s (2.4 %) above the floor: FRAGILE |
| 1v7 (1001-1040) | 55 % (log 121) | 544 s | 3 % | same | PASS. Median 4 s (0.7 %) above the floor: FRAGILE |
| 1v3 (1-40) | 45 % | 515 s | 0 % | win 25-75, median <= 660, time-ups <= 20 | PASS |
| 1v3 (1001-1040) | 45 % | 475 s | 0 % | same | PASS |
| 1v1 (1-40) | 25 % | 689 s | 18 % | win 15-85, time-ups <= 30 | PASS. Win is 10 points above the floor, not fragile |
| 1v1 (1001-1040) | 35 % | 783 s | 18 % | same | PASS |
| idle Killer + 7 | 100 % both sets | 214 s | 0 % | win >= 90, exits > collapse | PASS: 280/280 and 279/280 escapes via exits, collapse losses 0; end reason AllResolved 40 of 40 |

Other margins:
- MatchLength is exactly at its limit (4 of 15, limit <= 4).
- Gated AUC 0.716 and KS p 0.59 are comfortable.
- All gates are on fixed seeds, so they are deterministic, but any change to `GameServer.cs`, `GameServer.Terminals.cs` (unit U6g) or other bot constants can move the 1v7 median below 540 s.

## 5. Tests

- The new `BotV2Tests` and `BotV2A2Tests` call the real `SurvivorBrain.Update` and `KillerBrain.Update` with `SurvivorBotView` and `KillerBotView`, the real `FixedMap` and `NavGrid`. Some read private fields by reflection (`_terminal`, `_hiding`, `_patrol`). That is white-box but not a shortcut.
- One weak assertion: in `PredictiveAim...` the last check (`_targetX` stays equal after `_hasTarget` is false) holds trivially, because the code never recomputes the target then (KB:75-81).
- `EveryNormalSurvivorWorks...` counts hackers by the brain's `_terminal`, not by position, so it is partly self-referential. It does exercise the cap code path.
- **Changed old assertions:**
  - `SurvivorCampTests.AnIdleKillerInSightNearTheLastTerminalDoesNotFreezeTheBot` was inverted. It now asserts the bot does not hack for 5 s, and then needs the Killer to become unseen. This is a real weakening of the original guarantee ("an idle Killer in plain sight next to the last terminal does not freeze the bot"). It is justified by F2 only for terminals the Killer cannot see (the new F2 test covers that). The visible-terminal case is no longer tested and is now blocked by design (m3).
  - `BotMatchTests` `Assert.Empty(EscapedIds)` became `Count < 4` (half-escape rule). Justified, though loose for non-7-survivor configs.
  - `SyncTimingTests` seeds 11-25 became 11-50 to keep >= 150 samples. Justified; it also removed the old reference counts from the comment.
  - `ReconnectAndBotTests` 20 s to 60 s. See 3b.
  - Bounds tightened (MatchLength, AUC, KS 0.02): stronger, as required.

## 6. Findings

### M1 (major): 1v1 stalemates, zero interaction, on both seed sets

- **Evidence:** `log` lines 79, 85, 92, 104, 108, 109, 110: seeds 9, 15, 22, 34, 38, 39, 40 (1-40 set) are 900.0 s `TimeUp` with `cap 0`. Seeds 15 and 38 have 3/3 terminals done and powered exits but no escape and no capture in 900 s. The 1001 set also reports "time 7" (log 67). This is the same signature as the F1 evidence (seed 26: 900 s, 0 captures). The implementer report mentions only "time-ups at 18 % against a 30 % limit" and does not diagnose them.
- **Likely mechanism (static reading, not reproduced):**
  - `SwitchDuty` (SB:466-487): for a lone Normal survivor, round 0 already returns itself as holder of the nearest equal-progress switch (SB:487) before any later round. The stall fallback (SB:466, "other powered switch gets its own holder") therefore never sends a lone survivor to the other switch. The test `AStalledLeadingSwitchSendsTheNextNearestSurvivor...` covers two survivors only.
  - The Killer guards the highest-progress powered exit, which defaults to exit 0 when progress is equal (KB:221-224, KB:237) and circles within 3 m of that switch (KB:271-281). The survivor treats the Killer as `cautious` within 10 m and moving (SB:175), so it hides instead of holding (SB:199-215), and neither side can progress.
  - For the 2/3-terminal cases a Killer repeatedly sweeping the last unfinished terminal (KB:239-254) and kicking it plus survivors hiding while it moves in view (SB:253) is a similar mutual lock.
- **Expected:** a bot match must not run 900 s with no capture and no progress; F1 asked that the Killer never idle and F2 that survivors not freeze. **Actual:** about 17.5 % of 1v1 matches on each set do. For a human Killer against a bot survivor this is visible as a survivor that never finishes.
- **Suggested fix:** let `SwitchDuty` pick, for a lone survivor after the stall time, the powered switch farthest from the Killer's last seen position (or the other switch), and add a test with 1 survivor and 2 powered switches; trace one of seeds 15 or 38 to confirm.

### Minor findings

- **m1 Killer guards an already open exit.** KB:221-224 picks the powered exit with the highest `Progress`. An open exit reports `Progress 100` (`GameServer.Exits.cs:166`) and keeps the Powered flag, so the Killer keeps guarding the switch of the exit that is already open, and limits terminal patrol to 18 m around it (KB:232), instead of the other unopened switch or the open door. Expected: skip `Open` exits. Actual: spec-literal but pointless after the first opening.
- **m2 F5 signature remains in the sweep.** 1v3 (1-40): seeds 2, 4, 15, 18, 39 have `cap 3 res 0 lagTimeout 3` with 4/4 terminals (log 18, 20, 31, 34, 55). The unit test passes, but the implementer gives no seed trace showing these are Killer chains rather than a remaining rescue gap. The win band is still met.
- **m3 Idle Killer with every unfinished terminal in sight.** SB:620 refuses all terminals in the camper's line of sight on both passes. If all remaining work is visible to an idle Killer, survivors never finish and the match runs to the time limit. This is more likely in 1v1 and 1v3. The old test that guarded against this was inverted (section 5). The idle-killer sweep (100 %) does not cover it, because the idle Killer sits at the centre.
- **m4 Fragile margins.** 1v7 median 544 s against the 540 s floor (1001 set) and 553 s (1-40 set); MatchLength exactly 4 of 15. See section 4.
- **m5 Reporting gap.** `GameServer.Phantoms.cs` is outside the owned-file list and is not named in the impl report (spec 14 says to state why). The diff also adds a trailing-newline change.
- **m6 Unverified cost.** `PlanEscape` runs up to 48 ring candidates with two A* searches each (SB:404-416) per think (every 0.25-0.6 s) per bot while a Killer is visible and the direct route is unsafe. I could not measure it; the sweeps run fast, so offline/Unity main-thread cost should be checked in the live check. `Tools/Harness.Tests` and live play were not run (impl report).

## Verdict

Spec and fairness are met, F1-F7 are implemented with tests, and all balance bands pass. One major remains: M1, the persistent 1v1 stalemates (same signature as the F1 evidence), with a concrete suspected cause in `SwitchDuty`.

VERDICT: FAIL (0 blockers, 1 majors)
