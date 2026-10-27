# QA Report U1b — attempt 1 (protocol v0.2)

QA model: claude-sonnet-5-5

Verdict: **QA-FAIL** (1 failing test, 1 minor finding; no blocker/major).

## Commands and counts
- `dotnet build PingTheKiller.sln -warnaserror`: PASS, 0 warnings, 0 errors.
- `dotnet test PingTheKiller.sln`: 325 total, 324 passed, **1 failed**, 0 skipped.
  - `Server.Tests/QA/U1bAdversarialTests.cs` (new): 103 total, 102 passed, 1 failed.
  - `Server.Tests/QA/U1AdversarialTests.cs` (migrated): 80 total, 80 passed.
  - Implementer tests (outside QA): 142, all passed.

## Fixture migration (brief section 1)
Rewrote v0.1 byte literals in `U1AdversarialTests.cs` by hand to the v0.2 layouts (WorldState gets ThreatLevel/ExitOpen/ExitLocked/TerminalsRequired + terminal count + 24-byte players; LobbyState gets IsOffline + HostRoleChoice; GameResult gets Reason). Intent of each test is unchanged and controls now also assert the new header fields.
Beyond the three tests named in the brief, these other tests were silently passing for the wrong reason under v0.2 (rejected because of the old layout, not the condition they test) and were migrated too: `InvalidUtf8_InsideLobbySlotNickname`, `LobbyState_SlotCountOtherThanEight`, `LobbyState_DeclaresEightButOnlySevenPresent`, `LobbyState_EightSlotsPlusTrailingByte`, `WorldState_MoreThanEightPlayers`, `WorldState_DeclaresEightButSevenPresent`, `GameResult_NineEscapedIds`. `UnknownPacketIds` used 0x000D, which is now `C2S_Interact`; changed to 0x0010. `Decode_EmptyPayloadForNonEmptyPacket` now covers ids up to 0x0F.

## My own expectation errors (fixed, not code bugs)
- `WorldState_V02_GoldenFrame_Encodes`: I first wrote the frame length as 56. Hand recount: payload 48 + 2 id + 4 header = 54 (length field 0x32 = 50 was right). Fixed to 54 per the spec layout; the code was correct.

## New tests (`U1bAdversarialTests.cs`)
Hand-computed golden frames (WorldState with 1 terminal + 1 player; GameResult TimeUp with 2 escaped ids; `C2S_Interact`), boundary acceptance/rejection for ThreatLevel 3/4, Progress 100/101, SetBots 7/8, SetRole 255 (254 rejected; 256 is not representable), Interact 0/1/2; undefined enum bytes (Role 2, PlayerStatus 4, GameEndReason 3, RoleChoice 2, MatchStart Role 2); all 32 defined PlayerFlags combinations; default-constructed round trip for all 15 packets; default lobby/world/result exact bytes; explicit-null lists throw only `PacketFormatException`; assembler 10 MB cases; `TerminalsFor` 1..7 plus out-of-range; every `GameConstants` value against GDD; Protocol limits and enum numeric values.

## Findings

1. **[minor] Assembler accepts a 10 MB single `Append` without faulting** — Repro: `new PacketAssembler().Append(new byte[10*1024*1024])` (first 4 bytes `00 40 00 00`, nothing drained); test `Assembler_TenMegabytesInOneAppend_FaultsWithoutThrowing`. Expected (QA brief item 7): `IsFaulted == true`, later `TryRead` = `Malformed`. Actual: no exception (good) but `IsFaulted == false`, the buffer is grown to 10 MB, and `TryRead` returns the first 16384-body frame as `Packet`. Cause: the cap is `remaining + chunk <= 4 + maxBody + chunk`, i.e. it only limits *undrained leftover* (<= 16388 bytes) and lets any single chunk size through. Multi-call undrained growth (impl tests, and my 1 MB x 10 case) does fault correctly. The brief item 5 wording ("appending far more data than the cap without draining") is ambiguous for one huge call, so this is minor: a real socket loop with a bounded receive buffer cannot hit it, but the assembler itself has no per-call bound. Decision for the orchestrator: either bound the chunk size in `Append`, or relax the QA expectation; the failing assertion is kept unweakened.

2. **[minor] Bool-like bytes are not validated (unspecified in brief)** — `S2C_WorldState.ExitOpen`, `ExitLocked`, `S2C_LobbyState.IsOffline`, `Slot.IsBot`, `Slot.IsHost` are read with plain `ReadByte()`, so values such as 2 or 255 decode successfully. The brief mandates 0/1 validation only for `C2S_Interact.Active` (which is enforced). No assertion was made (brief item 4 says to record rather than assert); the test `UnspecifiedBoolBytes_NeverThrowAnythingButFormatException` only pins that no foreign exception type escapes.

3. **[minor, behaviour note] `PlayerFlags` unknown bits are rejected** — any `Flags` byte > 31 (e.g. 0x20, 0x80, 0xFF) throws `PacketFormatException` (mask of the five defined bits, read via `ReadByte(31)`). Defined combinations 0..31 are all accepted (asserted). The brief says "every enum-typed byte is validated" but does not say whether `[Flags]` unknown bits count, so rejection is consistent with the strictest reading but is not explicitly required; future flag additions will need a coordinated Shared update. Test `PlayerFlags_UnknownHighBits_...` asserts only the exception type.

4. **[info] Wire layer does not check cross-field consistency** — e.g. `GameResult { WinnerRole = Killer, Reason = TimeUp, EscapedIds = [3, 258] }` and `PlayerState` with Role = Survivor plus Status values are legal bytes; `TerminalState.Id`, `CaptureCount`, `TerminalsRequired` (any 0..255) and `StatusTimer` (NaN/Inf bits) are unconstrained. Not a defect against the brief; game rules must validate semantically.

## GDD constants
All 20 `GameConstants` values and `TerminalsFor` (1..7 literal table; 0, 8, -1, int.Min/Max throw `ArgumentOutOfRangeException`) match GDD sections 3.1-3.5, 4 and 5. No mismatches. `placed` (<= 6) never exceeds the 8-terminal wire cap. Not covered by `GameConstants` (GDD numbers with no constant, outside the brief list): skill durations (1.5 s, 3 s, 1 s, 5 s, 2 s, 5 m, 5 fake signals), overclock 1.5x, traffic-overload 50 %, map 60 x 60 m. Not a defect.

## Evidence
`dotnet test PingTheKiller.sln`: failed `PingTheKiller.Tests.QA.U1bAdversarialTests.Assembler_TenMegabytesInOneAppend_FaultsWithoutThrowing` (`Assert.True() Failure`, line of the `IsFaulted` assertion). All other 324 pass.

## Orchestrator ruling (Claude Opus)
- Finding 1 (single 10 MB Append not faulted): spec-conformant. U1 brief allows growth up to one frame plus one incoming chunk; only undrained growth across calls must fault (verified by other tests). Test renamed to `Assembler_TenMegabytesInOneAppend_DoesNotThrowAndReadsFirstFrame` and asserts the spec behaviour.
- Findings 2-4: accepted as documented behaviour. Bool-like bytes decode leniently (non-zero = true); cross-field consistency is the server's job (U2a/U2b).
- Final: 325/325 tests pass. U1b PASS.
