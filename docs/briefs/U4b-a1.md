# Brief U4b — attempt 1: offline bot mode + U4 follow-ups + minimal play readouts

Implementer: Claude Opus subagent (quota fallback: Codex is out of credits; a quota switch is not a failed attempt). QA: Claude Sonnet (different model).
Branch `claude/u4b-offline` (from main `123e50c`). Do not commit.

Read first: `AGENTS.md`, `docs/GDD.md` §2, §3, §4, §6.2 and §7, `docs/ui/SLOP_DEFINITION.md` (binding for every on-screen string and element), and `docs/qa/U4-judgement.md` (the follow-up list).

Read the code: `Server.Core/Runtime/**` (the public API you code against: `GameServer`, `ServerOptions`, `InMemoryTransport`, `IConnection`, `FixedMap`), `Shared/Runtime/**` (protocol v0.2) and all of `Client/Assets/Scripts/**` (U4, merged).

U2b (server rules: hacking, Lagging, skills, GameResult, return to lobby) is being built in parallel. Its spec is `C:\Unity\MultiPlayGame\PingtheKiller\docs\briefs\U2b-a1.md`, for reference only. The packets it will start sending are already defined in protocol v0.2: `S2C_GameResult`, `S2C_SkillEffect`, `TerminalState.Progress`, `PlayerState.Status/StatusTimer/Flags`. Handle them from those definitions.

## Owned files
- `Client/**`: scripts, resources, tests (not `Client/Assets/Tests/EditMode/QA/**`, which is QA-owned), `Packages/manifest.json`, and whatever Unity regenerates.
- `docs/qa/U4b-impl-a1.md`
- `docs/qa/U4b-screens/*.png`

Never edit `Server.Core/`, `Server/`, `Shared/` sources, `Tools/` or `PingTheKiller.sln`. Unity-generated `.meta` files for `Server.Core/` are fine.

## Scope

### 1. Offline mode (GDD §6.2)
- Add `"com.pingthekiller.servercore": "file:../../Server.Core"` to `Client/Packages/manifest.json`.
- Reference `PingTheKiller.ServerCore` from the client asmdef.
- **Link abstraction:** introduce a client-side interface, e.g. `IGameLink`, with `Send(IPacket)`, a main-thread `Pump()`, `PacketReceived`, `Disconnected`, `RttMilliseconds` (−1 when not applicable), `IsConnected` and `Dispose`.
  - `NetClient` (TCP) implements it unchanged in behaviour.
  - New `OfflineLink` implements it:
    - `var options = new ServerOptions { IsOffline = true, BotCount = 3 }`
    - `options.CreateTransport()` must return an `InMemoryTransport`; fail loudly otherwise.
    - Wire `ConnectionAccepted` to `server.AddConnection`, call `transport.Start()`, then `transport.Connect()` for the client side, then `server.Start()`.
    - Packets arrive on the game-loop thread through `PacketAvailable`/`TryReceive`. Queue them with an arrival timestamp and dispatch only in `Pump()` on the main thread, exactly like `NetClient`.
    - Send with `PacketCodec.Encode(packet)`, then `connection.Send(frame)`.
    - The client sends `C2S_Join` itself, as `NetClient` does.
  - `ClientApp` talks only to `IGameLink`.
- **No socket on the offline path (Store requirement: no firewall prompt).**
  - Never construct `NetClient`, `TcpClient`, `TcpTransport`, `Socket` or `TcpListener` when Offline is chosen.
  - `OfflineLink.Dispose` closes the client connection and calls `server.Stop()` off the main thread (Stop joins the game-loop thread; the main thread must not wait on it). Quitting must not leave the game thread running; it is a background thread, but stop it anyway.
- **Menu:** the Offline button works; remove the "Offline mode arrives in U4b" line.
- **Offline lobby:**
  - The player is host.
  - Bots can be added and removed between 1 and 7 (1v1 to 1v7).
  - Role choice is Survivor, Killer or Random.
  - Start begins the match.
  - Use the same lobby screen. Show that it is offline, e.g. the title "Lobby · offline" from `LobbyState.IsOffline`. Do not show RTT offline.
  - Leave returns to the menu and disposes the link.

