# QA Brief U1 — attempt 1: adversarial review of Shared protocol + PacketAssembler

QA agent: Antigravity (gemini-3.8-flash-medium). Read `AGENTS.md` (section 5 binds you) and the implementation brief `docs/briefs/U1-a1.md`.

## Rules
- **Do not edit anything under `Shared/`** or the implementer's existing test files. You may only create `Server.Tests/QA/U1AdversarialTests.cs` and `docs/qa/U1-qa-a1.md`.
- Do not trust the implementer's tests. Write your own, independently, from the spec in `docs/briefs/U1-a1.md`.
- Expected values must come from the spec (hand-computed literals), never from calling the code under test to compute them.

## Write these tests (xUnit, class `U1AdversarialTests`)
1. Header split across many chunks: send the 4 length bytes one at a time with `TryRead` calls in between; must return `NeedMoreData` until complete.
2. Exact buffer boundary: payloads sized so frames end exactly at 4096 and 8192 bytes of accumulated stream.
3. Max-size body (`Length == MaxBodyLength`) delivered in 3 chunks, followed immediately by a small packet in the same chunk as the tail.
4. Faulted assembler: after a `Malformed`, appending a perfectly valid frame still yields `Malformed`.
5. Nickname edge cases: exactly 48 UTF-8 bytes (accepted), 49 bytes (rejected on decode), multibyte Korean text whose UTF-8 length crosses 48, empty string.
6. Invalid UTF-8 bytes inside a string field → `PacketFormatException`.
7. `S2C_LobbyState` with a slot count other than 8 in the wire data → rejected.
8. `S2C_WorldState` with 9 players in the wire data → rejected.
9. Golden bytes for `C2S_MoveInput { DirX = 1f, DirZ = -1f, Yaw = 0f, Seq = 1 }`: hand-compute the full expected frame and assert byte equality.
10. Payload lifetime: the payload returned by `TryRead` is read **before** the next `Append`; verify a second `Append` does not corrupt a packet that was already fully decoded.

## Also check (report only, no code changes)
- `dotnet build PingTheKiller.sln -warnaserror` and `dotnet test PingTheKiller.sln` results (counts).
- No `bin/` or `obj/` under `Shared/`.
- Read `Shared/Runtime/**` and list any spec deviation, host-endianness dependence, unbounded allocation, missing limit check, or exception path that could crash a server thread.

## Report `docs/qa/U1-qa-a1.md` (English)
Header line: model used. Then: test counts (implementer's + yours, pass/fail), each finding as `[blocker|major|minor] title — repro — expected vs actual — evidence`, and a one-line verdict `QA-PASS` or `QA-FAIL`.
