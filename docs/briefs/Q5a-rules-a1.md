# Brief Q5a-rules: shutters and vault rails, server/Shared/bots (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 90 minutes. A parallel client unit (Q5b) waits for your Shared protocol, so **S0 comes first.**
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/q5-rules`, branch `claude/q5-rules`, from main 3675877.
- **Hard rules:**
  - Never push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start.
  - Leave every Unity editor alone.
  - Do not spawn subagents.
  - The **only** commit you may make is the S0 Shared commit described below. Everything else stays uncommitted.
- Read `AGENTS.md`. The wire, authority, masking and threading rules are absolute.
- Read `docs/GDD-v2.md`. This unit is §7's deferred chase tools, with this game's own names.

## Design (user-approved direction; the 🔶 numbers are tunable)

The names are deliberately our own; never use another game's terms. The theme is a cyber city:
- a **Shutter** is a roll-down metal shutter in an alley mouth;
- a **Rail** is a low safety rail a Survivor can vault.

### Shutters (map: 10)

| State | Movement | Sight |
|---|---|---|
| `Up` | passable | — |
| `Down` | blocks movement, box across the gap, 1.2 m high | does not block sight (like low obstacles) |
| `Broken` | passable | — |

`Broken` is permanent for the match.

- **Pull (Survivor):** a Normal Survivor within 1.5 m of an `Up` shutter uses Interact.
  - The pull takes 0.3 s 🔶 and the Survivor cannot move during it. At the end the shutter is `Down`.
  - Anyone overlapping the shutter box at that moment is pushed out to the nearest free side; the puller stays on its own side.
- **Stun:** if the Killer's circle intersects the shutter box inflated by 0.6 m when it goes `Down`, the Killer is **stunned 2.0 s** 🔶.
  - While stunned it cannot move, capture, interact or use skills.
  - It is pushed to the side away from the puller.
- **Vault a `Down` shutter (Survivor only):** Interact within 1.0 m. It takes 1.0 s 🔶 and you end on the opposite side.
- **Break (Killer only):** hold Interact within 1.5 m of a `Down` shutter for 2.0 s 🔶. Moving or releasing cancels it. The result is `Broken`.

### Rails (map: 8)

- A low rail obstacle about 3 m long, 1.0 m high. It blocks movement, not sight.
- It has one vault point at its middle.
- **Vault:** Interact within 1.0 m of the vault point. You end mirrored on the other side.
  - Survivor: 0.8 s 🔶.
  - Killer: 1.6 s 🔶.

### Common rules

- During a pull, vault or break the actor cannot move, and the move input is ignored. Captures can still happen to a pulling or vaulting Survivor.
- **A Survivor is forced Signal during a pull or vault, and for 1.0 s after.** This keeps the "moving = visible" identity, because these are loud actions.
- **Interact priority** must be deterministic and documented. Proposed order: rescue > shutter/rail > hack/switch/kick, choosing the nearest target within its own range. Check the existing Interact pipeline and pick the minimal consistent rule.
- **Server authority:** every outcome is decided by the server. The client only sends the existing `C2S_Interact`.

## S0 — Shared protocol first (commit it, then signal)

1. **Static geometry in `Shared/Runtime/Game/`:** a new `WorldChaseProps` (or similar), following `WorldExits.cs`.
   - The shutter boxes and rail boxes, with each rail's vault point and axis.
   - Place them in FixedMap v2.1 alley mouths, loop corners and plaza edges, away from terminals, exits and spawns. Check every placement against the walls and obstacles in `Server.Core/Runtime/Map/FixedMap.cs`.
   - The client and server must read the same data.
2. **State on the wire.** One of:
   - a new packet `S2C_PropState` = 0x0017: `[byte shutterCount][byte state per shutter]`, sent on match start, on every change, and to anyone joining mid-match;
   - or an addition to `WorldState`.
   Pick one and justify it. Shutter state is public to every recipient. Keep the length validation strict.
3. **Per-player action flags** in the WorldState entry (or wherever `Hacking` lives), for animation: `Pulling`, `Vaulting`, `Breaking`, `Stunned`.
   - Masking is unchanged. A Silent entry is still omitted for the Killer, and an acting Survivor is Signal anyway.
4. Update `docs/PROTOCOL.md` to v0.7. Update the codec tests.
5. **Commit only `Shared/**`, `docs/PROTOCOL.md` and the codec/protocol tests.**
   - Message: `Q5a-rules S0: Shared protocol for shutters and vault rails`, ending with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
   - Then write the file `C:/Unity/MultiPlayGame/PtK-work/q5-rules/SHARED_READY`. It must contain the commit hash and a 10-line summary of the API: type names, flag bits, packet layout.
   - Do this within the first ~30 minutes.
   - After that commit, do not change `Shared/` again unless you must. If you must, write `SHARED_CHANGED` with the reason.

## S1 — Server rules (`Server.Core`)

1. **Movement collision:**
   - `Down` shutters and rails block movement.
   - Vault moves the actor across over the action duration, using the server path. No client position.
2. **Rules:** the pull, stun and push-out, break, and vault rules above, inside the 20 Hz game loop only.
3. **Line of sight:** unchanged. Low props never block sight. Confirm against the LOS code and the harness `--expect-los`.
4. **Logs:** one line per state change and per stun, for example `shutter 3 down by player=4 stun=killer`.

## S2 — Bots

- **Pathfinding:** the A* grid treats `Down` shutters as blocked, updated on state change, and rails as passable edges with a vault cost.
- **Survivor bot:** when chased (the Killer visible within 6 m) and an `Up` shutter lies within 3 m on its flight path, it runs past the shutter and pulls it once the Killer is close behind. It vaults rails when that lengthens the gap.
- **Killer bot:** it breaks a `Down` shutter that blocks its chase path when the detour is longer than about 8 m; otherwise it goes around. It vaults rails when that is shorter.
- **Fairness:** the bots keep the fair-information rule. They use only what their role's stream would show.

## S3 — Tests, harness, balance

1. **Server.Tests:**
   - each rule: pull, stun, no stun outside the zone, push-out, vault for both roles, break hold and cancel, priority;
   - Signal forcing;
   - the Killer stunned cannot capture;
   - masking unchanged;
   - the protocol round-trip;
   - late-join state.
2. **Harness:** stays 100/100 with `--threshold 100 --expect-los`. Use two fresh servers: 7777 with `--sandbox` and 7778 for H6. See `AGENTS.md` §3.
3. **BalanceSweep:**
   - Run it.
   - If the 1v7 survivor win rate leaves 35-65 % or the median leaves 9-11 min, tune the 🔶 numbers, or the bot use rate, and report.
   - If a balance test pins old numbers, update it only where the new mechanic legitimately moves it, and list each one.
4. **Harness.Tests:** full run, about 15-18 min, in the background.

## Gates

- `dotnet build PingTheKiller.sln -warnaserror`
- Server.Tests, both filters
- harness 3×
- Harness.Tests

You do not need a Unity run. Q5b covers the client. But `Shared/` must stay Unity-compilable: netstandard2.1, C# 9, no LINQ in hot paths.

Logs go in `docs/qa/Q5a-runs/`.

## Owned files

- `Shared/**`
- `Server.Core/**`
- `Server/**` (only if a flag is needed)
- `Server.Tests/**`
- `Tools/Harness/**` and `Tools/Harness.Tests/**` (only what the new packet or flags need)
- `docs/PROTOCOL.md`
- `docs/GDD-v2.md`: a new §8 "Shutters and rails (11/17)" with the final numbers
- `docs/qa/Q5a-*`

## Report

Write `docs/qa/Q5a-rules-impl-a1.md` in English, ≤ 300 words. Cover:
- the protocol choice;
- the placements (a list of coordinates);
- the rules;
- the priority;
- the bot behaviour;
- the balance before and after;
- the tests;
- the gate counts;
- the risks.

Also write `docs/qa/Q5a-rules-commit-message.txt`.
