# S1-msix implementation report (attempt 2, minor fixes) - Claude Sonnet 5.5

**Changes:**
- Minor 3: `make-logos.ps1` writes `Client/Assets/Art/Icon/AppIcon.png` (256x256, same mark). `BuildScript.Build()` sets it with `PlayerSettings.SetIcons(Standalone, ..., Application)`, one slot per size Unity requests (a single-element array left the slots empty). `ProjectSettings.asset` records it. Extracted exe icon shows the amber ping mark, not the Unity logo.
- Minor 4: `make-logos.ps1` also makes scale-200 for the four logos and `Square44x44Logo.targetsize-{16,24,32,48,256}` plated and `_altform-unplated` (transparent). `pack-msix.ps1` builds `resources.pri` with makepri 10.0.26100.0 (Assets only). makepri also emits `resources.scale-200.pri` (its default split); kept.
- Minor 5: staging in a temp dir, `Packaging\layout` replaced only after a successful pack; the 4th version part must be 0 (clear error).
- Minors 1, 2, 6, 7: doc §0 (unsigned cannot be installed; Methods A/B), Korean Method B (Trusted People only, never Trusted Root), §1-§8 marked superseded, dev flags ship and are inert.

**Verified:**
- Release build succeeded.
- Default pack: zero errors, 68,242,089 bytes (65.1 MB).
- Unpack: 18 Assets files, resources.pri, no pdb or DoNotShip.
- Offline killer run (60 s, 7 bots): exit 0, no exception, file listing unchanged.
- `dotnet build -warnaserror`: 0 warnings, 0 errors.
- Version 1.0.1.1 is rejected.

**Unverified:** that Windows actually picks the variants (no install allowed), WACK, Store certification.
