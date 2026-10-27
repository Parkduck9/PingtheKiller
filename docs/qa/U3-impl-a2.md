# U3 implementation — attempt 2

Updated existing `Tools/Harness/` MatchChecks, MatchSession, Measurements, WireClient, and README; extended `Tools/Harness.Tests/` LobbyServer, LobbyFlowTests, and MeasurementTests. H4 retains its survivor socket, records defensive disconnects, replaces only closed sockets without asserting restored identity, requires positive movement with one opposite-direction retry, and reports actual flood rate (25×20 inputs, 1.15-second allowance). H2's inferred tick period is evidence only. README retains the server-flags table.

Verified:
- `dotnet build Tools/Harness/Harness.csproj -warnaserror`: zero warnings/errors.
- `dotnet test Tools/Harness.Tests/Harness.Tests.csproj`: **40/40 passed**, including broken reconnect, 10× speed, stationary movers, opposite-direction recovery, defensive disconnects, H2 thresholds, and flood jitter.

Restricted-workspace verification: ordinary build hit denied SDK-profile access. Shell-only workaround (existing cached packages; audit disabled for verification):

```powershell
$env:APPDATA = "$PWD/artifacts/profile/roaming"
$env:LOCALAPPDATA = "$PWD/artifacts/profile/local"
$env:TargetPlatformSdkPath = "$PWD/artifacts/sdk-unused"
$env:TargetPlatformDisplayName = 'Unused'
$env:UseSharedCompilation = 'false'
New-Item -ItemType Directory -Force $env:APPDATA,$env:LOCALAPPDATA | Out-Null
Set-Content artifacts/harness-nuget.config '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>'
$env:RestoreConfigFile = "$PWD/artifacts/harness-nuget.config"
$env:NuGetAudit = 'false'
```

Run the two verification commands above in that shell. Generated artifacts are ignored. Real-server H1–H5 remain unverified pending U2a; H6 remains explicitly skipped. H3 coordinate-byte coincidences remain a conservative failure risk. No files outside owned scope changed; Shared/Server/Client/solution untouched. No commits.
