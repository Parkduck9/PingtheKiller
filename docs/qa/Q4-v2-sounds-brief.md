# Q4-v2-sounds — brief (attempt 1)

Quota switch: Codex is out of credits and the Antigravity Gemini Flash sign-in is expired. Per CLAUDE.md quota fallback,
a Claude Opus subagent implements and a Claude Sonnet subagent does QA. Record this in every report header.

## Goal
The rules-v2 events are silent (docs/PLAN.md "v2 sounds missing"). Add short procedural 2D cues for them. No downloaded
or imported audio: every sample is generated in code, like `Killer/SurvivorSoundSynth.cs`, `Killer/KillerCues.cs`
(`KillerCueSynth`) and `UI/SurvivorFeedback.cs` (`SurvivorCueSynth`).

## Owned files
- New folder `Client/Assets/Scripts/Audio/` (+ `.meta` files Unity creates): e.g. `V2Cues.cs` (enum, pure synth, pure
  trigger rules) and `RulesV2Audio.cs` (MonoBehaviour that owns clips and AudioSources).
- `Client/Assets/Scripts/UI/RulesV2/RulesV2Wiring.cs` (exclusive): e.g. a `Resolved` event on `SyncCheckTracker.OnResult`.
- New tests under `Client/Assets/Tests/EditMode/` (e.g. `V2SoundTests.cs`).
- `Client/Assets/Scripts/ClientApp.cs`: **minimal hook lines only** (create the component, feed it WorldState / SelfState /
  frame tick, reset on MatchStart / EndMatch). Keep the diff as small as possible (target ≤ 15 changed lines): another unit
  may touch this file in parallel. Say why in the report.
- `docs/qa/Q4-v2-sounds-impl-a<N>.md` (your report).

Do NOT touch: `World/CharacterViews.cs`, Q1 copy files (`UI/RulesV2/RulesV2Copy.cs`, `UI/*Copy*.cs`), `Killer/EdgePlacement.cs`,
`Killer/TagLayout.cs`, `UI/SettingsScreen.cs`, `UI/ClientScreens.cs`, `World/RoleCamera.cs`, `Shared/`, `Server/`. You may
read the server rules (`Server/`) to get edges right. No new settings UI.

## Cues (V2Cue enum) and trigger rules
All triggers are edges, computed by a pure rules class (no UnityEngine audio types inside) that the MonoBehaviour drains
(same pattern as `KillerCueRules.Pending`). The first WorldState after MatchStart only sets the baseline (no cue for state
that was already true, e.g. a late join), except nothing special is needed at match start. At most one cue of each kind per
snapshot; no cue repeats while its condition stays true.

| Cue | Who hears it | Edge |
|-----|--------------|------|
| SyncGreat / SyncGood / SyncFail | Survivor | `SyncCheckTracker.OnResult` returned true (matched, first result for the current check) |
| SwitchHold (loop) | the holder | own `SelfState.ActionKind` != None: loop plays while held; pitch (or a filter/tick rate) rises with `ActionProgress` 0..1; stops within one frame when the action ends. Use it for Switch; Kick and HatchClose may reuse it with a different base pitch. Loop must be seamless (no click at wrap). |
| ExitsPowered | both roles | first time any exit's Powered flag goes on in the match |
| ExitOpen | Survivor only (the Killer already has `KillerCue.ExitOpen`) | any exit's Open flag rising edge |
| CollapseStart | both roles | global `WorldState.CollapseLeft` goes from < 0 to ≥ 0 |
| HatchOpen | both roles | `HatchVisible` 0 → 1, first time per match only (later re-sightings are silent: visibility also depends on sight distance) |
| HatchClose | both roles where observable | the hatch was closed (not just out of sight). Derive the observable signal from the server rules (e.g. Killer: own HatchClose action completed and hatch gone; Survivor: hatch gone in the snapshot where collapse starts, or whatever the rules make reliable). Document the rule you chose and its limits in the report. Never fire on walking out of sight range. |
| Capture | Survivor | another Survivor's entry Status Normal → Lagging. The own capture already plays `SurvivorCue.Captured` in ClientApp — do not double it. The Killer already has `KillerCue.CaptureConfirm` — no new Killer capture cue. |
| Rescue | Survivor | any Survivor entry (own included) Status Lagging → Normal |
| TerminalKick | both roles | any terminal's `Regressing` flag (TerminalFlags.Regressing = 1) rising edge |

