[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$BaseRef,
    [string]$ReleaseRepository = $env:GITHUB_REPOSITORY,
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [string]$ChangelogEntryPath,
    [switch]$AllowCurrentCommitTag,
    [switch]$SkipRemoteReleaseCheck
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Fail([string]$Message) {
    throw "Release metadata validation failed: $Message"
}

function Read-RequiredText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Fail "Required file is missing: $Path"
    }

    (Get-Content -LiteralPath $Path -Raw).Trim()
}

function Assert-StrictVersion([string]$Version, [string]$Source) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Fail "$Source must be a strict X.Y.Z version, got '$Version'."
    }
}

function ConvertTo-VersionParts([string]$Version) {
    Assert-StrictVersion $Version 'Version'
    $Version.Split('.') | ForEach-Object { [int]$_ }
}

function Compare-StrictVersion([string]$Left, [string]$Right) {
    $leftParts = @(ConvertTo-VersionParts $Left)
    $rightParts = @(ConvertTo-VersionParts $Right)

    for ($i = 0; $i -lt 3; $i++) {
        if ($leftParts[$i] -gt $rightParts[$i]) { return 1 }
        if ($leftParts[$i] -lt $rightParts[$i]) { return -1 }
    }

    return 0
}

function Get-GitOutput([string[]]$Arguments, [switch]$AllowFailure) {
    $output = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        Fail "git $($Arguments -join ' ') failed."
    }

    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return ($output -join "`n").Trim()
}

function Get-VersionFromBuildInfo([string]$Path) {
    $content = Read-RequiredText $Path
    $match = [regex]::Match($content, 'public\s+const\s+string\s+Version\s*=\s*"(?<version>\d+\.\d+\.\d+)"')
    if (-not $match.Success) {
        Fail "Could not find BuildInfo.Version in $Path."
    }

    $match.Groups['version'].Value
}

function Get-CompanionFallbackVersion([string]$Path) {
    $content = Read-RequiredText $Path
    $match = [regex]::Match(
        $content,
        '(?:public|internal)\s+const\s+string\s+FallbackRequiredVersion\s*=\s*"(?<version>[^"]+)"\s*;'
    )
    if (-not $match.Success) {
        Fail "Could not find public or internal const string FallbackRequiredVersion in $Path."
    }

    $match.Groups['version'].Value
}

function Read-PackageJson([string]$Path) {
    $content = Read-RequiredText $Path
    try {
        return $content | ConvertFrom-Json
    } catch {
        Fail "Could not parse JSON in $Path. $($_.Exception.Message)"
    }
}

function Get-VersionFromPackageJson([string]$Path) {
    $package = Read-PackageJson $Path

    if ([string]::IsNullOrWhiteSpace($package.version)) {
        Fail "Missing version in $Path."
    }

    [string]$package.version
}

function Get-UnityMetaGuid([string]$Path) {
    $content = Read-RequiredText $Path
    $match = [regex]::Match($content, '(?m)^guid:\s*(?<guid>[0-9a-f]{32})\s*$')
    if (-not $match.Success) {
        Fail "Could not find a Unity GUID in $Path."
    }

    $match.Groups['guid'].Value
}

function Assert-ExactJsonMap([object]$Object, [hashtable]$Expected, [string]$Source) {
    if ($null -eq $Object) {
        Fail "$Source is missing."
    }

    $actualNames = @($Object.PSObject.Properties.Name)
    $expectedNames = @($Expected.Keys)
    $missing = @($expectedNames | Where-Object { $actualNames -cnotcontains $_ })
    $unexpected = @($actualNames | Where-Object { $expectedNames -cnotcontains $_ })
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        $details = @()
        if ($missing.Count -gt 0) { $details += "missing: $($missing -join ', ')" }
        if ($unexpected.Count -gt 0) { $details += "unexpected: $($unexpected -join ', ')" }
        Fail "$Source must contain exactly the expected entries ($($details -join '; '))."
    }

    foreach ($name in $expectedNames) {
        $actualValue = [string]$Object.PSObject.Properties[$name].Value
        $expectedValue = [string]$Expected[$name]
        if ($actualValue -cne $expectedValue) {
            Fail "$Source entry '$name' must be '$expectedValue', got '$actualValue'."
        }
    }
}

