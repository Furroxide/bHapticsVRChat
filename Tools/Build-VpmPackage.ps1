[CmdletBinding()]
param(
    [string]$PackagePath = (Join-Path $PSScriptRoot '..\Unity\Packages\com.furroxide.bhaptics-vrchat'),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Fail([string]$Message) {
    throw "VPM package build failed: $Message"
}

$PackagePath = Resolve-FullPath $PackagePath
$manifestPath = Join-Path $PackagePath 'package.json'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Fail "Package manifest not found: $manifestPath"
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
} catch {
    Fail "Could not parse $manifestPath. $($_.Exception.Message)"
}

$packageName = [string]$manifest.name
$packageVersion = [string]$manifest.version
if ($packageName -ne 'com.furroxide.bhaptics-vrchat') {
    Fail "Expected package name 'com.furroxide.bhaptics-vrchat', got '$packageName'."
}

if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') {
    Fail "Package version must be a strict X.Y.Z version, got '$packageVersion'."
}

$archiveName = "$packageName-$packageVersion.zip"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "..\dist\$archiveName"
}
$OutputPath = Resolve-FullPath $OutputPath

if ((Split-Path -Leaf $OutputPath) -cne $archiveName) {
    Fail "Archive must be named '$archiveName', got '$(Split-Path -Leaf $OutputPath)'."
}

$packagePrefix = $PackagePath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($OutputPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    Fail 'OutputPath must be outside PackagePath.'
}

$filesByEntryName = @{}
foreach ($file in Get-ChildItem -LiteralPath $PackagePath -Recurse -File -Force) {
    $relativePath = $file.FullName.Substring($packagePrefix.Length)
    $entryName = $relativePath.Replace([IO.Path]::DirectorySeparatorChar, '/').Replace([IO.Path]::AltDirectorySeparatorChar, '/')
    $filesByEntryName[$entryName] = $file.FullName
}

$entryNames = [string[]]$filesByEntryName.Keys
[Array]::Sort($entryNames, [StringComparer]::Ordinal)
if ($entryNames.Count -eq 0) {
    Fail "No package files found under $PackagePath."
}

if (-not $filesByEntryName.ContainsKey('package.json')) {
    Fail 'The archive would not contain package.json at its root.'
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

Add-Type -AssemblyName System.IO.Compression
$fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$regularFilePermissions = [BitConverter]::ToInt32(
    [BitConverter]::GetBytes([Convert]::ToUInt32('81A40000', 16)),
    0
)
$outputStream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $outputStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false,
        [Text.Encoding]::UTF8
    )
    try {
        foreach ($entryName in $entryNames) {
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $entry.ExternalAttributes = $regularFilePermissions

            $sourceStream = [IO.File]::OpenRead($filesByEntryName[$entryName])
            try {
                $entryStream = $entry.Open()
                try {
                    $sourceStream.CopyTo($entryStream)
                } finally {
                    $entryStream.Dispose()
                }
            } finally {
                $sourceStream.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }
} finally {
    $outputStream.Dispose()
}

$archiveHash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "archive_name=$archiveName"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "archive_path=$OutputPath"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "manifest_path=$manifestPath"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "package_name=$packageName"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "version=$packageVersion"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "sha256=$archiveHash"
}

Write-Host "Built deterministic VPM package: $OutputPath"
Write-Host "SHA-256: $archiveHash"