Track entries by Survivor `Id` on a Survivor client only (the Killer client must not read Survivor Ids, HUD_SPEC 6.3).
An entry that disappears and reappears is not a status edge.

## Synth requirements
- Pure `float[] Build(V2Cue cue, int rate)` at `KillerAudio.Rate` (44100), mono, fixed seeds, deterministic.
- Peak ≤ 0.95 for every cue (aim at the existing 0.4 reference loudness; stingers may go a bit higher but never clip).
- Buffers start and end at exactly 0 (edge fade) so there are no clicks; the hold loop is seamless at the wrap.
- Character: Great = bright two-note up; Good = single clean tone; Fail = low dull buzz/down; ExitsPowered = rising hum
  swell; ExitOpen = open chord; CollapseStart = low alarm pulse; HatchOpen = metallic clunk up; HatchClose = clunk down;
  Capture = harsh glitch stinger; Rescue = warm rising stinger; TerminalKick = digital crunch. Lengths 0.08–1.2 s.

## Volume
`ClientApp.ApplyOptions` already sets `AudioListener.volume = GameSettings.Volume01`. Cues must go through AudioSources that
respect the listener volume (`ignoreListenerVolume == false`, `spatialBlend == 0`) and must NOT multiply by Volume01 again
(no double scaling). Do not add settings UI.

## Logging (for the exe run)
Every played cue logs exactly one line `[audio] v2 cue <Cue>` (hold loop: `[audio] v2 hold start <Kind>` / `stop`).

## Acceptance tests (EditMode)
1. Each cue renders non-silent (RMS above a floor), peak ≤ 0.95, first/last sample == 0, deterministic (two builds equal),
   expected fixed length.
2. Hold loop: first and last samples continuous (wrap step small).
3. Cue-to-event mapping: each listed edge triggers exactly its cue once; repeating the same snapshot N times triggers
   nothing more; baseline snapshot triggers nothing; Killer client never gets Capture/Rescue/ExitOpen; own capture gives no
   Capture cue; a hatch leaving sight range gives no HatchClose.
4. Volume: RulesV2Audio's AudioSources have `ignoreListenerVolume == false` and `spatialBlend == 0`; clip data is not
   pre-scaled by settings volume (same samples at volume 0.3 and 1.0).
5. Unity EditMode suite all green; `BuildScript.BuildWindows` succeeds.

## Commands
- Unity (one instance per project; this worktree's `Client/` is its own project):
  `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <abs>/docs/qa/unity-editmode.xml -logFile <abs log>`
  `... -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <abs log>`
- Offline exe: `Client/Builds/Win64/PingTheKiller.exe -ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-auto-sync good -ptk-quit-after 150 -logFile <abs>` (check `Client/Assets/Scripts/Dev/DevArgs.cs` for exact flags; consider `-batchmode -nographics` is NOT used for the exe — just launch it with flags).
- Run long commands in the background.

## Rules
- Never send keyboard/mouse input to the desktop. Launching the exe with flags is fine.
- Never kill processes you did not start (Unity Hub PID 38484 and Unity instances for `C:\Unity\CsharpPrac` must stay).
- No downloads. No commits, no push (the orchestrator commits after judgement).
- After Unity runs, revert Client files whose only change is line endings (`git diff --ignore-cr-at-eol --stat` vs `git diff --stat`).
- Report ≤ 200 words in `docs/qa/Q4-v2-sounds-impl-a1.md` (+ header with the quota switch): files, verification commands and
  results, what is unverified (real listening is unverified), risks.
