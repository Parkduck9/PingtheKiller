# U4 — orchestrator judgement (Claude Opus)

**Verdict: PASS on attempt 3 of 3.**

| Attempt | Implementer | Result | Why |
|---|---|---|---|
| a1 | Codex gpt-6.1-sol | FAIL | MoveInput sent every frame; U2a server drops > 60/s, so the release zero is lost at high fps (blocker). SLOP 84 (QA-01 USS specificity, spacing, killer Esc). |
| a2 | Claude Opus subagent (quota switch: Codex out of credits; not counted as a failure) | FAIL | Rate rule and USS fixed, but the real 1280×720 build showed wrapped and overlapping labels (`U4-screens/a2-menu-1280x720.png`). |
| a3 | Claude Opus subagent, different approach: screenshot-verified layout + dev capture mode (`-ptk-*` flags) | PASS | No wrap, overlap or clipping at 1280 or 1920. All spec bullets PASS (`U4-qa-a3.md`). SLOP 87/100, no gate failure. |

QA: Claude Sonnet (a1, a3). It was a different model from the a2/a3 implementer.

## Gate (run by the orchestrator, Unity 6000.3.25f1 batch)
- Import: 0 `error CS`.
- EditMode tests: 292 of 292 pass. That is the implementer's tests plus 201+ QA tests in `Client/Assets/Tests/EditMode/QA/`.
- `BuildScript.BuildWindows` builds `Client/Builds/Win64/PingTheKiller.exe`: Success (Mono).
- `Shared/`: only Unity-generated `.meta` files. Sources untouched.
- Screenshots come from the built player against the real U2a server (`aef3204`, run from an export outside the repo).

## Checked beyond the brief
- Main thread never blocks: connect, read and write all run on worker threads; `Dispose` only closes the socket.
- Socket threads are background threads and exit when the socket closes, so quitting does not leak them.
- Materials use URP/Lit (`WorldLit*.mat`, guid 933532a4…) and no Built-in shaders.
- The killer view only creates avatars for ids the server sent. The survivor view skips any killer entry (defence in depth).

## Known minor items (follow-ups, not blockers)
1. The connection error line appends the raw OS-localized exception text (Korean on this PC). Map socket errors to plain English (SLOP §4).
2. Several copy and HUD items need polish before Store screenshots:
   - `Offline mode arrives in U4b` names an internal unit.
   - Lobby rows read `Player · player`.
   - The survivor HUD carries the RTT/Esc debug line.
   - The `Exit · Locked` label overlaps the exit ring.
3. Panel scale is clamped to [0.8, 1], so on 1440p/4K the UI stays at 1080p pixel size.
4. The local survivor camera renders 100 ms in the past. Prediction is deferred: `TODO(U4b)`.
5. `S2C_GameResult` is not handled, so the client has no way back to the lobby after a match (U2b/U4b).
6. `MapSource` is a copy of U2a `FixedMap`: `TODO(U4b)` share it.
7. Seen from above, terminals (grey cylinders) look much like capsules.
8. The U2a server listens on all interfaces, which triggers a Windows Firewall prompt on first run. A `--bind 127.0.0.1` option would avoid it for local testing (U2a scope).

## Needs a human in the editor or game window
Beep audibility and timing, hover and pressed feel, keyboard Tab/arrow and gamepad traversal, mouse-look feel, and a live multi-client session.
