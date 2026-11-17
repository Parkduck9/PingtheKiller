# R3-help QA report, attempt 1 (Fable)

**Verdict: PASS.** No blocker or major. SLOP Section 9: about 92/100, no gate failure.

## Spec bullets
- S1 explicit rescue, no automatic rescue, E priority, bots: PASS.
- S2 prompts, ally tag, edge arrow, downed line: PASS (amber marker is a documented deviation).
- S3 intro card, How to play, hints: PASS.
- S5 get-up: PASS. `SetStatus(Normal)` covers both paths; 1.5 s inside the 2 s protection; move, E and skill refused. `GettingUp` is absent from the Killer view (`AddKillerEntry` writes `Signal` only). Masking unchanged.
- Clamps (`ActionKind` 7, flags 63): PASS.
- Banner 70 → 80: justified, the 77-character line fits (`capture-banner.png`). Min-of-5 GC: justified, a real allocation still fails it.
- `Animate` rate: PASS. Only the Survivor-view caller passes it, only for the GettingUp pose.

## Gates
- Build `-warnaserror`: 0/0.
- Server.Tests: 1131/1131; BalanceSweep 18/18.
- Harness 3× on 7787/7788, fresh servers: 100/100 each.
- Harness.Tests: 640/641 in the full run, made while my live runs loaded the machine (H2 p99 601 ms in `U3cFullRunUnknownFramesIdModeTests`); that test passes alone. Not a clean 641 run.
- EditMode: 1580 passed, 2 skipped, 0 failed.
- BuildWindows: not rerun; the exe is newer than every source file.

## Live (1280x720, `docs/qa/R3-qa-screens/`)
Every required shot reads clearly, including the get-up pose with `Getting up…`. Nothing covers the feet.
- Not exercised: movement keys during the get-up and the any-key dismiss (no input allowed). Server tests cover the first.
- The Killer hint has no shot; the log shows it at t=13.14.

## Balance
Confirmed: 1v7 50 % / 48 %, 1v3 58 % / 75 %. 1v3 on seeds 1001–1040 is 30/40, exactly the limit: one more Survivor win fails the gate, so R1/R2 bot changes can break it at integration.

## Findings (all minor)
1. Copy promises features absent here: `Left click Attack`, `WASD routes packets`, the Killer hint; the Survivor page says `The Killer's touch`. Align at integration.
2. Terms mix `restore`, `rescue` and `ally`; SLOP §8 fixes `Rescue` and `Survivor`.
3. `hints.Killer` runs before the `PromptFree` check (`ClientApp.cs` ~540), so a busy slot uses up the hint (jam prompt displaced it at t=14.80).
4. Any key, WASD included, dismisses the intro card.
5. E never vaults or pulls within 1.5 m of a Lagging ally (`GameServer.ChaseProps.cs:72`). Per brief.
6. HUD_SPEC §3 still describes a role line and `then reach the Exit.`
7. How to play hard-codes `1.5 s`; nothing says you cannot move.
8. The ally tag overlaps the own head at 1 m (`survivor-helper/r3-rescue-prompt.png`).
9. `AllyMarkers.Build` allocates every frame.
10. The selected How to play tab is weaker than the focus ring (`howto/howto-killer.png`).

Logs: `docs/qa/R3-qa-runs/`.
