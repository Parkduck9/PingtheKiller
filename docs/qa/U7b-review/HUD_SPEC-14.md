## 14. Skill feedback (U7b)

Playtest finding (11/04): after a skill press the player cannot tell what happened. This section gives each of the nine skills (§8) a world effect, a result line and a sound per viewer, built only from packets the viewer already receives. It amends §4.8 (reason lines, `skill_use`), §6.1c (copy), §6.5 (`skill_use`) and §12.5 (Reduced motion); those sections point here. All §0 rules apply.

### 14.0 Source: who receives which effect
One packet, `S2C_SkillEffect {CasterId, SkillId, X, Z, Duration}`, sent as in `Server.Core/Runtime/GameServer.Skills.cs` (unchanged). Skill ids are role-local (Killer 0 and Survivor 0 are different skills), so the receiver resolves the skill first.

| Skill | Sent to | `CasterId` | `X, Z` | `Duration` |
|---|---|---|---|---|
| Ping scanner (K0) | the Killer | Killer | the Killer's position | 1.5 |
| Ping scanner (K0) | the scanned Survivor only (Normal or Lagging) | Killer | **0, 0** | 1.5 |
| Overclock (K1) | the Killer only | Killer | the Killer's position | 3 |
| Packet afterimage (K2) | nobody (passive) | — | — | — |
| Firewall block (K3) | every player | Killer | the locked exit's position (a static map point) | 5 |
| Dummy packet (S0), Stealth relay (S1), White noise (S4) | every Survivor; never the Killer | caster | the caster's position | 3 / 2 / 1.5 |
| Traffic overload (S2) | every Survivor | caster | the caster's position | 2 |
| Traffic overload (S2) | the Killer | **0** | **0, 0** | 2 |
| Emergency restore (S3) | every Survivor | caster | the restored Survivor's position | 0 |

**Resolution rules (normative):**
- **Killer client.** It acts on an effect only when `CasterId` = own id (own skill), or when `CasterId = 0` and `SkillId = TrafficOverload`. It ignores everything else. A Survivor skill never reaches the Killer; if one did, this rule would drop it, so no Killer-side output can depend on a decoy skill.
- **Survivor client.** `CasterId` = own id → own skill. `CasterId` = a Survivor of the last WorldState → that Survivor's skill. Otherwise → the Killer's skill (only K0 and K3 arrive). The client never looks up the Killer's `CasterId`, never matches it to the Killer entry (§0.1) and never places anything from it.
- **No Killer coordinates on a Survivor client.**
  - The Survivor-bound Ping scanner carries `0, 0`. Nothing is ever placed from it.
  - The Survivor-bound Firewall block carries a static exit position. It is used only to pick the exit (the static exit within 1 m of `X, Z`). Everything is drawn at that exit's static switch point (`FixedMap.ExitSwitches[i]`). If no exit is within 1 m, nothing is placed and the line says `An Exit switch` instead of `Exit 2 switch`.
  - Nothing is ever attached to the drawn Killer model.

### 14.1 Global rules

**Viewers.** For each effect, the client is exactly one of these:
- **caster**: its own skill;
- **affected**: the skill changed this player's own state (the rule is given per skill);
- **ally**: any other Survivor client that received it;
- **Killer**.

Spectators (own `Status` Eliminated or Escaped) see world effects and hear positional sound only: no line and no 2D sound.

**Result line, Survivor (new element 14.1a).**
- **Block:** 672×40 u, `panel` fill, 1 u `line` border, padding 8. It sits in the §13.1 bottom-center stack, **directly above the prompt row**. When the prompt row is hidden, it takes the prompt row's place, and the stack closes up as before.
- **Content:** a 4×18 u bar in the line's colour, an 8 u gap, then the text in M 400 `text`. One line, at most 64 characters (≈ 634 u ≤ 640 u). Names are `Slot.Nickname`, ellipsized at 12 characters (§0.3).
- **Bar colour by meaning:**
  - `danger`: the Killer acted against you;
  - `signal`: you are made visible;
  - `ally`: a Survivor-team outcome;
  - no bar and `muted` text: a reason line (14.3).
