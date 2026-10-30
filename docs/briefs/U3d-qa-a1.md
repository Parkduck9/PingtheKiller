# QA brief U3d — attempt 1: legal line-of-sight killer entry accepted, every leak still caught

QA agent: Claude Sonnet. The implementer was Claude Opus, a different model.

Read:
- `AGENTS.md`
- `docs/briefs/U3d-a1.md` (spec and contract)
- `docs/GDD.md` §2
- `docs/qa/U3d-impl-a1.md`
- `Tools/Harness/README.md`
- the code under `Tools/Harness/**` and `Tools/Harness.Tests/**`

## Allowed writes
- `Tools/Harness.Tests/QA/U3d/**` for new tests.
- Existing `QA/**` tests whose expectation is made obsolete only by this spec change, for example a test asserting that any killer entry in a survivor world fails. Never change what a test proves, and list every such edit.
- `docs/qa/U3d-qa-a1.md`

Do not commit, switch branches or touch git stash.

## Main job
Make sure the new allowance cannot be abused, and that the harness does not fail a correct line-of-sight server.

1. **The harness's own line-of-sight test**, compared with an independent reference you write in the test:
   - segment through a wall, touching an edge, touching a corner, parallel and flush against a face;
   - zero-length segment, i.e. a survivor standing on the killer;
   - pairs on the ambiguous 0.05 m band, which must be counted as skipped, never as pass or fail.
   - Fuzz at least 100k random pairs over `FixedMap` against your reference, with no disagreements outside the ambiguous band.
2. **False passes. Each of these must fail H6, or H3 where relevant:**
   - the entry is sent while line of sight is blocked;
   - it is sent to a spectator (Escaped or Eliminated recipient);
   - one survivor's line of sight reveals the killer to another;
   - it carries a stale (previous-tick) position, or a position off by more than 0.05 m;
   - it has non-zero Flags, StatusTimer or CaptureCount;
   - it has a wrong Role, or the right Role under a different Id;
   - killer X/Z also appears in a SkillEffect, an unknown-Id frame or any other field while the entry is legal;
   - the entry is duplicated in the same frame;
   - it is sent on a tick the killer's own stream never delivered (unknown, so it must not pass silently);
   - it is sent only on ambiguous-band ticks, and exploited, e.g. leaked when the segment is 0.04 m inside a wall.
3. **False fails. These must pass:**
   - a contract-correct line-of-sight server in sandbox (H1–H5) and normal mode (H6), with `--expect-los` on and off;
   - killer and survivor streams skewed by up to 200 ms;
   - the killer standing exactly at a wall corner;
   - several survivors with different line-of-sight results in the same tick.
4. **`--expect-los`:**
   - off: 0 visible entries is fine;
   - on: H6 fails if no legal visible entry or no blocked-and-absent tick is observed, and passes against a correct line-of-sight fake;
   - the evidence counts are present and correct.
5. **Regression:**
   - the U3, U3b and U3c QA suites still mean what they meant, including H3 masking under the anonymous killer view;
   - `dotnet test PingTheKiller.sln` ×3 plus one loaded run, reporting any non-deterministic outcome.

## Rules
- Never weaken, skip or delete a failing assertion.
- A false pass on a leak is a **blocker** for gate H3 and **major** for H6.
- A false fail against a contract-correct server is **major**.
- Every finding needs: severity, reproduction (test name), expected vs actual, evidence.

## Report
`docs/qa/U3d-qa-a1.md` contains:
- a table: fake → expected → actual;
- the line-of-sight fuzz results;
- the stress counts;
- the findings.

Its last line is exactly `VERDICT: PASS` or `VERDICT: FAIL (<reason>)`.
