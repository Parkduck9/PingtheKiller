# U4l-gapfill — implementation report, attempt 1 (Claude Opus)

**Detector (`WorldArtGapScan.cs`):**
- The city is placed as WorldProps places it, with a temporary MeshCollider on every art mesh (back faces hit).
- Eye points stand 1 m and 2 m out from every wall and obstacle face, every 1 m, where `CanStand(r=0.2)` holds.
- Rays fan ±60° in 5° steps at 0.3, 1.0, 1.7 and 2.6 m. Box height is 3 m for walls and `MapGeometry.ObstacleHeight` 1.2 m for obstacles.
- See-through: more than 0.25 m inside a box without art, or the ray exits the box. Ignored: chords under 0.05 m, and rays that hit the entered face more than 60° off its normal (these skim 4 cm window recesses).
- Also checked: per-axis fill, through-gaps above the podium (4 m and 5.5 m), and sky between the skyline pieces up to 12° pitch.

**Before → after:**
- See-through rays: 10,754 rays in 136 clusters → 0.
- Upper through-gaps: 470 → 0.
- Sky gaps: 8 → 0. 5 roofline notches at 15–19° remain, listed in the after report.
- Fill: obstacles 1.000, podiums 1.000.
- The before scan ran detector v1. Its 9 wall rays were grazing artefacts.

**Changes:**
- **Obstacles:** cars and works pens are replaced:
  - long boxes and odd elongated boxes get a generated 1.45 m ribbed container;
  - the other elongated boxes get a dumpster row;
  - square boxes get dumpsters or a crate stack 1.05–1.25 m tall.
  - A dark core inset 0.1 m sits under the dumpsters and crates; dumpsters also get corner posts.
- **Fitted houses (walls 9, 12, 15, 22, 28):** a two-storey core block, 0.2 m inside the house footprint.
- **Skyline:** a 12 m backing row 7–13 m behind the house line.
- **Podiums:** unchanged; they were already solid.

**Counts (triangles / renderers):**

| | Before | After |
|---|---|---|
| Walls | 289,904 / 60 | 294,266 / 65 |
| Backdrop | 1,280,646 / 67 | 1,301,878 / 71 |
| Obstacles | 52,826 / 43 | 41,612 / 70 |

The obstacle renderers rise 63 %. They are small static meshes (cores, dumpster slots).

**Tests:**
- New `WorldArtGapTests` (5 tests): see-through, uppers, sky, obstacle fill, podium. They use a reduced density.
- No existing test changed.

**Gates:**
- EditMode: 1491/1491.
- BuildWindows: Success.
- `dotnet build -warnaserror`: 0 warnings, 0 errors.
- Offline exe, Survivor, 120 s: exit 0, 0 errors.

**Shots:** `docs/qa/U4l-screens/before/` and `after/`: 71 each, the same `views.txt`.

**Left / risks:**
- The cars are gone. A stretched car still leaves the 1.0 m ray open over its hood.
- The house core may show through model notches. I did not review this visually.
- Setup reruns churn fileIDs, as before.
