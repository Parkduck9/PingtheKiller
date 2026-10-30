# Brief U3d — attempt 3 (different approach): tick coverage instead of edge tolerance

- **Implementer:** Claude Opus (quota fallback).
- **Base:** same spec, owned files and rules as `docs/briefs/U3d-a1.md`, with attempt 2's code in the working tree, uncommitted. Edit it.
- **Do not commit.**
- **Report:** `docs/qa/U3d-impl-a3.md`, ≤ 200 words.
- **This is the last attempt** per CLAUDE.md. Its approach must differ from attempts 1–2, as stated below.

## Why attempts 1–2 failed (`docs/qa/U3d-qa-a1.md`, `docs/qa/U3d-qa-a2.md`)
Survivor-bound frames whose tick never joins with the killer's own stream were either:
- tolerated: in attempt 1, everywhere; in attempt 2, within 6 tick numbers of the killer's range; or
- given inconsistent treatment: `Finish()` tolerates the edge, but the scan gets no allowance (F7, a false fail).

Keying the tolerance on tick **numbers** lets a server label leaking frames `Tick 0`, which falls just outside the killer range (F6, blocker). A tolerance of any size reopens the hole.

## New approach: no tolerance; the harness guarantees coverage by tick
**Contract clarification (orchestrator decision):** a correct server sends every `S2C_WorldState` tick to every connected human in the match. A survivor and the killer therefore receive the same tick sequence. A survivor-bound world whose tick the killer never receives is a server defect.

1. **Tick integrity per stream.** Inside every judged window (H3 capture, H6 whole scenario), each client's WorldState ticks must be strictly increasing with gap ≤ 2, as H2 already requires.
   - A frame that violates this fails the check, e.g. `tick sequence violated in survivor stream: 812 -> 0`.
   - This removes tick renumbering (F6) by construction.
2. **Coverage instead of tolerance.**
   - **Judged window:** it is defined by survivor-stream ticks.
   - **Killer frames:** the harness keeps them indexed by tick from before the window starts (a retained buffer, e.g. the last 2 s). It keeps reading the killer stream after the window until the killer's tick is ≥ the window's last survivor tick.
     - Use a bounded wait (`--timeout`). A timeout fails the check with evidence.
     - Never filter killer or survivor frames per reader by wall-clock `ReceivedAt` in a way that leaves edge ticks unjoined.
   - **Join rule:** every survivor world in the window must join.
     - A survivor world with **no** killer tick fails, whether or not it carries a killer entry: `survivor tick N never delivered to the killer`.
     - There is no "undecided" outcome in a finished window. Remove the 6-tick tolerance and the edge logic.
3. **One consistent verdict for the scan.**
   - After the join is complete, every entry has a final verdict: `Visible`, `Ambiguous` or leak.
   - The H6 leak scan runs only after that point. It grants offset allowance only for `Visible` or `Ambiguous`.
   - With no undecided state, F7 cannot recur.
4. **H6 capture covers all three clients from the first WorldState after MatchStart to the end of the GameResult drain.** `WireClient` buffers everything, so no client misses the first ticks. Verify the last ticks match across streams. If the killer stream ends earlier than a survivor stream, that survivor's extra ticks fail per item 2.
5. **Skew robustness (F8).** A correct server with up to 300 ms lag on any one stream must pass H3 and H6, because the harness waits for coverage instead of tolerating gaps. Add tests at +300 ms, killer and survivor separately.

## QA tests (QA-owned; do not edit)
Under the clarified contract, QA's `U3dH6KillerStreamStartsLateTests` (F7) uses a server that withholds the first 3 killer ticks. It now **must fail** H6 (`never delivered to the killer`). QA updates that test in round 3. List it as expected red if it fails as a must-pass. Every other test must be green, including QA's F6 tests `U3dH3EdgeTickZeroLeakTests` and `U3dH6EdgeTickZeroPerturbedLeakTests`, and all of U3/U3b/U3c/U3d.

## Gate
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings.
- `dotnet test PingTheKiller.sln`: green, apart from the listed F7 test.
- README: replace the tolerance text with the coverage rule and the contract clarification.
- The orchestrator re-runs the real-server gates: main, and U2e `57839b6` with `--expect-los`.
