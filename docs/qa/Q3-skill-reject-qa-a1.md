# Q3-skill-reject — QA report, attempt 1

- **Reviewer:** Fable (QA only; no product code touched). **Implementer:** Claude Opus.
- **Tree:** worktree `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject`, branch `claude/q3-skill-reject`, uncommitted on main 113b8e1.
- **Inputs read:** brief `docs/briefs/Q3-skill-reject-a1.md`, report `docs/qa/Q3-skill-reject-impl-a1.md`, AGENTS.md, SLOP §4/§8/§9, HUD_SPEC §4.8/§6.1c/§14.3, PROTOCOL.md, the full diff plus the three new files.
- **Logs:** `docs/qa/Q3-runs/qa/`. **Shots:** `docs/qa/Q3-screens/qa/{killer,survivor}/`.

## Verdict: PASS (0 blockers, 0 majors, 3 minors)

Every gate re-run by QA is green; every spec bullet is PASS or a documented, acceptable DEVIATION. The minors are copy vocabulary, a load-flaky pre-existing test, and a note on the live-run limit. None blocks the merge; minor 1 is a one-line copy change the orchestrator may want before commit.

## Gate counts (QA re-run)

| Gate | Implementer | QA re-run | Evidence |
|---|---|---|---|
| `dotnet build PingTheKiller.sln -warnaserror` | clean | clean, 0 warnings 0 errors | `qa/build.log` |
| `dotnet test Server.Tests --filter "Category!=BalanceSweep"` | 1087/1087 | **1086/1087** on the first run; the one failure is `RttProbeTests.RealTcpLoopbackEchoesProduceASmallPositiveServerTimedRtt` (loopback RTT 0.101 s > 0.05 s, a wall-clock assertion) with three Unity editors open. Re-run twice alone: 2/2 pass. Not Q3 code. | `qa/server-tests.log`, `qa/server-tests-rtt-rerun.log` |
| `dotnet test Server.Tests --filter "Category=BalanceSweep"` | 18/18 | 18/18 | `qa/server-tests-balancesweep.log` |
| Harness `--threshold 100 --expect-los`, 3 runs | 100/100 ×3 | 100/100 ×3, gatesPassed True (H1 25, H2 20, H3 20, H4 15, H5 10, H6 10) | `qa/harness-{1,2,3}.{out,json}` |
| `dotnet test Tools/Harness.Tests` | 641/641 | 641/641 passed, 0 failed (15 m 3 s; ran alongside the Unity EditMode run, the Killer live run and the BuildWindows gate) | `qa/harness-tests.log` |
| Unity EditMode (absolute `-testResults`) | 1486/1486 | 1486/1486 passed, 0 failed (12 `SkillRejectFeedbackTests` cases included) | `qa/unity-editmode.xml`, `qa/unity-editmode.log` |
| Unity `BuildScript.BuildWindows` | OK | `Build Finished, Result: Success.` | `qa/build-windows.log` |
| Live offline Killer / Survivor runs | clean | 0 exceptions in either log; rejections logged and routed (see Live) | `qa/live-killer.log`, `qa/live-survivor.log` |

Harness setup: `--bots auto` does not exist (`Server/Program.cs` usage: `--bots 0..8`). As the harness runbook (`Tools/Harness/README.md`, `Program.cs` usage text) requires, each run used two fresh instances: `Server --port 7777 --bots 0 --bind 127.0.0.1 --sandbox` (H1-H5) and `Server --port 7778 --bots 0 --bind 127.0.0.1` (H6), started with `Start-Process -PassThru` and stopped by PID after each run (`qa/server-pids-{1,2,3}.txt`; PIDs 48776/46172, 64812/48224, 16608/35500). Server stderr files are all empty.

Unity churn: after EditMode, `Client/Assets/Resources/DOTweenSettings.asset`; after BuildWindows, 18 art/settings assets. All were CRLF-only (`git diff --ignore-cr-at-eol --quiet` true) and were reverted. `git status` now shows only the implementer's files.

## Spec table

