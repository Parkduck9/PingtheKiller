# Brief Q1-playcheck-polish, attempt 1

Quota switch (CLAUDE.md fallback, 2026-11-08): Codex is out of credits and the agy Gemini Flash sign-in is expired. Implementer: Claude Opus subagent. QA: Claude Sonnet subagent (different model). Record this in the report header.

Worktree: `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\relaxed-leavitt-607e3e` (branch `claude/relaxed-leavitt-607e3e`). Do not commit; the orchestrator commits after judging.

## Goal
Fix the visible nits from the 2026-11-05 play check (findings 3-6). Evidence lives in the MAIN checkout (untracked there): `C:\Unity\MultiPlayGame\PingtheKiller\docs\qa\playcheck-1105.md`, `...\docs\qa\playcheck-1105\surv\*.png`, `surv.log`, `killer.log`. Read them first (`surv/lobby.png`, `surv/v2-sync-check.png`, `surv/survivor-rescue.png`).

Other sessions edit these areas in parallel: Q2 (Killer edge labels: `Killer/EdgePlacement.cs`, `Killer/TagLayout.cs`), Q4 (sounds: `UI/RulesV2/RulesV2Wiring.cs`, likely `SurvivorCueSynth` in `UI/SurvivorFeedback.cs`), Q6 (survivor in-match menu: `UI/SettingsScreen.cs`, `UI/ClientScreens.cs`, `World/RoleCamera.cs`). Do not touch those files/regions. Also no `Shared/`, no `Server*/`, no bots.

## Owned files
- `Client/Assets/Scripts/UI/SurvivorFeedback.cs` — **only the `CaptureWatch` class** (the `EliminationReason` enum may change too). Keep the hunk small; Q4 may edit `SurvivorCueSynth` in the same file.
- `Client/Assets/Scripts/UI/SurvivorReadout.cs` — only the capture tag line (~line 166) and anything it needs.
- `Client/Assets/Scripts/UI/HudCopy.cs`, `UI/LobbyCopy.cs`, `UI/ResultCopy.cs`, `UI/RulesV2/RulesV2Copy.cs`, `Killer/KillerHudCopy.cs`
- `Client/Assets/Scripts/World/CharacterViews.cs`
- `Client/Assets/Scripts/ClientApp.cs` — only the lobby-state handling (bot names) and the elimination log line if needed.
- `Client/Assets/Scripts/Net/OfflineLink.cs` — only if needed for bot names (probably not).
- New pure helper files under `Client/Assets/Scripts/UI/` or `World/` are fine.
- `docs/ui/HUD_SPEC.md` — only §6.1a line 2 wording (item 4).
- Tests: `Client/Assets/Tests/EditMode/` (new file `PlaycheckPolishTests.cs` preferred; update existing assertions that encode the old copy).

## Spec
1. **Elimination copy matches what the player saw (finding 3).** Server `CaptureCount` counts Lagging *stages*, not captures: a first stage that runs out escalates to stage 2 and raises `CaptureCount` to 2 without a new capture (GDD-v2 §3/§4). In the play check: captured at 48 s, stage ran out, rescued, captured once more at 154 s → line "Disconnected: caught a third time." after 2 real captures.
   - `CaptureWatch` counts the captures it actually observed (Normal→Lagging transitions after the baseline) and whether it observed an escalation.
   - The ThirdCapture reason line never claims more captures than observed. Use the observed count when the watch saw the whole match (baseline at Normal with `CaptureCount` 0); otherwise a count-free line that is still true. Suggested lines (adjust for SLOP §4 and the meter width; numerals OK): `Disconnected: caught 2 times, no Lagging stage left.` / `Disconnected: caught 3 times.` / count unknown: `Disconnected: caught with no Lagging stage left.`
   - Same bug, meter tag (`SurvivorReadout.cs` ~166): `CaptureCount >= 2` shows "Captured twice" even after one capture + escalation. Replace with a tag that is true for any history and actionable, e.g. count 1 → `Captured once`, count ≥ 2 → `Next capture disconnects`. Check it fits the meter tag slot (HUD_SPEC sizes) — measure, don't guess.
   - `Collapse` and `TimedOut` lines unchanged. The `[client] eliminated, reason=...` log line still prints.
