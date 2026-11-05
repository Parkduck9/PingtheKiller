# QA Review: Unit U7b-skillfx (Skill Feedback) — Attempt 1

- **Reviewer:** Gemini Flash (QA Reviewer)
- **Implementer:** Claude Opus subagent (quota fallback)
- **Target Spec:** `docs/ui/HUD_SPEC.md` §14 ("Skill feedback"), approved in `HUD_SPEC-14.md`
- **Scope:** Skill feedback in-world VFX, result lines, audio cues, information safety, and pooling

---

## 1. §14 Conformance Table

| Skill | Viewer | Result Line & Tone | VFX (Kind / Tint / Diam / Dur) | Audio Clip, dB, Spatial, Cut | Verdict | Evidence |
|---|---|---|---|---|---|---|
| **K0: Ping scanner** | Killer (caster) | `Nearest Survivor shown · 1.2 s` (plain/ticking, 1.5 s, P3) | Procedural sweep / `text` / 2→32 m / 0.6 s at own pos | `ScanKiller` (`Z_Bonus/..._End_2.wav`), −9 dB, 2D, full (0.40 s) | **PASS** | [`SkillFeedback.cs:183-186`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L183-L186), [`KillerFxState.cs:533,582`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L533-L582), [`KillerHudCopy.cs:321`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerHudCopy.cs.txt#L321) |
| | Affected (target) | `Scanned: the Killer sees you · 1.5 s` (`danger` red, 2.5 s, P5) | Hovl `Marker 5 Circle` / `signal` / 2.0 m / 0.6 s at feet | `ScanTarget` (`Z_Bonus/..._End_1.wav`), −6 dB, 2D, full (0.56 s) | **PASS** | [`SkillFeedback.cs:228-234`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L228-L234) |
| | Ally Survivors | — (not sent) | — | — | **PASS** | [`GameServer.Skills.cs:55-57`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L55-L57) |
| **K1: Overclock** | Killer (caster) | `Overclock: 1.5× speed · 2.1 s` (ticking), then `Recovering: cannot move · 0.8 s` (plain, P3) | None (FOV +4° ease in 150 ms, hold 3 s, ease out 150 ms) | Start: `OverclockStart` (`Mechanics/..._Gun1_..._Part_2.wav`), −9 dB, 2D, full (0.68 s); Recover: `OverclockRecover` (`Mechanics/..._Reload_..._1.wav`), −12 dB, 2D, full (0.23 s) | **PASS** | [`SkillFeedback.cs:187-189,391-396`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L187-L189), [`KillerFxState.cs:546-551,578-581,590-597`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L546-L597), [`RoleCamera.cs:532`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/RoleCamera.cs.txt#L532) |
| | Survivors | — (not sent) | — | — | **PASS** | [`GameServer.Skills.cs:63`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L63) |
| **K2: Packet afterimage** | Killer | Passive (`Packet afterimage is passive.` on press, `muted`, 2.0 s, P1) | None | None | **PASS** | [`SkillFeedback.cs:95,378`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L95-L378), [`KillerFxState.cs:540`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerFxState.cs.txt#L540) |
| | Survivors | — (no packet) | — | — | **PASS** | [`GameServer.Skills.cs:75-78`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L75-L78) |
| **K3: Firewall block** | Killer (caster) | `Exit E switch locked · 4.2 s` (plain/ticking, 5.0 s, P3) | Hovl `Marker 3 Zone` / `text` / 3.0 m / 1.0 s at switch | `FirewallKiller` (`Mechanics/..._Analogue_Set_Part_1.wav`), −9 dB, 2D, full (0.50 s) | **PASS** | [`SkillFeedback.cs:190-198`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L190-L198), [`KillerFxState.cs:535,583`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerFxState.cs.txt#L535-L583), [`KillerHudCopy.cs:322`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerHudCopy.cs.txt#L322) |
| | Affected Survivor | `Exit E switch locked by the Killer · 5 s` (`danger` red, 2.5 s, P5) | Hovl `Marker 7 Danger zone` / `danger` / 3.0 m / 1.0 s at switch | `FirewallSurvivor` (`Mechanics/..._Pistol_..._Part_2.wav`), −6 dB, 2D, full (0.56 s) | **PASS** | [`SkillFeedback.cs:236-248`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L236-L248) |
| | Ally Survivors | — | Hovl `Marker 7 Danger zone` / `danger` / 3.0 m / 1.0 s at switch | Same clip, −12 dB at 2 m, positional at switch, range 30 m | **PASS** | [`SkillFeedback.cs:242,249-250`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L242-L250) |
| **S0: Dummy packet** | Caster | `Dummy packet: fake Signal sent from here · 3 s` (`ally` teal, 2.5 s, P3) | Hovl `Marker 2 Pointer` / `signal` / 1.5 m / 0.8 s at X,Z | `Dummy` (`Shoot/Energy/..._Big_Gun3_1.wav`), −9 dB, 2D, cut 0.6 s | **PASS** | [`SkillFeedback.cs:260-264`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L260-L264) |
| | Ally Survivors | — | Hovl `Marker 2 Pointer` / `signal` / 1.5 m / 0.8 s at X,Z | Same clip, −15 dB at 2 m, positional at X,Z, range 20 m | **PASS** | [`SkillFeedback.cs:261,263`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L261-L263) |
| | Killer | — (not sent; ignored if received) | — | — | **PASS** | [`SkillFeedback.cs:178`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L178), [`GameServer.Skills.cs:135-139`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L135-L139) |
| **S1: Stealth relay** | Caster | `Stealth relay: you and N Survivor(s) are Silent · 2 s` (`ally` teal, 2.5 s, P3; on next snapshot) | Procedural ring / `ally` / 0→10 m over 0.15 s, held 2.0 s at X,Z | `Relay` (`Skill/..._Whoosh_Electrified_1.wav`), −12 dB, 2D, cut 2.0 s (300 ms fade) | **PASS** | [`SkillFeedback.cs:265-268,306-315`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L265-L315), [`SurvivorFxState.cs:484,491-500`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L484-L500) |
| | Affected Survivor | `You are Silent · 2 s · Stealth relay from N` (`ally` teal, 2.5 s, P4; on next snapshot) | Same ring at X,Z | Same clip, −15 dB, 2D, cut 1.0 s | **PASS** | [`SkillFeedback.cs:317-322`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L317-L322) |
| | Ally Survivors (unaffected) | — | Same ring at X,Z | Positional at caster X,Z, −18 dB at 2 m, range 20 m, cut 1.0 s | **FAIL** | Positional anchor is overridden to own position: [`SkillFeedback.cs:324-326`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L324-L326), [`ClientApp.cs:182-186`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/ClientApp.cs.txt#L182-L186) (Finding 1) |
| | Killer | — (not sent) | — | — | **PASS** | [`GameServer.Skills.cs:135-139`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L135-L139) |
| **S2: Traffic overload** | Caster | `Traffic overload: you are Signal, the Killer is slowed · 2 s` (`signal` amber, 2.5 s, P3) | Hovl `Marker 6 Arrows` / `signal` / 2.0 m / 0.5 s at X,Z | `Overload` (`Shoot/Electric/..._Medium_Gun_1.wav`), −9 dB, 2D, cut 0.6 s | **PASS** | [`SkillFeedback.cs:269-272`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L269-L272) |
| | Ally Survivors | `N slowed the Killer · 2 s` (`ally` teal, 2.5 s, P2) | Hovl `Marker 6 Arrows` / `signal` / 2.0 m / 0.5 s at X,Z | Same clip, −15 dB at 2 m, positional at caster X,Z, range 20 m | **PASS** | [`SkillFeedback.cs:270,272`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L270-L272) |
| | Killer (affected) | `Slowed by traffic overload · 1.6 s` (`danger` red, ticking, 2.0 s, P5) | None (no X,Z on wire, caster blip unmarked) | `OverloadSlowed` (`Mechanics/..._Gun_Out_of_Ammo.wav`), −6 dB, 2D, cut 1.0 s | **PASS** | [`SkillFeedback.cs:170-176`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L170-L176), [`KillerFxState.cs:529,556`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerFxState.cs.txt#L529-L556), [`KillerHudCopy.cs:323`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerHudCopy.cs.txt#L323) |
| **S3: Emergency restore** | Caster | `Restored N · no longer Lagging` (`ally` teal, 2.5 s, P3) | Hovl `Marker 4 Pillar` / `ally` / 1.5 m / 1.0 s at target X,Z | `Restore` (`Z_Bonus/..._Start_2.wav`), −9 dB, 2D, cut 1.0 s | **PASS** | [`SkillFeedback.cs:274-279`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L274-L279) |
| | Affected Survivor | `N restored you · no longer Lagging` (`ally` teal, 2.5 s, P4) | Same pillar at X,Z | Same clip, −6 dB, 2D, cut 1.0 s | **PASS** | [`SkillFeedback.cs:280-285`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L280-L285) |
| | Ally Survivors | `N restored N` (`ally` teal, 2.5 s, P2) | Same pillar at X,Z | Same clip, −15 dB at 2 m, positional at X,Z, range 20 m | **PASS** | [`SkillFeedback.cs:286`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L286) |
| | Killer | — (not sent) | — | — | **PASS** | [`GameServer.Skills.cs:120,135-139`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L120-L139) |
| **S4: White noise** | Caster | `White noise: fake Signals sent across the map · 1.5 s` (`ally` teal, 2.5 s, P3) | Hovl `Marker 3 Zone` / `signal` / 3.0 m / 1.0 s at X,Z | `WhiteNoise` (`Skill/..._Fire_Rain_1.wav`), −12 dB, 2D, cut 1.5 s (200 ms fade) | **PASS** | [`SkillFeedback.cs:289-291`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L289-L291) |
| | Ally Survivors | `N sent fake Signals across the map · 1.5 s` (`ally` teal, 2.5 s, P2) | Same zone at X,Z | Same clip, −18 dB at 2 m, positional at X,Z, range 20 m, cut 1.0 s | **PASS** | [`SkillFeedback.cs:290,292`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L290-L292) |
| | Killer | — (not sent) | — | — | **PASS** | [`GameServer.Skills.cs:125,135-139`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L125-L139) |
| **Refused press (§14.3)** | Survivor / Killer | Survivor: `Skill not used — the server did not accept it.`<br>Killer: `Not used · server did not accept` (`muted`, no bar, 2.0 s, P1) | None | None | **PASS** | [`SkillFeedback.cs:97-98,449-466`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L97-L466), [`ClientApp.cs:131-167`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/ClientApp.cs.txt#L131-L167) |

---

## 2. Information Safety

- **Killer client isolation:**
  - `SkillFeedback.ForKiller` ([`SkillFeedback.cs:166-178`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L166-L178)) evaluates only `casterId == ownId` and `casterId == 0 && skillId == TrafficOverload`. All other packets yield `FxViewer.None`.
  - Killer blips remain anonymous instances drawn via `KillerScene` ([`KillerWorldLayer.cs:148-160`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/KillerWorldLayer.cs.txt#L148-L160)). `SkillFx` attaches nothing to blips or models.
- **Decoy safety:**
  - Neither Dummy packet nor White noise packets are sent to the Killer by `GameServer` ([`GameServer.Skills.cs:135-139`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/GameServer.Skills.cs.txt#L135-L139)).
  - Even if fed locally, `KillerFxState` ignores them completely; verified by `TheKillerHasNoOutputForAnySurvivorSkill` and `DummyPacketAndWhiteNoiseDecoysAreAbsentFromTheKillerPath` ([`SkillFeedbackTests.cs:113-143`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedbackTests.cs.txt#L113-L143)).
- **No Killer coordinates on Survivor client:**
  - Ping scanner effect carries `0, 0` and is never read; VFX is strictly anchored to `FxAnchor.Own` ([`SkillFeedback.cs:232`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L232)). Verified by `ScannedSurvivorUsesItsOwnPositionNeverTheEffectPoint` ([`SkillFeedbackTests.cs:148-163`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedbackTests.cs.txt#L148-L163)).
  - Firewall block effect uses `X, Z` solely to resolve `ExitAt(x, z)` within 1.0 m; the visual is placed exclusively at `SharedMapPoints.Switches[exit]` ([`SkillFeedback.cs:236-243`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L236-L243)).
- **Killer ID lookup:**
  - `CasterIsSurvivor` ([`SkillFeedback.cs:208-213`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L208-L213)) checks only `ownId` and `mates`. If unmatched, it treats the skill as a Killer skill without querying any Killer player entry.
- **Lobby names:**
  - Names are looked up only via `SurvivorFrame.Names` for Survivor casters and targets; never for the Killer.

---

## 3. Correctness Review

- **Emergency restore target derivation:**
  - Matches closest Lagging teammate within 0.5 m in the snapshot prior to effect ([`SkillFeedback.cs:331-342`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L331-L342)). If none matches, falls back cleanly to `"a Survivor"`.
- **Stealth relay count timing:**
  - `SurvivorFxState` defers resolving the caster count line until `OnWorld` processes the first snapshot at or after the effect ([`SurvivorFxState.cs:484,491-500`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SurvivorFxState.cs.txt#L484-L500)). Lagging mates are excluded (`mate.Status == PlayerStatus.Normal`).
- **Refused-press inference (0.5 s):**
  - Evaluated in `PressReason` ([`SkillFeedback.cs:375-389`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L375-L389)). Client-consumed presses (passive, Lagging, on cooldown, missing target/switch precondition) return reasons immediately and set `watch = false`. Only presses sent while derived Ready arm `PressWatch`.
- **Line priority & replacement:**
  - `ResultLine.Offer` ([`SkillFeedback.cs:436-442`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L436-L442)) enforces `priority >= shown.Priority`. Lower priority lines are dropped immediately without queueing.
- **Match lifecycle cleanup:**
  - `SkillFx.Clear()`, `survivorFx.Reset()`, and `rig.KillerFovOffset = 0` are called consistently on `MatchStart`, `GameResult`, and `Disconnect` ([`ClientApp.cs:72,108,212`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/ClientApp.cs.txt#L72-L212)).
- **Pooling:**
  - Pooled at warm-up: 3 instances per Hovl prefab, 6 procedural rings, 2 2D voices, 4 positional voices. Verified by `SkillFxCreatesNothingAfterWarmUp` ([`SkillFeedbackTests.cs:438-481`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedbackTests.cs.txt#L438-L481)). Exhausted pools reuse the oldest active instance.
- **Reduced motion:**
  - In `SkillFeedback.Reduced` ([`SkillFeedback.cs:354-360`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L354-L360)), Hovl prefabs are replaced with flat procedural rings at full diameter with 0 grow time; Ping scanner sweep and Overclock FOV widening are disabled.
- **Audio priority cutting:**
  - Separate 2D audio channels for `FxTier.Skill` and `FxTier.High` prevent skill cues from cutting high-tier cues ([`SkillFx.cs:94-95,235`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFx.cs.txt#L94-L235)). However, the 30 ms fade on voice reuse was not implemented (Finding 2).

---

## 4. Visuals & Screenshot Inspection

- **Marker Diameters & Centering:**
  - Inspected all 23 PNG captures in `docs/qa/U7b-review/`.
  - Comparing against character height (~1.8 m):
    - Hovl Circle (Ping scanner target): ~2.0 m diameter, centered at character feet (`s0-survivor-k0-affected-1080p.png`).
    - Hovl Pointer (Dummy packet): ~1.5 m diameter (`s0-survivor-s0-caster-1080p.png`).
    - Procedural Ring (Stealth relay): ~10 m diameter (~5.5× character height), centered at cast point (`s1-survivor-s1-caster-1080p.png`).
    - Hovl Arrows (Traffic overload): ~2.0 m diameter (`s2-survivor-s2-caster-1080p.png`).
    - Hovl Zone (White noise): ~3.0 m diameter (`s4-survivor-s4-caster-1080p.png`).
    - Still Ring (Reduced motion White noise): 3.0 m flat unlit ring (`s4reduced-survivor-s4-caster-1080p.png`).
- **Bloom & Glow Check:**
  - `_Emission` set to 0.8 in `SkillFxSetup.cs:22` (below the 0.9 URP bloom threshold).
  - `Flash` and `EndSparks` glow puff children disabled in `SkillFx.cs:113`.
  - No white burn-out in any capture; amber and teal retain chromatic purity.
- **Typography & Layout (720p & 1080p):**
  - Result line (672×40) sits directly above prompt row with 8 u padding. When prompt row is hidden, result line takes its position without awkward spacing.
  - No overlapping or text wrapping; high legibility at 720p.

---

## 5. Test Quality

- `SkillFeedbackTests.cs` contains 26 comprehensive EditMode tests:
  - Validates every viewer role across all 9 skills.
  - Decoy safety tests (`TheKillerHasNoOutputForAnySurvivorSkill`, `DummyPacketAndWhiteNoiseDecoysAreAbsentFromTheKillerPath`) pass wire-equivalent packets through `KillerFxState` and assert total state invariance.
  - Coordinate decoupling tests pass nonzero `X, Z` to Survivor Ping scanner and Firewall and verify strictly static or own positions are used.
  - Pooling test checks GameObject instantiation counts before and after warm-up across multiple stress rounds.
- Tests are meaningful and non-tautological.

---

## 6. Findings

### Finding 1: Unaffected Ally of Stealth Relay plays positional sound at listener's own position instead of caster's position
- **Severity:** Major
- **File & Line:** [`src/SkillFeedback.cs.txt:324-326`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFeedback.cs.txt#L324-L326) and [`src/ClientApp.cs.txt:182-186`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/ClientApp.cs.txt#L182-L186)
- **Reproduction Steps:**
  1. Join a match as Survivor.
  2. Have an ally Survivor cast Stealth Relay at a distance > 25 m.
  3. Observe the positional sound played when the snapshot arrives.
- **Expected vs Actual:**
  - *Expected (§14.2.6):* Sound plays positional at the caster's `X, Z`, −18 dB at 2 m, range 20 m, cut at 1.0 s. Teammates beyond 20 m should be outside audible range.
  - *Actual:* `SkillFeedback.RelayResolved` returns `r.Sound = Positional(...)` but leaves `r.Visual` uninitialized (`X = 0, Z = 0, Shown = false`). In `ClientApp.PlayFx`, `if (record.Visual.Anchor == FxAnchor.Own || (record.Sound.Positional && !record.Visual.Shown))` evaluates to true, overriding `at` to the listener's own position (`own.X, own.Z`). Consequently, the positional sound plays at 0 m distance from the listener at full volume anywhere on the map.

### Finding 2: Missing 30 ms fade on skill-tier voice reuse
- **Severity:** Minor
- **File & Line:** [`src/SkillFx.cs.txt:15,257`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/src/SkillFx.cs.txt#L15-L257)
- **Reproduction Steps:**
  1. Trigger two consecutive skill-tier cues in rapid succession.
- **Expected vs Actual:**
  - *Expected (§14.1 Sound priority):* "A new skill-tier cue cuts the previous skill-tier cue (30 ms fade)."
  - *Actual:* The constant `TierCutFade = .03f` is defined but never used in `SkillFx.cs`. `voice.Source.Stop()` performs an immediate cut without a 30 ms fade-out before playing the new clip.

### Finding 3: Missing 720p screenshot for Ping scanner scanned target
- **Severity:** Minor
- **File & Line:** [`U7b-skillfx-impl-a1.md:19`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U7b-review/U7b-skillfx-impl-a1.md#L19)
- **Expected vs Actual:**
  - *Expected (Brief §Acceptance):* Scanned target view captured at both 720p and 1080p.
  - *Actual:* Only 1080p screenshot is provided (`s0-survivor-k0-affected-1080p.png`).

---

## 7. SLOP §9 Score

| Check | Points | Score | Notes |
|---|---:|---:|---|
| No V1–V12 visual tells present (V1, V2, V4, V8, V10 gates) | 20 | 18 | No bloom, no UI glow, no gradients. Minor deduction for Hovl particle soft halo. |
| No L1–L8 layout tells; 4px spacing scale adhered to | 15 | 15 | 672×40 bar, 8 u padding, consistent bottom-center stack alignment. |
| Copy follows Section 4 and fixed vocabulary | 15 | 15 | Exact adherence to fixed terms and §14 spec copy. |
| Every element answers "what state, what action" (Gate) | 15 | 15 | All lines, rings, and sounds directly communicate concrete skill outcomes. |
| Role-specific HUDs; zero forbidden information leak (Gate) | 20 | 20 | Decoy safety and coordinate isolation fully preserved. |
| Interactive element states & navigation (Gate) | 10 | 10 | Prompt and settings accessibility intact. |
| Text contrast ≥ 4.5:1; readable at 720p | 5 | 5 | Clean white on `#1A1C1F` panel, verified at 720p. |
| **Total** | **100** | **98** | **Pass Threshold: ≥ 85 and no gate failures** |

---

VERDICT: FAIL (0 blockers, 1 majors)
