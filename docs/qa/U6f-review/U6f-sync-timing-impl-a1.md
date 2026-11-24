# U6f-sync-timing implementation, attempt 1

Implementer: Claude Opus (quota switch: Codex out of credits). Branch `claude/sync-great-timing`.

**Diagnosis confirmed:** check stamped at tick end (+50 ms), press at the draining tick's start, so judged = needle − 0..50 ms; the autopilot also aimed 0.01 before the great part. Beyond the brief: `Run` passes each tick's lateness, so a late-waking loop (Windows timer, not measured) does not shift the judgement.

**Changed:** `GameServer.cs` (Stopwatch clock, `ServerOptions.ClockForTests`, `Input.ReceivedAt`, tick start/lateness, arrival passed to `SyncPress` only); `GameServer.Terminals.cs` (check stamped when it leaves, press timed by arrival clamped to 0.25 s, full RTT, Fail once a tick starts past sent + 1.25 s + RTT, bot press resolves on the first tick at/after it: seeded matches identical); `DevAutopilot.cs` (`GreatLead` removed, aims at the great middle); HUD_SPEC §13.2.

**Gates:** build 0 warnings; Server.Tests 982/982; Harness.Tests 635/635; harness 100/100 ×3; EditMode 1386/1386; BuildWindows success. Logs: `docs/qa/U6f-sync-timing-runs/`.

**Live (offline 1v7, 150 s):** great 7/7 Great, good 8/8 Good (before: 0/6 Great).

**Unverified / risks:** no server RTT probe, so online presses are still judged late by the real RTT. Clamp and late-tick mapping are unit-tested only. Human frame/input latency is not modelled.

## Changed test assertions

| Test | Old | New | Why |
|---|---|---|---|
| `RulesV2Tests.PressAt` (helper) | RTT = 2 × (arrival − target) | RTT = arrival − target | The judge subtracts the full RTT; the judged moment is still exactly the target. |
| `RulesV2Tests.AnUnansweredCheckFails…HalfTheRtt` → `…AndTheRtt` | n in 26..28 (1.35 s) | n in 28..30 (1.1 + 0.15 + 0.2 = 1.45 s = 29 ticks) | Limit uses the full RTT; same ±1 tolerance. |
| `RulesV2Wiring.AutoSyncPresses…` | Great due at 0.595 (lead 0.59) | not due at 0.6 / 0.617, due at 0.618 (great middle 0.6175) | Autopilot aims at the middle of the great part. |

New: `Server.Tests/Core/SyncTimingTests.cs` (27 tests: offset sweep 0/10/25/40/49 ms for Great and Good, late-tick lag, 8 edges × 3 send delays, full RTT, end-of-tolerance arrival, 0.25 s clamp, bot shares 30/162/22 Great/Good/Fail before and after).
