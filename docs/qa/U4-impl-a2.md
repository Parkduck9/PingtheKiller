Implementer: Claude Opus subagent (quota switch from Codex)

# U4 implementation — attempt 2

## Changes per fix item
1. `World/MovementInput.cs`: `TryCreate(held, right, forward, yaw, dt, out packet)` — 30 Hz unscaled-dt accumulator, fresh press due at once, ≤1 send/frame, hitch drops missed slots, release = zero now + one backup zero at next slot, hard cap 31 sends per sliding 1 s (ring of send times; also bounds key tapping). Old 5-arg overload kept (each call = one slot). `ResetKeys()` keeps Seq. `ClientApp` creates `MovementInput` per `NetClient` (QA-07), passes `Time.unscaledDeltaTime`.
2–3. `ClientUI.uss` rewritten with `.root .unity-button`/`:hover`/`:active`/`:hover:active`/`:focus`/`:disabled`, `.root .unity-label`, `.root .address .unity-text-field__input`; spacing only 4/8/12/16/24/32 (screen margins 32).
4. Esc/click-resume only for survivor; `ClientScreens.SetDebug(rtt, survivor, released)` shows no cursor hint to the killer (text rebuilt only on change).
5–6. `ThreatAudio.BuildBeepSamples(int)`, `ThreatAudio.NextBeepAfterChange(...)`; `SetLevel` null-safe before `Awake`.
7. RTT uses `Received.Time` (receive thread).
8. Numeric `IsServerPacket` check; `WorldView` reuses terminal `HashSet`/`List`.
9. `BuildScript`: no `[InitializeOnLoad]`; `EnsureAssets` from `BuildWindows` or menu `Ping the Killer/Ensure Build Assets`.
10. QA-05 comment in `SnapshotBuffer`; `TODO(U4b)` prediction note in `ClientApp`.

## Tests
New: `MovementInputTests` (9 methods: 144/30/45/60/240 fps hold, hitch, release+backup, Seq, tapping cap, ResetKeys, invalid dt), `ThreatAudioTests` (clip length/zero crossings at 44.1/48 kHz, rising/falling/oscillating/zero). `ClientMathTests` per-frame test replaced by `LegacyOverloadTreatsEachCallAsOneSlotWithReleaseAndBackupZero`.

Scratch .NET 9 build (outside repo, Unity DLLs, LangVersion 9): all `Scripts/**` + `BuildScript.cs` compile; implementer + QA tests: 145 pass, 2 fail.

## Obsolete QA tests (item 1)
`InputQaTests.OneSendPerHeldFrameThenExactlyOneZeroSendThenSilence`, `InputQaTests.SeqIsStrictlyIncreasingAcrossHeldFramesZeroSendAndALaterPress` (both reject the backup zero).

## Unverified
Unity import/batch tests, rendered USS look, focus/hover pixels, beep audibility.
