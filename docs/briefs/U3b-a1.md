# Brief U3b — attempt 1: harness H6 scenario check + flaky self-test fix

Implementer: Claude Opus subagent (quota fallback — Codex is out of credits; not a failed attempt). QA: Claude Sonnet (different model). Work in this worktree on branch `claude/u3b-h6`. Do not commit.

Read first: `AGENTS.md`, `docs/PLAN.md` §5, `docs/GDD.md` §3, `Tools/Harness/README.md` and all of `Tools/Harness/**`, `Tools/Harness.Tests/**` (incl. `QA/`), `Shared/Runtime/**`, `Server.Core/Runtime/Map/FixedMap.cs`. **Server contract for the game rules:** `C:\Unity\MultiPlayGame\PingtheKiller\docs\briefs\U2b-a1.md` (U2b is being implemented in parallel and is NOT in this worktree; treat that file as the spec of the server you will eventually talk to).

## Owned files
`Tools/Harness/**`, `Tools/Harness.Tests/**` (you may edit existing non-QA tests and, only for the flaky fix below, existing `QA/` tests — never weaken what they assert), `docs/qa/U3b-impl-a1.md`. Do not touch `Shared/`, `Server.Core/`, `Server/`, `Server.Tests/`, `Client/`, `PingTheKiller.sln`, `docs/PLAN.md`.

## Part A — H6 scenario (10 points, not a gate)
Replace the H6 placeholder with a real check. Everything over TCP, as real clients, like H1–H5.

**Server.Core reference rule:** `Harness.csproj` may add a `ProjectReference` to `Server.Core/Server.Core.csproj` **only** to read static geometry from `PingTheKiller.ServerCore.Map` (`FixedMap` walls, terminal positions, exit, `PlayerRadius`, `CanStand`). The harness must never use `GameServer`, transports, `ServerOptions`, `Player` or any other Server.Core type. Add a self-test that enforces this (e.g. scan `Tools/Harness/**/*.cs` for `ServerCore` usages and allow only the `.Map` namespace, plus a reflection check that no Harness type references a Server.Core type outside `PingTheKiller.ServerCore.Map`). Say this in the README.

**Server instance:** H6 needs a fresh lobby (`--bots 0`, no `--autostart`) of its own, because H2–H5 occupy their server's match. Add `--h6-port <port>` (and `--h6-host`, default = `--host`). Rules: `--only H6` without `--h6-port` uses `--port`. If H6 is selected together with any of H2–H5 and `--h6-port` is missing or equals `--port` on the same host, H6 fails immediately with evidence `"H6 needs its own fresh server: pass --h6-port"` (no network I/O). Document the two-server full run in the README and in `--help`.

**Setup:** 3 humans. Host joins first, `SetBots(0)`, `SetRole(Killer)`, survivors A and B join, `StartMatch`. Assert MatchStart roles (host Killer, A and B Survivor). With 2 survivors the server requires 2 of 4 terminals (`GameConstants.TerminalsFor`).

**Navigation:** a small planner over `FixedMap` (e.g. grid A* at ≤ 0.5 m cells, blocked where `CanStand` with a safety margin fails), and a waypoint follower that drives each client with `C2S_MoveInput` at ~20 Hz from the client's **own observed position** in its WorldState (killer sees itself; survivors see themselves). Stop (zero input) on arrival. Every leg has a timeout derived from distance / speed + slack; a stuck mover fails the check with evidence (leg, position, target).

