# Brief U6d-bots: bot AI v2, attempt 2 (fix list)

- Implementer: Codex `gpt-6.1-sol`, effort high.
- QA: Gemini Flash.
- Same worktree and branch as attempt 1: `C:/Unity/MultiPlayGame/PtK-work/u4h`, `claude/u6d-bots`. Attempt 1 is uncommitted in the working tree; build on it.
- Never push. Never merge.

The scope, rules and owned files of `docs/briefs/U6d-bots-a1.md` still apply. Attempt 1 met every band. QA (`docs/qa/U6d-bots-qa-a1.md`) and the orchestrator found the problems below. Fix them with the same approach.

## Fix list

### F1. Major: the Killer idles when every terminal was visited recently

- **Where:** `KillerBrain.Patrol`. A not-done terminal visited within `revisit` (90-150 s) is skipped. When all are skipped, `_patrol = -1` and the Killer stops and spins in place, repeating every 2 s until the window expires.
- **Evidence:** 1v1 seed 26 (seeds 1001-1040) is a 900 s time-up with 0 captures and 2/3 terminals.
- **Fix:**
  - Never leave the Killer without a destination. When the revisit filter empties the list, fall back to the not-done terminal visited least recently, and prefer ones with a recent Signal.
  - If every terminal is done and no exit is powered yet, patrol a far nav spot.
  - The Killer may stand still only while kicking, looking around after arriving (`LookMin`-`LookMax`), closing the hatch or guarding a switch.
- **Test:** all not-done terminals are marked visited within the window, and there are no blips. Over 30 s, the Killer travels at least 15 m and never stands still longer than `LookMax` + 0.5 s.

### F2. Major (orchestrator): survivors freeze while a still Killer stays in sight

- **Where:** `SurvivorBrain`. `danger = view.KillerVisible || ...`. The camp rule releases only an unseen Killer. Your own test change (`SurvivorCampTests`: "continuous sight still requires hiding in v2") confirms it.
- **Problem:** a Killer that stands still in view (idle, frozen as in F1, or a camping human) keeps every survivor that can see it hiding forever. A human survivor would go and work somewhere out of its sight. This is the survivor half of the seed 26 deadlock.
- **Fix:**
  - Extend the camp rule to a visible Killer. It counts as camping when it has not moved, and Threat has not risen, for `CampSeconds` (20 s).
  - A camping Killer in sight no longer counts as danger. The bot then works at a terminal that the Killer cannot see (use `map.SegmentBlocked` from the Killer to the terminal) and is not the avoided terminal.
  - Any Killer movement, or a Threat rise, makes the danger acute again and triggers hiding.
  - Keep hiding on sight as the default reaction, as spec 2 says.
- **Test:** the Killer is visible, still, and more than `ThreatLevel2Distance` away for 60 s. Within 40 s the bot hacks a terminal out of the Killer's line of sight. If the Killer then moves, the bot hides again.

### F3. Minor: unguarded division by speed

`CanRescueBeforeEscape` divides by `view.Speed` with no guard. Use `Math.Max(0.1f, view.Speed)`, as `RescueTarget` does.

### F4. Minor: no terminal stickiness

- **Where:** `ChooseTerminal` resets `_terminal` every think, and the score is `-Progress*200 + distance`.
- **Problem:** a bot hacking terminal A abandons it the moment a slot opens at a terminal with even 1 more progress point.
- **Fix:** keep the current terminal while all of these hold:
  - it is not done;
  - it is not in the danger or camp-avoid set;
  - its slot is still valid;
  - no other terminal has at least 15 more progress points.
- **Test:** a bot hacking at progress 40 does not move to an open terminal at 45. It does move to one at 60.

### F5. Minor (orchestrator): 1v3 matches lost with no rescue at all

- **Evidence:** in the 1v3 sweep on seeds 1001-1040, seeds 3, 5, 11, 22, 29 and 40 end with 3 captures, 0 rescues, and 2-3 Lagging timeouts. Every one of them is after 4/4 terminals, so it is the end game.
- **Task:** find out why nobody rescues there. Possible causes:
  - switch duty or exit preference outranks rescue;
  - the Killer guards the switch next to the Lagging ally and the bots hide;
  - `RescueTarget` drops the target as unreachable.
  
  If it is a brain bug, fix it. Rescue a Lagging ally while the exits are powered but not yet open, when the timer allows. Report the cause in one or two sentences.

### F7. Minor (orchestrator, live check): bots walk into a visible Killer on the way to the exit

- **Evidence:** offline exe, idle human Killer + 7 bots (`docs/qa/U6d-runs/live-killer.log`).
  - After `exit 1 open`, players 6 and 7 were captured by the motionless Killer while heading out.
  - Nobody rescued them, and both died in the collapse.
- **Cause:** attempt 1 removed the `cautious` / `_fleeUntil` check on the open-exit branch ("Open exits take priority over hiding").
- **Fix:** while heading to an open exit, keep clear of a Killer in sight.
  - If the straight path or the exit itself passes within about `ThreatLevel3Distance` of the Killer, prefer another open exit.
  - Otherwise route via a waypoint that keeps a distance of at least `CaptureRadius` + 2 m.
  - Never freeze: if no safer option exists, go anyway.
  - A Lagging ally captured near a Killer that is not moving (camp rule, F2) is a valid rescue target while the collapse clock allows.
- **Test:** a visible, still Killer stands between the bot and an open exit. The bot reaches the exit, or another one, without ever coming within `CaptureRadius` + 1 m of the Killer.

### F6. Balance margins

After the fixes, all bands must still pass on both seed sets.
- QA flagged 1v7 win 38 % and 1v1 win 18 % (seeds 1-40) as 3 points from the floor. Aim for at least 40 % in 1v7 and at least 20 % in 1v1 on both sets.
- This is a target, not a new gate. Report the numbers. Do not widen any band.

## Gates

Run the same commands as attempt 1:
- `dotnet build PingTheKiller.sln -warnaserror`;
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`;
- `dotnet test Server.Tests --filter "Category=BalanceSweep"` (paste the tables);
- `dotnet test Tools/Harness.Tests`, in the background.

The restored bounds must still hold: MatchLength ≤ 4, AUC < 0.85, KS p ≥ 0.02.

## Output

- **Do not try to commit.** Git cannot write the worktree index from your sandbox. Leave the changes in the working tree; the orchestrator commits after judging.
- Report at `docs/qa/U6d-bots-impl-a2.md`, at most 300 words plus the sweep tables. Cover:
  - F1-F6, each with what changed and its test;
  - the F5 cause;
  - before and after numbers;
  - what is unverified;
  - risks.
- Update `docs/qa/U6d-bots-commit-message.txt` so it describes attempts 1 and 2 together.
