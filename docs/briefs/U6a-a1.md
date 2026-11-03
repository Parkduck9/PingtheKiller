# Brief U6a — attempt 1: Rules v2 (server + protocol v0.4)

**Implementer:** Claude Opus subagent. This is the quota fallback: Codex is out of credits on 2026-11-03 (re-checked).
**QA:** Antigravity Gemini Flash.
**Next unit:** U6c builds the client HUD on top of this protocol.

Read first:
- `AGENTS.md`
- `docs/GDD-v2.md` §1–§4. It is in Korean; this brief is the binding English summary, and where they differ this brief wins.
- `docs/GDD.md` §3 for the rules that do not change.

Do **not** commit. Work only in your worktree.

## Goal

Implement the DbD-style match rules on the server and put them on the wire (protocol **v0.4**). Keep the client compiling with a **minimal** adaptation; the real HUD is U6c.

Give the bots just enough new behaviour that all-bot matches can end through the new rules. Smart behaviour is U6d.

## Owned files

| Area | Files |
|---|---|
| Shared | `Shared/Runtime/**`: packets, `PacketId`, `PlayerTypes`, `GameConstants`, `Protocol` |
| Server.Core | `Server.Core/Runtime/**`: rules, simulation, visibility, skills, bots (minimal), `Map/FixedMap.cs` (data additions only) |
| Tests | `Server.Tests/**`, `Tools/Harness/**`, `Tools/Harness.Tests/**` (follow the protocol change) |
| Client | Only what is needed to compile and keep the existing HUD meaningful: `Client/Assets/Scripts/**` readers of `ExitOpen` / `ExitLocked` / `GameEndReason`, plus their EditMode tests. No new UI. |
| Docs | `docs/PROTOCOL.md` (or the protocol table in `docs/PLAN.md` §3) updated to v0.4; `docs/qa/U6a-impl-a1.md` |

## Rules (server-authoritative)

Constants go in `GameConstants`, named, with a GDD reference comment.

### R1. Match length
`MatchLengthSeconds = 900`. A timeout ends the match (see R9).

### R2. Terminals ("generators")

**Count.** The required/placed table is already 3/5, 4/7, 5/8 (U6b).

**Hacking.**
- `TerminalHackTimeSeconds = 45` for one hacker.
- Several hackers on the same terminal use effective rate multipliers: 1 → 1.0, 2 → 1.8, 3 or more → 2.4. This replaces the old linear multiplier with a cap of 3.
- Hacking still sets `Signal` and the `Hacking` flag.

**Progress resolution.**
- Progress on the wire is a byte 0–100.
- Server-side, keep a float. Done means ≥ 100.

**Regression (killer "kick").**
- The Killer holds Interact for **2.0 s** within 1.5 m of a terminal whose progress is greater than 0 and less than 100, and which is not already regressing.
- On completion: progress −5 percentage points, then **−0.5 points/s** continuously.
- Any survivor starting to hack that terminal clears regression.
- Progress never drops below 0.
- While the Killer holds Interact for a kick, the Killer cannot move. Moving or releasing cancels it.

### R3. Sync check ("skill check")

**Trigger.**
- Each survivor who is actively hacking gets checks at random intervals, uniform **8–16 s**.
- The interval timer runs only while that survivor hacks. Use per-survivor RNG from the match seed.

**Check message.** The server sends `S2C_SyncCheck` to that survivor only, with:
- `CheckId` (uint, increasing);
- `TerminalId`;
- `Duration` = 1.1 s, the time for the needle to sweep 0 → 1;
- `ZoneStart`, a random value in [0.45, 0.8];
- `ZoneSize` = 0.12;
- `GreatSize` = 0.035, which is the first part of the zone.

**Press.** The client answers with `C2S_SyncPress { CheckId }`, at most one per check.

**Judging.**
- The server estimates the press moment as `arrivalTime − rtt/2`. Use the session's smoothed RTT from Ping/Pong; offline and bots use 0.
- Fraction = (press moment − send time) / Duration.
- Allow `SyncToleranceSeconds = 0.15` on both edges of the zone, converted to a fraction.

**Outcomes.** The server replies with `S2C_SyncResult { CheckId, Result }`.

| Result | Value | Condition | Effect |
|---|---|---|---|
| Great | 2 | in the great part | +1 point |
| Good | 1 | in the zone | 0 |
| Fail | 0 | outside the zone, or no press within Duration + tolerance | −5 points, terminal **Overloaded** flag for 2 s |

- If the survivor stops hacking before the press, the check is cancelled silently, with no penalty and no result.
- The Overloaded flag is visible to everyone in the terminal state. The client uses it to alert the Killer.

