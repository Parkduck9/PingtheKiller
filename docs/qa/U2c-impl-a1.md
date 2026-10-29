# U2c implementation report, attempt 1 (Claude Opus, quota fallback)

**Changed:** Shared `PacketId`, `ServerPackets`, `PacketCodec` (S2C_SelfState 0x0010, 4 floats). Server.Core: GameServer, `.Visibility` (rewritten), `.Phantoms` (new), `.Bots` (rewritten), `.Skills`, `.Rules`, `.Lobby`, Player, `PrivateRandom` (new), `Map/NavGrid` (new), 3 `.meta`. `Server.Tests/Core`: 5 new files, 11 edited.

**Results**
- Build `-warnaserror`: 0 warnings, 0 errors.
- Tests: Server 645/687, Harness.Tests 284/284. Failing: 41 `QA/**` tests that assert replaced behaviour (killer-view Ids or flags, Id borrowing, exact killer progress, `System.Random` streams, old bot timing), and the root test `ProtocolV02Tests.DefaultPacketCoverageIncludesAllFifteenIds` (not owned; it needs 15 changed to 16).
- Harness ×3, fresh servers: **80/100** each run. H3 times out, as expected until U3c (it looks up the killer stream by Id). H6 passes.
- Median match length, 15 seeds, before → after: idle human killer 22.9 → 227.7 s; killer bot 53.7 → 136.9 s (**target 180 s missed**).
- 3/4 stall: reproduced with seeds 3 and 5. Bots were pinned on wall (18,14)-(20,32), and a threat freeze never released. Fixed with NavGrid routing and time-limited freezes.
- Indistinguishability (761 phantom vs 253 real tracks): **gate FAILS**, so the test prints it instead of asserting. KS D (all p≈0): speed 0.71, still-away 0.31, spawn distance 0.31, tail 0.31 (mean 13.9 vs 20), lifetime 0.90 (residual).

**Judgement calls**
- The killer view is capped at the 8-entry wire limit: real entries first, then phantoms by age.
- A phantom's last frame is still, to mirror the 0.15 s hold.
- White Noise walkers start 0–4 m along clear landmark segments.
- A Dummy at the open exit vanishes without a tail.
- `PrivateRandom` is xoshiro256** and subclasses `Random`.
- `InternalsVisibleTo` exposes ground-truth labels to the tests.
- Bots work one terminal at a time with burst pacing and bounded threat holds. The killer bot has reaction delays.
- `EqualWireSeedsDoNotGiveEqualBotPaths` is kept as a non-test method because QA calls it.

**Unfinished:** the KS gate, the killer-bot median, and phantom-tail truncation (probably the cap; not verified). SelfState doubles the outbound packet rate, so undrained in-memory peers overflow after about 2048 ticks.
