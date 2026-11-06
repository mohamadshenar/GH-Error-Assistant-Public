$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../tools/release-paths.ps1')
$unsafe = @([Text.Encoding]::UTF8.GetBytes('C:\Users\synthetic\build.pdb'),
    [Text.Encoding]::Unicode.GetBytes('C:\Users\synthetic\build.pdb'),
    ([byte[]]@(1) + [Text.Encoding]::Unicode.GetBytes('C:\Users\synthetic\build.pdb')),
    [Text.Encoding]::UTF8.GetBytes('/Users/synthetic/build.pdb'),
    [Text.Encoding]::UTF8.GetBytes('/home/synthetic/build.pdb'))
foreach ($bytes in $unsafe) {
    $rejected = $false
    try { Assert-PublicBinaryPaths $bytes } catch {
        if ($_.Exception.Message -ne 'Candidate contains a private user path. Rebuild with canonical compiler paths.') { throw }
        $rejected = $true
    }
    if (!$rejected) { throw 'Private-path regression: unsafe sample accepted.' }
}
Assert-PublicBinaryPaths ([Text.Encoding]::UTF8.GetBytes('/_/GHErrorAssistant/obj/Release/GHErrorAssistant.pdb'))
Write-Output 'Package privacy regressions passed: five unsafe encodings/paths rejected; canonical path accepted.'
