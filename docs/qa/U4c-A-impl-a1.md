# U4c-A implementation report — attempt 1

**Files (new):** `Client/Assets/Scripts/Killer/{BlipTracker,BlipSoundModel,SurvivorSoundSynth}.cs`, `Client/Assets/Tests/EditMode/{BlipTrackerTests,BlipSoundModelTests}.cs`. No `.meta` files; Unity generates them on import.

**Verification:** a scratch netstandard2.1 lib (C# 9, warnings as errors) plus a net9.0 NUnit 3.14 test project under `<SCRATCH>\u4cA\`, run with `dotnet test u4cA.Tests.csproj`. Result: 82/82 pass (tracker 39, sound model and synth 43). The suite passed on two runs. It was not run in Unity.

**API additions:** constructors for `BlipInput` and `BlipEvent`; public constants; `BlipSoundModel.StepInterval(status, v)`, `IsVoiced(handle)`, `Reset()`; `SurvivorSoundSynth.*Peak`/`VariantCount`. All other members are internal.

**Judgement calls:**
- Cue events need a continuous snapshot, so `CaptureConfirm` and `ExecuteConfirm` are suppressed in three cases: the first snapshot after construction or `Reset`, a tick gap over 10, and a tick regression.
- The speed history stores displacement ÷ tick gap, so packet loss does not inflate `v`.
- `Reset` keeps handles increasing, so no handle is ever reused.
- Entries with non-finite X or Z are dropped.
- The step timer runs for unvoiced instances too. At most one step is emitted per frame per instance, and a status change restarts stepping with a new random delay.
- Out-of-range breathing is reported as `Breath = None` with a gain of −∞.
- Distances are horizontal (x, z) and use the snapshot X/Z, not the tweened position.
- The allocation tests use `GC.GetAllocatedBytesForCurrentThread` (< 4 KB over 500–1000 calls). This needs checking under Unity Mono.
