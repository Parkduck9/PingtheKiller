# Brief U4e — attempt 1: Survivor sees the Killer + survivor danger and capture feedback (client)

Status: **active** (2026-10-30, main after `0e39579`). Implementer: Claude Opus (quota fallback). QA: Claude Sonnet, including a screenshot review against `docs/ui/SLOP_DEFINITION.md` §9. Run the loop in your session. Commit on your own branch only after the orchestrator's PASS.

**Why — the user's playtest on main (2026-10-30), offline as a Survivor:** "적도 안보이고 능력은 써지는거같고 왜죽는지도 모르겠어". Three causes:
- The Killer is never drawn for survivors.
- The only threat cue is the beep. The §4.2 visuals were never built.
- There is no capture cue, and elimination never says why.

The user then decided that survivors see the Killer when their line of sight is clear (GDD §2, updated on main). The server side (U2e) and harness (U3d) run in parallel under the contract below.

**Owned files:**
- `Client/Assets/**`
- `docs/ui/HUD_SPEC.md`: amend only the sections you implement, and mark each amendment `(10/30)`.
- `docs/qa/U4e-*`
- `docs/briefs/U4e-qa-a1.md`

Do not touch `Shared/`, `Server.Core/` or `Tools/`.

## Contract (from `docs/briefs/U2e-a1.md`)
A survivor-bound `S2C_WorldState` contains the Killer entry (Role=Killer, real Id/X/Z/Yaw, Flags/StatusTimer/CaptureCount = 0) only while that survivor's line of sight to the Killer is clear. Spectators never get it. The wire layout is unchanged.

Until U2e merges, the server never sends the entry. Test it with fakes and the in-process offline server once U2e lands; DevCapture can inject a Killer entry for screenshots.

## Spec
1. **Killer model for survivors:**
   - While the entry is present, draw the Killer model (placeholder until the D3 assets), interpolated like other players and facing `Yaw`.
   - When the entry disappears, fade the model out over 0.3 s at its last position. Never extrapolate and never keep moving it.
   - No marker, outline or through-wall indicator for the Killer.
   - When the entry reappears after more than 0.3 s, snap: do not interpolate across the gap, because that would reveal the hidden path.
2. **Threat visuals, HUD_SPEC §4.2:** build it as written.
   - Meter-edge 1-bit `danger` noise strip at 1/3 width (level 1) and 2/3 width (level 2).
   - Screen-edge noise band at level 3.
   - The noise is static and re-rolled once per level change.
   - Add `threat_scratch` at level 2.
   - Hide it while Eliminated or Escaped.
   - The beep (`ThreatAudio`) stays as it is.
3. **Capture cue:**
   - On your own Normal → Lagging transition, play the `captured` sound (HUD_SPEC §4.1).
   - Run the Lagging screen stutter from the spec, with the `-ptk-reduce-stutter` alternative.
   - Show a one-time banner for about 3 s: `Caught. Get an ally within 1.5 m for 3 s — 60 s left.` (final copy may follow SLOP §6 vocabulary; record it in HUD_SPEC).
4. **Why you were eliminated:**
   - On your own Lagging → Eliminated transition, infer the reason. If the last Lagging `StatusTimer` was ≤ 0.2 s, it was a timeout; otherwise you were executed.
   - The meter reason becomes `Executed: the Killer touched you while Lagging.` or `Disconnected: no rescue within 60 s.`
   - The result screen's personal line uses the same reason.
   - A capture while already captured once (CaptureCount ≥ 1, Normal → Eliminated directly) reads `Executed: caught a second time.`
5. **HUD_SPEC:** update §2/§6.3, U2b rule 10 ("a Survivor client has no Killer position"), to the new contract, and add the banner and reason copy.

## Acceptance
- EditMode tests:
  - Killer model: present/absent/fade/snap, using fake snapshots.
  - Threat visual state per level, including the re-roll only on change.
  - Capture banner fires once per capture.
  - Elimination reason inference, all three cases.
  - No Killer rendering from any source other than the Killer entry (audit test).
- The Unity batch tests are green and the build compiles.
- Screenshots at 1280×720 and 1920×1080:
  - Killer visible in line of sight;
  - threat levels 1 and 3;
  - the capture banner;
  - the Lagging stutter frame;
  - the eliminated reason.

  QA scores them against SLOP §9 (≥ 85, no gate violations).

## Report
`docs/qa/U4e-impl-a1.md` (≤ 200 words), then QA `docs/qa/U4e-qa-a1.md` ending in `VERDICT: PASS` or `VERDICT: FAIL (...)`.
