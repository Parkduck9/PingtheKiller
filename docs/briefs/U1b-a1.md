# Brief U1b — attempt 1: protocol v0.2 (spec change from docs/GDD.md)

Implementer: Codex (gpt-6.1-sol). This is a **spec change**, not a fix: U1 passed review. Read `AGENTS.md`, `docs/GDD.md` (rules source of truth) and the previous brief `docs/briefs/U1-a1.md` (all its constraints still apply). Do **not** commit.

## Owned files
- `Shared/Runtime/**`
- `Server.Tests/**` except `Server.Tests/QA/**` (QA-owned; you may only fix compile breaks there caused by renamed types, without weakening assertions)
- `docs/qa/U1b-impl-a1.md` (your report)

## Changes

### 1. Game enums (`Game/PlayerTypes.cs`)
- `PlayerStatus : byte { Normal = 0, Lagging = 1, Escaped = 2, Eliminated = 3 }` — `Frozen` is removed (GDD §3.3 "packet loss").
- `PlayerFlags` add `Hacking = 8`, `Rescuing = 16`.
- New `GameEndReason : byte { SurvivorEscaped = 0, AllSurvivorsDown = 1, TimeUp = 2 }`.
- `Role` stays `{ Survivor = 0, Killer = 1 }`; add a wire value `RoleChoice : byte { Survivor = 0, Killer = 1, Random = 255 }` for the lobby.

### 2. `PlayerState` (wire order)
`int Id, byte Role, byte Status, float X, float Z, float Yaw, byte Flags, float StatusTimer, byte CaptureCount`

### 3. New `TerminalState`
`byte Id, float X, float Z, byte Progress` — `Progress` 0..100, values > 100 are a format error.

### 4. Changed packets
| Id | Type | Fields (wire order) |
|---|---|---|
| 0x0005 | S2C_WorldState | `uint Tick, float TimeLeft, byte ThreatLevel, byte ExitOpen, byte ExitLocked, byte TerminalsRequired, List<TerminalState> Terminals (≤ 8), List<PlayerState> Players (≤ 8)` — `ThreatLevel` > 3 is a format error |
| 0x0007 | S2C_GameResult | `byte WinnerRole, byte Reason (GameEndReason), List<int> EscapedIds (≤ 8)` |
| 0x000A | S2C_LobbyState | `byte IsOffline, byte HostRoleChoice (RoleChoice), Slot[8] Slots` |

### 5. New packets
| Id | Type | Fields |
|---|---|---|
| 0x000D | C2S_Interact | `byte Active` — only 0 or 1 valid |
| 0x000E | C2S_SetBots | `byte Count` — 0..7 valid |
| 0x000F | C2S_SetRole | `byte RoleChoice` — 0, 1, or 255 valid |

Every enum-typed byte is validated on decode: an undefined value is a `PacketFormatException`.

### 6. `Game/GameConstants.cs` (new) — numbers from GDD, single source
Tick 20 Hz; survivor speed 4.0; killer speed 4.6; lagging speed multiplier 0.25; signal hold 0.15 s; capture radius 1.2; rescue radius 1.5; rescue time 3.0 s; lagging timeout 60 s; post-rescue invulnerability 2 s; terminal radius 1.5; terminal hack time 10 s; max co-hack multiplier 3; exit radius 2.0; match length 600 s; threat distances 18 / 12 / 6 m; killer skill cooldown 12 s; survivor skill cooldown 15 s; required/placed terminals by survivor count (1–2 → 2/4, 3–5 → 3/5, 6–7 → 4/6) exposed as a static method `TerminalsFor(int survivorCount, out int required, out int placed)`.

### 7. PacketAssembler hardening
`Append` must never throw on data-dependent conditions. Where it currently throws `InvalidOperationException` (undrained growth beyond the cap), set `IsFaulted = true` and return instead. Keep the `checked` arithmetic safe the same way (no exception escapes for any input size).

### 8. Safe defaults (from U1 QA finding)
`new S2C_LobbyState()` must encode without throwing: every `Slot` in the array is a non-null instance with `Nickname = string.Empty`, and every list field on every packet is initialized to an empty list. Encoding a packet whose list was explicitly set to `null` may still throw `PacketFormatException`.

## Acceptance tests
1. Update existing round-trip, golden and fuzz tests to v0.2 (fuzz must generate the new packets and terminal lists).
2. New golden frame (hand-computed in the test as a byte literal): `C2S_Interact { Active = 1 }` → `03 00 00 00 0D 00 01`.
3. Decode rejects: `ThreatLevel = 4`, `Progress = 101`, `C2S_Interact.Active = 2`, `C2S_SetBots.Count = 8`, `C2S_SetRole = 2`, `PlayerStatus = 4`, `GameEndReason = 3`.
4. `TerminalsFor` table test for survivor counts 1..7 with literal expectations; 0 or 8 → `ArgumentOutOfRangeException`.
5. Assembler: appending far more data than the cap without draining sets `IsFaulted` and does not throw.
6. All existing QA tests under `Server.Tests/QA/` still pass (after mechanical renames only).
7. Default-constructed instances of every packet type encode and decode round-trip without throwing.

Commands: `dotnet build PingTheKiller.sln -warnaserror` and `dotnet test PingTheKiller.sln` must pass; no `bin/`/`obj/` under `Shared/`. Repeat the Unity-Roslyn compile check you did in U1 if available.

## Report
`docs/qa/U1b-impl-a1.md` (English, ≤ 200 words).
