# U4e implementation report — attempt 1

Implementer: Claude Opus (quota fallback). Not committed.

**Changed:** `World/SnapshotBuffer.cs` (`SampleKiller`), `World/WorldView.cs` (Killer model), new `World/LaggingStutter.cs`, new `UI/SurvivorFeedback.cs` (ThreatVisual, CaptureWatch, cue synth, StutterClock), `UI/SurvivorHud.cs`, `UI/SurvivorReadout.cs`, `UI/ResultCopy.cs`, `UI/ClientScreens.cs`, `ClientUI.uss`, `ClientApp.cs`, `Dev/DevArgs.cs`, `Dev/DevCapture.cs`, new `Dev/FakeKiller.cs`, `Killer/KillerWorldLayer.cs`, new `Tests/EditMode/SurvivorFeedbackTests.cs` (41 tests), `docs/ui/HUD_SPEC.md` (§0.1, §4.1, §4.2, §4.5, §4.10, §6.5.1, §7.2, §10, all `(10/30)`).

**Verified:**
- EditMode: 1094 total, 1094 passed, 0 failed (41 new, 754 QA). 0 `error CS`.
- BuildWindows succeeded.
- The offline match opened no socket: tcp=0 and udp=0 at +20 s and +35 s.

**Screens** (`docs/qa/U4e-screens/`, 1080 and 720):
- Natural, from offline bot matches: threat-1, threat-3, capture-banner, lagging-stutter, eliminated-reason. The eliminated-reason shot is TimedOut at 1080 and SecondCapture at 720.
- Injected (`-ptk-fake-killer`): killer-in-sight-injected and killer-fade-injected. U2e is not merged, so the server never sends the entry.
- I opened every shot and found no text collisions.

**Fixed:** F-01 (clamped body rect), F-02 (tag on the tilted head), F-03 (`skill_ready` only while Normal).

**Deviations:**
- A gap of up to 0.3 s in the Killer entry is interpolated instead of fading.
- The strip uses 1 u texels.
- Added an `eliminated` cue.
- The beep is fed the effective level (0 while Eliminated or Escaped).
- The banner copy uses SLOP vocabulary ("Captured", "Survivor").
- The fake Killer also raises ThreatLevel.

**Unverified / risks:**
- No real U2e entry has been tested.
- The Executed reason has not been seen in a match.
- Pre-existing from U4d: at 720p the skill name wraps mid-word as "Emergenc y restore".

## Attempt 2 (fix round, `docs/briefs/U4e-a2-fixes.md` + Q-01 ruling)

**Fixes**
- **Q-01:** the meter strip is 8 u at 45 % (`ThreatVisual.StripHeight`/`StripDensity`), with 2 u texels. The band is unchanged (24 u, 30 %). HUD_SPEC §4.2 is updated and marked `(10/30)`.
- **Q-02:**
  - The skill block is 176 u with a 100 u name column; the info block is 160 u. The cluster stays 672 u.
  - New test `Q02EverySkillNameWrapsOnlyAtSpaces…` measures Inter advances plus TextCore bold spacing. It covers all Survivor names at 14 and 15 u, all Killer names (one line at 232 u), and the USS widths.
  - HUD_SPEC §4.0 and §4.8 are updated.
- **Q-03:** the Killer visor uses a transparent material and fades with the body. Test: `Q03TheVisorFadesTogetherWithTheBody`.
- **Q-04b:** new `WorldView.KillerDrawable` hides the model while the own status is Eliminated or Escaped. Test: `Q04bASpectating…`.
- **Q-04:** the banner now reads "Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left". HUD_SPEC §4.1 is updated.

**Gate**
- EditMode: 1143 total, 1142 passed, 1 failed, 0 `error CS`.
- The only failure is QA's `SurvivorFeedbackQaTests.CaptureBanner_CopyIsPlainFixedVocabularyWithTheRuleNumbers`. It asserts `StartWith("Captured.")`, which is the expected result of the Q-04 wording change. I did not edit it.
- No QA test asserts 4 u or 30 % for the strip.
- BuildWindows succeeded.

**Screens** (natural, offline bots)
- threat-1, threat-2 (new), threat-3, capture-banner, lagging-stutter and eliminated-reason, at both 1080 and 720.
- emergency-restore-720 shows "Emergency / restore" wrapping at the space.
- The 720 eliminated-reason shot shows "Executed: the Killer touched you while Lagging.", the first time this reason has appeared in a natural match. It wraps to two lines.
- I opened every shot and found no collisions.
