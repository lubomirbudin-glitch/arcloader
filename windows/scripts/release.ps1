# Release helpers for build.ps1 (spec 4.4, 4.5): the version, the update manifest and code signing.
# Dot-source it: . (Join-Path $PSScriptRoot 'release.ps1'). Windows PowerShell 5.1 and PowerShell 7.
# Saved as UTF-8 with BOM: Windows PowerShell 5.1 reads a file without one in the ANSI code page (the dashes in the messages).

# The version from Directory.Build.props (R22: the one source), e.g. "0.4.0".
function Get-OffloadVersion([string]$Props) {
    $v = ([xml](Get-Content -Raw -Encoding UTF8 $Props)).Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $v) { throw "Version not found in $Props." }
    return $v.Trim()
}

# build\offload-update.json for the update check: {"version", "url", "notes"}, UTF-8 without BOM.
function Write-UpdateManifest([string]$Path, [string]$Version, [string]$Url, [string]$Notes) {
    $json = [ordered]@{ version = $Version; url = $Url; notes = $Notes } | ConvertTo-Json
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding $false))
}

# signtool.exe: $env:SIGNTOOL, else PATH, else the newest Windows SDK. A SIGNTOOL that points nowhere is an error
# (never silently another signtool).
function Find-SignTool {
    if ($env:SIGNTOOL) {
        if (Test-Path -LiteralPath $env:SIGNTOOL -PathType Leaf) { return $env:SIGNTOOL }
        throw 'signtool not found — install the Windows SDK (Signing Tools for Desktop Apps), or set SIGNTOOL.'
    }
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk = Get-ChildItem -Path $kits -Directory -Filter '10.*' -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($sdk) { return $sdk }
    throw 'signtool not found — install the Windows SDK (Signing Tools for Desktop Apps), or set SIGNTOOL.'
}

# The password of the PFX never appears in anything printed.
function Hide-Secret([string]$Text) {
    if ($env:OFFLOAD_SIGN_PFX_PASSWORD) { return $Text.Replace($env:OFFLOAD_SIGN_PFX_PASSWORD, '***') }
    return $Text
}

# A certificate that could not sign is refused before the build: expired, no private key, or (when it names its
# purposes at all) not for code signing. $Label names it in the message.
function Assert-SigningCertificate($Cert, [string]$Label) {
    if ($Cert.NotAfter -lt (Get-Date)) { throw "Certificate $Label expired on $($Cert.NotAfter.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture))." }
    if (-not $Cert.HasPrivateKey) { throw "Certificate $Label has no private key — it cannot sign." }
    $ekuExtension = $Cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
    if ($ekuExtension) {
        $eku = New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($ekuExtension, $false)
        if (-not ($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })) {
            throw "Certificate $Label is not meant for code signing (the usage 1.3.6.1.5.5.7.3.3 is missing)."
        }
    }
}

