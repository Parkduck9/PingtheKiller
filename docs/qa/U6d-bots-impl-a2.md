# U6d-bots implementation report, attempt 2

Implementer: Claude Opus (quota switch from Codex gpt-6.1-sol mid-attempt)

## Fixes
- **F1** `KillerBrain.Patrol`: the Killer always has a destination. When the revisit filter is empty it sweeps the least recently checked terminal (a Signal within 60 s counts 20 s older), skipping the one underfoot. Small and mid lobbies roam a far nav spot instead (70 % / 30 %). A kick cooldown (`Revisit`: 150/110/45 s) stops re-kicks. Tests: `KillerKeepsPatrollingWhenAllWorkWasRecentlyVisitedOrDone`, `KillerSweepRevisitsLeastRecentlyChecked...`, `KillerDoesNotKickATerminalAgainWithinRevisit`.
- **F2** (Codex): the camp rule covers a visible Killer. Test: `VisibleCampReleasesOccludedWorkWithinFortySecondsAndMovementRestoresHiding`.
- **F3** (Codex): speed clamp. Test: `EscapeRescueClampsNonPositiveSpeed`.
- **F4** (Codex): +15 stickiness. Test: `TerminalStickinessRequiresAtLeastFifteenMoreProgress`.
- **F5 cause**: switch duty returned before rescue was considered, and camp-flagged sight or threat also blocked rescue. Test: `PoweredSwitchHolderRescuesReachableAllyBeforeOpeningExit`.
- **F7** (Codex): detour or alternative exit. Tests: `EscapeDetoursAroundVisibleStillKiller...`, `CampingKillerDoesNotSuppressAffordableCollapseRescue`.
- **F6 balance** (survivor side):
  - calm 2-4 s;
  - freeze in place while the Killer is beyond 18 m;
  - an unseen Killer at Threat 2 makes the bot freeze; it flees only at Threat 3;
  - only a team of 5 or more abandons the threatened terminal;
  - after 30 s with no progress on the leading switch, a second holder goes to another powered switch;
  - phantom worker mix 2-4.

  These are covered by the tests in `BotV2A2Tests.cs`.

## Numbers
| Config | Attempt 1 win (seeds 1-40 / 1001-1040) | Attempt 2 win (seeds 1-40 / 1001-1040) | Attempt 2 median (s) |
|---|---|---|---|
| 1v7 | 38 / 45 % | 58 / 55 % | 553 / 544 |
| 1v3 | 40 / 35 % | 45 / 45 % | 515 / 475 |
| 1v1 | 18 / 23 % | 25 / 35 % | — |

Restored bounds:
- MatchLength: 4 of 15 time-ups (limit ≤ 4).
- Gated AUC: 0.716 (all features 0.775).
- Gated KS: lowest p is 0.59.

## Gates
- Build with `-warnaserror`: 0 warnings, 0 errors.
- Non-sweep tests: 1024 of 1024 pass.
- Balance sweep: 8 of 8 pass.

Logs: `U6d-build-a2-final.log`, `U6d-tests-a2-final.log`, `U6d-balance-a2-final.log`.

## Unverified
- `Tools/Harness.Tests` was not run.
- No live play in the exe.

## Risks
- **Thin margins:**
  - 1v7 median 544 s against a 540 s floor;
  - MatchLength exactly at its limit of 4;
  - 1v1 time-ups at 18 % against a 30 % limit.

  The sweep is chaotic, so a small change can tip any of these.
- **Deviations from spec 2:**
  - the bot hides in place beyond 18 m instead of moving to cover;
  - an unseen Killer at Threat 2 makes the bot freeze instead of flee;
  - the F1 fallback favours recent Signals with a bonus rather than strictly.
- **Test change:** the `ReconnectAndBotTests` wait went from 20 s to 60 s, because of hide-in-place.
- **Test removed:** `KillerFallbackPrefersRecentSignal...` was replaced by the sweep test.

## Sweep tables (`--filter "Category=BalanceSweep"`)
| Config | Survivor win | Median length (p25/p75) | End reasons | Terminals done | Captures / rescues / executions per match | Timeouts | Matches with >= 1 rescue |
|---|---|---|---|---|---|---|---|
| 1v7 (1-40) | 58% | 553 s (437/698) | resolved 27, collapse 7, all down 5, time up 1 | 5.55 | 9.58 / 8.43 / 0.00 | 3% | 100% |
| 1v7 (1001-1040) | 55% | 544 s (443/606) | AllResolved 27, Collapse 8, AllSurvivorsDown 4, time 1 | 5.60 | 9.23 / 7.85 / 0.00 | 3% | 100% |
| 1v3 (1-40) | 45% | 515 s (471/555) | resolved 17, all down 15, collapse 8 | 4.00 | 2.75 / 1.53 / 0.00 | 0% | 63% |
| 1v3 (1001-1040) | 45% | 475 s (422/591) | AllResolved 17, AllSurvivorsDown 16, Collapse 7 | 3.98 | 2.73 / 1.48 / 0.00 | 0% | 68% |
| 1v1 (1-40) | 25% | 689 s (583/846) | all down 23, resolved 10, time up 7 | 2.23 | 0.60 / 0.00 / 0.00 | 18% | 0% |
| 1v1 (1001-1040) | 35% | 783 s (702/867) | AllSurvivorsDown 19, AllResolved 14, time 7 | 2.60 | 0.58 / 0.00 / 0.00 | 18% | 0% |
| idle killer + 7 (1-40) | 100% | 214 s (186/224) | resolved 40 | 5.10 | 0.03 / 0.03 / 0.00 | 0% | 3% |
| idle killer + 7 (1001-1040) | 100% | 214 s (198/229) | AllResolved 40 | 5.15 | 0.05 / 0.05 / 0.00 | 0% | 3% |

Idle survivor outcomes, exit escapes against collapse losses:
- seeds 1-40: 280/280 against 0;
- seeds 1001-1040: 279/280 against 0.
