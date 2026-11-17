# U9-skillfx QA report, attempt 1

- **QA:** Fable (review only, no product code edited). **Implementer:** Claude Opus.
- **Worktree:** `PtK-work/u9-skillfx`, branch `claude/u9-skillfx`, uncommitted on main 3675877.
- **Verdict: FAIL (narrow).** Gates, safety and performance pass. Six of the eight castable skills read at a glance. The Killer's Ping scanner world signature is effectively invisible (major), White noise is weak, and Firewall block is still unverified live. The fix list for attempt 2 is small.

## Gates (run by QA)

| Gate | Result | Evidence |
|---|---|---|
| Unity EditMode | **1498 / 1498 passed**, 0 failed, 0 skipped | `docs/qa/U9-runs/qa/editmode.xml` |
| BuildWindows | **Success**, exit 0 | `docs/qa/U9-runs/qa/build-windows.log` line 6783 |
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors | console |
| Worktree after runs | no new modified files; the same 29 `M` entries as before (the eol-only ones have an empty `git diff`), so nothing was reverted | `git status` |

Live runs: 7 clients in total (`docs/qa/U9-runs/qa/*.log`, shots under `docs/qa/U9-screens/qa/`). Five of them ran at the same time, then two ran alone.

## 1. Recognisability per skill (1280x720)

| Skill | World signature | Glyph | Own-cast flash | At a glance? | Evidence |
|---|---|---|---|---|---|
| K0 Ping scanner | Rings are 1–2 px white arcs on the ground. Facing a wall they are not seen at all; on an open street they look like the lane markings next to them | none (by design, first person) | shown, right of the Killer cluster | **No.** Only the HUD flash and the result line identify it | `U9-screens/killer-0/skillfx-killer-k0-caster-720p.png`, `U9-screens/qa/k0/skillfx-killer-k0-caster-720p.png` |
| K1 Overclock | Four or five white streaks, clearly visible | none (by design) | shown | Yes | `U9-screens/killer-1/skillfx-killer-k1-caster-720p.png` |
| K2 Packet afterimage | passive, unchanged | – | – | n/a | – |
| K3 Firewall block | **not captured live** | – | – | Unverified (EditMode only) | see section 5 |
| S0 Dummy packet | Dashed amber capsule around the character. Clear at the default boom. With the boom pulled in by a wall it fills the screen and the glyph is out of frame | dashed circle, readable | shown, left of the cluster | Yes (default camera) | `U9-screens/qa/s0-reduced/skillfx-survivor-s0-caster-720p.png`, `U9-screens/survivor-0/skillfx-survivor-s0-caster-720p.png` |
| S1 Stealth relay | From inside the 5 m dome only two teal meridians and a ground arc are in frame. It reads as "large teal arcs", not as a dome, but nothing else looks like it | readable | shown | Yes, mostly through the glyph | `U9-screens/survivor-1/skillfx-survivor-s1-caster-720p.png` |
| S2 Traffic overload | Amber jagged spikes from the chest, very clear | readable | not in the shot (taken about 0.6 s after the cast; the flash had ended) | Yes | `U9-screens/survivor-2/skillfx-survivor-s2-caster-720p.png` |
| S3 Emergency restore | Teal cage of columns with climbing rings on the restored Survivor, very clear (affected view) | readable | caster view not captured | Yes | `U9-screens/qa/s3-b/skillfx-survivor-s3-affected-720p.png` |
| S4 White noise | Only 3 thin square outlines are visible on the character. Weak, and it does not read as static | five dots, touching the top screen edge | shown | Marginal. The glyph and the flash carry it | `U9-screens/survivor-4/skillfx-survivor-s4-caster-720p.png` |

**Colours.** Keeping the four-colour palette is the right call (SLOP V7, palette max 4). Shape plus glyph is enough for S0 against S2 (both `signal`) and S1 against S3 (both `ally`): a capsule and spikes, or a dome and a beam, are not confusable. It is not enough where the shape itself is too thin to see (K0, S4).

## 2. Spec table