| # | Bullet | Result | Notes |
|---|---|---|---|
| S1.1 | `S2C_SkillRejected` 0x0016 `[byte SkillId][byte Reason]`; PacketId, ServerPackets, PacketCodec; wrong length / unknown reason → `PacketFormatException` | PASS | `Read` uses `ReadByte(Max=8)` then rejects 0; `Decode` calls `EnsureEnd()`. Golden bytes `04 00 00 00 16 00 03 06` and the 0..255 / lengths 0,1,3,4 sweep are in `SkillRejectProtocolTests`. |
| S1.2 | `enum SkillRejectReason : byte` 1..8 in `Shared/Runtime/Game/`, 0 invalid, listed reasons | PASS | Exactly the eight values, numbered 1-8. No extra value; I also found no uncovered refusal path (every `return false` in `UseKillerSkill`/`UseSurvivorSkill` sets a reason; the `default` branches map to Passive or NotYourSkill). |
| S1.3 | netstandard2.1, C# 9, no UnityEngine, no LINQ | PASS | Plain enum + static helper; Unity compiled it (EditMode, BuildWindows). |
| S1.4 | Update tests pinning "0x0016 unknown" / highest id | PASS | `ProtocolV02Tests` 21→22, Reverify S2C count 11→12. No other pin exists (grep for `0x0016` outside the new tests is empty). |
| S2.1 | Every refusal path sends exactly one rejection to the caster only, game-loop thread, session send queue | PASS | Lobby `UseSkill` → NotInMatch inside `Handle`, which runs only from `Tick` under `_tickGate` (the receive thread only enqueues). `RequestSkill` → Sandbox / NotYourSkill; `TryUseSkill` → NotInMatch / NotYourSkill / NotNormal / Cooldown / per-skill. Sent via the same `Send(binding.Connection, …)` as `SendEffect`. |
| S2.2 | Security: nothing beyond the caster's own stream; Firewall exit progress verified; Silent-independence test | PASS | `BroadcastWorld` calls `AddExitStates(world.Exits)` for every recipient before any role masking, so the Killer's WorldState carries every exit's `SwitchProgress`. `FirewallTarget()` refuses only when every unopened exit has `_switchSeconds ≤ TimeEpsilon`, i.e. every displayed percent is 0, so a rejection never contradicts the Killer's view. Ping scanner `NoTarget` only when no `IsActiveSurvivor` (Normal/Lagging), which ends the match the same tick (test asserts the GameResult follows). `KillerAnswersDoNotDependOnASilentSurvivorsPosition` covers Firewall, Ping scanner and Afterimage with the Survivor at three places including the exit-0 switch point. |
| S2.3 | Flooding: ≤ 1 rejection per player per tick; existing de-dup untouched; tested | PASS | `SkillAnsweredTick` gate; a use also marks the tick, so cooldown-refused duplicates of a used press send nothing. `_skillRequests` list unchanged (there was no de-dup before; all queued requests still execute). `FiftyPressesInOneTick…` covers ready, on-cooldown, mixed ids and 40-tick spam. See observation O1 for the one swallow case. |
| S2.4 | Cooldown edge with `TimeEpsilon` | PASS | `CooldownUpToTheLastTickBeforeReadyAndNoRejectionOnAUse` presses on the last refused tick and the first ready tick. |
| S2.5 | Bots never get a rejection, nothing breaks | PASS | `RejectSkill` returns on `IsBot` before anything; `BotsNeverReceiveOrLogRejectionsInABotMatch` runs a 7-bot match to its result. |
| S2.6 | One `WriteLog` per rejection, throttled if noisy | PASS (throttled, stated) | First line per player per second, later ones counted as ` unlogged=N` on the next line. Live logs show one line per scripted press (1 s apart). |
| S3.1 | NetClient + ClientApp decode and route on the main thread | PASS | `IsServerPacket` accepts 0x0016; `ClientApp` dispatch `case S2C_SkillRejected` → `OnSkillRejected` next to the effects, gated on `playing`. Live logs show `[fx] skill rejected id=3 reason=… (no line)` right after each `[offline server] skill rejected` line, so the full NetClient→ClientApp→SkillFeedback path runs with the real decoder. |
| S3.2 | Rejection replaces the inference; same line/styling; disarm watch; keep watch as fallback; late rejection after own effect ignored | PASS | `PressWatch.TakeRejection()` answers only an armed press; `OnRejected` returns null for another id or no pending press; `OnOwnEffect` disarms first so a later rejection is ignored (tests for both roles). The watch can still fire only when no effect and no rejection reach the client within 0.5 s (a reply slower than 0.5 s); every server path now answers, so it is a pure fallback. |
| S3.3 | Copy table; pre-check lines identical to server lines; no two texts for one case | DEVIATION (minor 1) | Mapping matches the brief's table exactly. `LocalPreChecksAndServerReasonsAgree` pins PressReason == RejectionLine for Restore, Firewall, Passive and Lagging; live logs show `(no line)` for every pre-checked press. The brief's own `recharging` wording conflicts with SLOP §4/§8 fixed term `Cooldown` (see minor 1); the brief said to adjust if a SLOP rule needs it, and the implementer did not. |
| S3.4 | HUD_SPEC §14.3 "Server reason" column; gap paragraph replaced; `(11/10, Q3)` marks; other gaps kept | PASS | Table has the column and new rows; "Server reasons" paragraph replaces "Protocol gap"; the three out-of-scope unknowns are kept. §4.8 gets a "Not used (server reason)" row. `PROTOCOL.md` bumped to v0.6 with a 0x0016 section consistent with the code (1-8, 0 and >8 rejected, one per tick, caster only). |
| S4.1 | Harness accepts the packet; unknown-packet reverify tests updated not deleted | PASS | `WireClientTests.SkillRejectionsAreKnownPacketsNotUnknownFrames`; the Reverify unknown-packet tests use fault-injected synthetic ids and needed no change (confirmed, none pinned 0x0016). |
| S4.2 | `KillerLeakScan` treats it as Killer-bound, structural check | PASS | `KillerLeakScan` case (no 4-byte field) + `Measurements.Masked` requires exactly header+id+2 bytes and byte-exact re-encoding; both tested with appended coordinate bytes. |
| S4.3 | Harness 100/100 with `--threshold 100 --expect-los` | PASS | 3/3 runs by QA. |
| T1 | Server.Tests: codec round-trip + malformed; one per reason via real `GameServer`; one rejection to caster only; no rejection on success; flood 50; Killer privacy; Sandbox | PASS | 15 methods / 19 cases in `Server.Tests/Core/SkillRejectTests.cs`, all present as listed. |
| T2 | EditMode: mapped line + disarm; effect after rejection and rejection after effect; copy pinned; no "error"/"fail" | PASS | 11 tests in `SkillRejectFeedbackTests.cs` (12 cases). The copy test also pins Killer lines ≤ 34 chars for the 6.1c block. |
| T3 | Existing tests green; only old-gap assertions changed, listed | PASS | Only the two count pins changed (listed in the report). The one Server.Tests failure in my run is the load-flaky RTT wall-clock test, pre-existing and unrelated. |

