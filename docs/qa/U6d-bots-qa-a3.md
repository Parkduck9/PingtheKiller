# U6d-bots QA review, attempt 3 (final)

Reviewer: Claude Sonnet (fallback for Gemini Flash, auth expired)
Unit: U6d-bots (bot AI v2), attempt 3. Implementer: Claude Opus (Codex out of credits).
Method: static reading of the pack in `docs/qa/U6d-review-a3/` plus read-only lookups in `PtK-work/u4h` (`GameServer.Visibility.cs`, `GameServer.Exits.cs`). I ran no builds, tests or games. `SB` = `src/SurvivorBrain.cs.txt`, `KB` = `src/KillerBrain.cs.txt`, `log` = `balance-sweep.log.txt`, `td` = `tests.diff.txt`. The AUC, KS p, MatchLength and non-sweep numbers come from the implementer report and the orchestrator's runs. They are not in my pack, so I could not re-check them.

## 1. Spec conformance (a1 brief, bullets 1-14)

| # | Spec | Status | Evidence |
|---|---|---|---|
| 1 | Spread, cap 2, progress then distance, no ramp | PASS | SB:56 `TerminalCap=2`; SB:939-946 `Occupants`; SB:964-981 score `-Progress*200+dist`; test `EveryNormalSurvivorWorks...` (counts hackers by the brain's own `_terminal`, so partly self-referential) |
| 2 | Hide on sight or Threat >= 2, cover, Silent, camp rule | DEVIATION (acceptable except Desperate, see M1) | SB:427-499 is the default path. Commit mode (SB:393-411) and Desperate (SB:350-353) override it by design |
| 3 | Rescue split, timer-aware | PASS | SB:911-937 (nearest Normal, arrival vs `StatusTimer`, `urgent` relaxes gates); tests `RescueUsesArrivalHoldAndUrgency`, `OnlyNearestEligible...`, `AnIdleTeammateIsNotCountedOn...` |
| 4 | End game: switch duty, everyone to an open exit, collapse at 2x for Lagging, hatch kept | PASS | SB:354-386, SB:772-824, SB:998-1006, SB:367-372 |
| 5 | Skills | PASS | SB:1049-1078; test `SurvivorUsesChaseAndHackSkills` |
| 6 | Killer predictive chase, 1.5 s, then search | PASS | KB:91-97, KB:148-190, KB:110-116; test `PredictiveAim...` (last assertion is trivially true) |
| 7 | Patrol ranking by blips then distance; 2 s kick; skip Lagging | PASS | KB:200-288, KB:85-88; tests `PatrolRanks...`, `KillerHoldsKick...`, `KillerDoesNotKick...` |
| 8 | Exit guard on the highest `SwitchProgress`; Firewall; hatch | PASS (QA-a2 m1 fixed) | KB:243-253 skips `Open` exits; KB:441-449; KB:98-109 |
| 9 | Killer skills | PASS | KB:426-451 |
| 10 | Phantoms follow survivor pacing | PASS (note m3) | `GameServer.Bots.cs:16-24` reference `SurvivorBrain.*`; hack pauses 1-2 s flow through. AUC 0.705 and KS p 0.84 per impl report |
| 11 | Fair information | PASS | section 2 |
| 12 | Restored bounds (MatchLength <= 4, AUC < 0.85, KS p >= 0.02) | PASS (per impl report, not in my pack) | `td` shows the gates at `<= 4`, `0.02` and 0.85. Report: MatchLength 0 time-ups, AUC 0.705, KS p 0.84 |
| 13 | Balance bands gated | PASS | section 4; `td:177-211` gates win, median and time-ups |
| 14 | Do not touch rules, protocol, Shared, client, sync judge | PASS with note | Only `BotViews.cs` (`CollapseLeft`), `GameServer.Bots.cs` and `GameServer.Phantoms.cs`. The Phantoms edit is named in the report |

## 2. Fairness

**Survivor brain reads** (`GameServer.Bots.cs:79-105`, `BotViews.cs:14-37`):
- Own state: Time (match clock), SelfId, X, Z, Speed, Status, Skill, SkillReady, Threat, CollapseLeft.
- Survivor entries: Id, Status, Flags (Signal, Hacking, Rescuing), X, Z, StatusTimer. `GameServer.Visibility.cs:137-140` sets those flags on the entries a survivor receives.
- Terminal progress, X, Z. `TerminalsRequired`, which is in `SelfState` for survivors (`ServerPackets.cs:32`).
- Exits: X, Z, Powered, Open, `SwitchProgress`, plus the static switch point from the map. Hatch while visible.
- Killer X, Z, Yaw only while `KillerInSightOf`.
- All of it is available to a human survivor. The `Urgent` and `Desperate` calculations use only the match clock, `TerminalsRequired`, terminal progress, exit Open and the Normal count. No fairness violation.

**Killer brain reads** (`GameServer.Bots.cs:109-130`):
- Own X, Z, Yaw, Speed, Skill, SkillReady.
- Anonymous blips (X, Z, Lagging).
- Terminals as X, Z, Done only.
- Exits (Powered, Open, `SwitchProgress`), hatch, match clock.
- The `Noticed` and `LateAttention` knobs use only `TerminalCount` and Time.
- All of it is available to a human Killer.

**Terminal progress in the Killer.** None. Patrol ranking (KB:260-268) uses `Done`, distance and `_signalAt` (blips within 6 m, 60 s). The kick (KB:206-231) uses `Done`, `_kickedAt` and blips. PASS.

## 3. Attempt-3 brief A-D, F1-F7, QA-a2 findings

| Item | Status | Evidence and covering test |
|---|---|---|
| A. Stall invariant | PASS, with caveats in 3a | `StallInvariantTests` (10 theory rows: 5 configs x 2 seed sets). All rows pass in `log` (lines 74, 164, 317, 360, 403, 446, 489, 532, 575, 619). Impl claims attempt 2 had 4 and 5 stall windows (idle survivor + 6) and 6 and 3 silent time-ups (1v1). I cannot verify the red run |
| B. Commitment | PASS, with DEVIATIONS | SB:61-80, 238-291, 393-411, 600-648, 772-824. Tests `AfterCommitSeconds...`, `CommittedSurvivorStillFlees...`, `CommitPickAvoids...`, `ALoneSurvivorMovesToTheOtherSwitch...`, `AnIdleTeammateIsNotCounted...`. Deviations: 90 s big-team delay (SB:68), pick score capped at 30 m (SB:76, 637), Urgent and Desperate (M1) |
| C. Killer search and guard | PASS | KB:43-45, 241-256, 289-293, 339-403. Tests `KillerWithoutAnyBlip...` and `KillerPatrolsAnOpenExit...` (both input rows). Note m2 |
| D m5 | PASS (note m3) | Report names `GameServer.Phantoms.cs`. The diff shows no stray newline change |
| D m6 | PASS (note m4) | SB:691-735, test `EscapeDetourIsReusedForOneSecond...`. Wall time 8.2 to 7.2 s and 20.1 to 18.6 s per the report |
| D m2 | PASS | In the final log the old `cap 3 res 0` end-game signature is gone for seeds 2, 4, 15, 18, 39 (`log:23, 25, 36, 39, 60`: res 2, 3, 1, 3, 3). Impl calls seed 4 a Killer chain |
| F1 Killer never without a destination | PASS | KB:276-288 (sweep fallback), KB:316-321 (far spot); old test kept |
| F2 visible still Killer does not freeze | PASS | SB:953-955 `visibleCamp`; commit mode covers the rest (SB:393-411); test `VisibleCampReleases...` |
| F3 speed clamp | PASS | SB:921, 994, 1001 use `Math.Max(0.1f, view.Speed)` |
| F4 stickiness +15 | PASS | SB:977-979 |
| F5 rescue in the end game | PASS | SB:376-381 rescue before `HoldSwitch`; test `PoweredSwitchHolderRescues...` |
| F6 margins | PASS | 1v7 win 58/55 %, 1v1 win 50/48 % |
| F7 detour around a visible Killer | PASS | SB:661-675, 678-735 |
| QA-a2 M1 (1v1 stalemates) | FIXED | `log:116-445`: 1v1 silent time-ups 0 on both sets. The 2 and 3 remaining time-ups all have 1 capture (`log:137, 139, 418, 427, 439`) |
| QA-a2 m1 (guard an open exit) | FIXED | KB:243-253 |
| QA-a2 m3 (every terminal in sight) | FIXED | commit mode SB:393-411; before it, SB:955 would refuse them all |
| QA-a2 m4 (fragile margins) | PARTLY | MatchLength 4 to 0, 1v7 median 566 and 594 s, but 566 is only 26 s above the floor (section 4) |
| QA-a2 m5, m6 | FIXED | see D |

### 3a. Is the stall invariant real?

**What counts as progress** (`td:36-109`):
- Per-tick sampling by reflection of `GameServer._progress` (terminal progress) and `_switchSeconds` (switch progress). Any rise in any element counts (`Rising`, `td:56-66`).
- Log events: capture, second capture, rescue, emergency restore, any Escaped status, `hatch ...`, terminal complete, `exit N open`.

**Checked and sound:**
- The window is closed at match end (`td:96-97`), so a time-up tail with no progress counts.
- `Mark` measures the gap in ticks against `StallSeconds = 150` (`td:30, 43-45`), applied as written. The assertion is `stalls == 0` and `silent.Count == 0` (`StallInvariantTests:49-50`).
- Switch progress never decays (`GameServer.Exits.cs:79`, only `+=`), so a rise there is real work.
- No config was skipped. All 10 rows ran (`log` lines above). The idle survivor + 6 case is covered (`RunIdleSurvivor`, `td:135-159`): one human survivor that never moves, 6 bots, and the Killer bot.
- 150 s was not weakened.

**Weaknesses** (see m1): captures and rescues count as progress, and so does any rise in terminal progress, even if a kick takes it back down.
- A capture and rescue loop therefore passes. Examples in the idle survivor + 6 sweep: `log:334` (seed 1016, exits powered at 588 s, never opened, 14 captures, TimeUp) and `log:335` (seed 1017, 2/5 terminals, 11 captures).
- The invariant proves "something happened every 150 s", not "the match approaches its end".
- The "no silent time-up" rule is met in the strict sense only: 5 and 2 of the 1v1 time-ups have exactly 1 capture, and the 1v1 survivor stayed alive for the rest of the match.
- The longest gap is 120 s and 110 s on idle survivor + 6 (`log:359, 617`), 80 % of the threshold. Fragile if anything changes.

### 3b. Deviations and new modes

| Item | Fairness | Gameplay |
|---|---|---|
| Big-team commit 90 s (SB:68, 328) | OK: uses the Normal count from public entries | Reasonable. Once the team drops below 5 Normal survivors the delay falls to 25 s and an already stale clock commits at once |
| Urgency (SB:296-297, 349) | OK: public clock, terminal progress, `TerminalsRequired` | Commit mode only. A Killer that is still or far no longer stops work; Threat 3 and a closing Killer within 10 m still break off. Acceptable |
| **Desperate (SB:301-302, 350-353)** | OK on information | **Disables every flee and hide, including Threat 3 and a Killer closing within 10 m (M1)** |
| Hack pause 1-2 s (SB:19) | No information used | Hack duty about 55 %, so a lone bot needs about 80 s per terminal. Phantoms follow (`GameServer.Bots.cs:20-21`). Acceptable bot handicap |
| Mid-lobby miss 0.6 (KB:26) | Uses `TerminalCount` only | Killer slightly more perceptive in 3-5 survivor lobbies. Keeps 1v3 inside the band. Acceptable |
| Late attention from 600 s (KB:29, 138-140) | Uses only the match clock | Fades small and mid lobby handicaps toward the large-lobby values, linearly to 900 s. A human survivor in a 1-5 survivor lobby meets a gradually sharper bot Killer in the last 5 minutes. A tuning decision rather than a rule break. Minor note only (m5) |

**Changed older tests:**
- Camp Sim raises hack progress (`td:293-306`): **justified**. A view frozen at progress 0 is unrealistic (the server raises it, as the test comment says) and made the bot commit. It does not weaken the camp checks.
- Team entries carry `Hacking` (`BotV2Tests:50-55`): **justified**. The real server sets the flag (`GameServer.Visibility.cs:139`), and idle-ally detection (SB:220) needs it. It keeps the cap-2 check meaningful.
- Patrol test's `_lastBlipAt = 60` (`BotV2Tests:228-229`): **justified but reflection-based**. The test checks that blips are forgotten after 60 s, which coincides with search mode at 45 s. The cost is a test that pokes private state.
- Also changed in attempt 3 (`td:329-338`): the flee test now checks the farthest point reached rather than the final distance. It would pass for a bot that flees and then returns, which is the intended commit behaviour. Acceptable.

### 3c. Behaviour bugs

- **Freezes and thrashing:** none found. Commit pick is sticky (SB:604-609) and re-picks only when the Killer moved 8 m and is within 12 m of the pick. The `Interact` cancel at SB:166-170 re-thinks each tick (CPU only, pre-existing).
- **Killer with no destination:** none. Fallback chain KB:276-288, 316-321. The search branch cannot return the Killer's own position unless `_cellX` is empty.
- **Kick loops:** none. `_kickedAt` plus `Revisit` (KB:206, 230).
- **Rescue and escape dead ends:** none found. Far Lagging allies with an open exit are left on purpose (`arrive < 4`, SB:1004).
- **CPU:** `PlanEscape` is cached for 1 s (SB:707-712); the worst case is still up to 96 A* searches per re-plan per bot (SB:716-728). `CommitPick`, `TimeShort`, `MarkCell` are small. See m4.

## 4. Balance evidence (`log`)

| Config (set) | Win | Median | Time-ups | Band | Verdict |
|---|---|---|---|---|---|
| 1v7 (1-40) | 58 % (`log:258`) | 594 s | 0 % | win 35-65, median 540-660, time-ups <= 10 | PASS. Win 7 points under the cap |
| 1v7 (1001-1040) | 55 % (`log:212`) | 566 s | 3 % | same | PASS. **Median 26 s (4.8 %) above the 540 s floor: FRAGILE** |
| 1v3 (1-40) | 65 % (`log:64`) | 538 s | 0 % | win 25-75, median <= 660, time-ups <= 20 | PASS |
| 1v3 (1001-1040) | 68 % (`log:18`) | 503 s | 0 % | same | PASS. **7 points under the 75 % cap: FRAGILE** |
| 1v1 (1-40) | 50 % (`log:161`) | 712 s | 5 % | win 15-85, time-ups <= 30 | PASS (target <= 10 % met) |
| 1v1 (1001-1040) | 48 % (`log:72`) | 611 s | 8 % | same | PASS. 8 % is 2 points inside the 10 % target (not a gate) |
| idle Killer + 7 (both) | 100 % | 222 / 217 s | 0 % | win >= 90, exits > collapse | PASS: exit escapes 280/280 and collapse losses 0/280 on both sets (`log:268, 316`) |

Targets: 1v7 win 40-60 % met (58/55); median >= 555 s met on both (594/566); 1v1 time-ups <= 10 % met.

The sweeps are fixed-seed and deterministic, but any change in `GameServer*.cs` (U6g) or a bot constant can move 1v7 median below 540 s or 1v3 win above 75 %.

Not banded and worth noting: idle survivor + 6 wins 10/40 (1001 set) and 14/40 (1-40) as Survivor, i.e. 25 % and 35 %. TimeUp is 9/40 and 11/40 (22 % and 28 %), against the 10 % ceiling for a full 1v7. The brief gives this config no band (report agrees).

## 5. Tests

- `BotV2Tests`, `BotV2A2Tests` and `BotV2A3Tests` call the real `SurvivorBrain.Update` and `KillerBrain.Update` with real `SurvivorBotView` and `KillerBotView`, `FixedMap` and `NavGrid`. Some read private fields by reflection (`_terminal`, `_hiding`, `_patrol`, `_commitPick`). White-box, not shortcuts.
- Weak spots:
  - `BotV2A3Tests:76-77` ends in `A || B`, where B is true for any unblocked other terminal: nearly tautological.
  - `PredictiveAim...` last assertion holds trivially.
  - `KillerWithoutAnyBlip...` covers only the "no blips at all" arm of search (KB:256). The second arm (`powered && t - _noticedAt >= 45`, the one that actually fires in matches, since phantoms and Signals keep `_lastBlipAt` fresh) has no unit test.
- **No test covers `Urgent` or `Desperate`** (grep of all three BotV2 files). This is why M1 went unnoticed.
- Changed old assertions: see 3b. None weakens a check in a way that hides a defect, apart from the attempt-2 inversion of `AnIdleKillerInSight...` (`td:316-326`), which commit mode now covers.

## 6. Findings

### M1 (major): Desperate mode stops survivor bots from fleeing a Killer that is chasing them

- **Evidence:** SB:301-302, 350-353: `threatened = !Desperate(view) && (Threat >= 3 || (KillerVisible && dist <= 10 && _killerClosing))`. With `Desperate` true, `commit` is also true (Desperate implies Urgent), so `cautious` is false and the commit branch at SB:393-411 returns before any flee, hide or `coming` logic (SB:427-499). The bot keeps hacking or walking to its switch beside an adjacent Killer. No test (section 5).
- **When it triggers:** `view.Time + 3 * work + 30 >= 900` (SB:304-326).
  - 1v1: nothing done gives `work = 150`, so from 420 s. One terminal done and two at 0 gives 105, so from 555 s. The 1v1 median is 712 s, so most of the second half of an average 1v1 is spent in this mode.
  - 1v3: two left at 0 gives roughly 690 s.
  - 1v7: rarely, past about 640 s.
- **Expected** (a3 brief B, spec 2): commit mode is interrupted by Threat 3 or a Killer closing within 10 m, and the bot flees. **Actual:** Desperate bots ignore both. A human Killer chasing a bot survivor in a small lobby late in the match will see it keep working while being chased.
- **Why major rather than minor:** a player would notice it, it contradicts the brief's own interrupt rule and spec 2, and the sweeps cannot show it, because both Killer and survivors are bots in them.
- **Suggested fix:** keep the Threat 3 and closing-within-10-m interrupt in Desperate mode, and let Desperate lift only the sight, Threat 2 and calm-wait holds. Add a test: Desperate bot, Killer closing, expect flee.

### Minor findings

- **m1 Stall invariant counts capture and rescue as progress** (`td:101-109`; evidence `log:334-335`). A capture and rescue loop with exits powered and unopened for 300+ s passes. This follows the brief's own definition of progress. Consider also reporting "powered but not open for 200 s" for information.
- **m2 Search has two arms, only one tested** (KB:256; test `KillerWithoutAnyBlip...`). The first arm rarely fires in matches because phantoms and Signals keep `_lastBlipAt` fresh.
- **m3 Phantom mix changed more than the report says** (`code.diff:55-56`). `Next(2)` became `Next(WhiteNoiseTerminalPointChoices = 3)`, so 2-3 of 5 became 2-4 of 5. The old comment recorded 1..4 as failing AUC (0.872) under the old pacing; the justification was dropped. AUC 0.705 and KS p 0.84 now pass with margin, so this is a reporting gap, not a failing gate.
- **m4 `PlanEscape` cache ignores Killer movement** (SB:707-712). A cached waypoint can be about 1 s stale (a Killer moves about 4.6 m). The worst-case ring search is unchanged (up to 96 A* per re-plan). The wall-time gain is modest (8.2 to 7.2 s). Check cost in the live offline run.
- **m5 Late attention is an undocumented-in-GDD difficulty ramp** (KB:29, 138-140). Gradual and public-clock only, but a human survivor in a 1-5 survivor lobby faces a sharper bot Killer from 10:00. Mention it in the GDD or the commit message.
- **m6 Idle survivor + 6 has no band and time-ups of 22-28 %** (`log:359, 617` and the TimeUp rows above). Win 25 % and 35 %. Impl report lists it as unverified; keep it on the live-check list.
- **m7 Fragile margins** (section 4): 1v7 median 566 s (4.8 % over the floor), 1v3 win 68 % (7 points under the cap), idle survivor + 6 longest gap 120 s (80 % of 150 s).

## Verdict

Spec, fairness and the attempt-3 brief A-D are met. Every banded sweep and the 150 s stall invariant pass on both seed sets, and QA-a2 M1 and m1-m6 are fixed. One major remains: Desperate mode removes all flee behaviour, including against an adjacent Killer (SB:350-353), with no test.

VERDICT: FAIL (0 blockers, 1 majors)
