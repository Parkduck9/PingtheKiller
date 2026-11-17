# Brief R3-help: explicit rescue, downed-ally markers, and onboarding (attempt 1)

- **Implementer:** Claude Sonnet. **QA:** Fable.
- **Time box:** about 100 minutes. Two other units run in parallel (R1-killer, R2-hack).
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/r3-help`, branch `claude/r3-help`, from main 2213343.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/integ-1117/Client/Library` into `Client/Library`, skipping `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start, and stop every server you start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
  - After each Unity run, revert files that changed only in line endings.
  - **Harness ports:** use **7785 (sandbox) and 7786 (H6)**, not 7777/7778.
- **Read first:**
  - `AGENTS.md` (architecture rules are absolute);
  - `docs/ui/SLOP_DEFINITION.md` (mandatory for all copy and UI);
  - `docs/ui/HUD_SPEC.md`;
  - `docs/GDD-v2.md`.

## Playtest feedback (Korean, translated)

"I don't know how to heal. The game is unfriendly. Players start without knowing their own abilities."

## Facts found (verified in code)

- **Rescue is automatic.** Any Normal Survivor within 1.5 m for a continuous 3 s raises a Lagging Survivor (`GameServer.Rules.cs` ~126-146). There is no key.
  - The only explanation is a 3 s banner on the downed player's own screen.
  - Helpers get a text prompt only within 6 m.
  - There is no distance tag or edge arrow for downed allies on the Survivor HUD. Only an amber body tint.
- **There is no rules screen, tutorial, goal text or controls legend.**
  - The match starts with meters and "No power" tags only.
  - The skill block shows only the skill name and `[Q]`. The effect text exists only in the Esc menu (`PlayMenuCopy`).

## Protocol allocation (fixed)

- `ActionKind 7 = Rescue` is yours.
- `0x0017`, `0x0018`, `0x0019`, `ChaseFlags` bit 16 and `TerminalFlags` bit 4 belong to the other units. Do not use them.
- **Do not bump the protocol version constant.** The orchestrator bumps it once at integration.

## Spec

### S1. Explicit rescue (server-authoritative)

1. A Normal Survivor rescues a Lagging ally by **holding E** within 1.5 m for 3 s.
   - The automatic proximity rescue is removed.
   - Releasing E or leaving range resets the count, as now.
   - The rescuer's own `ActionKind` is `Rescue` (7), with `ActionProgress`. The `Rescuing` player flag stays as it is.
2. **E priority when several actions are in reach** (document it): rescue a Lagging ally, then a chase prop (Q5), then an exit switch, then a terminal.
   - Check how `Terminals.cs` and `GameServer.ChaseProps.cs` pick the E action today, and slot rescue in at the top with a minimal change.
3. **Bots** hold interact to rescue, as they do now by standing near. Update the bot code.
4. **Masking.** Nothing new for the Killer. Rescuing already makes a Survivor Signal.
5. Keep Emergency restore (Q skill) as is.
6. **Balance.** Run BalanceSweep before and after, and keep the 1v7 Survivor win rate inside 35–65 %.

### S2. Rescue clarity (client, Survivor)

1. **Helper prompt** within 1.5 m: `Hold [E]  Restore <name>`. While held, show a progress bar using the existing ally style.
2. **Downed-ally markers on the Survivor HUD.** For every Lagging ally: a distance tag (`<name> · Lagging · 23 m`) and an off-screen edge arrow.
   - Reuse the existing marker and edge infrastructure (`MarkerLayer`, `EdgeSpread`/`TagLayout`) and its spacing rules.
   - Use the `Signal` amber already used for Lagging bodies, or the ally colour; pick per SLOP's one-meaning-per-colour and record it in HUD_SPEC.
3. **Downed player's HUD.** It keeps a persistent line, not just a 3 s banner: `Lagging · an ally can restore you with [E] · nearest ally 18 m · 54 s`. The nearest ally distance comes from the received state.

### S3. Onboarding (client)

1. **Match intro card**, shown for about 7 s at match start, and dismissable with any key:
   - the role and its goal in one line:
     - Survivor: `Hack {N} terminals, then hold an exit switch and escape.`
     - Killer: `Capture every Survivor before they escape.`
   - your skill: name, effect line and key `[Q]`, plus the cooldown;
   - a controls legend for the role:
     - Survivor: `WASD Move · Hold E Hack / Restore / Exit switch / Shutter · While hacking: WASD routes packets · Q Skill · Esc Menu`
     - Killer: `WASD Move · Left click Attack · Hold E Kick terminal / Break shutter / Close hatch · Q Skill · Esc Menu`
   - The parallel units are adding the Killer left-click attack (R1) and the WASD packet routing while hacking (R2). Write the copy as given here; the orchestrator aligns wording at integration.
   - Skill effect text comes only from `PlayMenuCopy.SurvivorEffect(skill)` and a new `PlayMenuCopy.KillerEffect(skill)`.
     - R1 is adding `KillerEffect` in parallel. Add a minimal `KillerEffect` with the current strings; the integration keeps R1's version.
     - Do not duplicate skill strings anywhere else.
   - The card must not cover the character's feet (HUD_SPEC §4.10). Respect Reduced motion.
2. **"How to play" screen** from the main menu: a button under Offline, with two pages, Survivor and Killer.
   - Each page: the goal, the controls, the status ladder (Normal → Lagging → Eliminated, plus how a rescue works), Signal/Silent in one line, and all skills of that role with their effects.
   - Follow the SLOP layout and copy rules.
3. **First-time contextual hints**, once per match each, short, on the existing prompt or result line:
   - Survivor:
     - the first time within 6 m of a terminal: `Hold [E] at a terminal to hack it`;
     - the first time a Lagging ally exists: `<name> is Lagging · hold [E] next to them to restore`;
     - the first time the Survivor becomes Signal: `You are Signal: the Killer can see you`.
   - Killer:
     - the first time a blip is within 6 m: `Get close and left click to attack`.
4. **HUD skill block:** R1 adds the one-line effect under the skill name. Do not edit the skill block.

### S4. Docs

HUD_SPEC sections for the rescue prompt, the ally markers, the intro card, How to play and the hints, marked `(11/17, R3)`.

## Tests

- **Server:**
  - rescue needs E held;
  - range and reset;
  - E priority;
  - bots rescue;
  - no automatic rescue;
  - the `Rescue` ActionKind on the wire.
- **Client:**
  - prompt selection;
  - ally marker placement rules and the off-screen arrow;
  - downed HUD line;
  - intro card content per role and skill;
  - the How to play pages' copy;
  - the hints fire once;
  - SLOP copy length rules.
- **Harness:** update any scenario that relied on proximity rescue.

## Gates

- `dotnet build PingTheKiller.sln -warnaserror`
- Server.Tests (`Category!=BalanceSweep`, and `Category=BalanceSweep`)
- Harness 3× on 7785/7786, threshold 100, `--expect-los`, fresh servers
- Harness.Tests
- Unity EditMode, with an ABSOLUTE `-testResults` path
- BuildWindows
- Live offline runs at 1280x720, Survivor and Killer. Shots:
  - the intro card for both roles;
  - How to play, both pages;
  - a downed-ally marker and edge arrow;
  - the rescue prompt and progress;
  - the downed HUD line.
  Use `Client/Assets/Scripts/Dev` flags; e.g. `-ptk-rescue` exists.

## Owned files

- Server rescue code in `GameServer.Rules.cs`: only the rescue function and the E priority;
- bots' rescue;
- Survivor HUD prompts and markers;
- new UI files for the intro card, How to play and hints;
- the main menu button;
- tests;
- the harness, only if needed;
- the docs sections above.

Expected merge overlap with the parallel units:
- `ClientApp.cs`, `GameServer.Rules.cs` (R1 changes capture there), `PlayMenuCopy` (R1) and the HUD files.
- Keep each edit small and local. Put new UI in new files.

## Report

- Write `docs/qa/R3-help-impl-a1.md` in English, at most 350 words: what was built, the copy, balance before and after, tests, gates with counts, shot paths, what is unverified, and risks.
- Write `docs/qa/R3-help-commit-message.txt`.

## Addendum (user request, mid-attempt): S5 Getting up after a rescue

"When someone is healed and gets up, make them unable to move."

- After a rescue (the E-hold rescue or Emergency restore), the Survivor enters a **1.5 s get-up state**: speed 0, no interact, no skill. They stay inside the 2 s invulnerability.
- Allocation: `PlayerFlags` bit 32 = `GettingUp`, sent to recipients that already receive the entry. Masking is unchanged.
- The client plays the Mixamo `GettingUp` clip from the flag. The HUD line is `Getting up…`.
- Tests cover both rescue paths, the invulnerability, the wire and masking, the resume, and the pose mapping. Docs and the report include it.
