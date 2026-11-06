function Assert-PublicBinaryPaths([byte[]]$Bytes) {
    $pattern = '(?i)[a-z]:[\\/]+Users[\\/]+|/Users/|/home/'
    $texts = @([Text.Encoding]::UTF8.GetString($Bytes), [Text.Encoding]::Unicode.GetString($Bytes))
    if ($Bytes.Length -gt 1) { $texts += [Text.Encoding]::Unicode.GetString($Bytes, 1, $Bytes.Length - 1) }
    if ($texts | Where-Object { $_ -match $pattern }) { throw 'Candidate contains a private user path. Rebuild with canonical compiler paths.' }
}
