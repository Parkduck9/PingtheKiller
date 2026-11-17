# Brief R1-killer: a real Killer attack, chosen skills, stronger Killer skills (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 120 minutes. Two other units run in parallel (R2-hack, R3-help).
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/r1-killer`, branch `claude/r1-killer`, from main 2213343.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/integ-1117/Client/Library` into `Client/Library`, skipping `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start, and stop every server you start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
  - After each Unity run, revert files that changed only in line endings.
  - **Harness ports:** the other units also run harnesses. Use ports **7781 (sandbox) and 7782 (H6)**, not 7777/7778.
- **Read first:**
  - `AGENTS.md` (architecture rules are absolute: server authority, per-recipient masking, input sanitising);
  - `docs/ui/SLOP_DEFINITION.md`;
  - `docs/ui/HUD_SPEC.md`;
  - `docs/GDD-v2.md`;
  - the project skills `game-feel` and `unity-animation` under `.claude/skills/`.

## Playtest feedback (Korean, translated)

"As the Killer, attacking doesn't work. The skills only matter while Survivors hack or when the doors open; they are weak. The Killer is too weak. Players start without knowing their own abilities."

## Facts found (verified in code)

- There is **no attack input**. Capture is automatic: any Normal, non-invulnerable Survivor within `CaptureRadius` 1.2 m, every tick (`GameServer.Rules.cs` `Capture()`). Nothing tells the player this.
- **Skills:** the server picks 1 of 4 at random (`GameServer.Lobby.cs` ~171). The HUD shows only the skill name; the effect text is only in the Esc menu (`PlayMenuCopy`).
  - Packet afterimage is passive, so pressing it does nothing.
  - Firewall block needs an exit switch in progress, so it is useless until the end.
  - Overclock freezes the Killer for 1 s afterwards.
  - Ping exposes for only 1.5 s.

## Protocol allocations (fixed; the other units use the neighbouring values)

- `C2S_Attack = 0x0017`: yours. Choose the payload; e.g. `[uint Seq]`, since the server uses the latest look yaw.
- `C2S_ChooseSkill = 0x0018`: yours. Payload `[byte SkillId]`.
- `C2S_HackInput = 0x0019`: R2. Do not use it.
- `ChaseFlags` bit `16 = Swinging`: yours, set during the wind-up and the recovery.
- `TerminalFlags` bit `4 = Locked`: yours, for Firewall block on terminals.
- `ActionKind 7 = Rescue`: R3. Do not use it.
- **Do not bump the protocol version constant.** The orchestrator bumps it once at integration. Update golden frames and byte-parsing tests that your changes touch.

## Spec

### S1. Killer attack (server-authoritative)

1. **Remove automatic proximity capture.** A capture now happens only from a swing.
2. **Starting a swing.** `C2S_Attack` starts a swing when the Killer is:
   - Normal;
   - not stunned, vaulting or breaking;
   - not already swinging.
   Sanitise it like any input: drop a stale `Seq` and rate-limit.
3. **Wind-up (0.20 s).** The Killer moves at 50 % speed.
4. **Hit check at the end of the wind-up:**
   - Candidates are Survivors that are Normal, not invulnerable, within **2.2 m** (2D), within **±35°** of the Killer's current look yaw, and with **line of sight** (reuse the existing wall and obstacle LOS helper).
   - The nearest candidate is captured, using the existing capture consequences: `CaptureCount`, Lagging, elimination.
   - One capture per swing.
5. **Recovery:**
   - after a hit: 0.6 s at 0 % speed;
   - after a miss: **1.0 s at 40 % speed**, so a Survivor can escape;
   - no new swing until the recovery ends.
6. **Interrupts.** A shutter stun interrupts the wind-up, with no hit.
7. **Bots:**
   - The bot Killer swings when a target blip is within 2.0 m and roughly in front. It uses only `KillerBotView` data, as now.
   - Phantom/decoy blips can bait a swing (good).
   - Bots never see more than the human Killer does.
8. **Masking.** The swing is visible to the Survivors who already receive the Killer entry (the `Swinging` flag). The Killer gets no new information about Survivors.
9. **Logs:** one line per swing (hit/miss) per Killer, rate-limited.

### S2. Killer client

1. **Inputs:** left mouse button = attack (Killer only); gamepad right trigger.
2. **Animation:** play the existing U8 swipe (`CharacterMotion`, `KillerAttackKick`, `MotionTiming`) from the `Swinging` flag.
   - A hit keeps the U8 impact, hit-stop and flash.
   - A miss gets a short whoosh (procedural, like the `Audio/V2Cues.cs` style) and a tiny first-person arm sweep. No impact FX on a miss.
3. **HUD prompt** (SLOP copy): `Click  Attack` when any visible blip is within 3 m. On a miss, the result line says `Missed` briefly.
4. **Survivor view:** the Killer model visibly winds up, so Survivors can react.

### S3. Skill choice in the lobby (both roles)

1. **Server side:**
   - Each human sends `C2S_ChooseSkill` while in the lobby. The server validates it against the player's role at match start.
   - If a player chose nothing, or the role is Random, they get a random valid skill as now. Bots get random skills.
   - The chosen skill is shown in the lobby state per slot: append a byte to the lobby slot.
   - `S2C_MatchStart` already carries the skill.
2. **Lobby UI:**
   - For the player's role (or for both roles while the role is Random), list the skills with name, effect line and cooldown, and let the player pick one.
   - The effect lines come from one copy source. Extend `PlayMenuCopy` with Killer effect strings and update the Survivor ones if needed.
   - The R3 unit will read these strings for the match intro card, so keep them in that one place with stable method names: `PlayMenuCopy.SurvivorEffect(skill)` and a new `PlayMenuCopy.KillerEffect(skill)`.
3. **HUD:** the skill block also shows the one-line effect under the name, within the HUD_SPEC budget at 1280x720.

### S4. Stronger Killer skills (keep SkillIds 0–3)

- **Ping scanner:** exposure 1.5 → **3.0 s**.
- **Overclock:** remove the 1 s freeze. After the 3 s boost, apply 0.3 s at 60 % speed.
- **Firewall block:**
  - If an exit switch is in progress: as now, lock the switch for 5 s.
  - Otherwise: lock the **unfinished terminal with the highest progress** for **8 s**. Set the `TerminalFlags.Locked` bit. Hackers on it are interrupted, and no one can hack it while it is locked.
  - It fails only if no terminal has progress.
  - Survivors see `Terminal N · Locked · 8 s`. The Killer sees the lock on its terminal marker.
- **Packet afterimage → Packet trace:**
  - It becomes an active skill: every Survivor within **12 m** of the Killer is forced Signal for **2.5 s**. Reuse the existing forced-exposure mechanism that Ping uses.
  - Rename the player-facing name to `Packet trace` and update the copy, HUD glyph and U9 signature mapping if needed. Keep the shape distinct from Ping, e.g. a short local burst rather than a sweep.
- **Cooldowns:** all 12 s, as now.
- **U9 skill FX:** keep the viewer and safety rules (HUD_SPEC §14). Update §14 for the changed skills.

### S5. Balance

- The user says the Killer is too weak. After S1–S4, target the 1v7 Survivor win rate at **45–55 %**.
- The test band stays 35–65 %, so tests must keep passing.
- Tune swing numbers within ±20 % if needed, and report it.
- Run BalanceSweep before and after. Report win rates and captures per match for 1v1, 1v3 and 1v7.

### S6. Docs

- GDD-v2: add a section "Killer attack and skills (11/17, R1)", in Korean like the rest of the GDD.
- HUD_SPEC: the attack prompt, the miss line, the lobby skill picker and the skill block effect line, marked `(11/17, R1)`.

## Tests

- **Server:**
  - swing hit and miss: range, angle, LOS, invulnerable, Lagging;
  - wind-up and recovery speeds;
  - stun interrupt;
  - one capture per swing;
  - no capture without a swing;
  - rate limit and stale `Seq`;
  - skill choice: validation, default random, bots;
  - each reworked skill;
  - the masking of `Swinging`;
  - the golden frames.
- **Client:**
  - left-click → `C2S_Attack`, Killer only;
  - prompt selection;
  - the miss line;
  - lobby picker copy and selection;
  - the HUD effect line.
- **Harness:** if any scenario relies on proximity capture, update it to swing, and keep every scenario's intent.

## Gates

- `dotnet build PingTheKiller.sln -warnaserror`
- Server.Tests (`Category!=BalanceSweep`, and `Category=BalanceSweep`)
- Harness 3× on 7781/7782, threshold 100, `--expect-los`, fresh servers
- Harness.Tests
- Unity EditMode, with an ABSOLUTE `-testResults` path
- BuildWindows
- Live offline runs at 1280x720:
  - Killer with `-ptk-chase`, about 240 s: swings hit and miss; shots of the wind-up, the hit and the miss.
  - Survivor, about 200 s: seeing the Killer's wind-up.
  - Lobby shot of the skill picker: use the menu offline path; find dev flags in `Client/Assets/Scripts/Dev`, or add a dev-only shot.

## Owned files

- `Shared` (only the allocations above);
- `Server.Core` combat, skills and lobby code;
- the bot Killer;
- Killer client input and HUD;
- the lobby UI;
- `PlayMenuCopy`;
- U9 skill copy and glyph for Packet trace;
- tests;
- the harness, only if needed;
- the docs sections above.

Expected merge overlap with the parallel units:
- `ClientApp.cs`, `GameServer.Rules.cs` (R3 changes rescue there; you change capture) and `Terminals.cs` (R2 rewrites hacking; you add only the Locked gate).
- Keep each edit small and local.

## Report

- Write `docs/qa/R1-killer-impl-a1.md` in English, at most 350 words: the design as built, the numbers, balance before and after, tests, gates with counts, shot paths, what is unverified, and risks.
- Write `docs/qa/R1-killer-commit-message.txt`.
