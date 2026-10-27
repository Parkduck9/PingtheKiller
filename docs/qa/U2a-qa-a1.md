# U2a QA report, attempt 1

QA model: Claude Sonnet 5.5 (independent of implementer Codex gpt-6.1-sol). Build: `dotnet build PingTheKiller.sln -warnaserror` PASS (0 warnings, 0 errors). `dotnet test PingTheKiller.sln`: **416 total, 415 passed, 1 failed**. QA tests (`Server.Tests/QA/U2a*`): **56 total (theory cases counted), 55 passed, 1 failed**. Implementer tests: 360, all passed. The QA suite was run 4 times with no flakes (TCP/threaded tests included, all waits <= 5 s, loopback, ephemeral ports, port 7777 never used).

Files: `Server.Tests/QA/U2aHelpers.cs`, `U2aAdversarialTests.cs` (deterministic, `Tick(dt)` + `InMemoryTransport`), `U2aNetworkTests.cs` (TCP, no-socket, threading).

## Brief checklist

| # | Item | Result |
|---|------|--------|
| 1 | Masking bytes (0.05/0.10 s Signal, 0.15/0.20 s and 40 more ticks absent, Id and X/Z LE float bytes absent, present again on move, byte-search validated with a positive control) | PASS |
| 2 | Killer hidden from survivors (12 s match, moving human killer, 2 humans + 2 bots; bot-killer variant; no killer entry, no killer X/Z bytes, every survivor frame lists all 4 survivors; threat level equals GDD band every tick) | PASS |
| 3 | Threat bands 20/18.01/17.99/15/12.01/11.99/9/6.01/5.99/3 -> 0,0,1,1,1,2,2,2,3,3 | PASS |
| 4 | Input sanitizing (13 vectors x both roles, per-tick step <= role speed x dt, no NaN/Inf, non-finite and zero -> no move, oversize vectors clamped to full speed) | PASS |
| 5 | Seq ordering (older, duplicate ignored; first Seq 0 accepted; wrap-around characterised) | PASS (see minor F4) |
| 6 | Rate cap (200 inputs in one tick, only first 60 applied; window reopens after 1 s; 20 Hz client never throttled) | PASS |
| 7 | Reconnect window (9.9 s: same PlayerId, token, exact position, MatchStart re-sent; 10.1 s: slot is a bot, lobby IsBot=1, bot walks, stale token rejected; 4 wrong/near tokens never grab a slot, real token still works) | PASS |
| 8 | Lobby authority (non-host SetBots/SetRole/StartMatch ignored; total clamped to [2,8] with 1 and 2 humans; 9th human rejected with Result != 0 and connection still usable; 8-human match runs with 7 survivors/6 terminals; human joining a full bot lobby replaces a bot; nicknames sanitized) | PASS |
| 9 | TCP isolation (12 malformed variants incl. Length -1/0/1/16385/int.Max/int.Min, garbage payloads, unknown id, bad enum, bad string length; each offender closed individually; good client keeps Pong/lobby; Join in 1-byte writes; valid frame + garbage in one write) | PASS |
| 10 | Slow consumer (Ping flood, client never reads, receive buffer 1 KB): server connection closed, `QueuedBytes` never above 256 KB, game loop ticks on for the healthy client | PASS |
| 11 | InMemory opens no socket | PASS (method below) |
| 12 | Loop thread ownership (6 client threads + connect/close churn for 1.2 s, ~no exceptions, ticks monotonic, ids unique, `Tick()` from a foreign thread throws while running, `Stop()` < 1 s, manual `Tick` allowed afterwards) | PASS |
| extra | Walking 8 directions x 20 s never leaves the map or enters a wall | PASS |

Method for item 11: (a) the offline factory never calls the online factory; (b) a process EventListener on System.Net.Sockets/NameResolution, filtered to the test thread (the whole offline run executes on it), records zero events during a 400-tick offline match, with a positive control showing it does capture a loopback connect from the same thread; (c) a reflection walk of the object graph of server, transport and both connections (plus all static fields of Server.Core) finds no `System.Net.Sockets` object and no `Stream`. Limitation: socket creation or bind alone raises no .NET telemetry event, so (b) detects connect/accept activity only; (c) is the structural proof for creation.

## Findings

