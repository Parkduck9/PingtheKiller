# U2c QA report, attempt 1

QA: Claude Sonnet (implementer: Claude Opus). Branch `claude/u2c-protocol`, implementation uncommitted. Nothing committed, pushed or switched.
Files touched: `Server.Tests/QA/**` only (plus this report). Product code was not edited. The temporary U3c worktree was removed.

## Results at a glance

| Item | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror --no-incremental` | 0 warnings, 0 errors |
| `dotnet test` Server.Tests | 713 / 713 passed (was 646 / 688 before migration) |
| `dotnet test` Harness.Tests | 284 / 284 passed |
| Main harness (current `Tools/Harness`), 3 runs on fresh servers | 80 / 80 / 80 (H3 timeout in all three, as the brief expects) |
| U3c harness (`refs/heads/claude/u3c-anon`, detached worktree), 3 runs | 100 / 100 / 100, exit 0 |
| Classifier AUC (position tracking at 20 Hz, ground truth) | 0.758 gated per-step CV; best single gated feature 0.730 (still-step fraction); 0.790 with lifetime/spawn; 0.798 stillness only |
| Server processes left running | none (the remaining dotnet processes are MSBuild node-reuse workers and Unity's VBCSCompiler) |

Harness setup: per run a fresh `--port P --sandbox` server and a fresh `--port P+1` normal server, both `--bots 0 --bind 127.0.0.1`, harness with `--host 127.0.0.1 --port P --h6-port P+1`. Logs and JSON: `artifacts/u2c-qa1-main-*` and `artifacts/u2c-qa1-u3c-*`. Main harness: H1 25/25, H2 20/20, H3 0/20 ("OperationCanceledException: Timed out or cancelled waiting for server/check completion", the check looks up survivors by Id), H4 15/15, remaining items full; `gatesPassed=false` only because of H3.

## Findings

No blockers and no majors. Three minors, all non-blocking.

### F1 (minor, integration, outside U2c ownership): the shipped Unity client cannot render an anonymous killer view
- Reproduction: read `Client/Assets/Scripts/World/WorldView.cs` `Accept` (avatars keyed by `player.Id`) and `World/SnapshotBuffer.cs` `Find` (first entry with `entry.Id == id`).
- Expected: a killer client shows every anonymous survivor entry (up to 15).
- Actual: all survivor entries in a killer-bound frame have `Id = 0`, so the client would create one avatar for Id 0, overwrite its `Status`, and interpolate it from the first Id-0 entry only. The other entries are never drawn.
- Evidence: the code above; server side proven by `Leak_KillerFramesAtByteLevel...` (38485 survivor entries, all Id 0). `Client/` is owned by U4c per `docs/briefs/U2c-a1.md`, so this is a hand-over item, not a U2c defect. U2c must not be merged into a client that still keys by Id without U4c landing.

### F2 (minor, accepted residual, measured): per-track statistics still separate phantom from real Normal tracks
- Reproduction: `Classifier_PositionTrackingAt20Hz_SeparatesPhantomFromRealNormalTracks_AucReportedWithGroundTruth` (position-matches anonymous killer entries against ground-truth labels at 20 Hz, builds tracks, 8 seeds with one White Noise and one Dummy user; excludes Lagging, Exposed, Rescuing).
- Data: 2081 tracks, 1011 phantom, 407 real Normal (33 association errors dropped).
  Single-feature AUC (0.5 = chance): median step speed 0.379, mean step speed 0.275, still-step fraction 0.730, straightness 0.676, end distance to terminal 0.325, gap count 0.494, final still run 0.663; report-only: lifetime 0.308, observations 0.309, spawn distance to terminal 0.351, path length 0.274.
  Cross-validated logistic AUC: gated per-step features 0.758, all features 0.790, stillness only 0.798.
- Expected (orchestrator ruling): per-step features at chance, accepted residual in per-track averages (D about 0.49) and in lifetime and spawn distance.
- Actual: consistent with the ruling. The residual is worth stating plainly: a tracker that watches an entry for a few seconds identifies a phantom about three times in four from still-step fraction alone. Per-step features (median step speed, afterimage tail) are close to chance by implementer KS tests; this test measures per-track aggregates. Not re-litigated; the test pins a regression ceiling of 0.85, so a worse residual fails the build.

### F3 (minor, observation): Ping Scanner bot killers react to Silent survivors by design
- With the new private RNG, bot-killer seeds 2 and 6 draw Ping Scanner, which exposes the nearest survivor whether or not it is Signal. `Bots_KillerBotIgnoresSilentSurvivors_ChasesWhatItSees` therefore had to neutralise the killer skill and clear forced exposure; the invariant it protects (bot decisions use only the anonymous view) still holds and passes for both seeds. Not a defect; recorded so nobody reads the neutralisation as a loophole.

## Brief sections 1 to 6

| # | Section | Verdict | Evidence |
|---|---|---|---|
| 1 | Migration of the 39 superseded tests (42 cases) | PASS | all 42 cases pass; none deleted (see table below) |
| 2a | Killer-bound leaks at byte level (Id, Yaw, CaptureCount, StatusTimer, non-Signal flag) | PASS | `Leak_KillerFramesAtByteLevel...`: 18438 frames over 10 bot matches, 38485 survivor entries, 5710 phantom entries, all four killer skills; `Leak_KillerView_IsIdenticalInShape...`: 1597 snapshots holding real and phantom entries, 5906 entries compared, every field except position identical; sweep `LeakSweep_EveryEncodedFrame_AllSkillCombinations` clean |
| 2a | Order uniformity (chi-square) | PASS | permutations (df 23, critical 49.7): 23.6 / 31.3 / 14.2 for seeds 1 / 2 / 3; killer rank (df 4, critical 18.5) 0.9 / 6.2 / 2.6; slot-0 repeats about 20 percent (2379, 2301, 2418 of 11900); phantoms not clustered: mean slot 3.502 vs 3.5 +- 0.008 over 6285 snapshots |
| 2a | Exact terminal progress | PASS | `Leak_TerminalProgress...`: killer sees only 0 or 100 at every byte; survivors still see exact percentages |
| 2a | Survivor Id in killer-bound SkillEffect | PASS | `Leak_KillerBoundSkillEffects...`: every survivor skill and every killer skill; CasterId is 0 or the killer's own Id |
| 2b | Behavioural classifier | PASS (with residual F2) | AUC figures above |
| 2c | RNG | PASS | xoshiro256** matches an independent reference implementation and the known vector, unbiased; private seed hook deterministic (killer stream, phantoms and order bit identical, the cosmetic `MatchSeedForTests` has no influence); stream replay from `MatchStart.Seed` finds no stream (`Seed_*` tests); source scan finds no `System.Random`, clock seeding or `Guid` in server logic (transport connection ids are opaque labels) and the hooks are unset in `Server/Program.cs` and `OfflineLink.cs` |
| 2d | SelfState | PASS | owner only, never about another player; first one arrives with the first WorldState, again after reconnect, not sent to bots or outside a match; on every jump and at least once per second (max gap 21 ticks), at most one per tick; values match server timers every tick (1312 frames compared per seed); `ExecutionGraceLeft` killer only, survivors always receive 0 |
| 2e | Entry cap 16 | PASS | decoders accept exactly 16 (also through the framing layer) and reject 17; phantom flood never exceeds 16 (observed max 11 in the flood scenario with the binding limit active in 10 frames); real Signals never cut; shown phantoms are not dropped when a hidden survivor turns Signal |
| 2f | Regressions (masking, capture, rescue, grace, Seq reset, sandbox isolation) | PASS | all U1, U2a, U2b QA tests pass after migration; `Regression_Sandbox_KillerViewIsAnonymousRealOnlyAndShuffled...` |
| 3 | Build, tests, harness x3, U3c harness x3 | PASS | table above; main harness 80 (H3 expected), U3c harness 100 |
| 4 | Look beyond the spec | F1 to F3 | see findings |

Coverage caveats (UNTESTED): Unity play-mode and EditMode tests were not run (client out of scope); real TCP behaviour of `S2C_SelfState` under packet loss is covered only by the in-memory transport and the harness.

## Migration of the superseded tests

No test was deleted; every invariant survived in re-targeted form. Method: a killer-bound survivor is matched by position (the survivor's own frame is ground truth) or by the server's test accessor `KillerViewSources`; private streams are replayed with `PrivateRandom`.

| Area | Re-targeting |
|---|---|
| `U2bHelpers.QaRig.KillerSees` | position based match instead of Id lookup |
| `U2aA2Tests`, `U2aAdversarialTests`, `U2aNetworkTests` | position-matching `Watch`, anonymous view; killer skill neutralised where a pair test needs a clean view |
| `U1AdversarialTests` | cap 8 to 16 (9 to 16 accepted, 17 rejected, 17 throws on encode, 16 round-trips) |
| `U2bSkillTests` | Dummy and White Noise rewritten: anonymous fakes, no borrowed Ids, caster independence, 5 fakes even in 1 v 1, 0.5 s stagger, 1.5 s lifetime |
| `U2bAdversarialTests`, `U2bInputSandboxBotTests` | killer-bot test uses the anonymous view; skill and forced exposure neutralised; non-vacuity bound relaxed for look-around pauses |
| `U2bSweepTests` | Id-free sweep with ground-truth labels; dummy bookkeeping extended by the 1 s afterimage tail; SelfState floats added to the legitimate-value set (a 5.0 s cooldown equals the 5.0 spawn coordinate bit for bit) |
| `U2bA2Tests` | seed tests replay streams through `PrivateRandom`; the probabilistic F6 test replaced by a stream-replay search |

## Test-harness notes for the implementer and orchestrator
- A match lasts 600 s = 12000 ticks; after that the last frame is stale. Long tests stop at 11900 ticks.
- In-memory peers that never drain their receive queue are disconnected after roughly 4000 ticks and taken over by bots (test artefact; the long loops now drain survivors).

VERDICT: PASS
