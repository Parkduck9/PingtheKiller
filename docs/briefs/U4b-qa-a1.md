# QA brief U4b — attempt 1 (code, tests, screenshots)

QA: Claude Sonnet. Implementer: Claude Opus subagent, a different model.

## Read
- `AGENTS.md` §5
- `docs/briefs/U4b-a1.md` (the spec under test)
- `docs/qa/U4b-impl-a1.md`
- `docs/ui/SLOP_DEFINITION.md`
- `docs/GDD.md` §3, §4, §6.2, §7
- `docs/qa/U4-judgement.md` (the follow-ups U4b must close)
- `Server.Core/Runtime/**` (the API the client hosts)
- all of `Client/Assets/Scripts/**`
- every PNG in `docs/qa/U4b-screens/`, opened with the Read tool

## You may create/edit ONLY
- `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4b-qa-a1.md`

No product code. Do not run Unity, the game or the server: the orchestrator runs batch tests, and a Unity batch run may be using `Client/` now, so do not touch `Client/Library`, `Client/Temp` or `Client/Builds`. Do not commit.

## Independent EditMode tests, written from the spec
1. **Offline link end to end.** Use the real `GameServer` thread. Every wait has a timeout ≤ 5 s; dispose in `finally`.
   - JoinResult, then LobbyState `IsOffline = 1`, local player is host.
   - SetBots 1 → exactly 1 bot; SetBots 7 → 7 bots; SetBots 0 is clamped to at least 1v1; SetBots 9 is clamped to at most 1v7.
   - SetRole Killer → StartMatch → MatchStart Role Killer → at least 3 WorldStates whose `Tick` increases.
   - Packets are dispatched only inside `Pump()` on the calling thread.
   - Dispose → Disconnected via Pump, and the server game thread ends within 2 s.
   - The transport is `InMemoryTransport`.
2. **No socket on the offline path, structural.**
   - The offline types never construct `TcpClient`, `Socket`, `TcpListener`, `TcpTransport` or `NetClient`. Use reflection over the client assembly's method bodies only if it is reliable; otherwise use a code-read finding with file:line.
   - The orchestrator already proved the runtime side: the built player had 0 TCP and 0 UDP endpoints during an offline match.
3. **Connection messages.** Refused, timeout and lost each produce the fixed sentence. Feed an exception whose `Message` is Korean or other localized text and assert that text never appears in the output.
4. **Result copy.** All 3 reasons; the local outcome (escaped / eliminated / survivor-side loss / killer view); escaped names with a fallback to `Player <id>`; empty escaped list; unknown reason byte does not throw.
5. **Interact / UseSkill edges.**
   - E press → exactly one Active=1; hold → no repeats; release → one Active=0.
   - Focus loss, cursor release, match end and disconnect each force one Active=0 only if Active=1 was sent.
   - Killer → no Interact.
   - Q → UseSkill with the MatchStart SkillId; passive killer skill (2) → none; not Normal → none.
6. **HUD copy.**
   - Lagging countdown rounding: 42.01 → 43 s, 0.2 → 1 s.
   - Escaped and Eliminated text.
   - Terminals line with Progress = 100 counting as complete and 99 not.
   - Skill-name table for every (role, id), plus an unknown id.
7. **Map.** World geometry comes from `FixedMap` (wall count, exit and terminal positions), and no hard-coded map copy remains.
8. **DevArgs.** `-ptk-offline` and `-ptk-preview-result` (valid, invalid, missing value); flags stay inert when absent.

Keep all your earlier QA tests green. If one is now wrong by design, report it; do not edit its assertion silently.

## Review
- PASS/FAIL per spec bullet of U4b-a1 §1–§5, with file:line evidence.
- Threading:
  - main thread never waits on the game thread
  - `OfflineLink.Dispose` from `OnDestroy`/quit leaves no running thread
  - the link is disposed on Leave, on a second Offline, and on Join after Offline
- Screenshots. Check each PNG for:
  - wrap, overlap or clip
  - SLOP §2–§6
  - role-specific HUDs
  - no information leak: the survivor never sees the killer; the killer sees no Silent survivors
  - result screen not a generic banner
  - the preview label on synthetic results
- Every on-screen string, quoted.
- Full SLOP §9 score table with gates.
- List what is pending until U2b merges and what needs a human.

## Report `docs/qa/U4b-qa-a1.md`
Findings with severity, repro, expected, actual and evidence. Then the spec table and the SLOP table. The last line is `QA-PASS` or `QA-FAIL`.
