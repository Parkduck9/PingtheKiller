# U4i attempt 1 — toon shading (implementer report)

quota fallback: Codex out of credits. Implementer: Claude Opus subagent. Not committed.

**Deviation:** the vendor `Toon Shaders Pro/URP/Toon` multiplies albedo twice (`baseColor.rgb *= lightColor`, which already holds it). A probe showed colours rendered as albedo²: grey .5 came out about 5× too dark. So materials use `PingTheKiller/Toon` (`Resources/Shaders/PtkToon.shader`). It is the vendor shader with one line changed; vendor files are untouched. It loads like PtkDither, so GraphicsSettings is unchanged.

**Changed:**
- `WorldMaterials.cs` (`ToonShading`, `NewMaterial` with Lit fallback), `CharacterLook.cs`, `KillerWorldLayer.cs`, `SceneLighting.cs`
- `PtkDither.shader` (`_Toon` bands), both builders, `HUD_SPEC.md`
- 207 `.mat` converted
- New: `Resources/WorldToon*.mat`, `ToonShadingTests.cs` (11 tests)
- `WorldArtTests` accepts the toon shader name

**Settings:**
- World: two bands, light .52/.52/.56, shadow .16/.19/.27. Cast-shadow edge .30–.34; ground soft edge .15–.65.
- Characters: three bands, 1 / .62 / .22.
- Rim and specular off.

**Lighting (mood):**
- Ambient sky .30→.23, equator .21→.14
- Key light 1.0→.9, fill .3→.25
- Fog 30/160→26/120 m
- Mean luminance about 25% lower (plaza 72→55).

**Exceptions:** lit window and sign Unlit materials, the Exit particles, and PtkDither.

**Results:** EditMode 1250/1250. `BuildWindows`: Success. The exe renders toon.

**Screens** (`docs/qa/U4i-screens/`, all opened):
- Exe, before and after, 720 and 1080: survivor-3p, killer-inview (`signal` outline visible), hacking.
- Editor renders (after at 720 and 1080, before at 720): plaza-wide, alley, terminal-states, exit-open, walls-lit, walls-shadow.

**SLOP §9:** about 95; no gate failures.

**Unverified:**
- Exit-open and the terminal from the exe.
- The Lit fallback inside a player.
- Draw calls.

## Attempt 2

Not committed.

**Night grade:** `ToonShading.NightGrade` runs in `WorldArtSetup.Lit()`, so names and GUIDs are unchanged.
- Settings: saturation ×.45; value ×.64 above a .12–.35 knee; cool ×(.97,.99,1.03).
- Ungraded: characters, role colours, devices, Unlit windows/signs. The warm windows are now the brightest world colours.
- `_LightTint` stays .52.
- Mean luminance: plaza 72→55→41, exe killer 69→60→46.

**Shadows:**
- `PC_RPAsset`: soft shadows High, 2 cascades (split .3), shadowmap 4096.
- Key light: `Soft`.
- World cast-shadow edge: .35–.75.
- The awning edge is now clean.
- Tests updated: LightingQa (soft needs pipeline support, ≤2 cascades) and ToonShading (edge range, plus 2 new tests).

**Ground:** the "black rectangle" is the road asphalt between the plaza and the parking pad. It is intended, and now softer (road 23 vs plaza 34). The markings and kerb are graded.

**Staging:**
- 1080 framing matches 720. Killer: same spawn and yaw. Survivor: reran until the spawn matched.
- No flag makes the own Survivor hack, so `hacking.png` does not show hacking.

**Results:** EditMode 1252/1252; BuildWindows Success. All 24 `a2-*` PNGs opened.
