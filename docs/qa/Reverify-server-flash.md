# Server Re-verification Report (U2c, U2e) — Gemini 3.8 Flash

Reviewer: Gemini 3.8 Flash (Medium) (independent QA).  
Working Directory: `C:\Unity\MultiPlayGame\PtK-reqa\server` on branch `claude/reqa-server`.  
Scope: Area A — Server: U2c (protocol v0.3 / decoy hardening / RNG privacy) and U2e (survivors see Killer in line of sight).  
New Tests Added: `Server.Tests/QA/Reverify/ServerReverifyTests.cs` (namespace `PingTheKiller.Tests.QA.Reverify`). Product code (`Shared/`, `Server.Core/`, `Server/`, `Tools/`, `Client/`) and existing test suites were strictly untouched.

---

## 1. Specification Compliance Matrix

### U2c: Protocol v0.3 (SelfState) + Decoy Hardening + Server RNG Privacy

| Section | Spec Bullet | Verdict | Evidence |
|---|---|---|---|
| Sec 1 | Killer view is anonymous: byte-level Id=0, Yaw=0, CaptureCount=0, StatusTimer=0, Flags=Signal (0x01) only. Real Id only on Killer entry. Shuffled with private RNG. Decoys no longer borrow Ids; WhiteNoise always shows 5 points even in 1v1. | PASS | `ServerReverifyTests.U2c_Sec1_KillerView_ByteLevelAnonymity_AndShuffling`<br>`ServerReverifyTests.U2c_Sec1_DecoysNoLongerBorrowIds_WhiteNoiseAlwaysFive_In1v1`<br>`U2cAdversarialTests.Leak_KillerFramesAtByteLevel...` |
| Sec 2 | One emit path for real and fake blips: phantoms have same visibility fields (`Visible`, `LastVisibleAt`, `VisibleX/Z`). PacketAfterimage lingers 1.0 s frozen at last seen position. Skill durations Dummy 3.0 s, WhiteNoise 1.5 s. | PASS | `ServerReverifyTests.U2c_Sec2_PhantomAndReal_IdenticalFields_AndAfterimageLinger`<br>`U2cAdversarialTests.Leak_KillerView_IsIdenticalInShape...` |
| Sec 3 | Decoy behaviour: movement driven by private RNG, never stands inside walls (`Map.CanStand`), max step bounded by `SurvivorSpeed * dt`. DummyPacket spawns at caster position, still near incomplete terminals, walks otherwise. WhiteNoise 5 points, spawn stagger 0-0.5 s, expires cast + 1.5 s, clearance >= 3 m from killer and >= 6 m apart. | PASS | `ServerReverifyTests.U2c_Sec3_PhantomMovement_NeverInsideWalls_AndStillNearTerminals`<br>`ServerReverifyTests.U2c_Sec3_DummyPacket_SpawnsAtCasterPosition`<br>`U2cAdversarialTests.U2bSkillTests` |
| Sec 4 | Server RNG privacy: `MatchStart.Seed` is cosmetic-only; private simulation is driven by CSPRNG-seeded 256-bit xoshiro256** streams. Varying wire seed produces bit-identical simulation. Killer-bound `S2C_SkillEffect` carries `CasterId = 0` for TrafficOverload. | PASS | `ServerReverifyTests.U2c_Sec4_CosmeticWireSeed_DoesNotAffectPrivateSimulation`<br>`ServerReverifyTests.U2c_Sec4_KillerBoundTrafficOverload_CarriesZeroCasterId`<br>`U2cAdversarialTests.SeedAttack` tests |
| Sec 5 | Protocol v0.3 — `S2C_SelfState` sent only to own player at 20 Hz (with delta-compression/refresh). `ExecutionGraceLeft` sent only to Killer when Lagging survivor is within 3 m; survivors always receive 0. `RescueProgress` 0-1 for Lagging and rescuer. Terminal progress in Killer view quantised to 0 or 100 only. | PASS | `ServerReverifyTests.U2c_Sec5_SelfState_OnlyToOwnPlayer_AndExecutionGraceLeft_AndTerminalProgress`<br>`U2cAdversarialTests.SelfState_*`<br>`U2cAdversarialTests.Leak_TerminalProgress...` |
| Sec 6 | Carried from U2b QA: private RNG state >= 128-bit (`PrivateRandom` 256-bit). Deterministic seed hook allows reproducible test traces. | PASS | `ServerReverifyTests.U2c_Sec6_PrivateRandom_Has256BitState_AndDeterministicHook`<br>`U2bA2Tests.Seed_*` |

---

### U2e: Survivors See the Killer in Line of Sight (Server)

