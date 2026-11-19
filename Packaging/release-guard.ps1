<#
.SYNOPSIS
  Shared release check, dot-sourced by build-release.ps1 and pack-msix.ps1. Windows PowerShell 5.1 compatible.
.NOTES
  The Store build must not ship editor tooling: com.coplaydev.unity-mcp (MCPForUnity*.dll) or its dependency
  Newtonsoft.Json.dll. BuildScript.BuildWindowsRelease filters them out; this is the same rule as
  BuildScript.IsEditorToolingAssembly, applied to a build folder on disk.
#>

function Get-EditorToolingAssemblies([string]$BuildDir) {
    return @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File -Filter '*.dll' -ErrorAction Stop |
        Where-Object { $_.Name -like 'MCPForUnity*' -or $_.Name -ieq 'Newtonsoft.Json.dll' })
}

function Assert-NoEditorTooling([string]$BuildDir) {
    $found = Get-EditorToolingAssemblies $BuildDir
    if ($found.Count -ne 0) {
        $names = ($found | ForEach-Object { $_.FullName.Substring($BuildDir.Length).TrimStart('\') }) -join ', '
        throw ("RELEASE BUILD REJECTED: editor-tooling assemblies must not ship in the Store build, found {0} in {1}. " +
            "Build with Packaging\build-release.ps1 (BuildScript.BuildWindowsRelease), not BuildWindows.") -f $names, $BuildDir
    }
}
