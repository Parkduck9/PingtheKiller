# R2-hack QA, attempt 1 (Fable, no quota switch)

**Verdict: FAIL** — one major input bug (F1). The rest is sound; the fix is small.

## Spec bullets
- S1 routes, points, rate limit, stale ids, logs: PASS. 2,048 foreign or guessed keys change nothing.
- S1.7 masking: PASS. Only the hacker gets the route; the Killer run shows `Terminal 6 overloaded · 41 m`.
- S1.5 sync check removal: PASS (comments only).
- S1.4 co-hack: PASS as specified (F4). S1.6 bots: PASS.
- S2.1 input switch: **FAIL** (F1, F2).
- S2.2–S2.5 panel, prompts, holo, sounds: PASS (sounds not heard). SLOP §9: 90/100, no gate failure.
- S3 docs: PASS.

## Gates
- Build `-warnaserror`: 0 warnings. Server.Tests 1080/1080. BalanceSweep 18/18.
- Harness on 7787/7788: 100/100 three times.
- EditMode: 1546 passed, 2 skipped; my 3 finding tests fail as intended.
- Skipped: BuildWindows (exe newer than the sources), Harness.Tests.

## Findings
**F1, major — an abandoned route captures WASD on any later E hold.**
- Repro: hack, press one key, release E, walk away, hold E anywhere. Expected: WASD moves.
- Actual: SelfState keeps the route until that terminal is done. `RouteKeys.Capturing(E, false, route.Held)` is true, so movement sends zero, the panel shows, and keys the server drops are predicted (a false `the Killer was alerted`).
- R1: a terminal Locked mid-hack keeps the panel and the capture.
- Evidence: `R2HackQaTests.F1_*` (Server.Tests, EditMode); `R2-qa-runs/live-f1-stale-route.log`, route 2 held from t=44.37 to 146.70.

**F2, minor — prediction never rolls back.** A dropped key leaves the client ahead or locked out until the route id changes. Evidence: EditMode `F2_*`.

**F3, minor.** Stale comment at `ServerOptions.cs:21`; the RTT probe feeds nothing.

**F4, design note.** Bursts skip the co-hack falloff: two perfect hackers need 12.8 s (old rule 25 s).

## Feel and timing (`R2-qa-runs/qa-server-tests.log`)
- Solo: fast 30.8 s, slow 38.3 s, sloppy 40.7 s with 10 alerts, idle 89.9 s, scripted best 25.4 s.
- About 70 keys per minute, and errors cost about 10 s plus alerts. But the 3 s gap is 83 % of the cycle, so slow is not near 90 s.
- The panel is readable at 720p and clears the feet.

## Balance
- Confirmed: 1v7 is 50 % (was 63 %), and 38 % (was 53 %) on the QA seeds.
- It is a risk: two more losses in 40 break the 35 % floor. The bot error rate (8 %) is the first lever.

## Fix list
1. Server: send route fields only while `Hacking` at the route's terminal.
2. Client: `Capturing` = E held and own `Hacking`.
3. Client: when a SelfState arrives well after the last key, adopt its index and clear the lockout.
4. Make the F1 and F2 tests pass.

# Attempt 2 (Fable)

**Verdict: FAIL** — F1 and F2 are fixed, but the F1 fix opened a new major slide (N1).

## Fix list
- **F1:** PASS. The route is advertised and answerable only while its owner hacks that terminal. This covers release, walk-away, capture, a done terminal, another terminal and, by code, an R1 Locked gate. The test edit is legitimate. `a2-walkaway/walked-away.png`: panel hidden, moving.
- **F2:** PASS. The 0.3 s rule self-heals; above about 300 ms ping a cell can flicker back for one packet (minor).

## New finding
**N1, major — WASD falls through to movement in every gap between routes.**
- `Capturing` now needs a present route, so for 0.7 s or more after each completed route a key still down, or pressed early, moves the character.
- Repro: `-ptk-dev-hack 200 -ptk-hold-w 60 -ptk-auto-route fast`.
- Actual: after every route the character walks about 2.4 m off the terminal and the hack stops, 20 times in 25 s (`R2-qa-runs/a2/live-n1-w-held.log`). EditMode `N1_*` fails.
- Fix: capture on E held and own `Hacking`; only a present route answers.

**Minor.** The flash reads `+3.4 %` when a co-hacker's burst is smaller.

## Timing
Confirmed: fast 28.7 s, slow 44.0 s, sloppy 50.3 s, idle 89.9 s, two fast 15.8 s, bots 40.8 s. Typing speed now matters; slow is 1 s under the 45–50 s target.

## Balance
Confirmed: 1v7 48 % / 50 %; 1v3 65 % / 75 %, at the band's upper edge. Retune after the merge.

## Gates
Server.Tests 1093/1093; BalanceSweep 18/18; EditMode 1550 passed, 2 skipped, N1 failing; harness on 7789/7790 100/100 three times.

## Orchestrator fix after QA attempt 2 (N1)

The fix is one line, applied by the orchestrator (Opus) as the trivial review fix that CLAUDE.md allows:
- `RouteKeys.Capturing` is now `interactHeld && ownHacking`. A present route still gates which keys are answered (`RouteTracker.TryPress`). `RulesV2HudTests` now expects the gap between routes to keep movement off.

Verification:
- EditMode: 1551 passed, 0 failed, 2 skipped (QA's `N1_*` passes).
- BuildWindows: Success.
- Live run with `-ptk-dev-hack 200 -ptk-hold-w 60 -ptk-auto-route fast`: 23 routes completed with W held, and the character stayed at the terminal, about 0.5 m of drift from t=10.4 to 33.5 (`R2-hack/a3-n1.log`). Before the fix it walked off 20 times in 25 s.
