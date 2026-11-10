# Brief Q3-skill-reject: the server says why a skill press did nothing (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable, as a separate run after you.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject`, branch `claude/q3-skill-reject`, from main 113b8e1.
  - LFS is already checked out.
  - Before any Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/minors-1110/Client/Library` into `Client/Library`. It is the nearest tree and saves a full import.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop. Launching an exe with flags is fine.
  - Never kill a process you did not start.
  - Leave Unity Hub and every other Unity editor alone. The user has editors open on `C:\Unity\MultiPlayGame\PingtheKiller\Client` and `C:\Unity\Number`.
- Read `AGENTS.md` first. Its wire format, server authority, masking and threading rules are absolute.

## Problem

HUD_SPEC §14.3 records a protocol gap.
- `GameServer.TryUseSkill` (`Server.Core/Runtime/GameServer.Skills.cs`) returns silently when it refuses a use. So does `RequestSkill`, for the sandbox and for an out-of-range id.
- The client infers a refusal only from a 0.5 s watch (`Client/Assets/Scripts/UI/SkillFeedback.cs`, around lines 398 and 474). It then shows a reason-less line: `Skill not used — the server did not accept it.` (Survivor) or `Not used · server did not accept` (Killer).
- The server knows the real reason. Send it.

**Pattern to follow:** U6g-rtt-probe (commit ffaa8a6) added `S2C_RttProbe` 0x0014 and `C2S_RttEcho` 0x0015 end to end. Use it as the template for Shared, codec, server, client, harness and tests.

## Spec

### S1. Shared

1. **New packet `S2C_SkillRejected` = 0x0016**, payload `[byte SkillId][byte Reason]`.
   - Add it to `PacketId`, `ServerPackets.cs` and `PacketCodec`.
   - A wrong payload length or an unknown `Reason` value is a `PacketFormatException`, the same as other packets.
2. **New `enum SkillRejectReason : byte`** in `Shared/Runtime/Game/`. At minimum:
   - `NotInMatch`
   - `NotYourSkill` (wrong id, or out of range)
   - `NotNormal` (Lagging, Eliminated or Escaped)
   - `Cooldown`
   - `NoTarget` (Ping scanner with no active Survivor; Emergency restore with no Lagging ally in radius)
   - `NoSwitchInProgress` (Firewall block)
   - `Passive` (Packet afterimage)
   - `Sandbox`
   - Number them explicitly, starting at 1; 0 is invalid.
   - Add a value only if you find a real refusal path that is not covered, and list it.
3. Keep `Shared/` netstandard2.1, C# 9, no UnityEngine, no LINQ.
4. Update every test that pins "0x0016 is unknown" or the highest packet id. `Server.Tests/Core/ProtocolV03Tests.cs` and the fuzz and golden-frame tests may do this.

### S2. Server

1. **Every refusal path sends exactly one `S2C_SkillRejected` to the caster only, with the true reason.** That covers both `RequestSkill` and `TryUseSkill`, including the `default` branches. The game-loop thread sends it through the session's send queue, the same way as `SendEffect`.
2. **Security.** A rejection must never tell a client anything its normal stream does not already tell it.
   - Killer: `NoTarget` for Ping scanner happens only when no Survivor is active. That is already derivable from the result rows and statuses, so it is fine.
   - Killer: `NoSwitchInProgress` must match what the Killer already derives in §6.1d. Verify that the Killer's stream really carries exit switch progress for every exit. If it does not, say so and propose how to map the case, for example to a less specific reason.
   - Add a test asserting that the Killer never receives a rejection whose content depends on a Silent Survivor's position.
3. **Flooding.** A client that spams `C2S_UseSkill` must not be able to make the server send more than one rejection per player per tick.
   - Do not touch the existing request de-duplication, if any; check how several requests in one tick behave today.
   - Test it.
4. **Cooldown edge.** The server's cooldown uses `TimeEpsilon`. A press refused for `Cooldown` while the client already showed Ready is exactly the case the line exists for.
5. **Bots.** Bots run inside `Server.Core` and send no packets. They do not need rejections. Check that they do not break, and that no rejection is ever sent to a bot (bots have no connection).
6. **Log.** One `WriteLog` line per rejection: `skill rejected player=.. id=.. reason=..`. If it is too noisy in the 20 Hz loop under spam, log only the first per player per second, and say what you did.

### S3. Client

1. **`NetClient` and `ClientApp`:** decode the new packet and route it to the skill feedback. It must be handled on the main thread, like the other packets.
2. **`SkillFeedback`:** a rejection replaces the timeout inference.
   - On a rejection for the own pending press, show the reason line at once, on the same line and with the same styling as §14.3. Disarm the 0.5 s watch.
   - Keep the 0.5 s watch only as a fallback, in case nothing arrives. Say whether it can still fire at all.
   - A rejection that arrives after an own effect for the same press is ignored.
3. **Copy (SLOP: short, concrete, no blame, no "error").** Proposed lines; adjust only if a SLOP rule or the HUD_SPEC length budgets need it, and say why.

   | Reason | Survivor line (14.1a, no bar) | Killer line (6.1c, `muted`) |
   |---|---|---|
   | Cooldown | `Skill still recharging.` | `Not used · still recharging` |
   | NoTarget | Emergency restore: the existing `Emergency restore needs a Lagging Survivor next to you.` | Ping scanner: `Not used · no Survivor to scan` |
   | NoSwitchInProgress | — | the existing `No Exit switch in progress` |
   | Passive | — | the existing `Packet afterimage is passive.` |
   | NotNormal | the existing `Skills are off while Lagging.` when Lagging; otherwise no line | — |
   | NotInMatch, NotYourSkill, Sandbox | `Skill not used — the server did not accept it.` | `Not used · server did not accept` |

   - Keep the client-side pre-checks that consume a press without sending: Lagging, passive, and others. Their lines must stay identical, so a server rejection and a local pre-check never show two different texts for the same case.
4. **HUD_SPEC §14.3:** update the table with a "Server reason" column, and replace the "Protocol gap" paragraph with the new behaviour. Mark the changes `(11/10, Q3)`. Keep the other gaps listed there: which exit Firewall will lock, White noise counts, the Dummy shown state. They stay out of scope.

### S4. Harness and tools

1. `Tools/Harness` must accept the new packet. Any "unknown packet" check (`Tools/Harness.Tests/QA/Reverify/UnknownPacketAndSkillEffectReverifyTests.cs`) must be updated, not deleted.
2. `KillerLeakScan` must treat `S2C_SkillRejected` as a Killer-bound packet and check that it carries no Survivor-derived data. It carries only `SkillId` and `Reason`, so this is a structural check.
3. The harness score and gates must stay at 100/100 with `--threshold 100 --expect-los`.

## Tests

1. **Server.Tests:**
   - A codec round-trip, plus malformed-length and bad-reason rejection.
   - One test per reason, through the real `GameServer` with a fake connection, as the existing skill tests do.
   - Exactly one rejection, sent only to the caster. No rejection on a successful use.
   - Flood: 50 requests in one tick give at most 1 rejection.
   - Killer privacy, as in S2.2.
   - Sandbox gives `Sandbox`.
2. **Unity EditMode:**
   - `SkillFeedback` shows the mapped line on a rejection and disarms the watch.
   - An effect after a rejection, and a rejection after an effect.
   - The copy table is pinned. No line contains "error" or "fail".
3. Keep every existing test green. Change an existing assertion only where it encoded the old protocol gap, and list each change.

## Gates (all must pass)

Commands:
```
dotnet build PingTheKiller.sln -warnaserror
dotnet test Server.Tests --filter "Category!=BalanceSweep"
dotnet test Server.Tests --filter "Category=BalanceSweep"
dotnet run --project Server -- --port 7777 --bots auto   (background), then 3x:
dotnet run --project Tools/Harness -- --host 127.0.0.1 --port 7777 --threshold 100 --expect-los --report docs/qa/Q3-runs/harness-N.json
dotnet test Tools/Harness.Tests     (about 18 min; run in background)
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml path> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
```

- **Before the first run**, check the real harness CLI flags and the `-executeMethod` name.
- **Server:** stop the server you started when you are done.
- **Live runs:** one offline Killer run and one offline Survivor run:
  ```
  Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role <killer|survivor> -ptk-shots <abs dir> -ptk-quit-after 200 -logFile <abs log>
  ```
  - Give the shell a timeout above 230 s.
  - Grep the logs for exceptions and for any `skill rejected` lines.
  - If the dev build has an auto-skill flag (look for `-ptk-auto-*` in the client), use it to force a rejection live and screenshot the line. If there is none, say so.
- **Unity file churn:** after each Unity run, `git status`. Revert files that changed only in line endings (`git diff --ignore-cr-at-eol --quiet -- <file>`).
- **Logs:** under `docs/qa/Q3-runs/`. Screenshots: under `docs/qa/Q3-screens/`.

## Owned files

- `Shared/Runtime/**` (new packet and enum, codec).
- `Server.Core/Runtime/GameServer.Skills.cs` and the minimal plumbing next to it.
- `Server.Tests/**`.
- `Tools/Harness/**` and `Tools/Harness.Tests/**`, only what the new packet needs.
- `Client/Assets/Scripts/Net/NetClient.cs`, `ClientApp.cs`, `UI/SkillFeedback.cs`.
- Client tests.
- `docs/ui/HUD_SPEC.md` §14.3, plus §4.8 and §6.1c lines if they reference the gap.
- `docs/qa/Q3-*`.

Say why if you touch anything else.

## Report

Write `docs/qa/Q3-skill-reject-impl-a1.md` in English, ≤ 300 words. Cover:
- the packet layout and reasons;
- every refusal path mapped to its reason;
- the result of the privacy check, including the Firewall exit-progress question;
- the flood behaviour;
- the client mapping and whether the fallback watch remains;
- the tests added or changed;
- every gate with counts;
- the live-run evidence;
- what is unverified;
- the risks.

Also write `docs/qa/Q3-skill-reject-commit-message.txt`, without an attribution line.
