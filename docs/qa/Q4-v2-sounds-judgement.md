# Q4-v2-sounds — orchestrator judgement (attempt 1): PASS

Routing: Codex out of credits, Gemini Flash sign-in expired → Claude Opus implemented (quota fallback); QA by Fable
(user routing change 2026-11-09; the brief originally named Sonnet QA).

## Gates (run in this worktree, after `git lfs checkout` filled the 852 LFS files from the local store, no download)
- Unity EditMode: 1402/1402 passed, 0 failed (V2SoundTests 16/16).
- `BuildScript.BuildWindows`: succeeded (`Client/Builds/Win64/PingTheKiller.exe`).
- Unity's line-ending-only churn in Client assets was reverted. Full Unity logs/xml are kept outside the repo (too large).

## Spec
Fable QA: 22/22 spec bullets PASS. The HatchClose inference is a documented DEVIATION. Server code confirms the inferences:
the collapse starts only on an exit opening or a hatch close; Regressing is masked for the Killer; a completed hold zeroes
ActionKind.

## Live evidence (offline exe, 7 runs: `Q4-v2-sounds-runs/*.log`)
Each `[audio] v2 cue` line matches a real `[offline server]` event.
- **Observed live:** SyncGreat, SyncGood, SyncFail, Capture (allies only, own capture silent), Rescue, ExitsPowered,
  ExitOpen, CollapseStart, HatchClose (Survivor, collapse-start path).
- **Not observed live, covered only by mapping tests:** HatchOpen, TerminalKick (the bot Killer never kicked), the
  Killer's own-hold HatchClose path, and the switch/kick/hatch hold loop.
- **Negatives held:** the Killer got no ExitOpen, Capture or Rescue. No cue fired at match start.
- **Real listening (loudness, character, mix) is unverified.**

## Accepted minor findings (follow-ups, not blockers)
- F1: `ClientApp.Disconnect` does not call `v2Audio.End()`. No audible effect: every feed is gated by `playing`, and
  `Frame` stops the loop. Add it next time ClientApp is touched.
- F4: a hatch close plays ExitsPowered, CollapseStart and HatchClose in the same snapshot, so the summed peak can pass 0.95.
  Consider letting HatchClose suppress the other two, or ducking.
- F5/F6 design notes: Rescue also plays for Emergency Restore, and spectators keep hearing the cues.
- F3: a Killer release in the last 10 % of a kick plays TerminalKick.
- F7: no tests yet for the HatchClose→Kick transition or the three-cue snapshot.
