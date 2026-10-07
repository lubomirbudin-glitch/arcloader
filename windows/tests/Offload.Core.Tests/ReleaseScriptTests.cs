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
        var manifest = Path.Combine(dir.Path, "offload-update.json");
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
        Assert.Contains("ERROR: signtool nenalezen — nainstalujte Windows SDK (Signing Tools for Desktop Apps), nebo nastavte SIGNTOOL.", r.Out);
    }

    [Fact]
    public void Certificate_Missing_Unknown_Expired_NoPassword()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        var store = EmptyStore(dir.Path);
        var none = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool });
        Assert.Contains("ERROR: Není zadaný certifikát: použijte -CertThumbprint <otisk>, nebo nastavte OFFLOAD_SIGN_PFX a OFFLOAD_SIGN_PFX_PASSWORD.", none.Out);
        var thumb = new string('0', 40);
        var unknown = Ps($"Get-OffloadSignSetup -CertThumbprint '{thumb}' -StoreRoots '{store}' | Out-Null", new() { ["SIGNTOOL"] = signtool });
        Assert.Contains($@"ERROR: Certifikát s otiskem {thumb} nenalezen v CurrentUser\My ani LocalMachine\My.", unknown.Out);
        var noPassword = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1") });
        Assert.Contains("ERROR: Chybí OFFLOAD_SIGN_PFX_PASSWORD.", noPassword.Out);
        var expired = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1", expired: true), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-1" });
        Assert.Contains("vypršel", expired.Out);
        Assert.DoesNotContain("tajne-heslo-1", expired.Out);
        var wrong = Ps("Get-OffloadSignSetup | Out-Null", new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "tajne-heslo-1"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "spatne-heslo-9" });
        Assert.Contains("nejde otevřít", wrong.Out);
        Assert.DoesNotContain("spatne-heslo-9", wrong.Out);
    }

    // M8: a certificate that could not sign (no private key, not for code signing) is refused before the build.
    [Fact]
    public void Certificate_NoPrivateKey_WrongPurpose_Refused()
    {
        using var dir = new TempDir();
        var env = (string pfx) => new Dictionary<string, string?> { ["SIGNTOOL"] = FakeSigntool(dir.Path), ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h5" };
        var noKey = Ps("Get-OffloadSignSetup | Out-Null", env(Pfx(dir.Path, "h5", withKey: false)));
        Assert.Contains("ERROR: Certifikát", noKey.Out);
        Assert.Contains("nemá soukromý klíč", noKey.Out);
        var wrongPurpose = Ps("Get-OffloadSignSetup | Out-Null", env(Pfx(dir.Path, "h5", eku: "1.3.6.1.5.5.7.3.1")));   // server authentication
        Assert.Contains("není určen k podepisování kódu", wrongPurpose.Out);
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
        Assert.Contains("OFFLOAD_SIGN_PFX se nepoužije", r.Out);
    }

    // Spec 4.5: sign (SHA-256, RFC 3161 timestamp, description) then verify; the password is never printed.
    [Fact]
    public void Sign_ThenVerify_PasswordNeverPrinted()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        var pfx = Pfx(dir.Path, "tajne-heslo-2");
        var a = Exe(dir.Path, "Offload.exe");
        var b = Exe(dir.Path, "offload.exe.cli");
        File.WriteAllText(Path.Combine(dir.Path, "warn"), "");   // a harmless stderr line on success must not abort (I4)
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}', '{b}') -TimestampUrl 'http://ts.example' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = pfx, ["OFFLOAD_SIGN_PFX_PASSWORD"] = "tajne-heslo-2" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains($"podepsáno: {a}", r.Out);
        Assert.DoesNotContain("tajne-heslo-2", r.Out);
        var calls = File.ReadAllLines(Path.Combine(dir.Path, "calls.txt"));
        Assert.Equal(2, calls.Length);
        Assert.Equal($"sign /fd SHA256 /td SHA256 /tr http://ts.example /f {pfx} /p tajne-heslo-2 /d Offload {a} {b}", calls[0].Trim());
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
        var a = Exe(dir.Path, "Offload.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -TimestampUrl 'http://ts.invalid' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "heslo-3"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "heslo-3" });
        Assert.Equal(1, r.Code);
        Assert.Contains("ERROR: Časové razítko se nepodařilo získat z http://ts.invalid.", r.Out);
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
        var a = Exe(dir.Path, "Offload.exe");
        // the first call fails; the script block removes the marker before the retry (the delay is a function of the script)
        var r = Ps($"$s = Get-OffloadSignSetup; function Start-Sleep {{ param($Seconds) Remove-Item '{Path.Combine(dir.Path, "ts-fail")}' -ErrorAction SilentlyContinue }}; " +
                   $"Invoke-OffloadSign -Setup $s -Files @('{a}') -TimestampUrl 'http://ts.flaky' -RetryDelaySeconds 0",
            new() { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "h7"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h7" });
        Assert.True(r.Code == 0, r.Out);
        Assert.Contains("podepsáno:", r.Out);
        Assert.Equal(3, File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")).Length);   // sign (failed), sign, verify
    }

    [Fact]
    public void VerifyFails_ClearError_MissingFileRefusedFirst()
    {
        using var dir = new TempDir();
        var signtool = FakeSigntool(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "verify-fail"), "");
        var env = new Dictionary<string, string?> { ["SIGNTOOL"] = signtool, ["OFFLOAD_SIGN_PFX"] = Pfx(dir.Path, "h4"), ["OFFLOAD_SIGN_PFX_PASSWORD"] = "h4" };
        var a = Exe(dir.Path, "Offload.exe");
        var r = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{a}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: Podpis nejde ověřit: SignTool Error: No signature found.", r.Out);
        var missing = Ps($"$s = Get-OffloadSignSetup; Invoke-OffloadSign -Setup $s -Files @('{Path.Combine(dir.Path, "gone.exe")}') -RetryDelaySeconds 0", env);
        Assert.Contains("ERROR: Soubor k podpisu nenalezen:", missing.Out);
    }
}
