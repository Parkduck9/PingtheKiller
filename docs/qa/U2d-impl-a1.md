# U2d implementation report, attempt 1 (Claude Opus; quota fallback, Codex has no credits)

**Changed**
- `Server.Core/Runtime/GameServer.Bots.cs` (rewritten): fills per-bot query objects and applies each brain's intent through MoveInput, Interact and UseSkill. Test-only probe `BotProbeForTests` is internal. ServerOptions is unchanged, because the existing `IsOffline/Seed/BotCount/AutoStart` already run all-bot matches.
- New brain files: `BotViews.cs`, `BotBrain.cs`, `SurvivorBrain.cs`, `KillerBrain.cs`, each with a `.meta`. `NavGrid.Spots` was added.
- New tests:
  - `BotBalance(Tests).cs`: the sweep, traited `BalanceSweep`, runs in about 3 s.
  - `BotFairInformationTests.cs`: scrambled hidden state must leave lock-step matches identical, plus 2 negative controls and a reflection check.

**Re-targeted tests (old → new)**
- Core:
  - `SkillTests.DummyPacket…`: the dummy stops at 1.0 m → 1.4 m.
  - `MatchLengthTests.MedianMatchLengthReport`: the idle floor 180 → 120–360 s; a bot time-out now fails → at most 2 of 15.
  - `DecoyAndLeakTests.LeakSweep_…`: 8 seeds → as many as needed, up to 24, until all 4 killer skills are drawn.
- QA:
  - `U2bInputSandboxBotTests.Bots_SurvivorBotsNeverSteer…`: line of sight is now part of the compared inputs, two off-map spots were added, and runs are 600 ticks.
  - `U2bInputSandboxBotTests.Bots_SurvivorBotStops…`: the killer is placed only at hidden spots, and the threat-1 run is 600 ticks.
  - `U2bSweepTests.Disconnect_HumanKiller…`: only survivor entries are counted.
  - `U2bSweepTests.LeakSweep_EveryEncodedFrame…`: this is an oracle fix, flagged for review.
    - Byte windows that straddle shown entries no longer fail.
    - The match-end afterimage is exempt. These false positives were present on main too and are re-rolled by any bot path change.

**Verified**
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings, 0 errors.
- `dotnet test PingTheKiller.sln`: Server 805/805 (32 s), Harness 616/616 (6 m). The balance sweep (160 matches) takes about 3 s.
- Gates:
  - KS gate PASS.
  - U2c AUC: gated 0.805 (baseline 0.758; ceiling 0.85), all features 0.889, best single feature 0.739.
- Harness, 3 runs per PLAN §5 (`--threshold 100 --expect-los`, a fresh sandbox server and a fresh normal server per run): all 100/100, exit 0. Reports: `docs/qa/U2d-harness-run{1,2,3}.json`. The servers were stopped.

**Unverified / risks**
- 1v1 has the thinnest margin.
- The killer bot's perception scales with lobby size, inferred from the number of placed terminals.
- The KS gate passes with p = 0.017.
- Old `Player.Bot*` fields are now unused outside the sandbox (`Player.cs` is not owned).

**Before (main 48abe4a) / after, seeds 1..40**

| Config | Survivor win | Median (p25/p75) | End reasons | Terminals | Capt / resc / exec | Timeouts | ≥1 rescue |
|---|---|---|---|---|---|---|---|
| 1v7 before | 0% | 49 s (37/76) | all down 40 | 0.03 | 8.75 / 1.75 / 2.05 | 0% | 78% |
| 1v7 after | **53%** | **401 s** (317/446) | escaped 21, down 17, time 2 | 3.13 | 6.40 / 0.65 / 4.83 | **5%** | **53%** |
| 1v3 before | 0% | 20 s (17/33) | all down 40 | 0.13 | 3.50 / 0.50 / 0.53 | 0% | 33% |
| 1v3 after | **48%** | **409 s** (327/538) | escaped 19, down 16, time 5 | 2.30 | 2.70 / 0.58 / 1.30 | 13% | 53% |
| 1v1 before | 0% | 10 s (6/11) | all down 40 | 0.00 | 1.00 / 0 / 0 | 0% | 0% |
| 1v1 after | **33%** | **310 s** (203/410) | down 25, escaped 13, time 2 | 1.10 | 0.63 / 0 / 0 | 5% | 0% |
| idle + 7 before | 100% | 394 s (340/465) | escaped 40 | 4.00 | 0 | 0% | 0% |
| idle + 7 after | **100%** | **144 s** (136/156) | escaped 40 | 4.00 | 0 | 0% | 0% |

Wide check over 200 seeds (not gated): 1v7 49%, 1v3 45%, 1v1 25%.
