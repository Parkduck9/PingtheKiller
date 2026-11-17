# R1-killer QA a1 (Fable; implementer Opus)

**Verdict: FAIL.** Server sound; three major defects in the Killer client's swing feedback.

## Spec
- S1 swing authority, hit check, interrupts, masking, logs: PASS
- S1.1 proximity capture gone: PASS. `ContactSwingForTests` is `internal`, default off, set only by Server.Tests: unreachable in production.
- S2 Killer client: **FAIL** (F1–F3). Survivor sees the wind-up: PASS.
- S3 lobby choice, validation: PASS. HUD effect line: Killer only (reported deviation).
- S4 skills: PASS (trace and terminal lock seen live). Bots use views only: PASS.
- S5: PARTIAL. 1v7 is 48 % / 60 %; one seed set misses 45–55 %.

## Gates
- build `-warnaserror`: 0/0
- Server.Tests: 1142/1143, rerun 1143/1143 (flake `SegmentBlockedAllocatesNothing`, not RttProbe). BalanceSweep 18/18; the 1v1 band is 15–85 %.
- Harness 3× on 7791/7792: 100/100 each
- EditMode: 1562 passed, 0 failed, 2 skipped
- BuildWindows: Success. **The implementer's exe was stale** (built 15:49; sources changed 16:03), so their live evidence predates the final code.
- Live, 5 runs, 0 exceptions: `docs/qa/R1-qa-runs/`, `docs/qa/R1-qa-screens/`

## Findings
**F1 major — a miss blacks out the world** for 0.3 s when no capture has happened yet this session. `KillerAttackKick.Apply`: `startedAt = -inf` makes `Shake()` return NaN (`Mathf.Sin(inf)`), so the camera position is NaN. Repro: fresh exe, Killer, swing and miss. Evidence: `killer-trace/r1-killer-miss.png`, `killer-k0/r1-killer-miss.png` (HUD only).

**F2 major — a click gives no immediate feedback.** `SwingEvent.Started` only logs; nothing plays until the capture confirm, or the whoosh and `Missed` 0.55 s later (log t=84.27 → 84.82). This repeats "attacking doesn't work".

**F3 major (code-read, not reproduced) — an eliminating hit reads as `Missed`.** The third capture removes the entry; `BlipTracker` confirms only Lagging blips, so `KillerSwingWatch` times out.

**F4 minor — skill choices leak.** Every slot's `SkillChoice` goes to all clients. Blips are anonymous, so nothing maps to a player, but the Killer learns which Survivor skills are in the match. `C2S_ChooseSkill` is also unthrottled.

**F5 minor (SLOP ≈ 86, no gate failure).** Picker effect lines are small, muted and orphan words (`s.`, `you.`).

## Balance risks
- No reliable Killer buff in bot sweeps: 1v7 mean 58 → 54 %, 1v3 57.5 → 63 % (n=40, ≈ ±8 pp noise).
- Firewall terminal lock: 8 s per 12 s cooldown, map-wide, repeatable on the last terminal. The bot was capped because it ran the clock out.
- 1v1 at 25–33 % is already Killer-favoured.

## Fix list
1. Zero the shake and offset terms outside `[0, Seconds)`; test `PlayMiss` without `Play`.
2. On `Started`, play the whoosh and sweep at once; keep `Missed` as the result line.
3. Count a Normal blip despawning within `SwingCaptureRadius` during a pending swing as a hit; test it.
4. Mask `SkillChoice` per recipient; throttle `ChooseSkill`.