function Assert-VpmManifest([object]$Manifest, [string]$Version, [string]$Path) {
    $expectedName = 'com.furroxide.bhaptics-vrchat'
    if (([string]$Manifest.name) -cne $expectedName) {
        Fail "VPM package name in $Path must be '$expectedName', got '$($Manifest.name)'."
    }

    if (([string]$Manifest.displayName) -cne 'bHaptics VRChatOSC') {
        Fail "VPM displayName in $Path must be 'bHaptics VRChatOSC', got '$($Manifest.displayName)'."
    }

    if (([string]$Manifest.version) -cne $Version) {
        Fail "VPM package version ($($Manifest.version)) must match VERSION ($Version)."
    }

    if (([string]$Manifest.unity) -cne '2022.3') {
        Fail "VPM unity version in $Path must be '2022.3', got '$($Manifest.unity)'."
    }

    if (([string]$Manifest.license) -cne 'GPL-3.0-only') {
        Fail "VPM license in $Path must be 'GPL-3.0-only', got '$($Manifest.license)'."
    }

    Assert-ExactJsonMap $Manifest.author @{
        name = 'Furroxide'
        email = '221987073+furroxide@users.noreply.github.com'
        url = 'https://github.com/furroxide'
    } "VPM author in $Path"

    Assert-ExactJsonMap $Manifest.vpmDependencies @{
        'com.vrchat.avatars' = '3.10.x'
        'com.vrcfury.vrcfury' = '>=1.1341.0 <2.0.0'
        'com.furroxide.contact-compressor' = '>=0.1.0 <1.0.0'
    } "VPM dependencies in $Path"

    Assert-ExactJsonMap $Manifest.legacyFolders @{
        'Assets\bHapticsOSC\VRChat\Materials' = '13f92bc2b3af777418356c43e176eb0d'
        'Assets\bHapticsOSC\VRChat\Models' = '0ea71aee00703a54098c3828d5467e1d'
        'Assets\bHapticsOSC\VRChat\Prefabs' = 'd4be18ff8ac3b7440b79abe75706e198'
        'Assets\bHapticsOSC\VRChat\Scripts' = 'e5ed1b6b981cfd24daba2d9156e2093c'
        'Assets\bHapticsOSC\VRChat\Shaders' = '04ab7b92a321da2428e8bf372e46fe6b'
        'Assets\bHapticsOSC\VRChat\Textures' = '34984ce1bee61fe4b85179972649bfaa'
    } "VPM legacyFolders in $Path"

    Assert-ExactJsonMap $Manifest.legacyFiles @{
        'Assets\bHapticsOSC\VRChat\ParameterExclusions.txt' = ''
    } "VPM legacyFiles in $Path"

    $legacyPackages = @($Manifest.legacyPackages)
    if ($legacyPackages.Count -ne 1 -or ([string]$legacyPackages[0]) -cne 'bHapticsOSC.VRChat') {
        Fail "VPM legacyPackages in $Path must contain only 'bHapticsOSC.VRChat'."
    }
}

function Assert-ContactCompressorManifest([object]$Manifest, [string]$Path) {
    $expectedName = 'com.furroxide.contact-compressor'
    if (([string]$Manifest.name) -cne $expectedName) {
        Fail "VPM package name in $Path must be '$expectedName', got '$($Manifest.name)'."
    }

    if (([string]$Manifest.displayName) -cne 'Contact Compressor') {
        Fail "VPM displayName in $Path must be 'Contact Compressor', got '$($Manifest.displayName)'."
    }

    Assert-StrictVersion ([string]$Manifest.version) "Contact Compressor package version in $Path"

    if (([string]$Manifest.unity) -cne '2022.3') {
        Fail "VPM unity version in $Path must be '2022.3', got '$($Manifest.unity)'."
    }

    if (([string]$Manifest.license) -cne 'GPL-3.0-only') {
        Fail "VPM license in $Path must be 'GPL-3.0-only', got '$($Manifest.license)'."
    }

    Assert-ExactJsonMap $Manifest.author @{
        name = 'Furroxide'
        email = '221987073+furroxide@users.noreply.github.com'
        url = 'https://github.com/furroxide'
    } "VPM author in $Path"

    Assert-ExactJsonMap $Manifest.vpmDependencies @{
        'com.vrchat.avatars' = '3.10.x'
    } "VPM dependencies in $Path"
}

