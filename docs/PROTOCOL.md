# Ping the Killer: wire protocol v0.8 (GDD v2, units U6g, Q3, Q5a, R1, R2, R3)

The source of truth is `Shared/Runtime/` (server and Unity compile the same files). This page covers the v0.4 layouts, v0.5 RTT probes, the v0.6 skill rejection, the v0.7 shutters and rails, and v0.8 (11/17: the Killer swing and lobby skill choice, packet routing instead of the sync check, the explicit rescue and get-up; `Protocol.Version`, documentation only). All integers and floats are little-endian. Counts are one byte, and a reader rejects any count above its maximum (`PacketFormatException`).

## Frame

`[int32 Length][ushort PacketId][Payload]`. `Length` is the size of `PacketId + Payload`. Values below 2 or above 16384 disconnect the sender.

## Packet ids

| Id | Packet | Dir | Changes |
|---|---|---|---|
| 0x0001-0x000F | Join, JoinResult, MoveInput, UseSkill, WorldState, SkillEffect, GameResult, Ping, Pong, LobbyState, StartMatch, MatchStart, Interact, SetBots, SetRole | | WorldState and GameResult changed |
| 0x0010 | S2C_SelfState | S→C | extended |
| 0x0011-0x0013 | (retired) | | v0.8: the sync check (`S2C_SyncCheck`, `C2S_SyncPress`, `S2C_SyncResult`) is removed (R2). The ids stay unused; a frame with one of them is an unknown id. |
| 0x0014 | S2C_RttProbe | S→C | v0.5: `uint Nonce` |
| 0x0015 | C2S_RttEcho | C→S | v0.5: `uint Nonce` |
| 0x0016 | S2C_SkillRejected | S→C | v0.6: `byte SkillId, byte Reason` |
| 0x0017 | C2S_Attack | C→S | v0.8 (R1): `uint Seq` |
| 0x0018 | C2S_ChooseSkill | C→S | v0.8 (R1): `byte SkillId` (a skill choice) |
| 0x0019 | C2S_HackInput | C→S | v0.8 (R2): `byte RouteId, byte Direction` |

## Killer attack and skill choice (0x0017-0x0018, v0.8, R1)

- **C2S_Attack** (4 bytes, `uint Seq`): the Killer's swing press. The server ignores it outside a match, from a
  Survivor and in the sandbox; drops a Seq not newer than the last one (reset on join and reconnect); counts at most 5
  presses per second. The swing itself is server-timed (GDD v2 section 9).
- **C2S_ChooseSkill** (1 byte): `0x00-0x04` a Survivor skill, `0x80 | 0x00-0x03` a Killer skill, `0xFF` random. Other
  values are rejected (`PacketFormatException`). Accepted in the lobby only.
- **Lobby `Slot`** gains a trailing `byte SkillChoice` (same encoding; bots `0xFF`). R1 a2: masked per recipient, each client receives only its own slot's choice and `0xFF` in every other slot. The server applies at most 4 `C2S_ChooseSkill` per player per second (the rest are dropped).
- **`ChaseFlags`** adds `Swinging = 16` (values above 31 are rejected); **`TerminalFlags`** adds `Locked = 4`
  (Firewall block on a terminal, sent to both roles; values above 7 are rejected).
- `SkillRejectReason.Passive` stays defined but is no longer sent (Packet trace is active).

## Shutters and rails (v0.7, GDD v2 section 8)

No new packet id. Shutter state rides at the **end of every `S2C_WorldState`** instead of a separate
`S2C_PropState`: the snapshot is self-healing (match start, late join and reconnect need no extra send path and no
ordering against `S2C_MatchStart`), and it costs at most 11 bytes per snapshot. Shutter state is public, so every
recipient gets the same list.

- Static geometry: `Shared/Runtime/Game/WorldChaseProps.cs` (`Shutters` 10, `Rails` 8, `ChaseProp` boxes with ids
  equal to their index). A rail's vault point is its centre; a prop is crossed along `(CrossX, CrossZ)`.
