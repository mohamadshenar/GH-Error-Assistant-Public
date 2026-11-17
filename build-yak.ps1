param([string]$YakPath = 'C:/Program Files/Rhino 9/System/Yak.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = $PSScriptRoot
$zip = Join-Path $root 'artifacts/GHErrorAssistant-3.1.0-windows.zip'
if (!(Test-Path -LiteralPath $zip)) { throw 'Build and verify the candidate Windows ZIP first.' }
if (!(Test-Path -LiteralPath $YakPath)) { throw 'Rhino 9 Yak executable not found; no installation attempted.' }
$stage = Join-Path $root ('artifacts/yak-staging/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
. (Join-Path $root 'tools/release-candidate.ps1')
Assert-CandidateFramework (Join-Path $stage 'GHErrorAssistant.gha')
Push-Location $stage
try { & $YakPath build --platform win; if ($LASTEXITCODE) { throw 'Local Yak build failed.' } }
finally { Pop-Location }
$generated = @(Get-ChildItem -LiteralPath $stage -Filter '*.yak' -File)
if ($generated.Count -ne 1) { throw 'Expected exactly one newly generated package.' }
# Yak infers rh8_19 from pinned API references, not the .NET 10 runtime requirement.
# Narrow THIS NEW package to Rhino 9; never retag an existing published artifact.
$output = Join-Path $root 'artifacts/gh-error-assistant-3.1.0-rh9_0-win.yak'
Copy-Item -LiteralPath $generated[0].FullName -Destination $output -Force
$archive = [IO.Compression.ZipFile]::Open($output, [IO.Compression.ZipArchiveMode]::Update)
try {
    # Yak normalizes manifest bytes. Recompute checksums from the actual final entries.
    $old = $archive.GetEntry('SHA256SUMS.txt')
    if ($old) { $old.Delete() }
    $lines = foreach ($entry in @($archive.Entries | Where-Object Name | Sort-Object FullName)) {
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        "$hash  $($entry.FullName)"
    }
    $sums = $archive.CreateEntry('SHA256SUMS.txt')
    $writer = [IO.StreamWriter]::new($sums.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($lines -join "`n") + "`n") }
    finally { $writer.Dispose() }
}
finally { $archive.Dispose() }
Write-Output "Private Rhino 9 native-acceptance Yak: $output"
Write-Output 'No install or upload performed. Do not distribute inferred Rhino 8 packages from staging.'
