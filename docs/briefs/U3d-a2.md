# Brief U3d — attempt 2: fix list on top of attempt 1

Implementer: Claude Opus (quota fallback). Same spec, owned files and rules as `docs/briefs/U3d-a1.md`. Attempt 1's code is in the working tree, uncommitted; edit it rather than starting over. Do not commit. Report: `docs/qa/U3d-impl-a2.md` (≤ 200 words).

Source: `docs/qa/U3d-qa-a1.md` (VERDICT: FAIL). QA tests live in `Tools/Harness.Tests/QA/**` and are QA-owned. Do not edit them, except as stated in item 1.

## 1. F1 — blocker for H3, major for H6: unknown-tick leak
`KillerSight.Judge` returns `Unknown` with the entry index when the killer's own tick is not known. `SightJoin.Finish()` only counts it. The H6 leak scan then allows that entry's X/Z. A server whose survivor-bound frames carry ticks the killer stream never sees can therefore show the killer through walls and still pass both H3 and H6.

Fix:
- **Undecided ticks inside the killer's range fail.** A survivor-bound world that contains a killer entry, whose tick stays undecided at `Finish()`, and whose tick lies inside the killer stream's observed tick range [first, last] is a leak. Fail with evidence such as `killer entry on tick=N that the killer's own stream never carried`. A correct server sends the killer every tick.
- **Edge ticks are tolerated.** Ticks outside that range, at the window edges, stay `Unknown` and are counted. Reserve a tolerance of a few ticks; QA observed 3–4 under a 200 ms skew. Choose a value from evidence and document it.
- **No offset allowance for undecided entries.** The leak scan must never allow an entry's offsets when its sight verdict is `Unknown` or not yet decided. Only a final `Visible` or `Ambiguous` verdict grants the allowance. If the scan runs before the join is final, re-run it, or defer it until `Finish()`.
- **QA pin tests.** QA pinned this gap as `Gap_…` tests that currently pass. After your fix they will fail, which is expected. List them in your report; QA flips them in round 2. Every other test must stay green.

## 2. F2 and F3 — accepted, no code change
- **F2:** the ambiguous band can be exploited by a server that always shows the killer standing 0.03 m inside a wall. Spec item 1 forbids deciding inside the band. Document it under "Known limits" in the README.
- **F3:** pre-existing. H6 fails at the contact hold when the killer stream lags by ≥ 100 ms. It is not related to line of sight. Document it under "Known limits".

## Gate
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings.
- `dotnet test PingTheKiller.sln`: green, apart from the listed QA `Gap_` tests.
