# M1-minors impl a1 (Sonnet 5.5)

**Fixes (client only, no protocol change)**
1. R1 M2: `KillerSwingWatch` already judged by flag length when the flag fell while pending; the gap was `LateHit` (Missed shown at 0.55 s, then a hit-length fall) only logging. `ClientApp.ObserveOwnSwing` now corrects it: kick (`PlayUnseenHit`), no impact (no position known), and a new `Hit` line (`KillerFxState.OnHit`, replaces `Missed`; also on the eliminating hit). Never shown without a hit-length flag. Tests: `M1MinorsTests` (late hit, miss/interrupt never hits, fall before window, Hit replaces Missed, source check).
2. R1 M3: no-vanish miss already reads at 0.55 s. A Silent blip near the Killer holds the verdict to the flag fall (20 ticks = 1.0 s). NOT made sooner: a hit's flag falls at 16 ticks, a miss's at 20, so earlier would risk a miss read for a hit. Pinned by test.
3. `IntroCardState.ConsumeBack` (Esc or gamepad Start) and `DismissInput(..., pad)` (gamepad A/B); `ClientApp` wired. Tests in `M1MinorsTests`.
4. `NearestNormalBlip` / `NearestSwingableBlip`: indexed `for`; source test.
5. HUD_SPEC 6.1f (a2 paragraph) and intro card section, marked `(11/18, M1)`.

**Gates**
- Build `-warnaserror`: 0 warnings, 0 errors.
- Server.Tests `Category!=BalanceSweep`: 1147/1147.
- Harness (7783/7784, fresh): 100/100, gates passed (`harness.json`).
- Unity EditMode: 1613 passed, 0 failed, 2 skipped (1615).
- BuildWindows OK. Live Killer, 1280x720, 7 bots, `-ptk-chase 230`, 240 s: 7 swings; server 2 hit, 5 miss; client showed 2 hits, 5 Missed. Match. Hit/miss shots checked. 0 exceptions.

**Unverified**: live run never reached an unseen-Survivor late hit or eliminating hit (unit tests only); gamepad Start not tried on hardware.

**Risks**: `Hit` line is new copy (only on non-confirm hits); `Missed` can flash 0.25 s before a late hit corrects it. The brief file is condensed, not verbatim.

Evidence: `docs/qa/M1-minors/`.
