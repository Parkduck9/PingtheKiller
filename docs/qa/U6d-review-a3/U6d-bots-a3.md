# Brief U6d-bots: bot AI v2, attempt 3 (final, different approach)

- **Implementer:** Claude Opus. Codex is out of credits. Opus is a different implementer instance from attempt 2.
- **QA:** Gemini Flash if its sign-in is back, otherwise Claude Sonnet.
- **Where:** same worktree and branch (`C:/Unity/MultiPlayGame/PtK-work/u4h`, `claude/u6d-bots`). Attempts 1 and 2 are uncommitted in the working tree; build on them.
- Never commit, push or merge.
- This is the **last attempt**. If it fails, the orchestrator stops and reports to the user.

The scope, rules and owned files of `docs/briefs/U6d-bots-a1.md` still apply. F1-F7 of `docs/briefs/U6d-bots-a2.md` must stay satisfied.

## Why attempt 2 failed

QA (`docs/qa/U6d-bots-qa-a2.md`, M1): in 1v1, 7 of 40 matches on each seed set run 900 s to a time-up with **0 captures**.
- Seeds 9, 15, 22, 34, 38, 39 and 40 on the 1-40 set. Seeds 15 and 38 have 3/3 terminals and powered exits.
- The lone survivor hides or freezes, and the Killer circles one switch or sweeps the last terminal. Neither side makes progress.

The live offline check of attempt 2 (`docs/qa/U6d-runs/a2-live-surv.log`) shows the same stall. The setup was 1 idle human survivor and 6 bots, so 4 of 7 must escape to win.
- Terminals completed at 75, 225, 348 and 479 s. After 479 s, no terminal completed up to 837 s; terminal 4 sat at 60 %.
- The last event of any kind was at 638 s.

The invariant must also cover a team that includes one idle survivor. Add a sweep config or a unit-level scenario for "6 bots + 1 idle human survivor": it must not stall for 150 s.

Attempts 1 and 2 tuned constants and patched individual triggers. Each patch moved the stall somewhere else.

## Different approach: a progress guarantee, not more trigger tuning

Make "the match keeps moving" an explicit, tested invariant, and add one mechanism on each side that enforces it.

### A. Invariant test (write it first)

Add a stall detector over the existing balance sweep runs. Reuse the `BotBalance` log parsing, extending it if needed. For every match in every BalanceSweep config and both seed sets:
- **No stall window.** No window of 150 s or more passes with no progress event. A progress event is any of these: terminal progress rising, switch progress rising, a capture, a rescue, an escape, a hatch event.
  - Use what the server log or the match state already exposes.
  - If terminal or switch progress is not in the log, sample the server state in the sweep harness. That is test code, which is fine.
- **No silent time-up.** No `TimeUp` match ends with 0 captures.

Report how many stall windows attempt 2 has before you change anything. That is your red test.

### B. Survivor commitment (SurvivorBrain)

- Each bot tracks `noProgressFor`: the seconds since it last advanced anything itself (hack progress at its terminal, switch progress while holding, rescue progress) or reached a goal.
- When `noProgressFor` reaches about 25 s, the bot enters **commit mode** until it makes progress again.
  - A Killer in sight that is still or far away no longer interrupts it.
  - It is interrupted only by Threat 3, or by the Killer moving toward it within about 10 m. Hide briefly then (flee), and keep the timer running.
  - It picks its work target (terminal, switch or open exit) as far as possible from the Killer's last known position. It prefers targets out of the Killer's line of sight, but accepts one in sight if there is nothing else.
  - This replaces the "visible idle Killer blocks every terminal in sight" problem (QA m3). Do not leave a path where every remaining target is refused forever.
- **Lone survivor, or a stalled leading switch** (QA M1 mechanism): `SwitchDuty` must let a survivor switch to another powered switch when the current one is guarded or stalled. Choose the one farther from the Killer's last known position. Add a test with 1 survivor and 2 powered switches.

### C. Killer search (KillerBrain)

- **No Normal blip for about 45 s** (search mode): stop circling one switch or terminal.
  - Sweep far nav spots and the less recently checked terminals or switches, in a route that covers the map.
  - Use Ping scanner when it is ready.
  - Kicks keep their cooldown.
- **Guard choice skips exits that are already `Open`** (QA m1). Guard the best unopened powered switch. If all are open, patrol between the open exits.

### D. Cleanups from QA

- **m5:** name `GameServer.Phantoms.cs` in the report, say why it changed, and drop the stray newline change.
- **m6:** bound the cost of `PlanEscape`. Cache the plan for at least 1 s, or cap the candidate count. Report sweep wall time before and after.
- **m2:** trace one of the 1v3 seeds 2, 4, 15, 18 or 39 (seeds 1-40, `cap 3 res 0 lagTimeout 3`). Say in one sentence whether that is a Killer chain or a rescue gap, and fix it if it is a gap.

## Bands (unchanged, never edit)

| Config | Win | Median | Time-ups |
|---|---|---|---|
| 1v7 | 35-65 % | 540-660 s | ≤ 10 % |
| 1v3 | 25-75 % | ≤ 660 s | ≤ 20 % |
| 1v1 | 15-85 % | | ≤ 30 % |
| idle Killer + 7 | ≥ 90 % | | more exit escapes than collapse losses |

**Targets, not gates:**
- 1v7 win 40-60 % and median ≥ 555 s on both seed sets;
- 1v1 time-ups ≤ 10 %;
- MatchLength bot time-ups ≤ 3 (gate ≤ 4).

If the bands and the invariant cannot both be met:
- stop and report the closest numbers;
- never widen a band;
- never weaken the invariant below 150 s.

## Gates

- `dotnet build PingTheKiller.sln -warnaserror`.
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`.
- `dotnet test Server.Tests --filter "Category=BalanceSweep"`, plus the new invariant test. Put the invariant in the BalanceSweep category if it reuses the sweep runs.
- Skip `Tools/Harness.Tests`; the orchestrator runs it.
- The restored bounds must hold: MatchLength ≤ 4, AUC < 0.85, KS p ≥ 0.02.
- If survivor hack or walk pacing changes, phantoms must follow.

## Output

- Report: `docs/qa/U6d-bots-impl-a3.md`, at most 350 words plus the final sweep tables. Cover:
  - the invariant red result before the change and green after, with the stall-window counts per config;
  - A-D, each with what changed and its test name;
  - the m2 trace;
  - before and after numbers (attempt 2 as before);
  - deviations;
  - what is unverified;
  - risks.
- Update `docs/qa/U6d-bots-commit-message.txt` for attempts 1-3.
