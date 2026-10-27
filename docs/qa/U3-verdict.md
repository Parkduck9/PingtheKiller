# U3 verdict — PASS (attempt 2)

Implementer: Codex gpt-6.1-sol (a1, a2). QA: Claude Sonnet (`U3-qa-a1.md`, QA-PASS). Judge: Claude Opus.

Gate: `dotnet build Tools/Harness/Harness.csproj -warnaserror` 0 warnings; `dotnet test Tools/Harness.Tests/Harness.Tests.csproj` 150/150 (40 impl + 110 QA), 3 QA runs stable.

| Spec bullet (U3-a1) | Result |
|---|---|
| TCP-only clients, own PacketAssembler each, references Shared only | PASS |
| CLI flags, table output, JSON shape, exit 0 only if total ≥ threshold and gates pass | PASS (exit 1 = fail, 2 = bad args) |
| H1 10,000 pings, seeded 1..1500 chunks, ordered pongs, no disconnect | PASS (65 broken-fake scenarios across H1–H5 all fail) |
| H2 arrival p99 ≤ 120 ms, Tick strictly increasing, gap ≤ 2 | PASS after a2 (a1 also gated on an inferred tick period — removed) |
| H3 omission + byte-level X/Z scan + killer id absent for survivors | PASS |
| H4 abuse lengths, unknown id, NaN, (100,100), 500/s; control Ping; speed ≤ max × 1.05 | PASS after a2 (a1 coupled H4 to reconnect and had no positive-movement proof) |
| H5 token rejoin within 5 s, same PlayerId | PASS |
| H6 skipped, 0, passed=false | PASS |
| Self-tests incl. broken fake H1 must fail | PASS |

Accepted minor gaps (documented, not blocking): H1 misses a duplicated barrier pong; H3 byte scan is exact-float only; H2 p99 tolerates ~1% long intervals (spec-defined); H4 requires disconnect on unknown packet id — consistent with `PacketCodec` throwing `PacketFormatException` and U2a closing on it.

Unverified until U2a merges: every H-check against the real server; whether U2a answers Ping from unjoined connections (H1 uses an unjoined client); whether `--bots 0` lobby + SetBots clamp behaves as the harness expects; first-run score for PLAN §5 threshold confirmation.
