# QA Review: Unit U4h — World Art (Map v2)

- **Reviewer:** Independent Visual & Code QA (Antigravity)
- **Implementer:** Claude Opus
- **Target:** `FixedMap` v2 art implementation
- **Verdict:** **FAIL**

---

## 1. Retarget Specification Review

| # | Spec Requirement | Verdict | Evidence |
|---|------------------|:-------:|----------|
| 1 | Buildings fill footprint ±0.05m to 3m, upper storeys ≤10m setback, varied facades | **PASS** | `WorldArtTests.cs#L97-160`; `editor-overview-720.png` |
| 2 | Border walls match footprint; backdrop/skyline beyond border | **PASS** | `WorldArtTests.cs#L97-113,L235-254`; `editor-overview-720.png` |
| 3 | Obstacles fitted ±0.1m, visual height ≤1.5m (lower than 1.8m character) | **PASS** | `WorldArtTests.cs#L163-179`; `editor-obstacles-720.png` |
| 4 | Ground roads/plazas flat (≤0.15m), dark asphalt/tile preserving contrast | **PASS** | `WorldArtTests.cs#L211-217`; `alley-1080.png`, `editor-plaza-wide-720.png` |
| 5 | Street dressing inside footprints or outside border; no free props in walkable areas | **PASS** | `WorldArtTests.cs#L235-254`; `SceneLighting.cs#L45-47` |
| 6 | 8 Terminals (3 states, ≤1.2m) & 2 Exits (ring + marker when open); isolated look methods | **FAIL** | `WorldProps.cs#L165-172`; `editor-exit-open-720.png`; `hacking-1080.png` |
| 7 | Hatch visual omitted (deferred to U6c) | **PASS** | `WorldProps.cs` (no hatch visual code) |
| 8 | Night city lighting: key + fill; characters read against walls/ground | **PASS** | `SceneLighting.cs#L17-47`; `survivor-3p-1080.png`, `editor-obstacles-720.png` |
| 9 | Performance: static batching, shared materials, shadows on key light only | **PASS** | `WorldProps.cs#L118-121`; `SceneLighting.cs#L45-47` |

---

## 2. Gameplay Readability & Visual Quality

- **Silhouette Readability:** Teal Rio (`#3FB8AF`) and red Kyle (`#E5484D`) pop cleanly against the dark asphalt and neutral shopfronts (`alley-1080.png`, `editor-obstacles-720.png`). The outlined survivor in the killer view (`killer-inview-1080.png`) is crisp down the alley.
- **Line-of-Sight Honesty:** Dumpsters and fence obstacles in `editor-obstacles-720.png` and `editor-plaza-wide-720.png` stand at ~1.2m–1.3m, leaving character chests and heads visible. They correctly read as non-sight-blocking cover.
- **Night City Coherence:** Low-poly City 03 buildings with unlit shop windows create a convincing dusk/night mood without glow halos or garish neon. Fog smoothly fades to palette `#101113` (`FogStart = 30`, `FogEnd = 160`), preventing void cutouts. No pink shaders or z-fighting observed.

---

## 3. Code Review Findings

- **Zero per-frame allocations in `Accept`:** Reuses `retained` (`HashSet<byte>`) and `removed` (`List<byte>`); dictionary enumerator is a non-allocating struct (`WorldProps.cs#L131-147`).
- **Static Batching:** Guarded by `Application.isPlaying` to preserve EditMode test introspection (`WorldProps.cs#L117-121`); combined meshes are tracked and destroyed on `Dispose`.
- **Exit Marker Particle Bug:** `SetExitLook` activates `item.Marker` and assigns `main.startColor`, but never calls `ParticleSystem.Play()`. If particle systems are not looping or auto-playing, particles never emit.

---

## 4. Findings & Defects

| Severity | Item | Expected | Actual | Evidence |
|----------|------|----------|--------|----------|
| **Major** | Exit Open Marker VFX invisible / unverified | Hovl particle pillar/zone appears over open exit ring | `editor-exit-open-720.png` shows ring with 0 particles. `exit-open-killer-1080.png` faces a solid wall. `WorldProps.cs` never calls `Play()`. | `WorldProps.cs#L165-172`; `editor-exit-open-720.png`; `exit-open-killer-1080.png` |
| **Major** | Missing terminal hacking exe frame | Exe capture showing terminal close-up during hacking | `hacking-1080.png` / `720.png` show Rio running on open asphalt near an exit; terminal is entirely absent. | `hacking-1080.png`; `hacking-720.png` |
| **Minor** | Missing bounds check in `SetExitLook` | Defensive check on `exit` parameter | Direct index `exits[exit]` will throw `IndexOutOfRangeException` if caller passes invalid exit index. | `WorldProps.cs#L160` |
| **Minor** | Draw call metrics omitted | Frame Debugger / UnityStats metrics in report | Not reported (disclosed in implementer report). | `U4h-impl-a1.md#L26-27` |

---

## 5. Anti-Slop (SLOP §9) Score

- **Visual Tells (V1–V12):** 20/20 (Palette strictly constrained; no glow halos, gradients, or glassmorphism).
- **Layout & Hierarchy:** 15/15.
- **Copy & Voice:** 15/15 (Fixed vocabulary: Signal, Silent, Terminal, Exit Locked).
- **State Encoding:** 10/15 (-5 for missing visual exit marker VFX feedback).
- **Role HUD & Info Integrity:** 20/20 (No killer state leaks).
- **Interaction States:** 10/10.
- **Contrast & Legibility:** 5/5.
- **Total Score:** **95 / 100** (Gate checks: PASS).

**Remediation for PASS:** Ensure `SetExitLook` calls `Play()` on particle systems, reshoot `exit-open` frame facing the active exit marker, and reshoot `hacking` showing the terminal close-up.