- `ShutterState`: `Up = 0`, `Down = 1`, `Broken = 2`. Values above 2 are rejected.
- `PlayerState` gains a trailing `byte Chase` (`ChaseFlags`): `Pulling = 1`, `Vaulting = 2`, `Breaking = 4`,
  `Stunned = 8` (v0.8: `Swinging = 16`, values above 31 are rejected). Masking is unchanged (a Silent survivor is still omitted for the killer;
  a pulling or vaulting survivor is Signal anyway).
- `S2C_SelfState.ActionKind` adds `Pull = 4`, `Vault = 5`, `Break = 6` with `ActionProgress` 0-1 (v0.8: `Rescue = 7`, values above 7 are rejected). Values above 6 were
  rejected.
- The client sends nothing new: shutters and rails use `C2S_Interact`.

## Server-timed RTT (0x0014-0x0015, v0.5)

Both packets have exactly four payload bytes (`uint Nonce`); short or trailing payloads are rejected.
The server probes each joined, connected online human in the lobby and match, starting on the first tick after joining,
then every 1 s with at most one outstanding nonce. Bots, offline sessions and unjoined connections receive none.
The client immediately queues the matching echo on its receive thread, bypassing the Unity main thread.

The server stamps the send and echo receipt with its own monotonic clock. Only an outstanding matching nonce counts;
duplicates, guesses and stale echoes are ignored. A probe expires at 1 s without changing the estimate.
Samples are clamped to 0–0.25 s; the first sets the estimate, then `RTT += (sample - RTT) / 8`.
Disconnect/reconnect clears the estimate and probe state. `C2S_Ping`/`S2C_Pong` remain unchanged and client-timed
for display only; the server never uses their client-supplied timestamps for judging.

## S2C_SkillRejected (0x0016, v0.6)

Exactly two payload bytes: `byte SkillId` (the requested id, echoed) and `byte Reason` (`SkillRejectReason`):
1 `NotInMatch`, 2 `NotYourSkill` (another id, or out of range), 3 `NotNormal` (Lagging, Eliminated or Escaped),
4 `Cooldown`, 5 `NoTarget` (Ping scanner without an active Survivor; Emergency restore without a Lagging ally in
radius), 6 `NoSwitchInProgress` (Firewall block), 7 `Passive` (Packet afterimage), 8 `Sandbox`. 0 and values above 8,
and short or trailing payloads, are rejected (`PacketFormatException`).
The server sends one to the caster only, for every refused `C2S_UseSkill`, at most one per player per tick (a use in
the same tick also counts as the answer). Bots receive none. Each reason is derivable from the caster's own stream.

## S2C_WorldState (0x0005)

The `ExitOpen` and `ExitLocked` bytes of v0.3 are removed.

| Offset | Field | Type | Notes |
|---|---|---|---|
| 0 | Tick | uint32 | |
| 4 | TimeLeft | float | 900 s match |
| 8 | ThreatLevel | byte 0-3 | survivors only |
| 9 | TerminalsRequired | byte | |
| 10 | CollapseLeft | float | global collapse clock; -1 when not running |
| 14 | exit count | byte ≤ 2 | then `ExitState` × count |
| | HatchVisible | byte 0/1 | per recipient: 1 only within 15 m with a wall-free line |
| | HatchX, HatchZ | float, float | present only when HatchVisible = 1 |
| | terminal count | byte ≤ MaxTerminals | then `TerminalState` × count |
| | player count | byte ≤ MaxWorldEntries | then `PlayerState` × count (v0.7: + `byte Chase`) |
| | shutter count | byte ≤ MaxShutters (10) | v0.7: then `byte ShutterState` × count, indexed by shutter id |

- **ExitState** (11 bytes): `byte Id, float X, float Z, byte Flags, byte SwitchProgress (0-100)`.
  - Flags: `Powered = 1`, `Open = 2`, `Locked = 4` (the switch is locked by Firewall Block). Values above 7 are rejected.
