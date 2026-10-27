# TCP QA harness

The executable references **Shared only**. It uses real `TcpClient` connections, with an independent `PacketAssembler` and bounded receive queue per connection. All fake servers are confined to `Tools/Harness.Tests`.

## Server instances and flags

| Check | Server instance and flags | Setup |
|---|---|---|
| H1 | Any reachable U2a server, e.g. `--port 7777 --bots 7 --autostart`, or the lobby instance below | Unjoined, dedicated Ping client; consumes no player slot. |
| H2 | Fresh lobby instance: `--port 7777 --bots 0`, **without `--autostart`** | Harness connects first as host, reduces initial bots to 0/1 (U2a may clamp to two participants), joins second human, sends `SetRole(Killer)`, `SetBots(6)`, waits for lobby acknowledgement, then `StartMatch`. Observes both human streams for 60 seconds. |
| H3 | Same lobby/match as H2, or fresh lobby instance with the same flags if selected alone | Host is Killer. Controlled survivor first moves (positive Signal baseline), sends zero input, then sends no input. After the 0.15-second grace plus 0.25-second margin, captures all killer frames for at least 3.1 seconds, scans X and Z bytes independently, and verifies survivor-side killer omission. |
| H4 | Same lobby/match as H2/H3, or fresh lobby instance with the same flags if selected alone | Separate unjoined throwaway sockets send five invalid lengths and an unknown ID; each must disconnect. NaN, `(100,100)`, and 500-input abuse use the existing survivor socket. A server disconnecting an abusive mover is acceptable when the Killer control Ping/Pong still works; only a closed socket is replaced for the next attack, without requiring H5 identity restoration. Disconnects and replacement identities are recorded. |
| H5 | Same lobby/match as earlier selected checks, or fresh lobby instance with the same flags if selected alone | Stops the survivor, records its own state, drops its socket, rejoins with token, and verifies identity/position/role/status/capture count within five seconds. |
| H6 | No server required | `skipped (needs U2b)`, score 0, `passed: false`. |

Run all checks against **one new lobby server**:

```powershell
dotnet run --project Server -- --port 7777 --bots 0
# In another terminal:
dotnet run --project Tools/Harness -- --host 127.0.0.1 --port 7777 --report docs/qa/harness-latest.json
```

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

Checks award full points or zero. Scores are always out of 100; `--only` does not rescale them. `gatesPassed` requires H1, H3, and H4 to be present and pass. An omitted gate remains unverified. Exit 0 requires all gates and `total >= --threshold` (default 90); exit 1 means checks/threshold failed; exit 2 means invalid arguments or report I/O failed. All H1-H5 passing yields 90 points while H6 remains skipped. Partial runs are diagnostic and can exit 1 even if every selected check passed.

`--duration` is H2's observation time in seconds (default 60). `--timeout` is each connect, write, expected-packet read, idle receive, and disconnect deadline in seconds (default 5). Both accept 0.1..3600. Expected-packet reads have one total deadline, so unrelated traffic cannot keep them alive indefinitely. Whole checks have an additional finite budget; Ctrl+C cancels pending I/O and produces failed check results. H5 additionally enforces a five-second reconnect window. H1 uses a fixed seed, chunks 1..1500 bytes, random 1..3 ms delays, and a final ordered Ping barrier to detect duplicate trailing replies.

H2 measures timestamps recorded by the receive loop, not the time the check drains its queue. It requires arrival p99 <=120 ms per client, strictly increasing ticks, and no gap >2. The observed tick-period p99 (arrival delta / wire Tick delta) is evidence only and does not affect pass/fail. **TCP cannot expose the server's CPU/game-loop execution time**; that metric needs server-side instrumentation. No internal timing is fabricated.

H4 speed is displacement / (wire Tick delta / 20 Hz), so TCP coalescing does not cause false speed spikes. Missing self states, nonfinite positions, and insufficient observations on an open connection fail. NaN must also produce no movement. Both `(100,100)` and 500/s modes require observed max speed >=0.5 times role max and <=1.05 times role max. If movement is too low, the harness retries once with the opposite direction (`(-100,-100)` or `(-1,0)`); failure reports "no movement evidence". A defensive disconnect is acceptable, with any already measurable movement still checked for excess speed. Flood inputs use 25 batches of 20 every 40 ms, allow up to 1.15 seconds for timer jitter, and report elapsed time and actual achieved rate. Control Ping/Pong runs after every attack.

H3 searches complete received frames, including headers and non-WorldState packets, for each coordinate's exact little-endian bytes. It deliberately uses nontrivial coordinates, but a coincidental byte match elsewhere still conservatively fails and is reported as evidence. This byte-level criterion is stricter than semantic PlayerState omission.

Self-tests cover literal scores/JSON, malformed and invalid options, timing/masking/speed rejection, and real TCP Ping fakes that pass, omit a middle/final pong, duplicate the last pong, remain silent, or return malformed framing. A test-only lobby fixture verifies the shared H3/H4/H5 match, minimum-participant bot clamping, H4's socket retention, H5-only rebind, and deliberate masking regressions. Additional fakes prove broken reconnect fails H5 independently of H4, 10x speed and stationary movers fail H4, opposite-direction retries work, and defensive NaN/flood disconnects are accepted with a healthy control. Deterministic timing tests cover 70–120 ms H2 arrivals and the 1.15-second flood allowance with achieved-rate evidence. Disconnect tests reject silent open sockets and decoder failures. H2's wall-clock performance is left for a real deployment. These tests validate the harness, not the unmerged U2a implementation.
