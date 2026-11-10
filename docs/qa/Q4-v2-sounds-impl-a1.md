# Q4-v2-sounds — implementation, attempt 1

Quota switch: Codex out of credits, Gemini Flash sign-in expired → Claude Opus implements, Claude Sonnet QA.

**Changed:** new `Client/Assets/Scripts/Audio/V2Cues.cs` (V2Cue, pure `V2CueRules`, `V2CueSynth`) and `Audio/RulesV2Audio.cs` (2 AudioSources: one-shot plus hold loop; logs `[audio] v2 cue|hold start|hold stop`). `RulesV2Wiring.cs`: `SyncCheckTracker.Resolved` (+3 lines). `ClientApp.cs`: 9 hook lines (create, Begin, OnWorld, OnSelf, Frame, End). New `Tests/EditMode/V2SoundTests.cs` (16 tests).

**HatchClose rule:** fires when the collapse starts while no exit is Open, on both roles. That can only come from the hatch closing. On the Killer it also fires when its own HatchClose hold ends at predicted progress ≥ 0.9 and the hatch leaves the snapshot within 0.5 s. Limit: a Survivor cannot hear a close while the collapse already runs. On the Killer, TerminalKick fires when its own Kick hold completes, because the server never sends Regressing to the Killer. A release in the last 10 % of the hold looks the same as a completed kick.

**Verified:**
- EditMode: 1401/1402 passed, 0 failed, 1 skipped. This ran in an LFS-complete scratch copy of the code. The worktree has only LFS pointer files there, so 15 art/motion tests fail in it.
- BuildWindows succeeded (scratch copy).
- Exe runs (150 s, 480 s): 8 SyncGood, 2 SyncGreat, 8 Capture and 11 Rescue cues. Each one matches a server event. Own captures gave no Capture cue, and bot syncs gave no cue.

**Unverified:** real listening. In the exe runs there was no ExitsPowered, ExitOpen, Collapse, Hatch, Kick or hold loop (≤ 2 terminals done); these are covered by EditMode only.
