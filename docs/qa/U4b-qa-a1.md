# U4b QA — attempt 1

QA: Claude Sonnet (implementer: Claude Opus subagent, different model). Date 2026-10-27. Branch `claude/u4b-offline`, nothing committed.

## Verdict summary
- No blocker, no major. F-01 was a mistake in my brief (closed), F-02 is fixed by the orchestrator; F-03 to F-07 are cosmetic or informational.
- Product behaviour: PASS on every U4b-a1 bullet that can be verified without a human (table below).
- 140 new QA test cases, none red after the orchestrator rulings (F-01 re-scoped, F-02 fixed).
- SLOP section 9: **91 / 100**, no gate failure (threshold 85).

## What I added (only under `Client/Assets/Tests/EditMode/QA/` and this file)
| File | Cases | Covers |
|---|---:|---|
| `OfflineLinkQaTests.cs` | 15 | brief item 1, real `GameServer` thread over `InMemoryTransport` |
| `OfflineStructureQaTests.cs` | 12 | item 2, IL scan + comment-free source scan + lifecycle code-read |
| `ConnectionMessagesQaTests.cs` | 22 | item 3 |
| `ResultCopyQaTests.cs` | 26 | item 4 |
| `ActionInputQaTests.cs` | 26 | item 5 |
| `HudCopyQaTests.cs` | 31 | item 6 and lobby rows |
| `MapQaTests.cs` | 6 | item 7 |
| `DevArgsOfflineQaTests.cs` | 30 | item 8 |
| `PanelScaleQaTests.cs` | 13 | U4b-a1 3.7 |
| `CopyHygieneQaTests.cs` | 4 | SLOP section 4 over every string literal |
| `QaSupport.cs` | - | source reader, IL scanner (shared helpers) |
| `PingTheKiller.Client.QA.Tests.asmdef` | - | added reference `PingTheKiller.ServerCore` |

Earlier QA tests are untouched. Nothing in them is wrong by design (checked: `NetClientQaTests`, `DevArgsQaTests`, `EndpointQaTests` only use API that still exists).

