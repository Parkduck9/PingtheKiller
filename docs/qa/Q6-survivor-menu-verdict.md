# Q6-survivor-menu — Opus verdict (attempt 1): PASS

**Quota fallback:** Codex out of credits, Gemini Flash sign-in expired → Claude Opus implemented, Claude Sonnet did QA (`docs/qa/Q6-survivor-menu-1.md`).

- Spec 1–10: PASS (QA table plus my diff read). One shared `UI/PlayMenu.cs` (Stacked for the Killer, Row for the Survivor), `PlayMenuGate` for the look/key gate, `SetMenu` for both roles, Esc/Start toggles, B = Resume, Settings closes back to the open role's menu, click-relock blocked under the open menu, menu reset on start/result/leave/disconnect.
- Gates: Unity EditMode 1404/1404 (`q6-runs/qa-editmode.xml`), 0 `error CS`; `BuildWindows` success; offline Survivor exe at 720p and 1080p: menu, settings-from-menu and Leave → main menu with the cursor free, clean exit.
- SLOP §9: 97/100, no gate failure (QA). I checked `survivor-720p/play-menu.png`, `settings-play.png` and `killer-720p/play-menu.png`: Killer menu unchanged, Survivor block sits in the prompt slot above the cluster, Resume focused.
- Beyond the brief: line 3 copy corrected for GDD v2 (third capture eliminates); recorded in HUD_SPEC §5. Press edges (Space, Q) are dropped on the frame a Submit closes the menu.
- Accepted minors: a blank band above the buttons when line 3 is absent (fixed 152 u height per spec); line 3 not seen on screen (unit-tested only).
- Pre-existing, out of scope: the Settings "Reduced motion" note overflows the panel's right edge at 720p.
- Not committed: large Unity logs, player logs, and the LFS art/font files that were copied in from the main checkout for local runs (no content change against their pointers' objects).
