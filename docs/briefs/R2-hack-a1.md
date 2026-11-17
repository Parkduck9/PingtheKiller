# Brief R2-hack: replace the passive hack hold with an active "packet routing" minigame (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 120 minutes. Two other units run in parallel (R1-killer, R3-help).
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/r2-hack`, branch `claude/r2-hack`, from main 2213343.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/integ-1117/Client/Library` into `Client/Library`, skipping `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start, and stop every server you start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
  - After each Unity run, revert files that changed only in line endings.
  - **Harness ports:** use **7783 (sandbox) and 7784 (H6)**, not 7777/7778. Other units run harnesses in parallel.
- **Read first:**
  - `AGENTS.md` (architecture rules are absolute: server authority, per-recipient masking, input sanitising);
  - `docs/ui/SLOP_DEFINITION.md`;
  - `docs/ui/HUD_SPEC.md` (the Sync lock and hacking sections, and §4.10 for the U10 holo panel);
  - `docs/GDD-v2.md`;
  - the `game-feel` skill under `.claude/skills/`.

## Playtest feedback (Korean, translated)

"Hacking is boring."

## Facts found (verified in code)

- **Hacking is a 45 s hold of E,** standing still within 1.5 m (`GameServer.Terminals.cs`, `GameConstants.cs`). Co-hack rates are ×1.8 and ×2.4.
- **The only interaction is the sync check:** every 8–16 s, press Space in a window (`SyncLockBar.cs` client). Great gives +1 point, Good +0, and a fail -5 points plus 2 s `Overloaded` for the Killer.
- That is about 5 decisions per terminal, with almost no stakes.

## Protocol allocations (fixed)

- `C2S_HackInput = 0x0019` is yours. The payload is `[byte RouteId][byte Direction]`.
- `0x0017` and `0x0018` belong to R1. `ActionKind 7` belongs to R3. `TerminalFlags` bit 4 (`Locked`) belongs to R1: R1 will add a gate so no one can hack a Locked terminal. **Do not change the terminal wire layout.**
- You may append route fields to `S2C_SelfState`. It goes only to the hacker.
- **Do not bump the protocol version constant.** The orchestrator bumps it once at integration. Update the golden frames and byte-parsing tests that you touch.

## Spec

### S1. Packet routing (server-authoritative)

1. **Start.** While a Survivor hacks (E held, in range, terminal not done), the server gives that hacker a **route**: 4 directions from {Up, Down, Left, Right}, generated from the server RNG. Send it in that hacker's `SelfState`: route id, length, packed directions and current index.
2. **Inputs.** The client sends `C2S_HackInput(routeId, direction)`.
   - Sanitise: unknown route ids are dropped, and inputs are rate-limited to at most 12 per second.
   - **Correct direction:** the index advances.
   - **Full route:** a **burst of +8 points** (100 points per terminal), then a new route.
   - **Wrong direction:** the route resets to a new one, -3 points, and the terminal gets `Overloaded` for 1.5 s (the existing flag, which the Killer sees). That is the risk.
3. **Passive progress.** While E is held, progress keeps going at a slower base: 100 points per **90 s**, solo.
   - A focused player finishes a terminal in about 30–40 s solo; an idle holder takes 90 s.
   - Tune the numbers so the average human-like bot finishes in about 40–45 s. Keep the current overall match pacing.
4. **Co-hacking.** Each hacker gets their own route. Bursts add up, and the base co-hack multipliers apply to the passive part.
5. **Remove the sync check entirely:** server, constants, packets if any, bots and dev flags.
   - Replace `-ptk-auto-sync great|good|miss` with `-ptk-auto-route fast|slow|sloppy`. A dev bot for the human's own route: fast has a 0.15 s interval, slow 0.5 s, and sloppy makes 20 % errors.
   - Update every test and harness scenario that used sync checks (U6f/U6g; check `Tools/Harness`, `Server.Tests`, `Tools/Harness.Tests`). Keep their intent, e.g. a timing-fairness or RTT probe adapted to route input, or retire it with a reason.
6. **Bots** solve routes with human-like timing: 0.3–0.6 s per key, about 8 % errors. Only bots, never the Killer view, get to know a route.
7. **Masking.**
   - The route goes only to the hacker. Others see only progress.
   - The Killer sees nothing new except the existing `Overloaded` flash on a mistake.
   - Survivor-to-Survivor: allies see hacking as now.
8. **Logs:** one line per completed route and per error, rate-limited per player.

### S2. Client

1. **Input while hacking** (E held and a route present):
   - WASD and the arrow keys send `C2S_HackInput` instead of movement, and the client sends zero movement. Make sure the server does not cancel the hack for this.
   - Releasing E returns WASD to movement immediately.
   - Gamepad: the d-pad or the left stick flicked.
2. **Route panel** (SLOP; it replaces the Sync lock bar's spot and budget, and must not cover the character's feet):
   - 4 arrow cells; the current one is highlighted;
   - a correct key gives a short pulse;
   - a completed route gives a brief `+8 %` flash;
   - an error gives a red shake and the line `Route dropped · the Killer was alerted`.
   - Use only existing colour tokens. Respect Reduced motion.
   - Retire `SyncLockBar` and its copy cleanly. Remove dead files, and keep IP-audit's rename work consistent: no "skill check" wording.
3. **Prompts:**
   - The hack prompt changes to `Hold [E] to hack Terminal N · x% · route the packets with WASD — you will be Signal`, or shorter if SLOP length rules require it.
   - The first route of the match also shows a one-line hint, `Press the arrows in order`.
4. **U10 holo panel tie-in** (`World/InteractionMotion*.cs`, `HoloPropFx`):
   - The panel's keystroke pulse fires on real route inputs for the own character, instead of the fixed 0.17 s beat.
   - For other Survivors, keep the fixed beat, because their inputs are not received.
5. **Sounds:** a soft tick per correct key, a rising chord on route completion, and a glitch on an error. Procedural, `V2Cues` style.

### S3. Docs

- GDD-v2: replace the sync check section with "Packet routing (11/17, R2)", in Korean like the rest.
- HUD_SPEC: replace the Sync lock section with the route panel, marked `(11/17, R2)`.

## Tests

- **Server:**
  - route generation is deterministic per seed;
  - correct, wrong and complete inputs, with the points;
  - rate limit and stale route ids;
  - passive rate;
  - co-hack;
  - only the hacker receives the route;
  - golden frames;
  - bot route solving;
  - the terminal completion time distribution for bots (about 40–45 s solo average).
- **Client:**
  - the input mapping while hacking vs moving;
  - the panel state machine;
  - copy;
  - the holo pulse hook.
- **Balance:** BalanceSweep before and after; keep the 1v7 Survivor win rate inside 35–65 % and report the numbers. R1 makes the Killer stronger in parallel; the orchestrator retunes after the merge.

## Gates

- `dotnet build PingTheKiller.sln -warnaserror`
- Server.Tests (`Category!=BalanceSweep`, and `Category=BalanceSweep`)
- Harness 3× on 7783/7784, threshold 100, `--expect-los`, fresh servers
- Harness.Tests
- Unity EditMode, with an ABSOLUTE `-testResults` path
- BuildWindows
- Live offline Survivor runs at 1280x720 with `-ptk-dev-hack` and each `-ptk-auto-route` mode. Shots: the route panel mid-route, completion and error, and the holo panel pulsing.

## Owned files

- `Shared` (only the allocations above);
- `Server.Core` hacking/terminal code, constants and bots' hacking;
- the client hacking input, route panel, sounds and the `InteractionMotion` pulse hook;
- the harness and tests that used sync checks;
- the docs sections above.

Expected merge overlap with the parallel units:
- `ClientApp.cs` and `Terminals.cs`: R1 adds a Locked gate there.
- Keep each edit small and local.

## Report

- Write `docs/qa/R2-hack-impl-a1.md` in English, at most 350 words: the design as built, the numbers, the completion-time distribution, balance before and after, tests, gates with counts, shot paths, what is unverified, and risks.
- Write `docs/qa/R2-hack-commit-message.txt`.
