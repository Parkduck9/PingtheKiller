@AGENTS.md

# CLAUDE.md — Orchestrator rules (Claude Opus)

Claude plans, briefs, supervises and judges. Claude does not write product code itself except for trivial one-line fixes during review.

## Model routing

| Work | Primary | Escalation (after 2 failed attempts) |
|------|---------|--------------------------------------|
| .NET server, framing, threading | `codex exec -m gpt-6.1-sol` (effort high) | `gpt-6-astra` |
| Unity C# gameplay / netcode client | `codex exec -m gpt-6.1-sol` (effort medium–high) | Claude Opus subagent |
| Boilerplate, small fixes | `codex exec -m gpt-6-luna` | `gpt-6.1-sol` |
| UI build (after slop doc) | `codex exec -m gpt-6.1-sol` | — |
| QA: tests, fuzz, harness runs | `agy -p --model gemini-3.8-flash-medium` | `claude-sonnet-5-5-medium` via agy |
| QA: visual/UI review from screenshots | `agy -p --model gemini-3.8-flash-high` | `codex exec -m gpt-6.1-terra` |

Argon is not used for QA (user decision 2026-10-27: token cost). If a Flash call fails for quota/rate-limit reasons, retry once after a short wait, then fall back to the escalation column; a capacity switch does not count toward the 3-attempt limit.
| Read-only lookups | Claude Haiku subagent | `gemini-3.8-flash-low` |
| Supervision / final judgement | Claude Opus (this session) | Fable for architecture dead-ends |

The implementer and the QA reviewer of the same unit must be different model families.

## Loop engineering (per unit of work)

1. Claude writes an English brief: goal, owned files, spec bullets, acceptance tests, harness items affected.
2. Implementer runs. Claude reads the diff + report.
3. QA runs tests/harness and writes `docs/qa/<unit>-<attempt>.md`.
4. Claude judges: spec bullets PASS/FAIL, harness score vs threshold, plus anything wrong that the brief did not mention (Claude must look beyond the brief).
5. Retry policy — **max 3 attempts per unit:**
   - Attempt 1–2: same approach, with Claude's concrete fix list.
   - Attempt 3: a **different approach** (different design or escalated model), stated explicitly in the brief.
   - After attempt 3 fails: stop and report to the user in Korean (what failed, evidence, options).
6. Commit only after Claude's PASS.

## Cost guards

- **Split by default (user decision 2026-10-27):** any independent piece (no shared files, no dependency) goes to a worker — subagent, side session, Codex, or QA — in parallel. Writers never share files.
- Tell the user whenever more than 2 agents run concurrently.
- If token use spikes unexpectedly, first check for unintended multi-agent spawning (subagents spawning subagents, `/loop`, workflows).
- Context: auto-compact window is set to 450k in user settings; keep it in the 400k–750k range.
