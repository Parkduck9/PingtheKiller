# U2c implementation report, attempt 2

**Fix list status**
1. **Phantoms.** Phantoms act as ghost survivor bots: bot burst and pause pacing, NavGrid, SurvivorSpeed and a final still frame.
   - White Noise: 1–2 of the 5 points sit at terminals. The others walk their landmark segment and then head for the nearest incomplete terminal.
   - Dummy: spawns at the caster.
2. **Entry cap.** `Protocol.MaxWorldEntries = 16`; only the count changed, the layout did not. Every hidden player keeps one reserved slot, so a survivor who turns Signal never cuts a phantom that is already shown. The phantom tail mean is now exactly 20.0 ticks, matching real tracks.
3. **SelfState.** Sent on any jump plus a 1 Hz refresh. `ATenMinuteMatchStaysFarBelowTheReceiveQueueCap` passes.
4. **Coverage test.** `ProtocolV02Tests` now asserts 16 ids.

**Indistinguishability gate** (6 seeds; 1183 phantom tracks vs 221 real Normal tracks; Lagging, Exposed and Rescuing excluded):

| Feature | KS D | p |
|---|---|---|
| Median step speed | 0.059 | 0.53 |
| Still-away steps before final frame | 0.014 | 1.0 |
| Afterimage tail | 0 | 1.0 |
| Spawn distance (reported) | 0.192 | — (aim < 0.2 met) |
| Lifetime (reported) | 0.80 | — |

Judgement call: White Noise lives 1.5 s, so the gated features are per-step rather than per-track means. The per-track means still separate (D ≈ 0.49) and are reported.

**Median match length** (15 seeds)

| | Before U2c | Attempt 1 | Attempt 2 |
|---|---|---|---|
| Idle killer | 22.9 s | 227.7 s | 384.7 s |
| Killer bot | 53.7 s | 136.9 s | 600.0 s |

Risk: the 600 s killer-bot median is not a balanced match. In 9 of 15 seeds the bots are all eliminated with 0–1 terminals done, and the match times out on the hidden idle human.

**Gate**
- Build with `-warnaserror`: 0 warnings, 0 errors.
- Server tests: 646/688 pass. The 39 failing names (42 cases) are all QA tests.
- Harness.Tests: 284/284.
- Harness: 80/100 on 3 runs, H3 timeout only. No servers were left running.
