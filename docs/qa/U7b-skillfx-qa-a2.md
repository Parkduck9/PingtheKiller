# QA Review: Unit U7b-skillfx (Skill Feedback) — Attempt 2

- **Reviewer:** Gemini 3.8 Flash (QA Reviewer)
- **Implementer:** Claude Opus subagent (quota fallback)
- **Target Spec:** `docs/ui/HUD_SPEC.md` §14 ("Skill feedback"), approved in `HUD_SPEC-14.md`
- **Scope:** Resolution verification of F1, F2, and F3; regression check; pooling and information safety audit
- **Test Baseline:** EditMode 1386/1386 passed (4 new tests added), `BuildScript.BuildWindows` succeeded

---

## 1. Resolution Table

| Finding | Severity (a1) | Description | Status | Evidence |
|---|---|---|---|---|
| **F1** | Major | Unaffected Stealth relay ally played positional sound at listener's own position instead of caster's position | **RESOLVED** | `SkillFeedback.cs:52-63,326,348-358`, `SkillFx.cs:176-184,261-274`, `ClientApp.cs:528-541`, `SkillFeedbackTests.cs:486-539` |
| **F2** | Minor | Missing 30 ms fade on skill-tier voice reuse | **RESOLVED** | `SkillFx.cs:15,53-56,98-99,250-257,313-317`, `SkillFeedbackTests.cs:544-568` |
| **F3** | Minor | Missing 720p screenshot for Ping scanner scanned target | **RESOLVED** | Brief allowance satisfied; 2 additional staging runs attempted; documented in `U7b-skillfx-impl-a1.md:36` |

---

## 2. Detailed Verification of Findings

### Finding 1 (Major): Positional Sound Anchoring & Coordinate Isolation
- **Positional Cues Audit:**
  - `DummyPacket` (S0 Ally): `Positional(FxClip.Dummy, -15, 20, 0, x, z)` ([`SkillFeedback.cs:264`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L264)). Positioned at caster's `(x, z)`.
  - `TrafficOverload` (S2 Ally): `Positional(FxClip.Overload, -15, 20, 0, x, z)` ([`SkillFeedback.cs:273`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L273)). Positioned at caster's `(x, z)`.
  - `EmergencyRestore` (S3 Ally): `Positional(FxClip.Restore, -15, 20, 0, x, z)` ([`SkillFeedback.cs:287`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L287)). Positioned at restored target's `(x, z)`.
  - `WhiteNoise` (S4 Ally): `Positional(FxClip.WhiteNoise, -18, 20, 1f, x, z)` ([`SkillFeedback.cs:293`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L293)). Positioned at caster's `(x, z)`.
  - `StealthRelay` (S1 Unaffected Ally): `Positional(FxClip.Relay, -18, 20, 1f, x, z)` ([`SkillFeedback.cs:326`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L326)). Positioned at caster's `(x, z)`.
  - `FirewallBlock` (K3 Ally): `Positional(FxClip.FirewallSurvivor, -12, 30, 0, at.X, at.Z)` ([`SkillFeedback.cs:250`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L250)). Positioned strictly at static switch coordinates `SharedMapPoints.Switches[exit]`.
- **Survivor Client Coordinate Isolation:**
  - K0 (Ping scanner): Carries `0, 0` on wire ([`GameServer.Skills.cs:57`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L57)). On Survivor receipt, `Visual.Anchor = FxAnchor.Own` ([`SkillFeedback.cs:233`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L233)), sound is 2D (`Positional = false`). The packet coordinate is never read.
  - K3 (Firewall block): Packet `X, Z` is evaluated exclusively via `ExitAt(x, z)` ([`SkillFeedback.cs:150-158,238`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L150-L238)) to match a static exit within 1.0 m. Both visual marker and positional audio are placed at `SharedMapPoints.Switches[exit]` ([`SkillFeedback.cs:240,244,250`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L240-L250)).
  - Verified: No Survivor client ever places a sound or visual at a Killer's live coordinates.
