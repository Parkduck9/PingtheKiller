# S1-msix implementation report (attempt 3) — Claude Opus 5.5

Escalated model (a1/a2 were Sonnet). Fixes QA a2 F1 and F2.

**Changed:** `Packaging/pack-msix.ps1` — the PRI config is written to a separate temp folder (`<stage>-cfg`, outside the `/pr` root, cleaned in `finally`); `<packaging>` is removed with `System.Xml.XmlDocument.RemoveChild` and saved via `XmlWriter` as UTF-8 without BOM (same as `createconfig` output); after `makepri new` the script throws if `resources.pri` is missing or any other `*.pri` exists in the stage. `docs/store/MSIX_PACKAGING.md` §0 PRI sentence corrected.

**Verified (Windows PowerShell 5.1, SDK 10.0.26100.0):** default pack exit 0, 0 error/warning lines, 65.1 MB. Unpacked: one `resources.pri` (3,352 bytes), no split PRI. `makepri dump`: Square150x150, Square44x44, StoreLogo, Wide310x150 each have `Scale-200` + neutral; Square44x44 has all 10 TargetSize candidates; no `priconfig.xml` entry. No `ptk-msix-*` temp left.

**Unverified:** install/tile rendering at 200 % (no install by rule).
