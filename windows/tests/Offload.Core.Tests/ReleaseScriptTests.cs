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

    /// Runs `body` after dot-sourcing release.ps1; a thrown error prints "ERROR: <message>" and exits 1. `env` is set
    /// for that PowerShell only. $ErrorActionPreference is 'Stop', as in build.ps1 (the point of ruling I4).
    static (int Code, string Out) Ps(string body, Dictionary<string, string?>? env = null)
    {
        var command = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ErrorActionPreference = 'Stop'; " +
            $". '{Script}'; try {{ {body} }} catch {{ [Console]::Out.WriteLine('ERROR: ' + $_.Exception.Message); exit 1 }}";
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
        foreach (var name in new[] { "SIGNTOOL", "OFFLOAD_SIGN_PFX", "OFFLOAD_SIGN_PFX_PASSWORD" }) psi.Environment.Remove(name);
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
        var manifest = Path.Combine(dir.Path, "arcloader-update.json");
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

    // The rename (0.10.0): the zip, the folder in it and the exes are ArcLoader; the update manifest is published under
    // both names (old apps read offload-update.json).
    [Fact]
    public void ReleaseNames_AreArcLoader()
    {
        var r = Ps("Get-ReleaseZipName '0.10.0'; (Get-OwnExes 'C:\\b\\ArcLoader') -join '|'; (Get-UpdateManifestNames) -join '|'");
        Assert.True(r.Code == 0, r.Out);
        Assert.Equal(
            ["ArcLoader-win-x64-0.10.0.zip", @"C:\b\ArcLoader\ArcLoader.exe|C:\b\ArcLoader\bin\arcloader.exe|C:\b\ArcLoader\bin\braw-decode.exe",
             "arcloader-update.json|offload-update.json"],
            r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    // publish.ps1's channel assets: the zip under the new name first (the notes link to it), then the same zip under the
    // old name (links on the user's web); stable also both manifests, dev none.
    [Fact]
    public void ChannelAssets_NewNameFirst_OldNamesKept()
    {
        var r = Ps("foreach ($c in 'stable', 'dev') { $a = Get-ChannelAssetNames $c; \"$c zips=$($a.Zips -join ',') manifests=$($a.Manifests -join ',')\" }");
        Assert.True(r.Code == 0, r.Out);
        Assert.Equal(
            ["stable zips=ArcLoader-Windows.zip,Offload-Windows.zip manifests=arcloader-update.json,offload-update.json",
             "dev zips=ArcLoader-Windows-dev.zip,Offload-Windows-dev.zip manifests="],
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
        Assert.Contains(@"Copy-Item (Join-Path $root 'package\ArcLoader-manual.pdf') $out", build);
        Assert.Contains(@"'build\staging\ArcLoader' } else { 'build\ArcLoader' }", build);
        Assert.Contains("-Files (Get-OwnExes $out)", build);
        Assert.Contains(@"('build\' + (Get-ReleaseZipName $version))", build);
        Assert.Contains("foreach ($name in Get-UpdateManifestNames)", build);
        Assert.DoesNotContain("Offload.exe", build, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Offload-win-x64", build);
        Assert.DoesNotContain("Offload-manual", build);

        var publish = File.ReadAllText(Path.Combine(WindowsDir, "scripts", "publish.ps1"));
        Assert.Contains("[string]$Repo = 'lubomirbudin-glitch/arcloader'", publish);
        Assert.Contains(@"('build\' + (Get-ReleaseZipName $version))", publish);
        Assert.Contains("$channelAssets = Get-ChannelAssetNames $Channel", publish);
        Assert.Contains("foreach ($name in $channelAssets.Zips)", publish);
        Assert.Contains("foreach ($name in $channelAssets.Manifests)", publish);
        Assert.Contains("\"ArcLoader for Windows $version\"", publish);
        Assert.Contains("\"ArcLoader for Windows — stable version ($version)\"", publish);
        Assert.Contains("\"ArcLoader for Windows — development version ($version)\"", publish);
        Assert.DoesNotContain("Offload for Windows", publish);
        Assert.DoesNotContain("glitch/offload", publish);
        Assert.DoesNotContain("Offload.exe", publish);
        Assert.DoesNotContain("Offload-win-x64", publish);
    }

    // The manual names the versioned zip.
    [Fact]
    public void Manual_NamesTheVersionedZip()
    {
        var html = File.ReadAllText(Path.Combine(WindowsDir, "package", "manual", "manual.html"));
        Assert.DoesNotContain("ArcLoader-win-x64.zip", html);
        Assert.Contains("ArcLoader-win-x64-&lt;version&gt;.zip", html);
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
        var a = Exe(dir.Path, "ArcLoader.exe");
        var b = Exe(dir.Path, "arcloader.exe.cli");
        File.WriteAllText(Path.Combine(dir.Path, "warn"), "");   // a harmless stderr line on success must not abort (I4)
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}', '{b}') -TimestampUrl 'http://ts.example' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-2" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains($"signed: {a}", r.Out);
        Assert.DoesNotContain("tajne-heslo-2", r.Out);
        var calls = File.ReadAllLines(Path.Combine(dir.Path, "calls.txt"));
        Assert.Equal(2, calls.Length);
        Assert.Equal($"sign /fd SHA256 /td SHA256 /tr http://ts.example /f {pfx} /p tajne-heslo-2 /d ArcLoader {a} {b}", calls[0].Trim());
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
        var a = Exe(dir.Path, "ArcLoader.exe");
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
        var a = Exe(dir.Path, "ArcLoader.exe");
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
        var a = Exe(dir.Path, "ArcLoader.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: Cannot verify the signature: SignTool Error: No signature found.", r.Out);
        var missing = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{Path.Combine(dir.Path, "gone.exe")}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: File to sign not found:", missing.Out);
    }
}