<#
.SYNOPSIS
    Requires the Contact Compressor version to move whenever its contents do.

.DESCRIPTION
    The compressor's version is independent of VERSION, so nothing else makes it change. The VPM
    listing keys packages by (id, version) and keeps the first entry it saw, so republishing
    different bytes under an unchanged version leaves every VCC user on the old archive with no
    error anywhere - the release succeeds and the fix simply never ships. Skipped when there is no
    usable baseline to compare against, matching how the VERSION baseline behaves.
#>
function Assert-ContactCompressorVersionBumped(
    [string]$RequestedBaseRef,
    [string]$CurrentVersion,
    [string]$PackageRelativePath) {

    if ([string]::IsNullOrWhiteSpace($RequestedBaseRef) -or $RequestedBaseRef.Trim() -match '^0+$') {
        return
    }

    $baseRef = $RequestedBaseRef.Trim()
    $baselineManifest = Get-GitOutput @('show', "${baseRef}:$PackageRelativePath/package.json") -AllowFailure
    if ([string]::IsNullOrWhiteSpace($baselineManifest)) {
        Write-Warning "Contact Compressor manifest is unavailable at $baseRef; skipping its version-increase check."
        return
    }

    $changed = Get-GitOutput @('diff', '--name-only', $baseRef, 'HEAD', '--', $PackageRelativePath) -AllowFailure
    if ([string]::IsNullOrWhiteSpace($changed)) {
        return
    }

    try {
        $baselineVersion = [string](($baselineManifest | ConvertFrom-Json).version)
    } catch {
        Write-Warning "Contact Compressor manifest at $baseRef could not be parsed; skipping its version-increase check."
        return
    }

    if ([string]::IsNullOrWhiteSpace($baselineVersion)) {
        return
    }

    Assert-StrictVersion $baselineVersion "Contact Compressor version at $baseRef"
    if ((Compare-StrictVersion $CurrentVersion $baselineVersion) -le 0) {
        $files = ($changed -split "`n" | Select-Object -First 5) -join ', '
        Fail ("$PackageRelativePath changed since $baseRef but its version is still $CurrentVersion " +
              "(was $baselineVersion). Bump it, or the VPM listing keeps serving the old archive. " +
              "Changed: $files")
    }
}

<#
.SYNOPSIS
    Checks that every first-party VPM dependency is a package this pipeline actually publishes.

.DESCRIPTION
    A dependency on com.furroxide.* that no release ships is unresolvable in VCC: the package
    installs and then fails to resolve, which looks like a broken listing rather than a missing
    build step. This is the check that was absent when the Contact Compressor dependency was added
    without the pipeline learning to publish it.

    The set of published packages is passed in by the caller rather than read out of the workflow
    files, so this catches a dependency on a package that does not exist - not a workflow that
    stopped building one that does. Removing a build step still fails, but later, at the publish
    job's asset-count check.
#>
function Assert-FirstPartyDependenciesArePublished(
    [object]$Manifest,
    [hashtable]$PublishedVersionsByName,
    [string]$Path) {

    if ($null -eq $Manifest.vpmDependencies) {
        return
    }

    foreach ($dependency in $Manifest.vpmDependencies.PSObject.Properties) {
        $name = [string]$dependency.Name
        if (-not $name.StartsWith('com.furroxide.', [StringComparison]::Ordinal)) {
            continue
        }

        if (-not $PublishedVersionsByName.ContainsKey($name)) {
            Fail ("$Path depends on '$name', which this repository does not publish. Add it to the " +
                  'release pipeline or drop the dependency; VCC cannot resolve it as things stand.')
        }

        $range = [string]$dependency.Value
        $published = [string]$PublishedVersionsByName[$name]
        if (-not (Test-VersionSatisfiesRange $published $range)) {
            Fail "$Path requires '$name' $range, but the version this repository publishes is $published."
        }
    }
}

