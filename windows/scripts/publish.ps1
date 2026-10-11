# Publishes the built zip on GitHub Releases, so that fixed links always point to the newest build (user 2026-10-10):
#   -Channel stable  → release "stable" (rolling tag, marked Latest): mmMover-Windows.zip + mmmover-update.json
#   -Channel dev     → release "dev" (rolling tag, pre-release):       mmMover-Windows-dev.zip
# and in both cases the versioned archive release "windows-v<version>" with mmMover-win-x64-<version>.zip
# (pre-release while it is only a dev build; publishing the same version as stable makes it a normal release).
# Since the renames (0.10.0 Offload → ArcLoader, 0.11.0 ArcLoader → mmMover) the channel zip is also uploaded under its
# older names (ArcLoader-Windows(-dev).zip, Offload-Windows(-dev).zip — links already out there point there), and stable
# also carries arcloader-update.json and offload-update.json (the same manifest — 0.10.x and versions before 0.10.0 read
# them). The names: Get-ChannelAssetNames (release.ps1).
# Run it in the checkout the zip was built from (scripts\build.ps1), after that commit is pushed to origin/main.
# The release notes come from docs\VYVOJ-WINDOWS.md: the sections of the versions newer than -Since (default: this one).
param(
    [Parameter(Mandatory = $true)][ValidateSet('stable', 'dev')][string]$Channel,
    [string]$Since,
    [string]$Repo = 'lubomirbudin-glitch/mmmover',
    # Print the release notes and stop (nothing is uploaded).
    [switch]$DryRun,
    # Do not upload to the website's download storage (web-upload.ps1).
    [switch]$SkipWeb
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release.ps1')
$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$version = Get-OffloadVersion (Join-Path $root 'Directory.Build.props')
$zip = Join-Path $root ('build\' + (Get-ReleaseZipName $version))
if (-not (Test-Path $zip)) { throw "$zip not found - run scripts\build.ps1 first" }

function Invoke-Gh {
    & gh @args
    if ($LASTEXITCODE -ne 0) { throw "gh $($args -join ' ') failed" }
}
# Windows PowerShell 5.1 turns a native tool's stderr into a terminating error under 'Stop': probes run with 'Continue'.
function Test-Release([string]$Tag) {
    $ErrorActionPreference = 'Continue'
    & gh release view $Tag --repo $Repo --json tagName 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}
function Test-Tag([string]$Tag) {
    $ErrorActionPreference = 'Continue'
    & gh api "repos/$Repo/git/ref/tags/$Tag" 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

# The release must be the code that is on GitHub.
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
& git -C $repoRoot fetch -q origin
& git -C $repoRoot merge-base --is-ancestor $commit origin/main
if ($LASTEXITCODE -ne 0) { throw "commit $commit is not on origin/main - push it first" }
$short = $commit.Substring(0, 7)

# Release notes: the VYVOJ sections whose heading or first lines name a version in (Since, version].
function Get-VersionNotes([string]$From, [string]$To) {
    $text = [IO.File]::ReadAllText((Join-Path $repoRoot 'docs\VYVOJ-WINDOWS.md'), [Text.Encoding]::UTF8) -replace "`r`n", "`n"
    $sections = [regex]::Split($text, '(?m)^(?=## )') | Where-Object { $_ -match '^## ' -and $_ -notmatch '^## (Plány|Naplánováno)' }
    $picked = foreach ($s in $sections) {
        $head = ($s -split "`n" | Select-Object -First 4) -join "`n"
        $m = [regex]::Match($head, '(?<![\d.])(\d+\.\d+\.\d+)(?![\d.])')
        if (-not $m.Success) { continue }
        $v = [version]$m.Groups[1].Value
        $inRange = if ($From) { $v -gt [version]$From -and $v -le [version]$To } else { $v -eq [version]$To }
        if ($inRange) { $s.TrimEnd() -replace '^## ', '### ' }
    }
    if (-not $picked) { return "Details in [docs/VYVOJ-WINDOWS.md](https://github.com/$Repo/blob/main/docs/VYVOJ-WINDOWS.md) (in Czech)." }
    # Repository-relative links in the log must work from the release page too.
    return (($picked -join "`n`n") -replace '\]\((?!https?://|#)([^)]+)\)', "](https://github.com/$Repo/blob/main/docs/`$1)")
}

$base = "https://github.com/$Repo/releases"
$channelAssets = Get-ChannelAssetNames $Channel
# The first name is the new one: the one the notes link to.
$downloadName = $channelAssets.Zips[0]
$download = "$base/download/$Channel/$downloadName"
# Default: only this version's own section(s); -Since 0.6.1 also lists 0.7.0, 0.7.1 … up to this version.
$notesBody = Get-VersionNotes -From $Since -To $version

$intro = if ($Channel -eq 'stable') {
    "**Stable version** — the latest released version of mmMover for Windows; all automated tests passed.`n`n" +
    "### ⬇️ Download: [$downloadName]($download)"
} else {
    "**Development version** — the newest development build. All automated tests passed, but it may contain unfinished " +
    "or not yet manually tested changes. On set, use the [stable version]($base/tag/stable).`n`n" +
    "### ⬇️ Download: [$downloadName]($download)"
}
$notes = @"
$intro

Version **$version** · commit [$short](https://github.com/$Repo/commit/$commit)

**How to run:** Windows 10/11 x64. Unpack the ZIP anywhere and run ``mmMover\mmMover.exe`` — nothing is installed.
The user manual is in the package (``mmMover-manual.pdf``), a short intro in ``READ-ME.txt``. For an unsigned program
Windows may show SmartScreen: *More info → Run anyway*.

**RAW decoders** (not in the package, the makers' licences do not allow it; without them clips are copied and verified, just without proxies):
[Blackmagic RAW](https://www.blackmagicdesign.com/products/blackmagicraw) ·
[REDCINE-X PRO (REDline)](https://www.reddigitalcinema.com/downloads) ·
[ARRI Reference Tool (art-cmd)](https://www.arri.com/en/learn/camera-systems/tools/arri-reference-tool)

Stable version: [$base/tag/stable]($base/tag/stable) · development version: [$base/tag/dev]($base/tag/dev) ·
all versions: [$base]($base) · source code (view only, all rights reserved): https://github.com/$Repo

## Changes (development log, Czech)

$notesBody
"@
if ($DryRun) { Write-Output $notes; return }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("mmmover-publish-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $notesFile = Join-Path $tmp 'notes.md'
    [IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))

    # 1) Archive release windows-v<version>.
    $versionTag = "windows-v$version"
    $title = "mmMover for Windows $version" + $(if ($Channel -eq 'dev') { ' (development)' } else { '' })
    $released = (Test-Release $versionTag) -and -not [bool]((& gh release view $versionTag --repo $Repo --json isPrerelease | ConvertFrom-Json).isPrerelease)
    if ($Channel -eq 'dev' -and $released) {
        Write-Host "$versionTag is already a stable release - archive left as it is"
    } elseif (Test-Release $versionTag) {
        Invoke-Gh release upload $versionTag $zip --repo $Repo --clobber
        $flags = @('--title', $title, '--notes-file', $notesFile, '--latest=false')
        if ($Channel -eq 'stable') { $flags += '--prerelease=false' }
        Invoke-Gh release edit $versionTag --repo $Repo @flags
    } else {
        $flags = @('--target', $commit, '--title', $title, '--notes-file', $notesFile, '--latest=false')
        if ($Channel -eq 'dev') { $flags += '--prerelease' }
        Invoke-Gh release create $versionTag $zip --repo $Repo @flags
    }

    # 2) Rolling channel release: its tag moves to this commit, its assets keep their names.
    if (Test-Tag $Channel) {
        Invoke-Gh api -X PATCH "repos/$Repo/git/refs/tags/$Channel" -f "sha=$commit" -F force=true | Out-Null
    } else {
        Invoke-Gh api "repos/$Repo/git/refs" -f "ref=refs/tags/$Channel" -f "sha=$commit" | Out-Null
    }
    # The same zip under the new name and under the older ones (a copy each: gh uploads a file under its own name).
    $assets = @()
    foreach ($name in $channelAssets.Zips) {
        $asset = Join-Path $tmp $name
        Copy-Item $zip $asset
        $assets += $asset
    }
    # The update check (Settings → App → Updates…) can read these manifests (the same content under every name):
    # https://github.com/<repo>/releases/download/stable/mmmover-update.json (and arcloader-, offload-update.json).
    foreach ($name in $channelAssets.Manifests) {
        $manifest = Join-Path $tmp $name
        Write-UpdateManifest -Path $manifest -Version $version -Url "$base/tag/stable" -Notes "mmMover for Windows $version"
        $assets += $manifest
    }
    $channelTitle = if ($Channel -eq 'stable') { "mmMover for Windows — stable version ($version)" } else { "mmMover for Windows — development version ($version)" }
    if (Test-Release $Channel) {
        Invoke-Gh release upload $Channel @assets --repo $Repo --clobber
        $flags = @('--title', $channelTitle, '--notes-file', $notesFile)
        if ($Channel -eq 'stable') { $flags += '--latest' } else { $flags += @('--prerelease', '--latest=false') }
        Invoke-Gh release edit $Channel --repo $Repo @flags
    } else {
        $flags = @('--title', $channelTitle, '--notes-file', $notesFile, '--verify-tag')
        if ($Channel -eq 'stable') { $flags += '--latest' } else { $flags += @('--prerelease', '--latest=false') }
        Invoke-Gh release create $Channel @assets --repo $Repo @flags
    }
    Write-Host "published $version ($short) as $Channel"
    Write-Host "  page:     $base/tag/$Channel"
    Write-Host "  download: $download"
    Write-Host "            $(($channelAssets.Zips | Select-Object -Skip 1 | ForEach-Object { "$base/download/$Channel/$_" }) -join ', ') (older names, the same zip)"
    Write-Host "  archive:  $base/tag/$versionTag"
    # The website (mmmover.video …) serves its own copy: https://<site>/download/mmMover-Windows(-dev).zip and, as a
    # server-side copy, ArcLoader-Windows(-dev).zip (web-upload.ps1).
    if ($SkipWeb) {
        Write-Host '  website:  skipped (-SkipWeb)'
    } else {
        # Run as a script of its own (&), never dot-sourced: its param block would set this script's $Channel, $zip and
        # $version to nothing.
        & (Join-Path $PSScriptRoot 'web-upload.ps1') -Channel $Channel -Zip $zip -Version $version -Commit $short
    }
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