- **Time:** shown for **2.5 s** (reason lines 2.0 s), then removed in one frame. Numbers in the line are the skill's static duration, not a countdown. The live countdowns stay where they are: the meter (§4.1), the active arc (§4.8) and the objective row (§13.4).
- **One line at a time, never stacked (SLOP §5).** Priority: affected (`danger` first) > caster > ally > reason. A new line replaces the shown one when its priority is equal or higher. A lower one is dropped, not queued.
- **Stack geometry:** with every child shown, the stack top is 96 + 48 + 8 + 40 + 8 + 40 + 8 + 144 + 8 = 400 u. While hacking (ring shown, objective row normally hidden), a line lifts the ring by 48 u. The ring top is then 344 u from the bottom, at 61.8 % of the 720p height, over the feet (67 %, §4.10). This happens only while a line shows during a sync check (≤ 2.5 s), and it is accepted like the §13.1 case.
- Hidden while the play menu is open (§5); its timer keeps running.

**Result line, Killer.** The §6.1c effect line (320×32, S, with the 4×18 u bar slot) carries the Killer's lines.
- Own-skill lines tick (`· 1.2 s`, `Seconds1`) for exactly the effect's duration. Reason lines show for 2.0 s.
- One line at a time: Slowed (`danger` bar) > own skill > reason (`muted`, no bar).
- At most 34 characters (282 u, §6.1).

**World effects (VFX).**
- **Two kinds only:**
  - a **Hovl one-shot**: a non-`Loop` prefab from `Client/Assets/ThirdParty/Hovl Studio/Map track markers VFX/Prefabs/`;
  - a **procedural ring**: a flat unlit ring 0.02 m above the ground, line width 0.08 m, one palette colour, depth-tested, no texture, no glow.
- **Every effect:**
  - encodes a live event;
  - is depth-tested (never drawn through walls);
  - lasts ≤ 2 s, or exactly as long as the effect;
  - is cleared (`StopEmittingAndClear`) at its stated end at the latest.
- **Tint:** the particle `startColor` is set to the palette colour, keeping the prefab's alpha, as the Exit marker does (`WorldProps.SetExitLook`). Meanings:
  - `signal`: a Signal (real or fake) is transmitted here;
  - `ally`: a Survivor-team effect;
  - `danger`: Killer-caused, against Survivors (Survivor view only);
  - `text`: neutral; used for every Killer-view effect.
- **No bloom:**
  - The marker material is one runtime copy of the vendor material, with `_Emission` 0.8. The vendor value of 18 burns tints to white, and the Exit marker's 1.4 is above the URP bloom threshold of 0.9 (`DefaultVolumeProfile`).
  - The `Flash` and `EndSparks` children (pure glow puffs) are disabled in every pooled instance.
  - If a 1080p capture still shows bloom on an effect, that effect falls back to its procedural ring.
- **Size:** the stated outer diameter in metres. The prefab uses `scalingMode = Hierarchy`, and the root is scaled from the prefab's own size.
- **Reduced motion** (the §12.5 setting or `-ptk-reduce-stutter`):
  - every Hovl one-shot is replaced by its procedural ring at the final diameter, still, with the same colour and duration;
  - rings that would grow appear at full size;
  - the Overclock FOV change and the Ping scanner sweep are off;
  - lines and sounds are unchanged.
- **Pooling:**
  - Everything is created at warm-up (the first match start): 3 instances per Hovl prefab used, 6 procedural rings, 2 2D audio sources and 4 positional ones.
  - After that, no effect calls `Instantiate` or `new GameObject`. When a pool is empty, its oldest instance is reused.
  - Everything is cleared on MatchStart, GameResult and disconnect.

