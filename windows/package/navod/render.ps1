# Prints navod.html (with offload-icon.png next to it) to windows\package\Offload-navod.pdf with Microsoft Edge
# in headless mode - the same way the app prints its PDF report (PdfReport.cs): a throwaway profile, no header/footer,
# page size and margins from the page's @page rule.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$html = Join-Path $here 'navod.html'
$pdf = Join-Path (Split-Path -Parent $here) 'Offload-navod.pdf'

$edge = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('offload-navod-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
$tmp = Join-Path $work 'navod.pdf'
try {
    $uri = ([Uri]$html).AbsoluteUri
    $edgeArgs = @(
        '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--disable-extensions',
        "--user-data-dir=`"$(Join-Path $work 'profile')`"",
        '--no-pdf-header-footer', "--print-to-pdf=`"$tmp`"", $uri
    )
    $p = Start-Process -FilePath $edge -ArgumentList $edgeArgs -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Edge did not print the manual in 120 s' }
    if (-not (Test-Path $tmp)) { throw 'Edge did not produce a PDF' }
    $head = [IO.File]::ReadAllBytes($tmp)[0..3]
    if ([Text.Encoding]::ASCII.GetString($head) -ne '%PDF') { throw 'Edge output is not a PDF' }
    Move-Item -Force $tmp $pdf
    Write-Host "printed $pdf ($([math]::Round((Get-Item $pdf).Length / 1KB)) KB)"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
