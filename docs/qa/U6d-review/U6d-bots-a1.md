# Brief U6d-bots: bot AI v2, attempt 1

- Implementer: Codex `gpt-6.1-sol`, effort high.
- QA: Gemini Flash.
- Worktree: `C:/Unity/MultiPlayGame/PtK-work/u4h`, branch `claude/u6d-bots`, cut from main `53f4ef9`.
- Never push. Never merge.

## Goal

Bots must play the GDD v2 rules well enough that all-bot matches reach the end game (exits, collapse, hatch), are balanced, and finish on time. The source of truth is `docs/GDD-v2.md`: §2-§4 for the rules, §6 for the bot rows and the balance targets.

## Baseline

Main `53f4ef9`, `dotnet test Server.Tests --filter "Category=BalanceSweep"`. Log: main checkout `docs/qa/U6d-baseline/sweep-main-53f4ef9.log`.

| Config | Survivor win | Median | Time-ups | Terminals per match |
|---|---|---|---|---|
| 1v7 | 0-3 % | 757-790 s | 28-33 % | about 2 of 5 |
| 1v3 | 0 % | 446-468 s | | |
| 1v1 | 0 % | 279-349 s | | |
| idle Killer + 7 | 98-100 % | 631-636 s | | |

- **Idle Killer + 7:** most matches end by Collapse (32-34 of 40). Survivors power the exits but rarely get out in time.
- **Live offline check** (main checkout `docs/qa/playcheck-1105.md`): 330 s gave 0-2 terminals.

## Diagnosis from code reading (verify it)

### Survivors (`Server.Core/Runtime/SurvivorBrain.cs`)

- `TerminalCap=1` and `ActiveWorkers=1` (around line 60): one worker at a time, two with four or more Normal survivors. More workers are added only at 210 s and 360 s. The others idle at hideouts.
- The hack duty cycle is about 20 %: bursts of 2-4 s, then pauses of 8-16 s. A bot with no terminal waits 25-50 s. The resulting pace is about 225 s per 45 s terminal.
- There is no collapse awareness. There is no rescue-timer awareness: rescue is gated by `RescueQuietSeconds=5` and Threat ≤ 1.

### Killer (`Server.Core/Runtime/KillerBrain.cs`)

- No predictive chase: it goes to the last blip plus aim error for 5 s.
- Patrol only weights done versus not-done terminals.
- It never kicks terminals. The server mechanic exists: `KickHeld`/`KickTerminal`, `GameServer.Terminals.cs` lines 118-144.
- Exit guarding is only a +4 patrol weight.

### Phantom tracks

`GameServer.Bots.cs` lines 15-21 copy the `SurvivorBrain` hack and walk constants. If survivor pacing changes, phantoms must change with it. Otherwise Indistinguishability (U2c AUC, KS) breaks.

## Spec

1. **Survivors: spread (GDD §6).**
   - All Normal survivors who are not on rescue, switch or escape duty work terminals. At most 2 per terminal.
   - Prefer terminals with more progress, then nearer ones.
   - Remove the 1-worker cap and the 210 s / 360 s ramp.
   - Retune the hack bursts and pauses so the targets below are met. Hiding and fleeing remain the reasons to stop.
2. **Survivors: hide (GDD §6).**
   - Trigger when the Killer is in line of sight, or Threat ≥ 2.
   - Move behind cover that breaks the Killer's line of sight (keep `PickRefuge`), stop, and stay Silent.
   - Resume once Threat ≤ 1 and the Killer has been unseen for a few seconds.
   - Keep the merge-1104 camp rule: an unseen Killer standing still for 20 s means go to another terminal.
3. **Survivors: rescue split.**
   - Only the nearest eligible Normal survivor goes. The others keep working.
   - Rescue must respect the Lagging timer: commit when the timer left (30 s or 60 s stage) is enough to arrive and hold 3 s. Relax the quiet and Threat gates as time runs out.
4. **Survivors: end game.**
   - When the exits are powered, the nearest Normal survivors hold a switch. Prefer the exit with the most switch progress.
   - Once an exit is open, everyone who is Normal heads to it and escapes. Rescue first only if the Lagging ally is close and collapse time allows. Lagging survivors count the collapse at double speed (GDD §3).
   - With a single survivor left, go for the hatch when it is visible (keep the existing behaviour).
