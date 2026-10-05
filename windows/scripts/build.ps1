# Builds windows\build\Offload: Offload.exe (GUI) + bin\offload.exe (CLI) + bin\ffmpeg.exe, ffprobe.exe (all self-contained).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'build\Offload'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\Offload.Cli\Offload.Cli.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
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
}
Write-Host "built $out"