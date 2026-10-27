# QA Brief U2a — attempt 1: Server.Core + Server host

QA agent: Claude Sonnet (independent of the implementer, Codex gpt-6.1-sol).
Read `AGENTS.md` §1 and §5, `docs/GDD.md` §3, `docs/briefs/U2a-a1.md` (the spec), `docs/qa/U2a-impl-a1.md` (implementer's report and public API), and the code under `Server.Core/`, `Server/`, `Shared/Runtime/`.

## Ownership
You may only create/edit `Server.Tests/QA/U2a*` (e.g. `Server.Tests/QA/U2aAdversarialTests.cs`) and `docs/qa/U2a-qa-a1.md`. Do not edit `Shared/`, `Server.Core/`, `Server/`, `Server.Tests/Core/`, any other test file, or the solution. Do not commit.

## Rules
- Write tests **from the spec**, not from the implementation. Use the public API; if something the spec requires is not reachable through the public API, record it as a finding instead of using reflection hacks.
- Deterministic tests use `GameServer.Tick(dt)` + `InMemoryTransport`. Only the TCP tests may use real sockets (ephemeral port, loopback, every wait bounded by a timeout ≤ 5 s).
- Never weaken a failing assertion. If your expectation was wrong per the spec, fix it and say so in the report. A failing test that reflects a real defect stays failing and becomes a finding.

## Adversarial tests to write (`Server.Tests/QA/U2aAdversarialTests.cs`, more files with the `U2a` prefix allowed)
1. **Masking bytes:** survivor stationary ≥ 0.15 s (after having moved, so it has a non-spawn position) → encode the killer-bound `S2C_WorldState` frame and assert the survivor's Id is absent and its X and Z float bytes (little-endian) do not appear anywhere in the frame. Moving again → entry present. Check the 0.15 s edge (still Signal at ~0.10 s, gone at ≥ 0.20 s).
2. **Killer hidden from survivors:** over a multi-second match with a moving killer bot and humans, no survivor-bound WorldState frame contains the killer's Id entry, and the killer's X/Z float bytes never appear in survivor-bound frames.
3. **Threat bands:** place killer at distances 20, 18.01, 17.99, 15, 12.01, 11.99, 9, 6.01, 5.99, 3 m → ThreatLevel 0,0,1,1,1,2,2,2,3,3 (document the boundary inclusivity you derive from the spec/GDD; if unspecified, report the actual behaviour as a minor finding rather than asserting).
4. **Input sanitizing:** NaN, +Inf, -Inf, (NaN, 1), (1e30, 1e30), (10, 0) → no NaN position, speed never exceeds role speed × dt per tick; zero vector → no move.
5. **Seq ordering:** Seq 5 then 3 → 3 ignored; duplicate Seq ignored; uint wrap-around behaviour reported (finding if unhandled, not a blocker).
6. **Rate cap:** 200 inputs within one simulated second → at most 60 accepted (observe via behaviour, e.g. direction changes in later inputs ignored once cap is hit, or via a public counter if exposed).
7. **Reconnect window:** disconnect, rejoin with token at 9.9 s → same PlayerId and position; at 10.1 s → bot owns slot, token rejected or treated as a new player (per spec); a wrong token never grabs another player's slot.
8. **Lobby authority:** non-host `C2S_SetBots`, `C2S_SetRole`, `C2S_StartMatch` ignored; SetBots clamps so total ∈ [2, 8]; 9th human join is rejected or handled without crashing; nickname with control chars / whitespace-only → sanitized.
9. **TCP isolation:** real `TcpTransport` on an ephemeral port; client A sends Length = -1, client B sends Length = 16385, client C sends a valid header with garbage payload → each closed individually; client D (well-behaved) still gets JoinResult/Pong afterwards. Also Join split into 1-byte writes.
10. **Slow consumer:** a TCP client that never reads while the server sends WorldState → disconnected once queued bytes exceed 256 KB; game loop keeps ticking (other client still receives updates).
11. **InMemoryTransport opens no socket:** run an offline match for some ticks and prove no socket is created (e.g. `System.Net` EventSource/`Socket` counters, or a documented equivalent). State the method used.
12. **Game-loop thread ownership:** with `Start()` running, send packets concurrently from several threads for ~1 s; no exceptions, state consistent; verify via any exposed hook (or code review) that state is mutated only on the loop thread. `Stop()` returns promptly and joins the thread.

Also review (no test needed, record as findings): unbounded queues, exceptions that could escape a thread and kill the process, Linux-incompatible APIs in `Server/` or `Server.Core/`, CSPRNG use for ReconnectToken, fake/placeholder logic.

## Run and report
`dotnet build PingTheKiller.sln -warnaserror`, `dotnet test PingTheKiller.sln` → real counts (total/passed/failed, and your QA tests separately).
Report `docs/qa/U2a-qa-a1.md`: header line with model and counts; findings as `[blocker|major|minor] title — repro — expected vs actual — evidence (test name)`; final line exactly `QA-PASS` or `QA-FAIL` (any blocker or major → QA-FAIL).
