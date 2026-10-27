# QA brief U3b — round 2 (attempt 2)

Same QA agent, rules and allowed writes as `docs/briefs/U3b-qa-a1.md`, plus `docs/qa/U3b-qa-a2.md` (new report). Read `docs/briefs/U3b-a2.md` and `docs/qa/U3b-impl-a2.md`.

1. **Contract change:** U2b executes only on **leave-and-re-enter**. On capture the survivor is disarmed. It is re-armed only on a tick where the killer is outside 1.2 m, and execution happens only on contact while armed. The next U2b attempt adds a **2 s post-capture execution grace**. Update `QaRulesServer` in `QA/U3b/` to this rule, with a grace switch, and keep a knob for the old literal rule as a broken fake that H6 must fail. Your correct controls must pass with the grace off and on.
2. Re-run all your attempt-1 fakes against attempt 2. F1, F2 and F3 must now be fixed: re-verify F1 on your own fake, now that the capture step works.
3. Hunt for new false passes and false fails in the changed code:
   - the 2 s contact hold;
   - the escape-evidence rule (escaper Escaped, or within 2.5 m of the exit);
   - the per-frame ExitOpen/terminals rule;
   - the structural leak scan: decode/re-encode equality, field-level comparison, and the any-alignment scan of string bytes. Probe for clean frames being flagged.
4. Keep the `KnownLimit` tests that characterize the accepted limits F4–F8. They may need updating to the new rule, but never weaken a check that should fail.
5. Stress: `dotnet test PingTheKiller.sln` ×3, plus one loaded run.

Report: `docs/qa/U3b-qa-a2.md`, with the last line `QA-PASS` or `QA-FAIL`.
