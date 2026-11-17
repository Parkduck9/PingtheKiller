# INTEG-R impl report, attempt 1 (Claude Opus)

Branch `claude/integ-1117b`, not pushed:
- merges: R3 b639213, R1 b7f041c;
- then v0.8 91d24a6, balance 605252a, docs 24eb633, picker 44966d8.

**Conflicts.**
- R3 (7): both sides kept. ActionKind clamps to 7; R2 route reset and R3 intro/markers sit together.
- R1 (14):
  - ids 0x17, 0x18, 0x19;
  - codec, dispatch, bots and ScenarioCheck carry both units;
  - `Swing()` replaces `Capture()`;
  - `LockTerminal` clears the route;
  - R1 PlayMenu/HelpCopy win.
- Clamps: Flags 63, Chase 31, Terminal 7.

**Cross-unit.** `IntegRCrossUnitTests` covers:
- E near a Lagging ally rescues (no hack, route quiet, keys dropped);
- getting up blocks hacking and routing;
- a Firewall lock drops the route and its keys;
- a swing misses a getting-up Survivor.

R1 minors are in: M1 (no predicted swing while Stunned/Pulling/Vaulting/Breaking) and M4 (17 ticks).

**v0.8.** `Protocol.Version = "0.8"` (new; there was no constant before). PROTOCOL.md: both units' edits plus the bump. `ProtocolV08Tests` has the golden frames.

**Copy.**
- One term, `Rescue`.
- Intro and How to play show left-click, WASD routing, hold-E rescue and the 1.5 s get-up.
- "touch" removed; HUD_SPEC §3 fixed.
- R3 minors 3, 4, 8 and 9 done.
- The live run found "Signal for 2 / s;" in the Survivor picker. Fixed: a number keeps its unit.

**Balance** (Survivor win %, seeds 1–40 / 1001–1040; 240-seed mean in brackets):

| | 1v7 | 1v3 | 1v1 |
|---|---|---|---|
| Merged | 63/60 (64) | 63/65 (58) | 38/48 (38) |
| Final | 50/53 (50) | 53/48 (50) | 33/43 (28) |

1v7 medians: 602/574 s.

Changes:
- bot swing trigger 2.2→2.75 m;
- mid-lobby miss chance 0.6→0.5;
- rescue quiet time 5→7 s;
- route keys 0.16–0.38 s;
- `SwingRange` 2.6→2.75 (+6 %).

Rejected: slower routing (1v7 median 682), Lagging 50 s, swing 2.9/45° (1v7 40 %). Masking is unchanged. GDD-v2 §10 has the table.

**Gates (final tree).**
- build: 0 warnings, 0 errors.
- Server.Tests: 1147/1147 (one run right after a Unity build failed 2 tests, names not captured; two reruns clean).
- BalanceSweep: 18/18.
- Harness: 100/100 three times (7793/7794).
- Harness.Tests: 641/641.
- EditMode: 1603 passed, 2 skipped.
- BuildWindows: OK.

**Shots** (`docs/qa/INTEG-R/`, looked at):
- Survivor: intro, route, rescuing, getting-up, ally tag/edge, shutter prompt, How to play.
- Killer: picker, intro, swing hit and miss, Firewall lock, captures.
- `survivor-lobby/`: the wrap fix.

**Risks / unverified.**
- `r3-rescue-prompt` and `chase-shutter-down` did not trigger (the prompt is unit-tested).
- The balance is noisy (±8 pts over 40 seeds); 240-seed means are given. Test bands are not tightened.
- Timing tests flake under load.
- The first pre-R1 harness run was spoiled by another agent and is marked INVALID; it was rerun.
- R1 M2/M3 are follow-ups.
