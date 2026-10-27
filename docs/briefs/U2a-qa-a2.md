# QA Brief U2a — attempt 2: re-verify after fix list

QA agent: Claude Sonnet (independent of the implementer, Codex gpt-6.1-sol). Same ownership and rules as `docs/briefs/U2a-qa-a1.md` (only `Server.Tests/QA/U2a*` and `docs/qa/U2a-qa-a2.md`; never weaken a failing assertion; do not commit).

Read `docs/briefs/U2a-a2.md` (the fix list = spec changes), `docs/qa/U2a-impl-a2.md`, your previous report `docs/qa/U2a-qa-a1.md`, and the changed code.

## Tasks
1. Re-run your whole attempt-1 suite. `Masking_MovingSurvivorIsNeverOmittedEvenWithoutFurtherInputs` must pass **unchanged**.
2. `Seq_UintWraparound_IsNotHandled_SpecLiteralBehaviour` encoded attempt-1 behaviour; the spec now requires serial-number comparison (a2 fix 4). Rewrite it to assert the new spec (wrap accepted; stale-after-wrap dropped; a jump of exactly 2^31 is your call — state it) and say so in the report.
2b. Five more of your attempt-1 tests now fail because they assume one input keeps a player moving for longer than 0.15 s, which a2 fix 1 forbids: `InputSanitizing_NeverNaN_NeverFasterThanRoleSpeedTimesDt` (3 cases: oversize vectors checked after 6 ticks of a single input), `RateCap_AtMost60InputsPerSecondAccepted_ThenWindowReopens` (one burst, then expects ~0.95 s of motion), `Seq_OutOfOrderAndDuplicateAreIgnored_FirstSeqZeroAccepted` (third tick lands exactly at the 0.15 s expiry). The orchestrator verified this. Rewrite each to keep its intent under the new rule (e.g. observe per-tick step within the hold, or keep resending fresh valid inputs alongside the adversarial ones), and list each rewrite in the report. Do not drop any check that is still in the spec.
3. New adversarial tests for each a2 fix:
   - Fix 1: one input then silence → survivor stops within 0.15 s (+1 tick) and is never moving while absent from the killer frame; 20 Hz resend → continuous full-speed movement and continuous Signal; inputs at 5 Hz → movement stutters but never hidden-while-moving; killer held direction also expires.
   - Fix 2: inject a fault via the public API (e.g. throwing `Log` subscriber) with `Start()` running → loop keeps ticking (clients still receive WorldState), no process crash.
   - Fix 3: throwing `ConnectionAccepted` subscriber on a real `TcpTransport` → that client closed, next client accepted normally; `Stop()` does not throw.
   - Fix 5: token takeover while the old TCP/in-memory connection is still open → old closed, new bound to same PlayerId/position; a connection that never Joins is closed after 10 s of ticks (Ping before Join answered); another player's token never takes over.
   - Fix 6: zero-width / bidi / 200-char / emoji-at-the-byte-limit nicknames → sanitized, encoded `S2C_LobbyState` never throws.
   - Fix 8: for many seeds and every killer index, initial ThreatLevel of all survivors is 0.
   - Fix 7 (SIGTERM): code review only.
4. Re-review for anything new: races introduced by the takeover/join-timeout code (bindings touched off the loop thread?), unbounded growth, Linux-incompatible APIs.

## Run and report
`dotnet build PingTheKiller.sln -warnaserror`, `dotnet test PingTheKiller.sln` (run the QA suite at least 3 times for flakes). Report `docs/qa/U2a-qa-a2.md`: header with model and counts; status of every attempt-1 finding (fixed / still open + evidence); new findings `[blocker|major|minor] title — repro — expected vs actual — evidence`; final line exactly `QA-PASS` or `QA-FAIL` (any blocker or major → QA-FAIL).