## Findings

### Minor 1 — copy: "recharging" is a second term for the fixed vocabulary word "Cooldown" (SLOP §4, §8)
- **Where:** `Client/Assets/Scripts/UI/SkillFeedback.cs` `SurvivorRecharging = "Skill still recharging."`, `KillerRecharging = "Not used · still recharging"`; HUD_SPEC §14.3 "Cooldown edge" row.
- **Expected:** SLOP §4 forbids "inconsistent terms for the same thing", §8 fixes `Cooldown`; the Killer block next to this line reads `Cooldown 7 s` and the Survivor ring is the cooldown ring. The brief proposed the text but asked for an adjustment if a SLOP rule needs it.
- **Actual:** the same concept is called "recharging" on the result line and "Cooldown" one row below it.
- **Suggestion:** `Skill still on cooldown.` / `Not used · still on cooldown` (27 chars, within the 34-char pin), update the two constants, the pinned test strings and the §14.3 row. Not a gate; deduction only.

### Minor 2 — pre-existing load-flaky test, not Q3
- `RttProbeTests.RealTcpLoopbackEchoesProduceASmallPositiveServerTimedRtt` asserts a real loopback RTT < 0.05 s; it measured 0.101 s once while three Unity editors were open, then passed 2/2 alone. Evidence `qa/server-tests.log` line 8-13, `qa/server-tests-rtt-rerun.log`. Untouched by this unit; recorded so the 1086/1087 first run is not read as a Q3 regression.

### Minor 3 — no live screenshot can show a server-reason line (as the implementer said)
- With the dev flags (`-ptk-want-skill 3 -ptk-skill-at 3`: reroll offline matches until the own skill is id 3, then press at match second 3 and once per second while no cooldown starts) the Killer's Firewall and the Survivor's Emergency restore are refused locally first (`watch=False`), so the server rejection is correctly swallowed as a duplicate: `play-4s.png` shows `No Exit switch in progress` (Killer, 6.1c muted) and `Emergency restore needs a Lagging Survivor next to you.` (Survivor, 14.1a) exactly once each, no second text. No existing dev flag can arm a press that the server then refuses (Ping scanner `NoTarget` ends the match; the cooldown edge needs a human press in the 50 ms window; the other skills succeed). The "line shown" branch is therefore covered only by EditMode (`SurvivorRejectionShowsTheReasonAtOnce…`, `KillerRejectionShowsTheMutedReason…`), while the decode → `ClientApp` → `OnRejected` routing is proven live by the `[fx] skill rejected id=3 reason=… (no line)` lines. I did not add a further EditMode test: `ClientApp` is a MonoBehaviour with no test seam, and the two halves of the path are each covered.