### 2. Map
- Delete the hard-coded `MapSource` copy and build the world from `Server.Core`'s `FixedMap`: walls are min/max boxes, plus terminal positions, exit, `Size` and the centre.
- Remove the `TODO(U4b)` about sharing the map.

### 3. U4 follow-ups (from `docs/qa/U4-judgement.md`)
1. **Connection errors use our own fixed messages, never the raw OS/exception text.** Map at least:
   - refused / unreachable → `Server not reachable at <ip:port>. Check that the host is running and the port is right.`
   - timeout → `No answer from <ip:port> within 5 s. Check the IP and that both PCs are on the same network.`
   - lost after connecting → `Connection to <ip:port> lost. Rejoin when the host is back.`

   The lobby timeout message must not say "protocol v0.2". Use e.g. `The server did not open a lobby. Check that the host runs the same version as this client.` Log raw exception details with `Debug.Log` only. Write the mapping as a pure function and test it.
2. No internal unit names (U4b, U2b …) or protocol version strings in any on-screen copy.
3. **GameResult screen** (`S2C_GameResult`).
   - Stop play, unlock the cursor, stop the beep, and send a final zero MoveInput and an Interact 0.
   - Show the winner side and the reason in plain words. Map each `GameEndReason`:
     - SurvivorEscaped → `Survivors win. <names> reached the Exit.`
     - AllSurvivorsDown → `Killer wins. No survivor is left in play.`
     - TimeUp → `Killer wins. Time ran out.`
   - Show the local player's own outcome, e.g. `You escaped.` / `You were eliminated.`
   - List escaped players by nickname, taken from the last `LobbyState` slots; fall back to `Player <id>`.
   - One primary action, `Back to lobby`. It is enabled once a `LobbyState` has arrived after the GameResult (U2b returns the server to the lobby). Until then it is disabled and the screen says `Waiting for the host to reopen the lobby.`
   - Secondary action: `Leave`.
   - No generic VICTORY banner and no confetti (SLOP §6).
   - Write the copy mapping as a pure function and test it.
4. **Lobby rows:**
   - Show `Player · player` as nickname plus markers only: `· bot`, `· host`, `· you`. No redundant kind word for humans.
   - Keep row numbers aligned in a fixed-width column.
5. **Exit label:** offset it so it no longer overlaps the exit ring.
6. **Debug line:**
   - RTT is no longer on the survivor HUD. Show it in a small top-left dev line toggled with F3 (default off).
   - The survivor cursor hint appears only while the cursor is released (`Click to resume mouse look`).
7. **Panel scale:** allow scaling up on large screens: clamp `min(w/1920, h/1080)` to [0.8, 2].

### 4. Minimal play readouts (not the D3 UI pass)
- **Survivor HUD, bottom-centre only:**
  - The existing Signal/Silent meter.
  - The player's own status: show nothing extra while Normal, `Lagging · <seconds> s` from `StatusTimer` (rounded up), `Escaped. Waiting for the match to end.`, or `Eliminated. Watching until the match ends.`
  - One line of team progress: `Terminals <completed>/<TerminalsRequired> · Exit locked|open`. Completed means `Progress == 100`.
  - While the local player has the `Hacking` flag, the nearest terminal's progress `%`.
  - Survivors may see every terminal's progress and the exit state (team information).
- **Killer:**
  - Exit state stays on the exit itself, in the world.
  - Skill readout bottom-left: `Q · <skill name>`, or `Passive · Packet Afterimage`.
- **Survivor skill readout:** inside the bottom-centre HUD, `Q · <skill name>`.
- **Skill names:** a client-side table by (role, SkillId) in GDD §4 order.
  - Killer: 0 Ping Scanner, 1 Overclock, 2 Packet Afterimage (passive), 3 Firewall Block.
  - Survivor: 0 Dummy Packet, 1 Stealth Relay, 2 Traffic Overload, 3 Emergency Restore, 4 White Noise.
  - Add `// TODO: switch to Shared SkillIds when U2b merges`.
  - There is no cooldown display yet: the server does not send cooldowns (D3).