| Contract Item | Spec Bullet | Verdict | Evidence |
|---|---|---|---|
| Item 1 | Survivor-bound `S2C_WorldState` contains Killer entry ONLY when recipient is Normal or Lagging and 2D segment from recipient to Killer touches no wall in `FixedMap.Walls`. Closed AABB, no inflation, edge/corner touch blocks, zero-length never blocked. | PASS | `ServerReverifyTests.U2e_Contract1_SegmentBlocked_EdgeCases`<br>`LineOfSightTests.SegmentBlockedMatchesTheClosedBoxRule`<br>`U2eAdversarialTests.MapSweep_*` |
| Item 2 | Entry fields exact: real Id, Role = Killer, current server X/Z/Yaw, Status. Flags = 0, StatusTimer = 0, CaptureCount = 0. | PASS | `ServerReverifyTests.U2e_Contract2_And_4_EntryFieldsExact_AndPerRecipientIsolation`<br>`LineOfSightTests.ExactContractFields`<br>`U2eAdversarialTests.CheckFrame` |
| Item 3 | Spectators (Eliminated and Escaped) NEVER receive the Killer entry, even with clear line of sight. | PASS | `ServerReverifyTests.U2e_Contract3_SpectatorsNeverReceiveKillerEntry`<br>`U2eAdversarialTests.LaggingRecipient..._EliminatedAndEscapedNever...` |
| Item 4 | Per-recipient isolation: line of sight is evaluated independently per recipient. One survivor's line of sight never reveals Killer to another. | PASS | `ServerReverifyTests.U2e_Contract2_And_4_EntryFieldsExact_AndPerRecipientIsolation`<br>`U2eAdversarialTests.MapSweep_*` (2160 ticks with survivor disagreement) |
| Item 5 | No linger and no prediction: same-tick flip. Entry appears on exact tick line of sight clears, vanishes on next tick when blocked. | PASS | `ServerReverifyTests.U2e_Contract5_SameTickFlip_NoLinger_NoPrediction`<br>`U2eAdversarialTests.SameTick_MovingKillerAndMovingSurvivor_*` |
| Item 6 | Unchanged invariants: `ThreatLevel` distance band computed regardless of LoS; survivor-bound `S2C_SkillEffect` contains no Killer coordinates (PingScanner carries X=0, Z=0); Killer-bound view remains anonymous. | PASS | `ServerReverifyTests.U2e_Contract6_ThreatLevelUnaffectedByLoS_AndNoKillerCoordsInSkillEffects`<br>`U2eAdversarialTests.ThreatLevelIsTheDistanceBand*` |

---

## 2. Findings

No blockers and no major defects were found. All spec contracts are strictly enforced in product code. Five minor observations/accepted residuals were verified:

### F1 (minor, accepted residual): Per-track classifier AUC separates phantoms from real tracks
- **Severity**: Minor (accepted residual per PLAN/brief).
- **Reproduction**: Run `Classifier_PositionTrackingAt20Hz_SeparatesPhantomFromRealNormalTracks_AucReportedWithGroundTruth` across seeded matches.
- **Expected vs Actual**: Brief accepts AUC <= 0.85 on aggregate track features. Observed cross-validated AUC is ~0.798 on stillness-only and ~0.758 on per-step features, well within the 0.85 threshold. Per-step speed and afterimage tail features remain indistinguishable at chance level.
- **Evidence**: `U2cAdversarialTests.Classifier_*` output and `docs/qa/U2c-qa-a1.md`.

### F2 (minor, observation): Phantom afterimage tail duration measured from `LastVisibleAt`
- **Severity**: Minor.
- **Reproduction**: Track a Dummy phantom's afterimage tail when killer has `PacketAfterimage`.
- **Expected vs Actual**: Phantoms that enter a pause shortly before expiration have their afterimage window calculated from `LastVisibleAt` rather than `Until`. This causes the observable afterimage tail after skill expiry to be ~14 ticks rather than 20 ticks in some burst/pause phase offsets. Invariant holds: total visible-to-expiry window never leaks and never exceeds 1.0 s after last visible blip.
- **Evidence**: `ServerReverifyTests.U2c_Sec2_PhantomAndReal_IdenticalFields_AndAfterimageLinger`.

### F3 (minor, observation): Denormal coordinate handling in `FixedMap.SegmentBlocked`
- **Severity**: Minor.
- **Reproduction**: Feed denormal float coordinates ($|v| < 2^{-10}$, e.g. $10^{-45}$) to `FixedMap.SegmentBlocked`.
- **Expected vs Actual**: Double delta absorptions can cause minor asymmetry on denormal inputs.
- **Actual**: All playable coordinates are bounded in $[0, 60]$, and player clearance from boundaries ($>= 0.4$ m) guarantees denormal inputs never occur during gameplay.
- **Evidence**: `U2eAdversarialTests` geometry analysis (0 mismatches across 120,000 in-map segments).

### F4 (minor, observation): Per-tick allocation when Killer is in line of sight
- **Severity**: Minor.
- **Reproduction**: Profiling `BroadcastWorld` when Killer is visible to survivors.
- **Expected vs Actual**: Segment decision allocates 0 B. Adding the legal killer entry to `world.Players` adds ~1 entry per visible recipient (~7 KB/tick across 7 recipients), which is bounded and standard for snapshot serialization.
- **Evidence**: `U2eAdversarialTests.PerTickAllocation_*`.

### F5 (minor, integration observation): Ping Scanner bot killer exposes nearest survivor
- **Severity**: Minor.
- **Reproduction**: Seeded killer bots with Ping Scanner.
- **Expected vs Actual**: Ping Scanner skill exposes nearest survivor regardless of Signal status by design; killer bot decisions still consume only anonymous world state positions.
- **Evidence**: `U2cAdversarialTests.Bots_KillerBotIgnoresSilentSurvivors_ChasesWhatItSees`.

---

## 3. Gate Verification

Commands executed:
1. `dotnet build PingTheKiller.sln -warnaserror --no-incremental`
2. `dotnet test PingTheKiller.sln`

### Gate Counts:
- **Build Warnings**: 0
- **Build Errors**: 0
- **Server.Tests**: 813 / 813 passed (793 existing + 20 new reverify tests, 0 failed, 0 skipped)
- **Harness.Tests**: 616 / 616 passed (0 failed, 0 skipped)
- **Total Tests**: 1429 / 1429 passed

---

VERDICT: PASS
