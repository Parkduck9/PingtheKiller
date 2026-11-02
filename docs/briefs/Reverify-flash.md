# Re-verification brief: Gemini Flash QA of units already merged

Status: **active** (2026-11-02, main `48abe4a`). The user asked for it ("Flash로 재검증").

**Why:** the units U2c, U3c, U4c, U4d, U2e, U3d and U4e were implemented by Claude Opus and QA'd by Claude Sonnet. CLAUDE.md requires QA from a different model family whenever one is available. Antigravity Gemini Flash was available the whole time and was never tried. This pass supplies the independent, non-Claude QA those units are missing.

**Who runs it:** Antigravity only.
- Tests, rules and harness: `agy -p ... --model gemini-3.8-flash-medium`.
- Screenshot and UI review: `--model gemini-3.8-flash-high`.

If Flash fails on quota or rate limits, retry once after a short wait, then fall back to `claude-sonnet-5-5-medium` via agy, and record the switch in the report header.

**QA rules (AGENTS.md §5):** QA never edits product code. It may add tests only under:
- `Server.Tests/QA/Reverify/`
- `Tools/Harness.Tests/QA/Reverify/`
- `Client/Assets/Tests/EditMode/QA/Reverify/`

Every finding gives:
- severity (blocker, major or minor);
- reproduction;
- expected vs actual;
- evidence (test name, log line, screenshot path).

Each area runs on its own branch from main `48abe4a`, in its own worktree, and commits only its new tests and its report there. Nothing merges to main without the user.

## Areas

### A. Server (U2c, U2e). Branch `claude/reqa-server`
- **Read:** `docs/briefs/U2c-a1.md`, `docs/briefs/U2e-a1.md`, `docs/qa/U2c-qa-a1.md`, `docs/qa/U2e-qa-a1.md`, and `Server.Core/Runtime/**`.
- **Attack these:**
  - killer-view anonymity at byte level;
  - shuffle uniformity;
  - phantom indistinguishability (the accepted AUC is ≤ 0.85);
  - private RNG independence from `MatchStart.Seed`;
  - SelfState ownership;
  - the line-of-sight contract: exact entry fields, per-recipient, spectators never, same-tick, edge and corner cases;
  - killer coordinates anywhere else in survivor-bound traffic.
- **Gate:**
  - `dotnet build PingTheKiller.sln -warnaserror --no-incremental`
  - `dotnet test PingTheKiller.sln`
- **Report:** `docs/qa/Reverify-server-flash.md`

### B. Harness (U3c, U3d). Branch `claude/reqa-harness`
- **Read:** `docs/briefs/U3c-a1.md`, `docs/briefs/U3d-a1.md`, `docs/briefs/U3d-a3.md`, `docs/qa/U3d-verdict.md`, `Tools/Harness/**` and `Tools/Harness/README.md`.
- **Attack:** look for any broken server that still scores 100. Build fakes for:
  - a survivor leak hidden in anonymous entries;
  - a killer entry while line of sight is blocked;
  - a stale tick;
  - killer coordinates inside unknown-Id frames or SkillEffect.

  Check the ±0.05 m ambiguous band for abuse, for example a server that leaks only near walls.
- **Gate:** build and tests as in A, plus 3 real-server runs per PLAN §5: fresh servers, `--threshold 100 --expect-los`, all servers stopped afterwards.
- **Report:** `docs/qa/Reverify-harness-flash.md`

### C. Client (U4c, U4d, U4e). Branch `claude/reqa-client`
- **Read:** `docs/briefs/U4c-a1.md`, `docs/briefs/U4d-a1.md`, `docs/briefs/U4e-a1.md`, `docs/ui/HUD_SPEC.md`, `docs/ui/SLOP_DEFINITION.md` §9, and the judgement and QA files of those units.
- **Attack:**
  - The killer client must never use survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or `Afterimage`.
  - The survivor client must draw the Killer only from the line-of-sight entry, with no extrapolation and a snap after a gap.
  - Check the camera rules, SelfState readouts, threat visuals, capture cue and elimination reasons.
- **Screenshots:** score the U4c, U4d and U4e screenshots under `docs/qa/U4*-screens/` with **flash-high** against SLOP §9 (≥ 85, no gate violations).
- **Gate:** Unity EditMode batch tests and a compile check:
  - `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/reverify-editmode.xml`
  - Never run two Unity instances on the same project folder.
- **Report:** `docs/qa/Reverify-client-flash.md`

## Report format
Each report has:
- a header naming the model that actually ran it;
- a table of each unit's spec sections marked PASS, FAIL or UNTESTED;
- the findings;
- the gate output counts.

It ends with `VERDICT: PASS` or `VERDICT: FAIL (n blockers, m majors)`. Claude Opus (orchestrator) judges the findings afterwards.
