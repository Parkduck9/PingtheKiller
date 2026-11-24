# Brief U7b-skillfx: skill feedback (attempt 1)

- **Implementer:** Claude Opus subagent. This is the quota fallback, because Codex is out of credits.
- **QA:** Gemini Flash, run through agy by the orchestrator. It covers code and visuals.
- **Worktree:** `C:\Unity\MultiPlayGame\PtK-work\u6c-ui`, branch `claude/u7b-skillfx`, based on main `6a155ab`. The Unity Library is warm.
- Do not commit, push or merge.
- Never send desktop input.

## Why

Playtest feedback from the user: "When I use a skill I can't tell what happened." Today a skill press gives almost nothing:
- no world effect;
- almost no sound;
- no line saying what the skill did or to whom.

Every one of the 9 skills (HUD_SPEC §8) needs clear feedback for each viewer:
- the caster;
- the affected player;
- allies who can see it;
- the opponent, where the rules allow.

## Read first

- `AGENTS.md`
- `docs/ui/SLOP_DEFINITION.md` (mandatory)
- `docs/ui/HUD_SPEC.md`: §0.7 sound rules, §4.8 skill block, §6.1 effect line, §6.3 anonymous blips (**decoy safety is normative**), §6.5 Killer sounds, §8 skill table
- `docs/GDD-v2.md` and `docs/GDD.md` §4 (skills)
- `Server.Core/Runtime/GameServer.Skills.cs`: exactly who receives which `S2C_SkillEffect { CasterId, SkillId, X, Z, Duration }`. Do not change it.
- Client today:
  - `ClientApp.cs` (effect dispatch)
  - `UI/SurvivorFeedback.cs`, `UI/SurvivorReadout.cs`
  - `Killer/KillerWorldLayer.cs`, `Killer/KillerAudio.cs`, `Killer/KillerCues.cs`
- Assets already in the project. **Do not download anything.**
  - `Client/Assets/ThirdParty/Hovl Studio/Map track markers VFX/` (16 marker prefabs)
  - `Client/Assets/ThirdParty/Sci-Fi Weapons-Bullet Hell Sound Effects Pack/AUDIO/` (94 clips)

## Phase 1: spec only, then STOP

Write **HUD_SPEC §14 "Skill feedback"** in the existing style. For each of the 9 skills, cover:

**Per viewer:** caster, affected player, ally Survivors, Killer.
- What they **see in the world**: VFX, its position, size, colour and duration.
- What they **see on the HUD**: a result line with exact copy, where it sits and how long it stays.
- What they **hear**: the exact clip filename from the Sci-Fi pack (or "none"), its volume, and whether it is 2D or positional.

**Information-safety check (per skill):**
- Only data the client already receives may be used. Name the packet field for each.
- Nothing may reveal hidden data. In particular, the Killer must never be able to tell a Dummy packet or White noise decoy from a real Survivor (§6.3).
- Survivor-bound killer effects carry no Killer coordinates (`X = Z = 0`), so nothing may be placed at the Killer's position from them.

**Global rules, as you decide them:**
- **Caster result lines** state the outcome in plain words. Examples of intent, not final copy:
  - "Ping scanner: nearest Survivor shown 1.5 s"
  - "Restored Rio"
  - "Five fake Signals sent"
- **Target lines** say what happened to you. Examples:
  - "Scanned: the Killer sees you 1.5 s"
  - "Exit switch locked 5 s"
  - "You are Silent 2 s (Stealth relay)"
- **VFX and SLOP.** The Hovl markers are additive and glowing. In world space, a glow is acceptable only if it encodes a live game state, is short, and does not bloom. Never use glow on UI.
  - Tint the markers to the HUD palette meanings (`signal`, `ally`, `danger`, `text`).
  - Keep each one ≤ 2 s, or exactly as long as the effect.
  - If a prefab cannot meet this, use a simpler procedural ring instead and say so.
- **Sound priority** follows §0.7, with one short cue per event and no loops longer than the effect. A Reduced motion setting may exist; it must disable non-essential VFX motion.
- **Self-use with no effect.** If the server rejects the use (for example Firewall block without a switch in progress, or Emergency restore without a target), state what the player sees. If the client cannot know that a use was rejected, say so: it is a server/protocol gap, so describe it and do not invent a fix.

**Stop at the end of phase 1.**
- Do not implement yet.
- Reply with: "PHASE 1 DONE" and a ≤ 150-word summary listing the open questions.
- The orchestrator reviews §14 and replies "GO" or sends changes.

## Phase 2: implement §14 (only after "GO")

**Owned files**
- `ClientApp.cs`
- `UI/**`
- `Killer/**`
- `World/**`: a new `SkillFx` component; keep `WorldView` changes minimal
- `Resources/**`: USS and any VFX/SFX references
- `Dev/**`
- `Client/Assets/Tests/EditMode/**`
- `docs/ui/HUD_SPEC.md`

**Do not edit**
- `Shared/**`
- `Server*/**`
- `ThirdParty/**` source assets. Make tinted material copies under `Client/Assets/Art/` if needed.

**Acceptance**
- **EditMode:** all green, with 0 compile errors. The main baseline is 1356.
- **New tests cover:**
  - per skill, the line or VFX choice for each viewer role;
  - the decoy-safety rule: the Killer-side output for Dummy packet and White noise is identical to a real Survivor's;
  - no Killer position is used from a Survivor-bound killer effect;
  - durations;
  - pooling: no per-effect GameObject creation after warm-up.
- **Build:** `BuildScript.BuildWindows` succeeds.
- **Screenshots** from **live offline matches**, using `-ptk-skill-at`, `-ptk-role` and the existing dev flags (add a flag if needed):
  - at least one caster view per role for 4 skills;
  - the ping-scanned target view;
  - the White noise or Dummy packet Survivor view;
  - captured at 720p and 1080p, in `docs/qa/U7b-skillfx-screens/`;
  - open every one yourself.
- **Housekeeping:** revert re-serialised out-of-scope files.

**Report:** `docs/qa/U7b-skillfx-impl-a1.md`, ≤ 300 words. Start it with the header "quota fallback: Codex out of credits". Cover:
- files;
- the per-skill table status;
- tests;
- the build;
- the shots;
- a SLOP self-score;
- gaps.