<#
.SYNOPSIS
    Evaluates the ">=x.y.z <a.b.c" ranges this repository uses for its own packages.

.DESCRIPTION
    Deliberately narrow: it understands the exact shape written in these manifests and refuses
    anything else, rather than half-implementing the VPM range grammar and silently passing a
    range it got wrong.
#>
function Test-VersionSatisfiesRange([string]$Version, [string]$Range) {
    $match = [regex]::Match(
        $Range.Trim(),
        '^>=\s*(?<min>\d+\.\d+\.\d+)\s+<\s*(?<max>\d+\.\d+\.\d+)$')
    if (-not $match.Success) {
        Fail "Unsupported first-party dependency range '$Range'; expected '>=X.Y.Z <A.B.C'."
    }

    return (Compare-StrictVersion $Version $match.Groups['min'].Value) -ge 0 -and
           (Compare-StrictVersion $Version $match.Groups['max'].Value) -lt 0
}

function Get-LatestTagVersion([string]$ExcludedTag) {
    $tags = Get-GitOutput @('tag', '--list', 'v[0-9]*.[0-9]*.[0-9]*') -AllowFailure
    if ([string]::IsNullOrWhiteSpace($tags)) {
        return $null
    }

    $versions = @()
    foreach ($tag in ($tags -split "`n")) {
        $trimmed = $tag.Trim()
        if (-not [string]::IsNullOrWhiteSpace($ExcludedTag) -and $trimmed -ceq $ExcludedTag) {
            continue
        }

        if ($trimmed -match '^v(?<version>\d+\.\d+\.\d+)$') {
            $versions += $matches['version']
        }
    }

    if ($versions.Count -eq 0) {
        return $null
    }

    $latest = $versions[0]
    foreach ($candidate in $versions) {
        if ((Compare-StrictVersion $candidate $latest) -gt 0) {
            $latest = $candidate
        }
    }

    return $latest
}

function Get-BaselineVersion([string]$RequestedBaseRef, [string]$ExcludedTag) {
    $hasRequestedBaseRef = -not [string]::IsNullOrWhiteSpace($RequestedBaseRef) -and $RequestedBaseRef.Trim() -notmatch '^0+$'
    if ($hasRequestedBaseRef) {
        $candidate = $RequestedBaseRef.Trim()
        $version = Get-GitOutput @('show', "${candidate}:VERSION") -AllowFailure
        if (-not [string]::IsNullOrWhiteSpace($version)) {
            $version = $version.Trim()
            Assert-StrictVersion $version "VERSION at $candidate"
            return [pscustomobject]@{
                Version = $version
                Source = "VERSION at $candidate"
            }
        }

        Write-Warning "VERSION is unavailable at $candidate; using the latest semantic version tag as the release baseline."
    }

    $tagVersion = Get-LatestTagVersion $ExcludedTag
    if (-not [string]::IsNullOrWhiteSpace($tagVersion)) {
        return [pscustomobject]@{
            Version = $tagVersion
            Source = 'latest v* tag'
        }
    }

    return $null
}

function Get-ChangelogEntry([string]$ChangelogPath, [string]$Version) {
    $content = Read-RequiredText $ChangelogPath
    $headingPattern = '(?m)^## \[(?<version>\d+\.\d+\.\d+)\] - (?<date>\d{4}-\d{2}-\d{2})\s*$'
    $headingMatches = [regex]::Matches($content, $headingPattern)

    if ($headingMatches.Count -eq 0) {
        Fail "CHANGELOG.md must contain at least one '## [X.Y.Z] - YYYY-MM-DD' entry."
    }

    $latest = $headingMatches[0]
    $latestVersion = $latest.Groups['version'].Value
    if ($latestVersion -ne $Version) {
        Fail "Latest CHANGELOG.md entry must be [$Version], got [$latestVersion]."
    }

    $nextStart = $content.Length
    if ($headingMatches.Count -gt 1) {
        $nextStart = $headingMatches[1].Index
    }

    $entry = $content.Substring($latest.Index, $nextStart - $latest.Index).Trim()
    if ([string]::IsNullOrWhiteSpace($entry)) {
        Fail "CHANGELOG.md entry for $Version is empty."
    }

    return $entry
}

