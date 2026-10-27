# U4b — orchestrator judgement (Claude Opus)

**Verdict: PASS on attempt 1.**

- Implementer: Claude Opus subagent. This was a quota switch from Codex and does not count as a failed attempt.
- QA: Claude Sonnet, a different model.

## Gate (orchestrator's own runs, Unity 6000.3.25f1 batch)
- Import: 0 `error CS`.
- EditMode tests: all pass. See the commit message for the count, which covers the implementer suite and the QA suite in `Client/Assets/Tests/EditMode/QA/`.
- `BuildScript.BuildWindows` builds `Client/Builds/Win64/PingTheKiller.exe`: Success.
- **No-socket proof, re-run by the orchestrator on the fresh build.** Command: `PingTheKiller.exe -ptk-offline -ptk-bots 3 -ptk-role survivor -ptk-start -ptk-hold-w 3 -ptk-quit-after 14`.
  - The server log line `[offline server] match start players=4` confirms the match was running.
  - `Get-NetTCPConnection -OwningProcess` and `Get-NetUDPEndpoint -OwningProcess` were sampled at about 5 s and about 9 s. Both returned **0 TCP and 0 UDP** endpoints.
  - The process exited cleanly.
  - The implementer's online control run did show the 7777 connection, so the method does detect sockets.
- Screenshots are in `docs/qa/U4b-screens/`: menu, offline lobby (1+7), offline survivor and killer play, three result previews (synthetic, labelled), and the online lobby against the real server. I checked them by eye: no wrap, overlap or clipping, and the HUDs are role-specific.

## Rulings during review
- **F-01 — QA brief error.** Shared protocol v0.2 rejects `SetBots > 7` at decode, so a raw 9 closes the session by design. The client clamps to 0..7. The test was re-scoped to that contract.
- **F-02 — fixed by the orchestrator (trivial).** `ClientApp.EndMatch` now releases Interact only through `InteractInput.TryRelease`. Before, it sent an unconditional `Interact 0`, including for the killer. A QA test category name with a hyphen, which NUnit rejects, was also corrected; no assertion changed.

## Pending until U2b merges
These need a live check against a U2b server:
- a real `S2C_GameResult` and the server returning to the lobby (the "Back to lobby" enable path)
- terminal `Progress`, `Lagging` status and timer, and the `Hacking` flag
- switching to the Shared `SkillIds` (the client name table carries a TODO)
- `SkillEffect` visuals (currently ignored safely)

## Follow-ups (not in U4b scope)
1. **U2c anonymous killer view.** Killer-bound survivor entries become `Id = 0`. Today `SnapshotBuffer` and `WorldView` key avatars by Id, so the killer path needs nearest-position matching with a speed gate, plus spawning and fading of unmatched blips. This needs its own unit and must land with or before U2c.
2. **Protocol v0.3 `S2C_SelfState`.** It will feed the skill cooldown ring and rescue progress; the readouts are isolated in `HudCopy`.
3. QA cosmetic items F-03 to F-07 in `docs/qa/U4b-qa-a1.md`.

## Needs a human
- E/Q feel, the F3 dev line, the released-cursor hint
- beep audibility
- gamepad navigation
- a full rules play-through after U2b

---

# Round 2 (after merging main 5f83456 / U2b) — orchestrator judgement

**Verdict: PASS.** Round 2 is a scope extension, not a retry. Implementer: Claude Opus subagent. QA: Claude Sonnet (`docs/qa/U4b-qa-a2.md`, 92/100, no blocker or major).

Scope:
- SkillIds constants
- lighting: ambient and fill light added by script; pure-black pixels went from 850 to 0 in the survivor view and from 10698 to 0 in the killer view; the background stays `#101113`
- lobby display order: host, then humans, then bots (a client-side sort; Server.Core order unchanged)
- real-rules verification mode (`-ptk-wander`, `-ptk-rematch`, event-triggered shots)
- QA cosmetic items

## Gate (orchestrator runs)
- 0 `error CS`
- EditMode tests: all pass (count in the commit message), including QA round 2. The superseded TODO test was replaced by ruling.
- `BuildWindows` succeeds.
- Socket proof, re-run on the fresh build: `-ptk-offline -ptk-bots 6 -ptk-wander` gave **0 TCP / 0 UDP** at about 6 s and 11 s while the in-process match ran (`status player=4 Lagging reason=capture`).

## Real-rules evidence (orchestrator read the player logs)
- **Offline survivor:**
  - local survivor captured at about 10 s;
  - HUD `Lagging · 60 s`, then 59, 58 … matches the server `StatusTimer`;
  - result `AllSurvivorsDown` → "Killer wins. No survivor is left in play.";
  - Back to lobby disabled until `LobbyState` arrives, then enabled;
  - second match: moved 11.62 m between seconds 2 and 5.
- **Offline killer:** terminals completed, then `exit open` at about 16–22 s; HUD and ring show `Exit · Open`; result `SurvivorEscaped`, "Bot 6 reached the Exit."
- **Online** (`--bind 127.0.0.1`, no firewall prompt): exit-open, escape, result, lobby return, and a rematch that moved 9.60 m.
- Hacking runs at 1 %/0.1 s solo and 2× when co-hacking, which matches U2b.

## Observations for the server/design owners (not U4b defects)
1. **Matches are very short with bots.** Survivor spawns cluster about 21 m from the killer spawn.
   - Killer bot vs 6 survivor bots: all Lagging, ending `AllSurvivorsDown`, in about 18 s. No survivor bot rescued anyone before the end.
   - Human killer standing still: 6 bots hack in parallel, so the exit opens in about 20 s.
   - This needs a GDD/U2b balance look (spawn spread, terminal distance, bot threat behaviour).
2. **Online run 3:** survivor bots stayed at 3/4 terminals for the full 600 s, ending `TimeUp`. This may be a U2b bot-AI stall.
3. **QA minors:**
   - `Terminals 5/4` is reachable, because more terminals are placed than required. The display could clamp, or say `4/4 · Exit open`.
   - A firewall-locked exit reads the same as a not-yet-open exit.

## Follow-ups
- U4c: camera redesign plus the U2c anonymous killer view. Camera-dependent code is isolated in `RoleCamera.Turn`, `DevCapture.Steer` with `Wander`, and `SceneLighting`.

## Needs a human
- audio
- E/Q feel
- gamepad navigation
- playing a full match by hand