### Observations (no action needed)
- **O1** One-answer-per-tick can swallow a legitimate rejection only if the same client sends two *different* skill ids in one tick (an on-arrival `NotYourSkill`/`Sandbox` answer then suppresses the queued press's `Cooldown`/`NoTarget`); a use is never suppressed (`TryUseSkill` does not consult `SkillAnsweredTick`), and the real client only ever sends its own id. `ARejectionThenAUseInTheSameTickSendsBothInOrder` pins the opposite order.
- **O2** `_tick` is a `uint` that only ever increments (`++_tick`, also in the lobby) and is never reset at match start or end, so `Player.SkillAnsweredTick` (init -1, kept across matches) can never collide after a restart; wrap-around is 6.8 years at 20 Hz.
- **O3** `NotNormal` → `Skills are off while Lagging.` is chosen by the client's `localStatus` at packet time; when the status change and the press cross on the wire the line is skipped and the prompt row shows the steady Disabled reason a few ms later. Matches the §14.3 row "(not repeated while the prompt row shows it)".
- **O4** Unity's incremental player build reported Success but left `Client/Builds/Win64/PingTheKiller.exe` with the implementer's 15:50 timestamp; every client source edit is from 15:46, so the live runs used a build of the reviewed code.
- **O5** `TryUseSkill`'s `NotInMatch` branch is unreachable (`ExecuteSkills` runs only inside `RunRules`, which runs only when `_matchStarted`); harmless defence.

## Live evidence
- Killer: `qa/live-killer.log` lines 51-66: `[DEV] t=3.00 skill press 1`, `[fx] skill press sent=True watch=False reason="No Exit switch in progress"`, `[offline server] skill rejected player=4 id=3 reason=NoSwitchInProgress`, `[fx] skill rejected id=3 reason=NoSwitchInProgress (no line)`; repeated at t=4.03, 5.03, 6.03. 0 exceptions. Shot `qa/killer/play-4s.png`.
- Survivor: `qa/live-survivor.log` lines 65-79: the same pattern with `reason="Emergency restore needs a Lagging Survivor next to you."` and `reason=NoTarget`. The match rerolled twice (skill 0, 0) before landing on skill 3. 0 exceptions. Shot `qa/survivor/play-4s.png`.

## SLOP §9 score for the copy (three new lines on existing 14.1a / 6.1c lines)

| Check | Points | Score |
|---|---:|---:|
| No V1-V12 visual tells (no new visuals; muted reason styling reused) | 20 | 20 |
| No L1-L8 layout tells (existing line slots, 30-char Killer line fits 320 u) | 15 | 15 |
| Copy follows §4 and fixed vocabulary | 15 | 12 (minor 1: "recharging" vs `Cooldown`; otherwise short, concrete, no blame, no "error"/"fail", one dot separator as elsewhere) |
| Every element answers "what state, what action" (gate) | 15 | 15 |
| Role-specific HUDs, no forbidden leak (gate) | 20 | 20 (reasons derivable from the own stream; tested) |
| Interactive states / navigation (gate; nothing interactive added) | 10 | 10 |
| Contrast ≥ 4.5:1, readable at 1280×720 | 5 | 5 (muted line legible in both 720p shots) |
| **Total** | **100** | **97, no gate failures** |

## Orchestrator judgement (Claude Opus, 2026-11-10)

**PASS.** Every S1-S4 bullet is met. I checked the server diff myself: one answer per player per tick, rejections go to the caster only, bots return first, and the privacy reasoning holds.

- **Minor 1 (copy):** fixed in review. `SkillFeedback.SurvivorOnCooldown` / `KillerOnCooldown` now read `Skill still on cooldown.` / `Not used · still on cooldown`. The test pins and the HUD_SPEC §14.3 row were updated to match. Unity EditMode re-run after the change: 1486/1486 (`docs/qa/Q3-runs/review/`).
- **Minor 2:** the RTT loopback test is load-flaky. It is pre-existing and not caused by Q3. Recorded as a follow-up candidate.
- **Minor 3:** the server-reason line has no live screenshot. This is an accepted coverage limit: EditMode covers the line, and the live logs prove the routing.
- **AGENTS.md:** it lists `--bots auto`, which does not exist. The harness runbook (a sandbox server on 7777 plus an H6 server on 7778) is the real procedure. Fix that doc line when Q3 merges.
