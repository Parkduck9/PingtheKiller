# TCP QA harness

The executable references **Shared** plus **Server.Core for static map geometry only**: it may read `PingTheKiller.ServerCore.Map` (`FixedMap` walls, terminal positions, exit, `PlayerRadius`, `CanStand`) to plan H6 walking routes, and must never use `GameServer`, transports, `ServerOptions`, `Player` or any other Server.Core type. `ServerCoreGuardTests` enforces this three ways: a source scan of `Tools/Harness/**/*.cs` (every `ServerCore` token must be `ServerCore.Map`), a metadata scan of every type reference compiled into `Harness.dll` that resolves to the Server.Core assembly (signatures, locals, method bodies, lambdas, async state machines), and the exact `Harness.csproj` reference list. It uses real `TcpClient` connections, with an independent `PacketAssembler` and bounded receive queue per connection. All fake servers are confined to `Tools/Harness.Tests`.

## Server instances and flags

| Check | Server instance and flags | Setup |
|---|---|---|
| H1 | Any reachable server, e.g. `--port 7777 --bots 7 --autostart`, or the sandbox lobby instance below | Unjoined, dedicated Ping client; consumes no player slot. |
| H2 | Fresh **sandbox** lobby instance (U2a rules only): `--port 7777 --bots 0 --bind 127.0.0.1 --sandbox`, **without `--autostart`** | Harness connects first as host, reduces initial bots to 0/1 (U2a may clamp to two participants), joins second human, sends `SetRole(Killer)`, `SetBots(6)`, waits for lobby acknowledgement, then `StartMatch`. Observes both human streams for 60 seconds. |
| H3 | Same lobby/match as H2, or fresh lobby instance with the same flags if selected alone | Host is Killer. Controlled survivor first moves (positive Signal baseline), sends zero input, then sends no input. After the 0.15-second grace plus 0.25-second margin, captures all killer frames for at least 3.1 seconds, scans X and Z bytes independently, and verifies survivor-side killer omission. |
| H4 | Same lobby/match as H2/H3, or fresh lobby instance with the same flags if selected alone | Separate unjoined throwaway sockets send five invalid lengths and an unknown ID; each must disconnect. NaN, `(100,100)`, and 500-input abuse use the existing survivor socket. A server disconnecting an abusive mover is acceptable when the Killer control Ping/Pong still works; only a closed socket is replaced for the next attack, without requiring H5 identity restoration. Disconnects and replacement identities are recorded. |
| H5 | Same lobby/match as earlier selected checks, or fresh lobby instance with the same flags if selected alone | Stops the survivor, records its own state, drops its socket, rejoins with token, and verifies identity/position/role/status/capture count within five seconds. |
| H6 | A **second**, **normal (non-sandbox)** fresh lobby instance: `--port 7778 --bots 0 --bind 127.0.0.1`, **without `--autostart`**, passed as `--h6-port 7778` (`--h6-host`, default = `--host`) | Three humans: host joins, `SetBots(0)`, `SetRole(Killer)`, survivors A and B join, `StartMatch`; MatchStart roles and `TerminalsRequired` (2 of 4 for two survivors) are asserted. See "H6 scenario" below. |

A full run needs **two new lobby server instances**: a **sandbox** one (U2a rules only, `--sandbox`) for H1-H5, because H2-H5 occupy its match, and a **normal** one (full U2b rules) for H6, which needs a lobby of its own:

```powershell
dotnet run --project Server -- --port 7777 --bots 0 --bind 127.0.0.1 --sandbox   # H1-H5 (U2a rules)
dotnet run --project Server -- --port 7778 --bots 0 --bind 127.0.0.1             # H6 (full rules)
# In a third terminal:
dotnet run --project Tools/Harness -- --host 127.0.0.1 --port 7777 --h6-port 7778 --report docs/qa/harness-latest.json
```

`--only H6` without `--h6-port` uses `--port` (one fresh server). If H6 is selected together with any of H2-H5 and `--h6-port` is missing or equals `--port` on the same host (string comparison of `--h6-host`/`--host`), H6 fails at once with `H6 needs its own fresh server: pass --h6-port`, without network I/O.

The harness never restarts a server and cannot turn an active match back into a lobby. Within a run, selected H2-H5 checks reuse the two human identities and six configured bots. A new independent invocation containing any H2-H5 check needs a new lobby server (or an otherwise genuinely fresh lobby), even when using `--only`. Merely reconnecting new humans does not reset U2a match state. A failed lobby setup makes subsequent match checks fail with the same setup evidence.

