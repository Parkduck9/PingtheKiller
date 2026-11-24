# U6d-bots implementation report, attempt 3

Implementer: Claude Opus (attempt 3, final; Codex out of credits)

## A. Invariant
`StallInvariantTests`: all sweep configs plus idle survivor + 6, both seed sets; no 150 s window without progress, no time-up with 0 captures.
- **Red (attempt 2):** idle survivor + 6 had 4 and 5 stall windows; 1v1 had 6 and 3 silent time-ups. **Green:** all 0.

## B-D
- **B:** after 25 s without progress a survivor commits.
  - Only Threat 3 or a Killer closing within 10 m interrupts it.
  - It picks the target farthest from the Killer and out of its sight.
  - A stalled `SwitchDuty` takes the switch farthest from the Killer.
  - Idle allies are ignored.
  - Tests: `BotV2A3Tests`.
- **C:** after 45 s without a blip the Killer searches 20 m cells, and it patrols open exits.
  - Tests: `KillerWithoutAnyBlip…` and `KillerPatrolsAnOpenExit…`.
- **m5:** `GameServer.Phantoms.cs` changed because its mix constants moved to `GameServer.Bots.cs`, beside the survivor pacing. The newline is reverted.
- **m6:** the `PlanEscape` detour is cached for 1 s. Wall time 8.2 → 7.2 s and 20.1 → 18.6 s.
- **m2:** 1v3 seed 4 is a **Killer chain**: 4 captures in 28 s at the switch, nobody left to rescue.

## Attempt 2 → now (seeds 1-40/1001-1040)
| Config | Win | Other |
|---|---|---|
| 1v7 | 58/55 → 58/55 % | median 553/544 → 594/566 s |
| 1v3 | 45/45 → 65/68 % | |
| 1v1 | 25/35 → 50/48 % | time-ups 18/18 → 5/8 % |

- MatchLength time-ups: 4 → 0.
- AUC: 0.716 → 0.705.
- KS p: 0.84.

## Gates
- Build: 0 warnings.
- Non-sweep tests: 1033/1033.
- BalanceSweep: 18/18.

## Deviations
- **Constants:** big-team commit 90 s; release 15 points; hack pause 1-2 s (phantoms follow); mid-lobby miss chance 0.6; late attention from 600 s.
- **Additions:** urgency and desperate modes.
- **Test fixes:** the camp Sim raises progress; team entries carry Hacking; the patrol test sets `_lastBlipAt`.

## Unverified
- `Harness.Tests` and live play.
- Idle survivor + 6 has no band (win rate 35/25 %).

## Risks
- The sweep is chaotic.
- 1v3 is at 68 % against a 75 % cap.

## Final sweep tables
| Config | Survivor win | Median length (p25/p75) | End reasons | Terminals done | Captures / rescues / executions per match | Timeouts | Matches with >= 1 rescue |
|---|---|---|---|---|---|---|---|
| 1v7 (1-40) | 58% | 594 s (501/682) | resolved 27, collapse 9, all down 4 | 5.30 | 9.70 / 8.50 / 0.00 | 0% | 100% |
| 1v7 (1001-1040) | 55% | 566 s (484/694) | AllResolved 21, Collapse 12, AllSurvivorsDown 6, time 1 | 5.15 | 9.40 / 8.03 / 0.00 | 3% | 100% |
| 1v3 (1-40) | 65% | 538 s (453/624) | resolved 25, all down 8, collapse 7 | 3.95 | 2.50 / 1.70 / 0.00 | 0% | 73% |
| 1v3 (1001-1040) | 68% | 503 s (424/559) | AllResolved 27, AllSurvivorsDown 8, Collapse 5 | 3.90 | 2.45 / 1.75 / 0.00 | 0% | 83% |
| 1v1 (1-40) | 50% | 712 s (587/805) | resolved 20, all down 17, time up 2, collapse 1 | 2.38 | 0.50 / 0.00 / 0.00 | 5% | 0% |
| 1v1 (1001-1040) | 48% | 611 s (526/780) | AllResolved 19, AllSurvivorsDown 18, time 3 | 2.20 | 0.53 / 0.00 / 0.00 | 8% | 0% |
| idle killer + 7 (1-40) | 100% | 222 s (212/237) | resolved 40 | 5.23 | 0.00 / 0.00 / 0.00 | 0% | 0% |
| idle killer + 7 (1001-1040) | 100% | 217 s (206/226) | AllResolved 40 | 5.13 | 0.08 / 0.08 / 0.00 | 0% | 8% |

Idle survivor exit escapes are 280/280 on both seed sets, with 0 collapse losses. Logs: `U6d-balance-a3-final.log`, `U6d-tests-a3-final.log`, `U6d-gates-a3-final.log`, `U6d-build-a3-final.log`.