function Test-LocalTagExists([string]$Tag) {
    $matchingTag = Get-GitOutput @('tag', '--list', $Tag)
    return $matchingTag -eq $Tag
}

function Get-RemoteRelease([string]$Repository, [string]$Tag, [string]$Token) {
    if ([string]::IsNullOrWhiteSpace($Repository) -or [string]::IsNullOrWhiteSpace($Token)) {
        Write-Warning 'Skipping remote release check because ReleaseRepository or GitHubToken is not available.'
        return $null
    }

    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $Token"
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $page = 1
    while ($true) {
        $uri = "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page"
        $releases = @(Invoke-RestMethod -Method Get -Uri $uri -Headers $headers)
        foreach ($release in $releases) {
            if (([string]$release.tag_name) -ceq $Tag) {
                return $release
            }
        }

        if ($releases.Count -lt 100) {
            return $null
        }

        $page++
    }
}

$RepositoryRoot = Resolve-FullPath $RepositoryRoot
Push-Location $RepositoryRoot
try {
    $rootVersion = Read-RequiredText (Join-Path $RepositoryRoot 'VERSION')
    Assert-StrictVersion $rootVersion 'VERSION'

    $buildInfoVersion = Get-VersionFromBuildInfo (Join-Path $RepositoryRoot 'External\bHapticsOSC\Properties\BuildInfo.cs')
    $packageVersion = Get-VersionFromPackageJson (Join-Path $RepositoryRoot 'External\bHapticsOSC\Packages\bHapticsOSC.VRChat\package.json')
    $packageTextVersion = Read-RequiredText (Join-Path $RepositoryRoot 'External\bHapticsOSC\Packages\bHapticsOSC.VRChat\version.txt')
    $vpmManifestPath = Join-Path $RepositoryRoot 'Unity\Packages\com.furroxide.bhaptics-vrchat\package.json'
    $vpmManifest = Read-PackageJson $vpmManifestPath
    $vpmPackageVersion = [string]$vpmManifest.version
    $companionRequirementsPath = Join-Path $RepositoryRoot 'Unity\Packages\com.furroxide.bhaptics-vrchat\Editor\bCompanionRequirements.cs'
    $companionFallbackVersion = Get-CompanionFallbackVersion $companionRequirementsPath
    $vpmRuntimeMetaPath = Join-Path $RepositoryRoot 'Unity\Packages\com.furroxide.bhaptics-vrchat\Runtime.meta'
    $vpmRuntimeGuid = Get-UnityMetaGuid $vpmRuntimeMetaPath

    Assert-StrictVersion $buildInfoVersion 'BuildInfo.Version'
    Assert-StrictVersion $packageVersion 'Unity package version'
    Assert-StrictVersion $packageTextVersion 'Unity package version.txt'
    Assert-StrictVersion $vpmPackageVersion 'VPM package version'
    Assert-StrictVersion $companionFallbackVersion 'Companion fallback required version'

    if ($buildInfoVersion -ne $rootVersion) {
        Fail "BuildInfo.Version ($buildInfoVersion) must match VERSION ($rootVersion)."
    }

    if ($packageVersion -ne $rootVersion) {
        Fail "Unity package version ($packageVersion) must match VERSION ($rootVersion)."
    }

    if ($packageTextVersion -ne $rootVersion) {
        Fail "Unity package version.txt ($packageTextVersion) must match VERSION ($rootVersion)."
    }

    if ($companionFallbackVersion -ne $rootVersion) {
        Fail "Companion fallback required version ($companionFallbackVersion) must match VERSION, app BuildInfo, and VPM package version ($rootVersion)."
    }

    Assert-VpmManifest $vpmManifest $rootVersion $vpmManifestPath

    $compressorManifestPath = Join-Path $RepositoryRoot 'Unity\Packages\com.furroxide.contact-compressor\package.json'
    $compressorManifest = Read-PackageJson $compressorManifestPath
    Assert-ContactCompressorManifest $compressorManifest $compressorManifestPath
    Assert-ContactCompressorVersionBumped `
        $BaseRef `
        ([string]$compressorManifest.version) `
        'Unity/Packages/com.furroxide.contact-compressor'

    Assert-FirstPartyDependenciesArePublished $vpmManifest @{
        'com.furroxide.contact-compressor' = [string]$compressorManifest.version
    } $vpmManifestPath

    $legacyRootGuid = 'aa20f348b2d0ed2438d3fc45ceb17fe6'
    if ($vpmRuntimeGuid -ceq $legacyRootGuid) {
        Fail "VPM Runtime GUID must differ from the preserved legacy VRChat root GUID ($legacyRootGuid)."
    }

    $candidateTag = "v$rootVersion"
    $excludedBaselineTag = if ($AllowCurrentCommitTag) { $candidateTag } else { $null }
    $baseline = Get-BaselineVersion $BaseRef $excludedBaselineTag
    if ($baseline) {
        if ((Compare-StrictVersion $rootVersion $baseline.Version) -le 0) {
            Fail "VERSION ($rootVersion) must be greater than $($baseline.Source) ($($baseline.Version))."
        }
        Write-Host "Version baseline: $($baseline.Source) = $($baseline.Version)"
    } else {
        Write-Warning 'No baseline VERSION or v* tag found; skipping version-increase comparison.'
    }

    $changelogEntry = Get-ChangelogEntry (Join-Path $RepositoryRoot 'CHANGELOG.md') $rootVersion

    $tag = $candidateTag
    $remoteRelease = $null
    if (-not $SkipRemoteReleaseCheck) {
        $remoteRelease = Get-RemoteRelease $ReleaseRepository $tag $GitHubToken
        if ($null -ne $remoteRelease) {
            if (-not [bool]$remoteRelease.draft) {
                Fail "GitHub release $tag already exists in $ReleaseRepository."
            }
        }
    }

    $localTagExists = Test-LocalTagExists $tag
    if ($null -eq $remoteRelease) {
        if ($localTagExists) {
            $headCommit = Get-GitOutput @('rev-parse', 'HEAD')
            $tagCommit = Get-GitOutput @('rev-parse', "$tag^{commit}")
            if (-not $AllowCurrentCommitTag -or $tagCommit -cne $headCommit) {
                Fail "Tag $tag already exists locally."
            }

            Write-Host "Tag $tag already targets the current commit; deferring release-state validation to the publisher."
        }
    } else {
        $headCommit = Get-GitOutput @('rev-parse', 'HEAD')
        $draftTargetCommit = $null
        if ($localTagExists) {
            $draftTargetCommit = Get-GitOutput @('rev-parse', "$tag^{commit}")
        } else {
            $targetCommitish = [string]$remoteRelease.target_commitish
            $targetCandidates = @($targetCommitish)
            if ($targetCommitish -notmatch '^[0-9a-fA-F]{40}$') {
                $targetCandidates += "origin/$targetCommitish"
            }

            foreach ($candidate in $targetCandidates) {
                $draftTargetCommit = Get-GitOutput @('rev-parse', "$candidate^{commit}") -AllowFailure
                if (-not [string]::IsNullOrWhiteSpace($draftTargetCommit)) {
                    break
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($draftTargetCommit) -or $draftTargetCommit -cne $headCommit) {
            Fail "Draft release $tag does not target the current commit and cannot be recovered safely."
        }

        Write-Host "Found recoverable draft release $tag for the current commit in $ReleaseRepository."
    }

    if (-not [string]::IsNullOrWhiteSpace($ChangelogEntryPath)) {
        $fullEntryPath = Resolve-FullPath $ChangelogEntryPath
        $entryDirectory = Split-Path -Parent $fullEntryPath
        if (-not [string]::IsNullOrWhiteSpace($entryDirectory)) {
            New-Item -ItemType Directory -Force -Path $entryDirectory | Out-Null
        }
        Set-Content -LiteralPath $fullEntryPath -Value $changelogEntry -NoNewline
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "version=$rootVersion"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "tag=$tag"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "vpm_archive=com.furroxide.bhaptics-vrchat-$rootVersion.zip"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "compressor_archive=com.furroxide.contact-compressor-$($compressorManifest.version).zip"
    }

    Write-Host "Release metadata is valid for $tag."
} finally {
    Pop-Location
}
