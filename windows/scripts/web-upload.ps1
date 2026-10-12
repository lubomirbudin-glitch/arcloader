# Uploads a released build to the website's download storage (user 2026-10-11: downloads from our own site, not GitHub).
# Storage: the public Tigris bucket on Fly.io (S3 API, https://fly.storage.tigris.dev); the website serves it as
# https://<site>/download/<file> (Caddy reverse proxy in the web repo). Credentials come from the user environment:
# ARCLOADER_S3_KEY, ARCLOADER_S3_SECRET (never stored in the repo); bucket ARCLOADER_S3_BUCKET (default arcloader-dl).
# These infrastructure names stay as they are since the rename to mmMover (0.11.0).
# Run it as a script (publish.ps1 does, with &):
#   web-upload.ps1 -Channel stable|dev -Zip <path> -Version <x.y.z> [-Commit <sha>]
# or dot-source it WITHOUT arguments and call Publish-WebChannel. Never dot-source it from a script that has its own
# $Channel, $Zip or $Version: the param block below would set them to nothing in that script.
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

# The website's files of a channel (0.11.0, rename ArcLoader → mmMover): the zip that is uploaded, the copies of it under
# older names that the storage makes itself (S3 CopyObject, no second upload — the live site's buttons and links already
# out there keep working), and on stable the update manifests (the same content under every name: 0.11.0 and later read
# mmmover-update.json, 0.10.x arcloader-update.json, versions before 0.10.0 offload-update.json).
function Get-WebChannelFiles([ValidateSet('stable', 'dev')][string]$Channel) {
    $suffix = if ($Channel -eq 'dev') { '-dev' } else { '' }
    $manifests = @()
    if ($Channel -eq 'stable') { $manifests = @('mmmover-update.json', 'arcloader-update.json', 'offload-update.json') }
    return [pscustomobject]@{
        Zip       = "mmMover-Windows$suffix.zip"
        Copies    = @("ArcLoader-Windows$suffix.zip")
        Manifests = $manifests
    }
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

# AWS Signature Version 4 for one S3 request — pure (no network, the time passed in), so the tests check it.
# $Headers: every signed header, name → value (host and x-amz-date included, the date as $Now gives it). Returns the
# canonical request, the string to sign, the signed header list, the signature and the Authorization header.
function Get-SigV4([string]$Method, [string]$UriPath, [System.Collections.IDictionary]$Headers, [string]$PayloadHash,
    [string]$AccessKey, [string]$Secret, [string]$Region, [DateTime]$Now, [string]$Query = '', [string]$Service = 's3') {
    $amzDate = $Now.ToUniversalTime().ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture)
    $date = $Now.ToUniversalTime().ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture)
    $lower = @{}
    foreach ($k in $Headers.Keys) { $lower[([string]$k).ToLowerInvariant()] = ([string]$Headers[$k]).Trim() }
    [string[]]$names = @($lower.Keys)
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $canonicalHeaders = -join ($names | ForEach-Object { "$($_):$($lower[$_])`n" })
    $signed = $names -join ';'
    $canonical = "$Method`n$UriPath`n$Query`n$canonicalHeaders`n$signed`n$PayloadHash"
    $scope = "$date/$Region/$Service/aws4_request"
    $toSign = "AWS4-HMAC-SHA256`n$amzDate`n$scope`n$(Get-Sha256Hex $canonical)"
    $k = Get-Hmac ([Text.Encoding]::UTF8.GetBytes('AWS4' + $Secret)) $date
    $k = Get-Hmac $k $Region; $k = Get-Hmac $k $Service; $k = Get-Hmac $k 'aws4_request'
    $signature = Get-Hex (Get-Hmac $k $toSign)
    return [pscustomobject]@{
        AmzDate       = $amzDate
        Canonical     = $canonical
        StringToSign  = $toSign
        SignedHeaders = $signed
        Signature     = $signature
        Authorization = "AWS4-HMAC-SHA256 Credential=$AccessKey/$scope, SignedHeaders=$signed, Signature=$signature"
    }
}