**Bots.**
- Survivor bots press automatically.
- Each bot has `BotSyncSuccessRate`, default 0.85 (U6d tunes it). On success it presses at the zone middle (great 20 % of the time); otherwise it misses.
- Implement this inside the server; no network traffic.

### R4. Exits (two)

`FixedMap.Exits` has 2 points (U6b). Add `FixedMap.ExitSwitches`: one switch point per exit, placed 4 m from the exit point toward the map centre.
- It must be standable.
- Validate this in a test.
- Update `MapSvg` to draw the switches.

**Power.** When terminals done ≥ required, both exits become **Powered**. Everyone gets this through the state.

**Switch.**
- A Normal survivor holds Interact within **1.5 m** of a powered, unopened exit's switch.
- The progress fills over **15 s**. Multiple survivors do not stack: use the max of one.
- Holding sets `Signal`.
- Releasing keeps the progress.
- At 100 % the exit is **Open**.

**Escape.**
- A Normal survivor whose position is within **2 m** of an **open** exit point becomes `Escaped`.
- An escaped survivor is out of the match and spectates.
- A Lagging survivor cannot escape.

**Firewall skill (Killer).**
- It targets the exit with the highest switch progress that is greater than 0 and not yet open.
- Effect: `Locked` for 5 s. A locked switch does not progress.
- Unusable (no cooldown spent) when no such exit exists.

### R5. Collapse

**Start.** Collapse starts at the first moment any exit becomes Open, or when the Killer closes the hatch (R6).

**Clock.**
- `CollapseSeconds = 120`. The global `CollapseLeft` counts down at 1×.
- Each survivor also has a personal collapse clock, starting at 120. It decrements at 1× while Normal and at **2×** while Lagging.
- When a survivor's personal clock hits 0 and they have not escaped, they become `Eliminated`.

### R6. Hatch

**Spawn.**
- When exactly **one** survivor remains Normal or Lagging (the others Escaped or Eliminated), and the match started with at least 2 survivors, the hatch opens at a random `FixedMap.HatchPoints` entry.
- Use the match RNG.
- Fix the U6b data bug first: hatch point 4 equals exit 1 at (76,76). Move it so that every hatch point is at least 6 m from every exit and switch. Add a test for this.

**Escape by hatch.** A Normal survivor within **1.5 m** who presses Interact escapes immediately.

**Close by Killer.**
- The Killer holds Interact for **1.0 s** within 1.5 m.
- On completion the hatch closes, both exits become Powered, and collapse starts unless it is already running.

**Visibility.** The hatch position is sent to a recipient only when the recipient is within **15 m** and the segment from recipient to hatch is not blocked by walls (`SegmentBlocked`). Otherwise the hatch is omitted, so neither side gets it for free.

### R7. Capture stages
Replaces GDD v1 §3.3 second capture.

**Catching a Normal survivor.**
- `CaptureCount += 1`.
- Count 1 → Lagging for **60 s**.
- Count 2 → Lagging for **30 s**.
- Count 3 or more → Eliminated.

**Touching a Lagging survivor.** The existing grace rule applies: 2 s, plus the Killer must leave and re-touch. The touch advances one stage:
- At count 1: count becomes 2, timer resets to 30 s.
- At count 2: Eliminated.

**Lagging timer running out.**
- At count 1: count becomes 2, timer becomes 30 s, still Lagging.
- At count 2: Eliminated.

Rescue, post-rescue invulnerability and the rest of §3.3 are unchanged. A rescue does **not** reset `CaptureCount`.

### R8. Match end

The match ends when no survivor is Normal or Lagging, or when the 900 s timeout fires.

**Winner.** Survivors win when `escaped × 2 ≥ startingSurvivors`; otherwise the Killer wins.

**`GameEndReason`.** Redefine for v0.4:

| Value | Name | Meaning |
|---|---|---|
| 0 | `AllResolved` | every survivor escaped or eliminated, and collapse was not the cause |
| 1 | `AllSurvivorsDown` | no survivor escaped and all are eliminated before collapse |
| 2 | `TimeUp` | the 900 s timeout fired |
| 3 | `Collapse` | the last unresolved survivors were eliminated by the collapse |

**`S2C_GameResult`** keeps `WinnerRole`, `Reason` and `EscapedIds`, and adds `EliminatedIds`.

### R9. Timeout
At 900 s every survivor who has not escaped is Eliminated. The winner follows R8.

## Protocol v0.4

- Bump the protocol version constant if one exists.
- Wire rules from `AGENTS.md` §1 are unchanged.
- Every new or changed packet gets round-trip and malformed-input tests (counts over max, enum out of range).

