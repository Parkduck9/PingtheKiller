# QA brief U2b — attempt 1

QA: Claude Sonnet (implementer was Claude Opus; Codex out of credits, Gemini headless blocked). Branch `claude/u2b-rules` in the main checkout. Do not commit. Do not edit product code (`Shared/`, `Server.Core/`, `Server/`). You own `Server.Tests/QA/**` and `docs/qa/U2b-qa-a1.md`.

Read: `docs/briefs/U2b-a1.md` (spec, rules 1–14), `docs/qa/U2b-impl-a1.md` and `U2b-impl-a2.md` (implementer reports and judgement calls), `docs/GDD.md` §3–§4 and §6.3, `AGENTS.md` §5, then the code under `Server.Core/Runtime/`.

## Tasks
1. **Migrate superseded QA tests.** Two U2a QA tests fail because U2b changed behaviour legitimately:
   - `U2aAdversarialTests.KillerHidden_MovingHumanKiller_*`: a bot's TrafficOverload slows the killer.
   - `U2aA2Tests.BotAndHumanSurvivors_NeverMoveWhileAbsent*`: a Lagging survivor moves and is executed in the same tick.

   Keep what each test protects. Run it under `ServerOptions.Sandbox = true` where the U2a invariant is the point, or tighten the assertion to the U2b rule. Never just delete it.
2. **Adversarial tests** in `Server.Tests/QA/U2bAdversarialTests.cs` (deterministic `Tick(dt)`, several seeds). Try to break:
   - **Leaks:**
     - The killer's X/Z bytes appear in any survivor-bound frame (WorldState, SkillEffect, GameResult, LobbyState) during long bot matches with every skill combination.
     - An absent survivor's real X/Z appears in a killer-bound frame. Exclude legitimate cases: an afterimage at the last seen position, and a DummyPacket placed at the cast position. List each exclusion and why.
     - A decoy can be told apart from a real entry: field pattern, flags, an Id that cannot be absent, duplicate Ids, or ordering.
   - **Rules vs GDD/brief:**
     - Capture, Lagging timers, execution and the 2 s invulnerability.
     - Rescue: continuity, multiple rescuers, and a rescuer who becomes Lagging mid-rescue.
     - Hacking: the co-hack cap, progress kept after interruption, and Lagging hackers.
     - Exit: opening and the FirewallBlock lock.
     - Win order within one tick: escape and all-down in the same tick, and escape at the exact time-up tick.
     - GameResult is sent exactly once, then the server returns to the lobby and a second match works.
   - **Skills:** every skill's duration and cooldown. Also cast while Lagging, wrong id, passive id, precondition failure (no cooldown consumed), and Overclock stacked with TrafficOverload.
   - **Bots** must not use hidden information. For example, a survivor bot must not steer by the killer's position, only by ThreatLevel. A killer bot must only chase what its masked view shows.
   - **Sandbox:** no capture, skills, hacking or match end before 600 s. Sandbox can never be on by default.
   - **Input:** NaN/Infinity in every client float field, plus out-of-range bytes that pass the codec. Neither may throw or disconnect.
   - **Stability:** a 600 s simulated match with 1 human and 7 bots, 10 seeds. It must end exactly once and produce no NaN positions or exceptions.
3. **Run:**
   - `dotnet build PingTheKiller.sln -warnaserror`
   - `dotnet test PingTheKiller.sln`
   - The real-server harness 3 times. Each run uses a fresh server: `--port <p> --bots 0 --bind 127.0.0.1 --sandbox`. Expect 90/100 with gates passed.
4. **Decoy Id tracking (raised by the HUD spec review):** a modified killer client could track each Id over time and spot a decoy when the same Id jumps to a distant point, or appears while its real owner was just seen elsewhere. Measure how often this happens in seeded bot matches, and say whether it reveals more than the decoy rules (rule 9) intend. Treat this as a finding with a recommendation, not a code change.
5. **Look beyond the spec.** Report anything wrong that the brief did not mention: races between the game-loop thread and transport callbacks, unbounded collections, per-tick allocations that grow with match length, and similar.

## Report
`docs/qa/U2b-qa-a1.md` (English). For each finding give: severity (blocker/major/minor), reproduction, expected vs actual, and evidence (test name). Then give a table of rules 1–14, each marked PASS, FAIL or UNTESTED, with the final test counts and harness totals. Write the verdict line as either `VERDICT: PASS` or `VERDICT: FAIL (n blockers, m majors)`.
