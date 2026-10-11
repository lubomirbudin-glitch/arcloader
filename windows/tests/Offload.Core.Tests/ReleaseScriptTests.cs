using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Offload.Core.Tests;

/// Spec 4.4, 4.5: windows\scripts\release.ps1 (dot-sourced by build.ps1) — the version, the update manifest and
/// signing, run in Windows PowerShell against a stand-in signtool. No real signtool, no network, and (M7) no real
/// certificate store: the store lookup is pointed at an empty folder or replaced by a script block, and PFX files are
/// opened with an ephemeral key set, so no key container is left in the user profile.
public class ReleaseScriptTests
{
    /// windows\ of this repo (from the test's bin folder up).
    static string WindowsDir
    {
        get
        {
            var d = AppContext.BaseDirectory;
            while (d != null && !File.Exists(Path.Combine(d, "Directory.Build.props"))) d = Path.GetDirectoryName(d.TrimEnd('\\'));
            return d ?? throw new InvalidOperationException("Directory.Build.props not found");
        }
    }

    static string Script => Path.Combine(WindowsDir, "scripts", "release.ps1");

    static string WebScript => Path.Combine(WindowsDir, "scripts", "web-upload.ps1");

    /// Runs `body` after dot-sourcing release.ps1 (or `script`); a thrown error prints "ERROR: <message>" and exits 1.
    /// `env` is set for that PowerShell only. $ErrorActionPreference is 'Stop', as in build.ps1 (the point of ruling I4).
    static (int Code, string Out) Ps(string body, Dictionary<string, string?>? env = null, string? script = null)
    {
        var command = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ErrorActionPreference = 'Stop'; " +
            $". '{script ?? Script}'; try {{ {body} }} catch {{ [Console]::Out.WriteLine('ERROR: ' + $_.Exception.Message); exit 1 }}";
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) psi.ArgumentList.Add(a);
        foreach (var name in new[] { "SIGNTOOL", "OFFLOAD_SIGN_PFX", "OFFLOAD_SIGN_PFX_PASSWORD", "ARCLOADER_S3_KEY", "ARCLOADER_S3_SECRET", "ARCLOADER_S3_BUCKET" })
            psi.Environment.Remove(name);
        foreach (var (k, v) in env ?? []) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr.Result);
    }

    /// An empty folder standing in for Cert:\...\My (M7): nothing in it, nothing real is read.
    static string EmptyStore(string dir)
    {
        var path = Path.Combine(dir, "empty-store");
        Directory.CreateDirectory(path);
        return path;
    }

    /// A stand-in signtool: logs every call to calls.txt; `sign` fails with a timestamp error while ts-fail exists,
    /// `verify` fails while verify-fail exists. Like the real signtool.exe it writes its errors to STDERR (ruling I4).
    static string FakeSigntool(string dir)
    {
        var path = Path.Combine(dir, "signtool.cmd");
        File.WriteAllText(path, """
            @echo off
            >>"%~dp0calls.txt" echo %*
            if "%1"=="verify" goto verify
            if exist "%~dp0ts-fail" (
              echo SignTool Error: The specified timestamp server either could not be reached or returned an invalid response. 1>&2
              echo Number of errors: 1 1>&2
              exit /b 1
            )
            if exist "%~dp0warn" echo SignTool Warning: something harmless 1>&2
            echo Successfully signed
            exit /b 0
            :verify
            if exist "%~dp0verify-fail" (
              echo SignTool Error: No signature found. 1>&2
              exit /b 1
            )
            echo Successfully verified
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        return path;
    }

    /// A self-signed PFX (good, or expired) with `password`. `eku`: an enhanced key usage OID to add (null = none, which
    /// means "any purpose"); `withKey: false` exports the certificate without its private key.
    static string Pfx(string dir, string password, bool expired = false, string? eku = null, bool withKey = true)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=Offload test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (eku != null)
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(eku)], false));
        var from = expired ? DateTimeOffset.Now.AddYears(-2) : DateTimeOffset.Now.AddDays(-1);
        using var cert = req.CreateSelfSigned(from, expired ? DateTimeOffset.Now.AddYears(-1) : DateTimeOffset.Now.AddYears(1));
        var path = Path.Combine(dir, $"test-{Guid.NewGuid():N}.pfx");
        if (withKey) File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        else
        {
            using var bare = X509CertificateLoader.LoadCertificate(cert.RawData);
            File.WriteAllBytes(path, bare.Export(X509ContentType.Pfx, password));
        }
        return path;
    }

    const string CodeSigning = "1.3.6.1.5.5.7.3.3";

    static string Exe(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    // Windows PowerShell 5.1 reads a .ps1 without a BOM in the ANSI code page: the Czech messages need it.
    [Fact]
    public void Script_IsUtf8WithBom()
    {
        var bytes = File.ReadAllBytes(Script);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[3..6]);   // one BOM, not two
    }

    [Fact]
    public void Version_FromProps_Manifest()
    {
        using var dir = new TempDir();
        var manifest = Path.Combine(dir.Path, "mmmover-update.json");
        var r = Ps($"Write-UpdateManifest -Path '{manifest}' -Version (Get-OffloadVersion '{Path.Combine(WindowsDir, "Directory.Build.props")}') " +
                   "-Url '\\\\NAS\\Offload\\' -Notes 'Projekty, oznámení'");
        Assert.True(r.Code == 0, r.Out);
        var bytes = File.ReadAllBytes(manifest);
        Assert.NotEqual(0xEF, bytes[0]);   // no BOM
        using var doc = JsonDocument.Parse(bytes);
        var props = XDocument.Load(Path.Combine(WindowsDir, "Directory.Build.props")).Descendants("Version").First().Value;
        Assert.Equal(props, doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(@"\\NAS\Offload\", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("Projekty, oznámení", doc.RootElement.GetProperty("notes").GetString());
        var parsed = Updates.UpdateChecker.Parse(Encoding.UTF8.GetString(bytes));   // what the app reads
        Assert.Equal(props, parsed.Version);
    }

    // Review focus 5: no signtool — a clear Czech error, nothing signed.
    [Fact]
    public void NoSigntool_ClearError()
    {
        using var dir = new TempDir();
        var r = Ps("Get-OffloadSignSetup -CertThumbprint 'ABCD' | Out-Null", new() { ["SIGNTOOL"] = Path.Combine(dir.Path, "none.exe") });
        Assert.Equal(1, r.Code);
        Assert.Contains("ERROR: signtool not found — install the Windows SDK (Signing Tools for Desktop Apps), or set SIGNTOOL.", r.Out);
    }

    [Fact]
    public void Certificate_Missing_Unknown_Expired_NoPassword()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        var store = EmptyStore(dir.Path);
        var none = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool });
        Assert.Contains("ERROR: No certificate given: use -CertThumbprint <thumbprint>, or set OFFLOAD_SIGN_PFX and OFFLOAD_SIGN_PFX_PASSWORD.", none.Out);
        var thumb = new string('0', 40);
        var unknown = Ps($"Get-OffloadSignSetup -CertThumbprint '{thumb}' -StoreRoots '{store}' | Out-Null", new() { ["SIGNTOOL"] = signtool });
        Assert.Contains($@"ERROR: Certificate with thumbprint {thumb} not found in CurrentUser\My or LocalMachine\My.", unknown.Out);
        var noPassword = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1") });
        Assert.Contains("ERROR: OFFLOAD_SIGN_PFX_PASSWORD is missing.", noPassword.Out);
        var expired = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1", expired: true), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-1" });
        Assert.Contains("expired on", expired.Out);
        Assert.DoesNotContain("tajne-heslo-1", expired.Out);
        var wrong = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "spatne-heslo-9" });
        Assert.Contains("Cannot open certificate", wrong.Out);
        Assert.DoesNotContain("spatne-heslo-9", wrong.Out);
    }

    // A thumbprint is 40 hex characters - anything else (wildcards, paths) is refused before any lookup.
    [Fact]
    public void Thumbprint_MustBe40HexCharacters()
    {
        using var dir = new TempDir();
        var env = new Dictionary<string, string?> { ["SIGNTOOL"] = FakeSigntool(dir.Path) };
        foreach (var bad in new[] { "ABCD", "*", @"..\..\x", new string('G', 40), new string('0', 41) })
        {
            var r = Ps($"Get-OffloadSignSetup -CertThumbprint '{bad}' -StoreRoots '{EmptyStore(dir.Path)}' | Out-Null", env);
            Assert.Equal(1, r.Code);
            Assert.Contains("ERROR: The certificate thumbprint must have 40 hexadecimal characters", r.Out);
        }
        // spaces (as copied from the certificate dialog) are fine
        var ok = Ps($"Get-OffloadSignSetup -CertThumbprint '{string.Join(" ", Enumerable.Repeat("0000", 10))}' -StoreRoots '{EmptyStore(dir.Path)}' | Out-Null", env);
        Assert.Contains($@"ERROR: Certificate with thumbprint {new string('0', 40)} not found", ok.Out);
    }

    // The renames (0.10.0, 0.11.0): the zip, the folder in it and the exes are mmMover; the update manifest is published
    // under every name (0.10.x reads arcloader-update.json, older apps offload-update.json).
    [Fact]
    public void ReleaseNames_AreMmMover()
    {
        var r = Ps("Get-ReleaseZipName '0.11.0'; (Get-OwnExes 'C:\\b\\mmMover') -join '|'; (Get-UpdateManifestNames) -join '|'");
        Assert.True(r.Code == 0, r.Out);
        Assert.Equal(
            ["mmMover-win-x64-0.11.0.zip", @"C:\b\mmMover\mmMover.exe|C:\b\mmMover\bin\mmmover.exe|C:\b\mmMover\bin\braw-decode.exe",
             "mmmover-update.json|arcloader-update.json|offload-update.json"],
            r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    // The names users see (0.11.0): mmMover in titles, reports and the exe's file properties; mmMover.exe and the CLI
    // mmmover.exe (its root command takes the exe's name).
    [Fact]
    public void ProductNames_Exes_FileProperties_AreMmMover()
    {
        Assert.Equal("mmMover", Product.Name);
        Assert.Equal("mmmover", Product.Cli);
        Assert.Equal("mmmover", typeof(Offload.Cli.CopyCommand).Assembly.GetName().Name);
        Assert.Contains("<AssemblyName>mmMover</AssemblyName>", File.ReadAllText(Path.Combine(WindowsDir, "src", "Offload.App", "Offload.App.csproj")));
        Assert.Contains("<AssemblyName>mmmover</AssemblyName>", File.ReadAllText(Path.Combine(WindowsDir, "src", "Offload.Cli", "Offload.Cli.csproj")));
        Assert.Equal("mmMover", XDocument.Load(Path.Combine(WindowsDir, "Directory.Build.props")).Descendants("Product").Single().Value);
    }

    // publish.ps1's channel assets: the zip under the new name first (the notes link to it), then the same zip under the
    // older names (links already out there); stable also every manifest, dev none.
    [Fact]
    public void ChannelAssets_NewNameFirst_OldNamesKept()
    {
        var r = Ps("foreach ($c in 'stable', 'dev') { $a = Get-ChannelAssetNames $c; \"$c zips=$($a.Zips -join ',') manifests=$($a.Manifests -join ',')\" }");
        Assert.True(r.Code == 0, r.Out);
        Assert.Equal(
            ["stable zips=mmMover-Windows.zip,ArcLoader-Windows.zip,Offload-Windows.zip manifests=mmmover-update.json,arcloader-update.json,offload-update.json",
             "dev zips=mmMover-Windows-dev.zip,ArcLoader-Windows-dev.zip,Offload-Windows-dev.zip manifests="],
            r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Contains("ERROR", Ps("Get-ChannelAssetNames 'beta'").Out);
    }

    // build.ps1 and publish.ps1 use those names (they cannot run here: dotnet publish, gh, the network) — checked in
    // their text: the new repo, the new zip and manual, no old exe or zip name left except the kept copies.
    [Fact]
    public void BuildAndPublish_UseTheNewNames()
    {
        var build = File.ReadAllText(Path.Combine(WindowsDir, "scripts", "build.ps1"));
        Assert.Contains(@"Copy-Item (Join-Path $root 'package\READ-ME.txt') $out", build);
        Assert.Contains(@"Copy-Item (Join-Path $root 'package\mmMover-manual.pdf') $out", build);
        Assert.Contains(@"'build\staging\mmMover' } else { 'build\mmMover' }", build);
        Assert.Contains("-Files (Get-OwnExes $out)", build);
        Assert.Contains(@"('build\' + (Get-ReleaseZipName $version))", build);
        Assert.Contains("foreach ($name in Get-UpdateManifestNames)", build);
        foreach (var old in new[] { "Offload.exe", "ArcLoader.exe", "Offload-win-x64", "ArcLoader-win-x64", "Offload-manual", "ArcLoader-manual", @"'build\ArcLoader'", @"staging\ArcLoader" })
            Assert.DoesNotContain(old, build, StringComparison.OrdinalIgnoreCase);

        var publish = File.ReadAllText(Path.Combine(WindowsDir, "scripts", "publish.ps1"));
        Assert.Contains("[string]$Repo = 'lubomirbudin-glitch/mmmover'", publish);
        Assert.Contains(@"('build\' + (Get-ReleaseZipName $version))", publish);
        Assert.Contains("$channelAssets = Get-ChannelAssetNames $Channel", publish);
        Assert.Contains("foreach ($name in $channelAssets.Zips)", publish);
        Assert.Contains("foreach ($name in $channelAssets.Manifests)", publish);
        Assert.Contains("\"mmMover for Windows $version\"", publish);
        Assert.Contains("\"mmMover for Windows — stable version ($version)\"", publish);
        Assert.Contains("\"mmMover for Windows — development version ($version)\"", publish);
        Assert.Contains(@"``mmMover\mmMover.exe``", publish);
        Assert.Contains("``mmMover-manual.pdf``", publish);
        foreach (var old in new[] { "Offload for Windows", "ArcLoader for Windows", "glitch/offload", "glitch/arcloader", "Offload.exe", "ArcLoader.exe",
                     "Offload-win-x64", "ArcLoader-win-x64", "Offload-manual", "ArcLoader-manual" })
            Assert.DoesNotContain(old, publish);
        // The website upload runs as a script of its own: dot-sourced, its param block would empty $Channel, $zip, $version.
        Assert.Contains("& (Join-Path $PSScriptRoot 'web-upload.ps1') -Channel $Channel -Zip $zip -Version $version -Commit $short", publish);
        Assert.DoesNotContain(". (Join-Path $PSScriptRoot 'web-upload.ps1')", publish);
    }

    // Why publish.ps1 runs web-upload.ps1 with & (found 2026-10-11): dot-sourcing a script with a param block sets those
    // parameters — unset, so empty — in the caller's scope; run with &, the caller's variables stay.
    [Fact]
    public void WebUpload_RunWithAmpersand_LeavesTheCallersVariables()
    {
        var dotSourced = Ps($"$Channel = 'dev'; $zip = 'x.zip'; . '{WebScript}'; \"[$Channel][$zip]\"");
        Assert.True(dotSourced.Code == 0, dotSourced.Out);
        Assert.Equal("[][]", dotSourced.Out.Trim());
        var run = Ps($"$Channel = 'dev'; $zip = 'x.zip'; & '{WebScript}'; \"[$Channel][$zip]\"");
        Assert.True(run.Code == 0, run.Out);
        Assert.Equal("[dev][x.zip]", run.Out.Trim());
    }

    // The manual names the versioned zip.
    [Fact]
    public void Manual_NamesTheVersionedZip()
    {
        var html = File.ReadAllText(Path.Combine(WindowsDir, "package", "manual", "manual.html"));
        Assert.DoesNotContain("mmMover-win-x64.zip", html);
        Assert.Contains("mmMover-win-x64-&lt;version&gt;.zip", html);
    }

    // web-upload.ps1 (0.11.0): the website's zip is mmMover-Windows(-dev).zip; ArcLoader-Windows(-dev).zip is a copy the
    // storage makes (the live site's buttons point there until they change); stable carries every update manifest — the
    // same names as the GitHub release (Get-UpdateManifestNames).
    [Fact]
    public void WebChannelFiles_NewZip_ArcLoaderCopy_EveryManifest()
    {
        var r = Ps("foreach ($c in 'stable', 'dev') { $f = Get-WebChannelFiles $c; \"$c zip=$($f.Zip) copies=$($f.Copies -join ',') manifests=$($f.Manifests -join ',') n=$(@($f.Manifests).Count)\" }",
            script: WebScript);
        Assert.True(r.Code == 0, r.Out);
        Assert.Equal(
            ["stable zip=mmMover-Windows.zip copies=ArcLoader-Windows.zip manifests=mmmover-update.json,arcloader-update.json,offload-update.json n=3",
             "dev zip=mmMover-Windows-dev.zip copies=ArcLoader-Windows-dev.zip manifests= n=0"],
            r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Contains("ERROR", Ps("Get-WebChannelFiles 'beta'", script: WebScript).Out);
        var release = Ps("(Get-UpdateManifestNames) -join ','");
        Assert.Equal(release.Out.Trim(), r.Out.Split('\n')[0].Split("manifests=")[1].Split(' ')[0]);
    }

    // The signing is AWS Signature V4 as AWS documents it: the "GET Object" example of the S3 documentation
    // (Signature Calculations for the Authorization Header, single chunk) — canonical request, string to sign, signature.
    [Fact]
    public void SigV4_AwsDocumentedExample()
    {
        var r = Ps("$h = [ordered]@{ Host = 'examplebucket.s3.amazonaws.com'; Range = 'bytes=0-9'; " +
                   "'x-amz-content-sha256' = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'; 'x-amz-date' = '20130524T000000Z' }; " +
                   "$a = Get-SigV4 -Method GET -UriPath '/test.txt' -Headers $h -PayloadHash 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855' " +
                   "-AccessKey 'AKIAIOSFODNN7EXAMPLE' -Secret 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY' -Region 'us-east-1' " +
                   "-Now ([DateTime]::new(2013, 5, 24, 0, 0, 0, [DateTimeKind]::Utc)); " +
                   "$a.Canonical.Replace(\"`n\", '|'); $a.StringToSign.Replace(\"`n\", '|'); $a.Authorization",
            script: WebScript);
        Assert.True(r.Code == 0, r.Out);
        Assert.Equal(
            ["GET|/test.txt||host:examplebucket.s3.amazonaws.com|range:bytes=0-9|x-amz-content-sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855|x-amz-date:20130524T000000Z||host;range;x-amz-content-sha256;x-amz-date|e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
             "AWS4-HMAC-SHA256|20130524T000000Z|20130524/us-east-1/s3/aws4_request|7344ae5b7ee6c3e7e6b0fe0640412a37625d1fbfff95c48bbb2dc43964946972",
             "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41"],
            r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// The signature of a canonical request, computed here independently of the script (HMAC chain of SigV4).
    static string Sign(string canonical, string secret, string date, string amzDate, string region)
    {
        static byte[] H(byte[] key, string text) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));
        var scope = $"{date}/{region}/s3/aws4_request";
        var toSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        var k = H(H(H(H(Encoding.UTF8.GetBytes("AWS4" + secret), date), region), "s3"), "aws4_request");
        return Convert.ToHexStringLower(H(k, toSign));
    }

    const string FakeStorage = "$S = @{ Key = 'AKIDTEST'; Secret = 'not-a-real-secret'; Bucket = 'arcloader-dl'; Host = 'fly.storage.tigris.dev'; Region = 'auto' }; " +
        "$now = [DateTime]::new(2026, 10, 11, 12, 0, 0, [DateTimeKind]::Utc); ";

    // The server-side copy (S3 CopyObject): a PUT of the new key with x-amz-copy-source: /<bucket>/<source key>, signed
    // like an upload — the copy header among the signed ones. Nothing is sent (no network in the tests).
    [Fact]
    public void CopyObject_CanonicalRequest_SignedCopySource()
    {
        var r = Ps(FakeStorage + "$p = Get-WebPutRequest $S 'ArcLoader-Windows.zip' $now 'mmMover-Windows.zip'; " +
                   "$p.Url; $p.Auth.Canonical.Replace(\"`n\", '|'); $p.Auth.Signature; " +
                   "($p.Headers.Keys | ForEach-Object { \"$_=$($p.Headers[$_])\" }) -join ' ## '",
            script: WebScript);
        Assert.True(r.Code == 0, r.Out);
        var lines = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal("https://fly.storage.tigris.dev/arcloader-dl/ArcLoader-Windows.zip", lines[0]);
        var canonical = "PUT\n/arcloader-dl/ArcLoader-Windows.zip\n\n" +
            "host:fly.storage.tigris.dev\nx-amz-content-sha256:UNSIGNED-PAYLOAD\nx-amz-copy-source:/arcloader-dl/mmMover-Windows.zip\nx-amz-date:20261011T120000Z\n\n" +
            "host;x-amz-content-sha256;x-amz-copy-source;x-amz-date\nUNSIGNED-PAYLOAD";
        Assert.Equal(canonical.Replace('\n', '|'), lines[1]);
        var signature = Sign(canonical, "not-a-real-secret", "20261011", "20261011T120000Z", "auto");
        Assert.Equal(signature, lines[2]);
        Assert.Equal(
            "x-amz-content-sha256=UNSIGNED-PAYLOAD ## x-amz-date=20261011T120000Z ## x-amz-copy-source=/arcloader-dl/mmMover-Windows.zip ## " +
            "Authorization=AWS4-HMAC-SHA256 Credential=AKIDTEST/20261011/auto/s3/aws4_request, " +
            $"SignedHeaders=host;x-amz-content-sha256;x-amz-copy-source;x-amz-date, Signature={signature}",
            lines[3]);
    }

    // An upload is signed as before the copy existed (no copy header); a key's parts are URL-encoded in the path and in
    // the copy source.
    [Fact]
    public void Upload_CanonicalRequest_NoCopySource_KeysEncoded()
    {
        var r = Ps(FakeStorage + "$p = Get-WebPutRequest $S 'mmMover-Windows-dev.zip' $now; $p.Auth.Canonical.Replace(\"`n\", '|'); $p.Auth.Signature; " +
                   "($p.Headers.Keys) -join ','; Get-WebObjectPath $S 'a b/c+d.zip'",
            script: WebScript);
        Assert.True(r.Code == 0, r.Out);
        var lines = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var canonical = "PUT\n/arcloader-dl/mmMover-Windows-dev.zip\n\nhost:fly.storage.tigris.dev\nx-amz-content-sha256:UNSIGNED-PAYLOAD\nx-amz-date:20261011T120000Z\n\n" +
            "host;x-amz-content-sha256;x-amz-date\nUNSIGNED-PAYLOAD";
        Assert.Equal(canonical.Replace('\n', '|'), lines[0]);
        Assert.Equal(Sign(canonical, "not-a-real-secret", "20261011", "20261011T120000Z", "auto"), lines[1]);
        Assert.Equal("x-amz-content-sha256,x-amz-date,Authorization", lines[2]);
        Assert.Equal("/arcloader-dl/a%20b/c%2Bd.zip", lines[3]);
    }

    // Publish-WebChannel with the storage replaced (nothing leaves the PC; channels.json is "read" from a closed local
    // port): the zip first, then the storage's copy under the ArcLoader name, the manifests, channels.json last — whose
    // `file` is the new name.
    [Fact]
    public void PublishWebChannel_Order_CopyNotUpload_ChannelsJsonNamesTheNewZip()
    {
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "mmMover-win-x64-0.11.0.zip");
        File.WriteAllBytes(zip, new byte[1234]);
        var log = Path.Combine(dir.Path, "log.txt");
        var body =
            "function Get-WebStorage { @{ Key = 'k'; Secret = 's'; Bucket = 'b'; Host = 'h'; Region = 'auto'; PublicBase = 'http://127.0.0.1:9' } }; " +
            $"function Send-WebObject($S, $Key, $Path, $ContentType, $CacheControl) {{ Add-Content -LiteralPath '{log}' \"send $Key $ContentType\"; " +
            $"  if ($Key -eq 'channels.json' -or $Key -like '*.json') {{ Add-Content -LiteralPath '{log}' ('  ' + ((Get-Content -Raw $Path) -replace '\\s+', ' ')) }} }}; " +
            $"function Copy-WebObject($S, $From, $Key) {{ Add-Content -LiteralPath '{log}' \"copy $From $Key\" }}; " +
            $"Publish-WebChannel -Channel stable -Zip '{zip}' -Version '0.11.0' -Commit 'abc1234' | Out-Null; " +
            $"Publish-WebChannel -Channel dev -Zip '{zip}' -Version '0.11.0' -Commit 'abc1234' | Out-Null";
        var r = Ps(body, script: WebScript);
        Assert.True(r.Code == 0, r.Out);
        var lines = File.ReadAllLines(log);
        var steps = lines.Where(l => !l.StartsWith("  ")).ToArray();
        Assert.Equal(
            ["send mmMover-Windows.zip application/zip", "copy mmMover-Windows.zip ArcLoader-Windows.zip",
             "send mmmover-update.json application/json", "send arcloader-update.json application/json", "send offload-update.json application/json",
             "send channels.json application/json",
             "send mmMover-Windows-dev.zip application/zip", "copy mmMover-Windows-dev.zip ArcLoader-Windows-dev.zip", "send channels.json application/json"],
            steps);
        var contents = lines.Where(l => l.StartsWith("  ")).Select(l => l.Trim()).ToArray();
        Assert.Equal(5, contents.Length);
        Assert.All(contents[..3], m => Assert.Equal("{ \"version\": \"0.11.0\", \"url\": \"/download/\", \"notes\": \"mmMover for Windows 0.11.0\" }", m));
        Assert.Contains("\"file\": \"mmMover-Windows.zip\"", contents[3]);
        Assert.Contains("\"file\": \"mmMover-Windows-dev.zip\"", contents[4]);
        Assert.Contains("\"size\": 1234", contents[4]);
        Assert.DoesNotContain("ArcLoader", contents[3] + contents[4]);
    }

    // The storage's infrastructure names stay (rules 0.11.0): the bucket arcloader-dl and ARCLOADER_S3_KEY/SECRET/BUCKET.
    [Fact]
    public void WebUpload_KeepsTheStorageNames()
    {
        var web = File.ReadAllText(WebScript);
        Assert.Contains("'ARCLOADER_S3_KEY'", web);
        Assert.Contains("'ARCLOADER_S3_SECRET'", web);
        Assert.Contains("'ARCLOADER_S3_BUCKET'", web);
        Assert.Contains("'arcloader-dl'", web);
        Assert.Contains("file     = $files.Zip", web);
        var bytes = File.ReadAllBytes(WebScript);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);   // Windows PowerShell 5.1: the dashes in the comments
    }

    // M8: a certificate that could not sign (no private key, not for code signing) is refused before the build.
    [Fact]
    public void Certificate_NoPrivateKey_WrongPurpose_Refused()
    {
        using var dir = new TempDir();
        var env = (string pfx) => new Dictionary<string, string?> { ["SIGNTOOL"] = FakeSigntool(dir.Path), ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h5" };
        var noKey = Ps("Get-OffloadSignSetup | Out-Null", env(Pfx(dir.Path, "h5", withKey: false)));
        Assert.Contains("ERROR: Certificate", noKey.Out);
        Assert.Contains("has no private key", noKey.Out);
        var wrongPurpose = Ps("Get-OffloadSignSetup | Out-Null", env(Pfx(dir.Path, "h5", eku: "1.3.6.1.5.5.7.3.1")));   // server authentication
        Assert.Contains("is not meant for code signing", wrongPurpose.Out);
        var good = Ps("Get-OffloadSignSetup | Out-Null", env(Pfx(dir.Path, "h5", eku: CodeSigning)));
        Assert.True(good.Code == 0, good.Out);
        var noEku = Ps("Get-OffloadSignSetup | Out-Null", env(Pfx(dir.Path, "h5")));   // no EKU = any purpose
        Assert.True(noEku.Code == 0, noEku.Out);
    }

    // M7/M8: the store lookup is replaceable; a certificate "found in the store" is checked like a PFX, and when both a
    // thumbprint and a PFX are given the script says which one it uses.
    [Fact]
    public void Thumbprint_FromLookup_WinsOverPfx_AndSaysSo()
    {
        using var dir = new TempDir();
        var pfx = Pfx(dir.Path, "h6", eku: CodeSigning);
        var thumb = new string('A', 40);
        var lookup = "{ param($t) New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:OFFLOAD_SIGN_PFX, 'h6', [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet) }";
        var r = Ps($"$s = Get-OffloadSignSetup -CertThumbprint '{thumb}' -CertLookup {lookup}; $s.CertArgs -join ' '",
            new() { ["SIGNTOOL"] = FakeSigntool(dir.Path), ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h6" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains($"/sha1 {thumb}", r.Out);
        Assert.Contains("OFFLOAD_SIGN_PFX is not used", r.Out);
    }

    // Spec 4.5: sign (SHA-256, RFC 3161 timestamp, description) then verify; the password is never printed.
    [Fact]
    public void Sign_ThenVerify_PasswordNeverPrinted()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        var pfx = Pfx(dir.Path, "tajne-heslo-2");
        var a = Exe(dir.Path, "mmMover.exe");
        var b = Exe(dir.Path, "mmmover.exe.cli");
        File.WriteAllText(Path.Combine(dir.Path, "warn"), "");   // a harmless stderr line on success must not abort (I4)
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}', '{b}') -TimestampUrl 'http://ts.example' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-2" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains($"signed: {a}", r.Out);
        Assert.DoesNotContain("tajne-heslo-2", r.Out);
        var calls = File.ReadAllLines(Path.Combine(dir.Path, "calls.txt"));
        Assert.Equal(2, calls.Length);
        Assert.Equal($"sign /fd SHA256 /td SHA256 /tr http://ts.example /f {pfx} /p tajne-heslo-2 /d mmMover {a} {b}", calls[0].Trim());
        Assert.Equal($"verify /pa /all {a} {b}", calls[1].Trim());
    }

    // Review focus 5 + I4: the timestamp server does not answer (signtool says so on stderr, as the real one does) —
    // 3 attempts, then a clear error, even with $ErrorActionPreference = 'Stop'.
    [Fact]
    public void Timestamp_ThreeAttempts_ThenClearError()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "ts-fail"), "");
        var a = Exe(dir.Path, "mmMover.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -TimestampUrl 'http://ts.invalid' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "heslo-3"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "heslo-3" });
        Assert.Equal(1, r.Code);
        Assert.Contains("ERROR: Could not get a timestamp from http://ts.invalid.", r.Out);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")).Length);
        Assert.DoesNotContain("heslo-3", r.Out);
    }

    // I4: a timestamp server that answers on the second try — signed, with one warning.
    [Fact]
    public void Timestamp_SecondAttemptWorks()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "ts-fail"), "");
        var a = Exe(dir.Path, "mmMover.exe");
        // the first call fails; the script block removes the marker before the retry (the delay is a function of the script)
        var r = Ps($"$s = Get-OffloadSignSetup; function Start-Sleep {{ param($Seconds) Remove-Item '{Path.Combine(dir.Path, "ts-fail")}' -ErrorAction SilentlyContinue }}; " +
                   $"Invoke-OffloadSign -Setup $s -Files @('{a}') -TimestampUrl 'http://ts.flaky' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "h7"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h7" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains("signed:", r.Out);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")).Length);   // sign (failed), sign, verify
    }

    [Fact]
    public void VerifyFails_ClearError_MissingFileRefusedFirst()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "verify-fail"), "");
        var env = new Dictionary<string, string?> { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "h4"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h4" };
        var a = Exe(dir.Path, "mmMover.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: Cannot verify the signature: SignTool Error: No signature found.", r.Out);
        var missing = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{Path.Combine(dir.Path, "gone.exe")}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: File to sign not found:", missing.Out);
    }
}
