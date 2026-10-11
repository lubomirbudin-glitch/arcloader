# Uploads a released build to the website's download storage (user 2026-10-11: downloads from our own site, not GitHub).
# Storage: the public Tigris bucket on Fly.io (S3 API, https://fly.storage.tigris.dev); the website serves it as
# https://<site>/download/<file> (Caddy reverse proxy in the web repo). Credentials come from the user environment:
# ARCLOADER_S3_KEY, ARCLOADER_S3_SECRET (never stored in the repo); bucket ARCLOADER_S3_BUCKET (default arcloader-dl).
# Dot-source it (publish.ps1 does) and call Publish-WebChannel, or run it directly:
#   web-upload.ps1 -Channel stable|dev -Zip <path> -Version <x.y.z> [-Commit <sha>]
param(
    [ValidateSet('stable', 'dev')][string]$Channel,
    [string]$Zip,
    [string]$Version,
    [string]$Commit
)
$ErrorActionPreference = 'Stop'

function Get-WebStorage {
    $get = { param($n) $v = [Environment]::GetEnvironmentVariable($n, 'Process'); if (-not $v) { $v = [Environment]::GetEnvironmentVariable($n, 'User') }; $v }
    $s = @{
        Key    = & $get 'ARCLOADER_S3_KEY'
        Secret = & $get 'ARCLOADER_S3_SECRET'
        Bucket = $(if (& $get 'ARCLOADER_S3_BUCKET') { & $get 'ARCLOADER_S3_BUCKET' } else { 'arcloader-dl' })
        Host   = 'fly.storage.tigris.dev'
        Region = 'auto'
    }
    if (-not $s.Key -or -not $s.Secret) { throw 'ARCLOADER_S3_KEY / ARCLOADER_S3_SECRET are not set - the website download storage cannot be written.' }
    $s.PublicBase = "https://$($s.Bucket).$($s.Host)"
    return $s
}

function Get-Hex([byte[]]$Bytes) { -join ($Bytes | ForEach-Object { $_.ToString('x2') }) }
function Get-Sha256Hex([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { Get-Hex $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)) } finally { $sha.Dispose() }
}
function Get-Hmac([byte[]]$Key, [string]$Text) {
    $h = New-Object Security.Cryptography.HMACSHA256 (, $Key)
    try { $h.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)) } finally { $h.Dispose() }
}

# One object, streamed (a 300 MB zip is never held in memory), AWS Signature V4 with an unsigned payload.
function Send-WebObject([hashtable]$S, [string]$Key, [string]$Path, [string]$ContentType, [string]$CacheControl = 'public, max-age=300') {
    $now = [DateTime]::UtcNow
    $amzDate = $now.ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture)
    $date = $now.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture)
    $escaped = (($Key -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    $uriPath = '/' + $S.Bucket + '/' + $escaped
    $signed = 'host;x-amz-content-sha256;x-amz-date'
    $canonical = "PUT`n$uriPath`n`nhost:$($S.Host)`nx-amz-content-sha256:UNSIGNED-PAYLOAD`nx-amz-date:$amzDate`n`n$signed`nUNSIGNED-PAYLOAD"
    $scope = "$date/$($S.Region)/s3/aws4_request"
    $toSign = "AWS4-HMAC-SHA256`n$amzDate`n$scope`n$(Get-Sha256Hex $canonical)"
    $k = Get-Hmac ([Text.Encoding]::UTF8.GetBytes('AWS4' + $S.Secret)) $date
    $k = Get-Hmac $k $S.Region; $k = Get-Hmac $k 's3'; $k = Get-Hmac $k 'aws4_request'
    $signature = Get-Hex (Get-Hmac $k $toSign)

    $req = [Net.HttpWebRequest]::Create("https://$($S.Host)$uriPath")
    $req.Method = 'PUT'
    $req.Timeout = 30 * 60 * 1000
    $req.ReadWriteTimeout = 30 * 60 * 1000
    $req.AllowWriteStreamBuffering = $false
    $req.SendChunked = $false
    $req.ContentType = $ContentType
    $req.Headers.Add('x-amz-date', $amzDate)
    $req.Headers.Add('x-amz-content-sha256', 'UNSIGNED-PAYLOAD')
    $req.Headers.Add('Cache-Control', $CacheControl)
    $req.Headers.Add('Authorization', "AWS4-HMAC-SHA256 Credential=$($S.Key)/$scope, SignedHeaders=$signed, Signature=$signature")
    $file = [IO.File]::OpenRead($Path)
    try {
        $req.ContentLength = $file.Length
        $out = $req.GetRequestStream()
        try { $file.CopyTo($out, 1MB) } finally { $out.Dispose() }
    } finally { $file.Dispose() }
    try { $req.GetResponse().Dispose() }
    catch [Net.WebException] {
        $body = ''
        if ($_.Exception.Response) { $r = New-Object IO.StreamReader ($_.Exception.Response.GetResponseStream()); $body = $r.ReadToEnd(); $r.Dispose() }
        throw "Upload of $Key failed: $($_.Exception.Message) $body"
    }
}

# Uploads the channel's zip (fixed name), the update manifest (stable) and channels.json, which the website reads to show
# the version of each channel. The zip goes first; the files that point at it last.
function Publish-WebChannel([string]$Channel, [string]$Zip, [string]$Version, [string]$Commit) {
    $S = Get-WebStorage
    $zipName = if ($Channel -eq 'stable') { 'ArcLoader-Windows.zip' } else { 'ArcLoader-Windows-dev.zip' }
    Send-WebObject $S $zipName $Zip 'application/zip'

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('arcloader-web-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $utf8 = New-Object Text.UTF8Encoding $false
        if ($Channel -eq 'stable') {
            $manifest = [ordered]@{ version = $Version; url = '/download/'; notes = "ArcLoader for Windows $Version" } | ConvertTo-Json
            $mf = Join-Path $tmp 'arcloader-update.json'
            [IO.File]::WriteAllText($mf, $manifest, $utf8)
            Send-WebObject $S 'arcloader-update.json' $mf 'application/json'
        }
        # channels.json: the other channel's entry is kept as it is online.
        $channels = [ordered]@{}
        try {
            $wc = New-Object Net.WebClient
            $wc.Encoding = [Text.Encoding]::UTF8
            $old = $wc.DownloadString("$($S.PublicBase)/channels.json?nocache=$([guid]::NewGuid().ToString('N'))") | ConvertFrom-Json
            foreach ($p in $old.PSObject.Properties) { $channels[$p.Name] = $p.Value }
        } catch { }
        $channels[$Channel] = [ordered]@{
            version  = $Version
            file     = $zipName
            size     = (Get-Item $Zip).Length
            date     = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
            commit   = $Commit
            platform = 'Windows 10/11 x64'
        }
        $cf = Join-Path $tmp 'channels.json'
        [IO.File]::WriteAllText($cf, ($channels | ConvertTo-Json -Depth 4), $utf8)
        Send-WebObject $S 'channels.json' $cf 'application/json' 'public, max-age=60'
    } finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
    Write-Host "website: $zipName ($Version) uploaded to $($S.PublicBase)/$zipName"
}

if ($Channel -and $Zip -and $Version) { Publish-WebChannel -Channel $Channel -Zip $Zip -Version $Version -Commit $Commit }