**Scenario (in this order, each step with positive evidence and its own deadline):**
1. **Hack:** A and B co-hack terminals (walk within 1.0 m of a terminal centre, send `C2S_Interact{Active=1}`, release when that terminal reaches `Progress == 100`) until `TerminalsRequired` terminals are complete. Evidence: progress values observed, then `ExitOpen == 1` seen by all three clients.
2. **Capture:** B moves ≥ 8 m away from A and stays still. The killer walks to A (A stands still) until A's status is `Lagging` with `CaptureCount == 1` and `StatusTimer > 0` (in survivor view). The killer must not touch B.
3. **Retreat:** immediately after the capture the killer walks to a point ≥ 8 m from A (and from B's rescue path) and stays still with zero input — a killer touching a Lagging survivor executes it.
4. **Rescue:** B walks to within 1.0 m of A and stays until A is `Normal` with `CaptureCount == 1`.
5. **Escape:** one survivor (choose and record it) walks to within 1.0 m of the exit; the other stands still away from the exit.
6. **GameResult:** every one of the 3 humans receives exactly one `S2C_GameResult` (drain ~1 s after the first to catch duplicates) with `WinnerRole == Survivor`, `Reason == SurvivorEscaped`, `EscapedIds` exactly `{escaper}`. All three copies identical. Evidence only (not required): whether a `LobbyState` follows.

**Killer-coordinate leak scan (whole scenario, from MatchStart to the end of the GameResult drain):** collect every killer X and Z observed in killer-bound WorldStates. For every survivor-bound raw frame, for each collected value, count the occurrences of its 4 little-endian bytes in the raw frame (any alignment), and count the decoded fields of that frame that legitimately hold exactly that float (survivor `PlayerState` X/Z/Yaw/StatusTimer, `TerminalState` X/Z, `TimeLeft`, `SkillEffect` X/Z when it is a survivor-skill effect). **Fail if occurrences > legitimate count.** This tolerates exact coincidences with legitimate fields (e.g. spawn coordinates shared by killer and survivors) but catches any extra copy. Also fail if any survivor-bound WorldState has a Killer-role entry or the killer's Id.

**Budget:** H6 has a whole-check deadline (e.g. 150 s) on top of per-step deadlines; it must never hang.

## Part A tests (`Tools/Harness.Tests/`, not `QA/`)
A test-only scripted rules fake (TCP, 3 clients, `FixedMap` movement at server speeds, hacking/capture/rescue/escape per the U2b contract, per-recipient masking, GameResult) that H6 passes against. Then broken variants, each must make H6 fail (score 0) with matching evidence:
- no GameResult ever sent;
- wrong `EscapedIds` (empty, or both survivors, or the non-escaper);
- wrong winner / reason;
- killer X/Z bytes leaked to survivors (at least two ways: an extra float pair inside a survivor-bound `SkillEffect`, and a killer `PlayerState` entry in a survivor WorldState);
- exit never opens; capture never happens (survivor stays Normal); rescue never completes.
Plus: the `--h6-port` rules above; the Server.Core reference guard; `--only H6` against a silent server fails within bounded time. Update scorer/JSON tests: H6 is now a real check (no "skipped"); all six passing = 100. Keep default `--threshold 90`.

## Part B — flaky self-test
`dotnet test PingTheKiller.sln` (Server.Tests and Harness.Tests in parallel) once failed 1 of 150 Harness.Tests; standalone reruns were green. Find the timing-sensitive test(s): stress-run Harness.Tests under CPU load (e.g. run it concurrently with `dotnet test Server.Tests` several times, or with xUnit parallelism maxed) and audit every wall-clock assertion (Stopwatch bounds, `< N s` checks, p99 thresholds, flood timing, receive-window counts). Make each robust **without weakening what it checks**: e.g. derive bounds from configured timeouts rather than tight constants, measure the harness's own deadline behaviour rather than scheduler speed, or move a scheduling-dependent assertion to a deterministic seam. Report which test failed, the root cause, and the evidence (stress runs before/after).

## Contract update (from U2b attempt 2) — binding
1. The real server gains `--sandbox` (U2a rules only: no hacking/capture/skills/decoys/afterimage/win-lose; TimeUp at 600 s still applies) and `--bind <ip>` (invalid IP → exit code 2). **H1–H5 run against `--port <p> --bots 0 --bind 127.0.0.1 --sandbox`.** Update `Tools/Harness/README.md`, `--help` and the startup hint text accordingly.
2. **H6 runs against a normal (non-sandbox) server**: `--port <p6> --bots 0 --bind 127.0.0.1`. In that mode decoys may borrow real survivor Ids in the killer view and Packet Afterimage keeps a survivor visible to the killer for 1 s after Silent. H6 must **not** assert anything about survivor Ids being absent from the killer view; its leak check is only "killer X/Z bytes never appear in survivor-bound frames (beyond legitimate coincidences)" plus "no killer entry in survivor WorldStates". The harness never uses skills.
3. Document the full run as two server instances (sandbox for H1–H5, normal for H6).

## Commands (must pass)
`dotnet build Tools/Harness/Harness.csproj -warnaserror`, `dotnet test Tools/Harness.Tests/Harness.Tests.csproj`, and `dotnet test PingTheKiller.sln` 3 times in a row all green (report counts).

## Report
`docs/qa/U3b-impl-a1.md` (English, ≤ 250 words): header noting the quota fallback (implementer Claude Opus); files changed; how to run H6 (server flags, `--h6-port`); test counts; the flaky test root cause; judgement calls; what remains unverified until U2b merges.
