param([string]$CandidatePath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$CandidatePath) { $CandidatePath = Join-Path $root 'src/GHErrorAssistant/bin/Release/net10.0-windows/GHErrorAssistant.gha' }
. (Join-Path $root 'tools/release-candidate.ps1')
Assert-CandidateFramework $CandidatePath
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($CandidatePath)
if ($version.FileVersion -ne '3.1.0.0' -or $version.ProductVersion -notmatch '^3\.1\.0\+[0-9a-f]{40}$') {
    throw 'Candidate metadata is inconsistent.'
}
# The framework gate must reject the real pinned Rhino 8/.NET Framework SDK assembly.
$assets = Get-Content (Join-Path $root 'src/GHErrorAssistant/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoot = @($assets.packageFolders.PSObject.Properties.Name)[0]
$legacy = Join-Path $packageRoot 'rhinocommon/8.19.25132.1001/lib/net48/RhinoCommon.dll'
if (!(Test-Path -LiteralPath $legacy)) { throw 'Pinned legacy reference missing; cannot exercise rejection.' }
$rejected = $false
try { Assert-CandidateFramework $legacy }
catch { $rejected = $true }
if (!$rejected) { throw 'Framework gate accepted the legacy Rhino reference.' }
Write-Output 'Candidate checks passed: real PE framework, stable version/revision, incompatible assembly rejection.'