**`S2C_WorldState`**
- Remove `ExitOpen` and `ExitLocked`.
- After `TerminalsRequired`, add:
  - `CollapseLeft` (float, −1 when not running);
  - exits: count ≤ 2 of `ExitState { byte Id; float X; float Z; byte Flags (Powered=1, Open=2, Locked=4); byte SwitchProgress 0..100 }`;
  - hatch: `byte HatchVisible`, then, if 1, `float X, float Z`.
- `TerminalState` gains `byte Flags` (Regressing=1, Overloaded=2) after `Progress`.

**`S2C_SelfState`** appends:
- `float CollapseLeft` (personal, −1 when not running);
- `float ActionProgress` 0..1, the own current hold action: exit switch, kick or hatch close;
- `byte ActionKind`: 0 none, 1 switch, 2 kick, 3 hatch close.

**`S2C_GameResult`** appends `EliminatedIds`, count ≤ 8.

**New packets**

| Id | Packet | Fields |
|---|---|---|
| `0x11` | `S2C_SyncCheck` | `uint CheckId; byte TerminalId; float Duration; float ZoneStart; float ZoneSize; float GreatSize` |
| `0x12` | `C2S_SyncPress` | `uint CheckId` |
| `0x13` | `S2C_SyncResult` | `uint CheckId; byte Result` (0..2) |

**Input sanitizing**
- `C2S_SyncPress` for an unknown, expired or already-pressed `CheckId`, or from a non-survivor, is ignored.
- It never disconnects unless it is malformed.
- Harness test-only unknown ids must move past `0x13` (they used `0x11`).

**Visibility masking is unchanged:** Silent survivors are omitted for the Killer, and the Killer entry is omitted for survivors without line of sight.

## Minimal bot support (so all-bot matches finish)

**Survivor bots:**
- Answer sync checks (R3).
- When exits are powered, the nearest unresolved survivor goes to the nearest unopened exit switch and holds it. The others continue as before.
- When an exit is open, every survivor bot goes to the nearest open exit (NavGrid) and escapes.
- When the hatch is visible to a bot and it is the last survivor, it goes to the hatch.

**Killer bot:**
- Unchanged, except it closes a hatch it can see.
- No kicks yet (U6d).

## Tests (new `Server.Tests/Core/RulesV2Tests.cs` and protocol tests)

Cover every rule R1–R9 with deterministic scenarios, including:
- coop multipliers;
- kick timing, and kick cancel on move;
- regression clears on re-hack;
- sync fail/good/great and tolerance edges with an RTT offset;
- a cancelled check;
- switch hold/release/lock;
- escape only when open and Normal;
- collapse personal clock at 2× while Lagging;
- hatch spawn condition, visibility at 15 m and blocked, survivor escape, and Killer close starting the collapse;
- capture stages, including timeout stage advance;
- the winner majority boundary (for example 3 of 7 → Killer, 4 of 7 → Survivors, 1 of 2 → Survivors);
- every `GameEndReason`.

Update existing tests whose old rules changed. Keep their intent where the rule still exists, and delete or replace only tests of rules that GDD v2 removed. List each one in the report.

## Acceptance (paste the summary lines in the report)

1. `dotnet build PingTheKiller.sln -warnaserror` has 0 warnings.
2. `dotnet test Server.Tests --filter "Category!=BalanceSweep"` is all green.
3. `dotnet test Tools/Harness.Tests` is all green.
4. `dotnet test Server.Tests --filter "Category=BalanceSweep"` must run to completion. Report the numbers (win %, median length, timeout %, how matches ended) and do not tune them; U6d does that.
   - Required: at least 70 % of 1v7 all-bot matches end by `AllResolved`/`Collapse` rather than `TimeUp`. If not, the minimal bot support is insufficient, so fix the bots, not the rules.
5. Unity EditMode, all green and 0 compile errors:
   ```
   "/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "$PWD/docs/qa/u6a-runs/editmode.xml" -logFile docs/qa/u6a-runs/editmode.log
   ```
   - Close nothing else.
   - The first run in a fresh worktree imports assets for a long time.
6. Harness at threshold 100 with `--expect-los`, using a fresh `--sandbox` server and a fresh normal server per run (see `docs/PLAN.md` §5 for the exact command). Score 100.
   - If a harness check encodes an old rule (for example a single exit or the old capture rule), update the check to the v2 rule and say so in the report.

## Report

`docs/qa/U6a-impl-a1.md`, English, ≤ 400 words:
- the rule → code map (file:line per rule);
- the protocol diff;
- tests added and tests changed;
- all acceptance outputs;
- sweep numbers;
- anything stubbed or deferred.

Note "quota fallback: Codex out of credits" in the header.
