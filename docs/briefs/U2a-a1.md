# Brief U2a — attempt 1: Server.Core (transport, sessions, lobby, movement, visibility, threat) + Server host

Implementer: Codex (gpt-6.1-sol, effort high). Read `AGENTS.md`, `docs/GDD.md` (rules), `docs/PLAN.md` §2, and the Shared API in `Shared/Runtime/**` (protocol v0.2). Do **not** modify `Shared/`. Do not commit unless your session's orchestrator tells you to.

## Owned files
- `Server.Core/**` (new), `Server/**` (new), `Server.Tests/Core/**` (new), `PingTheKiller.sln` (add projects), `docs/qa/U2a-impl-a1.md`

## Architecture
- `Server.Core`: `netstandard2.1`, C# 9, no `UnityEngine`. It is ALSO a local UPM package so Unity can host it in-process for Offline Bot mode: `Server.Core/package.json` (`com.pingthekiller.servercore`, depends on `com.pingthekiller.shared`), `Server.Core/Runtime/PingTheKiller.ServerCore.asmdef` (references `PingTheKiller.Shared`, `noEngineReferences: true`), `Server.Core/Server.Core.csproj` compiling `Runtime/**/*.cs` and referencing `Shared.csproj`. Artifacts go to root `artifacts/` (already configured).
- `Server`: `net10.0` console host only (arg parsing, Ctrl+C shutdown, logging to console). Must run on Windows and Linux (no Windows-only APIs) — it may be deployed to a Linux ARM64 VM later.
- **Transport abstraction** in Server.Core:
  - `IConnection` (Send(byte[] frame), Close(), events/queue for received packets, RemoteId) and `ITransportListener`.
  - `TcpTransport`: `System.Net.Sockets` listener; one accept loop; per-connection async receive feeding a `PacketAssembler`, draining `TryRead` until `NeedMoreData` after every receive; `Malformed` or decode `PacketFormatException` → close that connection only. Per-connection send queue drained by a dedicated send loop; a slow client never blocks the game loop (cap queued bytes at 256 KB → disconnect).
  - `InMemoryTransport`: a pair of in-process connections that pass **encoded frames** through a `PacketAssembler` exactly like TCP (so offline mode exercises the same codec) but **opens no socket at all** (Store requirement: no firewall prompt in Offline mode).
- **GameServer** (one per match/lobby): owns all state; a single game-loop thread at fixed 20 Hz (`GameConstants`) drains a `ConcurrentQueue` of (connectionId, IPacket) inputs. Only this thread touches game state. Expose `Start()`, `Stop()`, `AddConnection(IConnection)`, and a deterministic `Tick(float dt)` used by tests without threads.

## Scope (U2a only)
1. **Join/lobby:** `C2S_Join` → `S2C_JoinResult` (PlayerId, random 64-bit ReconnectToken from a CSPRNG). First human is host. `S2C_LobbyState` broadcast on every change (8 slots; bots fill per `C2S_SetBots`; `IsOffline` flag from server options). `C2S_SetBots` (host only, clamp so total ≤ 8 and ≥ 2 incl. bots), `C2S_SetRole` (host only; RoleChoice). Non-host lobby commands are ignored. Nickname sanitize: trim, strip control chars, empty → "Player N".
2. **Match start:** `C2S_StartMatch` (host only, ≥ 2 participants). Assign killer: host's RoleChoice if Killer/Survivor, else random among all participants (bots included). Random skill per player from the role's pool. Send each human `S2C_MatchStart` (their Role, SkillId, Seed). Build a fixed test map in code: 60×60 m square with an outer wall and a few interior box walls (axis-aligned rectangles), spawn points, terminal positions per `GameConstants.TerminalsFor`, one exit. Map data in `Server.Core/Runtime/Map/` as plain C# so Unity can render the same layout.
3. **Movement:** `C2S_MoveInput` sanitize — NaN/Infinity → 0; clamp direction length to ≤ 1; drop `Seq` ≤ last processed; ignore more than 60 inputs/s per player (excess dropped). Server applies the latest direction each tick: `pos += dir * speed * dt`, axis-aligned wall collision (circle radius 0.4 vs rectangles; slide along walls). Survivor 4.0, killer 4.6 m/s. Yaw is cosmetic only.
4. **Visibility (core rule):** a survivor is `Signal` while it has a non-zero input within the last 0.15 s. Per-recipient `S2C_WorldState` at 20 Hz:
   - To the **killer**: survivors that are not Signal are **omitted entirely** (no entry). Killer sees itself.
   - To **survivors**: all survivors; the **killer entry is omitted entirely**; `ThreatLevel` = 0..3 from distance to killer (18/12/6 m).
   - Terminals list and exit flags are sent to everyone (static in U2a: progress 0, exit closed).
5. **Bots (U2a level):** bots run inside the game loop (no sockets), produce the same input structs as humans. Survivor bot: walk toward a random terminal, pause 1–3 s at random (go Silent), repeat. Killer bot: walk toward the last-seen Signal position; else patrol terminals.
6. **Ping:** `C2S_Ping` → immediate `S2C_Pong` with the same timestamp.
7. **Disconnect & reconnect (basic):** on disconnect a human keeps their slot for 10 s (character stops). `C2S_Join` with a matching ReconnectToken within 10 s re-binds the connection to the same PlayerId and state. After 10 s, a bot takes over that player.
8. **Server host:** `dotnet run --project Server -- --port 7777 --bots 6` starts a lobby; `--autostart` starts a match immediately with bots (for harness/QA). Log one line per join/leave/match start.

Out of scope (U2b): terminal hacking, capture/Lagging/rescue, skills effects, win/lose, `S2C_GameResult`, `C2S_Interact`. Leave clear TODO seams, not fake logic.

## Acceptance tests (xUnit in `Server.Tests/Core/`, deterministic via `Tick(dt)` + `InMemoryTransport`)
1. Two clients join → JoinResult ids differ, LobbyState shows host flag on the first, bots per SetBots; non-host SetBots ignored.
2. Start match with 1 human + bots → each human gets MatchStart; exactly one killer.
3. Movement: 20 ticks of DirX=1 at survivor speed → x advanced by 4.0 m ± 0.01; NaN input → no movement; direction (10, 0) → speed still 4.0; out-of-order Seq ignored.
4. Wall collision: walking into a wall never ends inside it.
5. **Masking:** survivor stops for ≥ 0.15 s → its entry is absent from the killer's next WorldState; moving → present. Encode the killer-bound WorldState and assert the stationary survivor's X/Z float bytes do not appear in the frame.
6. **Killer hidden:** no survivor-bound WorldState ever contains the killer's Id; ThreatLevel matches distance bands at 20, 15, 9, 3 m.
7. Reconnect within 10 s restores PlayerId and position; after 10 s a bot controls the slot.
8. TCP smoke test: start `TcpTransport` on an ephemeral port, connect a raw `TcpClient`, send Join split into 1-byte writes, receive JoinResult; send a frame with Length = -1 → only that connection is closed, server keeps running.
9. InMemoryTransport opens no socket (assert via a transport that throws if any `Socket` is created — e.g., check no `TcpListener` instance exists in offline options path; document the method).

Commands: `dotnet build PingTheKiller.sln -warnaserror`, `dotnet test PingTheKiller.sln`, and a 10-second manual run of `dotnet run --project Server -- --port 7777 --bots 7 --autostart` that logs a match start without exceptions.

## Report
`docs/qa/U2a-impl-a1.md` (English, ≤ 250 words) incl. the public API of `GameServer`/transports that Unity (U4) and the harness (U3) should use.