**Sound.**
- One cue per event per viewer. A skill's cue **replaces** the generic `skill_use` (§4.8, §6.5) for that skill. `skill_ready` is unchanged.
- **Clips** come from `Client/Assets/ThirdParty/Sci-Fi Weapons-Bullet Hell Sound Effects Pack/AUDIO/`; the vendor files stay untouched.
- **Gain:** `AudioSource.volume = (0.4 / clip peak) × 10^(dB/20)`. Each clip's peak therefore sits at the 0 dB cue reference (`capture_confirm`, amplitude 0.4) before the stated dB is applied. The peaks are in the 14.4 table.
- **Maximum length:** a clip is stopped with a 50 ms fade at the stated cut, which is never longer than the effect, so no tail outlives the effect. No loops.
- **2D** means `spatialBlend 0`.
- **Positional** means:
  - `spatialBlend 1`, spread 0, no Doppler;
  - the stated level up to 2 m, then linear in dB down to −40 dB at the stated range, and muted beyond it (the §6.5.1 curve).
- **Priority (§0.7):**
  - Affected cues of Killer skills sit at the "exit opened/locked" tier (Firewall) or just below "capture" (scan).
  - Caster cues sit at the skill tier.
  - Ally positional cues sit at the world-sound tier and duck like §6.5.1 world sound.
  - A new skill-tier cue cuts the previous skill-tier cue (30 ms fade). It never cuts a higher tier.
- Reason lines and refused presses make no sound, because no state changed.

### 14.2 Per-skill feedback
Copy in `code` is final. `N` = a nickname (ellipsized at 12 characters); `E` = the exit number (`Id + 1`).

#### 14.2.1 Ping scanner (Killer 0)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | A procedural ring, `text`, centred on the own rendered position. Its radius sweeps from 1 to 16 m over 0.6 s, then it is removed. Reduced motion: none. **No blip is marked:** the client cannot know which instance is the scanned Survivor (§6.3). | `Nearest Survivor shown · 1.2 s` (ticking, 1.5 s) | `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_2.wav`, −9 dB, 2D, full length (0.40 s) |
| Affected (the scanned Survivor) | Hovl `Marker 5 Circle`, `signal`, at the own character's feet, 2 m, 0.6 s. The position is the own entry's `X, Z`, never the effect's. It is a one-shot for the event; the state itself stays on the meter only (§4.10). | `Scanned: the Killer sees you · 1.5 s` (`danger` bar) | `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_1.wav`, −6 dB, 2D, full length (0.56 s) |
| Ally Survivors | — (not sent) | — | — |

