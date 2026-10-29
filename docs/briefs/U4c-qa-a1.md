# QA brief U4c — attempt 1 (cameras, killer world layer and HUD, anonymous tracking)

QA: Claude Sonnet. Implementers were Claude Opus subagents (pieces A and B), so a different model.

## Read
- `AGENTS.md` §5
- `docs/ui/HUD_SPEC.md` (binding): §0, §3, §4.10, all of §6 (§6.0–§6.6, §6.5.1), §8, §11/§11.1
- `docs/ui/SLOP_DEFINITION.md` (§9 scoring)
- `docs/briefs/U4c-a1.md` (piece B spec) and `docs/briefs/U4c-a1-tracker.md` (piece A spec)
- `docs/briefs/U2c-a1.md` §1 (killer-bound entries: `Id = 0`, shuffled, Progress 0/100)
- `docs/qa/U4c-impl-a1.md`, `docs/qa/U4c-A-impl-a1.md`
- the changed client: `git diff 6495a7d -- Client/Assets/Scripts` plus the untracked files under `Client/Assets/Scripts/Killer/`, `Client/Assets/Scripts/World/`, `Client/Assets/Scripts/UI/` (`git status`)
- every `docs/qa/U4c-screens/*.png`, opened with the Read tool

## You may create or edit ONLY
- `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4c-qa-a1.md`

## Do not
- edit product code or the implementers' tests outside `QA/`
- run Unity, the game or the server (the orchestrator runs the Unity gate after you)
- touch `Client/Library`, `Client/Temp`, `Client/Builds`
- commit

Your tests must compile against the existing asmdef in `Client/Assets/Tests/EditMode/QA/`. Since you cannot run Unity, write carefully against the real public API (read the source), and list each new test name in your report so the orchestrator can match the XML.

## Rulings (orchestrator)
1. **Supersede `PanelScaleQaTests`.** HUD_SPEC §0.2 and Q15 now clamp the panel scale to **[0.8, 1.0]**. Replace `TheBoundsAreTheSpecValues` and the 2.0-capped cases of `TheScaleIsTheSmallerAxisRatioClampedBetweenPointEightAndTwo` with the 1.0 cap (e.g. 2560×1440 → 1.0, 3840×2160 → 1.0, 1280×720 → 0.8 floor check per the spec formula). Rename the test to match. This is a spec correction, not a weakening.
2. **Completed-terminal colour.** HUD_SPEC §0.4 says `ally` is "shown to Survivors only". `LightingQaTests.TerminalColoursAreMutedAndNeverAmberAndDoneIsTheTeamTealNotSignalAmber` binds done = teal for every role. Re-scope it: on the Survivor client done stays `ally` teal; on the Killer client a completed terminal must **not** use `ally` (and never `signal`). If the product API has no role parameter today, write the killer-side assertion anyway against the most natural seam you can find and report it as an expected failure (finding), do not skip it.

## Test work (write from the spec, not from the implementation)
1. **Anonymity (the key risk).** Feed the killer pipeline (`KillerScene` or whatever pure adapter B exposes) with WorldStates whose survivor entries are `Id = 0`, in shuffled order every tick, with `Yaw`, `StatusTimer`, `CaptureCount` and the Hacking/Rescuing/Stealthed/Afterimage flags set to **random garbage** each tick. Assert that rendering counts, classifications and blip positions are identical to a run with the same positions and those fields zeroed. Also: two runs with different survivor Ids for the same positions give identical results.
2. **Code audit (report, plus a test if feasible):** every killer-path file (`Killer/**`, killer branches of `WorldView`, `KillerHud`, `MarkerLayer`, `KillerWorldLayer`, `KillerAudio`) never reads Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount`, `Flags.Hacking/Rescuing/Stealthed/Afterimage`. The killer's own entry is found by `Role`. Cite file:line for every survivor-entry field access you find.
3. **Edge placement (§6.2 steps 1–6):** behind-camera mirroring, clamp to the inset rectangle, keep-out of the bottom-left cluster expanded by 8 u, nearest-6 limit with voiced-first selection.
4. **Classification hysteresis:** a blip flickering between in-view and occluded faster than 100 ms keeps its held class; a change lasting ≥ 100 ms switches.
5. **Rig math (§4.10, §6.0):** boom 4.0 m, look target +0.7 m, pitch clamp −10…+60 (default 20), vFOV 50; sphere-cast pull-in against an AABB wall shortens at once and eases out over 150 ms; dither threshold 1.0 m; facing turns at 720°/s and never turns while only orbiting; killer eye 1.7 m, pitch ±70, hFOV 90 at 16:9 → vFOV ≈ 58.7.
6. **Terminal markers:** Progress 100 = done = no marker; 0 and 99 = not done; never a percentage in any string; in view within 8 m → tag only.
7. **Killer HUD copy:** `m:ss`, last-60-s bar, `Terminals k of N`, `One more opens the Exit.` exactly at N − 1, effect lines (Overclock / Recovering / Slowed by traffic overload), passive and after-exit skill states, and that no skill cooldown ring is shown while `ISelfState.Available == false`.
8. **Execution guidance** (pure class): hidden when SelfState is unavailable; armed/re-arm per §6.4.
9. **DevArgs** new flags `-ptk-orbit`, `-ptk-killer-look nearest|away|<deg>`, `-ptk-menu`: valid, invalid, missing values; inert when absent.
10. **MovementInput:** survivor `Yaw` = character facing (not camera yaw); orbiting alone sends nothing; killer `Yaw` = view yaw.

## Review
- Each bullet of `U4c-a1.md` §1–§7 and of `U4c-a1-tracker.md` rules: PASS / FAIL / DEVIATION with evidence (file:line or screenshot).
- **Screenshots against HUD_SPEC and SLOP §9:** in particular
  - overlapping tags (the orchestrator sees `21 21 m m` in `killer-menu-1080.png` and `Terminal 4` sitting on a survivor in `killer-inview-*.png`);
  - whether the occluded and off-screen shots actually show a through-wall diamond and an edge chevron;
  - colour roles: `signal` only for Survivor information on the Killer client, no `ally` on the Killer client;
  - cluster geometry, type sizes (S floor 12 px at 720p), no wrap/overlap/clipping;
  - survivor third-person framing and the look-behind shot.
- Evaluate each deviation the impl report lists: acceptable or a finding.
- Anything wrong that the briefs did not mention.

Every finding: severity (blocker/major/minor), reproduction, expected vs actual, evidence.

## Report `docs/qa/U4c-qa-a1.md`
SLOP §9 score table for the killer HUD and the survivor view, findings list, new test names, audit table. The last line is `QA-PASS` or `QA-FAIL`.
