# Brief U4c-A — attempt 1: anonymous blip tracker and positional Survivor sound model (pure code)

Implementer: Claude Opus subagent (quota switch from Codex). QA: Claude Sonnet.

This is piece A of U4c and runs in parallel with piece B (`docs/briefs/U4c-a1.md`), which owns everything else in the client and consumes the API below. Piece B will integrate your files into its branch.

**Binding spec:** `docs/ui/HUD_SPEC.md` §6.3 (anonymous blips), §6.5 (cue triggers that come from tracking) and §6.5.1 (positional sound). Also read `docs/briefs/U2c-a1.md` §1 (killer-bound survivor entries are `Id = 0` with shuffled order) and `AGENTS.md`.

## Owned files (new files only)
- `Client/Assets/Scripts/Killer/BlipTracker.cs`
- `Client/Assets/Scripts/Killer/BlipSoundModel.cs`
- `Client/Assets/Scripts/Killer/SurvivorSoundSynth.cs`
- `Client/Assets/Tests/EditMode/BlipTrackerTests.cs`
- `Client/Assets/Tests/EditMode/BlipSoundModelTests.cs`
- `docs/qa/U4c-A-impl-a1.md`

Touch nothing else. Do **not** run Unity: piece B owns the Unity process on this project.

## Constraints
- **Engine-free C# 9, netstandard2.1:** no `UnityEngine` and no LINQ in the per-snapshot paths. Use plain floats or small structs. The client asmdef compiles these files as they are.
- Namespace `PingTheKiller.Client.Killer`.
- Verify by compiling these files and your tests in a scratch .NET project outside the repo. Use NUnit (the same API Unity's test framework exposes) under `<SCRATCH>\u4cA\` and run them. Report the counts. The orchestrator runs them in Unity later.
- **Randomness:** pass in a `System.Random` (or an `IRandom` you define) created by the caller. It must never be seeded from wire data.

## API (fixed; piece B codes against exactly this shape. You may add members, never rename or remove these)
```csharp
namespace PingTheKiller.Client.Killer {
  public enum BlipStatus : byte { Normal = 0, Lagging = 1 }
  public struct BlipInput { public float X, Z; public BlipStatus Status; }   // one Survivor entry of a killer-bound WorldState (killer's own entry already removed by Role)
  public sealed class Blip {
    public int Handle;            // client-local, never derived from Id or wire order
    public float X, Z;            // current snapshot position
    public float PrevX, PrevZ;    // position in the previous snapshot (== X,Z when just spawned) — B tweens Prev→current over one tick
    public BlipStatus Status;
    public float Speed;           // §6.5.1 v: mean of the last ≤4 matched displacement lengths / 0.05 s; 0 until first match
    public float FacingX, FacingZ;// unit vector, §6.3 rule 5 (last matched displacement ≥ 0.03 m, else toward nearest terminal)
    public bool Walking;          // Speed > 0.5
  }
  public enum BlipEventKind : byte { Spawned, Despawned, StatusNormalToLagging, CaptureConfirm, ExecuteConfirm }
  public struct BlipEvent { public BlipEventKind Kind; public int Handle; }
  public sealed class BlipTracker {
    public BlipTracker(System.Random rng, IReadOnlyList<(float x, float z)> terminals);
    // tick = S2C_WorldState.Tick; killerX/Z = killer's own entry position.
    public void Accept(uint tick, IReadOnlyList<BlipInput> entries, float killerX, float killerZ);
    public IReadOnlyList<Blip> Blips { get; }          // current instances (order carries nothing; B must not rely on it)
    public IReadOnlyList<BlipEvent> Events { get; }    // events produced by the last Accept
    public void Reset();                               // new match / reconnect
  }
}
```

## Rules (HUD_SPEC §6.3 and §6.5, normative)
1. **Shuffle and match.**
   - Shuffle the incoming entries with the RNG before anything else.
   - Greedy nearest-distance matching to the previous blips: consider every pair within the gate, take the pairs in ascending distance, break ties randomly, and use each side at most once.
   - **Gate** = `0.4 m × max(1, tick − previousTick)`. A gap of more than 10 ticks means no match at all (everything respawns).
2. **Matched pairs** keep their Handle and update `Prev*` and the current position, plus the speed history (4 entries) and facing.
3. **Unmatched entries** spawn a new Blip with `Speed = 0`. Its facing points toward the nearest terminal, or (0, 1) when no terminals are given. Emit `Spawned`.
4. **Unmatched old blips** despawn at once. Emit `Despawned`.
5. **Facing:** take the most recent matched displacement ≥ 0.03 m and normalise it. A blip that has not moved since it spawned faces the nearest terminal.
6. **Events (§6.5).**
   - `StatusNormalToLagging`: a matched pair changed Normal → Lagging.
   - `CaptureConfirm`: one of the following, at most once per 0.5 s of tick time (ticks × 0.05 s).
     - a `StatusNormalToLagging`;
     - a new unmatched entry with `Lagging` within 1.2 m of the killer.
   - `ExecuteConfirm`: a blip that was `Lagging` and within 1.2 m of the killer in the previous snapshot has no match now.
7. **Handles** are increasing integers local to the tracker. They are never exposed on screen (B's job), and they never come from `Id` or entry order.

## Sound model (HUD_SPEC §6.5.1, normative)
```csharp
public enum StepVariant : byte { Normal, Lagging }
public enum BreathVariant : byte { None, Calm, Heavy, Ragged }
public struct StepEvent { public int Handle; public StepVariant Variant; public int Sample; public float Pitch; public float GainDb; public bool Occluded; }
public struct VoiceState { public int Handle; public BreathVariant Breath; public float BreathGainDb; public bool Occluded; }
public sealed class BlipSoundModel {
  public BlipSoundModel(System.Random rng);
  // Called every rendered frame. dt = frame seconds. listener = killer camera position (x,z).
  // occluded(handle) = the §6.2 wall-ray result B computes (with its 100 ms hysteresis) for that blip.
  public void Update(float dt, IReadOnlyList<Blip> blips, float listenerX, float listenerZ, Func<int, bool> occluded);
  public IReadOnlyList<StepEvent> Steps { get; }     // steps to play this frame
  public IReadOnlyList<VoiceState> Voices { get; }   // up to 4 voiced instances and their breathing
  public static float AttenuationDb(float levelAt2mDb, float distance, float rangeLimit); // stated level to 2 m, linear in dB to −40 at the limit, then mute (return float.NegativeInfinity)
}
```

### Footsteps

| Status | Start above | Stop below | Step interval | Range | Level |
|---|---|---|---|---|---|
| Normal | 0.5 m/s | 0.4 m/s | `1.2/v`, clamped 0.25–0.9 s | 20 m | −20 dB at 2 m |
| Lagging | 0.2 m/s | 0.15 m/s | `0.8/v`, clamped 0.5–1.5 s | 20 m | −20 dB at 2 m |

- When stepping starts, the first step comes after a random delay in [0, interval).
- Each step picks a random sample 0–3 and a random pitch of ±5 %.

### Breathing

| Variant | When | Level | Range |
|---|---|---|---|
| Calm | Normal, v ≤ 0.5 | −30 dB | 6 m |
| Heavy | Normal, v > 0.5 | −24 dB | 8 m |
| Ragged | Lagging | −24 dB | 10 m |

- Calm ↔ Heavy switches use the same hysteresis as footsteps: up above 0.5 m/s, down below 0.4 m/s.

### Occlusion and voices
- **Occlusion:** an occluded instance gets −4 dB. It is flagged so B applies a 1.5 kHz low-pass.
- **Voice cap:** only the 4 nearest instances are voiced. A voiced instance keeps its voice until another instance is at least 1 m nearer than it.
  - Only voiced instances emit steps or breathing.
  - Expose `IsVoiced(handle)` so B's edge-indicator selection takes voiced instances first.
- **Decoy safety:** everything here depends only on position, `Speed` and `Status`. Write a test showing that two blips with identical motion produce identical step timing distributions and identical gains.

### `SurvivorSoundSynth`
Pure `float[]` generators at a given sample rate. B wraps them into `AudioClip`s.
- `Step(int variant, int sampleRate)`: 4 short noise-burst footstep variants, about 80–120 ms.
- `LaggingStep(int variant, int sampleRate)`: 4 longer dragging scuffs.
- `Breath(BreathVariant, int sampleRate)`: seamless loops of about 2–3 s.

These are placeholders until real samples arrive. Keep them quiet and dull, with no clicks at the loop points.

## Tests (required)
- **Tracker.**
  - **Anonymity:** fakes that emit `Id = 0` in shuffled order (your input has no Id at all). Two blips crossing paths keep their handles when the gate allows it.
  - A blip jumping more than the gate despawns and respawns.
  - The tick-gap gate.
  - Tie-breaking does not follow input order: run 1000 seeds with a symmetric tie and check both outcomes occur.
  - Facing threshold 0.03 m; new blips face the nearest terminal.
  - The speed window has 4 entries.
  - Each event rule, including the 0.5 s capture-confirm limit.
  - `Reset`.
  - Allocation sanity: no LINQ.
- **Sound model.**
  - Thresholds with hysteresis, interval clamps and the random first-step delay range.
  - `AttenuationDb` checked at 2, 11 and 20 m and beyond the limit.
  - Breathing variants and ranges.
  - The 4-voice cap with the 1 m hysteresis.
  - −4 dB on occluded instances.
  - Decoy parity.
  - No steps below threshold and none for unvoiced instances.

## Report `docs/qa/U4c-A-impl-a1.md` (≤ 200 words)
- files
- scratch test command and counts
- any judgement call the spec did not cover
