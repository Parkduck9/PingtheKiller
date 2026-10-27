QA model: claude-sonnet-5-5 (Antigravity headless blocked by permissions)

# QA report U1 attempt 1: Shared protocol + PacketAssembler

## Commands and counts

| Command | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror` | succeeded, 0 warnings, 0 errors |
| `dotnet test PingTheKiller.sln` | 163 total, 163 passed, 0 failed, 0 skipped |
| `dotnet test PingTheKiller.sln --filter "FullyQualifiedName~QA"` | 80 total, 80 passed, 0 failed |

- Implementer's tests: 83 passed, 0 failed (163 minus my 80; counted by subtraction, theory cases expanded).
- My tests (`Server.Tests/QA/U1AdversarialTests.cs`, class `U1AdversarialTests`): 80 passed, 0 failed.
- Test-side correction: my first run had 1 failure caused by my own wrong test data (one boundary plan summed to 12292 bytes, not 8192). I fixed the plan arithmetic (6+4090+100+3996 = 8192). No expectation about the product code was changed or weakened.
- No `bin/` or `obj/` exists anywhere under `Shared/` (checked after build and test; outputs go to `artifacts/`).
- `Shared/Runtime` contains none of `UnityEngine`, `System.Text.Json`, `Newtonsoft`, `System.Linq`.
- I edited only `Server.Tests/QA/U1AdversarialTests.cs` and this report. Nothing was committed.

## Coverage of the QA brief (all 10 items implemented, all pass)

1. Header fed 1 byte at a time with `TryRead` between each: `NeedMoreData` until the frame completes (plus 3-of-4 header bytes, and header-complete-body-missing cases).
2. Frames ending exactly on 4096 and 8192 accumulated bytes: 6 size plans, fed as cuts at 4096/8192, as one 8192 chunk, and as 4095/4097 chunks.
3. `Length == 16384` frame in 3 chunks with a small Ping in the same chunk as the tail; also a drip-fed tail.
4. Faulted assembler: lengths 0, 1, -1, `int.MaxValue`, `int.MinValue`, 16385; valid frame appended afterwards still returns `Malformed`; also fault after one valid packet; also header split byte by byte.
5. Nickname: 48 bytes accepted, 49 rejected, Korean 16 chars (48 B) accepted with a hand-built encode frame, 17 chars (51 B) rejected both ways, 7 mixed Korean/ASCII cases crossing 48 (including char count 48 but 50 bytes), empty string with hand-built frame, declared length 0xFFFF, declared length beyond remaining bytes, 4-byte UTF-8.
6. Invalid UTF-8 (9 sequences: bad continuation, lone continuation, 0xFF, overlong, encoded surrogate, truncated, > U+10FFFF, embedded invalid byte), plus one inside a lobby slot: all `PacketFormatException`.
7. `S2C_LobbyState` counts 0, 1, 7, 9, 16, 255 (self-consistent wire data), 8-declared-7-present, 8 plus trailing byte, and encoding 0/7/9 slots: all rejected; the exact-8 control is accepted.
8. `S2C_WorldState` counts 9, 10, 16, 255 and 8-declared-7-present rejected; 0 and 8 accepted; encoding 9 players throws. `S2C_GameResult` with 9 escaped ids rejected.
9. Golden `C2S_MoveInput` = `12 00 00 00 | 03 00 | 00 00 80 3F | 00 00 80 BF | 00 00 00 00 | 01 00 00 00` matches on encode and decode. Also -0f sign bit and NaN bits survive the wire.
10. Payload lifetime: after decoding, later `Append` calls that force compaction (capacity 24) and growth do not change decoded packets; a partial-frame leftover survives compaction.

Extra: random chunking with initial capacity 1, 7 and 4096 (300 frames, up to the max body, fixed seed); 200 x 16 KB chunks drained between appends; unknown ids; empty payload for every non-empty packet; 20,000 random garbage payloads only ever throw `PacketFormatException`.

## Findings

No blocker or major findings. No test failed against the product code.

- [minor] `PacketAssembler.Append` throws `InvalidOperationException` when the caller appends without draining — repro (by code reading, not executed): `Append` three 16388-byte chunks with no `TryRead` in between; the third has remaining+chunk above `4 + MaxBodyLength + chunk` — expected: either bounded buffering or a documented contract; actual: an unchecked exception on the receive thread (Shared/Runtime/Net/PacketAssembler.cs lines 36 and 49-50). The comment says "Drain TryRead between chunks", and the spec's growth cap implies it, so it is spec-consistent; the server session loop must always drain until non-`Packet` after each `Append`, and should catch this.
- [minor] A default-constructed `S2C_LobbyState` cannot be encoded — repro: `PacketCodec.Encode(new S2C_LobbyState())` — expected: encodable (spec says Slots = exactly 8) or clearly documented; actual: `PacketFormatException("Strings cannot be null.")` because `new Slot[8]` leaves `Nickname == null` (Slot.Write, PacketWriter.WriteString). Same for a null `Players`/`EscapedIds` list. This is a footgun on the server thread; later units must set every slot nickname (use `string.Empty`).
- [minor] Assembler buffer never shrinks — repro: one 1 MB `Append` (valid, as a socket chunk) — expected: bounded retained memory; actual: buffer stays at roughly the chunk size for the session lifetime (spec allows growth up to one chunk; no shrink is specified). Informational.
- [minor] `Decode` accepts any byte for `Role`, `Status`, `Flags`, `Result`, `SkillId` and any float bit pattern (NaN/Infinity) — repro: decode a MoveInput with NaN DirX (covered by `MoveInput_NegativeZeroAndNaNBitsSurviveWire`) — expected: per AGENTS.md section 1, the server sanitizes input; actual: the codec passes it through unchanged. Not a defect of U1 (spec does not ask for range checks), but the server unit must sanitize NaN/Infinity and validate enum bytes.

## Code review of `Shared/Runtime/**` (no deviations found)

- Spec deviations: none. Ids, field order, limits (Nickname 48 B, WorldState <= 8, Slots == 8, EscapedIds <= 8), `Length` bounds (< 2 or > max is `Malformed` and permanently faulted; `Append` ignored when faulted), doubling growth capped at `4 + max + chunk`, and trailing-byte rejection (`EnsureEnd`) are all implemented as specified.
- Host endianness: none. All primitives go through `BinaryPrimitives.*LittleEndian`; floats via `SingleToInt32Bits`/`Int32BitsToSingle`.
- Unbounded allocation: none. String length is checked before the read; list counts are checked before allocation (count is a byte, so at most 255 even before the limit check); `Decode` rejects payloads above 16382 bytes; the writer caps at 16382 bytes.
- Exception paths that could reach a server thread: `PacketFormatException` from `Decode` (by design, and the 20,000-sample random garbage test showed no other exception type); `PacketFormatException` from `Encode` on null/over-limit data; `InvalidOperationException` from `Append` without drain (see finding 1); `ArgumentOutOfRangeException` from constructors on bad arguments (programmer error).
- Payload lifetime: `TryRead` returns a view into the internal buffer; compaction and growth only happen in `Append`, matching the spec ("valid until the next Append").

QA-PASS
