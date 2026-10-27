# Brief U2c — attempt 1: protocol v0.3 (SelfState) + decoy hardening + server RNG privacy

Status: **queued** — starts after U2b is merged into main. Implementer: Claude Opus while Codex is out of credits; QA on a different model.
Decision basis: three parallel design evaluations on 2026-10-27 (A: anonymous/shuffled killer view, B: shared decoy lifecycle incl. afterimage, C: decoy motion and staggering). All three said "recommend with changes" and that none is sufficient alone. The orchestrator merged them into the design below. The user asked for this choice to be made by evaluation ("combine, pick one, or pick something better"), with a stated preference for A and B.

## 1. Killer view is anonymous (from A)
- Every survivor entry in a **killer-bound** `S2C_WorldState`, whether real, afterimage or decoy, is written with `Id = 0`, `Yaw = 0`, `CaptureCount = 0`, `StatusTimer = 0`, and `Flags = Signal` only (no Afterimage, Hacking, Rescuing or Stealthed bits). `Status` stays (Normal or Lagging; Lagging is always visible per GDD).
- Entry order is shuffled every snapshot with the private RNG (§4). The killer's own entry keeps its real Id; clients identify it by `Role`.
- Wire layout is unchanged (reuse `PlayerState`). The survivor-bound view is unchanged.
- Decoys no longer borrow Ids, so the decoy count no longer depends on absent survivors. WhiteNoise always shows its 5 points, including in 1v1. Remove `BorrowedId`, `SortById` and the absent-Id bookkeeping.
- The killer bot must use positions only, never Ids.

## 2. One emit path for real and fake blips (from B)
- Replace the decoy record with a "phantom" that has the same visibility fields as a real survivor (`Visible`, `LastVisibleAt`, `VisibleX/Z`). Real survivors and phantoms go through the same entry builder and the same afterimage logic, so the two cannot drift apart.
- When the killer has Packet Afterimage, an expired phantom lingers 1 s frozen at its last position, exactly like a real survivor.
- Skill durations stay exactly as in GDD §4 (Dummy 3 s, WhiteNoise 1.5 s). No duration jitter.

## 3. Decoy behaviour (from C)
- **Determinism:** phantom movement is driven by the private RNG and is deterministic for a given private seed. Every step goes through `Map.Move` at `SurvivorSpeed`, so phantoms never stand inside walls.
- **DummyPacket:** spawns at the caster's position (GDD: "현재 위치"). If within `TerminalRadius` of an incomplete terminal, it stands still ("hacking"). Otherwise it walks toward the nearest incomplete terminal or the open exit. When the caster is Signal at cast time, the dummy still appears; Ids are gone, so nothing blocks it.
- **WhiteNoise:** 5 points. Spawn times are staggered uniformly over 0–0.5 s, and all expire at cast + 1.5 s. Points are chosen from plausible spots: up to 2 at incomplete terminals (±1 m, still), and the rest walk 0–4 m along segments between terminals, the exit and spawn points. Reject points within 3 m of the killer and points within 6 m of each other.
- **Rule:** a phantom is still only within `TerminalRadius` of a terminal; anywhere else it moves.

## 4. Server RNG privacy (new finding from A)
- `S2C_MatchStart.Seed` goes to every client, including the killer. No server-side randomness may be derivable from it: phantom placement, motion, shuffle order and bot decisions all use a private RNG seeded from a CSPRNG at match start.
- `MatchStart.Seed` becomes a cosmetic-only seed, independently generated. Tests inject the private seed through `ServerOptions`, a test hook that the console and offline paths never set.
- Killer-bound `S2C_SkillEffect` never carries a survivor's Id: `CasterId = 0` for TrafficOverload, and audit every killer-bound packet the same way.

## 5. Protocol v0.3 — `S2C_SelfState` (user decision, HUD spec Q5/Q6)
- Add packet `S2C_SelfState { float SkillCooldown; float RescueProgress; float InvulnerableLeft; }` (new PacketId after 0x0F). It is sent at 20 Hz only to its own player, never about another player.
- `RescueProgress` is 0–1 for a Lagging player being rescued; for a rescuer it is the progress of the target they are rescuing.
- Shared change: new packet class, codec registration and round-trip tests. Unity and the harness compile it automatically. Unknown-packet handling in the harness decoder must tolerate it (coordinate with the harness owner).

## 6. Carried from U2b QA (`docs/qa/U2b-qa-a2.md`)
- The 2 s execution grace, the private RNG and the Seq reset were already done in U2b attempt 3.
- **F6:** replace the probabilistic `EqualWireSeedsDoNotGiveEqualBotPaths` with a deterministic test: fix the private seed through the hook, vary only the wire seed, and assert a bit-identical trace over 3 matches with 7 bots.
- **F7:** hidden draws must use a generator with at least 128-bit state, or `RandomNumberGenerator`, instead of 31-bit `System.Random`. A 31-bit state can be brute-forced in about 31 CPU-minutes.

## Acceptance (sketch — finalize when U2b merges)
- **Byte check:** no killer-bound frame contains any survivor Id bytes at an entry's Id offset.
- **Shuffle:** order is uniform (chi-square over many snapshots).
- **Determinism:** the same private seed gives identical phantoms, and the cosmetic Seed has no influence.
- **Phantom invariants:** a phantom is never inside a wall, never moves more than `SurvivorSpeed × dt` per tick, and is still only near a terminal. WhiteNoise has 5 points, spawn offsets ≤ 0.5 s, and expiry at cast + 1.5 s.
- **Afterimage parity:** with Afterimage, the tail is 20 ticks for both real and phantom blips. Without it, the tail is 0 for both.
- **Indistinguishability report:** over seeded bot matches with ground-truth labels, compare per-track features (speed, stillness away from terminals, lifetime, spawn distance to the nearest real survivor, afterimage tail) between phantom and real tracks. Gate: no feature with a KS p-value < 0.01, except lifetime, which is documented as a known residual.
- **SelfState:** only to its own player; values match the server timers.
- The leak sweep, sandbox harness 90/100 ×3, and H6 (normal server) all pass.

## Known residuals (accepted)
- Touching a phantom captures nothing.
- More blips than living survivors reveals that decoys exist, but not which ones are decoys.
- The Dummy spawn point is the caster's true position at that instant (GDD rule).
