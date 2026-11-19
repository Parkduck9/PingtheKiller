You are the implementer for unit B1-balance, attempt 1, of "Ping the Killer": a 1v7 asymmetric game with a Unity 6000.3.25f1 client, a .NET raw-TCP server and a Shared netstandard2.1 library. Fable will review your work; Claude Opus (orchestrator) judges. Save this brief verbatim as `docs/briefs/B1-balance-a1.md` in your worktree.

## Where
- Worktree: `C:/Unity/MultiPlayGame/PtK-work/b1-balance`, branch `claude/b1-balance`, from main 7b23663. LFS is checked out.
- Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/integ-1117b/Client/Library` into `Client/Library`. Skip `ArtifactDB-lock`.
- Another unit (M1-minors, client-only: the Killer swing watch, the attack prompt, the intro card dismissal) runs in parallel in `PtK-work/m1-minors`. Do not touch those client files.

## Goal
The user played the merged build and says: "Make the Killer stronger at 1v7 too."

Current Survivor win % (seed sets 1–40 / 1001–1040; 240 fresh seeds in brackets), from `docs/qa/INTEG-R-qa-a1.md`:
- 1v7: 50 / 53 (50). main before the playtest work was 63 / 53 (56). Captures per match are flat, 9.90 → 9.99.
- 1v3: 53 / 48 (55). Before: 66.
- 1v1: 33 / 43 (38).

## Targets (Survivor win %, BOTH fixed seed sets AND a 240-seed mean)
- **1v7: 38–46 %, 240-seed mean about 42 %.** Nothing may sit on a band edge. The Killer must be clearly stronger at 1v7 than now, and captures per match must rise visibly.
- **1v3: 40–55 %.** Do not make 1v3 much more Killer-sided than now.
- **1v1:** inside its existing test band.
- **1v7 match length:** median ≥ 7 min. No stomps: report the share of 1v7 matches that end before 4 min, and keep it under 10 %.

## How
1. **Diagnose first.** Measure why the Killer gains little at 1v7. Candidates:
   - rescue chains with 6 helpers;
   - hacking throughput with 7 routers;
   - several Signal targets diluting the Killer's attention;
   - escape timing.
   Use the server's BalanceSweep and any sim tooling under `Server.Tests` or `Tools/`. Write the per-setup numbers you find into the report.
2. **Prefer real rule changes that a human player will feel** over bot-only tuning. The user plays as a human against bots or humans, so a bot-only change mostly changes the simulation. Make rules scale with the Survivor count where that is the cause.
   Good levers, in no fixed order:
   - terminals required, or hack and route rates, scaled by the number of Survivors;
   - rescue time, or a rescue penalty for repeat downs;
   - Lagging duration;
   - the swing recovery and range, within ±15 % of today's values;
   - a Killer skill cooldown;
   - exit-switch time.
3. Bot changes are allowed only when bots are unrealistically good or bad compared with a human, and you must say which.
4. **Do not change:**
   - the masking rules;
   - the protocol: no new packets or fields. If a change needs one, stop and report;
   - the client, except a copy or number that must follow a rule change (for example an intro card or How to play number, HUD_SPEC or GDD).
5. **Record every change** and its effect in a before/after table: per setup, per seed set, and the 240-seed mean.
6. Update the BalanceSweep test bands so they fit the new targets, and keep them non-trivial: for example 1v7 30–55.

## Hard rules
- Never push, and never merge into main. You MAY commit on `claude/b1-balance`, ending each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Never send keyboard or mouse input to the desktop.
- Never kill a process you did not start, and stop every server and exe you start.
- Leave every Unity editor you did not launch alone; the user has editors open.
- Do not spawn subagents.
- Keep your scripts in your own scratchpad subfolder, e.g. `<scratchpad>/b1-balance/`, never the shared root.
- After each Unity run, revert files that changed only in line endings (`git diff --ignore-cr-at-eol --quiet`).
- **Ports: 7781 (sandbox) and 7782 (H6).**

## Gates (on your final tree)
- `dotnet build PingTheKiller.sln -warnaserror`
- Server.Tests: `Category!=BalanceSweep` and `Category=BalanceSweep`
- Harness 3×, fresh servers each time (see the AGENTS.md commands), `--threshold 100 --expect-los`
- Harness.Tests, only if you touch `Tools/`
- Unity EditMode with an ABSOLUTE `-testResults` path. This is needed if any Shared constant or client copy changed.
- BuildWindows, and one live offline Killer run at 1280x720 with 7 bots, about 300 s: `-ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-chase -ptk-shots <dir> -ptk-quit-after 300 -logFile <path>`. Look at the shots, and report captures and the result.

## Report
- Write `docs/qa/B1-balance-impl-a1.md` in English, at most 350 words: the diagnosis, the changes, the before/after table, the gates with counts, what is unverified, and the risks.
- Write `docs/qa/B1-balance-commit-message.txt`.
- Commit, then reply with a summary.
