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
