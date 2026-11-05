# Brief U6g-rtt-probe, attempt 1

Implementer: Codex `gpt-6.1-sol` (effort high). QA: Gemini Flash (different family). Worktree: `C:/Unity/MultiPlayGame/PtK-work/u6g`, branch `claude/sync-rtt-probe` (from `claude/sync-great-timing` `d598cd6`, not yet merged into main). Never push, never merge, never commit (the orchestrator commits after review).

## Goal

The sync-check judge (`Server.Core/Runtime/GameServer.Terminals.cs`: `SyncPress`, and the Fail limit in `UpdateSyncChecks`) subtracts `RttOf(player)` = `Player.RttSeconds`. Today nothing in production sets it (only `SetRttForTests`), so every online press is judged one real round trip late. With a great part of 0.035 × 1.1 s = 38.5 ms, Great is unreachable above roughly 20–35 ms RTT (`docs/qa/U6f-sync-timing-qa-a1.md`, minor finding 1).

Add a **server-initiated, server-timed RTT probe** that sets `Player.RttSeconds` for online human players.

## Design (decided by the orchestrator; deviate only with a reason in the report)

### Protocol v0.5: two new packets in `Shared/Runtime`

| ID | Name | Dir | Payload |
|---|---|---|---|
| 0x0014 | S2C_RttProbe | S→C | `uint Nonce` |
| 0x0015 | C2S_RttEcho | C→S | `uint Nonce` |

- Add both to `PacketId`, `ServerPackets.cs` / `ClientPackets.cs`, and `PacketCodec` decode, in the existing style. Trailing bytes / short payloads must be rejected exactly like the other fixed-size packets.
- The existing `C2S_Ping`/`S2C_Pong` stays unchanged. It is client-timed and the server must never use it for judging.

### Server (`Server.Core`)

Put the probe logic in a new partial `Server.Core/Runtime/GameServer.Rtt.cs` (plus a `.meta` with a fresh random 32-hex GUID, same format as `Player.cs.meta`). Constants live next to it (server-only tuning, like `MaxPressAgeSeconds`):
- `RttProbeIntervalSeconds = 1.0`
- `RttProbeTimeoutSeconds = 1.0`
- `MaxRttSeconds = 0.25`
- smoothing factor `1/8`

Rules:
1. **Who is probed.** Only a human player (not `IsBot`) with a live connection, in the lobby and in a match (so the estimate is warm before the first sync check). Connections that never joined get no probes. When `ServerOptions.IsOffline` is true the server sends **no probes at all**, and `RttSeconds` stays 0. Bots are never probed and `RttOf` keeps returning 0 for them.
2. **Sending.** Probes are issued on the game-loop thread, inside `Tick`. A player has at most **one outstanding probe**. Send a new one when none is outstanding and `clock >= nextProbeAt`. The first probe goes out on the first tick after the join.
   - Nonce: a fresh value from a **new private random stream** that is used for nothing else, nonzero, never sequential. Seed it like the other streams in `SeedPrivateStreams` (stream index 4 with the seed hook, CSPRNG otherwise). The nonce must reveal nothing about the other streams.
   - `probeSentAt = _clock()` read immediately before `Send`. Record it per player with the nonce. Then `nextProbeAt = probeSentAt + RttProbeIntervalSeconds`.
3. **Echo.** `C2S_RttEcho` goes through the normal input queue (it is **not** answered on the I/O thread like Ping). It is handled on the game-loop thread with the input's `ReceivedAt` (the receive-thread stamp that U6f added; today only `C2S_SyncPress` uses it).
   - Accept it only if this player has an outstanding probe and the nonce matches. Wrong nonce, no outstanding probe, duplicate, or an echo of an older probe: ignore it silently (no sample, no disconnect).
   - `sample = ReceivedAt − probeSentAt`, clamped to `[0, MaxRttSeconds]`. Clear the outstanding probe.
4. **Timeout.** An outstanding probe older than `RttProbeTimeoutSeconds` is cleared with **no sample**. The estimate keeps its value, and the next probe follows on schedule. An echo that arrives after its probe was cleared is ignored.
5. **Smoothing.** The first accepted sample sets `RttSeconds = sample`. Later samples apply `RttSeconds += (sample − RttSeconds) / 8`. Clamp the result to `[0, MaxRttSeconds]`.
6. **Reset.** On disconnect (`GameServer.cs` around line 346 already zeroes `RttSeconds`), also clear the outstanding probe, the sample count and the schedule. A reconnect starts fresh, with the first probe on the next tick.
7. **Log.** Append the judge's RTT in ms to the existing `sync player=… check=… result=…` log line (for example ` rtt=42`).
8. Keep `SetRttForTests`. Update its comment: it now overrides the probe's estimate.

Why this is safe (put a short version in the code comment):
- The server stamps both ends with its own clock.
- The nonce is unpredictable, so a client cannot echo before it has received the probe. RTT can never be measured shorter than the real path.
- Delaying or dropping echoes can only **raise** the estimate, and the clamp bounds it at 0.25 s. A larger RTT only shifts the judged moment earlier by a constant. The window width does not change, and a client already knows the zone from `S2C_SyncCheck`. So a client that delays its echoes gets nothing that a fair player with the same (clamped) RTT would not get: a shifted, same-width window and a Fail limit extended by the same RTT.

### Path symmetry (why the client echoes on the receive thread)

The client starts the needle at the **receive-thread** stamp of `S2C_SyncCheck` (`ReceivedPacket.Time`, `ClientApp.cs:281`). The press goes out through `NetClient.Send` (outgoing queue plus `wake`), which reaches the send thread. The echo must take the same path: probe in on the receive thread, echo out through the outgoing queue plus `wake`. This keeps Unity frame stalls out of the measurement, the same way the needle origin excludes them.

