# U2e implementation report, attempt 1 (Claude Opus; quota fallback because Codex has no credits)

**Changed**
- `Server.Core/Runtime/Map/FixedMap.cs`: added `Wall.IntersectsSegment` (a slab test against a closed box) and `FixedMap.SegmentBlocked`, which allocates nothing and treats a zero-length segment as not blocked.
- `Server.Core/Runtime/GameServer.Visibility.cs`: when a recipient survivor is Normal or Lagging and the segment is clear, `BroadcastWorld` adds the killer entry to that survivor's WorldState. It uses the same tick's positions. Flags, StatusTimer and CaptureCount are 0. Also added the test hook `PlaceForTests`.
- New tests: `Server.Tests/Core/LineOfSightTests.cs` (29 tests) and `KillerSight.cs` (a test helper). LineOfSightTests includes a random cross-check of `SegmentBlocked` against an exact orientation oracle.

**Re-targeted tests.** Old invariant: the killer never appears in a survivor frame. New invariant: the exact killer entry appears only when line of sight is clear. Blocked frames keep the original checks.
- Core:
  - `DecoyAndLeakTests.LeakSweep_*`, renamed to `..._BlockedSurvivorFrames...`
  - `SimulationTests.SurvivorReceivesExactKillerEntryInLineOfSight...` (renamed)
  - `BotMatchTests.OneHumanAndSevenBots...`
  - `AttemptTwoFixTests.RandomInputPatterns...`: now uses survivor entries only.
- QA:
  - `U2aAdversarialTests.KillerHidden_MovingHumanKiller_*`: renamed, and a phase behind the wall was added.
  - `U2aAdversarialTests.KillerHidden_BotKiller_*`: its scenario never produces a blocked frame.
  - `U2aAdversarialTests.Lobby_NinthHuman...`
  - `U2bSweepTests.LeakSweep_*`

**Verified**
- `dotnet build -warnaserror --no-incremental`: 0 warnings.
- `dotnet test`: Server 742/742, Harness 386/386. `H6FailsWithMatchingEvidence` failed once on time under load and passed on rerun.

**Harness** (3 runs, each with a fresh sandbox server and a fresh normal server)

| Run | H1 | H2 | H3 | H4 | H5 | H6 | Total |
|---|---|---|---|---|---|---|---|
| 1–3 | 25 | 20 | 0 | 0 | 10 | 0 | 55 |

- H3 and H6 fail with `Killer 1 leaked to survivor at tick=1279` and `tick=1324/1326`. Both are legal line-of-sight entries, which U3d will fix.
- H4 fails as a cascade: H3 throws and leaves its `SendMovingAsync` task running. A fresh run with `--only H1,H2,H4,H5` gives 70/70 and H4 passes.

**Unverified:** Unity compilation and bot use of line of sight (optional; not done).
