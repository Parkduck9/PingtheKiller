# Q5b-client implementation report (attempt 1)

**Implementer:** Claude Opus. **Merge:** `a28ed47` (Q5a S0, a fast-forward). There was no `SHARED_CHANGED`.

**Ownership change (orchestrator):**
- Art comes from a sibling `Editor/Setup/ChasePropSetup.cs` → `Art/ChaseProps/**` + `Resources/ChaseProps.asset`. WorldArt, WorldArtSetup and SkillFx were not touched.
- The stun burst is its own file (`StunBurst` in `World/ChaseActionPose.cs`).

**Art:**
- One prefab per Shared box, built from `ChasePropShapes`; the primitive fallback uses the same shapes.
- Shutter: posts, overhead housing, and Up / Down / Broken groups. Down is a slatted panel 1.2 m high with a 0.12 s drop.
- Rail: 3 m × 1.0 m, with a light vault band and plates.
- Toon materials, no colliders.

**Animations:** procedural `LateUpdate` overlay from `PlayerState.Chase`, timed to the server durations: pull 0.3 s, a hop vault at 0.8 / 1.0 / 1.6 s, a hammering break, and a stun stagger. **Deviation:** no Kevin Iglesias or Mixamo clip, because the Motion controller belongs to U8.

**Stun:**
- Survivors see a Hovl circle tinted `danger` over the drawn Killer model, or a still ring under Reduced motion.
- The Killer gets a 0.25 s FOV impulse and the `Stunned` effect line.

**Sounds:** procedural and positional: ShutterSlam, ShutterCrunch, VaultThud. None plays on the baseline snapshot.

**Prompts:** they follow the existing `Press/Hold [E] to …` form, not the brief's literal `E  Pull shutter`. They fill empty slots only. HUD_SPEC §15 has the full set.

**Hooks:** 13 lines in `ClientApp.cs`, a Shared reference in the Setup asmdef, and the QA audit allow-list. NetClient is unchanged because no new packet id exists.

**Tests:** 19 cases in `ChasePropsTests` (1486 → 1505).

**Gates:** EditMode **1505/1505** passed (`Q5b-runs/editmode-2.xml`; run 1 had one failure, the QA audit, now fixed). BuildWindows succeeded twice (`build-1/2.log`). `dotnet build -warnaserror`: 0 warnings, 0 errors.

**Shots:** `docs/qa/Q5b-screens/editor-*` are editor renders of the client code fed with hand-made snapshots (Up, Down, Broken, Killer eye, rail, mate vault, Killer stunned). Live offline exe at 1280x720 for 240 s, Killer and Survivor: 0 exceptions, 12 and 14 shots in `Q5b-screens/live-*`. No chase events, as expected, because the S0 server has no rules.

**Unverified:**
- No live Q5a rules: the server never changes a shutter or sets Chase.
- On the Killer client, it is assumed that the own entry carries `Chase.Stunned`.

**Risks:** the bone overlay can fight U8's IK, and the hop does not use a clip.
