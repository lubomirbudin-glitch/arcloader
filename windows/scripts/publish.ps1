# Publishes the built zip on GitHub Releases, so that fixed links always point to the newest build (user 2026-10-10):
#   -Channel stable  → release "stable" (rolling tag, marked Latest): Offload-Windows.zip + offload-update.json
#   -Channel dev     → release "dev" (rolling tag, pre-release):       Offload-Windows-dev.zip
# and in both cases the versioned archive release "windows-v<version>" with Offload-win-x64-<version>.zip
# (pre-release while it is only a dev build; publishing the same version as stable makes it a normal release).
# Run it in the checkout the zip was built from (scripts\build.ps1), after that commit is pushed to origin/main.
# The release notes come from docs\VYVOJ-WINDOWS.md: the sections of the versions newer than -Since (default: this one).
param(
    [Parameter(Mandatory = $true)][ValidateSet('stable', 'dev')][string]$Channel,
    [string]$Since,
    [string]$Repo = 'lubomirbudin-glitch/offload',
    # Print the release notes and stop (nothing is uploaded).
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release.ps1')
$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$version = Get-OffloadVersion (Join-Path $root 'Directory.Build.props')
$zip = Join-Path $root "build\Offload-win-x64-$version.zip"
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
    if (-not $picked) { return "Podrobnosti v [docs/VYVOJ-WINDOWS.md](https://github.com/$Repo/blob/main/docs/VYVOJ-WINDOWS.md)." }
    # Repository-relative links in the log must work from the release page too.
    return (($picked -join "`n`n") -replace '\]\((?!https?://|#)([^)]+)\)', "](https://github.com/$Repo/blob/main/docs/`$1)")
}

$base = "https://github.com/$Repo/releases"
$stableZip = "$base/download/stable/Offload-Windows.zip"
$devZip = "$base/download/dev/Offload-Windows-dev.zip"
# Default: only this version's own section(s); -Since 0.6.1 also lists 0.7.0, 0.7.1 … up to this version.
$notesBody = Get-VersionNotes -From $Since -To $version

$intro = if ($Channel -eq 'stable') {
    "**Stabilní verze** — poslední vydaná verze Offloadu pro Windows, všechny automatické testy prošly.`n`n" +
    "### ⬇️ Stáhnout: [Offload-Windows.zip]($stableZip)"
} else {
    "**Vývojová verze** — nejnovější build z vývoje. Všechny automatické testy prošly, ale může obsahovat nedokončené " +
    "nebo ručně neověřené změny. Na place používejte [stabilní verzi]($base/tag/stable).`n`n" +
    "### ⬇️ Stáhnout: [Offload-Windows-dev.zip]($devZip)"
}
$notes = @"
$intro

Verze **$version** · commit [$short](https://github.com/$Repo/commit/$commit)

**Jak spustit:** Windows 10/11 x64. Rozbalte ZIP kamkoli a spusťte ``Offload\Offload.exe`` — nic se neinstaluje.
Uživatelský návod je v balíčku (``Offload-navod.pdf``), krátký úvod v ``CTI-ME.txt``. Windows může u nepodepsaného
programu ukázat SmartScreen: *Další informace → Přesto spustit*.

**Dekodéry RAW** (nejsou v balíčku, licence výrobců to nedovolují; bez nich se klipy zkopírují a ověří, jen bez proxy):
[Blackmagic RAW](https://www.blackmagicdesign.com/products/blackmagicraw) ·
[REDCINE-X PRO (REDline)](https://www.reddigitalcinema.com/downloads) ·
[ARRI Reference Tool (art-cmd)](https://www.arri.com/en/learn/camera-systems/tools/arri-reference-tool)

Stabilní verze: [$base/tag/stable]($base/tag/stable) · vývojová verze: [$base/tag/dev]($base/tag/dev) ·
všechny verze: [$base]($base) · zdrojový kód (jen k nahlédnutí, všechna práva vyhrazena): https://github.com/$Repo

## Co je nového

$notesBody
"@
if ($DryRun) { Write-Output $notes; return }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("offload-publish-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $notesFile = Join-Path $tmp 'notes.md'
    [IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))

    # 1) Archive release windows-v<version>.
    $versionTag = "windows-v$version"
    $title = "Offload pro Windows $version" + $(if ($Channel -eq 'dev') { ' (vývojová)' } else { '' })
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

    # 2) Rolling channel release: its tag moves to this commit, its asset keeps one name.
    if (Test-Tag $Channel) {
        Invoke-Gh api -X PATCH "repos/$Repo/git/refs/tags/$Channel" -f "sha=$commit" -F force=true | Out-Null
    } else {
        Invoke-Gh api "repos/$Repo/git/refs" -f "ref=refs/tags/$Channel" -f "sha=$commit" | Out-Null
    }
    $assetName = if ($Channel -eq 'stable') { 'Offload-Windows.zip' } else { 'Offload-Windows-dev.zip' }
    $asset = Join-Path $tmp $assetName
    Copy-Item $zip $asset
    $assets = @($asset)
    if ($Channel -eq 'stable') {
        # The update check (Nastavení → Aplikace → Aktualizace…) can read this manifest:
        # https://github.com/<repo>/releases/download/stable/offload-update.json
        $manifest = Join-Path $tmp 'offload-update.json'
        Write-UpdateManifest -Path $manifest -Version $version -Url "$base/tag/stable" -Notes "Offload pro Windows $version"
        $assets += $manifest
    }
    $channelTitle = if ($Channel -eq 'stable') { "Offload pro Windows — stabilní verze ($version)" } else { "Offload pro Windows — vývojová verze ($version)" }
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
    Write-Host "  download: $(if ($Channel -eq 'stable') { $stableZip } else { $devZip })"
    Write-Host "  archive:  $base/tag/$versionTag"
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
