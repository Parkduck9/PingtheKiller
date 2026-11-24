# U6d-bots implementation, attempt 1

Verified the brief's diagnosis: worker caps/ramp, low hack duty, missing predictive chase, kicks and collapse awareness.

Changed SurvivorBrain/KillerBrain for two-worker spread, cover, timed rescue, switch/escape priorities, skills, anonymous prediction, Signal patrol and kicks. BotViews/GameServer.Bots carry the human-visible personal collapse clock and phantom tuning. Ownership exception: GameServer.Phantoms.cs relocates/uses the phantom mix settings; its fixed mix otherwise prevents spec 10 tuning. Rules, Shared, client and sync judge are untouched.

Tests cover the new decisions, gate both seed sets, strengthen KS, update obsolete visible-camp expectations, and expand sync sampling while retaining its floor.

Commands: `dotnet build PingTheKiller.sln -warnaserror` (0 warnings); `dotnet test Server.Tests --filter "Category!=BalanceSweep"` (1002/1002); `dotnet test Server.Tests --filter "Category=BalanceSweep"` (8/8, 320 matches); `dotnet test Tools/Harness.Tests` (635/635, 16.8 minutes). A contended H3 coverage flake passed isolated and full-suite retries. Flags: `--no-restore -p:TargetPlatformSdkPath=C:/nonexistent -p:TargetPlatformDisplayName=Windows`; harness also `--no-build`. Evidence: U6d-*-final.log.

Restored: Normal-bot timeouts 3/15 <=4 (idle 0/15), AUC 0.773 <0.85, all gated KS p=1 >=0.02. Risks: heuristic rescue travel estimates and timing-sensitive harness coverage. Orchestrator harness scoring, Unity and live play remain unverified.

Commit blocked (both staging attempts): Git cannot create C:/Unity/MultiPlayGame/PingtheKiller/.git/worktrees/u6b/index.lock (permission denied).

Baseline (provided main 53f4ef9 measurements):

| Config | Survivor win | Median | Time-ups | Terminals/match |
|---|---|---|---|---|
| 1v7 | 0–3% | 757–790 s | 28–33% | ~2/5 |
| 1v3 | 0% | 446–468 s | — | — |
| 1v1 | 0% | 279–349 s | — | — |
| idle + 7 | 98–100% | 631–636 s | — | — |

Seeds 1–40 (verbatim rounded sweep rows):

| Config | Survivor win | Median length (p25/p75) | End reasons | Terminals done | Captures / rescues / executions per match | Timeouts | Matches with >= 1 rescue |
|---|---|---|---|---|---|---|---|
| 1v7 | 38% | 557 s (506/650) | collapse 17, resolved 12, all down 9, time up 2 | 5.50 | 9.48 / 7.00 / 0.00 | 5% | 98% |
| 1v3 | 40% | 611 s (539/700) | all down 17, resolved 13, collapse 8, time up 2 | 4.05 | 3.10 / 1.48 / 0.00 | 5% | 73% |
| 1v1 | 18% | 582 s (478/737) | all down 32, resolved 7, time up 1 | 2.43 | 0.80 / 0.00 / 0.00 | 3% | 0% |
| idle killer + 7 | 100% | 239 s (211/282) | resolved 29, collapse 11 | 5.10 | 0.43 / 0.05 / 0.00 | 0% | 5% |

Seeds 1001–1040:

| Config | Survivor win | Median length (p25/p75) | End reasons | Terminals done | Captures / rescues / executions per match | Timeouts | Matches with >= 1 rescue |
|---|---|---|---|---|---|---|---|
| 1v7 | 45% | 560 s (476/667) | AllResolved 15, Collapse 13, AllSurvivorsDown 12 | 5.65 | 9.55 / 6.65 / 0.00 | 0% | 100% |
| 1v3 | 35% | 559 s (479/630) | AllSurvivorsDown 15, AllResolved 13, Collapse 9, time 3 | 3.93 | 2.85 / 1.38 / 0.00 | 8% | 78% |
| 1v1 | 23% | 487 s (310/593) | AllSurvivorsDown 30, AllResolved 9, time 1 | 2.13 | 0.75 / 0.00 / 0.00 | 3% | 0% |
| idle killer + 7 | 100% | 251 s (217/295) | AllResolved 28, Collapse 12 | 5.08 | 0.58 / 0.18 / 0.00 | 0% | 10% |

Idle outcomes: exits 265/280 and 262/280; collapse eliminations 14/280 and 16/280, respectively.

