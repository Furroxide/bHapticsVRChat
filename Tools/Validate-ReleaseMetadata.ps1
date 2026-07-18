[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$BaseRef,
    [string]$ReleaseRepository = $env:GITHUB_REPOSITORY,
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [string]$ChangelogEntryPath,
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

function Get-VersionFromPackageJson([string]$Path) {
    $content = Read-RequiredText $Path
    try {
        $package = $content | ConvertFrom-Json
    } catch {
        Fail "Could not parse JSON in $Path. $($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($package.version)) {
        Fail "Missing version in $Path."
    }

    [string]$package.version
}

function Get-LatestTagVersion {
    $tags = Get-GitOutput @('tag', '--list', 'v[0-9]*.[0-9]*.[0-9]*') -AllowFailure
    if ([string]::IsNullOrWhiteSpace($tags)) {
        return $null
    }

    $versions = @()
    foreach ($tag in ($tags -split "`n")) {
        $trimmed = $tag.Trim()
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

function Get-BaselineVersion([string]$RequestedBaseRef) {
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

    $tagVersion = Get-LatestTagVersion
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
    $matches = [regex]::Matches($content, $headingPattern)

    if ($matches.Count -eq 0) {
        Fail "CHANGELOG.md must contain at least one '## [X.Y.Z] - YYYY-MM-DD' entry."
    }

    $latest = $matches[0]
    $latestVersion = $latest.Groups['version'].Value
    if ($latestVersion -ne $Version) {
        Fail "Latest CHANGELOG.md entry must be [$Version], got [$latestVersion]."
    }

    $nextStart = $content.Length
    if ($matches.Count -gt 1) {
        $nextStart = $matches[1].Index
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

function Test-RemoteReleaseExists([string]$Repository, [string]$Tag, [string]$Token) {
    if ([string]::IsNullOrWhiteSpace($Repository) -or [string]::IsNullOrWhiteSpace($Token)) {
        Write-Warning 'Skipping remote release check because ReleaseRepository or GitHubToken is not available.'
        return $false
    }

    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $Token"
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $uri = "https://api.github.com/repos/$Repository/releases/tags/$Tag"

    try {
        $null = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
        return $true
    } catch {
        $response = $_.Exception.Response
        if ($response -and [int]$response.StatusCode -eq 404) {
            return $false
        }

        throw
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

    Assert-StrictVersion $buildInfoVersion 'BuildInfo.Version'
    Assert-StrictVersion $packageVersion 'Unity package version'
    Assert-StrictVersion $packageTextVersion 'Unity package version.txt'

    if ($buildInfoVersion -ne $rootVersion) {
        Fail "BuildInfo.Version ($buildInfoVersion) must match VERSION ($rootVersion)."
    }

    if ($packageVersion -ne $rootVersion) {
        Fail "Unity package version ($packageVersion) must match VERSION ($rootVersion)."
    }

    if ($packageTextVersion -ne $rootVersion) {
        Fail "Unity package version.txt ($packageTextVersion) must match VERSION ($rootVersion)."
    }

    $baseline = Get-BaselineVersion $BaseRef
    if ($baseline) {
        if ((Compare-StrictVersion $rootVersion $baseline.Version) -le 0) {
            Fail "VERSION ($rootVersion) must be greater than $($baseline.Source) ($($baseline.Version))."
        }
        Write-Host "Version baseline: $($baseline.Source) = $($baseline.Version)"
    } else {
        Write-Warning 'No baseline VERSION or v* tag found; skipping version-increase comparison.'
    }

    $changelogEntry = Get-ChangelogEntry (Join-Path $RepositoryRoot 'CHANGELOG.md') $rootVersion

    $tag = "v$rootVersion"
    if (Test-LocalTagExists $tag) {
        Fail "Tag $tag already exists locally."
    }

    if (-not $SkipRemoteReleaseCheck -and (Test-RemoteReleaseExists $ReleaseRepository $tag $GitHubToken)) {
        Fail "GitHub release $tag already exists in $ReleaseRepository."
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
    }

    Write-Host "Release metadata is valid for $tag."
} finally {
    Pop-Location
}
