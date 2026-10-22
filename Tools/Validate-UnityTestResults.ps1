[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsPath
)

$ErrorActionPreference = 'Stop'
$assemblies = @('bHapticsOSC.VRChat.Editor.Tests', 'Furroxide.ContactCompressor.Editor.Tests')
$seen = @{}

foreach ($file in Get-ChildItem -LiteralPath $ResultsPath -Recurse -File -Filter '*.xml') {
    [xml]$results = Get-Content -LiteralPath $file.FullName -Raw
    if ($null -eq $results.'test-run') { continue }

    if ([int]$results.'test-run'.failed -gt 0 -or [int]$results.'test-run'.inconclusive -gt 0 -or
        $results.'test-run'.result -eq 'Failed') {
        throw "Unity tests failed in $($file.FullName)."
    }

    foreach ($suite in $results.SelectNodes('//test-suite[@type="Assembly"]')) {
        $name = [IO.Path]::GetFileNameWithoutExtension([string]$suite.name)
        if ($assemblies -cnotcontains $name) { continue }
        if ([int]$suite.passed -le 0 -or [int]$suite.failed -gt 0) {
            throw "Unity assembly '$name' did not run successfully."
        }
        $seen[$name] = $true
    }
}

foreach ($name in $assemblies) {
    if (-not $seen.ContainsKey($name)) {
        throw "No passing Unity results found for '$name'."
    }
}

Write-Host 'Both Unity package test assemblies ran successfully.'
