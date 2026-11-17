# INTEG-R impl report, attempt 1 (Claude Opus)

Branch `claude/integ-1117b`: R2 + R3 (b639213) + R1 092e73b (b7f041c), then 91d24a6 v0.8, 605252a balance, 24eb633 docs, 44966d8 picker wrap. Not pushed.

**Conflicts.**
- R3, 7 conflicts, both sides kept:
  - `PlayerTypes` keeps R2 `ReadRouteDirection` and clamps ActionKind to 7;
  - ClientApp/SurvivorHud keep the R3 intro and ally markers plus R2 `Route.Reset`.
- R1, 14 conflicts:
  - packet ids 0x17, 0x18, 0x19;
  - codec, dispatch, BotViews and ScenarioCheck carry both units;
  - `Swing()` replaces `Capture()`;
  - `LockTerminal` clears the locked terminal's route;
  - R1's PlayMenu/HelpCopy win (Packet trace, no Passive).
- Clamps: Flags 63, Chase 31, TerminalFlags 7, ActionKind 7.

**Cross-unit.** `IntegRCrossUnitTests` covers:
- E near a Lagging ally rescues: no hack, route quiet, keys dropped;
- getting up: no hack or route;
- a Firewall-locked terminal drops the route and ignores keys, with a fresh route after the lock;
- a swing cannot hit a getting-up Survivor.

R1 minors are folded in: M1 (`Predict` refuses while Stunned/Pulling/Vaulting/Breaking) and M4 (`HitTicksMax` 17, 18-tick test).

**v0.8.** `Protocol.Version = "0.8"`; there was no constant before. PROTOCOL.md keeps both units' edits and adds the 0x17–0x19 rows, retired 0x11–0x13, SelfState 29 B and the slot skill byte. `ProtocolV08Tests` holds the golden frames.

**Copy (SLOP §8).**
- `Rescue` is the only term ("Emergency restore" keeps its skill name).
- The intro card and How to play show left-click Attack, WASD routing, hold E to rescue, and the 1.5 s get-up.
- "touch" is gone; HUD_SPEC §3 is fixed.
- R3 minors: 3 (hint only when the slot is free), 4 (Esc/Enter/Space/click), 8 (no tag within 2 m), 9 (no per-frame allocation; test < 1 KB per 500 builds).
- The live run found "Signal for 2 / s;" in the Survivor picker. Fixed: a number keeps its unit.

**Balance** (Survivor win %, seeds 1–40 / 1001–1040; 240-seed mean in brackets):

| | 1v7 | 1v3 | 1v1 |
|---|---|---|---|
| Merged, untuned | 63/60 (64) | 63/65 (58) | 38/48 (38) |
| Final | 50/53 (50), median 602/574 s | 53/48 (50) | 33/43 (28) |

Changes:
- Killer bot swing trigger 2.2→2.75 m;
- mid-lobby miss chance 0.6→0.5;
- rescue quiet time 5→7 s;
- route keys 0.16–0.38 s;
- `SwingRange` 2.6→2.75 (+6 %).

Rejected: slower routing alone (1v7 median 682), Lagging 50 s (weaker Killer), swing 2.9/45° (1v7 40 %). Masking is unchanged; the GDD-v2 §10 table has the details.

**Gates (final tree).**
- build -warnaserror: 0 warnings, 0 errors.
- Server.Tests: 1147/1147. One run right after a Unity build failed 2 tests (names not captured); two reruns were clean.
- BalanceSweep: 18/18.
- Harness: 100/100 three times on 7793/7794; no listener left.
- Harness.Tests: 641/641.
- EditMode: 1603/1605, 2 skipped.
- BuildWindows: succeeded.

**Shots** (`docs/qa/INTEG-R/`, looked at):
- `survivor/`: intro card (play-1s), route, rescuing, getting-up, ally tag/edge, shutter prompt, How to play.
- `killer/`: lobby picker, intro card, swing hit and miss, Firewall lock, captures.
- `survivor-lobby/`: the fixed wrap.

**Unverified / risks.**
- `r3-rescue-prompt` and `chase-shutter-down` shots did not trigger; the prompt string is unit-tested.
- Balance is chaotic (±8 pts over 40 seeds), so the in-band result is partly seed luck; 240-seed means are given. Test bands are not tightened (1v7 is still 35–65).
- RTT/timing tests flake under load.
- The first pre-R1 harness run was spoiled by another agent and is marked INVALID; it was rerun.
- R1 M2/M3 are follow-ups.
