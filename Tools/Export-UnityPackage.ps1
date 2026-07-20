[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\Unity'),
    [string]$PackagePath,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\dist\bHapticsOSC-VRChat.unitypackage'),
    [string]$UnityPath,
    [string]$LogPath = (Join-Path $PSScriptRoot '..\dist\export-unitypackage.log'),
    [string]$StagingPath,
    [switch]$StageOnly,
    [switch]$KeepStagingProject
)

$ErrorActionPreference = 'Stop'
$LegacyRootGuid = 'aa20f348b2d0ed2438d3fc45ceb17fe6'

function Resolve-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Test-PathIsEqualOrDescendant([string]$Candidate, [string]$Parent) {
    $normalizedCandidate = $Candidate.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $normalizedParent = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $parentPrefix = $normalizedParent + [IO.Path]::DirectorySeparatorChar

    return $normalizedCandidate.Equals($normalizedParent, [StringComparison]::OrdinalIgnoreCase) -or
        $normalizedCandidate.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function ConvertTo-ProcessArgument([string]$Argument) {
    if ($null -eq $Argument -or $Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }

        if ($character -eq '"') {
            [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
        } else {
            [void]$builder.Append(('\' * $backslashes))
        }

        [void]$builder.Append($character)
        $backslashes = 0
    }

    [void]$builder.Append(('\' * ($backslashes * 2)))
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-Process([string]$FilePath, [string[]]$Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    if ($null -ne $startInfo.GetType().GetProperty('ArgumentList')) {
        foreach ($argument in $Arguments) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
    } else {
        $startInfo.Arguments = ($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' '
    }

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start process: $FilePath"
    }

    $process.WaitForExit()
    return $process.ExitCode
}

function Copy-DirectoryContents([string]$Source, [string]$Destination, [scriptblock]$IncludeFile) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Required source directory not found: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $sourcePrefix = $Source.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File -Force) {
        if ($IncludeFile -and -not (& $IncludeFile $file)) {
            continue
        }

        $relativePath = $file.FullName.Substring($sourcePrefix.Length)
        $destinationPath = Join-Path $Destination $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationPath) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
}

function New-StagedUnityProject(
    [string]$SourceProject,
    [string]$CanonicalPackage,
    [string]$DestinationProject,
    [ref]$Created
) {
    if (Test-Path -LiteralPath $DestinationProject) {
        throw "StagingPath already exists; refusing to replace it: $DestinationProject"
    }

    New-Item -ItemType Directory -Path $DestinationProject | Out-Null
    $Created.Value = $true

    Copy-DirectoryContents (Join-Path $SourceProject 'ProjectSettings') (Join-Path $DestinationProject 'ProjectSettings')

    $sourceAssets = Join-Path $SourceProject 'Assets'
    $stagedAssets = Join-Path $DestinationProject 'Assets'
    New-Item -ItemType Directory -Path $stagedAssets | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $sourceAssets -Force) {
        if ($item.Name -eq 'bHapticsOSC') {
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination $stagedAssets -Recurse -Force
    }

    $sourcePackages = Join-Path $SourceProject 'Packages'
    $stagedPackages = Join-Path $DestinationProject 'Packages'
    New-Item -ItemType Directory -Path $stagedPackages | Out-Null
    $canonicalPackagePath = [IO.Path]::GetFullPath($CanonicalPackage).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    foreach ($item in Get-ChildItem -LiteralPath $sourcePackages -Force) {
        if ($item.Name -eq 'packages-lock.json') {
            # Unity regenerates this derived file for the staged project. Copying it
            # would retain a stale entry for the canonical embedded VPM package.
            continue
        }

        $itemPath = [IO.Path]::GetFullPath($item.FullName).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        if ($itemPath.Equals($canonicalPackagePath, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination $stagedPackages -Recurse -Force
    }

    $legacyParent = Join-Path $stagedAssets 'bHapticsOSC'
    $legacyRoot = Join-Path $legacyParent 'VRChat'
    Copy-DirectoryContents (Join-Path $CanonicalPackage 'Runtime') $legacyRoot {
        param($File)
        $File.Name -notlike '*.asmdef' -and $File.Name -notlike '*.asmdef.meta'
    }

    $legacyScripts = Join-Path $legacyRoot 'Scripts'
    $legacyEditor = Join-Path $legacyScripts 'Editor'
    Copy-DirectoryContents (Join-Path $CanonicalPackage 'Editor') $legacyEditor {
        param($File)
        $File.Name -notlike '*.asmdef' -and $File.Name -notlike '*.asmdef.meta'
    }

    # The VPM Runtime folder needs its own GUID because migration deliberately
    # leaves the legacy root in place when it contains user-generated assets.
    # Recreate the historical root metadata only inside the fallback project.
    $legacyRootMeta = @(
        'fileFormatVersion: 2'
        "guid: $LegacyRootGuid"
        'folderAsset: yes'
        'DefaultImporter:'
        '  externalObjects: {}'
        '  userData: '
        '  assetBundleName: '
        '  assetBundleVariant: '
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText(
        "$legacyRoot.meta",
        $legacyRootMeta + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )

    $editorMeta = Join-Path $CanonicalPackage 'Editor.meta'
    if (Test-Path -LiteralPath $editorMeta -PathType Leaf) {
        Copy-Item -LiteralPath $editorMeta -Destination "$legacyEditor.meta" -Force
    }

    $exporter = Get-ChildItem -LiteralPath $stagedAssets -Recurse -File -Filter 'bPackageExporter.cs' |
        Where-Object { $_.FullName -notlike "$(Join-Path $stagedAssets 'bHapticsOSC')*" } |
        Select-Object -First 1
    if (-not $exporter) {
        throw 'bPackageExporter.cs must be kept outside the canonical package so the staged project can export the legacy layout.'
    }

    Write-Host "Staged legacy Unity project: $DestinationProject"
}

$ProjectPath = Resolve-FullPath $ProjectPath
$OutputPath = Resolve-FullPath $OutputPath
$LogPath = Resolve-FullPath $LogPath
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $ProjectPath 'Packages\com.furroxide.bhaptics-vrchat'
}
$PackagePath = Resolve-FullPath $PackagePath

if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'Assets') -PathType Container)) {
    throw "ProjectPath does not look like a Unity project: $ProjectPath"
}

$projectVersionPath = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
    throw "ProjectVersion.txt not found under: $ProjectPath"
}

if (-not (Test-Path -LiteralPath (Join-Path $PackagePath 'package.json') -PathType Leaf)) {
    throw "Canonical VPM package not found: $PackagePath"
}

$removeStagingProject = $false
if ([string]::IsNullOrWhiteSpace($StagingPath)) {
    $StagingPath = Join-Path ([IO.Path]::GetTempPath()) "bHapticsOSC-unity-export-$([guid]::NewGuid().ToString('N'))"
    $removeStagingProject = -not $KeepStagingProject -and -not $StageOnly
} else {
    $StagingPath = Resolve-FullPath $StagingPath
    $removeStagingProject = -not $KeepStagingProject -and -not $StageOnly
}

$stagingProjectCreated = $false
if (Test-PathIsEqualOrDescendant $StagingPath $ProjectPath) {
    throw 'StagingPath must be outside ProjectPath.'
}

if ((Test-PathIsEqualOrDescendant $OutputPath $StagingPath) -or
    (Test-PathIsEqualOrDescendant $LogPath $StagingPath)) {
    throw 'OutputPath and LogPath must be outside StagingPath.'
}

try {
    New-StagedUnityProject $ProjectPath $PackagePath $StagingPath ([ref]$stagingProjectCreated)

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "project_path=$StagingPath"
    }

    if ($StageOnly) {
        Write-Host 'Staging complete; Unity export was skipped because -StageOnly was specified.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($UnityPath)) {
        $versionLine = Get-Content -LiteralPath $projectVersionPath |
            Where-Object { $_ -like 'm_EditorVersion:*' } |
            Select-Object -First 1
        $version = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()

        $candidates = @()
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles})) {
            $candidates += Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$version\Editor\Unity.exe"
        }
        $candidates += "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"

        $UnityPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($UnityPath)) {
            foreach ($commandName in @('Unity', 'Unity.exe', 'unity-editor')) {
                $command = Get-Command $commandName -ErrorAction SilentlyContinue
                if ($command) {
                    $UnityPath = $command.Source
                    break
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($UnityPath) -or -not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
        throw "Unity executable not found. Pass -UnityPath 'C:\Path\To\Unity.exe'."
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $LogPath -Parent) | Out-Null

    $unityArgs = @(
        '-batchmode',
        '-quit',
        '-nographics',
        '-projectPath', $StagingPath,
        '-executeMethod', 'bHapticsOSC.VRChat.bPackageExporter.ExportFromCommandLine',
        '-bHapticsExportPath', $OutputPath,
        '-logFile', $LogPath
    )

    Write-Host 'Exporting Unity package...'
    Write-Host "Unity: $UnityPath"
    Write-Host "Source project: $ProjectPath"
    Write-Host "Staged project: $StagingPath"
    Write-Host "Output: $OutputPath"

    $exitCode = Invoke-Process $UnityPath $unityArgs
    if ($exitCode -ne 0) {
        throw "Unity export failed with exit code $exitCode. Log: $LogPath"
    }

    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Unity completed without creating the package: $OutputPath. Log: $LogPath"
    }

    Write-Host "Export complete: $OutputPath"
} finally {
    if ($removeStagingProject -and $stagingProjectCreated -and (Test-Path -LiteralPath $StagingPath)) {
        Remove-Item -LiteralPath $StagingPath -Recurse -Force
    }
}