| Brief item | Result | Note |
|---|---|---|
| 1. One signature per skill, pairs differ in at least 2 attributes | **FAIL for K0, weak for S4**; the rest pass | The table is distinct in code and tests (9 shapes, 9 motions). The failure is on-screen visibility |
| 2. Glyph above the effect for about 1 s, same glyph on the HUD | PASS | `SkillGlyph` is the single source for the ring, the flash and the world texture. Framing issue: F3 |
| 3. Own-cast flash 0.6 s, feet clear, Reduced motion | PASS | Survivor flash at x 210–365, y 660–700; feet at about y 480 in the screen centre. With Reduced motion the offset is 0 (test). Killer flash sits right of the bottom-left cluster |
| 4. Safety | PASS | section 3 |
| 5. Prewarm, first-cast frame time | PASS | section 4 |
| 6. Assets: project art and generated geometry only | PASS | generated wire meshes, one new unlit alpha-clip material |
| Tests | PASS | 12 new tests. The U7b test change is as listed in the implementer report |
| HUD_SPEC updated | PASS | §14.6 added, `text` token note, §14.2 pointer |

## 3. Safety (code and tests)

- `SkillFeedback.ForKiller` returns `default` (Viewer None) for every Survivor skill except the positionless Traffic overload "slowed" record, which has no `Visual`. `SkillSignature.Plan` returns nothing for Viewer None and for `!Visual.Shown`. Test: `TheKillerIsNeverShownASurvivorSkill` (5 skills, 3 caster ids, the slowed record).
- `SkillFx.Play` places a signature only at `SkillFeedback.VisualPoint`, or at the own point for Overclock. A plan carries no position. Test: `SkillFxDrawsOnlyAtTheRecordsOwnPoint`. It includes the scanned Survivor being drawn at its own point and never at the effect's 0, 0.
- The glyph uses the same point as its rig. `IsOwnCast` is false for Killer-bound, ally and affected records, so no flash leaks.
- The viewer rules of §14.2 live in `SkillFeedback.cs`, which is not in the diff.
- Live: both Killer logs show only `survivor skill=2 viewer=Killer ... visual=None`, with no other Survivor record (`k3-a.log`, `k3-b.log`).

## 4. Performance

- Pool: `WarmBuildsEveryRigAndThePlaysInstantiateNothing` passes. No GameObject, mesh, material or texture is created across 3 rounds of every record. The per-frame code (`Animate`, `AnimateGlyph`, `DrawRing`) uses preallocated arrays; I found no allocation in it by reading.
- Warm-up: 21–39 ms in my 7 runs, with 92 objects, 10 meshes, 19 materials, 9 glyphs.
- First-cast frame, run alone: Streaks 6.1 ms (average before 6.1), WireDouble 6.1 ms (average before 6.6). With 5 clients at once: PulseRings 42.4, Static 34.2, WireDouble 21.2 ms. Those match the implementer's explanation that load from parallel clients causes the higher readings. The measure is one frame's `unscaledDeltaTime`; GC allocation was not profiled.

## 5. Firewall block and Emergency restore, live

- **S3 Emergency restore: captured** as affected and ally (bots cast it). The caster view was not captured: the scripted press tries only 6 times and never coincided with a Lagging teammate in range.
- **K3 Firewall block: not possible in the time box.** It needs the exits powered and a Survivor on a switch. With an idle Killer the bots reached Terminals 4/5 at 186 s and had not powered the exits by 225 s (`k3-b.log`). All 21 presses were rejected with `NoSwitchInProgress`. EditMode coverage only: shape, tint per viewer, static switch point. A capture needs a run of about 5–6 minutes, or a dev flag that presses when a switch is in progress.

## 6. Edits outside the owned files

- `ClientApp.PlayFx`: 2 lines, after `skillFx.Play`, guarded by `IsOwnCast`. Safe.
- `KillerHud` and `SurvivorHud`: a property, the element added to the skill block, `Hide()` on reset, `SetScale`. Minimal.
- `SkillRing`: the icon code moved to `SkillGlyph`, same primitives. The HUD icons are unchanged in the shots.
- `SkillFxArt.Glyph`: one field and one asset line.
- Leftover Hovl: `SkillFxArt.Prefabs` and `Marker` are still serialized, so the unused Hovl prefabs still ship in the build. Harmless functionally.

