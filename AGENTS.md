# AGENTS.md — Ping the Killer (shared rules for every agent)

Read by Codex (implementation), Antigravity (QA), and Claude (orchestration/review).
Agent-to-agent communication, briefs, reports, code comments and commit messages are in **English**. User-facing summaries are in Korean.

Full plan: `docs/PLAN.md`. UI rules: `docs/ui/SLOP_DEFINITION.md` (mandatory for any UI/HUD/menu/copy work).

## 1. Architecture absolute rules

- **Wire format:** every message is `[int32 Length, little-endian][ushort PacketId, little-endian][Payload]`. `Length` = byte count of `PacketId + Payload` (everything after the length field). Reject (disconnect) any `Length < 2` or `Length > 16384`.
- **No JSON, no third-party networking.** Raw `System.Net.Sockets` only. Serialization is hand-written `BinaryWriter`/span code in `Shared/`.
- **One shared source:** packet definitions, serializer, framing (`PacketAssembler`), constants and math live only in `Shared/Runtime/`. Server and Unity both compile these exact files. Never duplicate a packet struct elsewhere.
- `Shared/` targets `netstandard2.1`, C# 9, no `UnityEngine` references, no `System.Text.Json`, no LINQ in hot paths.
- **Server authority:** clients send only `MoveInput` (direction + look yaw + seq) and `UseSkill`. The server owns position, speed, visibility, capture, rescue, escape, skill timers and match result. Client-reported positions do not exist in the protocol.
- **Visibility masking is per recipient:** when building `WorldState` for the Killer, a Silent survivor's entry is **omitted entirely** (no coordinates sent, not just a flag). Survivors receive full state.
- **Input sanitizing:** direction vectors are normalized and clamped to length ≤ 1; NaN/Infinity → zero; server applies `speed × dt` itself. Inputs older than the last processed `Seq` are dropped.
- **Threading model:** one accept thread, async receive per session, a single game-loop thread at a fixed 20 Hz tick that drains a `ConcurrentQueue` of inputs. Game state is touched only by the game-loop thread. Each session has its own send queue.
- Unity main thread never blocks on sockets; network I/O runs off-thread and hands packets to the main thread via a queue.

## 2. Repository layout

```
Shared/            netstandard2.1 lib + local UPM package (package.json, Runtime/*.cs, asmdef)
Server/            .NET 10 console app: Listener, Session, GameLoop, Rules, Bots
Server.Tests/      xUnit: framing, serialization round-trip, masking, clamping, rules
Tools/Harness/     .NET console: headless QA client, fuzzer, scored harness (prints JSON score)
Client/            Unity 6000.3 LTS project; references Shared via Packages/manifest.json "file:../../Shared"
docs/              PLAN.md, ui/, qa/ reports, references/
```

## 3. Commands

```
dotnet build PingTheKiller.sln
dotnet test Server.Tests
dotnet run --project Server -- --port 7777 --bots 0 --bind 127.0.0.1 --sandbox   # H1-H5 server (fresh per harness run)
dotnet run --project Server -- --port 7778 --bots 0 --bind 127.0.0.1             # H6 server (fresh per harness run)
dotnet run --project Tools/Harness -- --host 127.0.0.1 --port 7777 --h6-port 7778 --threshold 100 --expect-los --report docs/qa/harness-latest.json
```
Unity batch tests: `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE path>.xml` (a relative path resolves against `-projectPath`)

## 4. Working rules for implementers

- Do exactly the brief's scope. Do not touch files outside the brief's owned-files list without saying why in the report.
- Every change to `Shared/` must keep `dotnet test` and Unity compilation green.
- Add or update tests for every rule or packet you change.
- No placeholder logic that "looks done" (fake scores, hard-coded results). If something is stubbed, say so in the report.
- Report format (English, ≤ 200 words): what changed (files), how verified (commands + results), what is unverified, known risks.

## 5. Working rules for QA

- QA never edits product code. It writes/runs tests under `Server.Tests/`, `Tools/Harness/`, `Client/Assets/Tests/`, and reports to `docs/qa/`.
- Every finding: severity (blocker/major/minor), reproduction steps, expected vs actual, evidence (log lines, test name, screenshot path).
- UI reviews score against `docs/ui/SLOP_DEFINITION.md` Section 9.
