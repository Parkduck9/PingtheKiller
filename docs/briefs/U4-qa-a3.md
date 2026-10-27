# QA brief U4 — attempt 3 (code + screenshot review)

QA: Claude Sonnet. Implementer of attempts 2–3: Claude Opus subagent (quota switch from Codex) — different model.

Read `AGENTS.md` §5, `docs/briefs/U4-a1.md` (base spec), `docs/briefs/U4-a2.md` (changes: 30 Hz MoveInput rule etc.), `docs/briefs/U4-a3.md` (layout + dev capture), `docs/qa/U4-impl-a3.md`, your previous report `docs/qa/U4-qa-a1.md`, `docs/ui/SLOP_DEFINITION.md`, the code under `Client/Assets/Scripts/**`, and every PNG in `docs/qa/U4-screens/` (open each with the Read tool; `a2-menu-1280x720.png` is the old failure for comparison).

## You may create/edit ONLY
`Client/Assets/Tests/EditMode/QA/**` and `docs/qa/U4-qa-a3.md`. No product code. Do not run Unity (the orchestrator runs batch tests). Do not commit.

## Test work
1. **Supersede the two per-frame tests** in `InputQaTests` (`OneSendPerHeldFrameThenExactlyOneZeroSendThenSilence`, `SeqIsStrictlyIncreasingAcrossHeldFramesZeroSendAndALaterPress`). The spec changed by orchestrator ruling (U4-a2 item 1) — this is not weakening. Replace them with independent tests of the new rule written from the a2 brief: 1 s of hold at simulated 144 fps and 30 fps → 30 ± 1 sends; a 250 ms hitch produces no burst; release → zero send in the same frame + exactly one backup zero at the next 30 Hz slot, then silence; ≤ 31 sends in any sliding 1 s window under rapid tapping; Seq strictly increasing across hold → release → hold; a new match on the same connection does not restart Seq.
2. **DevArgs** (`Client/Assets/Scripts/Dev/DevArgs.cs`): no flags → inert; each flag valid/invalid/missing value; unknown `-ptk-*` flag; a following flag used as a value.
3. **Beep seams** (`ThreatAudio.BuildBeepSamples`, `NextBeepAfterChange`): sample count, 880 Hz zero crossings, rising/falling rule.
Keep all your attempt-1 tests that still apply. Make sure everything compiles against the current APIs.

## Review work
- Spec bullets of U4-a1 §1–§8 as amended by a2/a3: PASS/FAIL with file:line evidence.
- Screenshots: judge each PNG for wrap/overlap/clip, SLOP §2–§6, role-specific HUD, no information leak (killer must not see Silent survivors; survivor never sees the killer), readability at 1280×720 and 1920×1080. Note `a3-menu-error-1280.png`: the error line appends the OS-localized raw exception text — judge it against SLOP §4.
- Full SLOP §9 score table with gates. Mark items that need a human (audio, hover/focus feel, gamepad) as such; do not guess.

## Report `docs/qa/U4-qa-a3.md`
Findings with severity/repro/expected/actual/evidence; spec table; SLOP table; last line `QA-PASS` or `QA-FAIL`.