- **Input:**
  - Hold `E` sends `C2S_Interact { Active = 1 }` once on press and `{ Active = 0 }` once on release, also on focus loss, cursor release, match end and disconnect. No repeats while held. Survivors only.
  - `Q` sends `C2S_UseSkill { SkillId = MatchStart.SkillId }` on press. It is not sent for a passive skill and not sent while not Normal.
  - Write the edge logic in pure classes and test it.
- **SkillEffect:** accept and ignore safely for now (log at most once per type). No visuals yet.
- **Lagging survivors in the world:**
  - In the survivor view, Lagging allies' capsules use the Signal amber tint, so allies can find them to rescue (team info).
  - Eliminated or Escaped survivors are hidden in both views.

### 5. Dev capture additions (keep `-ptk-*` inert when absent)
- `-ptk-offline`: start offline instead of joining. Combines with `-ptk-bots`, `-ptk-role`, `-ptk-start`, `-ptk-hold-w`, `-ptk-shots` and `-ptk-quit-after`.
- `-ptk-preview-result <SurvivorEscaped|AllSurvivorsDown|TimeUp>`: after the play screenshots, the client feeds itself a synthetic `S2C_GameResult` so the result screen can be captured before U2b lands. Dev-only, local dispatch only, never sent to a server; mark the screenshot as a preview.
- Extend the DevArgs tests.

## Tests (EditMode, `Client/Assets/Tests/EditMode/`)
- **Offline end-to-end** (real `GameServer` thread, every wait has a timeout ≤ 5 s):
  1. `OfflineLink` start → JoinResult.
  2. LobbyState with `IsOffline = 1`.
  3. SetBots 7 → LobbyState shows 7 bots. SetBots 1 → 1 bot.
  4. SetRole Killer → StartMatch → MatchStart Role Killer → at least one WorldState.
  5. Dispose → `Disconnected` delivered via Pump; the game thread stops within 2 s.

  Assert that the link's transport is `InMemoryTransport` and that no `NetClient` was created.
- Error-message mapping, result-copy mapping, Interact edge logic, UseSkill rules (passive / not Normal), skill-name table, survivor status text, terminals line.
- All existing tests (implementer and QA) keep passing.

## Verification you run yourself (as in U4 a3)
- Unity batch: import with 0 `error CS`, EditMode tests, `BuildScript.BuildWindows`. One Unity process at a time.
- **Screenshots** in `docs/qa/U4b-screens/` from the built player at 1280×720:
  - `menu.png`
  - `offline-lobby.png`: after `-ptk-bots 7` and `-ptk-role survivor`.
  - `offline-play-survivor.png`: with `-ptk-hold-w`, showing the HUD with Signal, the terminals line and the Q skill.
  - `offline-play-killer.png`
  - `result-preview-*.png` for the three reasons.
  - `online-lobby.png`: against the real server `dotnet <SCRATCH>\u2a\artifacts\bin\Server\release\Server.dll --port 7777 --bots 3`. It may raise a Windows Firewall prompt; never answer or click it, loopback works regardless.
- **No-socket proof on the built player.** Run `PingTheKiller.exe -ptk-offline -ptk-bots 3 -ptk-start -ptk-quit-after 12`. While it is in play, record `Get-NetTCPConnection -OwningProcess <pid>` and `Get-NetUDPEndpoint -OwningProcess <pid>` (PowerShell). Expected: no Listen entries and no TCP connections. Paste the output in the report. Also confirm no firewall prompt appeared for PingTheKiller.exe.
- Stop every server or player process you start.

## Report
`docs/qa/U4b-impl-a1.md` (≤ 300 words). Header: `Implementer: Claude Opus subagent (quota switch from Codex)`. Include:
- what changed
- commands and results (test counts from the XML)
- the screenshot list
- the socket-proof output
- what is pending until U2b merges (a real GameResult, hacking/Lagging values, SkillIds)
- what needs a human
