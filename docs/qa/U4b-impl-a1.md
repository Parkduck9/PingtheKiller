Implementer: Claude Opus subagent (quota switch from Codex)

## What changed (`Client/` only, plus Unity-generated `Server.Core/**/*.meta`)
- `Packages/manifest.json` + both asmdefs reference `PingTheKiller.ServerCore`.
- `Net/IGameLink.cs` (`ReceivedPacket`, `LinkClock`); `NetClient` implements it; new `OfflineLink` (`ServerOptions{IsOffline, BotCount=3}`, `InMemoryTransport` or throw, Pump-only dispatch, `Dispose` stops the server on a helper thread). `ClientApp` talks only to `IGameLink`.
- `Net/ConnectionMessages.cs`: fixed copy for unreachable / 5 s timeout / lost / lobby timeout / rejected; raw text goes to `Debug.Log` only.
- `MapSource` deleted; `World/SharedMap.cs` renders `FixedMap`.
- Menu Offline button; lobby `Lobby · offline`, number column, nickname + markers; result screen; survivor bottom-centre HUD (Signal, status, terminals, hacking %, `Q · skill`, cursor hint only when released); killer `Q · skill` / `Passive · …` bottom-left; exit label above the ring; F3 dev line (RTT online, "Offline"); panel scale [0.8, 2]; Lagging allies amber, Escaped/Eliminated hidden.
- Tested pure classes: `HudCopy`, `ResultCopy`, `LobbyCopy`, `InteractInput`, `SkillInput`.
- Dev: `-ptk-offline`, `-ptk-preview-result`.

## Verification
- Unity batch: 0 `error CS`. EditMode 341/341 (126 implementer, 215 QA).
- `BuildScript.BuildWindows`: Success.
- Screens (`docs/qa/U4b-screens/`, built player 1280×720): `menu`, `offline-lobby` (7 bots, Survivor), `offline-play-survivor` (Signal, `Terminals 0/4 · Exit locked`, `Q · White Noise`), `offline-play-killer`, `result-preview-{survivorescaped,allsurvivorsdown,timeup}` (marked as preview), `online-lobby` (real U2a server, `--bots 3`).

## Socket proof (`-ptk-offline -ptk-bots 3 -ptk-start -ptk-quit-after 12`, in play)
```
t=4 s / t=8 s  Get-NetTCPConnection -OwningProcess 23312 : (none)
               Get-NetUDPEndpoint  -OwningProcess 23312 : (none)
control, online: 127.0.0.1:58169 -> 127.0.0.1:7777 Established (+ Mono loopback pair)
```
No firewall rule or prompt for PingTheKiller.exe (rules exist only for Server.exe).

## Pending until U2b merges
Real `S2C_GameResult` and the lobby return (only previewed), `Progress`/`Status`/`StatusTimer`/`Hacking` values (server sends 0/Normal), Shared SkillIds (`TODO` in `HudCopy`), SkillEffect visuals, cooldowns.

## Needs a human
F3 dev line and the released-cursor hint (no keystrokes in capture), hold-E/Q feel, audio, gamepad.
