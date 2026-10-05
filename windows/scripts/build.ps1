# Builds windows\build\Offload: bin\offload.exe (self-contained, single file) + bin\ffmpeg.exe, ffprobe.exe.
# Plan 4 adds the GUI Offload.exe next to bin\.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'build\Offload'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\Offload.Cli\Offload.Cli.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
# ffmpeg/ffprobe: windows\tools\bin first (pinned copy), otherwise whatever is on PATH.
foreach ($t in 'ffmpeg.exe', 'ffprobe.exe') {
    $local = Join-Path $root "tools\bin\$t"
    $src = if (Test-Path $local) { $local } else { (Get-Command $t -ErrorAction SilentlyContinue).Source }
    if ($src) { Copy-Item $src (Join-Path $out 'bin'); Write-Host "bundled $t from $src" } else { Write-Warning "$t not found - proxies and thumbnails will need it at runtime" }
}
Write-Host "built $out"