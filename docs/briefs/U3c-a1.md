# Brief U3c — attempt 1: harness works with the anonymous killer view (U2c) and unknown packets

Implementer: Claude Opus subagent (quota fallback: Codex is out of credits, so this is not a failed attempt). QA: Claude Sonnet (a different model). Branch `claude/u3c-anon` (from main `2530684`) in this worktree. Do not commit.

Read first: `AGENTS.md`; `docs/briefs/U2c-a1.md` (server contract, §1, §5, §5b N9); `Tools/Harness/README.md`; all of `Tools/Harness/**` and `Tools/Harness.Tests/**` (including `QA/`); `Shared/Runtime/**`.

## Owned files
- `Tools/Harness/**`
- `Tools/Harness.Tests/**`, excluding `QA/**`, which is QA-owned; see "QA tests" below
- `docs/qa/U3c-impl-a1.md`

Never edit `Shared/`, `Server.Core/`, `Server/`, `Server.Tests/`, `Client/`, `PingTheKiller.sln` or `docs/PLAN.md`. U2c is being implemented in parallel on another branch and is **not** in this worktree. Its Shared changes, including `S2C_SelfState`, do not exist here yet.

## Target: the harness must pass against both server views
- **Today's view (main):** killer-bound survivor entries carry real Ids.
- **U2c anonymous view:** every killer-bound survivor entry has `Id = 0`, `Yaw = 0`, `CaptureCount = 0`, `StatusTimer = 0` and `Flags = Signal` only. Entry order is shuffled every snapshot. The killer's own entry keeps its real Id and `Role = Killer`. Killer-bound `TerminalState.Progress` is quantised to 0 or 100. The survivor-bound view is unchanged: all survivors with real Ids, never the killer.

## 1. H3: match survivors by tick and position, never by Id in the killer stream
- **Ground truth:** the harness survivor client's own stream holds every survivor's true position (bots included) per `Tick`. Index those frames by Tick.
- **Attribution:** each non-killer entry in a killer-bound WorldState at tick T is attributed to the survivor whose true position at the same tick T is nearest. A match must lie within a small epsilon, e.g. 0.05 m, because the positions should be bit-identical. An entry with no ground-truth match within epsilon is unexplained. Treat that as evidence in sandbox, where there are no decoys, and fail H3 with "unexplained killer-view entry".
- **Positive baseline (moving):** while the controlled survivor moves, at least one killer frame has an entry attributed to it, at the same tick.
- **Masking (silent ≥ 3.1 s):** no killer-view entry may be attributed to the stopped survivor. The existing raw X/Z byte scan over every killer frame stays as it is.
- **Robustness against bots (gate, so false fails matter):** a sandbox survivor bot that walks past within 0.5 m of the stopped survivor must not fail H3. Attribution to the bot's own true position handles this. Exact float equality between a bot and the stopped survivor is the only ambiguous case; if it happens, record it as evidence and do not fail. Never use a bare "entry within radius R of the stopped position" rule.
- **Tick alignment:** the killer and survivor streams may arrive in different orders. Buffer and join by Tick with a bounded wait. Ticks missing from the survivor stream are skipped, not failed, and the number of joined ticks must reach the existing coverage minimum (≥ 50).
- **Unchanged:** keep `KillerHidden` (no killer entry, by real Id or `Role = Killer`, in survivor-bound frames).
- Remove every lookup of a survivor by Id in a killer-bound frame across H2–H6. The killer finding its own entry by its real Id stays allowed. Add a self-test that enforces this, for example by running H3 and H6 against an anonymous fake.

## 2. H6
- Confirm and keep: H6 takes survivor positions and status only from the survivor streams. The killer locates itself by its real Id.
- **Killer-view progress (N9):** the per-frame rule "ExitOpen ⇒ at least `TerminalsRequired` terminals at Progress 100" must hold in every stream. Quantised killer progress (0 or 100) satisfies it. Do not add any killer-stream progress check other than "Progress ∈ {0,100} or the real percentage". Hack evidence (the 0..100 progress sequence) comes from the survivor streams.
- **Leak scan and `S2C_SelfState`:** once U2c merges, survivor-bound `S2C_SelfState` frames will decode with Shared. Their float fields are the player's own timers. Treat them as legitimate coincidence fields only if the packet type exists in Shared at compile time. Write that code so it compiles today: detect the packet by type name or reflection, or leave a single clearly marked seam. Document this as a known limit, like F4.

## 3. Unknown server packet Ids
- `WireClient`'s receive loop: a frame with a well-formed length whose PacketId is not handled by `PacketCodec.Decode` is **skipped**. It is not delivered as a packet. Count it per Id and expose the counts so every check can add "skipped unknown packets: id=0x10 ×N" to its evidence.
- Malformed frames (bad length), and decode failures of a **known** Id (bad payload, trailing bytes), still fail exactly as today. Distinguish the two cases without parsing exception messages, e.g. by checking the Id against the `PacketId` enum values the codec handles.
- **H6 leak scan:** an unknown-Id frame on a survivor-bound stream cannot be checked structurally. Scan its raw bytes at any alignment for killer X/Z bits and fail on any match with "killer coordinate bytes in unknown packet 0xNN (update Shared)". This is conservative on purpose; document it.
- **H3 raw scan:** it already covers raw bytes, so unknown frames are included automatically.

## Tests (`Tools/Harness.Tests/`, not `QA/`)
- **Anonymous-view fakes:** extend the existing test fakes (`LobbyServer`, `ScenarioServer`) with an `AnonymousKillerView` switch implementing the full U2c killer view. That means Id 0, Yaw 0, CaptureCount 0, StatusTimer 0, Signal-only flags, a per-snapshot shuffle and quantised progress. H3, H4, H5 and H6 must pass in both modes.
- **H3 broken fakes, anonymous mode, each must fail H3:**
  - the stopped survivor is still emitted anonymously at its exact position;
  - it is emitted anonymously at a rounded or offset position (≤ 0.05 m off);
  - an extra unexplained entry appears.
- **H3 false-fail guard:** a bot, or second scripted mover, that walks right past the stopped survivor (within 0.3 m) in anonymous mode must still let H3 pass. Add the same in Id mode.
- **Unknown packets:**
  - a fake that interleaves unknown-Id frames (e.g. Id 0x7E, 12-byte payload) in every stream must still pass H1–H6, with the skip counts in the evidence;
  - a fake sending a known Id with a bad payload must still fail;
  - a fake sending killer X/Z inside an unknown-Id frame to a survivor must fail H6.
- **QA tests in `QA/**`:** if any existing QA test fails only because a check no longer looks up survivors by Id (for example it asserts evidence text like `survivorId=… omitted`), do not edit it. List it in the report with the reason; QA updates its own tests. Every other test must be green.

## Gate
- `dotnet build Tools/Harness/Harness.csproj -warnaserror` and `dotnet test PingTheKiller.sln`, both green apart from listed QA-text tests.
- The orchestrator runs the real-server gate.

## Report
`docs/qa/U3c-impl-a1.md` (English, ≤ 250 words). Include:
- a header noting the quota fallback;
- files changed;
- test counts;
- which QA tests fail and why;
- judgement calls;
- what is unverified until U2c merges.