5. **Survivors: skills.** Keep `UseSkills`. Traffic overload must fire while being chased (Killer in sight within about 10 m, or Threat 3 while moving). Stealth relay fires while hacking with Threat ≥ 1.
6. **Killer: predictive chase.**
   - Track the anonymous blip it chases by proximity between ticks. Estimate its velocity from the last observations.
   - Aim at the predicted position for up to 1.5 s after losing sight, then search around that point.
   - Only data the human Killer receives may be used (`KillerBotView`).
7. **Killer: patrol and kick.**
   - The Killer cannot see terminal progress; the server strips it, and the human Killer sees 0 or 100. So rank not-done terminals by recent Signal blips seen near them (within about 6 m in the last 60 s), then by distance.
   - At a not-done terminal with no survivor in sight, hold Interact for the 2 s kick (GDD §2). Then move on.
   - Do not revisit Lagging survivors (keep this).
8. **Killer: end game.** When any exit is powered, guard the exit with the higher `SwitchProgress`. It is in the view and the human Killer HUD gets it too. Patrol between that switch and nearby terminals. Use Firewall block as it does now. Close a visible hatch (keep this).
9. **Killer: skills.** Keep Ping scanner and Overclock use on chases. Tune them if needed.
10. **Phantoms.** Update the phantom pacing in `GameServer.Bots.cs` to match the new survivor hack and walk pacing. Then make U2c and Indistinguishability pass at the restored bounds (spec 12).
11. **Information fairness.**
    - Brains see only `BotViews` data. No new fields that the matching human client does not get.
    - `BotFairInformationTests` and the Indistinguishability tests must pass unchanged, or be strengthened.
12. **Restore the temporarily relaxed bounds.**
    - `MatchLengthTests.cs:101`: bot timeouts ≤ 4 (currently 7).
    - `U2cAdversarialTests.cs:614`: AUC < 0.85 (currently 0.86).
    - KS (`IndistinguishabilityTests.cs:140`, gate p ≥ 0.01): the recorded p is 0.0166, which is thin. Report the measured p, which must be ≥ 0.02.
13. **Balance targets (GDD §6).** Gate them in `BotBalanceTests.Configs` and assert win, median and timeouts, not just timeouts. Update `U2dQaSweepTests` to the same v2 bands.
    - **1v7:** survivor win (half or more escaped) 35-65 %, median 540-660 s, time-ups ≤ 10 %.
    - **1v3:** win 25-75 %, median ≤ 660 s, time-ups ≤ 20 %.
    - **1v1:** win 15-85 %, time-ups ≤ 30 %.
    - **Idle Killer + 7:** win ≥ 90 %, with most survivors escaping through exits, not Collapse. Report the end-reason split.
    - If a band cannot be met without breaking another rule, stop. Report the closest numbers and the trade-off. Do not widen bands on your own.
14. **Do not change** rules constants, the protocol, `Shared/`, the client, or the sync judge code in `GameServer.Terminals.cs`. Another unit, U6g, is changing RTT handling there and in `GameServer.cs`. Touch them only for bot hooks, and say why.

## Tests

- New unit tests for each behaviour above:
  - spread cap of 2;
  - hide trigger;
  - rescue under timer;
  - end-game switch and escape;
  - predictive aim;
  - patrol ranking;
  - kick;
  - exit guard.
- Use the existing brain test patterns: brains fed with `BotViews`.
- The balance sweeps (`Category=BalanceSweep`) must pass with the new bands.
- `MatchLengthTests` must pass at ≤ 4.

## Gates (run all, report numbers)

- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings.
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: all pass.
- `dotnet test Server.Tests --filter "Category=BalanceSweep"`: all pass. Paste the summary tables into the report.
- `dotnet test Tools/Harness.Tests`: all pass. It takes about 18 minutes, so run it in the background.
- The orchestrator runs these afterwards (you need not):
  - the harness scored run 3 times;
  - Unity EditMode and BuildWindows (Server.Core is compiled by Unity for offline mode, so keep netstandard2.1 / C# 9 and no LINQ in hot paths);
  - the live offline check.

## Rules

- Follow `AGENTS.md`.
- Owned files:
  - `Server.Core/Runtime/SurvivorBrain.cs`, `KillerBrain.cs`, `BotBrain.cs`, `BotViews.cs`, `GameServer.Bots.cs`;
  - `Server.Tests/**`;
  - `docs/qa/U6d-*`.
- Commit on the branch when green. The message is in English and ends with `Co-Authored-By: Codex gpt-6.1-sol`.
- Report at `docs/qa/U6d-bots-impl-a1.md`, at most 300 words plus the sweep tables. Cover:
  - what changed;
  - before and after numbers;
  - the restored bounds with measured values;
  - what is unverified;
  - risks.
