# U4c — orchestrator judgement (Claude Opus)

**Verdict: PASS on attempt 2.**
- Implementers: Claude Opus subagents. Piece A is the tracker and sound model; piece B covers the cameras, the killer world layer, the HUD, the sounds and the SelfState seam. Using Claude was a quota switch from Codex and does not count as an attempt.
- QA: Claude Sonnet, a different model.

## Attempt 1 — FAIL
QA report `docs/qa/U4c-qa-a1.md`: 3 major and 3 minor findings.
- **F-01 (major), confirmed by the orchestrator in the screenshots:** tags overlap (`21 21 m m`).
- **F-02 (major):** a chevron's distance tag covers a through-wall diamond.
- **F-03 (major):** the Killer client paints a completed terminal and the open Exit ring in `ally` teal. HUD_SPEC §0.4 shows `ally` to Survivors only, and Q11 gives the Killer a `text` ring. The implementer listed this as a deviation; the orchestrator rejected it.
- **F-04, F-05, F-06 (minor):** terminal marks drawn over Survivors; dev-only reads of Survivor fields on the Killer client; a 100 ms gap with no blip drawn.

Rulings:
- `PanelScaleQaTests` is superseded. The panel scale is now clamped to [0.8, 1.0] (§0.2, Q15).
- The completed-terminal colour test is re-scoped: teal on the Survivor client only.

## Attempt 2 — PASS
- **Fixes:** a pure `TagLayout` (priority Survivor > Edge > Exit > Terminal, stack up to 3 steps, then hide); `text` colour on the Killer client; Killer dev logging uses blip counts only; out-of-frustum blips switch to a chevron on the same frame; distinct occluded and off-screen shots.
- **QA report** `docs/qa/U4c-qa-a2.md`: every finding FIXED. SLOP §9 scores: Killer HUD 92 (was 84), Survivor view 94.

## Gate (orchestrator runs, Unity 6000.3.25f1 batch)
- **Final EditMode: 986/986 pass.** This covers the implementer suites and QA attempts 1 and 2. 0 `error CS`.
- **`BuildWindows`:** Success. It was built on the final product code; QA changed only tests after that.
- **No-socket proof** on the U4c build: an offline 1v6 killer match ran with `-ptk-killer-look nearest`. The process had **0 TCP / 0 UDP** endpoints at about 6 s and about 11 s, and exited cleanly.
- **Live session against the main server:** the killer moved 12.77 m, blip counts were logged, 0 exceptions. Survivor live run from attempt 1: moved 9.60 m.
- **Screenshots** in `docs/qa/U4c-screens/`, 7 views × {720, 1080}. The orchestrator checked them by eye: no text collisions, no teal on the Killer client, and the occluded diamond and off-screen chevrons are separate cases.

## Spec bullets (U4c-a1 §1–§7)
1. **Survivor orbit rig:** PASS, with deviations.
   - The boom also stops at the floor; accepted.
   - The dither uses the main light plus SH; accepted.
   - Facing toward the terminal while hacking is visual only; accepted, because the server owns hacking.
2. **Killer first-person:** PASS.
3. **Killer world layer, anonymity:** PASS. QA ran garbage and shuffled Id, Yaw, timer and flag inputs and got the same output, and the code audit is clean. Classification, markers, edge placement and draw order are PASS. Terminal markers show no percentage and treat Progress = 100 as done.
4. **Killer HUD cluster, intro and menu:** PASS. Panel scale is clamped to [0.8, 1.0]. The font is Inter, because Pretendard is not in the project.
5. **Sounds:** implemented and unit-tested (piece A plus the `KillerAudio` wiring). **The audible result is not verified; a human must listen.**
6. **SelfState seam:** PASS. `Available = false`, with the marked adapter point.
7. **Dev flags:** PASS. New flags: `-ptk-orbit`, `-ptk-killer-look nearest|away|occluded|<deg>`, `-ptk-menu`.

## Pending until U2c merges (SelfState-fed)
- skill cooldown ring and the `skill_ready` cue
- execution-guidance prompt
- rescue progress and invulnerability readouts

The killer path already accepts `Id = 0` and shuffled entries. The terminal display already copes with 0/100 progress.

## Follow-ups (minor, not blocking)
- **F-07:** an edge tag can half-cover a terminal square, and stacked tags drift 40–70 u from their marker. Both stay readable.
- Tag widths are estimated from character count, not measured.
- The character is a capsule-plus-visor stand-in until a real model exists.

## Needs a human
- camera feel: orbit speed, the 150 ms ease and pitch limits
- mouse sensitivity
- audio: footstep and breathing mix, the low-pass when occluded, UI cue ducking
- the dither look near walls (not reached in the captures)
