# Brief INTEG-R: integrate R1-killer, R2-hack and R3-help, then align, version and rebalance (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/integ-1117b`, branch `claude/integ-1117b`, from main 2213343.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/integ-1117/Client/Library` into `Client/Library`, skipping `ArtifactDB-lock`.
- **Hard rules:**
  - Never push, and never merge into main.
  - You MAY commit on `claude/integ-1117b` (merge commits and fix commits) and merge the three unit branches into it.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start, and stop every server you start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
  - After each Unity run, revert files that changed only in line endings.
  - **Ports:** use **7793 (sandbox) and 7794 (H6)**.
  - Read `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md` and `CLAUDE.md`.

## Background

A playtest said:
- the Killer can't attack and is weak;
- hacking is boring;
- nobody knows how to heal;
- the game is unfriendly;
- players don't know their abilities.

Three units were built in parallel from main 2213343, each in its own worktree and branch:

| Unit | Branch | What it does | QA |
|---|---|---|---|
| R1-killer | `claude/r1-killer` | left-click swing attack (replaces proximity capture), lobby skill choice, stronger Killer skills (Ping 3 s, Overclock without freeze, Firewall on terminals with `TerminalFlags.Locked`, Packet trace replaces afterimage); protocol `C2S_Attack 0x0017`, `C2S_ChooseSkill 0x0018`, `ChaseFlags.Swinging = 16` | under Fable QA; the orchestrator will tell you when it is committed |
| R2-hack | `claude/r2-hack` (committed 1dc6259) | packet routing minigame replaces the hack hold and the sync check; `C2S_HackInput 0x0019`; route fields in SelfState | PASS after an orchestrator one-line fix |
| R3-help | `claude/r3-help` (committed b15c68f) | E-hold rescue (`ActionKind 7`), get-up 1.5 s (`PlayerFlags.GettingUp = 32`, not in the Killer view), ally markers, intro card, How to play, first-time hints | PASS with minors |

Each branch has its brief in `docs/briefs/`, its impl report and its QA report in `docs/qa/`.

## Current state

- `claude/r2-hack` is merged.
- A merge of `claude/r3-help` is **in progress with 7 conflicts**:
  - `ClientApp.cs`, `DevArgs.cs`, `DevCapture.cs`, `SurvivorHud.cs`;
  - `Server.Tests/ProtocolV02Tests.cs`, `Server.Tests/QA/U2bAdversarialTests.cs`;
  - `Shared/Runtime/Game/PlayerTypes.cs`.
- Resolve them by keeping both units' behaviour. Read both briefs to understand the intent of each side. Commit the merge.

## Spec

1. **Merge R3 now. Merge R1 when the orchestrator messages you that it is committed.**
   - After each merge: `dotnet build -warnaserror`, then the Unity compile via EditMode.
   - Clamps must cover every new value: `ActionKind` up to 7, `PlayerFlags` up to 63, `ChaseFlags` including 16, `TerminalFlags` including 4.
2. **Protocol version → v0.8.** Bump the protocol version constant once. Update `docs/PROTOCOL.md` with:
   - the new packets: 0x0017, 0x0018, 0x0019;
   - the removed sync packets: 0x0011–0x0013;
   - the new flags;
   - the SelfState route fields;
   - the lobby slot skill byte.
   Update the golden frames and version tests.
3. **Cross-unit behaviour:**
   - **E priority:** rescue, then chase prop, then exit switch, then terminal (R3).
     - Check against R2: hacking needs E plus routing; an E press near a Lagging ally must rescue, not hack.
     - Check against R1: a Locked terminal must refuse hacking and clear the route (R2's `RouteAdvertised`).
   - **The get-up state (R3):**
     - no hacking or routing;
     - no attack target? The rescued Survivor is invulnerable for 2 s anyway; make sure R1's swing respects invulnerability.
   - **The swing (R1)** must not hit through lowered shutters. R1 implemented that; keep it.
4. **Copy alignment** (R3 QA minors 1, 2 and 6; SLOP §8):
   - one term set: `Rescue`/`Restore`. Use SLOP's fixed term `Rescue` unless HUD_SPEC already decided otherwise, and make every string consistent;
   - the intro card and How to play must describe the real controls: left-click attack, WASD routing while hacking, hold E to rescue, and get-up;
   - fix the outdated "The Killer's touch" wording;
   - fix the HUD_SPEC §3 outdated role line;
   - the R1 skill effect strings come from `PlayMenuCopy` only: R1's `KillerEffect` wins over R3's stub;
   - the R1 lobby wrap issue: the Packet trace line leaves a lone `s.` on the second line.
5. **Small R3 QA minors** (only these, quickly):
   - #3: consume the Killer hint only when it is actually shown (`PromptFree`);
   - #4: the intro card is not dismissed by movement keys; it is dismissed by Esc, Enter, Space or a mouse click, or times out;
   - #8: the ally tag must not overlap the own character's head at about 1 m (suppress it within 2 m);
   - #9: `AllyMarkers.Build` must not allocate every frame.
6. **Balance retune** (the most important part):
   - The combined changes alter the pacing: a stronger Killer, slower average hacking, explicit rescue and get-up.
   - Run BalanceSweep and the independent QA sweeps on the merged tree for 1v1, 1v3 and 1v7.
   - The user wants the Killer clearly stronger than before. Targets for the Survivor win rate on **both** seed sets:
     - 1v7 at **45–55 %**;
     - 1v3 at **40–65 %**;
     - 1v1 inside its test band.
     - Nothing may sit on a band edge.
   - Levers, in order:
     1. bot skill parameters (route key timing and errors, swing reach, rescue willingness);
     2. route burst and passive rates;
     3. Lagging duration;
     4. swing numbers within ±15 %.
   - Do not change the masking rules. Record each change and its effect in a table.
7. **Docs:** update GDD-v2 and HUD_SPEC wherever the merge changed numbers or wording.

## Gates (all on the final merged tree)

- `dotnet build PingTheKiller.sln -warnaserror`
- Server.Tests, both filters
- Harness 3× on 7793/7794, fresh servers, threshold 100, `--expect-los`
- Harness.Tests
- Unity EditMode, with an ABSOLUTE `-testResults` path
- BuildWindows
- Live offline runs at 1280x720:
  - Survivor, about 300 s: intro card, routing, rescue, get-up, ally markers, shutters;
  - Killer with `-ptk-chase`, about 300 s: swing hit and miss, a skill, the lobby picker shot.
  Put shots in `docs/qa/INTEG-R/` and LOOK at them.

## Report

- Write `docs/qa/INTEG-R-impl-a1.md` in English, at most 450 words: the conflicts and how each was resolved, the cross-unit fixes, the copy changes, the balance table (before and after, per setup and seed set), the gates with counts, the shots, what is unverified, and the risks.
- Commit your work on `claude/integ-1117b` with clear messages, ending each with:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