**Safety:**
- The Killer uses `CasterId` (= own), `SkillId` and `Duration`. The ring's centre is its own position.
- The Survivor uses `CasterId` (not a Survivor → the Killer's skill), `SkillId`, `Duration` and its own entry's `X, Z`. It never reads the effect's `X = Z = 0`.
- While the target is Lagging (always visible), the line still shows: it is true, and it says that the scan picked them.

#### 14.2.2 Overclock (Killer 1)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | No world object (first person has no body). The camera FOV widens by 4°, eased in over 150 ms, held for `Duration`, and eased out over 150 ms when Recovering starts. Reduced motion: none. | `Overclock: 1.5× speed · 2.1 s`, then `Recovering: cannot move · 0.8 s` (static 1 s after `Duration`) | Start: `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun1_Set_to_Charge_Part_2.wav`, −9 dB, 2D, full length (0.68 s). Recovering start: `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Reload_Magazine_Out_1.wav`, −12 dB, 2D, full length (0.23 s). |
| Survivors | — (not sent; a Survivor with line of sight only sees the model move faster, §4.10) | — | — |

**Safety:** Killer only. It uses its own `CasterId`, `Duration` and the static `OverclockRecoverySeconds`.

#### 14.2.3 Packet afterimage (Killer 2, passive)
No feedback for anyone:
- No packet exists.
- The Killer never reads the `Afterimage` flag (§6.3 rule 1), so nothing may mark an afterimage.
- Survivors never receive the flag.

The Killer's skill block keeps `Passive — always on`. A press shows the reason line `Packet afterimage is passive.` (14.3).

#### 14.2.4 Firewall block (Killer 3)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | Hovl `Marker 3 Zone`, `text`, at the locked exit's switch point, 3 m (the switch's 1.5 m hold radius), 1.0 s. The §13.4 marker shows `Locked` for the full 5 s. | `Exit E switch locked · 4.2 s` (ticking, 5 s) | `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Set_Part_1.wav`, −9 dB, 2D, full length (0.50 s) |
| Affected Survivor: when the effect arrives, its own `SelfState.ActionKind = 1`, or its own position is within `ExitSwitchRadius` of that switch | Hovl `Marker 7 Danger zone`, `danger`, at the switch point, 3 m, 1.0 s | `Exit E switch locked by the Killer · 5 s` (`danger` bar) | `Mechanics/SFX_GUNS_MECH_SCIFI_Pistol_or_Rifle_Set_Part_2.wav`, −6 dB, 2D, full length (0.56 s) |
| Ally Survivors | The same `danger` one-shot, seen only if in view | — (the objective row already reads `Locked 5 s`, §13.4) | The same clip, positional at the switch point, −12 dB at 2 m, range 30 m |

**Safety:**
- Every client uses `SkillId`, `Duration`, and `X, Z` only to pick the static exit (14.0).
- The Survivor also uses its own `SelfState.ActionKind` and its own entry's position.
- The Killer's position is never in this packet.

#### 14.2.5 Dummy packet (Survivor 0)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 2 Pointer`, `signal`, at the effect's `X, Z`, 1.5 m, 0.8 s. It marks where the fake Signal started. The fake then moves like a Survivor and is not on the Survivor wire, so nothing follows it. | `Dummy packet: fake Signal sent from here · 3 s` (`ally` bar) | `Shoot/Energy/SFX_SCIFI_WEAPON_Energy_Shoot_Big_Gun3_1.wav`, −9 dB, 2D, cut at 0.6 s |
| Ally Survivors | The same one-shot, if in view | — | The same clip, positional at `X, Z`, −15 dB at 2 m, range 20 m |
| Killer | **Nothing beyond §6.2–§6.5.1.** No packet arrives. The fake is an anonymous instance, drawn, voiced and cued exactly like a real Signal Survivor, with no spawn effect (§6.3 rule 2). | — | — |

**Safety:**
- Survivors use `CasterId`, `SkillId`, `X, Z` (the caster's position, which Survivors already have) and `Duration`.
- **Decoy safety:** the Killer client has no code path for this skill (the 14.0 Killer rule). Its output for the fake equals its output for a real Signal Survivor at the same position and motion.

#### 14.2.6 Stealth relay (Survivor 1)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | A procedural ring, `ally`, centred on `X, Z`. Its radius is exactly `StealthRelayRadius` (5 m, the rule's area). It grows from 0 to 5 m over 0.15 s, is held until 2.0 s (`Duration`), then removed. | `Stealth relay: you and 2 Survivors are Silent · 2 s`; with one: `you and 1 Survivor`; with none: `Stealth relay: you are Silent · 2 s` (`ally` bar) | `Skill/SFX_SCIFI_ENERGY_Skill_Whoosh_Electrified_1.wav`, −12 dB, 2D, cut at 2.0 s (300 ms fade) |
| Affected: another Survivor whose own entry has `Stealthed` set, within 5 m of `X, Z`, in the first WorldState after the effect | The same ring | `You are Silent · 2 s · Stealth relay from N` (`ally` bar) | The same clip, −15 dB, 2D, cut at 1.0 s |
| Ally Survivors (not affected) | The same ring, if in view | — | The same clip, positional at `X, Z`, −18 dB at 2 m, range 20 m, cut at 1.0 s |
| Killer | Nothing (not sent). Covered Survivors simply stop being sent (§6.2). | — | — |

**The caster's count:**
- It counts other Survivors with `Status = Normal` and `Stealthed` set, within 5.0 m of `X, Z`, in the first WorldState at or after the effect.
- The line appears with that snapshot (at most 1 tick later).
- A Survivor already covered by an earlier relay is counted too (derived; accepted).
- Lagging Survivors are not covered by the server and are not counted.

**Safety:** `CasterId`, `X, Z` and `Duration`; the Survivors' `Flags.Stealthed`, `Status` and `X, Z` (full state on Survivor clients).

#### 14.2.7 Traffic overload (Survivor 2)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 6 Arrows`, `signal`, at the effect's `X, Z` (the caster's server position when the effect arrives), 2 m, 0.5 s | `Traffic overload: you are Signal, the Killer is slowed · 2 s` (`signal` bar) | `Shoot/Electric/SFX_SCIFI_WEAPON_Electric_Shoot_Medium_Gun_1.wav`, −9 dB, 2D, cut at 0.6 s |
| Ally Survivors | The same one-shot at the caster, if in view | `N slowed the Killer · 2 s` (`ally` bar) | The same clip, positional at the caster, −15 dB at 2 m, range 20 m |
| Killer (affected) | **None.** The effect has no position (`0, 0`), and the client cannot know which instance is the caster, so no instance is marked. | The existing `Slowed by traffic overload · 1.6 s` (`danger` bar) | `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun_Out_of_Ammo.wav`, −6 dB, 2D, cut at 1.0 s |

**Safety:**
- Survivors use `CasterId` (for the ally's name and live position from their WorldState) and `Duration`.
- The Killer uses only `CasterId = 0`, `SkillId` and `Duration`.

#### 14.2.8 Emergency restore (Survivor 3)
The restored Survivor (the "target") is derived. It is the Survivor in the WorldState before the effect who:
- is not the caster;
- has `Status = Lagging`;
- is nearest to `X, Z`, within 0.5 m.

With no match, the target is unnamed (`a Survivor`).

| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 4 Pillar`, `ally`, at `X, Z`, 1.5 m, 1.0 s | `Restored N · no longer Lagging` (`ally` bar) | `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_Start_2.wav`, −9 dB, 2D, cut at 1.0 s |
| Affected: own status Lagging → Normal, and own position within 0.5 m of `X, Z` | The same one-shot | `N restored you · no longer Lagging` (`ally` bar). The meter's `Protected` tag follows from `InvulnerableLeft`. | The same clip, −6 dB, 2D, cut at 1.0 s |
| Ally Survivors | The same one-shot, if in view | `N restored N` (`ally` bar), e.g. `Mina restored Rio` | The same clip, positional at `X, Z`, −15 dB at 2 m, range 20 m |
| Killer | Nothing (not sent). The Lagging instance turns Normal or leaves the view by the §6.3 rules. | — | — |

`Duration = 0`, so the caster's active arc does not show (§4.8). The line and the one-shot use their own times.

**Safety:** `CasterId`, `X, Z` (a Survivor's position), the Survivors' `Status` and `X, Z`, and lobby names.

#### 14.2.9 White noise (Survivor 4)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 3 Zone`, `signal`, at `X, Z`, 3 m, 1.0 s. The fakes' positions are not on the Survivor wire, so nothing is drawn where they are. | `White noise: fake Signals sent across the map · 1.5 s` (`ally` bar) | `Skill/SFX_SCIFI_SKILL_Fire_Rain_1.wav`, −12 dB, 2D, cut at 1.5 s (200 ms fade) |
| Ally Survivors | The same one-shot, if in view | `N sent fake Signals across the map · 1.5 s` (`ally` bar) | The same clip, positional at `X, Z`, −18 dB at 2 m, range 20 m, cut at 1.0 s |
| Killer | **Nothing beyond §6.2–§6.5.1**, as for Dummy packet. The fakes are five more anonymous instances, staggered by the server, with no spawn effect. The 6-nearest edge cap and the 4-voice cap apply to them equally. | — | — |

The copy gives no count: the server may place fewer than `WhiteNoiseSignalCount`, and the entry cap may hide some.

**Safety and decoy safety:** as in 14.2.5.

### 14.3 Presses with no effect
The client sends `C2S_UseSkill` on every press, except while its own `Status` is Lagging, Eliminated or Escaped. Reason lines show at once: on the Survivor line 14.1a with no bar, and on the Killer's 6.1c line in `muted`. They are replaced if an own effect arrives after all.

| Case | Known how | Survivor line | Killer line |
|---|---|---|---|
| Cooldown | `SelfState.SkillCooldown > 0` | None: the ring and its number are the feedback (§4.8) | None: the block reads `Cooldown 7 s` |
| Lagging | own `Status` | `Skills are off while Lagging.` | — |
| Emergency restore, no target | derived, §4.8 | `Emergency restore needs a Lagging Survivor next to you.` | — |
| Firewall block, no switch in progress | derived, §6.1d | — | `No Exit switch in progress` |
| Packet afterimage | static | — | `Packet afterimage is passive.` |
| Refused for an unknown reason | Derived Ready, but within 0.5 s of the press neither an own effect arrives nor `SkillCooldown` becomes > 0 (§4.8) | `Skill not used — the server did not accept it.` | `Not used · server did not accept` |

**Protocol gap (not fixed here).**
- The server sends nothing when it refuses a use (`TryUseSkill` returns silently). The client can only infer a refusal from the 0.5 s timeout, and cannot say why. Possible reasons: a server-side cooldown edge, a target that moved out of range, a switch that stopped, or the dev sandbox.
- The client also cannot know:
  - in advance, which exit Firewall block will lock;
  - how many White noise fakes were placed or shown;
  - whether the Dummy packet fake is currently shown to the Killer.
- A rejection reason would need a new packet or field (`Shared/` and the server). That is out of scope.

### 14.4 Sound table
Peaks are measured from the vendor files (int16 peak ÷ 32768). Lengths are the files' own.

| Clip | Length | Peak | Used for |
|---|---|---|---|
| `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_2.wav` | 0.40 s | 0.82 | Ping scanner, Killer |
| `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_1.wav` | 0.56 s | 0.75 | Ping scanner, scanned Survivor |
| `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun1_Set_to_Charge_Part_2.wav` | 0.68 s | 0.78 | Overclock start |
| `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Reload_Magazine_Out_1.wav` | 0.23 s | 0.63 | Overclock, Recovering start |
| `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Set_Part_1.wav` | 0.50 s | 0.69 | Firewall block, Killer |
| `Mechanics/SFX_GUNS_MECH_SCIFI_Pistol_or_Rifle_Set_Part_2.wav` | 0.56 s | 0.85 | Firewall block, Survivors |
| `Shoot/Energy/SFX_SCIFI_WEAPON_Energy_Shoot_Big_Gun3_1.wav` | 1.48 s | 0.84 | Dummy packet |
| `Skill/SFX_SCIFI_ENERGY_Skill_Whoosh_Electrified_1.wav` | 2.60 s | 0.85 | Stealth relay |
| `Shoot/Electric/SFX_SCIFI_WEAPON_Electric_Shoot_Medium_Gun_1.wav` | 1.50 s | 0.84 | Traffic overload, Survivors |
| `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun_Out_of_Ammo.wav` | 1.01 s | 0.78 | Traffic overload, Killer slowed |
| `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_Start_2.wav` | 1.13 s | 0.69 | Emergency restore |
| `Skill/SFX_SCIFI_SKILL_Fire_Rain_1.wav` | 1.71 s | 0.85 | White noise |

The clips were chosen by name, length and envelope (most of the energy early). None has been auditioned, so the levels are 🔶 for playtest tuning. Each clip belongs to one skill, so every skill sounds distinct, and the caster and the target of a Killer skill hear different clips.

### 14.5 Build notes
- **Rules type:** one pure type (no `UnityEngine`) maps (own role, own id, resolved skill, viewer context) to a feedback record. The record holds: line text and colour; VFX kind, prefab, tint, diameter and time; sound clip, dB, 2D or positional, and cut.
- **Player:** a `SkillFx` component under `World/` only plays records from its pools.
- **Tests** drive the rules type: per skill and viewer, decoy safety, the `0, 0` rule and durations. They also count pool instances after warm-up.
- The Killer world layer keeps its §6.3 path unchanged; `SkillFx` adds nothing to blips.
- **§12.5 Reduced motion** now also covers skill effects (14.1). The note under the toggle (S `muted`, at most 2 lines in 576 u) becomes: `Lagging shows every frame instead of holding the picture at 8 Hz. Skill effects show as still rings.`
