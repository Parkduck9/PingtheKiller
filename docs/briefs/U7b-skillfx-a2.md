# Brief U7b-skillfx: attempt 2 fix list

Attempt 1 failed QA. The report is `docs/qa/U7b-skillfx-qa-a1.md` (Gemini Flash). The orchestrator accepted every finding. The rules are the same as in attempt 1: same worktree, same owned files, no commit, no desktop input.

1. **(Major) F1: unaffected Stealth relay allies hear the cue at their own position.**
   - The cause:
     - `RelayResolved` leaves `Visual` unset;
     - `ClientApp.PlayFx` then anchors any positional sound with `!Visual.Shown` to the listener.
   - Fix it so that a positional sound always plays at the effect's world point, here the relay `X, Z`, whether or not a visual is shown.
   - Audit every record that has a positional sound for the same mistake.
   - Add tests that go through the same anchoring code path `ClientApp` uses (move the anchoring into a testable pure function if needed). They must show:
     - an ally 25 m away gets the relay point, not its own position;
     - beyond the 20 m range, the cue is muted.
2. **(Minor) F2: the 30 ms fade is missing.**
   - `TierCutFade` is defined but never used.
   - Implement the 30 ms fade-out when a skill-tier cue cuts the previous one. Do not add per-frame allocations.
   - Test it, or explain why that is impractical.
3. **(Minor) F3: the scanned-target shot at 720p.** Capture it if one or two more runs can stage it. Otherwise state that in one line.

## Acceptance

- EditMode is all green. `SurvivorKillerRenderingReverifyTests.SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract` is a known random-offline-match flake: if it fails, rerun it once and report both runs.
- BuildWindows succeeds.
- Keep the run XML and the build log in `docs/qa/u7b-runs/`, named `editmode-a2.xml` and `build-a2.log`.
- Append `## Attempt 2` (at most 150 words) to `docs/qa/U7b-skillfx-impl-a1.md`.
