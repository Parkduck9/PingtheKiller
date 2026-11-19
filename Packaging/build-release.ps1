<#
.SYNOPSIS
  Store release build: Unity batch BuildScript.BuildWindowsRelease -> Client\Builds\StoreWin64, release check, and
  (with -Pack) Packaging\pack-msix.ps1. Windows PowerShell 5.1 compatible.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\build-release.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\build-release.ps1 -Pack -IdentityName "12345Studio.PingtheKiller" `
    -Publisher "CN=00000000-0000-0000-0000-000000000000" -PublisherDisplayName "Studio" -Version 1.0.1.0
.NOTES
  Editor tooling (com.coplaydev.unity-mcp, Newtonsoft.Json) is stripped inside the build (BuildScript's
  IFilterBuildAssemblies filter), not by editing Packages\manifest.json: removing the git package would make the
  Package Manager delete it from Library\PackageCache, and getting it back needs GitHub. This script therefore never
  writes the manifest; it checks that manifest.json and packages-lock.json are byte-identical after the build.
  The Unity project must not be open in an editor (Unity refuses a second instance on the same project).
#>
param(
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe',
    [string]$LogFile = '',
    [switch]$Pack,
    [string]$IdentityName = 'PingTheKillerDev.PingtheKiller',
    [string]$Publisher = 'CN=PingTheKillerDev',
    [string]$PublisherDisplayName = 'PingTheKillerDev',
    [string]$Version = '1.0.0.0'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'release-guard.ps1')

$client = (Resolve-Path (Join-Path $here '..\Client')).Path
$buildDir = Join-Path $client 'Builds\StoreWin64'
if (-not (Test-Path $UnityExe)) { throw "Unity not found at $UnityExe. Pass -UnityExe <path to Unity.exe 6000.3.25f1>." }
if (-not $LogFile) {
    New-Item -ItemType Directory -Force (Join-Path $here 'out') | Out-Null
    $LogFile = Join-Path $here 'out\build-release.log'
}
$LogFile = [System.IO.Path]::GetFullPath($LogFile)

$packageFiles = @((Join-Path $client 'Packages\manifest.json'), (Join-Path $client 'Packages\packages-lock.json'))
$hashesBefore = @($packageFiles | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })

# A stale output must never pass as this build's result.
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }

$unityArgs = @('-batchmode', '-quit', '-projectPath', ('"{0}"' -f $client),
    '-executeMethod', 'BuildScript.BuildWindowsRelease', '-logFile', ('"{0}"' -f $LogFile))
Write-Host "Unity release build (log: $LogFile) ..."
$proc = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$exitCode = $proc.ExitCode

for ($i = 0; $i -lt $packageFiles.Count; $i++) {
    if ((Get-FileHash -LiteralPath $packageFiles[$i] -Algorithm SHA256).Hash -ne $hashesBefore[$i]) {
        throw "$($packageFiles[$i]) changed during the build. Inspect it (git diff) before shipping."
    }
}
if ($exitCode -ne 0) { throw "Unity release build failed with exit code $exitCode. See $LogFile." }
if (-not (Select-String -LiteralPath $LogFile -SimpleMatch '[S1] Windows release build succeeded' -Quiet)) {
    throw "Unity exited 0 but the log has no success line. See $LogFile."
}
if (-not (Test-Path (Join-Path $buildDir 'PingTheKiller.exe'))) { throw "PingTheKiller.exe not found in $buildDir." }
Assert-NoEditorTooling $buildDir
Write-Host "OK release build $buildDir (no editor-tooling assemblies)"

if ($Pack) {
    & (Join-Path $here 'pack-msix.ps1') -IdentityName $IdentityName -Publisher $Publisher `
        -PublisherDisplayName $PublisherDisplayName -Version $Version -BuildDir $buildDir
}
