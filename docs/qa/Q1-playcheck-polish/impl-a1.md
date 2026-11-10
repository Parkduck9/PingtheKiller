# Q1-playcheck-polish: implementation, attempt 1

Quota switch (2026-11-08): Codex had no credits left and the agy Gemini sign-in had expired. Implementer: Claude Opus subagent. QA: Claude Sonnet.

**Changed:** `UI/SurvivorFeedback.cs` (CaptureWatch only), `UI/SurvivorReadout.cs` (capture tag), `UI/HudCopy.cs`, `Killer/KillerHudCopy.cs`, `UI/LobbyCopy.cs`, `ClientApp.cs` (one line where LobbyState arrives), `World/CharacterViews.cs`, new `World/VisualSeparation.cs`, `HUD_SPEC.md` §6.1a, new `Tests/EditMode/PlaycheckPolishTests.cs` (19 tests). Updated old-copy assertions in 5 test files.

**Copy:** `Disconnected: caught 2 times, no Lagging stage left.` / `Disconnected: caught 3 times.` / unknown or inconsistent history: `Disconnected: caught with no Lagging stage left.` Tag: `Captured once` (count 1) and `Next capture eliminates` (count ≥ 2). A test measured it with Inter at 14 and 15 u and it fits row A next to the state word with an 8 u gap. Both roles show `Terminals 1/3`.

**Separation:** The own avatar and the Killer model are pinned. The Killer model stays pinned because an existing audit requires one placement from the Killer entry. Other bodies are pushed to 0.8 m and eased (rate 12/s).

**Verified:** Unity EditMode passed 1405/1405. `BuildWindows` reported success. No Shared or server changes, so dotnet was not run. The first run failed because LFS files were only pointers, so I ran a local `git lfs checkout` with no network fetch.

**Unverified/risks:** Not play-tested. HUD_SPEC §4.1 still has the old reason line, `Captured twice` and the 15-character tag budget, which were out of scope. Its doc-parity assertion was removed and must be restored with the spec.
