param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$ArchivePath) { $ArchivePath = Join-Path $root 'artifacts/GHErrorAssistant-3.1.0-windows.zip' }
$candidate = Join-Path $root 'src/GHErrorAssistant/bin/Release/net10.0-windows/GHErrorAssistant.gha'
$archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    $entries = @($archive.Entries | Where-Object Name)
    if (@($entries | Where-Object FullName -match '\.gha$').Count -ne 1) { throw 'Expected one assistant assembly.' }
    if (@($entries | Where-Object FullName -match '(^|/)\.git(/|$)|\.\.|^[A-Za-z]:|^/|\.(dll|pdb|gh|3dm)$').Count) {
        throw 'Unexpected development/vendor/path entry in distribution.'
    }
    $sumEntry = $archive.GetEntry('SHA256SUMS.txt')
    if (!$sumEntry) { throw 'Missing checksum manifest.' }
    $reader = [IO.StreamReader]::new($sumEntry.Open())
    try { $lines = $reader.ReadToEnd().Trim() -split '\r?\n' }
    finally { $reader.Dispose() }
    $sums = @{}
    foreach ($line in $lines) {
        if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$' -or $sums.ContainsKey($Matches[2])) { throw 'Invalid/duplicate checksum line.' }
        $sums[$Matches[2]] = $Matches[1]
    }
    $payload = @($entries | Where-Object FullName -ne 'SHA256SUMS.txt')
    if ($payload.Count -ne $sums.Count) { throw 'Checksum coverage mismatch.' }
    foreach ($entry in $payload) {
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if (!$sums.ContainsKey($entry.FullName) -or $sums[$entry.FullName] -ne $hash) { throw "Checksum mismatch: $($entry.FullName)" }
        if ($entry.FullName -eq 'GHErrorAssistant.gha' -and $hash -ne (Get-FileHash -LiteralPath $candidate).Hash) { throw 'Packaged GHA differs from candidate.' }
    }
}
finally { $archive.Dispose() }
Write-Output 'Archive checks passed: one exact candidate, no vendor/dev entries, complete actual-entry checksums.'
