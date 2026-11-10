# U6d-bots implementation report, attempt 3 follow-up (QA-a3 M1)

Quota switch: Codex → Claude Opus (Codex out of credits).

## What changed
- `SurvivorBrain.cs` (orchestrator's fix, unchanged by me): the `Desperate` guard on `threatened` and the `Desperate*` members are gone.
- `Server.Tests/Core/BotV2A3Tests.cs`: two regression tests. The view is set at 700 s with 2 terminals required (40 % / 20 %), so `TimeShort(view, 3, 30)` and `Urgent` are both true.
  - `LateInTheMatch...BreaksOffForAClosingKiller`: a visible Killer walks in from 14 m to 7 m. The bot hides or flees inside 10 m and hacks 0 ticks below 9 m.
  - `LateInTheMatch...StillFleesThreatThree`: Threat 3 sets `_fleeing`. The bot then stops hacking and moves away.

## Red/green
- Re-adding `!TimeShort(view, 3, 30) && `: both new tests **FAIL** (9/11 in the class pass).
- Guard removed again: **11/11 pass**.

## Gates
- `build -warnaserror`: 0 warnings, 0 errors.
- Non-sweep tests: 1035/1035 pass.
- BalanceSweep: **16/18**. Log: `docs/qa/U6d-balance-a3fix.log`.
  - All bands pass.
  - `StallInvariantTests` 1v1 **FAILS** on both seed sets, with time-ups that have 0 captures: seeds 15 and 19 (exit powered at 881 s and 888 s, not opened) and seed 1035.
  - Stall windows: 0 in every config.
- MatchLength bot time-ups: 1 (limit ≤ 4). AUC: 0.705. KS p: 0.843.

## Sweep (seeds 1-40 / 1001-1040; attempt 3 in brackets)
| Config | Win | Median | Time-ups | Silent time-ups |
|---|---|---|---|---|
| 1v7 | 58/58 % (58/55) | 594/566 s (594/566) | 3/3 % (0/3) | 0/0 |
| 1v3 | 65/68 % (65/68) | 538/503 s (same) | 0/0 % | 0/0 |
| 1v1 | 50/48 % (50/48) | 712/611 s (712/611) | 15/8 % (5/8) | **2/1** (0/0) |
| idle Killer + 7 | 100/100 % | 222/217 s | 0/0 %, 0 collapse losses | 0/0 |
| idle survivor + 6 | n/a | longest gap 110/120 s | n/a | 0/0 |

## Constant probes (none kept; `SurvivorBrain.cs` byte-identical to the orchestrator's version)
Silent 1v1 time-ups (1-40 + 1001-1040) for each probe:
- `FleeMaxReach` 10: 3+2, and idle survivor + 6 stalls.
- `CommitReleasePoints` 30: 3+3.
- `CommitSeconds` 15: 5+2.
- `UrgencyFactor` 4.5: 4+3.
- `UrgencyFactor` 5: 1+1, the closest.
- `UrgencyFactor` 6: 2+1.
- `UrgencyFactor` 8: 1+0, and idle survivor + 6 gets a 156 s stall.

No constant clears both seed sets. Without the "ignore the Killer" path, the 1v1 silent time-ups look structural: the bot evades by going Silent, and the Killer bot never lands a capture.

## Unverified / risks
- The 1v1 invariant needs a design decision: a Killer-side change or a revised invariant.
- No live-play check.