```powershell
# Against a fresh lobby, shorter sync observation:
dotnet run --project Tools/Harness -- --only H2,H3 --duration 2 --report artifacts/harness-subset.json
# H1 alone can also use an already running --autostart server:
dotnet run --project Tools/Harness -- --only H1 --report artifacts/harness-framing.json
dotnet build Tools/Harness/Harness.csproj -warnaserror
dotnet test Tools/Harness.Tests/Harness.Tests.csproj
```

## Scoring and timing

Checks award full points or zero. Scores are always out of 100; `--only` does not rescale them. `gatesPassed` requires H1, H3, and H4 to be present and pass. An omitted gate remains unverified. Exit 0 requires all gates and `total >= --threshold` (default 90); exit 1 means checks/threshold failed; exit 2 means invalid arguments or report I/O failed. All six passing yields 100; H1-H5 passing with H6 failing yields 90. Partial runs are diagnostic and can exit 1 even if every selected check passed.

## H6 scenario

Three real TCP clients; every client is steered from **its own** observed position in its own WorldState (the killer sees itself, survivors see themselves) with `C2S_MoveInput` once per received WorldState (~20 Hz), zero input on arrival. Routes come from a grid planner over `FixedMap` (0.5 m cells, `PlayerRadius` + 0.35 m margin, 8-connected, no corner cutting, line-of-sight smoothing); survivors route around the standing killer (4 m). Every leg has a timeout of distance / role speed × 1.5 + 3 s; a stuck mover fails with leg, position, waypoint and target. Steps, each with its own deadline and positive evidence:

1. **Hack**: A and B walk to the cheapest incomplete terminal (within 0.6 m), hold `C2S_Interact{1}` until it shows `Progress == 100` (limit 18 s), release, repeat until `TerminalsRequired`; the progress values are recorded. Then `ExitOpen == 1` must be seen by all three clients.
2. **Capture**: B walks >= 9 m from A (and away from the killer's line to A) and stands still. The killer walks to A at full speed until A leaves `Normal` **in A's own stream** (the killer stream is never searched for survivor Ids), then stops with zero input. A must show `Lagging`, `CaptureCount == 1`, `StatusTimer > 0` in its own view. The killer then **holds contact for 3 s** (longer than the 2 s execution grace + 1 s, so a server that re-arms while in contact is caught even with the grace) (distance <= `CaptureRadius` checked every A frame) and A must stay `Lagging` throughout. This locks in the U2b rule: execution needs the killer to leave the 1.2 m radius and re-enter it (the capturing contact never executes). The check passes with or without the planned 2 s post-capture execution grace and does not depend on it. B must still be `Normal`/0.
3. **Retreat**: the killer leaves the capture zone, walks to a point >= 9 m from A, >= 6 m from B and >= 5 m from B's line to A (preferably off A's line to the exit), and stands still with zero input. It never re-enters A's radius; after the rescue its distance to A must still exceed `RescueRadius` + 1 m.
4. **Rescue**: B walks within 0.6 m of A and stays until A is `Normal` with `CaptureCount == 1` (limit 3 s + 5 s).
5. **Escape**: the survivor with the shorter route walks to the exit (recorded in the evidence); the other stands still. Positive escape evidence: in the escaper's own stream, the last own state before the GameResult must be `Escaped` or within `ExitRadius` + 0.5 m of `FixedMap.Exit`, else "GameResult before escaper reached the exit".
6. **GameResult**: every human must receive exactly one `S2C_GameResult` within a 1 s drain after the first, byte-identical, `WinnerRole == Survivor`, `Reason == SurvivorEscaped`, `EscapedIds == [escaper]`. Whether a `LobbyState` follows is recorded as evidence only.

**Exit integrity**: on every WorldState of every stream (MatchStart to the end), `ExitOpen == 1` requires at least `TerminalsRequired` terminals at `Progress == 100` in that same frame.

**Killer-coordinate leak scan** (MatchStart to the end of the drain): every killer X/Z in the killer's own WorldState entries is collected. Every survivor-bound frame is scanned structurally. (1) The decoded packet must re-encode to exactly the raw bytes; extra or unexplained bytes fail. (2) The killer bit patterns are compared with every decoded 4-byte field: floats, ints reinterpreted, 8-byte fields as two halves, and string bytes at any alignment inside the string. A match in a legitimate field is a tolerated coincidence: survivor (A/B id) `PlayerState` X/Z/Yaw/StatusTimer, `TerminalState` X/Z, `TimeLeft`, and `SkillEffect` X/Z when the caster is a survivor. Any other match is a leak. Windows across field boundaries are never formed, so a round killer coordinate (e.g. 5.0 = `00 00 A0 40`) cannot be faked by zero Role/Status bytes plus a survivor X. The scan runs while streaming (values known so far) and once more at the end over every frame. Any survivor-bound WorldState with a Killer-role entry or the killer's id also fails. H6 asserts nothing about which survivor Ids the killer view contains (on the normal server decoys may borrow real survivor Ids and Packet Afterimage keeps a survivor visible 1 s after Silent); the harness never uses skills. A GameResult before the escape step fails. Whole-check budget: 150 s (the scenario takes about 50 s on the fake).

