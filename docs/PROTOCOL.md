# Ping the Killer: wire protocol v0.6 (GDD v2, units U6g, Q3)

The source of truth is `Shared/Runtime/` (server and Unity compile the same files). This page covers the v0.4 layouts, v0.5 RTT probes and the v0.6 skill rejection. All integers and floats are little-endian. Counts are one byte, and a reader rejects any count above its maximum (`PacketFormatException`).

## Frame

`[int32 Length][ushort PacketId][Payload]`. `Length` is the size of `PacketId + Payload`. Values below 2 or above 16384 disconnect the sender.

## Packet ids

| Id | Packet | Dir | Changes |
|---|---|---|---|
| 0x0001-0x000F | Join, JoinResult, MoveInput, UseSkill, WorldState, SkillEffect, GameResult, Ping, Pong, LobbyState, StartMatch, MatchStart, Interact, SetBots, SetRole | | WorldState and GameResult changed |
| 0x0010 | S2C_SelfState | S→C | extended |
| 0x0011 | S2C_SyncCheck | S→C | new |
| 0x0012 | C2S_SyncPress | C→S | new |
| 0x0013 | S2C_SyncResult | S→C | new |
| 0x0014 | S2C_RttProbe | S→C | v0.5: `uint Nonce` |
| 0x0015 | C2S_RttEcho | C→S | v0.5: `uint Nonce` |
| 0x0016 | S2C_SkillRejected | S→C | v0.6: `byte SkillId, byte Reason` |

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
| | player count | byte ≤ MaxWorldEntries | then `PlayerState` × count (unchanged) |

- **ExitState** (11 bytes): `byte Id, float X, float Z, byte Flags, byte SwitchProgress (0-100)`.
  - Flags: `Powered = 1`, `Open = 2`, `Locked = 4` (the switch is locked by Firewall Block). Values above 7 are rejected.
- **TerminalState** (11 bytes): `byte Id, float X, float Z, byte Progress (0-100), byte Flags`.
  - Flags: `Regressing = 1`, sent to survivors only (the killer kicked the terminal); `Overloaded = 2`, sent to everyone for 2 s after a failed sync check. Values above 3 are rejected.
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

25 bytes, sent only to the player it describes:

1. `float SkillCooldown`, `float RescueProgress`, `float InvulnerableLeft`, `float ExecutionGraceLeft` (unchanged).
   - `ExecutionGraceLeft` is **deprecated** (GDD v2 §4, 2026-11-03: no execution) and always 0. The layout is unchanged.
2. v0.4 fields:
   - `float CollapseLeft`: the survivor's personal clock, which runs at 2× while Lagging; the killer gets the global clock. -1 when not running.
   - `float ActionProgress` (0-1).
   - `byte ActionKind`: `None = 0`, `Switch = 1`, `Kick = 2`, `HatchClose = 3`. Values above 3 are rejected.

## Sync check (0x0011-0x0013)

- **S2C_SyncCheck**: `uint CheckId, byte TerminalId, float Duration (1.1), float ZoneStart (0.45-0.8), float ZoneSize (0.12), float GreatSize (0.035)`.
  - Sent only to the hacking survivor, every 8-16 s of its own hacking.
  - CheckId increases on every check.
- **C2S_SyncPress**: `uint CheckId`. At most one press counts per check.
  - Presses are ignored without a reply when the CheckId is unknown, stale or repeated, when the check was cancelled, and when they come from the killer or from a bot.
- **S2C_SyncResult**: `uint CheckId, byte Result`.
  - Values: `Fail = 0`, `Good = 1`, `Great = 2`. Values above 2 are rejected.
  - Great gives +1 point. Good changes nothing. Fail gives −5 points and sets the terminal's `Overloaded` flag for 2 s.

**Judging.** The server takes the press moment as receive-thread arrival − the full server-probed RTT and computes the fraction of the sweep since the check was sent. The client starts the needle at the check's receive-thread timestamp.
- Great: inside `[ZoneStart, ZoneStart + GreatSize]`.
- Good: inside `[ZoneStart − tol, ZoneStart + ZoneSize + tol]`, where `tol = 0.15 s / Duration`.
- Fail: anything else, including no press by `Duration + 0.15 s + RTT` after sending.
- Stopping the hack cancels the pending check silently.
