# QA brief U4 — attempt 1: Unity client (independent tests + SLOP review)

QA: Claude Sonnet subagent (implementer was Codex gpt-6.1-sol — different family). Read `AGENTS.md` §5, `docs/briefs/U4-a1.md` (the spec under test), `docs/GDD.md` §3.2 (beep intervals), `docs/ui/SLOP_DEFINITION.md` §2–§6 and §9, and the implementation under `Client/Assets/Scripts/**` and `Client/Assets/Tests/EditMode/**`.

## You may create ONLY
- `Client/Assets/Tests/EditMode/QA/**` (tests + a `.asmdef` if needed; reference the same assemblies the existing EditMode test asmdef references)
- `docs/qa/U4-qa-a1.md` (report)

Never edit product code, existing tests, `Shared/`, `Server*/`, `Tools/`. If a production type is not testable (internal/private, MonoBehaviour-bound logic with no pure seam), do NOT change it — record it as a finding (severity major if it blocks a spec test) and test what is reachable.

## Independent EditMode tests (write from the SPEC, not from the implementation's own tests)
1. **Interpolation timing:** render time = now − 100 ms; with snapshots at t=0 (x=0) and t=50 ms (x=10), rendering at now=125 ms gives x=5 (±0.01); yaw interpolation takes the short way across 350°→10°; render time before the first snapshot clamps to the first; entity missing from the newest snapshot fades alpha 1→0 linearly over 150 ms (alpha≈0.5 at 75 ms, 0 at ≥150 ms) and is removed after; an entity that reappears during the fade comes back to full alpha.
2. **Input → world direction** at camera yaw 0/90/180/270 and 45: W at yaw 0 = (0,1) in (DirX, DirZ); W at yaw 90 = (1,0) (Unity Y-up, yaw clockwise from +Z); W+D normalized to length 1; no keys = (0,0).
3. **Zero-input send on release:** while held, one send per frame; on the first frame after release exactly one zero-direction send; no sends on further idle frames.
4. **Seq monotonic:** strictly increasing across held frames, the zero send, and a later press.
5. **IP:port validation edge cases:** valid `127.0.0.1:7777`, `192.168.0.10:1`, `10.0.0.1:65535`; invalid: empty, whitespace, missing port, `:7777`, port 0, 65536, negative, non-numeric port, `256.1.1.1:7777`, `1.2.3:7777`, trailing spaces handling (state which behaviour the impl chose and whether the user-facing message says what to fix, SLOP §4).
6. **Beep intervals per ThreatLevel:** 0 → none, 1 → 1.2 s, 2 → 0.6 s, 3 → 0.25 s; out-of-range values do not crash. Generated clip: 880 Hz, 60 ms (sample count = sampleRate × 0.06 ± 1; zero crossings consistent with 880 Hz).
7. **Packet queue drained only on main thread:** packets enqueued from a background thread are not dispatched until the main-thread drain call; the drain dispatches them in order; enqueueing from 4 threads concurrently loses nothing.

## SLOP review (read the UI code, layout values and every user-visible string)
Score against `SLOP_DEFINITION.md` §9 table (each row's points, gates marked). Check §2 V1–V12, §3 L1–L8, §4 copy (exact strings quoted), §5 states (normal/hover/pressed/disabled/focused, keyboard/gamepad nav), §6 (killer view must not leak hidden survivors; separate role HUDs). Note anything you cannot judge without seeing pixels as "needs human eye", do not guess.

## Run
Do NOT run Unity yourself (the orchestrator runs batch tests). Make sure your tests compile by reading the referenced APIs carefully. Never weaken an assertion to make it pass.

## Report `docs/qa/U4-qa-a1.md`
Per AGENTS.md §5: each finding with severity (blocker/major/minor), repro, expected vs actual, evidence (file:line / test name). Include the SLOP §9 score table. End with a single line `QA-PASS` or `QA-FAIL` (your judgement; the orchestrator will merge in the batch test results).
