# Brief U2a — attempt 2: fix list on top of attempt 1

Implementer: Codex (gpt-6.1-sol, effort high). Same approach as attempt 1; the code from attempt 1 is in the working tree (uncommitted). Read `AGENTS.md`, `docs/briefs/U2a-a1.md` (still the full spec), `docs/qa/U2a-qa-a1.md` (QA findings), then apply exactly the fixes below. Do **not** modify `Shared/`, `Client/`, `Tools/`, or any file under `Server.Tests/QA/` (QA-owned; the failing QA test must pass without being edited). Do not commit.

## Owned files
`Server.Core/**`, `Server/**`, `Server.Tests/Core/**`, `docs/qa/U2a-impl-a2.md`.

## Fixes (each needs a test in `Server.Tests/Core/`)
1. **[major] Silent-move bypass.** Today the last direction is applied forever but Signal only refreshes on input, so one `MoveInput` then silence = invisible movement. New rule: a held direction is applied only while the player's last accepted non-zero input is within `GameConstants.SignalHoldSeconds`; after that the direction is treated as zero (player stops). Apply to every player (humans and bots, both roles). Invariant to test: in any tick where a survivor's position changes, that survivor's entry is present in the killer-bound `S2C_WorldState` sent in the same tick (evaluate Signal and the hold with the same clock value; watch the off-by-one between moving before `_now += dt` and broadcasting after). `Server.Tests/QA/U2aAdversarialTests.Masking_MovingSurvivorIsNeverOmittedEvenWithoutFurtherInputs` must pass unchanged. A client that sends input every tick (20 Hz) while a key is held must move continuously at full speed — add that test too. State in the report that clients must resend `MoveInput` at ≥ 10 Hz while moving.
2. **Game loop must not die.** In `GameServer.Run`, catch exceptions from each `Tick`, report them through `Log` (exception type + message, once per distinct message per second at most), and keep ticking. An exception thrown by a `Log` subscriber must not escape either. Test with a throwing `Log` subscriber or another injected fault reachable through the public API.
3. **Accept loop must not die.** In `TcpTransport.AcceptLoop`, a non-shutdown accept error is logged (add `event Action<string> Log` to `TcpTransport`, wire it to the console in `Server/Program.cs`) and the loop continues after a short delay (≈100 ms, no busy spin). An exception from a `ConnectionAccepted` subscriber or from constructing/starting a `TcpConnection` closes that client only and the loop continues. `Stop()` never rethrows a loop fault.
4. **Seq wrap-around.** Replace `move.Seq <= player.Seq` with serial-number comparison: accept only if `(int)(move.Seq - player.Seq) > 0`. Test: Seq `uint.MaxValue - 1`, `uint.MaxValue`, `0`, `1` are all accepted in order; an older Seq after wrap is dropped.
5. **Half-open connections.**
   - Enable TCP keep-alive on accepted sockets (`Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true)`; cross-platform only, no Windows-only `IOControl`).
   - A valid, unexpired ReconnectToken presented from a **new** connection while the player's old connection is still bound takes over the slot: the old connection is closed and unbound, the new one bound (same PlayerId/state). Tokens of other players still never match.
   - A connection that has not completed a successful `C2S_Join` within 10 s of being added is closed. (Ping is allowed before Join.)
6. **Nickname sanitize:** also strip Unicode format characters (`UnicodeCategory.Format`: zero-width, bidi overrides) and cap the result at `Protocol.MaxNicknameBytes` UTF-8 bytes without splitting a surrogate pair. Test with `"‮abc​"` → `"abc"`.
7. **SIGTERM:** `Server/Program.cs` shuts down gracefully on SIGTERM as well as Ctrl+C, using a cross-platform API (`System.Runtime.InteropServices.PosixSignalRegistration` for `PosixSignal.SIGTERM`, cancel the default and set the shutdown event). Catch any exception from `transport.Stop()`/`game.Stop()` and log it.
8. **Killer spawn.** At match start the killer must spawn ≥ 18 m (`ThreatLevel1Distance`) from every survivor spawn, so no survivor starts at threat > 0. Keep 8 spawn points total; place the killer spawn and survivor spawns in `FixedMap` so this holds for every killer index. Test: for every possible killer index the starting ThreatLevel of every survivor is 0.

## Unchanged (do not regress)
Everything in `U2a-a1.md`, all 360 existing tests, the 56 QA tests (55 passing + the masking one must now pass).

## Commands
`dotnet build PingTheKiller.sln -warnaserror`, `dotnet test PingTheKiller.sln`, and a 10-second `dotnet run --project Server -- --port 7777 --bots 7 --autostart` that logs a match start without exceptions.

## Report
`docs/qa/U2a-impl-a2.md` (English, ≤ 250 words): per-fix what changed and which test proves it, counts, the updated public API (GameServer, transports, `Log` events) for U3/U4, and the client input-rate requirement.