# The path of an object in the bucket (path style): /<bucket>/<key>, every part of the key URL-encoded.
function Get-WebObjectPath([hashtable]$S, [string]$Key) {
    return '/' + $S.Bucket + '/' + ((($Key -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/')
}

# One PUT to the storage, signed: an upload (no $CopySource) or a server-side copy of the object $CopySource in the same
# bucket (S3 CopyObject: x-amz-copy-source: /<bucket>/<key>, no body; the copy keeps the source's Content-Type and
# Cache-Control). Pure: returns the URL, the headers to send besides Host, and the signing (Get-SigV4).
function Get-WebPutRequest([hashtable]$S, [string]$Key, [DateTime]$Now, [string]$CopySource) {
    $uriPath = Get-WebObjectPath $S $Key
    $amzDate = $Now.ToUniversalTime().ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture)
    $send = [ordered]@{ 'x-amz-content-sha256' = 'UNSIGNED-PAYLOAD'; 'x-amz-date' = $amzDate }
    if ($CopySource) { $send['x-amz-copy-source'] = Get-WebObjectPath $S $CopySource }
    $signedHeaders = [ordered]@{ host = $S.Host }
    foreach ($h in $send.Keys) { $signedHeaders[$h] = $send[$h] }
    $auth = Get-SigV4 -Method 'PUT' -UriPath $uriPath -Headers $signedHeaders -PayloadHash 'UNSIGNED-PAYLOAD' `
        -AccessKey $S.Key -Secret $S.Secret -Region $S.Region -Now $Now
    $send['Authorization'] = $auth.Authorization
    return [pscustomobject]@{ Url = "https://$($S.Host)$uriPath"; Headers = $send; Auth = $auth }
}

# The storage's answer; an error (also a 200 whose body is an <Error>, which CopyObject may send) throws with $What.
function Read-WebResponse([Net.HttpWebRequest]$Req, [string]$What) {
    try {
        $resp = $Req.GetResponse()
        try {
            $r = New-Object IO.StreamReader ($resp.GetResponseStream())
            try { $body = $r.ReadToEnd() } finally { $r.Dispose() }
        } finally { $resp.Dispose() }
    } catch [Net.WebException] {
        $body = ''
        if ($_.Exception.Response) { $r = New-Object IO.StreamReader ($_.Exception.Response.GetResponseStream()); $body = $r.ReadToEnd(); $r.Dispose() }
        throw "$What failed: $($_.Exception.Message) $body"
    }
    if ($body -match '<Error>') { throw "$What failed: $body" }
}

function New-WebPut($Put, [string]$ContentType) {
    $req = [Net.HttpWebRequest]::Create($Put.Url)
    $req.Method = 'PUT'
    $req.Timeout = 30 * 60 * 1000
    $req.ReadWriteTimeout = 30 * 60 * 1000
    $req.AllowWriteStreamBuffering = $false
    $req.SendChunked = $false
    if ($ContentType) { $req.ContentType = $ContentType }
    foreach ($h in $Put.Headers.Keys) { $req.Headers.Add($h, $Put.Headers[$h]) }
    return $req
}

# One object, streamed (a 300 MB zip is never held in memory), AWS Signature V4 with an unsigned payload.
function Send-WebObject([hashtable]$S, [string]$Key, [string]$Path, [string]$ContentType, [string]$CacheControl = 'public, max-age=300') {
    $req = New-WebPut (Get-WebPutRequest $S $Key ([DateTime]::UtcNow)) $ContentType
    $req.Headers.Add('Cache-Control', $CacheControl)
    $file = [IO.File]::OpenRead($Path)
    try {
        $req.ContentLength = $file.Length
        $out = $req.GetRequestStream()
        try { $file.CopyTo($out, 1MB) } finally { $out.Dispose() }
    } finally { $file.Dispose() }
    Read-WebResponse $req "Upload of $Key"
}

# A copy of the object $From under the name $Key, made by the storage itself (S3 CopyObject): nothing is uploaded again.
function Copy-WebObject([hashtable]$S, [string]$From, [string]$Key) {
    $req = New-WebPut (Get-WebPutRequest $S $Key ([DateTime]::UtcNow) $From) $null
    $req.ContentLength = 0
    Read-WebResponse $req "Copy of $From to $Key"
}

# Uploads the channel's zip (fixed name), copies it under the older names, uploads the update manifests (stable) and
# channels.json, which the website reads to show the version of each channel. The zip goes first; the files that point
# at it last.
function Publish-WebChannel([string]$Channel, [string]$Zip, [string]$Version, [string]$Commit) {
    $S = Get-WebStorage
    $files = Get-WebChannelFiles $Channel
    Send-WebObject $S $files.Zip $Zip 'application/zip'
    foreach ($copy in $files.Copies) { Copy-WebObject $S $files.Zip $copy }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('mmmover-web-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $utf8 = New-Object Text.UTF8Encoding $false
        foreach ($name in $files.Manifests) {
            $manifest = [ordered]@{ version = $Version; url = 'https://mmmover.video/'; notes = "mmMover for Windows $Version" } | ConvertTo-Json
            $mf = Join-Path $tmp $name
            [IO.File]::WriteAllText($mf, $manifest, $utf8)
            Send-WebObject $S $name $mf 'application/json'
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
            file     = $files.Zip
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
    Write-Host "website: $($files.Zip) ($Version) uploaded to $($S.PublicBase)/$($files.Zip)"
    foreach ($copy in $files.Copies) { Write-Host "         copy (made by the storage): $($S.PublicBase)/$copy" }
}

if ($Channel -and $Zip -and $Version) { Publish-WebChannel -Channel $Channel -Zip $Zip -Version $Version -Commit $Commit }