### How they were run
Unity was not launched (brief). I compiled everything except `PanelScaleQaTests` (needs UI Toolkit) against the real `Shared`, `Server.Core` and Client sources with a throw-away NUnit look-alike runner in the scratchpad (`C:\Users\User\AppData\Local\Temp\claude\C--Unity-MultiPlayGame-PingtheKiller--claude-worktrees-u4-unity-client-1c3a1a\7a3836ba-cd40-46bb-a21b-f44576e596ab\scratchpad\qa\`), C# 9, net10. Result: **127 cases run: 126 pass, 0 fail, 1 ignored** (`ClientAppStartOfflineConstructsNoSocketOrNetClient`: ClientApp needs the Unity assembly, it runs in Unity). The threaded tests were run three times, same result. Real NUnit in Unity may differ in message text only. `PanelScaleQaTests` compiled in my head only: it calls `ClientScreens.PanelScale`, `MinScale`, `MaxScale`, all `public`.

Every threaded wait is at most 5 s, every link is disposed in `finally`, the runner watchdog is 60 s, nothing hung. The structure tests include a negative control (the IL scan must find `TcpClient` inside `NetClient`, otherwise the scan is declared inconclusive instead of passing).

## Findings
| ID | Severity | Title |
|---|---|---|
| F-01 | closed | Brief error: Shared rejects SetBots above 7 at decode; test re-scoped to the client clamp |
| F-02 | fixed by orchestrator | EndMatch now releases Interact only via TryRelease |
| F-03 | minor (cosmetic) | Offline lobby lists the local player in slot 4; bot names climb ("Bot 8") |
| F-04 | minor | Menu shows the developer line `Run the server on this PC: dotnet run --project Server` |
| F-05 | info | Offline waiting line differs from the brief's sentence (justified) |
| F-06 | info | Result screen has no hack/rescue counts (GDD section 7) |
| F-07 | info | An offline link stops itself if the main thread does not Pump for about 25 s |

### F-01 closed: spec correction by the orchestrator (no product defect)
- My QA brief was wrong, not the implementation. Shared protocol v0.2 is frozen: `C2S_SetBots.Read` uses `ReadByte(7)`, so a raw Count 9 is malformed and the connection is closed. U4b must not change Shared/Server.Core. `GameServer.SetBotCount` still clamps 0 to 1 (test `SetBotsZeroIsClamped...` passes).
- The red test was re-scoped to the actual contract (the `Finding-F01` category is gone): `TheClientNeverEmitsASetBotsCountAboveSevenAndAPlusAtSevenBotsIsDisabled` (the only `new C2S_SetBots` in `ClientApp.cs:71` is clamped to 0..7, `+ bot` is disabled when the eight slots are full at `ClientScreens.cs:215`, `-ptk-bots` above 7 is rejected) and `ARawSetBotsCountOfNineClosesTheOfflineSessionPerTheFrozenProtocol` (documents the close). Both pass.

### F-02 fixed by orchestrator
- Was: `ClientApp.EndMatch` sent an unconditional `Interact { Active = 0 }` (also for killers). Now `ClientApp.cs:193` uses `interact.TryRelease`, so a zero goes out only if `Active = 1` was sent and killers never send Interact. Test `ActionInputQaTests.MatchEndSendsInteractZeroOnlyWhenAPressIsOutstanding` (source check, unchanged) now passes.

### F-03 minor, cosmetic: slot order and bot names
- `offline-lobby.png`: rows are `1 Bot 1 · bot`, `2 Bot 2 · bot`, `3 Bot 3 · bot`, `4 Player · host · you`, `5 Bot 5 · bot` ... `8 Bot 8 · bot`. The server creates the bots first and the human joins afterwards. The player is the host of an offline lobby yet sits in the middle; bot numbers are player ids, so they skip and grow after removing and adding bots. Server.Core behaviour, not a client bug; consider showing the local player first. Not a spec bullet.

### F-04 minor: developer instruction on the menu
- `menu.png` shows `Run the server on this PC: dotnet run --project Server`. It was accepted in U4 but it is the first line a Store reviewer or player sees; it tells a normal player to run a .NET command. Hide it outside dev, or move it behind the Join path.

### F-05 info: offline waiting line
- Brief: `Waiting for the host to reopen the lobby.` Offline the code says `Waiting for the lobby to reopen.` (`ResultCopy.LobbyLine`) because the player is the host. Judged a justified deviation; online text matches exactly (tested).

### F-06 info: result screen content
- GDD section 7 lists winner, escaped players, hacks and rescues. `S2C_GameResult` carries no hack/rescue counts, so the screen shows winner, reason, escaped names and the local outcome only. Needs a protocol decision (U2b).

### F-07 info: backlog guard
- `OfflineLink.Drain` stops the session when more than 512 packets are queued (about 25 s of world states at 20 Hz). `Application.runInBackground = true` makes a stall unlikely; a long main-thread freeze would show `The offline match stopped...`. Acceptable, noted.

## Spec table: docs/briefs/U4b-a1.md sections 1 to 5
| Bullet | Result | Evidence |
|---|---|---|
| 1 manifest has `com.pingthekiller.servercore`; client asmdef references `PingTheKiller.ServerCore` | PASS | `Client/Packages/manifest.json:4`; `MapQaTests.TheClientAssemblyStillReferences...` |
| 1 `IGameLink` with Send, Pump, PacketReceived, Disconnected, RttMilliseconds (-1), IsConnected, Dispose; `NetClient` implements it | PASS | `Net/IGameLink.cs`; `NetClient.cs:14` (`: IGameLink`) |
| 1 `OfflineLink`: `IsOffline=true`, `CreateTransport` must be `InMemoryTransport` else throw, wire `ConnectionAccepted`, `Start`, `Connect`, `server.Start()` order | PASS | `OfflineLink.cs:28-44`; test `JoinResultComesFirst...` asserts `TypeOf<InMemoryTransport>`; `ServerOptionsHandsOut...` |
| 1 Pump-only dispatch on the calling thread, arrival timestamps | PASS | `OfflineLink.cs:64-73`; tests `PacketsAreHeldBackUntilPump...` (0 off-thread, 0 outside Pump, no dispatch between pumps), `ArrivalTimeIsStamped...` |
| 1 Send uses `PacketCodec.Encode` + `connection.Send`; client sends `C2S_Join` itself | PASS | `OfflineLink.cs:44,60` |
| 1 `ClientApp` talks only to `IGameLink` | PASS | `ClientApp.cs:19` (`IGameLink link`); only `Join` builds a `NetClient` (`ClientAppConstructsNetClientOnlyInsideJoin`) |
| 1 No socket on the offline path | PASS | `OfflineLink` IL and source reference no `Tcp*`, `Socket`, `NetClient`; `GameServer`, `InMemoryTransport`, `FramedConnection` IL clean; `NetClient.Created` unchanged over a full session. Runtime proof is the orchestrator's (0 TCP/UDP endpoints) |
| 1 Dispose closes the connection, stops the server off the main thread, leaves no running thread | PASS | `OfflineLink.cs:87-103`; Dispose returns well under 500 ms, game-loop thread (background, named `PingTheKiller.GameLoop`) ends within 2 s, also mid-match and when disposed right after construction |
| 1 Menu Offline button works; the "arrives in U4b" line is gone | PASS | `ClientApp.cs:69`, `ClientScreens.cs:118`; `ThePreviousOfflineStubLineIsGone` |
| 1 Offline lobby: player is host, bots 1 to 7, role Survivor/Killer/Random, Start, same lobby screen, title `Lobby · offline`, no RTT, Leave disposes | PASS | tests JoinResult/SetBots 1,7/Role; `ClientScreens.cs:201,214-222`; RTT only in the F3 dev line (`ClientApp.cs:218`); Leave -> `Disconnect` -> `Release` -> `Dispose` (`EveryNewLinkReleases...`, `LeaveAndDisconnectAndDestroy...`) |
| 2 Map from `FixedMap` (walls, terminals, exit, size, centre); `MapSource` and its TODO removed | PASS | `World/SharedMap.cs`, `WorldView.cs:36-47`; `MapQaTests` (8 walls, 6 terminals, exit 58/30, centre 30/30, no `MapSource`, no hand-built `Wall`) |
| 3.1 fixed connection messages, no raw text, pure mapping | PASS | `ConnectionMessages.cs`; 22 tests incl. Korean, German, Japanese, Spanish, Russian exception text never shown |
| 3.1 lobby timeout without "protocol v0.2" | PASS | `ConnectionMessages.LobbyTimeout` |
| 3.2 no internal unit names or protocol versions on screen | PASS | `CopyHygieneQaTests` over every string literal; only comments carry `U2b` |
| 3.3 result screen: stop play, unlock cursor, stop beep, final zero MoveInput | PASS | `ClientApp.cs:187-196` |
| 3.3 final Interact 0 | PASS | F-02 fixed by orchestrator |
| 3.3 reason sentences, local outcome, escaped names with `Player <id>` fallback | PASS | `ResultCopyQaTests` (26) |
| 3.3 `Back to lobby` primary, disabled until a LobbyState after the result; `Waiting for the host to reopen the lobby.`; `Leave` secondary | PASS (offline wording F-05) | `ClientScreens.cs:157-158,266-271`; `ClientApp.cs:141`; screenshots |
| 3.3 no VICTORY banner, no confetti | PASS | screenshots; `NoResultTextUsesBannerWords...` |
| 3.4 lobby rows nickname + `· bot`/`· host`/`· you`, fixed-width number column | PASS | `LobbyCopy.cs`; `.slot-number { width: 32px }`; `offline-lobby.png` |
| 3.5 exit label no longer overlaps the ring | PASS | `ClientApp.cs:253`; `offline-play-killer.png` (label above the ring) |
| 3.6 RTT off the survivor HUD, F3 dev line (default off); cursor hint only while released | PASS (F3 key itself needs a human) | `ClientApp.cs:216-218`, `ClientScreens.cs:245`; no RTT in any screenshot |
| 3.7 panel scale clamp [0.8, 2] | PASS | `ClientScreens.cs:19-20,48-49`; `PanelScaleQaTests` |
| 4 survivor HUD: Signal meter, status line, `Terminals c/r · Exit locked|open`, hacking %, `Q · skill`, bottom-centre only | PASS | `HudCopyQaTests` (31); `offline-play-survivor.png` |
| 4 killer: `Q · skill` or `Passive · Packet Afterimage` bottom-left; exit state on the exit | PASS | `offline-play-killer.png` |
| 4 skill table by (role, id) in GDD order, TODO present | PASS | `HudCopy.cs:11-13`; server deals killer 0..3, survivor 0..4 (`GameServer.Lobby.cs` `StartMatch`) |
| 4 hold E: one `Active=1`, no repeats, one `Active=0` on release, focus loss, cursor release, match end, disconnect; survivors only | PASS | `ActionInputQaTests`; guard `ClientApp.cs:245` |
| 4 Q: UseSkill with MatchStart SkillId on press; passive killer skill and not-Normal send nothing | PASS | `ActionInputQaTests`; `ClientApp.cs:246` |
| 4 SkillEffect accepted, logged at most once per skill | PASS | `ClientApp.cs:178-180` |
| 4 Lagging allies amber in the survivor view; Escaped/Eliminated hidden in both views | PASS (code-read; not exercised until U2b sends statuses) | `WorldView.cs:92,95-96` |
| 5 `-ptk-offline`, `-ptk-preview-result` valid/invalid/missing; inert when absent; preview is local only and labelled | PASS | `DevArgsOfflineQaTests` (30); `ClientApp.cs:200-211` (`Preview`) has no `Send`; label in all three `result-preview-*.png` |
| Tests: offline end-to-end, mappings, edge logic, existing tests keep passing | PASS | implementer 126 + QA 215 reported green by the orchestrator; mine are additional |

## Threading and lifecycle review
- Main thread never waits on the game thread: PASS. `Send` and `Pump` take no lock a game-thread tick holds for longer than an enqueue (server-side connection gate is taken only by the sender, client-side gate only inside `Drain`, which only enqueues). `Stop` runs `server.Stop()` (which joins the loop) on a helper thread (`OfflineLink.cs:95-99`).
- `Dispose` from `OnDestroy`/quit: `ClientApp.cs:288` -> `Stop`; game thread ends within 2 s (tests). The helper thread and the game thread are both background threads.
- Link disposed on Leave (`ClientApp.cs:70,282`), on a second Offline and on Join after Offline (`Attach` starts with `Release()`, `ClientApp.cs:117`, which detaches events then `previous.Dispose()`, `:276-278`). Tested at link level with four successive links, each thread ended.
- Nothing hung: two links can run at once without cross-talk.

## Screenshots (all 8 opened with Read, 1280x720)
No wrap, overlap or clip in any of them. Exact on-screen strings:
- `menu.png`: `Ping the Killer` / `Run the server on this PC: dotnet run --project Server` / `Server IP:port` / field `127.0.0.1:7777` / `Join` (primary, filled) / `Offline` (outlined) / `Play against bots on this PC. No network needed.` Join dominates; no gradient, glow or emoji. See F-04.
- `offline-lobby.png`: `Lobby · offline`; rows `1 Bot 1 · bot`, `2 Bot 2 · bot`, `3 Bot 3 · bot`, `4 Player · host · you`, `5 Bot 5 · bot`, `6 Bot 6 · bot`, `7 Bot 7 · bot`, `8 Bot 8 · bot`; `Match setup`; `Bots: 7`; `− bot` (enabled), `+ bot` (disabled, muted); `Your role: Survivor`; `Survivor` (4 px left bar = chosen), `Killer`, `Random`; `Choose a role, then start the match.`; `Start` (primary), `Leave`. Number column aligned. No RTT. See F-03.
- `online-lobby.png`: `Lobby`; rows `1 Bot 1 · bot`, `2 Bot 2 · bot`, `3 Bot 3 · bot`, `4 Player · host · you`, `5 Empty` to `8 Empty` (muted); `Host controls`; `Bots: 3`; `− bot`, `+ bot`; `Host role: Random` (Random marked); `Start`, `Leave`. Row text is `Player · host · you`, not `Player · player`. No RTT in the lobby.
- `offline-play-survivor.png`: first-person view, two teal ally capsules, grey terminal cylinders, no killer. Bottom-centre HUD panel only: `Signal` (amber, amber underline), `Terminals 0/4 · Exit locked`, `Q · White Noise`. No RTT, no cursor hint (cursor captured). Required count 4 matches 7 survivors. World wall faces are black (unlit sides; not part of the HUD pass).
- `offline-play-killer.png`: top-down map, no panel. Red killer capsule with white ring bottom-left, four teal Signal blips, grey terminal discs, ring with the label `Exit · Locked` above it (no overlap). Bottom-left `Passive · Packet Afterimage`. No survivor list, count or hint. Terminals are small grey discs; still easy to confuse with a distant capsule (U4 follow-up 7, open).
- `result-preview-survivorescaped.png`: `Match result` / `Survivors win. Player and Bot 2 reached the Exit.` (bold) / `You escaped.` / `Preview: synthetic result from a dev flag, not sent by a server.` (small, muted) / `Waiting for the lobby to reopen.` / `Back to lobby` (disabled) / `Leave` (focused, white border).
- `result-preview-allsurvivorsdown.png`: `Killer wins. No survivor is left in play.` / `You won as the Killer.` / same preview label, waiting line and buttons. (This run was the killer; the other two were survivor runs.)
- `result-preview-timeup.png`: `Killer wins. Time ran out.` / `You did not escape.` / same.
- Leak check: survivor view never shows the killer; killer view shows only Signal survivors (server masking verified at link level: every survivor entry in a killer WorldState has the Signal flag, a survivor WorldState has no killer entry). Result screens are plain sentences, not a banner.
- The preview label is present on all three synthetic results.

## SLOP section 9
| Check | Max | Score | Gate | Notes |
|---|---:|---:|:---:|---|
| No V1-V12 visual tells | 20 | 19 | PASS | flat fills, 1 px lines, one accent each (amber Signal, teal ally, red killer), Inter font, no glow, gradient, emoji or default Unity look; -1 for the black unlit wall faces that hurt readability of the first-person scene |
| No L1-L8 layout tells, one spacing scale | 15 | 14 | - | left-aligned lists, primary/secondary sizes differ, no modal in play, USS spacing 4/8/12/16/24/32 only; -1 for the empty reserved message row between field and `Join` on the menu |
| Copy per section 4, fixed vocabulary | 15 | 14 | - | sentence case, no exclamation marks, errors say what and what to do, `Exit`, `Signal`, `Silent`, `Lagging`; -1 for F-04 |
| Every element answers state and action | 15 | 14 | PASS | each HUD line encodes a server state; -1 for the `Match result` title that adds nothing |
| Role-specific HUDs, no leak | 20 | 18 | PASS | different HUD per role, no hidden-state leak; -2 because the cooldown ring and frozen-ally markers are absent (server does not send cooldowns, D3, excluded by the brief) |
| States and keyboard/gamepad navigation | 10 | 8 | PASS | normal/hover/pressed/disabled/focused rules present for buttons, field and primary buttons (`ClientUI.uss`); disabled `Back to lobby` and focused `Leave` visible; -2 because keyboard Tab/arrow and gamepad traversal were not exercised |
| Contrast and 1280x720 readability | 5 | 4 | - | `#9A9890` on `#101113` is about 6:1; smallest text (14 px at scale 0.8, about 11 px) is legible but small, the preview note most of all |
| **Total** | **100** | **91** | no gate failure | threshold 85 |

## Pending until U2b merges
- Real `S2C_GameResult` and the return to the lobby (`Back to lobby` stays disabled; only previewed).
- `Progress`, `Status`, `StatusTimer`, `Hacking` flag values (server sends 0, Normal): Lagging text, escaped/eliminated text, hacking %, amber Lagging allies, hidden eliminated avatars are code-read only.
- Shared SkillIds (`HudCopy.cs:11` TODO), `S2C_SkillEffect` visuals, cooldowns.
- Server-side handling of Interact and UseSkill (`GameServer.cs` has a `TODO U2b`).

## Needs a human
- F3 dev line and the cursor-released hint (keystrokes), hold-E and Q feel, Esc/click cursor behaviour, focus loss with E held.
- Keyboard Tab/arrow and gamepad navigation, hover/pressed feel, audio (beep stop at match end).
- A real offline match to the end once U2b lands, and the Windows Firewall check on a clean machine (runtime socket proof was the orchestrator's).
- Whether `Run the server on this PC...` (F-04) should ship.

QA-PASS
