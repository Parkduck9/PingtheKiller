# U10-motion — QA report, attempt 1 (Fable)

**Verdict: PASS** (minor findings only). Shots: `docs/qa/U10-qa-screens/`, logs: `docs/qa/U10-qa-runs/`.

## Gates
- EditMode re-run: 1501/1501 (`editmode-qa.xml`); with the QA tests 1502 passed, 2 explicit skipped.
- The build was newer than all sources, so no rebuild. `dotnet build -warnaserror`: 0/0.

## Spec bullets
- **Hack shows — PASS.** From the side it reads as typing on a hologram (`hack-orbit/survivor-behind.png`).
- **Mates' hack pose — PASS.** Live: a mate typing on its panel (`compare-before-after-behind.png`, right half). Real offline match, 50 s (`U10MateHackQaTests`): Hack shown in 2259 of 2376 flagged frames; the rest is blend-in plus 18 moving frames. The "arms down" mate is no bug: bots hack in 1.5–2 s bursts with 1–2 s pauses, and the flag is off in the pause.
- **Survivor run — PASS** (`hack/skillfx-survivor-s4-ally-720p.png`, `watch/threat-1.png`).
- **Masking — PASS.** The Killer client builds no Survivor character; `killer/*.png` show no prop. Jam reads only `Regressing`, which the server raises only on a completed kick.
- **Pooling — PASS.** Built once per character.
- **Swipe/kick untouched, no `ChaseActionPose` — PASS.**
- **Hooks — PASS.** `ClientApp` 1 line, `WorldView` 3; `CharacterViews` +120 (merge risk with U8 in `RenderKiller`).
- **Colours — PASS.** `ally`, `danger`; no rings.
- **Killer locomotion — not judged in motion.** Stills only; the planted-foot test passes.

## Findings (all minor)
1. **Own hack from the default camera.** The torso hides the hands and most of the panel; only its corners and the packets show (`compare-before-after-behind.png`). Fix: raise the panel to about 1.4 m or widen it to about 0.9 m.
2. **No restore pose while the rescuer moves.** Run `-ptk-dev-hack 600 -ptk-dev-switch`: a rescuer following the crawling own Survivor stands upright, no link (`switch/survivor-rescue.png`). Diagnostic: 70 of 1125 flagged frames. Fix: allow Restore up to about 1.5 m/s, or keep the link.
3. **Switch hold not seen settled.** Live frames are transitions only (`switch/v2-switch-hold.png`, `switch/exit-open.png`, which looks like a lunge). Measured settled pose is sane: 21° lean, palm at 1.15 m (`pose-qa.log`). The palm presses empty air; the switch is a floor ring.
4. **Jam has no shot,** though the brief asked for one; not covered live. Measured: hands rise to 1.73 m, land at 0.88 m, 0.43 m from the terminal centre. One slam, not repeated (masking).
5. **Dead code:** `InteractionMotion.JamWeight`, `JamTracker.Reset`; the Killer's props build an unused panel.
6. **The panel follows the character's facing,** not the terminal.

QA tests added: `Client/Assets/Tests/EditMode/QA/U10MateHackQaTests.cs` (explicit), `U10JamPoseQaTests.cs`.
