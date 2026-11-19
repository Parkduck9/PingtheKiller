# S1-msix implementation report (attempt 1) — Claude Sonnet 5.5

**Files:** `Client/ProjectSettings/ProjectSettings.asset` (productName "Ping the Killer", companyName placeholder `PingTheKillerDev`, bundleVersion 1.0.0), `Client/Assets/Editor/BuildScript.cs` (new `BuildWindowsRelease` -> `Client/Builds/StoreWin64`, shared `Build()`), `Packaging/` (`pack-msix.ps1`, `AppxManifest.template.xml`, `make-logos.ps1`, `Assets/*.png`), `docs/store/MSIX_PACKAGING.md` (new §0 tested path, Korean owner steps), `.gitignore` (Packaging/layout, out, *.msix).

**Verified:**
- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings, 0 errors.
- Unity release build (Mono; IL2CPP Windows not installed): succeeded.
- `Packaging\pack-msix.ps1` (placeholder identity): "Package creation succeeded", zero errors. Output `Packaging/out/PingTheKiller_1.0.0.0_x64.msix`, 65.1 MB (68,271,849 bytes).
- `makeappx unpack`: exe, UnityPlayer.dll, PingTheKiller_Data, MonoBleedingEdge present; 0 DoNotShip/.pdb entries.
- Offline run from the staging folder (`-ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-quit-after 60`): exit 0, no errors/exceptions in log; file listing before/after identical (196 entries).

**Write audit:** with no flags nothing writes into the install folder. Only `-ptk-shots <dir>` (DevCapture) writes PNGs, and only with that flag. Dev flags stay compiled in (the offline smoke test needs them). Changing product/company resets PlayerPrefs (fine pre-release).

**Unverified:** local install (`Add-AppxPackage`), Store certification, WACK, firewall prompt on a clean PC, 1.0 -> 1.1 update.

**Owner items:** Partner Center Identity Name / Publisher / PublisherDisplayName; real companyName; approve logos (owner to approve: flat amber ping mark on #101113, Inter text on the wide tile, per SLOP section 8); Store listing images; runFullTrust text (in doc).

**Risks:** `layout` run leaves unsigned package untested on Store tooling; the Unity build rewrites line endings in ~17 asset files (reverted, not committed); first Unity attempt was killed by me by mistake (misjudged elapsed time), the retry succeeded.
