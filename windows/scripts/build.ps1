# Builds windows\build\Offload: bin\offload.exe (self-contained, single file).
# Plan 2 adds bin\ffmpeg.exe + ffprobe.exe, plan 4 the GUI Offload.exe next to bin\.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'build\Offload'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\Offload.Cli\Offload.Cli.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:DebugType=none -o (Join-Path $out 'bin')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Write-Host "built $out"