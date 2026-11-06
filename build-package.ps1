param([string]$GhaPath, [string]$SourceCommit, [string]$ExpectedSha256)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '3.1.0-rc.1'
if (!$GhaPath) { $GhaPath = Join-Path $root 'src/GHErrorAssistant/bin/Release/net48/GHErrorAssistant.gha' }
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
$stage = Join-Path $root ('artifacts/staging/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath $GhaPath -Destination (Join-Path $stage 'GHErrorAssistant.gha')
$docs = @('README.md','CHANGELOG.md','EXAMPLES.md','LICENSE','PRIVACY.md','THIRD-PARTY-NOTICES.md','manifest.yml',
    'docs/branding/logo.png','docs/release/diagnosis.jpg','docs/release/LIMITATIONS.md',
    'docs/release/TROUBLESHOOTING.md','RELEASE-NOTES.md')
foreach ($relative in $docs) {
    $source = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required package document missing: $relative" }
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
@("Version: $version",'Status: release-specific RC awaiting targeted native smoke and publication approval',
    "Source commit: $SourceCommit", "GHA SHA256: $hash", "Product version: $($metadata.ProductVersion)",
    'Validation: see the exact native candidate report; CI build alone is not native acceptance.') |
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