# Checked before the build starts: signtool and the certificate — by thumbprint (CurrentUser\My, then LocalMachine\My;
# also a hardware token) or OFFLOAD_SIGN_PFX + OFFLOAD_SIGN_PFX_PASSWORD. An expired certificate, one without a private
# key or not meant for code signing is refused. $StoreRoots and $CertLookup exist for the tests (nothing real is read):
# the folders searched for the thumbprint, or a script block ($thumbprint) → certificate that replaces the search.
function Get-OffloadSignSetup([string]$CertThumbprint, [string[]]$StoreRoots = @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My'), [scriptblock]$CertLookup) {
    $signtool = Find-SignTool
    if ($CertThumbprint) {
        $t = ($CertThumbprint -replace '\s', '').ToUpperInvariant()
        # A thumbprint is 40 hex characters; nothing else (wildcards, paths) reaches the certificate lookup.
        if ($t -notmatch '^[0-9A-F]{40}$') { throw 'The certificate thumbprint must have 40 hexadecimal characters (SHA-1, no colons).' }
        if ($env:OFFLOAD_SIGN_PFX) { Write-Host 'Both a thumbprint and OFFLOAD_SIGN_PFX are given — the certificate of the thumbprint is used, OFFLOAD_SIGN_PFX is not used.' }
        $cert = $null
        if ($CertLookup) {
            $cert = & $CertLookup $t
        } else {
            foreach ($root in $StoreRoots) {
                if (-not $cert) { $cert = Get-ChildItem -LiteralPath (Join-Path $root $t) -ErrorAction SilentlyContinue }
            }
        }
        if (-not $cert) { throw "Certificate with thumbprint $t not found in CurrentUser\My or LocalMachine\My." }
        Assert-SigningCertificate $cert "s otiskem $t"
        return @{ SignTool = $signtool; CertArgs = @('/sha1', $t) }
    }
    if ($env:OFFLOAD_SIGN_PFX) {
        if (-not (Test-Path -LiteralPath $env:OFFLOAD_SIGN_PFX -PathType Leaf)) { throw "Certificate file not found: $env:OFFLOAD_SIGN_PFX (OFFLOAD_SIGN_PFX)." }
        if (-not $env:OFFLOAD_SIGN_PFX_PASSWORD) { throw 'OFFLOAD_SIGN_PFX_PASSWORD is missing.' }
        # EphemeralKeySet: the key is never written to a key container in the user profile.
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        try {
            $pfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:OFFLOAD_SIGN_PFX, $env:OFFLOAD_SIGN_PFX_PASSWORD, $flags)
        } catch {
            throw "Cannot open certificate $env:OFFLOAD_SIGN_PFX (wrong password in OFFLOAD_SIGN_PFX_PASSWORD?)."
        }
        try {
            Assert-SigningCertificate $pfx $env:OFFLOAD_SIGN_PFX
        } finally {
            $pfx.Dispose()
        }
        return @{ SignTool = $signtool; CertArgs = @('/f', $env:OFFLOAD_SIGN_PFX, '/p', $env:OFFLOAD_SIGN_PFX_PASSWORD) }
    }
    throw 'No certificate given: use -CertThumbprint <thumbprint>, or set OFFLOAD_SIGN_PFX and OFFLOAD_SIGN_PFX_PASSWORD.'
}

# One signtool call: its output (stdout and stderr together, the password hidden) and exit code. Windows PowerShell 5.1
# throws on the first stderr line of a native command when $ErrorActionPreference is 'Stop' and stderr is redirected —
# signtool writes every error there — so the preference is relaxed locally (ruling I4).
function Invoke-SignToolCall([string]$SignTool, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $text = (& $SignTool @Arguments 2>&1 | ForEach-Object { "$_" } | Out-String)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    return @{ Code = $code; Text = (Hide-Secret $text) }
}

# Signs our own exes (R23) with SHA-256 and an RFC 3161 timestamp, then verifies. A timestamp that fails is tried
# 3 times, $RetryDelaySeconds apart (5 s); every failure ends the build with a Czech message.
function Invoke-OffloadSign([hashtable]$Setup, [string[]]$Files, [string]$TimestampUrl = 'http://timestamp.digicert.com', [int]$RetryDelaySeconds = 5) {
    foreach ($f in $Files) { if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "File to sign not found: $f" } }
    $signtool = $Setup.SignTool
    $signArguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl) + @($Setup.CertArgs) + @('/d', 'Offload') + @($Files)
    for ($attempt = 1; ; $attempt++) {
        $r = Invoke-SignToolCall $signtool $signArguments
        if ($r.Code -eq 0) { break }
        if ($r.Text -match '(?i)timestamp') {
            if ($attempt -lt 3) {
                Write-Warning "Could not get a timestamp from $TimestampUrl (attempt $attempt of 3) — trying again."
                Start-Sleep -Seconds $RetryDelaySeconds
                continue
            }
            throw "Could not get a timestamp from $TimestampUrl."
        }
        throw "Signing failed: $($r.Text.Trim())"
    }
    $v = Invoke-SignToolCall $signtool (@('verify', '/pa', '/all') + @($Files))
    if ($v.Code -ne 0) { throw "Cannot verify the signature: $($v.Text.Trim())" }
    foreach ($f in $Files) { Write-Host "signed: $f" }
}
