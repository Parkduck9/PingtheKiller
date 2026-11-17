# U9-skillfx implementation report, attempt 1 (Claude Opus)

**Signatures** (`World/SkillSignature.cs`; 9 unique shapes and 9 unique motions; colour keeps its §0.4 meaning, SLOP V7, so no cyan/blue/green):
Ping: pulse rings (out for the Killer, closing in for the scanned Survivor). Overclock: speed streaks past the camera. Firewall: hex wall that rises and blinks once. Dummy: dashed wire double that pops in. Relay: wire dome. Overload: jagged spikes. Restore: beam with climbing rings. White noise: flickering static (`text`, not `signal`). Afterimage: unchanged.
Geometry is generated wire meshes (`SkillShapes.cs`). Hovl is no longer instantiated.

**Glyphs.** `UI/SkillGlyph.cs` holds the §8 icons as data. The skill ring paints them, the new 0.6 s own-cast flash paints them (`UI/SkillCastFlash.cs`: Survivor left of the cluster, Killer right of its cluster), and warm-up rasterizes them into world glyphs (alpha-clip material).

**Safety.** A plan exists only for a record that already has a §14.2 visual. It is drawn only at `VisualPoint` (Overclock: the own camera). The Killer gets nothing for Survivor skills or the slowed record. Tested.

**Prewarm.** Warm: 23–43 ms, 92 objects, 10 meshes, 19 materials, 9 glyphs. Each rig is drawn tiny for 2 frames. First-cast frame: 6.1–24 ms (average before: 6–12 ms; two clients ran at once).

**Tests.** `SkillSignatureTests` has 12 tests. U7b `SkillFxCreatesNothingAfterWarmUp` changed: `LastKind≠Hovl` became `LastShape==Static`, `BusyRings/OneShots` became `BusyRigs/BusyGlyphs`, and `RingCount` became `PerShape*8`.

**Gates.** EditMode **1498/1498**. BuildWindows Success. `dotnet build -warnaserror`: 0 warnings, 0 errors.

**Contact sheet:** `docs/qa/U9-screens/contact-sheet.png`.

**Unverified.**
- Live Firewall and Restore: no switch in progress or Lagging teammate in 60 s. Covered by EditMode only.
- Ally views were out of frame.
- Reduced motion was not captured.
- 1080p not captured.

**Risks.**
- The Killer's Ping rings are on the ground, so they are faint in first person.
- With a short boom, the Survivor's own spikes and static fill the view.
- One-line hooks in `ClientApp.cs`, `KillerHud.cs`, `SurvivorHud.cs` and `SkillRing.cs`.
- `SkillFxArt` still references the unused Hovl prefabs.
- Unity left eol-only `M` entries (`git diff` empty).

## Attempt 2 (QA a1 fix list)

- **F1:** the Killer's Ping scanner now draws vertical bands, 0.5–0.85 m above the ground, sweeping 2 m → 16 m over 1.0 s. They are visible on an open street (`killer-0-away`) and at the edges while facing a wall (`killer-0`).
- **F2:** White noise has 30 filled squares, 0.12–0.3 m, about half lit at once, spread over 1.5 m.
- **F3:** the world glyph sits at 1.9 m, rises 0.15 m, and is clamped 24 u inside the top screen edge.
- **F4:** Dummy packet: radius 0.5 m, 0.035 m lines, hidden when the camera is within 1 m.
- **F5:** the Stealth relay dome has 12 meridians and 3 parallels.
- **F7:** the flash shows the glyph only, with no skill name.
- **F8:** the `[fx]` log now prints the signature shape instead of `visual=Hovl`.
- **F6:** new dev flag `-ptk-skill-when-able`. Live S2 flash, S3 caster and K3 caster shots are now captured.
  - The K3 hex wall itself is off view (Exit 2).
  - The contact sheet is rebuilt.
- **Gates:** EditMode 1500/1500; BuildWindows Success.
- **Warm-up:** 23–40 ms. First-cast frames 6–16 ms.
