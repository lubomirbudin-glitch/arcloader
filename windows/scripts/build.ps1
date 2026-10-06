# Builds windows\build\Offload: Offload.exe (GUI) + bin\offload.exe (CLI) + bin\braw-decode.exe + bin\ffmpeg.exe, ffprobe.exe (all self-contained).
# -Staging builds into build\staging\Offload instead (e.g. while build\Offload\Offload.exe is running); same zip.
param([switch]$Staging)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root $(if ($Staging) { 'build\staging\Offload' } else { 'build\Offload' })
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\Offload.Cli\Offload.Cli.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
# braw-decode: Blackmagic RAW decoder (trimmed single file; BlackmagicRawAPI.dll comes from the SDK, Player or Resolve).
dotnet publish (Join-Path $root 'src\Offload.BrawDecode\Offload.BrawDecode.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (braw-decode) failed' }
# GUI: Offload.exe next to bin\ (single file; WPF native libraries are extracted at first start).
dotnet publish (Join-Path $root 'src\Offload.App\Offload.App.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (app) failed' }
# ffmpeg/ffprobe: windows\tools\bin first (pinned copy), otherwise whatever is on PATH.
# A tool on PATH under 5 MB is a launcher shim or a shared build (needs DLLs next to it): not bundled.
foreach ($t in 'ffmpeg.exe', 'ffprobe.exe') {
    $local = Join-Path $root "tools\bin\$t"
    $src = $null
    if (Test-Path $local) {
        $src = $local
    } else {
        $found = (Get-Command $t -ErrorAction SilentlyContinue).Source
        if ($found -and (Get-Item $found).Length -lt 5MB) {
            Write-Warning "$t on PATH ($found) is smaller than 5 MB - probably a shim or a shared build; skipped"
        } elseif ($found) {
            $src = $found
        }
    }
    if ($src) { Copy-Item $src (Join-Path $out 'bin'); Write-Host "bundled $t from $src" } else { Write-Warning "$t not bundled - proxies and thumbnails will need it at runtime" }
    # GPL: ship the licence and the build's README (version + source link) next to the binaries.
    if ($src -and -not (Test-Path (Join-Path $out 'licence\ffmpeg'))) {
        $buildDir = Split-Path -Parent (Split-Path -Parent $src)
        $notices = 'LICENSE', 'README.txt' | ForEach-Object { Join-Path $buildDir $_ } | Where-Object { Test-Path $_ }
        if ($notices) {
            New-Item -ItemType Directory -Force (Join-Path $out 'licence\ffmpeg') | Out-Null
            Copy-Item $notices (Join-Path $out 'licence\ffmpeg')
        } else { Write-Warning "ffmpeg licence not found next to $src - add it to licence\ffmpeg before sharing the zip" }
    }
}
# Instructions for whoever unpacks the zip.
Copy-Item (Join-Path $root 'package\CTI-ME.txt') $out
# User manual (Czech PDF, printed from package\navod\navod.html by package\navod\render.ps1).
Copy-Item (Join-Path $root 'package\Offload-navod.pdf') $out
# Built-in LUTs (shared with the Mac version).
$luts = Join-Path (Split-Path -Parent $root) 'shared\luts'
New-Item -ItemType Directory -Force (Join-Path $out 'luts') | Out-Null
Copy-Item (Join-Path $luts '*.cube'), (Join-Path $luts 'luts.json') (Join-Path $out 'luts')
New-Item -ItemType Directory -Force (Join-Path $out 'licence') | Out-Null
Copy-Item (Join-Path $luts 'README.md') (Join-Path $out 'licence\LUTy-README.md')
$zip = Join-Path $root 'build\Offload-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $out -DestinationPath $zip
Write-Host "zipped $zip"
Write-Host "built $out"