## Findings

**F1 (major): the Killer's Ping scanner rings are not readable.**
- Repro: `-ptk-role killer -ptk-want-skill 0 -ptk-skill-at 4`, with and without `-ptk-killer-look 180`.
- Expected: an expanding radar pulse seen at a glance.
- Actual: three flat lines 0.08 m wide at y 0.04 m, seen at a grazing angle from 1.6 m, so 1–2 px. They are depth-tested, so walls hide them, and on a street they look like lane markings.
- Evidence: the two K0 shots in the table.
- Fix proposal: make each ring a vertical band 0.3–0.4 m tall (a short cylinder strip, or `LineAlignment.View` with width 0.25 m) at 0.5–0.9 m height; let it run 1.0 s instead of 0.7 s; start at 2 m so the first ring is in frame at once. Still `text`, still at the own point only.

**F2 (minor): White noise static is too sparse.**
- Repro: `-ptk-role survivor -ptk-want-skill 4 -ptk-skill-at 3`.
- Expected: a cloud of flickering static.
- Actual: 16 outlines of 0.2 m at 35 % on, so about 5 are lit and 3 are visible.
- Evidence: `U9-screens/survivor-4/skillfx-survivor-s4-caster-720p.png`.
- Fix proposal: 28–32 squares, filled quads in mixed sizes 0.12–0.3 m, 50 % on, spread 1.5 m.

**F3 (minor): the world glyph sits at the top screen edge and leaves the frame.**
- Actual: at the default camera the glyph is at y 20–110 px of 720, and the White noise dots touch the edge. With the boom pulled in (S0 implementer shot) or a steeper pitch it is off screen.
- Fix proposal: lower `GlyphHeight` from 2.0 to about 1.9 m with `GlyphRise` 0.15 m, or clamp the glyph's screen position to stay 24 u inside the top edge.

**F4 (minor): Dummy packet double fills the view when the boom is short.**
- Evidence: `U9-screens/survivor-0/skillfx-survivor-s0-caster-720p.png`. At the default boom it is fine.
- Fix proposal: radius 0.35 → 0.5 m, line width 0.06 → 0.035 m, and hide the rig when the boom is under 1.0 m (the own-character dither rule of §4.10).

**F5 (minor): Stealth relay dome does not read as a dome from inside.**
- Fix proposal: 12 meridians instead of 8 and 3 parallels instead of 2, so several lines are always in frame.

**F6 (minor): evidence gaps.** K3 has no live capture at all, the S3 caster view is missing, and no shot contains the S2 flash. The contact sheet's S0 tile shows the worst camera case, and its label says "+ glyph" where no glyph is in frame.

**F7 (note): four simultaneous cues per own cast** (signature, glyph, flash, result line). SLOP §8 asks for one visual change per state change. The flash repeats the name of the skill block it touches. It was required by the brief; consider dropping the name and keeping only the glyph pulse if the HUD feels busy.

**F8 (note): stale log text.** The `[fx]` record log still prints `visual=Hovl/Zone`, which misleads now that Hovl is not instantiated.

## SLOP §9 score

| Check | Points |
|---|---:|
| No V1–V12 tells (flat unlit lines, palette colours only) | 20 / 20 |
| Layout, spacing scale | 15 / 15 |
| Copy and vocabulary (the flash reuses the block's name) | 15 / 15 |
| Every element answers "what state, what action" (F7) | 13 / 15 |
| Role-specific HUDs, no information leak | 20 / 20 |
| Interactive states (no new interactive element) | 10 / 10 |
| Contrast and readability at 1280x720 (F1, F2, F3) | 2 / 5 |
| **Total** | **95 / 100, no gate failure** |

The SLOP score passes. The FAIL verdict comes from brief spec item 1 and the user goal for K0, not from SLOP.

## Not verified

- K3 Firewall block live, in any view.
- S3 caster view; the S2 and S3 own-cast flash on screen.
- Reduced motion for anything other than S0 (still double, still flash position).
- 1080p, and aspect ratios other than 16:9.
- GC allocation per cast with a profiler.