### Known limits (accepted, U3b QA F4-F8)

- **F4**: a killer coordinate copied into a survivor-shaped legitimate field (A/B `PlayerState` X/Z/Yaw/StatusTimer, `TimeLeft`, a terminal, or a survivor-cast `SkillEffect`) counts as a coincidence and is not detected.
- **F5**: a duplicate GameResult arriving more than 1 s after the first one is outside the drain window and is not seen.
- **F6**: rule magnitudes (hack time, rescue time, radii, speeds) are not measured precisely by H6; Server.Tests owns them.
- **F7**: failure evidence text depends on which step and which stream notices a fault first; tests match key phrases only.
- **F8**: the `--h6-host`/`--host` comparison is textual, so `localhost` and `127.0.0.1` count as different hosts.

## Timeouts and H1-H5 details

`--duration` is H2's observation time in seconds (default 60). `--timeout` is each connect, write, expected-packet read, idle receive, and disconnect deadline in seconds (default 5). Both accept 0.1..3600. Expected-packet reads have one total deadline, so unrelated traffic cannot keep them alive indefinitely. Whole checks have an additional finite budget; Ctrl+C cancels pending I/O and produces failed check results. H5 additionally enforces a five-second reconnect window. H1 uses a fixed seed, chunks 1..1500 bytes, random 1..3 ms delays, and a final ordered Ping barrier to detect duplicate trailing replies.

H2 measures timestamps recorded by the receive loop, not the time the check drains its queue. It requires arrival p99 <=120 ms per client, strictly increasing ticks, and no gap >2. The observed tick-period p99 (arrival delta / wire Tick delta) is evidence only and does not affect pass/fail. **TCP cannot expose the server's CPU/game-loop execution time**; that metric needs server-side instrumentation. No internal timing is fabricated.

H4 speed is displacement / (wire Tick delta / 20 Hz), so TCP coalescing does not cause false speed spikes. Missing self states, nonfinite positions, and insufficient observations on an open connection fail. NaN must also produce no movement. Both `(100,100)` and 500/s modes require observed max speed >=0.5 times role max and <=1.05 times role max. If movement is too low, the harness retries once with the opposite direction (`(-100,-100)` or `(-1,0)`); failure reports "no movement evidence". A defensive disconnect is acceptable, with any already measurable movement still checked for excess speed. Flood inputs use 25 batches of 20 every 40 ms, allow up to 1.15 seconds for timer jitter, and report elapsed time and actual achieved rate. Control Ping/Pong runs after every attack.

H3 searches complete received frames, including headers and non-WorldState packets, for each coordinate's exact little-endian bytes. It deliberately uses nontrivial coordinates, but a coincidental byte match elsewhere still conservatively fails and is reported as evidence. This byte-level criterion is stricter than semantic PlayerState omission.

Self-tests cover literal scores/JSON, malformed and invalid options, timing/masking/speed rejection, and real TCP Ping fakes that pass, omit a middle/final pong, duplicate the last pong, remain silent, or return malformed framing. A test-only lobby fixture verifies the shared H3/H4/H5 match, minimum-participant bot clamping, H4's socket retention, H5-only rebind, and deliberate masking regressions. Additional fakes prove broken reconnect fails H5 independently of H4, 10x speed and stationary movers fail H4, opposite-direction retries work, and defensive NaN/flood disconnects are accepted with a healthy control. Deterministic timing tests cover 70–120 ms H2 arrivals and the 1.15-second flood allowance with achieved-rate evidence. Disconnect tests reject silent open sockets and decoder failures. H2's wall-clock performance is left for a real deployment. A test-only rules fake (`ScenarioServer`, U2b contract: FixedMap movement at server speeds, hacking, capture/execution, rescue, escape, masking, GameResult) passes H6, and one broken variant each makes H6 fail: no GameResult, duplicate GameResult, wrong `EscapedIds` (empty, both, the other survivor), wrong winner, wrong reason, killer X/Z in a survivor-bound `SkillEffect`, a killer `PlayerState` in survivor worlds, exit never opens, no capture, no rescue. Unit tests cover the `--h6-port` rules, the planner, the leak-scan counting and the Server.Core guard. These tests validate the harness, not the unmerged U2a/U2b implementation.
