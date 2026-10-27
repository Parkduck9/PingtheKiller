# QA Brief U1b — attempt 1: protocol v0.2

QA agent owns `Server.Tests/QA/**` and `docs/qa/U1b-qa-a1.md`. Read `AGENTS.md` §5, `docs/GDD.md`, `docs/briefs/U1b-a1.md` (the spec), and `docs/qa/U1b-impl-a1.md`. Do not edit `Shared/` or the implementer's tests. Do not commit.

## 1. Migrate your own fixtures (spec change, not a code bug)
These U1 QA tests encode **v0.1** wire layouts and now fail by design under v0.2:
`WorldState_ZeroPlayers_IsAccepted`, `WorldState_EightPlayers_IsAccepted_Control`, `LobbyState_ExactlyEightSlots_IsAccepted_Control` (and any other v0.1 byte literal in `U1AdversarialTests.cs`).
Rewrite their byte literals by hand to the v0.2 layouts from `docs/briefs/U1b-a1.md` §2–§4. Keep each test's intent (zero/eight/exact-count acceptance). Do not compute expected bytes with the code under test.

## 2. New adversarial tests — `Server.Tests/QA/U1bAdversarialTests.cs`
1. Hand-computed golden frame for a v0.2 `S2C_WorldState` with 1 terminal and 1 player (choose simple values like 1f, 0f), including ThreatLevel, ExitOpen, ExitLocked, TerminalsRequired.
2. `S2C_GameResult` golden frame with Reason = TimeUp and two escaped ids.
3. Boundary acceptance: `ThreatLevel = 3`, `Progress = 100`, `C2S_SetBots.Count = 7`, `C2S_SetRole = 255` accepted; one above each rejected.
4. Every enum-typed byte across all packets: an undefined value is rejected (Role 2, PlayerStatus 4, GameEndReason 3, RoleChoice 2, IsOffline/ExitOpen/ExitLocked/Active values other than 0/1 if the spec treats them as bool — check the brief; if the brief does not specify bool validation for a byte, record it as a finding instead of asserting).
5. `PlayerFlags` with unknown high bits (e.g. 0x80): check what the brief requires; report behaviour as a finding if unspecified.
6. Default-constructed instances of all 15 packet types encode/decode round-trip without throwing.
7. Assembler: append 10 MB in one call without draining → `IsFaulted` true, no exception; subsequent `TryRead` → `Malformed`.
8. `GameConstants.TerminalsFor` for 1..7 against GDD §3.4 literals; values in `GameConstants` match GDD §3–§5 numbers (list any mismatch).

## 3. Run and report
`dotnet build PingTheKiller.sln -warnaserror`, `dotnet test PingTheKiller.sln` → real counts.
Report `docs/qa/U1b-qa-a1.md`: header line with model, counts, findings `[blocker|major|minor] title — repro — expected vs actual`, verdict `QA-PASS` / `QA-FAIL`. Never weaken a failing assertion; if your expectation was wrong per the spec, fix it and say so.
