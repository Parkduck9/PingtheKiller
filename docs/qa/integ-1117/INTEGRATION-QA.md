# Integration QA, integ-1117 (Fable) — HEAD c8ea347

**Verdict: PASS with minors.** No blocker, no major. Paths below are under `docs/qa/integ-1117/`.

## Gates
- EditMode: 1552 total, **1550 passed, 0 failed, 2 explicit skipped** (`editmode-final.xml`). Same count as before: Q5 a2 added server tests only.
- BuildWindows: **Success** (`build-final.log:7021`).
- dotnet tests and harness: not run here (orchestrator).
- Live: 7 offline runs, 0 exceptions. Line-ending-only files reverted.

## Q5 re-QA
- **F1 PASS.** Catalog tests pass; props draw from the catalog.
- **F2 PASS, with M2.** All-bot chase (Survivor run, 310 s): 1 pull, 1 stun, 1 break, 6 rail vaults.
- **F3 PASS** (editor shots only): Broken reads wrecked, rails readable.
- **F4 PASS** (editor shot only): halo visible.
- **F5 PARTIAL**, see M1.

## Live evidence
- IP-audit: Sync lock bar, `screens/survivor/v2-sync-check.png`.
- U4l: no see-through gaps, `screens/killer/killer-occluded.png`.
- U8: `screens/survivor/u8-capture-1-burst.png`, `screens/killer/u8-killer-capture-1-after.png`; 12 + 7 impacts.
- U9: `screens/skill-survivor/skillfx-survivor-s1-caster-720p.png`, `screens/skill-killer/skillfx-killer-k0-caster-720p.png`.
- U10: holo panel in `v2-sync-check.png`; restore pose in `screens/survivor/skillfx-survivor-s3-affected-720p.png`.
- Q5: `screens/survivor/chase-prompt.png`; Killer `Stunned` line in `screens/killer2/chase-shutter-down.png`; force prompt logged (`live-killer2.log` t=128.78).

## Integration
- All four conflict resolutions are correct. The stun FOV `+=` is safe because the Overclock offset is reassigned every frame.
- Masking PASS: Killer-bound Survivor entries carry no `Chase`; the Killer client builds no actors; Killer logs show no vault thud.
- Colours: teal is team only, red is Killer only, amber is Signal only. No clash.
- No HUD overlap at 1280x720 between cast flash, effect line, prompts and Sync lock.
- Perf: solo Survivor mean 6.1 ms, equal to the U8-only build (6.3 ms). One own-capture window had 4 frames over 33 ms (max 38.2) with two clients running; no capture occurred in the solo run, so this is not re-checked.
- **SLOP §9: about 92/100, no gate failure.**
- Not seen live: stun halo, Killer force and vault blocks, own pull and vault lines, Firewall block, Reduced motion.

## Findings (all minor)
- **M1.** The chase event shots fire on map-wide state, not on what the camera sees. `screens/survivor/chase-shutter-down.png`, `-broken.png` and `-vault-seen.png` show only the own Survivor hacking. `chase-stun.png` never fired. The Killer shot is mid-drop (8 frames is 50 ms at 160 fps). Fix: require the prop near the own position and on screen; delay by time.
- **M2.** Against the scripted player Killer, bots rarely use props: 620 s, 7 captures, 2 pulls (one cut by a capture), 0 vaults (`live-killer.log`, `live-killer2.log`).
- **M3.** No precedence between the U10 interaction pose and the Q5 overlay. Rescue is proximity-only, so a Survivor pulling or vaulting beside a Lagging mate gets both on the same arms. From code only. Fix: `InteractionMap.Resolve` returns None while `Chase != 0`.
- **M4.** The Killer skill block reads `Ready` while Stunned, though the server refuses the skill (`screens/killer2/chase-shutter-down.png`).
- **M5.** The impact red (255, 74, 80) is not palette `danger` (229, 72, 77).
- **Note, not from these units:** terminals have no collision, so a character can stand inside one (`screens/survivor/exit-open.png`).
