param([string]$GhaPath, [string]$SourceCommit, [string]$ExpectedSha256)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '3.1.0'
if (!$GhaPath) { $GhaPath = Join-Path $root 'src/GHErrorAssistant/bin/Release/net10.0-windows/GHErrorAssistant.gha' }
if (!(Test-Path -LiteralPath $GhaPath)) { throw 'Build Release before packaging.' }
$hash = (Get-FileHash -LiteralPath $GhaPath -Algorithm SHA256).Hash
if ($ExpectedSha256 -and $hash -ne $ExpectedSha256) { throw 'Candidate hash mismatch.' }
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $GhaPath))
if ($metadata.FileVersion -ne '3.1.0.0' -or ($metadata.ProductVersion -split '\+', 2)[0] -ne $version) { throw 'Unexpected candidate version.' }
if (!$SourceCommit) { $SourceCommit = $env:GITHUB_SHA }
if (!$SourceCommit) { $SourceCommit = (git -C $root rev-parse HEAD).Trim(); if ($LASTEXITCODE) { throw 'Source commit unavailable.' } }
if ($SourceCommit -notmatch '^[a-fA-F0-9]{40}$') { throw 'A full source commit SHA is required.' }
if ($metadata.ProductVersion -ne "$version+$SourceCommit") { throw 'Binary source revision does not match the package source commit.' }
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $GhaPath))
. (Join-Path $root 'tools/release-paths.ps1')
Assert-PublicBinaryPaths $bytes
. (Join-Path $root 'tools/release-candidate.ps1')
Assert-CandidateFramework (Resolve-Path -LiteralPath $GhaPath).Path
$stage = Join-Path $root ('artifacts/staging/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath $GhaPath -Destination (Join-Path $stage 'GHErrorAssistant.gha')
$docs = @('README.md','INSTALLATION.md','RELEASE-NOTES.md','LICENSE','PRIVACY.md','THIRD-PARTY-NOTICES.md','manifest.yml',
    'docs/branding/logo.png','docs/release/diagnosis.jpg','docs/release/LIMITATIONS.md','docs/release/TROUBLESHOOTING.md')
foreach ($relative in $docs) {
    $source = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required package document missing: $relative" }
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
@("Version: $version",'Status: locally built package; not the approved binary unless its recorded SHA-256 matches',
    'Host: Windows / Rhino 9 / default .NET 10 / Grasshopper 1',
    "Source commit: $SourceCommit", "GHA SHA256: $hash", "Product version: $($metadata.ProductVersion)",
    'Validation: automated build checks do not establish native acceptance of a newly built binary.') |
    Set-Content -LiteralPath (Join-Path $stage 'BUILD-INFO.txt') -Encoding utf8
$allow = @('GHErrorAssistant.gha','BUILD-INFO.txt') + $docs
$actual = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { $_.FullName.Substring($stage.Length + 1).Replace('\','/') })
if (Compare-Object ($allow | Sort-Object) ($actual | Sort-Object)) { throw 'Package allowlist mismatch.' }
$sums = foreach ($relative in ($actual | Sort-Object)) { "$( (Get-FileHash -LiteralPath (Join-Path $stage $relative) -Algorithm SHA256).Hash )  $relative" }
$sums | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8
$zip = Join-Path $root "artifacts/GHErrorAssistant-$version-windows.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Output "Candidate ZIP: $zip"
Write-Output "GHA SHA256: $hash"
Write-Output "Staging directory: $stage"
