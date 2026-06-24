[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\Unity'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\dist\bHapticsOSC-VRChat.unitypackage'),
    [string]$UnityPath,
    [string]$LogPath = (Join-Path $PSScriptRoot '..\dist\export-unitypackage.log')
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

$ProjectPath = Resolve-FullPath $ProjectPath
$OutputPath = Resolve-FullPath $OutputPath
$LogPath = Resolve-FullPath $LogPath

if (-not (Test-Path (Join-Path $ProjectPath 'Assets'))) {
    throw "ProjectPath does not look like a Unity project: $ProjectPath"
}

if (-not (Test-Path (Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'))) {
    throw "ProjectVersion.txt not found under: $ProjectPath"
}

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $versionLine = Get-Content (Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt') |
        Where-Object { $_ -like 'm_EditorVersion:*' } |
        Select-Object -First 1
    $version = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
    $candidate = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (Test-Path $candidate) {
        $UnityPath = $candidate
    } else {
        $command = Get-Command Unity -ErrorAction SilentlyContinue
        if ($command) {
            $UnityPath = $command.Source
        }
    }
}

if ([string]::IsNullOrWhiteSpace($UnityPath) -or -not (Test-Path $UnityPath)) {
    throw "Unity executable not found. Pass -UnityPath 'C:\Path\To\Unity.exe'."
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $LogPath -Parent) | Out-Null

$unityArgs = @(
    '-batchmode',
    '-quit',
    '-nographics',
    '-projectPath', $ProjectPath,
    '-executeMethod', 'bHapticsOSC.VRChat.bPackageExporter.ExportFromCommandLine',
    '-bHapticsExportPath', $OutputPath,
    '-logFile', $LogPath
)

Write-Host "Exporting Unity package..."
Write-Host "Unity: $UnityPath"
Write-Host "Project: $ProjectPath"
Write-Host "Output: $OutputPath"

$process = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$exitCode = $process.ExitCode

if ($exitCode -ne 0) {
    Write-Error "Unity export failed with exit code $exitCode. Log: $LogPath"
}

if (-not (Test-Path $OutputPath)) {
    Write-Error "Unity completed without creating the package: $OutputPath. Log: $LogPath"
}

Write-Host "Export complete: $OutputPath"