- **[major] Moving survivor can be invisible to the killer (Silent-move bypass)** — Repro: survivor sends ONE `C2S_MoveInput` DirX=1, then sends nothing; run 40 ticks. Expected: a survivor that is moving is never masked (GDD 3.1: only a stationary survivor is Silent, "no wallhack" integrity; server authority principle). Actual: the server keeps applying the last direction (4 m/s) but `LastSignal` only refreshes when an input arrives, so the survivor leaves the killer-bound `WorldState` after 0.15 s while still moving, 38 of 40 ticks hidden (first at tick 2). A modified client (send a direction once, then stay quiet) gets permanent invisible movement; a legitimate client that only sends on change is also hidden while walking. Suggested fix (for the orchestrator to decide): drive Signal from actual movement (position changed this tick or non-zero current direction) or expire the held direction when no input arrived within the Signal hold. — Evidence: `U2aAdversarialTests.Masking_MovingSurvivorIsNeverOmittedEvenWithoutFurtherInputs` (FAILING, stays failing).
- **[minor] F2: accept loop can die permanently and Stop() can rethrow** — Code review of `TcpTransport.AcceptLoop`: only a `SocketException`/`ObjectDisposedException` while `_running == 0` is handled. Any other accept error while running (e.g. EMFILE under connection pressure, a throwing `ConnectionAccepted` subscriber) faults the task silently, the server stops accepting forever, and `Stop()` then rethrows through `GetResult()` (Program.cs catches only `SocketException`). Not reproduced. Expected: log and continue on transient accept errors.
- **[minor] F3: game-loop thread has no catch** — `GameServer.Run` has `try/finally` only. Any exception escaping `Tick` (including a throwing `Log` subscriber) terminates the whole server process. No trigger found by fuzzing (35 core tests, 12 malformed TCP inputs, churn test). Expected: catch, log, keep ticking or shut down cleanly.
- **[minor] F4: Seq wrap-around not handled** — plain `Seq <= last` as the brief says. A single packet with Seq = uint.MaxValue freezes all later input of that session (self-inflicted only; reconnect resets it). Natural wrap needs ~6.8 years at 20 Hz. Evidence: `Seq_UintWraparound_IsNotHandled_SpecLiteralBehaviour` (characterisation, passes).
- **[minor] F5: no idle/heartbeat timeout, no cap on connections or unauthenticated sessions** — Code review: a half-open TCP peer (cable pull, NAT drop) is never detected, so its slot is held forever and is never turned into a bot; unauthenticated connections accumulate without limit. Related: a valid ReconnectToken is rejected while the old connection still looks alive (`old.ConnectionId != null`), so reconnecting after a network drop fails until the dead socket is noticed. The brief does not specify this; recommend for U2b/U5 (server-side idle timeout on `Ping` silence, token takeover of a stale session).
- **[minor] F6: nickname sanitizer strips only control chars (Cc)** — Code review: zero-width (U+200B) and bidi override (U+202E) characters survive, so an invisible-only name or a spoofed display name is possible. Brief only requires control-char/whitespace handling, which passes (`Lobby_NicknameIsSanitized`, 8 cases).
- **[minor] F7: GDD threat table ambiguity at exactly 12 m** — "12~18" and "6~12" overlap. Actual: `<= 12` is level 2. Exactly 18 -> 1 and exactly 6 -> 3 match the GDD wording ("18 m 초과", "6 m 이내"). `ThreatBands_ExactBoundaryInclusivity_FollowsGddWording` asserts only 18 and 6 plus a valid level for 12. Docs should state the rule.
- **[minor] F8: Program.cs handles Ctrl+C only** — no SIGTERM handling (systemd/docker stop on the planned Linux ARM64 VM skips the graceful shutdown). Not tested.

## Review notes (no finding)
- Queues are bounded: per-binding pending packets 4096 (Ping answered inline, bounded by the 256 KB send cap), per-connection received 4096, per-connection send 256 KB; `_inputs` is drained every tick.
- ReconnectToken uses `RandomNumberGenerator` (CSPRNG), non-zero, unique among players. `System.Random` is used only for roles, skills, bots and the map seed.
- No Linux-incompatible API in `Server/` or `Server.Core/` (no registry, P/Invoke, Windows paths).
- No fake logic found; U2b seams are TODO comments. `ExitLocked=1`, terminal progress 0 are static as the brief allows.
- Not testable through the public API (not a defect): bot killer position and bot AI internals; bot behaviour was checked only through its observable effects (Signal/Silent alternation, threat levels).

QA-FAIL
