# R2-hack implementation, attempt 1 (Claude Opus, no quota switch)

**Server.**
- A route of 4 directions comes from a private RNG stream and is appended to the hacker's own `S2C_SelfState`.
- The client sends `C2S_HackInput 0x0019`.
- Right key: advance. Full route: +8, then a 3 s gap while hacking. Wrong key: -3, a new route, Overloaded 1.5 s.
- Stale ids dropped; 12 inputs/s; logs capped at 2 lines/s per player.
- Passive progress is 100 per 90 s. Co-hack rates apply to the passive part only.
- Sync check removed entirely (0x11–0x13, late-tick judge, SyncLockBar, cues, `-ptk-auto-sync`). Protocol version not bumped.

**Client.**
- RoutePanel: 4 drawn cells, pulse, `+8 %`, danger edge and shake, the error line; Reduced motion respected.
- Keys are predicted locally and reconciled with SelfState.
- WASD, arrows, d-pad or stick flick; movement sends zero while routing.
- The prompt names WASD. The first route shows a hint.
- Tick, chord and glitch cues; the own holo panel pulses on real keys.
- New flag `-ptk-auto-route`.

**Numbers.**
- Bot solo terminal time over 24 runs: mean 42.4 s, median 41.7 s, range 36.8–50.9 s.
- Bots make 8.1 % errors.

**Balance, 1v7 Survivor win rate.**
- Before: 63 % and 53 %.
- After: 50 % and 38 %. Both are inside 35–65 %.
- `R2-hack/balance-*.txt`.

**Gates.**
- Build `-warnaserror`: 0 warnings.
- Server.Tests: 1080/1080. BalanceSweep: 18/18.
- Harness: 100/100 on runs 1, 2 and 4. Run 3 lost its server mid-H2, when another worktree started its servers (`harness-3-interrupted.log`).
- Harness.Tests: 641/641.
- EditMode: 1546 passed, 0 failed, 2 skipped (planned diagnostics).
- BuildWindows: Success.
- Live fast/slow/sloppy runs exit 0; sloppy: server 14 completed, 13 errors, matching the client.

**Shots.**
- `R2-hack/shots-fast/` and `R2-hack/shots-slow/`: `v2-route-mid.png`, `v2-route-done.png`, `holo-route-pulse.png`.
- `R2-hack/shots-sloppy/v2-route-error.png`.

**Also changed.** U2e `OpenExitWith` drains its peers (an unread killer peer passed the pending-packet cap).

**Unverified.** Gamepad on real hardware, and the sounds by ear. The gap bar is a local estimate.

**Risks.**
- 38 % is near the lower edge of the band, and R1 makes the Killer stronger.
- `docs/PROTOCOL.md` still describes the sync check, because it is not an owned file.

## Attempt 2

- **F1:** a stopped route stays on the server (QA's `ReleasingEKeepsTheSameRouteAndIndex`; the brief said to clear it). It is only sent and answerable while its owner hacks that terminal.
  - Client `Capturing` = E held, own `Hacking`, a route present.
  - Walkaway log: E off 12.58 s, `route id=0` 12.63 s, walked 22.6 m, panel hidden.
- **F2:** a same-route SelfState ≥ 0.3 s after the last key wins and undoes a predicted error or completion (the 1 Hz refresh guarantees one).
- **Design:** burst 3.4, penalty 2, gap 0.7 s, 1.3 s minimum route cycle; burst share (rate/n)² (F4).
  - Solo: fast 28.7 s, slow 44.0, sloppy 50.3, idle 89.9. Two fast hackers 15.8 s.
- **Bots:** 0.15–0.35 s per key, 4.5 % errors, solo 40.8 s.
  - 1v7: 48 % / 50 %; 1v3: 65 / 75 % (edge). Rows in `a2-balance.txt`.
- **F3:** comment fixed. The RTT probe feeds no rule now; it is kept.
- **QA test:** the F1 server test spawned inside a wall (`PlaceForTests` threw). Now the first standable spot 12 m away; same intent.
- **Dev flag:** `-ptk-walk-after-hack`.
- **Gates:** build clean; Server.Tests 1093/1093; BalanceSweep 18/18; EditMode 1550 passed, 2 skipped; BuildWindows OK; harness 100/100 ×3 (`a2-harness-*.json`).