2. **No stacked bodies (finding 4).** Client-only visual separation in `CharacterViews.Render`: when two rendered character bodies (Survivors, and the Killer model if drawn) are closer than about one body width, push their *drawn* positions apart symmetrically to that minimum (pure function, e.g. `VisualSeparation.Resolve(positions, minDistance)` with deterministic tie-break for exactly coincident points, e.g. by player id). Rules: authoritative positions, snapshots, input and anything sent to the server are untouched; the local player's own avatar should move as little as possible (prefer pushing the other body; the camera follows the own character, so do not make it jitter); offsets are bounded (≤ minDistance) and smooth (no frame-to-frame popping — e.g. ease the offset or derive it purely from current positions so it is continuous); Lagging/hidden bodies follow the same rule only if drawn. Do not move world labels/tags that are positioned from authoritative positions unless it is trivial to keep them on the body.
3. **Contiguous bot names (finding 5).** The server names bots `"Bot " + PlayerId` (`Server.Core/Runtime/GameServer.Lobby.cs:135`), so with the player in slot 4 bots read 1,2,3,5,6,7,8. Fix client-side, no server change: a pure helper (e.g. `LobbyCopy.BotNames` / `ContiguousBotNames(S2C_LobbyState)`) renames bots whose nickname is exactly the server default `Bot <their PlayerId>` to `Bot 1..N` by ascending PlayerId among those bots; bots with any other nickname (a disconnected human who became a bot keeps their name) are left alone, and numbering must not collide with such a name. Apply it once in `ClientApp` where `S2C_LobbyState` arrives, before the state is stored/forwarded, so the lobby list, in-match names (`NameOf`), and the result screen (`RulesV2Wiring.Nickname(lobby, id)`) all show the same name. Deterministic: every client computes the same names from the same lobby state.
4. **One terminal-count format (finding 6).** Use `Terminals 1/3` for both roles (`HudCopy.TerminalsLine` and `KillerHudCopy.Terminals`). The Survivor info block (HUD_SPEC §4.3, inner 144 u) cannot fit `Terminals 4 of 4`, the Killer block (320 u) fits either. Update HUD_SPEC §6.1a line 2 to `Terminals 1/3`. Weight rules unchanged.
5. UI copy follows `docs/ui/SLOP_DEFINITION.md` (§4 copy tells) and `docs/ui/HUD_SPEC.md`.

## Acceptance
- New EditMode tests: elimination line for (a) 2 real captures + escalation, (b) 3 real captures, (c) unknown history (reconnect baseline in Lagging/with CaptureCount>0), Collapse/TimedOut unchanged; meter tag for count 1/2 after escalation; contiguous bot names for player in slot 1, 4, 8, two humans, a human-turned-bot keeping its name, empty slots, null; separation: coincident points separated deterministically, pair at distance ≥ min untouched, own avatar moves less than the other, offsets bounded, continuous for a small input change; both terminal formats equal `Terminals 1/3`.
- Unity EditMode all green: `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/Q1-playcheck-polish/unity-editmode.xml -logFile docs/qa/Q1-playcheck-polish/unity-editmode.log` (first run imports the Library for this worktree; it is slow — run it in the background and wait). Report pass/total.
- `BuildWindows` succeeds: `... -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile docs/qa/Q1-playcheck-polish/build-windows.log` (check `Client/Assets/Editor/BuildScript.cs` for the output path).
- No `Shared/`/server change → `dotnet` gates not required; say so.
- Unity must not run twice on the same project at once. Never kill processes you did not start (Unity Hub PID 38484 and Unity instances for `C:\Unity\CsharpPrac` must be left alone). No desktop keyboard/mouse input. No downloads.
- After Unity runs, `git checkout --` any Client files whose only change is line endings (check `git diff --ignore-all-space --stat` / `git diff -w`).

## Report (English, ≤ 200 words) to `docs/qa/Q1-playcheck-polish/impl-a1.md`
Header with the quota switch. Files changed, the exact copy chosen, how verified (commands + pass counts), what is unverified, risks.