### Unity client (`Client/Assets/Scripts/Net/NetClient.cs`)

- Add `PacketId.S2C_RttProbe` to `IsServerPacket`.
- In `ReceiveLoop`, answer an `S2C_RttProbe` immediately on the receive thread: enqueue `C2S_RttEcho { Nonce }` on `outgoing` (count it in `queuedOut` like `Send` does) and `wake.Set()`. Do not forward the probe to `incoming` or to the main thread.
- The offline link receives no probes and needs no change.
- Update every EditMode test that pins the whitelist (`Client/Assets/Tests/EditMode/NetClientTests.cs`, `.../QA/NetClientWhitelistQaTests.cs`, and any other you find). Add an EditMode test where a local `TcpListener` fake server sends an `S2C_RttProbe` and receives the matching `C2S_RttEcho`. New test files need a `.meta` with a fresh GUID.
- You cannot run Unity in your sandbox. Say so in the report; the orchestrator runs EditMode and BuildWindows.

### Harness (`Tools/Harness`)

The harness must stay at 100/100 with `--expect-los`. If any harness reader or check reads frames strictly and would choke on, or count against, an `S2C_RttProbe` (strict `ReadAsync<T>` sequences, per-packet allow lists, the leak scan in `KillerLeakScan.cs`), make it skip or structurally accept the probe without weakening the check. The probe has no coordinate fields, so the structural leak scan should treat it as a known packet with nothing to scan. Optional: harness human clients may echo probes. Do not change scoring or thresholds.

## Tests (Server.Tests; add `Server.Tests/Core/RttProbeTests.cs` and a protocol test file or section)

Use the injected clock (`ServerOptions.ClockForTests`) and the in-memory transport, as `SyncTimingTests.cs` does. Every test drives the real `Tick` and real packets through the connection; do not call private methods by reflection.
1. Serialization round trip and golden bytes for both packets. Short and trailing payloads are rejected.
2. A joined human on an online in-memory connection receives a probe on the first tick. An echo 80 ms later (clock) gives `RttSeconds == 0.080`. A second sample of 0.160 gives `0.080 + 0.010`.
3. Clamp: an echo 0.6 s after the probe (inside the timeout) gives a sample of 0.25, and the estimate stays ≤ 0.25.
4. **Adversarial – cannot shrink.** None of these changes the estimate or produces a sample: an echo with a wrong nonce, an echo sent before any probe, a guessed "previous nonce + 1", a duplicate echo, or an echo of an older probe after a newer one went out. Assert also that consecutive nonces are not sequential and not zero.
5. **Adversarial – delay and drop.**
   - (a) A client that delays every echo by 0.5 s converges to exactly `MaxRttSeconds`, never above. Then press at the needle's true great moment as an honest client with 40 ms real latency would: the result is not Great (it is judged early). Pressing with the constant offset the inflated estimate implies gives Great over the same window width as a fair 0.25 s client. Assert both, to show the window does not widen.
   - (b) A client that never echoes: the estimate stays 0, a new probe goes out only after the timeout (no probe flood), and the outstanding probe count is never above 1.
   - (c) A client that answers promptly and then stops: the estimate holds its last value.
6. Bots: never sent a probe, `RttOf` stays 0. Offline server (`IsOffline = true`): no `S2C_RttProbe` is ever sent, and unsolicited echoes leave `RttSeconds` at 0.
7. Disconnect and reconnect resets the estimate and the outstanding probe. The first probe after the reconnect is fresh.
8. End-to-end: an online client with a simulated symmetric latency (for example 60 ms round trip) that answers probes promptly and presses at the centre of the great part gets **Great** for several consecutive checks. A client with the same latency and no probe answers gets Good, which documents the old bug.
9. Real TCP loopback (`TcpTransport`): a raw client that joins and echoes probes ends with `0 < RttSeconds < 0.05` after a few probes. Expose a read-only internal accessor for tests if needed.

Existing tests: now that probes reach online in-memory peers, some tests that read packets strictly may see an unexpected `S2C_RttProbe`. Fix this in the **shared test helpers** (skip probes), never by deleting or loosening assertions. List every test file you touched and why.

## Docs

- `docs/PROTOCOL.md`: bump to v0.5 and add both packets with a short "who times what" note.
- `docs/PLAN.md` §2: add both rows to the protocol table, or a short "v0.5 변경" bullet under the existing change notes, matching the existing style (PLAN is in Korean).

## Owned files

`Shared/Runtime/Packets/{PacketId,ServerPackets,ClientPackets}.cs`, `Shared/Runtime/Net/PacketCodec.cs`, `Server.Core/Runtime/{Player,GameServer,GameServer.Terminals}.cs`, new `Server.Core/Runtime/GameServer.Rtt.cs(.meta)`, `Client/Assets/Scripts/Net/NetClient.cs`, Client EditMode tests that pin the whitelist plus the new probe test, `Server.Tests/**`, `Tools/Harness/**` and `Tools/Harness.Tests/**` (only as needed above), `docs/PROTOCOL.md`, `docs/PLAN.md`. Anything else: explain in the report.

## Gates you run

- `dotnet build PingTheKiller.sln -warnaserror` (0 warnings, 0 errors)
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"` (all pass)
- `dotnet test Tools/Harness.Tests` (all pass)

The orchestrator runs the harness ×3, Unity EditMode and BuildWindows.

## Report (English, ≤ 200 words, final message)

Files changed; commands and results (pass/total counts); what is unverified; risks; any deviation from this design and why.