- **PlayerState** (25 bytes, v0.7): `int32 Id, byte Role, byte Status, float X, float Z, float Yaw, byte Flags, float StatusTimer, byte CaptureCount, byte Chase`.
  - Flags (v0.8, R3): `Signal = 1, Afterimage = 2, Stealthed = 4, Hacking = 8, Rescuing = 16, GettingUp = 32` (the 1.5 s after a rescue, Survivor recipients only; the Killer's view never carries it). `Afterimage = 2` is no longer set since Packet trace replaced Packet afterimage (R1). Values above 63 are rejected.
- **TerminalState** (11 bytes): `byte Id, float X, float Z, byte Progress (0-100), byte Flags`.
  - Flags: `Regressing = 1`, sent to survivors only (the killer kicked the terminal); `Overloaded = 2`, sent to everyone for 1.5 s (`OverloadedSeconds`) after a wrong route key (v0.8, R2); `Locked = 4`, sent to everyone while a Firewall block holds the terminal (v0.8, R1). Values above 7 are rejected.
  - The killer still receives Progress as 0 or 100 only.

## S2C_GameResult (0x0007)

`byte WinnerRole, byte Reason, byte escapedCount ≤ 8, int32 EscapedIds[], byte eliminatedCount ≤ 8, int32 EliminatedIds[]`.

- Winner: Survivors when `escaped × 2 ≥ starting survivors`, otherwise the Killer.
- `GameEndReason` (values above 3 are rejected):
  - `AllResolved = 0`: every survivor escaped or was eliminated, and the collapse did not cause it.
  - `AllSurvivorsDown = 1`: nobody escaped.
  - `TimeUp = 2`: the 900 s timeout eliminated the survivors still in play.
  - `Collapse = 3`: the last survivors in play were eliminated by the collapse.

## S2C_SelfState (0x0010)

29 bytes (v0.8), sent only to the player it describes:

1. `float SkillCooldown`, `float RescueProgress`, `float InvulnerableLeft`, `float ExecutionGraceLeft` (unchanged).
   - `ExecutionGraceLeft` is **deprecated** (GDD v2 §4, 2026-11-03: no execution) and always 0. The layout is unchanged.
2. v0.4 fields:
   - `float CollapseLeft`: the survivor's personal clock, which runs at 2× while Lagging; the killer gets the global clock. -1 when not running.
   - `float ActionProgress` (0-1).
   - `byte ActionKind`: `None = 0`, `Switch = 1`, `Kick = 2`, `HatchClose = 3`; v0.7 `Pull = 4`, `Vault = 5`, `Break = 6`; v0.8 `Rescue = 7` (R3: a Survivor holding E next to a Lagging ally). Values above 7 are rejected.
3. v0.8 route fields (R2), filled only while the receiver hacks the terminal its route belongs to (otherwise all 0):
   - `byte RouteId`: 0 = no route (not hacking, or in the gap before the next route).
   - `byte RouteLength`: at most 4 (`HackRoute.MaxLength`); larger values are rejected.
   - `byte RouteDirs`: the directions packed 2 bits each, the first in the low bits (`RouteDirection`: `Up = 0`, `Down = 1`, `Left = 2`, `Right = 3`).
   - `byte RouteIndex`: the next direction to press, 0..RouteLength; larger values are rejected.

## Packet routing (0x0019, v0.8, R2)

- **C2S_HackInput**: `byte RouteId, byte Direction` (`RouteDirection`, values above 3 are rejected). Exactly two payload bytes.
- The server answers nothing. It drops the key when the RouteId is 0, unknown or stale, when the sender is not a Survivor
  hacking that route's terminal (released E, walked away, rescuing, getting up, a done or Locked terminal), and above 12
  keys in any 1 s window.
- A right direction advances `RouteIndex`; the full route adds the burst (3.4 points × the co-hack share) and the next route
  comes after the gap. A wrong one costs 2 points, deals a new route at once and sets the terminal's `Overloaded` flag for
  1.5 s, which is the only trace the Killer gets.
- The route lives only in the hacker's own `S2C_SelfState`; no other recipient, and never the Killer, learns it.
