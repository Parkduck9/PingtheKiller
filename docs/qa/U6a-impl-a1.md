# U6a implementation report, attempt 1

Quota fallback: Codex out of credits, so a Claude Opus subagent implemented this unit. QA must use a non-Claude family.

## Rule → code (Server.Core/Runtime)
| Rule | Code |
|---|---|
| R1 900 s, R9 timeout | GameConstants.cs; GameServer.Rules.cs:60 `TimeUp()` :195 |
| R2 rates, kick, regression | GameServer.Terminals.cs `Hack` :39, `KillerHolds` :112 |
| R3 sync checks | Terminals.cs `UpdateSyncChecks` :179, `IssueSyncCheck` :204, `Classify` :244, `Resolve` :254; GameServer.cs:340 |
| R4 power, switch, escape, firewall | GameServer.Exits.cs `PowerExits` :46, `Switches` :70, `Escape` :88, `FirewallTarget` :157; Skills.cs:68 |
| R5 collapse | Exits.cs `RunCollapse` :112 |
| R6 hatch | Exits.cs `UpdateHatch` :128, `CloseHatch` :142, `HatchVisibleTo` :151; FixedMap.cs:100-105 |
| R7 capture stages | Rules.cs `UpdateStatuses` :65, `AdvanceStage` :76, `Capture` :118 |
| R8 end and winner | Rules.cs `CheckResult` :168, `WinnerFor` :190 |

Bots: BotViews (exits, hatch); SurvivorBrain (switch duty, open-exit run, hatch); KillerBrain (hatch close, switch patrol, Firewall). The bot sync press is server-side (rate 0.85).

## Protocol v0.4 (`docs/PROTOCOL.md`)
- WorldState: `ExitOpen` and `ExitLocked` removed. Added `CollapseLeft`, `Exits[≤2]` and `HatchVisible` (+X/Z); terminals gain `Flags`.
- GameResult gains `EliminatedIds[≤8]`. Reasons are AllResolved, AllSurvivorsDown, TimeUp and Collapse.
- SelfState gains CollapseLeft, ActionProgress and ActionKind (25 bytes).
- New packets 0x11 SyncCheck, 0x12 SyncPress and 0x13 SyncResult. Shared reader: `WorldExits`.
- Client: `NetClient.IsServerPacket` accepts 0x11 and 0x13; without that the client drops the link on the first check. The ExitOpen, ExitLocked and GameEndReason readers were moved to v0.4.

## Tests
- New: Server.Tests `Core/RulesV2Tests.cs` (R1–R9: 46 cases) and `ProtocolV04Tests.cs` (5).
- Updated old-rule assertions (each listed in the handback): Server.Tests (timings 45 s/900 s, capture stages, per-survivor escape, v0.4 offsets and leak legit sets) and Harness.Tests.
  - Harness.Tests: the H6 fakes play v2; time bounds moved by +150 s; test-only unknown id 0x11 → 0x21.
- Client EditMode: GameEndReason rename, source-string checks, new copy and exit-line tests.

## Acceptance
| Check | Result |
|---|---|
| `dotnet build -warnaserror` | 0 warnings, 0 errors |
| Server.Tests (`Category!=BalanceSweep`) | 899/899 |
| Harness.Tests | 635/635 |
| Unity EditMode | 1224/1224 (`docs/qa/u6a-runs/editmode.xml`) |
| Harness, 3 runs (fresh `--sandbox` server + fresh normal server, `--expect-los`, threshold 100) | 100 / 100 / 100 |

- Harness reports: `docs/qa/u6a-runs/harness-run1..3.json`.
- H6 sync presses: 5–6 per survivor, all Good, 0 Fail.
- H6 sight counts: 5263–5265 visible and 3012–3027 blocked-and-absent.

## Sweep (BotBalanceTests, 40 seeds per config, not tuned)
| Config | Survivor win | Median (p25/p75) | End reasons | Time-up |
|---|---|---|---|---|
| 1v7 | 0% | 416 s (322/543) | resolved 20, all down 15, time up 5 | 13% |
| 1v3 | 0% | 210 s (137/265) | all down 27, resolved 13 | 0% |
| 1v1 | 0% | 291 s (213/538) | all down 36, time up 4 | 10% |
| idle killer + 7 | 100% | 584 s (573/633) | resolved 39, collapse 1 | 0% |

- 1v7 per match: 6.88 captures, 0.73 rescues and 6.00 executions.
- 1v7: 87.5% of matches end without TimeUp; AllResolved plus Collapse alone is 50%.
- `U2dQaSweepTests` (QA, the U2d win bands) fails 4 of 4 configs. Those are U6d's tuning targets, so I left them as they are.

## Stubs / deferred
- There is no server RTT probe, so `RttSeconds` is 0 except through the test hook.
- The Killer HUD line "After the Exit opens" (Firewall) still keys on an open exit. The copy belongs to U6c-ui.
- The client draws one exit (`Map.Exit`) and has no switch, hatch or sync UI (U6c-ui).

## Attempt 2 (no execution, user decision 2026-11-03)
- **Rule:** a touch never changes a Lagging survivor. The timer alone advances it: 60 s → count 2 with 30 s → Eliminated. Normal captures still count. `ExecutionGraceLeft` is always 0 (deprecated, layout unchanged). GDD §4 is updated. Client HUD files are untouched.
- **Tests:**
  - The execution tests were retargeted.
  - New R7 test: touches for the whole timer change nothing.
  - Flash's Silent test was wrong, not the product: the killer stood 1.0 m away, inside the 1.2 m capture radius. The test now also waits out a random Packet Afterimage.
- **Verified:**

  | Check | Result |
  |---|---|
  | Build with `-warnaserror` | clean |
  | Harness.Tests | 635/635 |
  | EditMode | 1224/1224 |
  | Harness, 3 fresh runs | 100/100 each |
  | Server.Tests | 922/924 |

- **Open (needs a decision, left unchanged):**
  - `MedianMatchLengthReport` (survivor case): 6 of 15 time-outs with a Normal bot, against a bound of ≤4.
  - U2c phantom classifier AUC: 0.850, against a ceiling of <0.85.
  - Cause: survivors live longer; the phantom code is unchanged.
- **Sweep:** the Killer still wins about 100%. 1v7: 20% TimeUp, 12.9 captures and 10.2 rescues per match.