- **Anchoring Path & Test Realism:**
  - `ClientApp.PlayFx` delegates anchoring entirely to `skillFx.Play(record, self)` ([`ClientApp.cs:534`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/ClientApp.cs.txt#L534)), which evaluates `SkillFeedback.VisualPoint` and `SkillFeedback.SoundPoint` ([`SkillFx.cs:180-181`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L180-L181)).
  - Tests `AnUnaffectedRelayAllyHearsTheCueAtTheRelayPoint`, `EveryPositionalCueCarriesTheEffectPoint`, and `SkillFxPlacesThePositionalVoiceAtTheEffectPoint` ([`SkillFeedbackTests.cs:486-539`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedbackTests.cs.txt#L486-L539)) exercise this exact production path, verifying voice placement at `(65, 0, 40)` and attenuation to silence beyond 20 m.

### Finding 2 (Minor): 30 ms Voice Fade
- **Allocation Check:**
  - In `Warm()`, two dedicated skill-tier 2D voices are pooled: `skillVoices[0]` and `skillVoices[1]` ([`SkillFx.cs:98-99`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L98-L99)).
  - In `Sound()`, cue cut updates `previous.FadeFrom = now; previous.Stop = now + TierCutFade;` and toggles index `skillSlot = 1 - skillSlot` ([`SkillFx.cs:253-256`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L253-L256)).
  - Custom rolloff curves for positional cues are cached in `rolloffs` ([`SkillFx.cs:57,271`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L57-L271)).
  - In `Tick()`, volume attenuation uses cached fields and local arithmetic ([`SkillFx.cs:314-316`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L314-L316)); no allocations occur per cue or per frame. Test `ANewSkillCueFadesThePreviousOneOver30Ms` asserts `fx.Created == created` ([`SkillFeedbackTests.cs:565`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedbackTests.cs.txt#L565)).
- **Tier Inviolability:**
  - High-tier 2D cues (`FxTier.High`: Ping scanned target, Firewall affected, Killer overload slowed) route exclusively to `highVoice` ([`SkillFx.cs:249`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L249)).
  - A skill-tier cue operates solely on `skillVoices` and cannot touch or cut `highVoice`.

### Finding 3 (Minor): 720p Scanned Target Screenshot
- Staged across two additional runs (`scan4`, `scan5`), but the offline Killer bot randomized to non-Ping skills.
- The brief explicitly allowed: *"Capture it if one or two more runs can stage it. Otherwise state that in one line."*
- Permitted condition fulfilled and noted in `docs/qa/U7b-review/a2/U7b-skillfx-impl-a1.md:36`.

---

## 3. Regression Analysis

- **Own-Position Anchor for Own-Anchored Visuals:**
  - `SkillFeedback.VisualPoint` assigns `ownX, ownZ` when `r.Visual.Anchor == FxAnchor.Own`, otherwise `r.Visual.X, r.Visual.Z` ([`SkillFeedback.cs:348-352`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L348-L352)).
  - Verified for Killer Ping scanner sweep and Survivor scanned circle marker; both anchor correctly to the character model's position.
- **Reduced Motion:**
  - `SkillFeedback.Reduced` ([`SkillFeedback.cs:380-386`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L380-L386)) converts Hovl prefabs to static unlit procedural rings at full scale (`GrowSeconds = 0`), removes Ping scanner sweep, and disables Overclock FOV widening. Verified by `ReducedMotionStillsEveryEffect` ([`SkillFeedbackTests.cs:385-396`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedbackTests.cs.txt#L385-L396)).
- **Pooling Counts:**
  - Pooled instances: 3 per Hovl prefab, 6 procedural rings, 2 skill 2D voices, 1 high 2D voice, 4 positional voices.
  - Zero GameObject or AudioSource instantiations after warmup; oldest busy instances are recycled under saturation ([`SkillFx.cs:221-241`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFx.cs.txt#L221-L241)). Verified by `SkillFxCreatesNothingAfterWarmUp` ([`SkillFeedbackTests.cs:438-481`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedbackTests.cs.txt#L438-L481)).
- **Match Lifecycle Cleanup:**
  - `SkillFx.Clear()`, `survivorFx.Reset()`, and `rig.KillerFovOffset = 0` are called consistently on `MatchStart` ([`ClientApp.cs:217`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/ClientApp.cs.txt#L217)), `EndMatch` ([`ClientApp.cs:308`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/ClientApp.cs.txt#L308)), and `Disconnect` ([`ClientApp.cs:618`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/ClientApp.cs.txt#L618)).
- **Decoy-Safety Rule:**
  - `SkillFeedback.ForKiller` returns `default` (`FxViewer.None`) for all Survivor skills ([`SkillFeedback.cs:173,179`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedback.cs.txt#L173-L179)). Verified by `TheKillerHasNoOutputForAnySurvivorSkill` and `DummyPacketAndWhiteNoiseDecoysAreAbsentFromTheKillerPath` ([`SkillFeedbackTests.cs:114-143`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/a2/SkillFeedbackTests.cs.txt#L114-L143)).

---

## 4. New Findings

No new blocker, major, or minor findings were introduced by attempt 2.

---

VERDICT: PASS